//! The launcher updating itself from its GitHub releases. Each kind of copy
//! updates its own way: the Windows installer runs the new installer, a
//! portable Windows copy swaps its exe, an AppImage replaces itself, the
//! release page's Flatpak has the system's flatpak install the new bundle,
//! and on Android the system's package installer installs the new APK.
//! Others (macOS, a Flatpak from Flathub, distro packages) are pointed at the
//! release page.
//! Every download is checked against the release's minisign signature, and
//! the launcher's saved files are backed up first.
use serde::{Deserialize, Serialize};
use std::cmp::Ordering;
use std::fs;
use std::io::{self, Read};
use std::path::{Path, PathBuf};

pub const PORTABLE_ZIP: &str = "QuiverLauncher-win-Portable.zip";
const API: &str = "https://api.github.com/repos/tgeorgiadis/quiver-launcher/releases?per_page=20";
const KEEP_BACKUPS: usize = 10;

#[derive(Deserialize, Clone)]
pub struct Release {
    pub tag_name: String,
    #[serde(default)]
    pub body: Option<String>,
    #[serde(default)]
    pub prerelease: bool,
    #[serde(default)]
    pub draft: bool,
    pub html_url: String,
    #[serde(default)]
    pub assets: Vec<Asset>,
}

#[derive(Deserialize, Clone)]
pub struct Asset {
    pub name: String,
    pub browser_download_url: String,
}

/// How this copy of the launcher was installed, which decides how it updates.
#[derive(Debug, PartialEq)]
pub enum Kind {
    /// The Windows installer's copy: the new installer updates it.
    Installer,
    /// A portable Windows copy (`portable.txt` beside the exe): the exe is swapped.
    Portable(PathBuf),
    /// An AppImage, which replaces itself.
    AppImage(PathBuf),
    /// Android: the new APK is installed over this one.
    Apk,
    /// The Flatpak from the release page: the system's flatpak installs the
    /// new bundle over it. Its manifest sets QUIVER_FLATPAK_BUNDLE; a
    /// Flatpak from a repository, such as Flathub's, updates from there.
    Flatpak,
    /// macOS, another Flatpak or a distro package: updated from the release page or the package manager.
    Elsewhere,
}

pub fn kind() -> Kind {
    if cfg!(target_os = "android") {
        return Kind::Apk;
    }
    if crate::sandbox::id().is_some() {
        return if std::env::var_os("QUIVER_FLATPAK_BUNDLE").is_some() { Kind::Flatpak } else { Kind::Elsewhere };
    }
    if let Some(image) = std::env::var_os("APPIMAGE").map(PathBuf::from).filter(|p| p.is_file()) {
        return Kind::AppImage(image);
    }
    if cfg!(windows) {
        if let Ok(exe) = std::env::current_exe() {
            if exe.parent().is_some_and(|d| d.join("portable.txt").exists()) {
                return Kind::Portable(exe);
            }
        }
        return Kind::Installer;
    }
    Kind::Elsewhere
}

/// The file in a release that updates this kind of copy.
pub fn asset<'a>(release: &'a Release, kind: &Kind) -> Option<&'a Asset> {
    let arch = if cfg!(target_arch = "aarch64") { ["aarch64", "arm64"] } else { ["x86_64", "amd64"] };
    release.assets.iter().find(|a| {
        let name = a.name.to_lowercase();
        match kind {
            Kind::Portable(_) => a.name == PORTABLE_ZIP,
            Kind::Installer => name.ends_with("-setup.exe"),
            Kind::AppImage(_) => name.ends_with(".appimage") && arch.iter().any(|x| name.contains(x)),
            Kind::Apk => name.ends_with(".apk") && arch.iter().any(|x| name.contains(x)),
            Kind::Flatpak => name.ends_with(".flatpak") && arch.iter().any(|x| name.contains(x)),
            Kind::Elsewhere => false,
        }
    })
}

