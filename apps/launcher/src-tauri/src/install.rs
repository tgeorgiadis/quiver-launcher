//! Downloads a release file, checks it against the site's checksum, and
//! installs it into the app's folder.
use futures_util::StreamExt;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::io::{self, Read};
use std::path::{Path, PathBuf};
use tokio::io::AsyncWriteExt;

/// What an install fails with when the download isn't the file its checksum names: the
/// page says so, and tells the site (`download-problem`).
pub const MISMATCH: &str = "mismatch";
/// What an install fails with when its file is gone from where it was published (a 404).
pub const MISSING: &str = "missing";
/// What an install stopped because the player cancelled it fails with.
pub const CANCELLED: &str = "cancelled";
/// Written into an installed app's folder; holds the installed version.
pub const VERSION_FILE: &str = ".quiver-version";

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallRequest {
    pub id: String,
    pub url: String,
    pub filename: String,
    /// `sha256:<hex>`, which the download must match. Absent only for a file
    /// nobody pinned: one the player agreed to install unchecked, or one from
    /// a GitLab release or a repository Quiver doesn't check.
    pub checksum: Option<String>,
    pub folder: String,
    /// Install into this folder instead (an install adopted from 3.x).
    pub dir: Option<String>,
    pub files_to_add: Vec<String>,
    pub version: String,
    /// The catalog's preferred programs, for finding the one a Windows installer put on the computer.
    #[serde(default)]
    pub preferred: Vec<String>,
    /// The program the app started with before an update.
    pub program: Option<String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Installed {
    pub dir: String,
    pub version: String,
    /// The downloaded file's `sha256:<hex>`, which tells it from the file its release is verified with later.
    pub checksum: String,
    /// "msi" for a Windows installer, whose files are where its setup put them.
    pub installer: Option<&'static str>,
    /// The program a Windows installer installed, when Quiver could tell.
    pub program: Option<String>,
    /// Where it installed, for choosing its program when Quiver couldn't tell.
    pub location: Option<String>,
    /// Windows wants a restart to finish installing.
    pub restart: bool,
}

#[derive(Serialize, Clone)]
pub struct Progress {
    pub id: String,
    pub phase: &'static str,
    pub received: u64,
    pub total: Option<u64>,
    /// Waiting on the app's own setup window (a Windows installer).
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    pub setup: bool,
}

/// A single file or folder name, never a path.
pub fn plain_name(name: &str) -> Result<&str, String> {
    let name = name.trim();
    let bad = name.is_empty()
        || name == "."
        || name == ".."
        || name.contains(['/', '\\', ':'])
        || name.ends_with('.')
        || name.chars().any(|c| c.is_control() || "<>\"|?*".contains(c))
        || is_device_name(name);
    if bad {
        Err(format!("\"{name}\" isn't a valid file or folder name."))
    } else {
        Ok(name)
    }
}

/// Names Windows reserves for devices, with or without an extension ("NUL.txt").
fn is_device_name(name: &str) -> bool {
    let stem = name.split('.').next().unwrap_or("").trim_end().to_ascii_uppercase();
    matches!(stem.as_str(), "CON" | "PRN" | "AUX" | "NUL")
        || (stem.len() == 4
            && (stem.starts_with("COM") || stem.starts_with("LPT"))
            && stem.as_bytes()[3].is_ascii_digit())
}

/// Installs a release. Until its files start going in, `cancel` turning true
/// stops it with `CANCELLED`, leaving whatever was installed before as it was.
pub async fn install(
    apps_dir: &Path,
    req: InstallRequest,
    mut cancel: tokio::sync::watch::Receiver<bool>,
    progress: impl Fn(Progress),
) -> Result<Installed, String> {
    let folder = plain_name(&req.folder)?.to_string();
    let filename = plain_name(&req.filename)?.to_string();
    // Never into a folder of files Quiver didn't put there, such as one the player fills themselves.
    if req.dir.is_none() && !may_install_into(&apps_dir.join(&folder)) {
        return Err(format!(
            "The folder {folder} in your apps folder has files Quiver didn't install, so it won't install over them. Rename or move that folder, then try again."
        ));
    }
    // Ids can be "github:owner/repo"; the staging folder only needs to be unique.
    let stage: String = req.id.chars().map(|c| if c.is_ascii_alphanumeric() { c } else { '_' }).collect();
    let staging = apps_dir.join(".staging").join(stage);
    let _ = fs::remove_dir_all(&staging);
    fs::create_dir_all(&staging).map_err(text)?;
    let result = async {
        let mut file = staging.join(&filename);
        let hash = {
            let stop = std::pin::pin!(cancel.wait_for(|c| *c));
            match futures_util::future::select(std::pin::pin!(download(&req, &file, &progress)), stop).await {
                futures_util::future::Either::Left((hash, _)) => hash?,
                futures_util::future::Either::Right(_) => return Err(CANCELLED.to_string()),
            }
        };
        if *cancel.borrow() {
            return Err(CANCELLED.to_string());
        }
        if !matches_checksum(req.checksum.as_deref(), &hash) {
            return Err(MISMATCH.to_string());
        }
        // A host can name a file without its extension ("Game-v1.2-Windows"): it's named for what's in it.
        let filename = match named_for_contents(&filename, &file) {
            Some(named) => {
                let renamed = staging.join(&named);
                fs::rename(&file, &renamed).map_err(text)?;
                file = renamed;
                named
            }
            None => filename,
        };
        let target = req.dir.as_ref().map(PathBuf::from).unwrap_or_else(|| apps_dir.join(&folder));
        if crate::msi::is_package(&filename) {
            progress(Progress { id: req.id.clone(), phase: "installing", received: 0, total: None, setup: true });
            let (dir, version, preferred) = (target.clone(), req.version.clone(), req.preferred.clone());
            let keep = req.program.clone().map(PathBuf::from);
            let setup = tokio::task::spawn_blocking(move || -> Result<crate::msi::Setup, String> {
                // Marked as Quiver's first, as other installs are; its files go where the setup puts them.
                fs::create_dir_all(&dir).map_err(text)?;
                if !dir.join(VERSION_FILE).exists() {
                    fs::write(dir.join(VERSION_FILE), "").map_err(text)?;
                }
                let setup = crate::msi::install(&file, &dir, &preferred, keep.as_deref())?;
                fs::write(dir.join(VERSION_FILE), &version).map_err(text)?;
                Ok(setup)
            })
            .await
            .map_err(text)??;
            let path = |p: Option<PathBuf>| p.map(|p| p.to_string_lossy().into_owned());
            return Ok(Installed {
                dir: target.to_string_lossy().into(),
                version: req.version.clone(),
                checksum: format!("sha256:{hash}"),
                installer: Some("msi"),
                program: path(setup.program),
                location: path(setup.location),
                restart: setup.restart,
            });
        }
        // Android asks the player to confirm installing an APK.
        let setup = cfg!(target_os = "android") && filename.to_lowercase().ends_with(".apk");
        progress(Progress { id: req.id.clone(), phase: "installing", received: 0, total: None, setup });
        let files = req.files_to_add.clone();
        let version = req.version.clone();
        let out = staging.join("out");
        let dir = target.clone();
        tokio::task::spawn_blocking(move || -> Result<(), String> {
            extract(&file, &filename, &out)?;
            flatten(&out).map_err(text)?;
            drop_records(&out);
            // Marked as Quiver's before anything goes in, so an install cut short can still be retried or removed.
            fs::create_dir_all(&dir).map_err(text)?;
            if !dir.join(VERSION_FILE).exists() {
                fs::write(dir.join(VERSION_FILE), "").map_err(text)?;
            }
            merge(&out, &dir).map_err(text)?;
            // A Flatpak bundle is installed for the player; Play then runs it with flatpak.
            if cfg!(target_os = "linux") {
                if let Some(bundle) = crate::flatpak::bundle_in(&dir) {
                    let id = crate::flatpak::install(&bundle)?;
                    fs::write(dir.join(crate::flatpak::FILE), id).map_err(text)?;
                }
            }
            // An APK is installed by Android, once the player confirms it; Play then starts its package.
            #[cfg(target_os = "android")]
            if let Some(apk) = crate::android::apk_in(&dir) {
                let installed = crate::android::install(&apk);
                // Android keeps its own copy; one that wasn't installed is downloaded again next time.
                let _ = fs::remove_file(&apk);
                fs::write(dir.join(crate::android::FILE), installed?.package_name).map_err(text)?;
            }
            for name in &files {
                let path = dir.join(plain_name(name)?);
                if !path.exists() {
                    fs::write(path, "").map_err(text)?;
                }
            }
            fs::write(dir.join(VERSION_FILE), &version).map_err(text)
        })
        .await
        .map_err(text)??;
        Ok(Installed {
            dir: target.to_string_lossy().into(),
            version: req.version.clone(),
            checksum: format!("sha256:{hash}"),
            installer: None,
            program: None,
            location: None,
            restart: false,
        })
    }
    .await;
    let _ = fs::remove_dir_all(&staging);
    result
}

/// Whether a download's SHA-256 is the one it was pinned to. A file pinned to
/// nothing passes (the page asked first where that matters); one pinned in a
/// way Quiver can't check fails, rather than going in unchecked.
fn matches_checksum(checksum: Option<&str>, hash: &str) -> bool {
    match checksum {
        None => true,
        Some(c) => c.strip_prefix("sha256:").is_some_and(|expected| expected.eq_ignore_ascii_case(hash)),
    }
}

/// Whether Quiver may install into a folder: a new or empty one, or one it
/// installed into. Never a folder of files the player put there themselves.
pub fn may_install_into(dir: &Path) -> bool {
    !fs::read_dir(dir).is_ok_and(|mut entries| entries.next().is_some()) || dir.join(VERSION_FILE).is_file()
}

/// Whether Quiver may remove an app's folder: only one that still looks like
/// an install, one it installed into or (adopted) a 3.x install.
pub fn may_remove(dir: &Path, adopted: bool) -> bool {
    dir.join(VERSION_FILE).is_file() || (adopted && dir.join("version.txt").is_file())
}

/// Streams the file to disk, hashing as it goes; returns its SHA-256.
async fn download(req: &InstallRequest, to: &Path, progress: &impl Fn(Progress)) -> Result<String, String> {
    let response = reqwest::Client::builder()
        .user_agent("QuiverLauncher/4")
        .build()
        .map_err(text)?
        .get(&req.url)
        .send()
        .await
        .map_err(|_| "The download failed. Check your connection and try again.".to_string())?;
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        // Its developer deleted it, or the release, after the site last read it.
        return Err(MISSING.into());
    }
    let response = response.error_for_status().map_err(|e| format!("The download failed: {e}"))?;
    let total = response.content_length();
    let mut file = tokio::fs::File::create(to).await.map_err(text)?;
    let mut hasher = Sha256::new();
    let (mut received, mut reported) = (0u64, 0u64);
    let mut stream = response.bytes_stream();
    while let Some(chunk) = stream.next().await {
        let chunk = chunk.map_err(|e| format!("The download stopped: {e}"))?;
        hasher.update(&chunk);
        file.write_all(&chunk).await.map_err(text)?;
        received += chunk.len() as u64;
        if received - reported >= 256 * 1024 || Some(received) == total {
            reported = received;
            progress(Progress { id: req.id.clone(), phase: "downloading", received, total, setup: false });
        }
    }
    file.flush().await.map_err(text)?;
    Ok(hex::encode(hasher.finalize()))
}

