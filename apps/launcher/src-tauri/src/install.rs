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
/// What the download cache's fetch fails with when the file went from under it as it came, as antivirus software
/// (Windows Defender) takes away a file it flags.
pub const BLOCKED: &str = "blocked";
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
    /// Install into this folder instead (an install adopted from 3.x, or one kept outside the apps folder).
    pub dir: Option<String>,
    /// A new install's folder goes in here instead of the apps folder: one the player chose for it.
    #[serde(default)]
    pub into: Option<String>,
    pub files_to_add: Vec<String>,
    pub version: String,
    /// The catalog's preferred programs, for finding the one a Windows installer put on the computer.
    #[serde(default)]
    pub preferred: Vec<String>,
    /// The program the app started with before an update.
    pub program: Option<String>,
    /// The player's GitHub token, for a file of a GitHub repository asked for through GitHub's API: a private
    /// repository's files come only that way. GitHub answers with a redirect to where the file is, which reqwest
    /// follows without the token, as it leaves `Authorization` behind going to another host.
    #[serde(default)]
    pub github_token: Option<String>,
    /// The file's browser link, tried without the token when GitHub refuses the token itself (expired or revoked):
    /// a public repository's file still downloads that way, as it did before the token was saved.
    #[serde(default)]
    pub fallback_url: Option<String>,
    /// The download cache (cache.rs), when the player turned it on: the file is taken from it when it's there, and
    /// kept in it once downloaded when `keep` says so.
    #[serde(default)]
    pub cache: Option<crate::cache::CacheUse>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Installed {
    pub dir: String,
    pub version: String,
    /// The downloaded file's `sha256:<hex>`, which tells it from the file its release is verified with later.
    pub checksum: String,
    /// "msi" for a Windows Installer package, "setup" for a setup program the player installed outside the app's
    /// folder: either's files are where its setup put them.
    pub installer: Option<&'static str>,
    /// The program a Windows installer installed, when Quiver could tell.
    pub program: Option<String>,
    /// Where it installed, for choosing its program when Quiver couldn't tell.
    pub location: Option<String>,
    /// Windows wants a restart to finish installing.
    pub restart: bool,
    /// A setup program installed it, in the app's folder or outside it.
    pub setup: bool,
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
    let target = match (&req.dir, &req.into) {
        (Some(dir), _) => PathBuf::from(dir),
        (None, Some(into)) => new_folder_in(Path::new(into), &folder)?,
        (None, None) => {
            let target = apps_dir.join(&folder);
            // Never into a folder of files Quiver didn't put there, such as one the player fills themselves.
            if !may_install_into(&target) {
                return Err(format!(
                    "The folder {folder} in your apps folder has files Quiver didn't install, so it won't install over them. Rename or move that folder, then try again."
                ));
            }
            target
        }
    };
    // Ids can be "github:owner/repo"; the staging folder only needs to be unique.
    let stage: String = req.id.chars().map(|c| if c.is_ascii_alphanumeric() { c } else { '_' }).collect();
    let staging = apps_dir.join(".staging").join(stage);
    let _ = fs::remove_dir_all(&staging);
    fs::create_dir_all(&staging).map_err(text)?;
    let result = async {
        let mut file = staging.join(&filename);
        // The same file from the download cache, checked against its checksum there, rather than downloaded again.
        let cached = match (&req.cache, req.checksum.as_deref()) {
            (Some(cache), Some(checksum)) => {
                let (cache, checksum, to) = (cache.clone(), checksum.to_string(), file.clone());
                tokio::task::spawn_blocking(move || crate::cache::take(&cache, &checksum, &to).and_then(|_| crate::cache::hex_of(&checksum)))
                    .await
                    .ok()
                    .flatten()
            }
            _ => None,
        };
        let from_cache = cached.is_some();
        let hash = match cached {
            Some(hash) => {
                let size = fs::metadata(&file).map(|m| m.len()).ok();
                progress(Progress { id: req.id.clone(), phase: "downloading", received: size.unwrap_or(0), total: size, setup: false });
                hash
            }
            None => {
                let stop = std::pin::pin!(cancel.wait_for(|c| *c));
                match futures_util::future::select(std::pin::pin!(download(&req, &file, &progress)), stop).await {
                    futures_util::future::Either::Left((hash, _)) => hash?,
                    futures_util::future::Either::Right(_) => return Err(CANCELLED.to_string()),
                }
            }
        };
        if *cancel.borrow() {
            return Err(CANCELLED.to_string());
        }
        if !matches_checksum(req.checksum.as_deref(), &hash) {
            return Err(MISMATCH.to_string());
        }
        // Kept in the download cache, as checked: a cache that can't take it never stops the install.
        if let Some(cache) = req.cache.clone().filter(|c| c.keep && !from_cache) {
            let (kept, name, hash) = (file.clone(), filename.clone(), hash.clone());
            let _ = tokio::task::spawn_blocking(move || crate::cache::keep(&cache, &kept, &name, &hash)).await;
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
                setup: false,
            });
        }
        // Android asks the player to confirm installing an APK.
        let setup = cfg!(target_os = "android") && filename.to_lowercase().ends_with(".apk");
        progress(Progress { id: req.id.clone(), phase: "installing", received: 0, total: None, setup });
        let files = req.files_to_add.clone();
        let version = req.version.clone();
        let out = staging.join("out");
        let dir = target.clone();
        let unpacked = out.clone();
        // A download that's a Windows setup program is run, not kept as the app: it installs the app itself.
        let installer = tokio::task::spawn_blocking(move || -> Result<Option<(PathBuf, crate::setup::Kind)>, String> {
            extract(&file, &filename, &unpacked)?;
            flatten(&unpacked).map_err(text)?;
            drop_records(&unpacked);
            Ok(if cfg!(windows) { crate::setup::installer_in(&unpacked) } else { None })
        })
        .await
        .map_err(text)??;
        if let Some((installer, kind)) = installer {
            progress(Progress { id: req.id.clone(), phase: "installing", received: 0, total: None, setup: true });
            let (dir, version, preferred, files) = (target.clone(), req.version.clone(), req.preferred.clone(), files.clone());
            let keep = req.program.clone().map(PathBuf::from);
            let outcome = tokio::task::spawn_blocking(move || -> Result<crate::setup::Outcome, String> {
                let outcome = crate::setup::install(&installer, kind, &dir, &preferred, keep.as_deref(), &version)?;
                if matches!(&outcome, crate::setup::Outcome::Installed(setup) if setup.here) {
                    set_files(&dir, &files, &[])?;
                }
                Ok(outcome)
            })
            .await
            .map_err(text)??;
            if let crate::setup::Outcome::Installed(setup) = outcome {
                // In the app's folder it's an install like any other; elsewhere, Play starts the program it put there.
                let path = |p: PathBuf| p.to_string_lossy().into_owned();
                return Ok(Installed {
                    dir: target.to_string_lossy().into(),
                    version: req.version.clone(),
                    checksum: format!("sha256:{hash}"),
                    installer: (!setup.here).then_some("setup"),
                    program: setup.program.filter(|_| !setup.here).map(path),
                    location: (!setup.here).then(|| path(setup.location)),
                    restart: false,
                    setup: true,
                });
            }
        }
        tokio::task::spawn_blocking(move || -> Result<(), String> {
            // On Android an archive is only an app with an APK in it: one without (another system's build the player
            // picked) fails before it's put in the folder.
            #[cfg(target_os = "android")]
            if crate::android::apk_in(&out).is_none() {
                return Err("This download has no Android app (APK) in it. Pick another of the release's files.".into());
            }
            // This release's Flatpak bundle, where it lands: not one an earlier build left in the folder.
            let bundle = crate::flatpak::bundle_in(&out).and_then(|b| b.strip_prefix(&out).ok().map(|rel| dir.join(rel)));
            // Marked as Quiver's before anything goes in, so an install cut short can still be retried or removed.
            fs::create_dir_all(&dir).map_err(text)?;
            if !dir.join(VERSION_FILE).exists() {
                fs::write(dir.join(VERSION_FILE), "").map_err(text)?;
            }
            merge(&out, &dir).map_err(text)?;
            // A Flatpak bundle is installed for the player; Play then runs it with flatpak. A build that isn't one (the
            // Windows build in place of a Flatpak, say) is what Play runs instead.
            if cfg!(target_os = "linux") {
                match bundle {
                    Some(bundle) => {
                        let id = crate::flatpak::install(&bundle)?;
                        fs::write(dir.join(crate::flatpak::FILE), id).map_err(text)?;
                    }
                    None => {
                        let _ = fs::remove_file(dir.join(crate::flatpak::FILE));
                    }
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
            set_files(&dir, &files, &[])?;
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
            setup: false,
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

/// A folder for a new install in `into`, a folder the player chose: `folder`, or "folder 2" and so on when there's
/// already something there, which is never installed over.
pub fn new_folder_in(into: &Path, folder: &str) -> Result<PathBuf, String> {
    if !into.is_absolute() {
        return Err("Choose a folder on this computer.".into());
    }
    let empty = |dir: &Path| !fs::read_dir(dir).is_ok_and(|mut entries| entries.next().is_some());
    Ok(std::iter::once(folder.to_string()).chain((2..).map(|i| format!("{folder} {i}"))).map(|name| into.join(name)).find(|dir| empty(dir)).unwrap())
}

/// Whether Quiver may install into a folder: a new or empty one, or one it or Quiver Launcher 3 installed into. Never
/// a folder of files the player put there themselves.
pub fn may_install_into(dir: &Path) -> bool {
    !fs::read_dir(dir).is_ok_and(|mut entries| entries.next().is_some()) || is_install(dir)
}

/// Whether a folder is an install: one Quiver installed into (its version file), or one Quiver Launcher 3 did (its
/// version.txt). Either is Quiver's, wherever it is and however the launcher came to know it: a 3.x install in the
/// apps folder is one, whether it was brought in with its own path or became one of the apps folder's.
pub fn is_install(dir: &Path) -> bool {
    dir.join(VERSION_FILE).is_file() || dir.join("version.txt").is_file()
}

#[cfg(test)]
#[test]
fn installs_into_a_chosen_folder_beside_whatever_is_there() {
    let root = tempfile::tempdir().unwrap();
    assert_eq!(new_folder_in(root.path(), "Game").unwrap(), root.path().join("Game"));
    fs::create_dir_all(root.path().join("Game")).unwrap();
    assert_eq!(new_folder_in(root.path(), "Game").unwrap(), root.path().join("Game"), "an empty folder");
    fs::write(root.path().join("Game/theirs.txt"), "").unwrap();
    assert_eq!(new_folder_in(root.path(), "Game").unwrap(), root.path().join("Game 2"));
    assert!(new_folder_in(Path::new("relative"), "Game").is_err());
}

/// Whether Quiver may remove an app's folder: only one that still looks like an install (`is_install`).
pub fn may_remove(dir: &Path) -> bool {
    is_install(dir)
}

/// Removes an install's folder with `remove`. One cut short, by a file still in use say, can have taken the version
/// file that says the folder is Quiver's: it gets that back, or the player couldn't remove the rest or install it again.
pub fn remove_install(dir: &Path, remove: impl Fn(&Path) -> io::Result<()>) -> io::Result<()> {
    let markers: Vec<_> = [VERSION_FILE, "version.txt"]
        .into_iter()
        .filter_map(|name| Some((dir.join(name), fs::read(dir.join(name)).ok()?)))
        .collect();
    // A file that went while it was being removed (an antivirus's doing, or the game's own) needs no second thought.
    let removed = remove(dir).or_else(|e| if e.kind() == io::ErrorKind::NotFound { remove(dir) } else { Err(e) });
    match removed {
        Err(e) if dir.exists() => {
            for (path, contents) in markers {
                if !path.exists() {
                    let _ = fs::write(path, contents);
                }
            }
            Err(e)
        }
        _ => Ok(()),
    }
}

/// What the player is told when the server answers a download with an error: who answered and what to do, in words,
/// with the code for anyone who knows it. Never the address, which says nothing to a player and runs off the page.
fn refused(status: reqwest::StatusCode, url: &reqwest::Url) -> String {
    let host = url.host_str().unwrap_or_default().trim_start_matches("www.");
    // A release's file on GitHub comes from objects.githubusercontent.com or the like.
    let site = if host == "github.com" || host.ends_with(".github.com") || host.ends_with(".githubusercontent.com") { "GitHub" } else { host };
    let code = status.as_u16();
    match code {
        429 | 403 => format!("{site} is limiting downloads right now (error {code}). Try again in a few minutes."),
        500..=599 => format!("{site} isn't answering right now (error {code}). Try again in a few minutes."),
        _ => format!("{site} wouldn't send the file (error {code})."),
    }
}

/// Downloads a release file into the download cache alone, without installing it: the cache's own fetches (Every
/// latest, Hoarder, Selected titles). Only a file with a checksum is kept, once it matches it. `cancel` turning true
/// stops it with `CANCELLED`.
pub async fn fetch_to_cache(req: InstallRequest, mut cancel: tokio::sync::watch::Receiver<bool>, progress: impl Fn(Progress)) -> Result<(), String> {
    let cache = req.cache.clone().ok_or("No download cache to fetch into.")?;
    let checksum = req.checksum.as_deref().and_then(crate::cache::hex_of).ok_or("Only a file with a checksum is cached.")?;
    let filename = plain_name(&req.filename)?.to_string();
    if crate::cache::has(Path::new(&cache.dir), &[checksum.clone()])[0] {
        return Ok(());
    }
    // In the cache's own folder, so it's moved into place rather than copied across drives. Named for its app, version
    // and file, as published: what antivirus software says it flagged, for the player (or Quiver's maintainers) to tell
    // which release it was.
    let incoming = incoming_dir(Path::new(&cache.dir), &req.folder, &req.version);
    fs::create_dir_all(&incoming).map_err(text)?;
    let file = incoming.join(&filename);
    let result = async {
        let stop = std::pin::pin!(cancel.wait_for(|c| *c));
        let hash = match futures_util::future::select(std::pin::pin!(download(&req, &file, &progress)), stop).await {
            futures_util::future::Either::Left((Err(_), _)) if !file.exists() => return Err(BLOCKED.to_string()),
            futures_util::future::Either::Left((hash, _)) => hash?,
            futures_util::future::Either::Right(_) => return Err(CANCELLED.to_string()),
        };
        if !file.exists() {
            return Err(BLOCKED.to_string());
        }
        if hash != checksum {
            return Err(MISMATCH.to_string());
        }
        let kept = file.clone();
        tokio::task::spawn_blocking(move || crate::cache::keep(&cache, &kept, &filename, &hash))
            .await
            .map_err(text)?
            .map_err(|e| if e.kind() == io::ErrorKind::NotFound { BLOCKED.to_string() } else { text(e) })
    }
    .await;
    let _ = fs::remove_file(&file);
    // Its app's and version's folders go once empty; another fetch's, under way, keep theirs.
    let _ = fs::remove_dir(&incoming);
    let _ = incoming.parent().map(fs::remove_dir);
    result
}

/// Where the cache's fetch of an app's version comes in: `.incoming/<app>/<version>`, each made a safe folder name.
fn incoming_dir(cache: &Path, app: &str, version: &str) -> PathBuf {
    let safe = |s: &str, or: &str| {
        let name: String = s.trim().chars().map(|c| if c.is_alphanumeric() || "-_.+ ".contains(c) { c } else { '_' }).collect();
        let name = name.trim_matches(|c| c == '.' || c == ' ').to_string();
        if name.is_empty() || plain_name(&name).is_err() { or.to_string() } else { name }
    };
    cache.join(".incoming").join(safe(app, "app")).join(safe(version, "version"))
}

/// Streams the file to disk, hashing as it goes; returns its SHA-256.
async fn download(req: &InstallRequest, to: &Path, progress: &impl Fn(Progress)) -> Result<String, String> {
    let client = reqwest::Client::builder().user_agent("QuiverLauncher/4").build().map_err(text)?;
    let failed = |_| "The download failed. Check your connection and try again.".to_string();
    let token = req.github_token.as_deref().map(str::trim).filter(|t| !t.is_empty());
    let mut request = client.get(&req.url);
    if let Some(token) = token {
        request = request.bearer_auth(token).header(reqwest::header::ACCEPT, "application/octet-stream");
    }
    let mut response = request.send().await.map_err(failed)?;
    // GitHub refused the token itself: the browser link, without it, as before the token was saved.
    if token.is_some() && response.status() == reqwest::StatusCode::UNAUTHORIZED {
        if let Some(fallback) = &req.fallback_url {
            response = client.get(fallback).send().await.map_err(failed)?;
        }
    }
    if response.status() == reqwest::StatusCode::NOT_FOUND {
        // Its developer deleted it, or the release, after the site last read it.
        return Err(MISSING.into());
    }
    let status = response.status();
    if !status.is_success() {
        return Err(refused(status, response.url()));
    }
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
        || [".zip", ".7z", ".rar", ".msi", ".exe", ".bat", ".cmd", ".appimage", ".flatpak", ".apk", ".x86_64", ".sh"].iter().any(|end| lower.ends_with(end))
}

/// The name a file with no known extension should have, from its first bytes;
/// None when its name already says, or its contents don't (a bare program).
pub(crate) fn named_for_contents(name: &str, file: &Path) -> Option<String> {
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
/// a script, a Linux binary (ELF) or a Windows program that runs for no one
/// becomes runnable.
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

/// An AppImage, a script, a Linux binary or a Windows program, by its name or
/// its first bytes. A Windows program runs through Wine or Proton, which don't
/// need the bit themselves, but Steam only treats a shortcut's program as one
/// to run when it can be executed, so a game added to Steam does.
#[cfg(unix)]
fn is_program(path: &Path) -> bool {
    let name = path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    if [".appimage", ".x86_64", ".sh", ".exe", ".bat", ".cmd"].iter().any(|e| name.ends_with(e)) {
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
        #[cfg(unix)]
        let mut links = Vec::new();
        for i in 0..archive.len() {
            let mut entry = archive.by_index(i).map_err(text)?;
            let Some(inside) = enclosed(entry.name()) else { continue };
            // A zip made with the Mac's Compress keeps each file's Finder details in `__MACOSX`, which no app needs and
            // which keeps a lone wrapper folder from being hoisted.
            if inside.starts_with("__MACOSX") {
                continue;
            }
            let path = out.join(&inside);
            // A Mac app's frameworks link `Versions/Current`; as a copy, its signature no longer verifies.
            #[cfg(unix)]
            if entry.unix_mode().is_some_and(|mode| mode & 0o170000 == 0o120000) {
                let mut target = String::new();
                entry.read_to_string(&mut target).map_err(text)?;
                links.push((inside, target));
                continue;
            }
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
        // Made last, so no entry is written through a link. Only links that point down from their own folder, as a
        // framework's do: a chain of links with `..` can lead out of `out` even when each one seems to stay inside,
        // and one to `.` makes a folder that holds itself.
        #[cfg(unix)]
        for (inside, target) in links {
            let down = !target.is_empty() && !target.starts_with('/') && !target.contains('\0') && !target.split('/').any(|p| p == ".." || p == ".");
            let path = out.join(&inside);
            if !down || fs::symlink_metadata(&path).is_ok() {
                continue;
            }
            if let Some(parent) = path.parent() {
                fs::create_dir_all(parent).map_err(text)?;
            }
            std::os::unix::fs::symlink(&target, &path).map_err(text)?;
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
/// A Mac app (`Game.app`) is the program itself, not a wrapper: hoisting its
/// `Contents` leaves a folder macOS can't start.
pub(crate) fn flatten(dir: &Path) -> io::Result<()> {
    loop {
        let entries: Vec<_> = fs::read_dir(dir)?.collect::<Result<_, _>>()?;
        let [only] = entries.as_slice() else { return Ok(()) };
        let app = Path::new(&only.file_name()).extension().is_some_and(|e| e.eq_ignore_ascii_case("app"));
        if !only.file_type()?.is_dir() || app {
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
    let mut names = vec![VERSION_FILE, crate::flatpak::FILE, crate::msi::FILE, crate::msi::V3_FILE, crate::setup::FILE, crate::script::FILE, crate::mods::RECORD];
    #[cfg(target_os = "android")]
    names.push(crate::android::FILE);
    names
}

/// A name for a file added to an app's folder: a plain name, and never one of
/// the files that say how the app was installed.
fn added_file_name(name: &str) -> Result<&str, String> {
    let name = plain_name(name)?;
    let reserved = records().into_iter().chain(["version.txt"]).any(|r| r.eq_ignore_ascii_case(name));
    if reserved {
        return Err(format!("\"{name}\" is a file Quiver keeps for itself."));
    }
    Ok(name)
}

/// Those of `names` that are files in an app's folder.
pub fn files_present(dir: &Path, names: &[String]) -> Vec<String> {
    names.iter().filter(|name| plain_name(name).is_ok_and(|n| dir.join(n).is_file())).cloned().collect()
}

/// Makes the empty files `add` names in an app's folder, leaving any already
/// there, and takes away those `remove` names. A file with something in it is
/// more than a marker, so it stays: its name comes back. Every name is checked
/// before anything changes.
pub fn set_files(dir: &Path, add: &[String], remove: &[String]) -> Result<Vec<String>, String> {
    let add = add.iter().map(|n| added_file_name(n)).collect::<Result<Vec<_>, _>>()?;
    let remove = remove.iter().map(|n| added_file_name(n)).collect::<Result<Vec<_>, _>>()?;
    let mut kept = Vec::new();
    for name in remove {
        let path = dir.join(name);
        let Ok(meta) = fs::symlink_metadata(&path) else { continue };
        if meta.is_file() && meta.len() == 0 {
            writable(&path);
            fs::remove_file(&path).map_err(text)?;
        } else {
            kept.push(name.to_string());
        }
    }
    for name in add {
        let path = dir.join(name);
        if !path.exists() {
            fs::write(path, "").map_err(text)?;
        }
    }
    Ok(kept)
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
            // A folder of the player's own game linked in here (own_copy.rs) stays theirs: an update never writes
            // through the link into it.
            // (A link the app's own files made stays inside the app's folder, and is merged into as before.)
            if crate::own_copy::is_link(&dest) && !dest.canonicalize().is_ok_and(|t| to.canonicalize().is_ok_and(|root| t.starts_with(root))) {
                continue;
            }
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
pub(crate) const HELPERS: [&str; 10] = ["unins", "crashpad", "crashreport", "crashhandler", "crashsender", "crash_", "vc_redist", "dxsetup", "setup", "updater"];

/// The program to start: the first file matching the catalog's preferred
/// executables, else the likeliest program, shallowest first.
pub fn find_executable(dir: &Path, preferred: &[String], os: &str) -> Option<PathBuf> {
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    files.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    let name = |p: &PathBuf| p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    if let Some(hit) = preferred_in(dir, &files, preferred) {
        return Some(hit.clone());
    }
    let helper = |n: &str| HELPERS.iter().any(|h| n.starts_with(h));
    let linux = |p: &PathBuf, n: &str| n.ends_with(".appimage") || n.ends_with(".x86_64") || is_unix_binary(p);
    // A Windows build's helper script (a run.sh beside its .exe) isn't the
    // game: with no Linux program here, there's none, and Wine runs the .exe.
    let scripts = files.iter().any(|p| linux(p, &name(p))) || !files.iter().any(|p| name(p).ends_with(".exe") && !helper(&name(p)));
    let found = if os == "windows" {
        likeliest(dir, files.iter().filter(|p| name(p).ends_with(".exe") && !helper(&name(p))).collect())
    } else {
        files.iter().find(|p| {
            let n = name(p);
            let program = match os {
                "macos" => n.ends_with(".app"),
                _ => linux(p, &n) || (scripts && n.ends_with(".sh")),
            };
            program && !helper(&n)
        })
    };
    // With nothing better: a Windows build a script starts, a Mac build that's a bare program rather than an .app.
    found
        .or_else(|| {
            files.iter().find(|p| {
                let n = name(p);
                let program = match os {
                    "windows" => n.ends_with(".bat") || n.ends_with(".cmd"),
                    "macos" => is_unix_binary(p),
                    _ => false,
                };
                program && !helper(&n)
            })
        })
        // Its setup or updater, when that's the only program it has: some apps are started by theirs.
        .or_else(|| files.iter().filter(|_| os == "windows").find(|p| name(p).ends_with(".exe") && ONLY_PROGRAMS.iter().any(|h| name(p).starts_with(h))))
        .cloned()
}

/// The first file matching the catalog's preferred programs, in their order. A pattern with a folder in it
/// ("bin/Game.exe") is matched against the file's path in the app's folder, any other against its name.
fn preferred_in<'a>(dir: &Path, files: &'a [PathBuf], preferred: &[String]) -> Option<&'a PathBuf> {
    let relative = |p: &PathBuf| p.strip_prefix(dir).unwrap_or(p).to_string_lossy().replace('\\', "/").to_lowercase();
    let name = |p: &PathBuf| p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    preferred.iter().find_map(|pattern| {
        let pattern = pattern.replace('\\', "/").to_lowercase();
        let glob = glob::Pattern::new(&pattern).ok()?;
        let folder = pattern.contains('/');
        files.iter().find(|p| glob.matches(&if folder { relative(p) } else { name(p) }))
    })
}

/// Programs reported to the catalog at most, when none is its preferred one.
const REPORTED_PROGRAMS: usize = 16;

/// When the catalog names preferred programs and the app's folder has none of them: the programs it does have, by
/// their paths in the folder, shallowest first, for the catalog to learn the right one (`start-program` on the site).
/// None when the catalog names none, or one is there.
pub fn preferred_missed(dir: &Path, preferred: &[String], os: &str) -> Option<Vec<String>> {
    if preferred.is_empty() || !dir.is_dir() {
        return None;
    }
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    files.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    if preferred_in(dir, &files, preferred).is_some() {
        return None;
    }
    let program = |p: &PathBuf| {
        let n = p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
        match os {
            "windows" => n.ends_with(".exe") || n.ends_with(".bat") || n.ends_with(".cmd"),
            "macos" => n.ends_with(".app") || is_unix_binary(p),
            _ => n.ends_with(".appimage") || n.ends_with(".x86_64") || n.ends_with(".sh") || is_unix_binary(p),
        }
    };
    let programs: Vec<String> = files
        .iter()
        .filter(|p| program(p))
        .take(REPORTED_PROGRAMS)
        .map(|p| p.strip_prefix(dir).unwrap_or(p).to_string_lossy().replace('\\', "/"))
        .collect();
    Some(programs)
}

/// Helpers that start an app when it has no other program.
const ONLY_PROGRAMS: [&str; 2] = ["setup", "updater"];

/// Words in a program's name that say it's a helper of the app's, not what starts it: "Game Settings.exe",
/// "game-updater.exe", "GameBugReporter.exe".
const HELPER_WORDS: [&str; 40] = [
    "setup", "install", "installer", "uninstall", "uninstaller", "updater", "update", "settings", "config", "configuration", "configurator", "configuracion", "server", "dedicated",
    "editor", "report", "reporter", "tool", "tools", "extract", "extractor", "patcher", "benchmark", "diagnostics", "debug", "debugger", "dev", "test", "tests", "handler", "helper",
    "redist", "cli", "console", "headless", "backend", "viewer", "packer", "converter", "importer",
];
/// Programs of other makers that come along with an app.
const TOOLS: [&str; 22] = [
    "ffmpeg", "ffprobe", "ffplay", "7z", "7za", "7zr", "curl", "aria2c", "wget", "zip", "unzip", "createdump", "gh", "python", "pythonw", "cmake", "ninja", "git", "hactool", "premake5", "chdman",
    "dolphintool",
];
/// Words for an earlier build kept beside the current one: "Game (classic).exe".
const OLD_WORDS: [&str; 4] = ["classic", "legacy", "old", "deprecated"];
/// Words in a folder's name that say nothing about which program is its app's.
pub(crate) const COMMON_WORDS: [&str; 26] = [
    "recomp", "recompiled", "recompilation", "static", "port", "native", "the", "for", "and", "launcher", "game", "engine", "windows", "win64", "win32", "x64", "x86", "decomp", "project",
    "remastered", "enhanced", "open", "edition", "play", "start", "pc",
];

/// A name's words, lowercase and without accents: split at anything that isn't a letter or digit, and where a
/// capital follows a small letter ("KameoBugReporter" is kameo, bug, reporter).
pub(crate) fn words(name: &str) -> Vec<String> {
    let plain = |c: char| match c {
        'á' | 'à' | 'â' | 'ä' | 'ã' | 'å' => 'a',
        'é' | 'è' | 'ê' | 'ë' => 'e',
        'í' | 'ì' | 'î' | 'ï' => 'i',
        'ó' | 'ò' | 'ô' | 'ö' | 'õ' => 'o',
        'ú' | 'ù' | 'û' | 'ü' => 'u',
        'ñ' => 'n',
        'ç' => 'c',
        c => c,
    };
    let mut words = vec![String::new()];
    let mut before = ' ';
    for c in name.chars() {
        let small = c.to_lowercase().next().map_or(c, plain);
        if !small.is_alphanumeric() || (c.is_uppercase() && (before.is_lowercase() || before.is_ascii_digit())) {
            words.push(String::new());
        }
        if small.is_alphanumeric() {
            words.last_mut().unwrap().push(small);
        }
        before = c;
    }
    words.retain(|w| !w.is_empty());
    words
}

/// The program likeliest to start a Windows build, of those in its folder: the shallowest, and there one that isn't
/// a helper by its name, isn't an earlier build kept beside the current one, is the app's own launcher, or is named
/// most like the folder; then by name.
fn likeliest<'a>(dir: &Path, programs: Vec<&'a PathBuf>) -> Option<&'a PathBuf> {
    let stem = |p: &Path| p.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
    let named: Vec<Vec<String>> = programs.iter().map(|p| words(&stem(p))).collect();
    let folder = dir.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let folder_words: Vec<String> = words(&folder).into_iter().filter(|w| w.len() >= 4 && !COMMON_WORDS.contains(&w.as_str())).collect();
    let folder_name = words(&folder).concat();
    let rank = |i: usize| {
        let (path, words) = (programs[i], &named[i]);
        let whole = stem(path).to_lowercase();
        // "7za64" is 7za.
        let tool = whole.trim_end_matches("x64").trim_end_matches("x86").trim_end_matches("64").trim_end_matches("32");
        let helper = words.iter().any(|w| HELPER_WORDS.contains(&w.as_str()) || w.ends_with("tool")) || TOOLS.contains(&whole.as_str()) || TOOLS.contains(&tool);
        // Old only beside the same name without the word: a game called Classic is itself.
        let current: Vec<&String> = words.iter().filter(|w| !OLD_WORDS.contains(&w.as_str())).collect();
        let old = current.len() < words.len() && named.iter().enumerate().any(|(j, other)| j != i && other.iter().collect::<Vec<_>>() == current);
        let launcher = words.iter().any(|w| w.ends_with("launcher"));
        // "Play Game.exe" is named as Game.exe is.
        let core: String = words.iter().filter(|w| !["play", "start"].contains(&w.as_str())).map(String::as_str).collect();
        let whole_match = if core.len() >= 3 && folder_name.contains(&core) { core.len() } else { 0 };
        let alike = folder_words.iter().filter(|w| core.contains(w.as_str())).map(|w| w.len()).max().unwrap_or(0).max(whole_match);
        let depth = path.strip_prefix(dir).map_or(0, |rel| rel.components().count());
        (depth, helper, old, !launcher, std::cmp::Reverse(alike), path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default())
    };
    (0..programs.len()).min_by_key(|&i| rank(i)).map(|i| programs[i])
}

/// The kinds of file in an app's folder (their extensions, never their names), for an error report to say what was
/// there when no program was found.
pub fn file_kinds(dir: &Path) -> Vec<String> {
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    let kinds: std::collections::BTreeSet<String> = files
        .iter()
        .filter_map(|p| p.extension())
        .map(|e| e.to_string_lossy().to_lowercase())
        .filter(|e| e.len() <= 8 && e.chars().all(|c| c.is_ascii_alphanumeric()))
        .collect();
    kinds.into_iter().take(20).collect()
}

/// Every program in an app's folder the player might start: its own, its
/// launcher, its setup or updater (an update can ask for those again), but
/// not an uninstaller or a runtime's installer. Shallowest first, then by name;
/// a Windows build's too on Linux, where Wine runs it.
pub fn programs(dir: &Path, os: &str) -> Vec<PathBuf> {
    const NOT_PROGRAMS: [&str; 8] = ["unins", "crashpad", "crashreport", "crashhandler", "crashsender", "crash_", "vc_redist", "dxsetup"];
    let mut files = Vec::new();
    walk(dir, 0, &mut files);
    files.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    files
        .into_iter()
        .filter(|p| {
            let n = p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
            let windows = n.ends_with(".exe") || n.ends_with(".bat") || n.ends_with(".cmd");
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

    /// A one-request HTTP server on this computer: what `answer` says to the request it gets, which goes to `seen`.
    fn serve(answer: impl FnOnce(&str) -> String + Send + 'static) -> (u16, std::sync::mpsc::Receiver<String>) {
        use std::io::{Read, Write};
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let port = listener.local_addr().unwrap().port();
        let (seen, heard) = std::sync::mpsc::channel();
        std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            let mut buf = [0u8; 8192];
            let n = stream.read(&mut buf).unwrap();
            let request = String::from_utf8_lossy(&buf[..n]).to_lowercase();
            stream.write_all(answer(&request).as_bytes()).unwrap();
            seen.send(request).unwrap();
        });
        (port, heard)
    }

    fn fetch(url: String, github_token: Option<&str>, fallback_url: Option<String>) -> Result<String, String> {
        let req = InstallRequest {
            id: "github:quiver/private".into(),
            url,
            filename: "game.zip".into(),
            checksum: None,
            folder: "private".into(),
            dir: None,
            into: None,
            files_to_add: vec![],
            version: "v1.0.0".into(),
            preferred: vec![],
            program: None,
            github_token: github_token.map(Into::into),
            fallback_url,
            cache: None,
        };
        let dir = tempfile::tempdir().unwrap();
        tauri::async_runtime::block_on(download(&req, &dir.path().join("game.zip"), &|_| {}))
    }

    #[test]
    fn a_cache_fetch_comes_in_named_for_its_app_version_and_file() {
        let cache = Path::new("/cache");
        assert_eq!(incoming_dir(cache, "ship-of-harkinian", "9.1.2"), cache.join(".incoming").join("ship-of-harkinian").join("9.1.2"));
        // Never out of the cache's folder, nor a name Windows refuses.
        assert_eq!(incoming_dir(cache, "../..", "v1/../../x"), cache.join(".incoming").join("_").join("v1_.._.._x"));
        assert_eq!(incoming_dir(cache, "", "CON"), cache.join(".incoming").join("app").join("version"));
    }

    #[test]
    fn a_cache_fetch_says_its_file_was_blocked_when_it_went_from_under_it() {
        let tmp = tempfile::tempdir().unwrap();
        let cache = tmp.path().join("cache");
        let body = "flagged by an antivirus";
        let checksum = format!("sha256:{}", hex::encode(Sha256::digest(body.as_bytes())));
        let watched = incoming_dir(&cache, "bad-app", "1.0").join("Bad-Windows.zip");
        let (port, _) = serve(move |_| format!("HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}", body.len()));
        let req = InstallRequest {
            id: "bad".into(),
            url: format!("http://127.0.0.1:{port}/Bad-Windows.zip"),
            filename: "Bad-Windows.zip".into(),
            checksum: Some(checksum),
            folder: "bad-app".into(),
            dir: None,
            into: None,
            files_to_add: vec![],
            version: "1.0".into(),
            preferred: vec![],
            program: None,
            github_token: None,
            fallback_url: None,
            cache: Some(crate::cache::CacheUse { dir: cache.to_string_lossy().into(), keep: true, app: "bad".into(), version: "1.0".into(), ..Default::default() }),
        };
        let (_cancel, cancelled) = tokio::sync::watch::channel(false);
        // As Windows Defender does: the file goes as soon as it's written, from where it's named for its release.
        let result = tauri::async_runtime::block_on(fetch_to_cache(req, cancelled, |p| {
            if p.received > 0 {
                fs::remove_file(&watched).expect("it came in named for its app, version and file");
            }
        }));
        assert_eq!(result, Err(BLOCKED.to_string()));
        assert_eq!(crate::cache::status(&cache, &Default::default()).files, 0);
        // Nothing left behind for the next fetch.
        assert!(!cache.join(".incoming").join("bad-app").exists());
    }

    #[test]
    fn a_kept_file_installs_again_from_the_download_cache_without_downloading_it() {
        let tmp = tempfile::tempdir().unwrap();
        let (apps, cache) = (tmp.path().join("apps"), tmp.path().join("cache"));
        let body = "the app's notes";
        let checksum = format!("sha256:{}", hex::encode(Sha256::digest(body.as_bytes())));
        let request = |url: String, keep: bool| InstallRequest {
            id: "cached-app".into(),
            url,
            filename: "notes.txt".into(),
            checksum: Some(checksum.clone()),
            folder: "cached-app".into(),
            dir: None,
            into: None,
            files_to_add: vec![],
            version: "1.0".into(),
            preferred: vec![],
            program: None,
            github_token: None,
            fallback_url: None,
            cache: Some(crate::cache::CacheUse { dir: cache.to_string_lossy().into(), keep, app: "cached-app".into(), version: "1.0".into(), ..Default::default() }),
        };
        let run = |req: InstallRequest| {
            let (_cancel, cancelled) = tokio::sync::watch::channel(false);
            tauri::async_runtime::block_on(install(&apps, req, cancelled, |_| {}))
        };
        let (port, _) = serve(move |_| format!("HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}", body.len()));
        run(request(format!("http://127.0.0.1:{port}/notes.txt"), true)).unwrap();
        assert!(crate::cache::has(&cache, &[checksum.clone()])[0], "kept once downloaded");
        fs::remove_dir_all(apps.join("cached-app")).unwrap();
        // Nothing listens there now: only the cache can have it.
        let installed = run(request(format!("http://127.0.0.1:{port}/notes.txt"), false)).unwrap();
        assert_eq!(installed.checksum, checksum);
        assert_eq!(fs::read_to_string(apps.join("cached-app").join("notes.txt")).unwrap(), body);
        // Without the cache, the same request fails to download.
        let mut plain = request(format!("http://127.0.0.1:{port}/notes.txt"), false);
        plain.cache = None;
        fs::remove_dir_all(apps.join("cached-app")).unwrap();
        assert!(run(plain).is_err());
    }

    #[test]
    fn a_private_repositorys_file_comes_through_githubs_api_with_the_token_which_its_redirect_leaves_behind() {
        let (file_port, file_saw) = serve(|_| "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello".into());
        // As GitHub's API: the file for the token, else as if there were no such repository.
        let (api_port, api_saw) = serve(move |request| {
            if request.contains("authorization: bearer secret") && request.contains("accept: application/octet-stream") {
                format!("HTTP/1.1 302 Found\r\nLocation: http://localhost:{file_port}/file\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")
            } else {
                "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".into()
            }
        });
        let sha = fetch(format!("http://127.0.0.1:{api_port}/repos/quiver/private/releases/assets/7"), Some(" secret "), None);
        assert_eq!(sha.unwrap(), hex::encode(Sha256::digest(b"hello")));
        assert!(api_saw.recv().unwrap().contains("authorization: bearer secret"));
        assert!(!file_saw.recv().unwrap().contains("authorization"), "the token stays with GitHub's API");
    }

    #[test]
    fn without_a_token_a_private_repositorys_file_is_missing() {
        let (api_port, _) = serve(|_| "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".into());
        assert_eq!(fetch(format!("http://127.0.0.1:{api_port}/repos/quiver/private/releases/assets/7"), None, None), Err(MISSING.into()));
    }

    #[test]
    fn a_token_github_refuses_falls_back_to_the_browser_link_without_it() {
        let (file_port, file_saw) = serve(|_| "HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello".into());
        // As GitHub's API for an expired token: Bad credentials.
        let (api_port, _) = serve(|_| "HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".into());
        let sha = fetch(format!("http://127.0.0.1:{api_port}/repos/quiver/public/releases/assets/7"), Some("expired"), Some(format!("http://127.0.0.1:{file_port}/game.zip")));
        assert_eq!(sha.unwrap(), hex::encode(Sha256::digest(b"hello")));
        assert!(!file_saw.recv().unwrap().contains("authorization"), "the browser link goes without the token");
    }

    #[test]
    fn a_refused_download_says_who_and_what_to_do_without_the_address() {
        let release = reqwest::Url::parse("https://github.com/HarbourMasters/SpaghettiKart/releases/download/1.0.0/spaghetti-windows.zip").unwrap();
        assert_eq!(refused(reqwest::StatusCode::GATEWAY_TIMEOUT, &release), "GitHub isn't answering right now (error 504). Try again in a few minutes.");
        assert_eq!(refused(reqwest::StatusCode::TOO_MANY_REQUESTS, &release), "GitHub is limiting downloads right now (error 429). Try again in a few minutes.");
        let other = reqwest::Url::parse("https://www.example.org/game.zip").unwrap();
        assert_eq!(refused(reqwest::StatusCode::GONE, &other), "example.org wouldn't send the file (error 410).");
    }

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
        assert!(!may_remove(&own));
        let ours = apps.path().join("Ours");
        fs::create_dir(&ours).unwrap();
        fs::write(ours.join("game.exe"), "").unwrap();
        fs::write(ours.join(VERSION_FILE), "1.0").unwrap();
        assert!(may_install_into(&ours) && may_remove(&ours));
    }

    #[test]
    fn a_quiver_3_install_is_quivers_however_the_launcher_knows_it() {
        // In the apps folder as one of its own (no path of its own on record), as moving 3.x's apps in leaves them.
        let apps = tempfile::tempdir().unwrap();
        let old = apps.path().join("AeroGauge-AeroGaugeRecompiled");
        fs::create_dir(&old).unwrap();
        fs::write(old.join("game.exe"), "").unwrap();
        fs::write(old.join("LastPlayed.txt"), "").unwrap();
        fs::write(old.join("version.txt"), "v0.7.0").unwrap();
        assert!(may_remove(&old), "uninstalling removes its files");
        assert!(may_install_into(&old), "installing again goes over it");
        // Only while it still says so: without 3.x's version file, it could be anyone's.
        fs::remove_file(old.join("version.txt")).unwrap();
        assert!(!may_remove(&old) && !may_install_into(&old));
    }

    #[test]
    fn a_removal_cut_short_still_leaves_quivers_folder() {
        let apps = tempfile::tempdir().unwrap();
        let dir = apps.path().join("Game");
        fs::create_dir(&dir).unwrap();
        fs::write(dir.join("game.exe"), "").unwrap();
        fs::write(dir.join(VERSION_FILE), "v1.2").unwrap();
        let failed = remove_install(&dir, |d| {
            fs::remove_file(d.join(VERSION_FILE))?;
            Err(io::ErrorKind::PermissionDenied.into())
        });
        assert_eq!(failed.unwrap_err().kind(), io::ErrorKind::PermissionDenied);
        assert_eq!(fs::read_to_string(dir.join(VERSION_FILE)).unwrap(), "v1.2");
        assert!(may_install_into(&dir) && may_remove(&dir));
    }

    #[test]
    fn a_removal_cut_short_still_leaves_an_adopted_install() {
        let apps = tempfile::tempdir().unwrap();
        let dir = apps.path().join("Old");
        fs::create_dir(&dir).unwrap();
        fs::write(dir.join("game.exe"), "").unwrap();
        fs::write(dir.join("version.txt"), "v1.0").unwrap();
        let failed = remove_install(&dir, |d| {
            fs::remove_file(d.join("version.txt"))?;
            Err(io::ErrorKind::PermissionDenied.into())
        });
        assert!(failed.is_err());
        assert!(may_remove(&dir));
        assert!(!dir.join(VERSION_FILE).exists(), "still not one Quiver installed");
    }

    #[test]
    fn a_file_gone_during_removal_is_no_failure() {
        let apps = tempfile::tempdir().unwrap();
        let dir = apps.path().join("Game");
        fs::create_dir(&dir).unwrap();
        fs::write(dir.join(VERSION_FILE), "v1.2").unwrap();
        let tries = std::cell::Cell::new(0);
        let removed = remove_install(&dir, |d| {
            tries.set(tries.get() + 1);
            if tries.get() == 1 {
                return Err(io::ErrorKind::NotFound.into());
            }
            fs::remove_dir_all(d)
        });
        assert!(removed.is_ok());
        assert_eq!(tries.get(), 2);
        assert!(!dir.exists());
        // The folder itself gone by the time it's removed: it's what was asked for.
        assert!(remove_install(&dir, |d| fs::remove_dir_all(d)).is_ok());
    }

    #[test]
    #[cfg(windows)]
    fn a_file_in_use_keeps_the_folder_quivers() {
        use std::os::windows::fs::OpenOptionsExt;
        let apps = tempfile::tempdir().unwrap();
        let dir = apps.path().join("Game");
        fs::create_dir(&dir).unwrap();
        fs::write(dir.join(VERSION_FILE), "v1.2").unwrap();
        fs::write(dir.join("game.exe"), "").unwrap();
        // Open with no sharing, as a running game or a scanner can hold it.
        let held = fs::OpenOptions::new().read(true).share_mode(0).open(dir.join("game.exe")).unwrap();
        assert!(remove_install(&dir, |d| fs::remove_dir_all(d)).is_err());
        assert!(may_install_into(&dir) && may_remove(&dir));
        drop(held);
        // Once it's let go, removing it again finishes the job.
        assert!(remove_install(&dir, |d| fs::remove_dir_all(d)).is_ok());
        assert!(!dir.exists());
    }

    #[test]
    fn adds_empty_files_and_takes_away_only_empty_ones() {
        let dir = tempfile::tempdir().unwrap();
        let names = |list: &[&str]| list.iter().map(|n| n.to_string()).collect::<Vec<_>>();
        assert!(set_files(dir.path(), &names(&["portable.txt", " config.ini "]), &[]).unwrap().is_empty());
        assert_eq!(fs::metadata(dir.path().join("portable.txt")).unwrap().len(), 0);
        assert!(dir.path().join("config.ini").is_file(), "its name is trimmed");
        assert_eq!(files_present(dir.path(), &names(&["portable.txt", "gone.txt", "../portable.txt"])), ["portable.txt"]);

        // A file already there keeps what's in it.
        fs::write(dir.path().join("config.ini"), "volume=3").unwrap();
        set_files(dir.path(), &names(&["config.ini"]), &[]).unwrap();
        assert_eq!(fs::read_to_string(dir.path().join("config.ini")).unwrap(), "volume=3");

        // Only what's empty goes; what isn't is named, and one that isn't there is fine.
        let kept = set_files(dir.path(), &[], &names(&["portable.txt", "config.ini", "gone.txt"])).unwrap();
        assert_eq!(kept, ["config.ini"]);
        assert!(!dir.path().join("portable.txt").exists());
        assert!(dir.path().join("config.ini").exists());
    }

    #[test]
    fn a_bad_name_for_an_added_file_changes_nothing() {
        let dir = tempfile::tempdir().unwrap();
        let keep = dir.path().join("keep.txt");
        fs::write(&keep, "").unwrap();
        for bad in ["../up.txt", "a/b.txt", "NUL", VERSION_FILE, "VERSION.TXT", crate::msi::FILE] {
            let remove = ["keep.txt".to_string()];
            assert!(set_files(dir.path(), &[bad.to_string()], &remove).is_err(), "{bad}");
            assert!(keep.exists(), "nothing was taken away for {bad}");
            assert!(set_files(dir.path(), &[], &[bad.to_string()]).is_err(), "{bad}");
        }
        assert!(!dir.path().parent().unwrap().join("up.txt").exists());
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
    fn tells_when_the_catalogs_program_is_missing() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["Game.exe", "bin/tool.exe", "readme.txt", "bin/Game-1.2.exe"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        let missed = |preferred: &[&str]| preferred_missed(dir.path(), &preferred.iter().map(|p| p.to_string()).collect::<Vec<_>>(), "windows");
        assert_eq!(missed(&[]), None);
        assert_eq!(missed(&["game.exe"]), None);
        assert_eq!(missed(&["Gone.exe", "bin/Game-*.exe"]), None);
        assert_eq!(missed(&["Gone.exe"]), Some(vec!["Game.exe".into(), "bin/Game-1.2.exe".into(), "bin/tool.exe".into()]));
        // A pattern with a folder is matched against the path.
        assert_eq!(find_executable(dir.path(), &["BIN/game-*.exe".into()], "windows"), Some(dir.path().join("bin/Game-1.2.exe")));
        assert_eq!(missed(&["tool.exe"]), None);
    }

    #[test]
    fn picks_the_game_over_the_helpers_beside_it() {
        let pick = |folder: &str, files: &[&str]| {
            let root = tempfile::tempdir().unwrap();
            let dir = root.path().join(folder);
            for f in files {
                let p = dir.join(f);
                fs::create_dir_all(p.parent().unwrap()).unwrap();
                fs::write(p, "").unwrap();
            }
            find_executable(&dir, &[], "windows").map(|p| p.strip_prefix(&dir).unwrap().to_string_lossy().replace('\\', "/"))
        };
        let picks = |folder: &str, files: &[&str], want: &str| assert_eq!(pick(folder, files).as_deref(), Some(want), "{files:?}");
        // A helper by a word anywhere in its name, however it's spelled.
        picks("Savage Wind", &["Savage Wind Configuration.exe", "savagewind.exe"], "savagewind.exe");
        picks("Strikers", &["strikers-settings.exe", "strikers.exe"], "strikers.exe");
        picks("sm64coopdx", &["coopdx_updater.exe", "sm64coopdx.exe"], "sm64coopdx.exe");
        picks("PT", &["pt-updater.exe", "pt.exe"], "pt.exe");
        picks("Kameo", &["KameoBugReporter.exe", "kameorepowered.exe"], "kameorepowered.exe");
        picks("RedDeadRevolver", &["Configuración.exe", "RedDeadRevolver.exe"], "RedDeadRevolver.exe");
        picks("Tenkaichi3", &["Tenkaichi3Decomp-setup.exe", "Tenkaichi3Decomp.exe"], "Tenkaichi3Decomp.exe");
        picks("DDNet", &["DDNet-Server.exe", "DDNet.exe"], "DDNet.exe");
        picks("SSNE", &["gctool.exe", "SSNE-Native.exe"], "SSNE-Native.exe");
        // A game named Crash is no crash reporter.
        picks("CrashOfTheTitans", &["crash-launcher.exe", "crashpad_handler.exe", "uninstall.exe", "builder-pack/toolchain/python/python.exe"], "crash-launcher.exe");
        // Another maker's program that came along.
        picks("Arena Evolved", &["ffmpeg.exe", "halo.exe"], "halo.exe");
        picks("MegaGlest", &["7z.exe", "7za64.exe", "megaglestx64.exe"], "megaglestx64.exe");
        // An earlier build kept beside the current one; a game named Classic is itself.
        picks("Infamous", &["inFAMOUS Second Son (classic).exe", "inFAMOUS Second Son.exe"], "inFAMOUS Second Son.exe");
        picks("Games", &["Sonic Classic.exe", "Tracker.exe"], "Sonic Classic.exe");
        // The app's own launcher, then the program named like its folder.
        picks("Wet", &["extract-xiso.exe", "wet.exe", "WetLauncher.exe"], "WetLauncher.exe");
        picks("zamn", &["zamn.exe", "zamn_launcher.exe"], "zamn_launcher.exe");
        picks("TimeSplitters-Recomp", &["Prepare Xbox Files.exe", "timesplitters.exe"], "timesplitters.exe");
        picks("StrikeCollectionHD", &["hdpack_xbrz.exe", "StrikeCollectionHD.exe"], "StrikeCollectionHD.exe");
        picks("Game", &["Play Game.exe", "The Game.exe"], "Play Game.exe");
        // Depth still comes first: a setup at the top is the first step, whatever tools are under it.
        picks("MP4", &["MP4-Setup.exe", "bin/mp4_xlate.exe", "llvm/bin/clang.exe"], "MP4-Setup.exe");
        // A helper is still the program when it's the only one, and a script comes before a bare setup.
        picks("Tools", &["Game Editor.exe"], "Game Editor.exe");
        picks("SMT", &["Updater.exe", "data.pak"], "Updater.exe");
        picks("Infamous 2", &["Setup.exe", "Play inFAMOUS 2.cmd"], "Play inFAMOUS 2.cmd");
        assert_eq!(pick("None", &["unins000.exe", "vc_redist.x64.exe"]), None);
    }

    #[test]
    fn splits_a_name_into_its_words() {
        assert_eq!(words("KameoBugReporter"), ["kameo", "bug", "reporter"]);
        assert_eq!(words("SM3Launcher v2"), ["sm3", "launcher", "v2"]);
        assert_eq!(words("Configuración_del-juego"), ["configuracion", "del", "juego"]);
        assert_eq!(words("inFAMOUS"), ["in", "famous"]);
    }

    #[test]
    fn a_script_starts_a_windows_build_with_no_program_of_its_own() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["setup.bat", "bin/Start Game.bat", "bin/data.pak"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        assert_eq!(find_executable(dir.path(), &[], "windows"), Some(dir.path().join("bin/Start Game.bat")));
        // A program comes first, deeper or not.
        fs::create_dir_all(dir.path().join("bin/x64")).unwrap();
        fs::write(dir.path().join("bin/x64/Game.exe"), "").unwrap();
        assert_eq!(find_executable(dir.path(), &[], "windows"), Some(dir.path().join("bin/x64/Game.exe")));
        // A folder of only a helper still has none.
        let helpers = tempfile::tempdir().unwrap();
        fs::write(helpers.path().join("setup.bat"), "").unwrap();
        assert_eq!(find_executable(helpers.path(), &[], "windows"), None);
    }

    #[test]
    #[cfg(unix)]
    fn a_bare_program_starts_a_mac_build_with_no_app() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        let game = dir.path().join("sm64coopdx");
        fs::write(&game, "").unwrap();
        fs::write(dir.path().join("readme"), "").unwrap();
        fs::set_permissions(&game, fs::Permissions::from_mode(0o755)).unwrap();
        assert_eq!(find_executable(dir.path(), &[], "macos"), Some(game));
        // An .app comes first.
        fs::create_dir(dir.path().join("Game.app")).unwrap();
        assert_eq!(find_executable(dir.path(), &[], "macos"), Some(dir.path().join("Game.app")));
    }

    #[test]
    fn says_what_kinds_of_file_a_folder_has_but_not_their_names() {
        let dir = tempfile::tempdir().unwrap();
        for f in ["My Secret Game.PAK", "bin/a.dll", "bin/b.dll", "notes", ".quiver-version", "odd.has space"] {
            let p = dir.path().join(f);
            fs::create_dir_all(p.parent().unwrap()).unwrap();
            fs::write(p, "").unwrap();
        }
        assert_eq!(file_kinds(dir.path()), ["dll", "pak"]);
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
        // A folder marked 0644 and a Linux binary, a Windows program and their
        // data marked 0644, as the Uncharted port's tar and a game's zip had.
        let mut tar = tar::Builder::new(Vec::new());
        let mut folder = tar::Header::new_gnu();
        folder.set_entry_type(tar::EntryType::Directory);
        folder.set_size(0);
        folder.set_mode(0o644);
        folder.set_cksum();
        tar.append_data(&mut folder, "analysis/", io::empty()).unwrap();
        for (path, body) in [("analysis/game", &b"\x7fELF\x02\x01"[..]), ("analysis/Game.exe", &b"MZ\x90\x00"[..]), ("analysis/level.dat", &b"level"[..])] {
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
        assert_eq!(mode("analysis/Game.exe"), 0o755, "Steam can run the Windows program");
        assert_eq!(mode("analysis/level.dat"), 0o644, "data stays data");
    }

    #[test]
    fn a_release_that_is_a_script_installs_as_the_apps_program() {
        let dir = tempfile::tempdir().unwrap();
        let file = dir.path().join("download");
        fs::write(&file, "<# : GT5-Installer.bat\r\n@echo off\r\n").unwrap();
        let out = dir.path().join("GT5");
        extract(&file, "GT5-Installer.bat", &out).unwrap();
        flatten(&out).unwrap();
        assert_eq!(find_executable(&out, &[], "windows"), Some(out.join("GT5-Installer.bat")));
        assert_eq!(programs(&out, "windows"), [out.join("GT5-Installer.bat")]);
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
        assert_eq!(named("GT5-Installer.bat", b"MZ"), None);
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

    #[test]
    fn a_zipped_mac_app_stays_an_app() {
        for top in ["", "Game-v1/"] {
            let dir = tempfile::tempdir().unwrap();
            let zip = {
                let mut w = zip::ZipWriter::new(io::Cursor::new(Vec::new()));
                for name in ["Game.app/Contents/Info.plist", "Game.app/Contents/MacOS/game"] {
                    w.start_file(format!("{top}{name}"), zip::write::SimpleFileOptions::default().unix_permissions(0o755)).unwrap();
                    io::Write::write_all(&mut w, b"game").unwrap();
                }
                // As the Mac's Compress makes it.
                w.start_file(format!("__MACOSX/{top}Game.app/Contents/._Info.plist"), zip::write::SimpleFileOptions::default()).unwrap();
                io::Write::write_all(&mut w, b"finder").unwrap();
                w.finish().unwrap().into_inner()
            };
            let file = dir.path().join("Game.zip");
            fs::write(&file, zip).unwrap();
            let out = dir.path().join("out");
            extract(&file, "Game.zip", &out).unwrap();
            flatten(&out).unwrap();
            assert!(out.join("Game.app/Contents/MacOS/game").is_file(), "{top}");
            assert!(!out.join("__MACOSX").exists(), "{top}");
            assert_eq!(find_executable(&out, &[], "macos"), Some(out.join("Game.app")), "{top}");
        }
    }

    #[cfg(unix)]
    #[test]
    fn a_zipped_mac_app_keeps_its_frameworks_links_but_none_that_lead_out() {
        let dir = tempfile::tempdir().unwrap();
        let framework = "Game.app/Contents/Frameworks/Engine.framework";
        let zip = {
            let mut w = zip::ZipWriter::new(io::Cursor::new(Vec::new()));
            let options = zip::write::SimpleFileOptions::default();
            w.start_file(format!("{framework}/Versions/A/Engine"), options).unwrap();
            io::Write::write_all(&mut w, b"engine").unwrap();
            w.add_symlink(format!("{framework}/Versions/Current"), "A", options).unwrap();
            w.add_symlink(format!("{framework}/Engine"), "Versions/Current/Engine", options).unwrap();
            // Each `..` seems to stay inside, but together they lead out of the folder.
            w.add_symlink("Game.app/Contents/Frameworks/up", "..", options).unwrap();
            w.add_symlink("Game.app/Contents/out", "Frameworks/up/../../..", options).unwrap();
            w.add_symlink("Game.app/etc", "/etc", options).unwrap();
            // A folder that holds itself.
            w.add_symlink("Game.app/Contents/self", "./", options).unwrap();
            // A link to where a later file goes isn't written through.
            w.add_symlink("Game.app/data", "Contents", options).unwrap();
            w.start_file("Game.app/data/save.bin", options).unwrap();
            io::Write::write_all(&mut w, b"save").unwrap();
            w.finish().unwrap().into_inner()
        };
        let file = dir.path().join("Game.zip");
        fs::write(&file, zip).unwrap();
        let out = dir.path().join("out");
        extract(&file, "Game.zip", &out).unwrap();
        let link = |p: &str| fs::symlink_metadata(out.join(p)).is_ok_and(|m| m.file_type().is_symlink());
        assert!(link(&format!("{framework}/Versions/Current")) && link(&format!("{framework}/Engine")));
        assert_eq!(fs::read(out.join(format!("{framework}/Engine"))).unwrap(), b"engine");
        for refused in ["Game.app/Contents/Frameworks/up", "Game.app/Contents/out", "Game.app/etc", "Game.app/Contents/self"] {
            assert!(fs::symlink_metadata(out.join(refused)).is_err(), "{refused}");
        }
        assert!(!link("Game.app/data") && out.join("Game.app/data/save.bin").is_file());
        assert!(!out.join("Game.app/Contents/save.bin").exists());
    }
}