/// Compares versions such as `4.0.1` and `4.0.0-alpha.2` the semver way: a release beats its previews.
pub fn compare(a: &str, b: &str) -> Ordering {
    let split = |v: &str| {
        let v = v.trim_start_matches('v');
        let v = v.split('+').next().unwrap_or(v);
        let (core, pre) = v.split_once('-').map_or((v, None), |(c, p)| (c, Some(p.to_string())));
        let core: Vec<u64> = core.split('.').map(|n| n.parse().unwrap_or(0)).collect();
        (core, pre)
    };
    let ((ca, pa), (cb, pb)) = (split(a), split(b));
    let len = ca.len().max(cb.len());
    for i in 0..len {
        match ca.get(i).unwrap_or(&0).cmp(cb.get(i).unwrap_or(&0)) {
            Ordering::Equal => {}
            other => return other,
        }
    }
    match (pa, pb) {
        (None, None) => Ordering::Equal,
        (None, Some(_)) => Ordering::Greater,
        (Some(_), None) => Ordering::Less,
        (Some(pa), Some(pb)) => {
            let (ia, ib): (Vec<&str>, Vec<&str>) = (pa.split('.').collect(), pb.split('.').collect());
            for (x, y) in ia.iter().zip(&ib) {
                let order = match (x.parse::<u64>(), y.parse::<u64>()) {
                    (Ok(x), Ok(y)) => x.cmp(&y),
                    (Ok(_), Err(_)) => Ordering::Less,
                    (Err(_), Ok(_)) => Ordering::Greater,
                    _ => x.cmp(y),
                };
                if order != Ordering::Equal {
                    return order;
                }
            }
            ia.len().cmp(&ib.len())
        }
    }
}

/// The newest release newer than `current`. Previews count when the player
/// asked for them, or when they're already on one.
pub fn newest<'a>(releases: &'a [Release], current: &str, preview: bool) -> Option<&'a Release> {
    let preview = preview || current.contains('-');
    releases
        .iter()
        .filter(|r| !r.draft && (preview || !r.prerelease) && compare(&r.tag_name, current) == Ordering::Greater)
        .max_by(|a, b| compare(&a.tag_name, &b.tag_name))
}

pub async fn releases() -> Result<Vec<Release>, String> {
    let url = std::env::var("QUIVER_UPDATE_API").unwrap_or_else(|_| API.into());
    let response = client()?.get(url).send().await.and_then(|r| r.error_for_status()).map_err(|e| format!("Couldn't check for updates: {e}"))?;
    let bytes = response.bytes().await.map_err(|e| format!("Couldn't check for updates: {e}"))?;
    serde_json::from_slice(&bytes).map_err(|e| format!("Couldn't check for updates: {e}"))
}

fn client() -> Result<reqwest::Client, String> {
    reqwest::Client::builder().user_agent("QuiverLauncher").build().map_err(|e| e.to_string())
}

pub async fn download(url: &str, progress: impl Fn(u64, Option<u64>)) -> Result<Vec<u8>, String> {
    use futures_util::StreamExt;
    let response = client()?.get(url).send().await.and_then(|r| r.error_for_status()).map_err(|e| format!("Couldn't download the update: {e}"))?;
    let total = response.content_length();
    let mut bytes = Vec::new();
    let mut stream = response.bytes_stream();
    while let Some(chunk) = stream.next().await {
        bytes.extend_from_slice(&chunk.map_err(|e| format!("Couldn't download the update: {e}"))?);
        progress(bytes.len() as u64, total);
    }
    Ok(bytes)
}

/// The key releases are signed with: set when the launcher is built
/// (`QUIVER_UPDATE_PUBKEY`). Debug builds also take it at run time, for tests.
pub fn public_key() -> Option<String> {
    let built = option_env!("QUIVER_UPDATE_PUBKEY").map(str::to_string);
    let runtime = if cfg!(debug_assertions) { std::env::var("QUIVER_UPDATE_PUBKEY").ok() } else { None };
    runtime.or(built).filter(|k| !k.trim().is_empty())
}