/// How a tar archive is compressed.
#[derive(Debug, PartialEq)]
enum Tarball {
    Gzip,
    Xz,
    Bzip2,
    Zstd,
    Plain,
}

/// A tar archive, from its name: .tar.gz, .tar.xz, .tar.bz2, .tar.zst, their short forms, or .tar.
fn tarball(name: &str) -> Option<Tarball> {
    const NAMES: [(&[&str], Tarball); 5] = [
        (&[".tar.gz", ".tgz"], Tarball::Gzip),
        (&[".tar.xz", ".txz"], Tarball::Xz),
        (&[".tar.bz2", ".tbz2", ".tbz"], Tarball::Bzip2),
        (&[".tar.zst", ".tzst"], Tarball::Zstd),
        (&[".tar"], Tarball::Plain),
    ];
    NAMES.into_iter().find(|(ends, _)| ends.iter().any(|end| name.ends_with(end))).map(|(_, packing)| packing)
}

/// Whether a name says what kind of file it is: one `extract` unpacks, or a program or package.
fn has_known_extension(name: &str) -> bool {
    let lower = name.to_lowercase();
    tarball(&lower).is_some()
        || [".zip", ".7z", ".rar", ".msi", ".exe", ".appimage", ".flatpak", ".apk", ".x86_64", ".sh"].iter().any(|end| lower.ends_with(end))
}

