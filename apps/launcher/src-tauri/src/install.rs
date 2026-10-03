//! Downloads a release file, checks it against the site's checksum, and
//! installs it into the app's folder.
use futures_util::StreamExt;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use tokio::io::AsyncWriteExt;

pub const MISMATCH: &str =
    "This download doesn't match the file Quiver checked, so it wasn't installed.";
/// Written into an installed app's folder; holds the installed version.
pub const VERSION_FILE: &str = ".quiver-version";

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct InstallRequest {
    pub id: String,
    pub url: String,
    pub filename: String,
    /// `sha256:<hex>`; absent when the site couldn't pin the file.
    pub checksum: Option<String>,
    pub folder: String,
    /// Install into this folder instead (an install adopted from 3.x).
    pub dir: Option<String>,
    pub files_to_add: Vec<String>,
    pub version: String,
}

#[derive(Serialize)]
pub struct Installed {
    pub dir: String,
    pub version: String,
}

#[derive(Serialize, Clone)]
pub struct Progress {
    pub id: String,
    pub phase: &'static str,
    pub received: u64,
    pub total: Option<u64>,
}

/// A single file or folder name, never a path.
pub fn plain_name(name: &str) -> Result<&str, String> {
    let name = name.trim();
    let bad = name.is_empty()
        || name == "."
        || name == ".."
        || name.contains(['/', '\\', ':'])
        || name.chars().any(|c| c.is_control());
    if bad {
        Err(format!("\"{name}\" isn't a valid file or folder name."))
    } else {
        Ok(name)
    }
}

pub async fn install(
    apps_dir: &Path,
    req: InstallRequest,
    progress: impl Fn(Progress),
) -> Result<Installed, String> {
    let folder = plain_name(&req.folder)?.to_string();
    let filename = plain_name(&req.filename)?.to_string();
    let staging = apps_dir.join(".staging").join(plain_name(&req.id)?);
    let _ = fs::remove_dir_all(&staging);
    fs::create_dir_all(&staging).map_err(text)?;
    let result = async {
        let file = staging.join(&filename);
        let hash = download(&req, &file, &progress).await?;
        if let Some(expected) = req.checksum.as_deref().and_then(|c| c.strip_prefix("sha256:")) {
            if !expected.eq_ignore_ascii_case(&hash) {
                return Err(MISMATCH.to_string());
            }
        }
        progress(Progress { id: req.id.clone(), phase: "installing", received: 0, total: None });
        let target = req.dir.as_ref().map(PathBuf::from).unwrap_or_else(|| apps_dir.join(&folder));
        let files = req.files_to_add.clone();
        let version = req.version.clone();
        let out = staging.join("out");
        let dir = target.clone();
        tokio::task::spawn_blocking(move || -> Result<(), String> {
            extract(&file, &filename, &out)?;
            flatten(&out).map_err(text)?;
            merge(&out, &dir).map_err(text)?;
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
        Ok(Installed { dir: target.to_string_lossy().into(), version: req.version.clone() })
    }
    .await;
    let _ = fs::remove_dir_all(&staging);
    result
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
        .and_then(|r| r.error_for_status())
        .map_err(|e| format!("The download failed: {e}"))?;
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
            progress(Progress { id: req.id.clone(), phase: "downloading", received, total });
        }
    }
    file.flush().await.map_err(text)?;
    Ok(hex::encode(hasher.finalize()))
}

fn extract(file: &Path, name: &str, out: &Path) -> Result<(), String> {
    fs::create_dir_all(out).map_err(text)?;
    let lower = name.to_lowercase();
    if lower.ends_with(".zip") {
        let mut archive = zip::ZipArchive::new(fs::File::open(file).map_err(text)?).map_err(text)?;
        for i in 0..archive.len() {
            let mut entry = archive.by_index(i).map_err(text)?;
            // enclosed_name refuses paths that would escape `out` (zip-slip).
            let Some(path) = entry.enclosed_name().map(|p| out.join(p)) else { continue };
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
    } else if lower.ends_with(".tar.gz") || lower.ends_with(".tgz") {
        let gz = flate2::read::GzDecoder::new(fs::File::open(file).map_err(text)?);
        // unpack keeps every entry inside `out`.
        tar::Archive::new(gz).unpack(out).map_err(text)
    } else if lower.ends_with(".7z") {
        sevenz_rust2::decompress_file(file, out).map_err(text)
    } else if lower.ends_with(".rar") {
        Err("RAR downloads aren't supported yet.".into())
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

/// Hoists the contents of a lone wrapper folder, as most archives have one.
fn flatten(dir: &Path) -> io::Result<()> {
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
            if fs::rename(entry.path(), &dest).is_err() {
                fs::copy(entry.path(), &dest)?;
            }
        }
    }
    Ok(())
}

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
    const HELPERS: [&str; 6] = ["unins", "crash", "vc_redist", "dxsetup", "setup", "updater"];
    files.into_iter().find(|p| {
        let n = name(p);
        let program = match os {
            "windows" => n.ends_with(".exe"),
            "macos" => n.ends_with(".app"),
            _ => n.ends_with(".appimage") || n.ends_with(".x86_64") || n.ends_with(".sh") || is_unix_binary(p),
        };
        program && !HELPERS.iter().any(|h| n.starts_with(h))
    })
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

#[cfg(test)]
mod tests {
    use super::*;

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
    fn flattens_a_wrapper_folder() {
        let dir = tempfile::tempdir().unwrap();
        fs::create_dir_all(dir.path().join("Game-v1/data")).unwrap();
        fs::write(dir.path().join("Game-v1/Game.exe"), "").unwrap();
        flatten(dir.path()).unwrap();
        assert!(dir.path().join("Game.exe").exists() && dir.path().join("data").is_dir());
    }
}