/// Checks a download against its `.sig` file, as `tauri signer sign` writes them
/// (base64 of a minisign signature) or as minisign writes them.
pub fn verify(key: &str, bytes: &[u8], signature: &str) -> Result<(), String> {
    use base64::Engine;
    let text = |s: &str| base64::engine::general_purpose::STANDARD.decode(s.trim()).ok().and_then(|b| String::from_utf8(b).ok());
    let key = text(key).unwrap_or_else(|| key.to_string());
    let key = minisign_verify::PublicKey::decode(&key)
        .or_else(|_| minisign_verify::PublicKey::from_base64(key.trim()))
        .map_err(|_| "This build's update key can't be read.".to_string())?;
    let signature = text(signature).unwrap_or_else(|| signature.to_string());
    let signature = minisign_verify::Signature::decode(&signature).map_err(|_| "The update's signature can't be read, so it wasn't installed.".to_string())?;
    key.verify(bytes, &signature, false).map_err(|_| "The update's signature doesn't match, so it wasn't installed.".to_string())
}

/// Copies the launcher's saved files (the JSON in the data folder) into
/// `backups/updates/<from>-to-<to>-<time>`, keeping the newest ten.
pub fn back_up(data: &Path, from: &str, to: &str) -> io::Result<PathBuf> {
    let root = data.join("backups").join("updates");
    let at = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_millis()).unwrap_or(0);
    let dir = root.join(format!("{at}-{from}-to-{to}"));
    fs::create_dir_all(&dir)?;
    for entry in fs::read_dir(data)?.flatten() {
        let path = entry.path();
        if path.is_file() && path.extension().is_some_and(|e| e == "json") {
            fs::copy(&path, dir.join(entry.file_name()))?;
        }
    }
    let mut all: Vec<PathBuf> = fs::read_dir(&root)?.flatten().map(|e| e.path()).filter(|p| p.is_dir()).collect();
    all.sort();
    let extra = all.len().saturating_sub(KEEP_BACKUPS);
    for old in &all[..extra] {
        let _ = fs::remove_dir_all(old);
    }
    Ok(dir)
}

/// Puts `new` in place of `current` (a running program): the running one moves
/// aside to `<name>.old`, which Windows allows, and is deleted on the next start.
/// If the new one can't be put in place, the old one goes back.
pub fn swap(current: &Path, new: &[u8]) -> io::Result<()> {
    let name = current.file_name().ok_or_else(|| io::Error::other("no file name"))?.to_string_lossy().into_owned();
    let dir = current.parent().ok_or_else(|| io::Error::other("no folder"))?;
    let incoming = dir.join(format!("{name}.new"));
    let old = dir.join(format!("{name}.old"));
    fs::write(&incoming, new)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(&incoming, fs::Permissions::from_mode(0o755))?;
    }
    let _ = fs::remove_file(&old);
    fs::rename(current, &old)?;
    if let Err(e) = fs::rename(&incoming, current) {
        let _ = fs::rename(&old, current);
        let _ = fs::remove_file(&incoming);
        return Err(e);
    }
    Ok(())
}

/// Removes what an earlier update moved aside.
pub fn clean_up(current: &Path) {
    if let Some(name) = current.file_name() {
        let _ = fs::remove_file(current.with_file_name(format!("{}.old", name.to_string_lossy())));
    }
}