/// The name a file with no known extension should have, from its first bytes;
/// None when its name already says, or its contents don't (a bare program).
fn named_for_contents(name: &str, file: &Path) -> Option<String> {
    if has_known_extension(name) {
        return None;
    }
    let mut head = [0u8; 262];
    let read = fs::File::open(file).and_then(|mut f| {
        let mut n = 0;
        while n < head.len() {
            match f.read(&mut head[n..])? {
                0 => break,
                m => n += m,
            }
        }
        Ok(n)
    });
    let head = &head[..read.ok()?];
    const MAGIC: [(&[u8], &str); 9] = [
        (b"PK\x03\x04", ".zip"),
        (b"7z\xBC\xAF\x27\x1C", ".7z"),
        (b"Rar!\x1A\x07", ".rar"),
        (b"\x1F\x8B", ".tar.gz"),
        (b"\xFD7zXZ\x00", ".tar.xz"),
        (b"BZh", ".tar.bz2"),
        (b"\x28\xB5\x2F\xFD", ".tar.zst"),
        (b"\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1", ".msi"),
        (b"MZ", ".exe"),
    ];
    let extension = MAGIC.iter().find(|(magic, _)| head.starts_with(magic)).map(|(_, ext)| *ext);
    // A plain tar says so at byte 257.
    let extension = extension.or_else(|| head.get(257..262).filter(|m| *m == b"ustar").map(|_| ".tar"))?;
    Some(format!("{name}{extension}"))
}

pub(crate) fn extract(file: &Path, name: &str, out: &Path) -> Result<(), String> {
    unpack(file, name, out)?;
    #[cfg(unix)]
    open_up(out);
    Ok(())
}

/// Lets the player into everything an archive unpacked and run its programs.
/// A tar can mark a folder 0644, which locks it ("Access denied"), and a 7z
/// keeps no Unix permissions at all, so a Linux build's program comes out
/// unable to run. Folders get rwx and files rw for their owner; an AppImage,
/// a script or a Linux binary (ELF) that runs for no one becomes runnable.
#[cfg(unix)]
fn open_up(dir: &Path) {
    use std::os::unix::fs::PermissionsExt;
    let Ok(entries) = fs::read_dir(dir) else { return };
    for entry in entries.flatten() {
        let path = entry.path();
        // Not through a link, which could lead out of the folder.
        let Ok(meta) = fs::symlink_metadata(&path) else { continue };
        let mode = meta.permissions().mode();
        let wanted = if meta.is_dir() {
            mode | 0o700
        } else if meta.is_file() {
            let runnable = if mode & 0o111 != 0 || !is_program(&path) { mode } else { mode | 0o755 };
            runnable | 0o600
        } else {
            continue;
        };
        if wanted != mode {
            let _ = fs::set_permissions(&path, fs::Permissions::from_mode(wanted));
        }
        if meta.is_dir() {
            open_up(&path);
        }
    }
}

/// An AppImage, a script or a Linux binary, by its name or its first bytes.
#[cfg(unix)]
fn is_program(path: &Path) -> bool {
    let name = path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    if [".appimage", ".x86_64", ".sh"].iter().any(|e| name.ends_with(e)) {
        return true;
    }
    let mut head = [0u8; 4];
    fs::File::open(path).and_then(|mut f| f.read_exact(&mut head)).is_ok() && &head == b"\x7fELF"
}

fn unpack(file: &Path, name: &str, out: &Path) -> Result<(), String> {
    fs::create_dir_all(out).map_err(text)?;
    let lower = name.to_lowercase();
    if lower.ends_with(".zip") {
        let mut archive = zip::ZipArchive::new(fs::File::open(file).map_err(text)?).map_err(text)?;
        for i in 0..archive.len() {
            let mut entry = archive.by_index(i).map_err(text)?;
            let Some(path) = enclosed(entry.name()).map(|p| out.join(p)) else { continue };
            if entry.is_dir() {
                fs::create_dir_all(&path).map_err(text)?;
                continue;
            }
            if let Some(parent) = path.parent() {
                fs::create_dir_all(parent).map_err(text)?;
            }
            io::copy(&mut entry, &mut fs::File::create(&path).map_err(text)?).map_err(text)?;
            #[cfg(unix)]
            if let Some(mode) = entry.unix_mode() {
                use std::os::unix::fs::PermissionsExt;
                let _ = fs::set_permissions(&path, fs::Permissions::from_mode(mode & 0o777));
            }
        }
        Ok(())
    } else if let Some(packing) = tarball(&lower) {
        let file = io::BufReader::new(fs::File::open(file).map_err(text)?);
        let stream: Box<dyn Read> = match packing {
            Tarball::Gzip => Box::new(flate2::read::MultiGzDecoder::new(file)),
            Tarball::Xz => Box::new(lzma_rust2::XzReader::new(file, true)),
            Tarball::Bzip2 => Box::new(bzip2::read::MultiBzDecoder::new(file)),
            Tarball::Zstd => Box::new(ruzstd::decoding::StreamingDecoder::new(file).map_err(text)?),
            Tarball::Plain => Box::new(file),
        };
        // unpack keeps every entry inside `out`.
        tar::Archive::new(stream).unpack(out).map_err(text)
    } else if lower.ends_with(".7z") {
        sevenz_rust2::decompress_file_with_extract_fn(file, out, |entry, reader, _| match enclosed(entry.name()) {
            Some(path) => sevenz_rust2::default_entry_extract_fn(entry, reader, &out.join(path)),
            // A solid archive's next entry starts where this one ends.
            None => io::copy(reader, &mut io::sink()).map(|_| true).map_err(sevenz_rust2::Error::io),
        })
        .map_err(text)
    } else if lower.ends_with(".rar") {
        unrar_into(file, out)
    } else {
        // A single program: an .exe, an AppImage or a bare binary.
        let path = out.join(name);
        fs::rename(file, &path).or_else(|_| fs::copy(file, &path).map(|_| ())).map_err(text)?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            fs::set_permissions(&path, fs::Permissions::from_mode(0o755)).map_err(text)?;
        }
        Ok(())
    }
}

/// Where a zip or 7z entry goes inside the folder it's unpacked into. `\`
/// separates folders as `/` does, as tools on Windows write it; None for
/// anything that would land outside (zip-slip): `..` past the top, `/etc`,
/// `\\server\share`, `C:\`.
fn enclosed(name: &str) -> Option<PathBuf> {
    let bytes = name.as_bytes();
    let drive = bytes.len() >= 2 && bytes[0].is_ascii_alphabetic() && bytes[1] == b':';
    if name.contains('\0') || name.starts_with(['/', '\\']) || drive {
        return None;
    }
    let mut path = PathBuf::new();
    for part in name.split(['/', '\\']) {
        match part {
            "" | "." => {}
            ".." => {
                if !path.pop() {
                    return None;
                }
            }
            // Anything a folder name can't be here, like `C:` on Windows.
            _ => {
                let mut parts = Path::new(part).components();
                if !matches!((parts.next(), parts.next()), (Some(std::path::Component::Normal(_)), None)) {
                    return None;
                }
                path.push(part);
            }
        }
    }
    (!path.as_os_str().is_empty()).then_some(path)
}

/// Hoists the contents of a lone wrapper folder, as most archives have one.
pub(crate) fn flatten(dir: &Path) -> io::Result<()> {
    loop {
        let entries: Vec<_> = fs::read_dir(dir)?.collect::<Result<_, _>>()?;
        let [only] = entries.as_slice() else { return Ok(()) };
        if !only.file_type()?.is_dir() {
            return Ok(());
        }
        let inner = dir.join(".quiver-flatten");
        fs::rename(only.path(), &inner)?;
        for entry in fs::read_dir(&inner)? {
            let entry = entry?;
            fs::rename(entry.path(), dir.join(entry.file_name()))?;
        }
        fs::remove_dir(inner)?;
    }
}

/// The files Quiver keeps in an app's folder about how it installed it, which
/// uninstalling and Play act on (`flatpak uninstall` with the id it holds,
/// `msiexec /x` with the product's code).
fn records() -> Vec<&'static str> {
    #[allow(unused_mut)]
    let mut names = vec![VERSION_FILE, crate::flatpak::FILE, crate::msi::FILE, crate::msi::V3_FILE, crate::mods::RECORD];
    #[cfg(target_os = "android")]
    names.push(crate::android::FILE);
    names
}

/// Takes Quiver's own records out of what a download unpacked: only Quiver
/// writes them, never a release.
fn drop_records(out: &Path) {
    for name in records() {
        let _ = fs::remove_file(out.join(name));
    }
}