/// The exe from a portable release's zip.
pub fn exe_from_zip(bytes: &[u8]) -> Result<Vec<u8>, String> {
    let mut zip = zip::ZipArchive::new(io::Cursor::new(bytes)).map_err(|e| format!("The update can't be opened: {e}"))?;
    let name = zip
        .file_names()
        .find(|n| n.to_lowercase().ends_with(".exe") && !n.contains('/'))
        .map(str::to_string)
        .ok_or("The update has no program in it.")?;
    let mut exe = Vec::new();
    zip.by_name(&name).map_err(|e| e.to_string())?.read_to_end(&mut exe).map_err(|e| e.to_string())?;
    Ok(exe)
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Available {
    pub version: String,
    pub notes: String,
    pub url: String,
    pub preview: bool,
    /// Why it can't install here (the player gets the release page instead).
    pub manual: Option<String>,
}

pub fn available(release: &Release, kind: &Kind) -> Available {
    let manual = if public_key().is_none() {
        Some("This copy of Quiver Launcher can't check an update's signature, so get it from the release page.".into())
    } else if asset(release, kind).is_none() {
        Some(match kind {
            Kind::Elsewhere if cfg!(target_os = "macos") => "Get the new version from the release page.".into(),
            Kind::Elsewhere => "Update Quiver Launcher the way you installed it (Flatpak or your package manager), or get it from the release page.".into(),
            _ => "This release has no download for this computer yet.".into(),
        })
    } else {
        None
    };
    Available {
        version: release.tag_name.trim_start_matches('v').into(),
        notes: release.body.clone().unwrap_or_default(),
        url: release.html_url.clone(),
        preview: release.prerelease,
        manual,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn release(tag: &str, prerelease: bool) -> Release {
        Release { tag_name: tag.into(), body: None, prerelease, draft: false, html_url: String::new(), assets: vec![] }
    }

    #[test]
    fn picks_the_newest_release_for_the_channel() {
        assert_eq!(compare("4.0.0", "4.0.0-alpha.1"), Ordering::Greater);
        assert_eq!(compare("v4.0.0-alpha.10", "4.0.0-alpha.9"), Ordering::Greater);
        assert_eq!(compare("4.0.0-beta", "4.0.0-alpha.3"), Ordering::Greater);
        assert_eq!(compare("4.1.0", "4.0.12"), Ordering::Greater);
        assert_eq!(compare("4.0", "4.0.0"), Ordering::Equal);
        let list = [release("v4.0.2-beta.1", true), release("v4.0.1", false), release("v4.0.0", false)];
        assert_eq!(newest(&list, "4.0.0", false).map(|r| r.tag_name.as_str()), Some("v4.0.1"));
        assert_eq!(newest(&list, "4.0.0", true).map(|r| r.tag_name.as_str()), Some("v4.0.2-beta.1"));
        // On a preview already: previews are offered.
        assert_eq!(newest(&list, "4.0.1-alpha.1", false).map(|r| r.tag_name.as_str()), Some("v4.0.2-beta.1"));
        assert!(newest(&list, "4.0.2", true).is_none());
    }

    #[test]
    fn finds_the_download_for_each_kind_of_copy() {
        let mut r = release("v4.0.1", false);
        for name in ["Quiver Launcher_4.0.1_x64-setup.exe", PORTABLE_ZIP, "Quiver Launcher_4.0.1_amd64.AppImage", "Quiver Launcher_4.0.1_aarch64.AppImage", "QuiverLauncher_4.0.1_arm64.apk", "QuiverLauncher_4.0.1_x86_64.flatpak"] {
            r.assets.push(Asset { name: name.into(), browser_download_url: String::new() });
        }
        assert_eq!(asset(&r, &Kind::Installer).unwrap().name, "Quiver Launcher_4.0.1_x64-setup.exe");
        assert_eq!(asset(&r, &Kind::Portable("q.exe".into())).unwrap().name, PORTABLE_ZIP);
        let image = if cfg!(target_arch = "aarch64") { "aarch64" } else { "amd64" };
        assert!(asset(&r, &Kind::AppImage("q".into())).unwrap().name.contains(image));
        assert!(asset(&r, &Kind::Elsewhere).is_none());
        // Android builds are for arm64 only, the Flatpak for x86_64.
        assert_eq!(asset(&r, &Kind::Apk).is_some(), cfg!(target_arch = "aarch64"));
        assert_eq!(asset(&r, &Kind::Flatpak).map(|a| a.name.as_str()), (!cfg!(target_arch = "aarch64")).then_some("QuiverLauncher_4.0.1_x86_64.flatpak"));
    }

    #[test]
    fn checks_signatures_as_tauri_signer_writes_them() {
        // A key and a signature of "hello", made as e2e/minisign.js makes them.
        let key = "dW50cnVzdGVkIGNvbW1lbnQ6IG1pbmlzaWduIHB1YmxpYyBrZXkgQjYzMzczNzYxNDQ4MTVBNgpSV1MyTTNOMkZFZ1Zwank5dUV2R2xSdVc1RHIzYUVMdHhDditJNnZxalNpN3dUS0lOdG1HbU5ycQo=";
        let signature = "dW50cnVzdGVkIGNvbW1lbnQ6IHNpZ25hdHVyZSBmcm9tIHRhdXJpIHNlY3JldCBrZXkKUlVTMk0zTjJGRWdWcGlGV0IxckhyZjhLV3ZkNHJEZi9ELzZvK3VKYlFaaUJoeHljelNSMXJSMHNvdy8xNDdZdFR0REc0eU94ZUI0SWhzKzIxclcyczV6dm1ONjFDTllxb0FFPQp0cnVzdGVkIGNvbW1lbnQ6IHRpbWVzdGFtcDoxNzkyMzg3MDU4CWZpbGU6dXBkYXRlClhhVmR4YnNwYy9WVzVLM3pRdWM2aHJLVXFhMjB0NUxSV29NMEtpNkZiVXFJRnVMeFlkVzRQT0krcFRkT0U3eVY4UXZta0J5V2ZlVWdoZFA5K1hFdkNnPT0K";
        assert!(verify(key, b"hello", signature).is_ok());
        assert!(verify(key, b"hellO", signature).unwrap_err().contains("doesn't match"));
        assert!(verify(key, b"hello", "not a signature").unwrap_err().contains("can't be read"));
    }

    #[test]
    fn swaps_a_program_and_cleans_up_after() {
        let dir = tempfile::tempdir().unwrap();
        let exe = dir.path().join("QuiverLauncher.exe");
        fs::write(&exe, "old").unwrap();
        swap(&exe, b"new").unwrap();
        assert_eq!(fs::read_to_string(&exe).unwrap(), "new");
        assert_eq!(fs::read_to_string(dir.path().join("QuiverLauncher.exe.old")).unwrap(), "old");
        clean_up(&exe);
        assert!(!dir.path().join("QuiverLauncher.exe.old").exists());
    }

    #[test]
    fn backs_up_saved_files_keeping_ten() {
        let data = tempfile::tempdir().unwrap();
        fs::write(data.path().join("library.json"), "[1]").unwrap();
        fs::write(data.path().join("quiver.log"), "log").unwrap();
        let mut last = PathBuf::new();
        for i in 0..12 {
            last = back_up(data.path(), "4.0.0", &format!("4.0.{i}")).unwrap();
            std::thread::sleep(std::time::Duration::from_millis(2));
        }
        assert_eq!(fs::read_to_string(last.join("library.json")).unwrap(), "[1]");
        assert!(!last.join("quiver.log").exists());
        assert_eq!(fs::read_dir(data.path().join("backups/updates")).unwrap().count(), 10);
    }

    #[test]
    fn takes_the_exe_out_of_a_portable_zip() {
        let mut bytes = io::Cursor::new(Vec::new());
        let mut zip = zip::ZipWriter::new(&mut bytes);
        let options = zip::write::SimpleFileOptions::default();
        zip.start_file("portable.txt", options).unwrap();
        zip.start_file("QuiverLauncher.exe", options).unwrap();
        io::Write::write_all(&mut zip, b"MZ new").unwrap();
        zip.finish().unwrap();
        assert_eq!(exe_from_zip(bytes.get_ref()).unwrap(), b"MZ new");
    }
}