/// Moves `from` into `to`, replacing files but keeping anything else in `to`
/// (saves, configs, files the player added).
fn merge(from: &Path, to: &Path) -> io::Result<()> {
    fs::create_dir_all(to)?;
    for entry in fs::read_dir(from)? {
        let entry = entry?;
        let dest = to.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            merge(&entry.path(), &dest)?;
        } else {
            if dest.is_dir() {
                fs::remove_dir_all(&dest)?;
            }
            // So it can be replaced now, and its new version next time.
            writable(&dest);
            writable(&entry.path());
            if fs::rename(entry.path(), &dest).is_err() {
                fs::copy(entry.path(), &dest)?;
            }
        }
    }
    Ok(())
}

/// Clears a file's read-only flag on Windows, which won't replace or remove a
/// read-only file ("Access is denied"). A tar without write permission or a
/// RAR unpacks files read-only, as can a tool the player copied them with. On
/// other systems the folder decides that, and `open_up` sees to what unpacked.
pub(crate) fn writable(path: &Path) {
    #[cfg(windows)]
    if let Ok(meta) = fs::symlink_metadata(path) {
        let mut permissions = meta.permissions();
        if meta.is_file() && permissions.readonly() {
            #[allow(clippy::permissions_set_readonly_false)]
            permissions.set_readonly(false);
            let _ = fs::set_permissions(path, permissions);
        }
    }
    #[cfg(not(windows))]
    let _ = path;
}

/// Programs that come with an app but don't start it.
pub(crate) const HELPERS: [&str; 6] = ["unins", "crash", "vc_redist", "dxsetup", "setup", "updater"];

/// The program to start: the first file matching the catalog's preferred
/// executables, else the likeliest program, shallowest first.
pub fn find_executable(dir: &Path, preferred: &[String], os: &str) -> Option<PathBuf> {
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    files.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    let name = |p: &PathBuf| p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    for pattern in preferred {
        let Ok(glob) = glob::Pattern::new(&pattern.to_lowercase()) else { continue };
        if let Some(hit) = files.iter().find(|p| glob.matches(&name(p))) {
            return Some(hit.clone());
        }
    }
    let helper = |n: &str| HELPERS.iter().any(|h| n.starts_with(h));
    let linux = |p: &PathBuf, n: &str| n.ends_with(".appimage") || n.ends_with(".x86_64") || is_unix_binary(p);
    // A Windows build's helper script (a run.sh beside its .exe) isn't the
    // game: with no Linux program here, there's none, and Wine runs the .exe.
    let scripts = files.iter().any(|p| linux(p, &name(p))) || !files.iter().any(|p| name(p).ends_with(".exe") && !helper(&name(p)));
    files.into_iter().find(|p| {
        let n = name(p);
        let program = match os {
            "windows" => n.ends_with(".exe"),
            "macos" => n.ends_with(".app"),
            _ => linux(p, &n) || (scripts && n.ends_with(".sh")),
        };
        program && !helper(&n)
    })
}

/// Every program in an app's folder the player might start: its own, its
/// launcher, its setup or updater (an update can ask for those again), but
/// not an uninstaller or a runtime's installer. Shallowest first, then by name;
/// a Windows build's too on Linux, where Wine runs it.
pub fn programs(dir: &Path, os: &str) -> Vec<PathBuf> {
    const NOT_PROGRAMS: [&str; 4] = ["unins", "crash", "vc_redist", "dxsetup"];
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    files.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    files
        .into_iter()
        .filter(|p| {
            let n = p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
            let windows = n.ends_with(".exe") || n.ends_with(".bat");
            let program = match os {
                "windows" => windows,
                "macos" => n.ends_with(".app"),
                _ => windows || n.ends_with(".appimage") || n.ends_with(".x86_64") || n.ends_with(".sh") || is_unix_binary(p),
            };
            program && !NOT_PROGRAMS.iter().any(|h| n.starts_with(h))
        })
        .take(40)
        .collect()
}

fn walk(dir: &Path, depth: usize, out: &mut Vec<PathBuf>) {
    let Ok(entries) = fs::read_dir(dir) else { return };
    for entry in entries.flatten() {
        let path = entry.path();
        let is_app_bundle = path.extension().is_some_and(|e| e == "app");
        if path.is_dir() && !is_app_bundle {
            if depth < 3 {
                walk(&path, depth + 1, out);
            }
        } else if path.file_name().is_some_and(|n| n != VERSION_FILE) {
            out.push(path);
        }
    }
}

#[cfg(unix)]
fn is_unix_binary(path: &Path) -> bool {
    use std::os::unix::fs::PermissionsExt;
    path.extension().is_none()
        && fs::metadata(path).is_ok_and(|m| m.is_file() && m.permissions().mode() & 0o111 != 0)
}
#[cfg(not(unix))]
fn is_unix_binary(_: &Path) -> bool {
    false
}

fn text(e: impl std::fmt::Display) -> String {
    e.to_string()
}

#[cfg(not(target_os = "android"))]
fn unrar_into(file: &Path, out: &Path) -> Result<(), String> {
    let mut archive = unrar::Archive::new(file).open_for_processing().map_err(text)?;
    while let Some(header) = archive.read_header().map_err(text)? {
        let entry = header.entry();
        // Same rule as enclosed: nothing outside `out`. unrar already reads RAR 4's `\` as a folder.
        let inside = entry.filename.components().all(|c| matches!(c, std::path::Component::Normal(_)));
        archive = if entry.is_file() && inside {
            header.extract_with_base(out).map_err(text)?
        } else {
            header.skip().map_err(text)?
        };
    }
    Ok(())
}

/// unrar's C++ doesn't build for Android.
#[cfg(target_os = "android")]
fn unrar_into(_file: &Path, _out: &Path) -> Result<(), String> {
    Err("RAR files can't be unpacked on Android yet.".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_release_cant_bring_quivers_own_records() {
        let dir = tempfile::tempdir().unwrap();
        let zip = {
            let mut w = zip::ZipWriter::new(io::Cursor::new(Vec::new()));
            for name in ["Game-v1/Game.exe", "Game-v1/.quiver-flatpak", "Game-v1/.quiver-msi", "Game-v1/.quiver-version", "Game-v1/.quiver-mods.json"] {
                w.start_file(name, zip::write::SimpleFileOptions::default()).unwrap();
                io::Write::write_all(&mut w, b"--all").unwrap();
            }
            w.finish().unwrap().into_inner()
        };
        let file = dir.path().join("Game.zip");
        fs::write(&file, zip).unwrap();
        let out = dir.path().join("out");
        extract(&file, "Game.zip", &out).unwrap();
        flatten(&out).unwrap();
        drop_records(&out);
        let left: Vec<_> = fs::read_dir(&out).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(left, ["Game.exe"]);
    }

    #[test]
    fn an_update_replaces_read_only_files_and_leaves_none() {
        let dir = tempfile::tempdir().unwrap();
        let (from, to) = (dir.path().join("out"), dir.path().join("Game"));
        fs::create_dir_all(from.join("data")).unwrap();
        fs::create_dir_all(to.join("data")).unwrap();
        let read_only = |path: &Path| {
            let mut permissions = fs::metadata(path).unwrap().permissions();
            permissions.set_readonly(true);
            fs::set_permissions(path, permissions).unwrap();
        };
        for (name, old, new) in [("Game.exe", "v1", "v2"), ("data/level.dat", "v1", "v2")] {
            fs::write(to.join(name), old).unwrap();
            read_only(&to.join(name));
            fs::write(from.join(name), new).unwrap();
        }
        // As a tar or a RAR can unpack it.
        fs::write(from.join("data/new.dat"), "v2").unwrap();
        read_only(&from.join("data/new.dat"));
        merge(&from, &to).unwrap();
        for name in ["Game.exe", "data/level.dat", "data/new.dat"] {
            assert_eq!(fs::read_to_string(to.join(name)).unwrap(), "v2");
            #[cfg(windows)]
            assert!(!fs::metadata(to.join(name)).unwrap().permissions().readonly(), "{name}");
        }
        // And uninstalling still removes them all.
        read_only(&to.join("Game.exe"));
        fs::remove_dir_all(&to).unwrap();
    }

    #[test]
    fn never_installs_over_or_removes_the_players_own_folder() {
        let apps = tempfile::tempdir().unwrap();
        let own = apps.path().join("Mine");
        assert!(may_install_into(&own), "a new folder");
        fs::create_dir(&own).unwrap();
        assert!(may_install_into(&own), "an empty folder");
        fs::write(own.join("game.exe"), "").unwrap();
        assert!(!may_install_into(&own), "the player's files");
        assert!(!may_remove(&own, false));
        // A 3.x install is only removed while it still has its version file, and only when adopted.
        fs::write(own.join("version.txt"), "1.0").unwrap();
        assert!(!may_remove(&own, false));
        assert!(may_remove(&own, true));
        let ours = apps.path().join("Ours");
        fs::create_dir(&ours).unwrap();
        fs::write(ours.join("game.exe"), "").unwrap();
        fs::write(ours.join(VERSION_FILE), "1.0").unwrap();
        assert!(may_install_into(&ours) && may_remove(&ours, false));
    }

    #[test]
    fn a_download_must_match_the_checksum_it_was_pinned_to() {
        let hash = "ab".repeat(32);
        assert!(matches_checksum(None, &hash), "a file nobody pinned");
        assert!(matches_checksum(Some(&format!("sha256:{hash}")), &hash));
        assert!(matches_checksum(Some(&format!("sha256:{}", hash.to_uppercase())), &hash));
        assert!(!matches_checksum(Some(&format!("sha256:{}", "cd".repeat(32))), &hash));
        // Pinned in a way Quiver can't check: refused, not installed unchecked.
        assert!(!matches_checksum(Some(&format!("sha512:{hash}")), &hash));
        assert!(!matches_checksum(Some(""), &hash));
    }

    #[test]
    fn prefers_catalog_executable_then_skips_helpers() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["unins000.exe", "Game.exe", "bin/Tool.exe"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        let found = |preferred: &[&str]| {
            let preferred: Vec<String> = preferred.iter().map(|s| s.to_string()).collect();
            find_executable(dir.path(), &preferred, "windows").unwrap().file_name().unwrap().to_owned()
        };
        assert_eq!(found(&[]), "Game.exe");
        assert_eq!(found(&["tool*.exe"]), "Tool.exe");
    }

    #[test]
    fn a_windows_builds_helper_script_isnt_its_program_on_linux() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["run.sh", "run.py", "bin/GoW3.exe"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        // None here, so Play runs the .exe through Wine.
        assert_eq!(find_executable(dir.path(), &[], "linux"), None);
        // Unless the catalog names the script.
        assert_eq!(find_executable(dir.path(), &["run.sh".into()], "linux"), Some(dir.path().join("run.sh")));
        // A Linux build's launch script still runs, with or without a Windows tool beside it.
        fs::write(dir.path().join("bin/Game.AppImage"), "").unwrap();
        assert_eq!(find_executable(dir.path(), &[], "linux"), Some(dir.path().join("run.sh")));
        fs::remove_file(dir.path().join("bin/Game.AppImage")).unwrap();
        fs::remove_file(dir.path().join("bin/GoW3.exe")).unwrap();
        assert_eq!(find_executable(dir.path(), &[], "linux"), Some(dir.path().join("run.sh")));
    }

    #[test]
    fn lists_the_programs_a_player_might_start_but_not_uninstallers() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["unins000.exe", "Game.exe", "readme.txt", "launcher/Setup.exe", "redist/vc_redist.x64.exe"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        let names: Vec<_> = programs(dir.path(), "windows").iter().map(|p| p.strip_prefix(dir.path()).unwrap().to_string_lossy().replace('\\', "/")).collect();
        assert_eq!(names, ["Game.exe", "launcher/Setup.exe"]);
    }

    #[test]
    fn folder_names_stay_inside_the_apps_folder() {
        for bad in ["..", "a/b", "a\\b", "C:", "NUL", "com1.txt", "game.", ""] {
            assert!(plain_name(bad).is_err(), "{bad}");
        }
        assert_eq!(plain_name(" Sample-Port ").unwrap(), "Sample-Port");
    }

    #[test]
    fn unpacks_tarballs_however_theyre_packed() {
        // A wrapper folder with a program and its data, as a release has.
        let mut tar = tar::Builder::new(Vec::new());
        for (path, body) in [("Game-v1/game.sh", "#!/bin/sh\n"), ("Game-v1/data/level1.dat", "level")] {
            let mut header = tar::Header::new_gnu();
            header.set_size(body.len() as u64);
            header.set_mode(0o755);
            header.set_cksum();
            tar.append_data(&mut header, path, body.as_bytes()).unwrap();
        }
        let plain = tar.into_inner().unwrap();
        let xz = {
            let mut w = lzma_rust2::XzWriter::new(Vec::new(), lzma_rust2::XzOptions::default()).unwrap();
            io::Write::write_all(&mut w, &plain).unwrap();
            w.finish().unwrap()
        };
        let bz2 = {
            let mut w = bzip2::write::BzEncoder::new(Vec::new(), bzip2::Compression::default());
            io::Write::write_all(&mut w, &plain).unwrap();
            w.finish().unwrap()
        };
        let zst = ruzstd::encoding::compress_to_vec(plain.as_slice(), ruzstd::encoding::CompressionLevel::Fastest);
        let gz = {
            let mut w = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::default());
            io::Write::write_all(&mut w, &plain).unwrap();
            w.finish().unwrap()
        };
        for (name, bytes) in [("Game.tar.xz", &xz), ("Game.tar.bz2", &bz2), ("Game.tar.zst", &zst), ("Game.tgz", &gz), ("Game.tar", &plain), ("Game.TXZ", &xz)] {
            let dir = tempfile::tempdir().unwrap();
            let file = dir.path().join(name);
            fs::write(&file, bytes).unwrap();
            let out = dir.path().join("out");
            extract(&file, name, &out).unwrap_or_else(|e| panic!("{name}: {e}"));
            flatten(&out).unwrap();
            assert_eq!(fs::read_to_string(out.join("data/level1.dat")).unwrap(), "level", "{name}");
            #[cfg(unix)]
            {
                use std::os::unix::fs::PermissionsExt;
                assert_eq!(fs::metadata(out.join("game.sh")).unwrap().permissions().mode() & 0o111, 0o111, "{name} keeps the program runnable");
            }
        }
        // Something else is a single program, not an archive.
        assert_eq!(tarball("game.x86_64"), None);
        assert_eq!(tarball("Game.tar.zst"), Some(Tarball::Zstd));
    }

    #[cfg(unix)]
    #[test]
    fn a_locked_folder_or_a_program_that_cant_run_is_opened_up() {
        use std::os::unix::fs::PermissionsExt;
        // A folder marked 0644 and a Linux binary and its data marked 0644, as the Uncharted port's tar had.
        let mut tar = tar::Builder::new(Vec::new());
        let mut folder = tar::Header::new_gnu();
        folder.set_entry_type(tar::EntryType::Directory);
        folder.set_size(0);
        folder.set_mode(0o644);
        folder.set_cksum();
        tar.append_data(&mut folder, "analysis/", io::empty()).unwrap();
        for (path, body) in [("analysis/game", &b"\x7fELF\x02\x01"[..]), ("analysis/level.dat", &b"level"[..])] {
            let mut header = tar::Header::new_gnu();
            header.set_size(body.len() as u64);
            header.set_mode(0o644);
            header.set_cksum();
            tar.append_data(&mut header, path, body).unwrap();
        }
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("Game.tar");
        fs::write(&file, tar.into_inner().unwrap()).unwrap();
        let out = dir.path().join("out");
        extract(&file, "Game.tar", &out).unwrap();
        let mode = |p: &str| fs::metadata(out.join(p)).unwrap().permissions().mode() & 0o777;
        assert_eq!(mode("analysis") & 0o700, 0o700, "the folder can be entered");
        assert_eq!(mode("analysis/game"), 0o755, "the binary runs");
        assert_eq!(mode("analysis/level.dat"), 0o644, "data stays data");
    }

    #[test]
    fn names_a_file_without_an_extension_for_its_contents() {
        let dir = tempfile::tempdir().unwrap();
        let zip = {
            let mut w = zip::ZipWriter::new(io::Cursor::new(Vec::new()));
            w.start_file("Game/Game.exe", zip::write::SimpleFileOptions::default()).unwrap();
            io::Write::write_all(&mut w, b"MZ").unwrap();
            w.finish().unwrap().into_inner()
        };
        let named = |name: &str, bytes: &[u8]| {
            let file = dir.path().join("download");
            fs::write(&file, bytes).unwrap();
            named_for_contents(name, &file)
        };
        // GitLab names a release's files as their links are titled.
        let name = "Game-v0.9.2-Windows-RelWithDebInfo";
        assert_eq!(named(name, &zip).as_deref(), Some("Game-v0.9.2-Windows-RelWithDebInfo.zip"));
        assert_eq!(named("Game-v1-Windows", b"MZ\x90\x00").as_deref(), Some("Game-v1-Windows.exe"));
        // A bare program, and a file whose name already says, stay as they are.
        assert_eq!(named("game", b"\x7FELF\x02"), None);
        assert_eq!(named("Game.AppImage", &zip), None);
        assert_eq!(named("game.x86_64", b"\x7FELF"), None);
        // And it unpacks as the archive it is.
        let file = dir.path().join(name);
        fs::write(&file, &zip).unwrap();
        let out = dir.path().join("out");
        extract(&file, &named(name, &zip).unwrap(), &out).unwrap();
        flatten(&out).unwrap();
        assert!(out.join("Game.exe").is_file());
    }

    #[test]
    fn unpacks_zips_made_on_windows_into_folders() {
        let dir = tempfile::tempdir().unwrap();
        let zip = {
            let mut w = zip::ZipWriter::new(io::Cursor::new(Vec::new()));
            let options = zip::write::SimpleFileOptions::default();
            w.add_directory("SSX3-recomp\\hd-textures\\", options).unwrap();
            for name in [
                "SSX3-recomp\\ssx3_recomp.exe",
                "SSX3-recomp\\hd-textures\\board.dds",
                "SSX3-recomp\\..\\..\\escaped.txt",
                "..\\escaped.txt",
                "C:\\escaped.txt",
                "\\escaped.txt",
                "/escaped.txt",
            ] {
                w.start_file(name, options).unwrap();
                io::Write::write_all(&mut w, b"MZ").unwrap();
            }
            w.finish().unwrap().into_inner()
        };
        let file = dir.path().join("SSX3Recomp.zip");
        fs::write(&file, zip).unwrap();
        let out = dir.path().join("apps").join("SSX3");
        extract(&file, "SSX3Recomp.zip", &out).unwrap();
        assert!(out.join("SSX3-recomp").is_dir());
        assert!(out.join("SSX3-recomp/hd-textures/board.dds").is_file());
        flatten(&out).unwrap();
        assert!(out.join("ssx3_recomp.exe").is_file() && out.join("hd-textures").is_dir());
        // Nothing lands outside the app's folder, and nothing beside the game.
        assert!(!dir.path().join("escaped.txt").exists() && !dir.path().join("apps/escaped.txt").exists());
        assert_eq!(fs::read_dir(&out).unwrap().count(), 2);
    }

    #[test]
    fn unpacks_7z_files_made_on_windows_into_folders() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("Game.7z");
        let mut w = sevenz_rust2::SevenZWriter::create(&file).unwrap();
        // Solid, so a refused entry sits between two the game needs.
        let entries = ["Game\\Game.exe", "..\\escaped.txt", "Game\\data\\level.bin"];
        w.push_archive_entries(
            entries.iter().map(|name| sevenz_rust2::SevenZArchiveEntry::new_file(name)).collect(),
            entries.iter().map(|name| sevenz_rust2::SourceReader::new(name.as_bytes())).collect::<Vec<_>>().into(),
        )
        .unwrap();
        w.finish().unwrap();
        let out = dir.path().join("out");
        extract(&file, "Game.7z", &out).unwrap();
        assert_eq!(fs::read(out.join("Game/Game.exe")).unwrap(), b"Game\\Game.exe");
        assert_eq!(fs::read(out.join("Game/data/level.bin")).unwrap(), b"Game\\data\\level.bin");
        assert!(!dir.path().join("escaped.txt").exists());
    }

    #[test]
    fn archive_paths_stay_inside_the_folder_theyre_unpacked_into() {
        let path = |p: &str| Some(PathBuf::from_iter(p.split('/')));
        assert_eq!(enclosed("Game\\data\\level.bin"), path("Game/data/level.bin"));
        assert_eq!(enclosed("Game/./data\\..\\Game.exe"), path("Game/Game.exe"));
        assert_eq!(enclosed("Game\\"), path("Game"));
        for escape in ["..\\x", "Game\\..\\..\\x", "../x", "/etc/x", "\\x", "\\\\server\\share\\x", "C:\\x", "c:x", "a\0b", "", "."] {
            assert_eq!(enclosed(escape), None, "{escape:?}");
        }
    }

    #[test]
    fn flattens_a_wrapper_folder() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir_all(dir.path().join("Game-v1/data")).unwrap();
        fs::write(dir.path().join("Game-v1/Game.exe"), "").unwrap();
        flatten(dir.path()).unwrap();
        assert!(dir.path().join("Game.exe").exists() && dir.path().join("data").is_dir());
    }
}
