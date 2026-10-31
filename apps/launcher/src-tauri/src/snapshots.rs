//! Snapshots of the player's library, so a damaged or emptied library can be
//! put back. Each snapshot is a folder in `backups` holding the library,
//! installs, settings and collections as they were together. One is taken at
//! the first save after the launcher starts, whenever an app is added,
//! removed, installed or uninstalled, and otherwise at most every ten minutes, and only
//! when something changed. The newest 50 are kept, within 25 MB.
use serde::Serialize;
use sha2::{Digest, Sha256};
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

/// What a snapshot holds; the catalog is only a cache, so it isn't kept.
pub const FILES: [&str; 4] = ["library", "installs", "settings", "collections"];
const EVERY: Duration = Duration::from_secs(600);
const KEEP: usize = 50;
const KEEP_BYTES: u64 = 25 * 1024 * 1024;

fn folder(data: &Path) -> PathBuf {
    data.join("backups")
}

fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(0)
}

/// Snapshots, newest first, by when they were taken (their folder names).
fn all(data: &Path) -> Vec<(u64, PathBuf)> {
    let mut list: Vec<(u64, PathBuf)> = fs::read_dir(folder(data))
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|e| Some((e.file_name().to_str()?.parse().ok()?, e.path())))
        .collect();
    list.sort_by(|a, b| b.0.cmp(&a.0));
    list
}

fn hash(dir: &Path) -> String {
    let mut sha = Sha256::new();
    for name in FILES {
        sha.update(name);
        sha.update(fs::read(dir.join(format!("{name}.json"))).unwrap_or_default());
    }
    format!("{:x}", sha.finalize())
}

/// Takes a snapshot of the saved files if one is due: `first` is the first
/// save since the launcher started. Returns whether it took one.
pub fn take(data: &Path, first: bool) -> std::io::Result<bool> {
    let list = all(data);
    let newest = list.first();
    let recent = newest.is_some_and(|(at, _)| now().saturating_sub(*at) < EVERY.as_millis() as u64);
    if !first && recent && newest.is_some_and(|(_, dir)| apps(dir) == apps(data) && installed(dir) == installed(data)) {
        return Ok(false);
    }
    let current = hash(data);
    if newest.is_some_and(|(_, dir)| fs::read_to_string(dir.join("hash")).is_ok_and(|h| h == current)) {
        return Ok(false);
    }
    // Never a snapshot of files that can't be read: those are what snapshots are for.
    if FILES.iter().any(|name| matches!(health(data, name), Some(Problem::Damaged))) {
        return Ok(false);
    }
    let at = now().max(newest.map_or(0, |(at, _)| at + 1));
    let temp = folder(data).join(format!(".{at}"));
    fs::create_dir_all(&temp)?;
    for name in FILES {
        let file = data.join(format!("{name}.json"));
        if file.exists() {
            fs::copy(&file, temp.join(format!("{name}.json")))?;
        }
    }
    fs::write(temp.join("hash"), current)?;
    fs::rename(&temp, folder(data).join(at.to_string()))?;
    prune(data);
    Ok(true)
}

fn prune(data: &Path) {
    let mut bytes = 0;
    for (i, (_, dir)) in all(data).into_iter().enumerate() {
        bytes += fs::read_dir(&dir).into_iter().flatten().flatten().filter_map(|e| e.metadata().ok()).map(|m| m.len()).sum::<u64>();
        // The newest is always kept, however big.
        if i > 0 && (i >= KEEP || bytes > KEEP_BYTES) {
            let _ = fs::remove_dir_all(dir);
        }
    }
}

#[derive(Serialize, Clone, Copy, PartialEq, Debug)]
#[serde(rename_all = "camelCase")]
pub enum Problem {
    /// The file is there but can't be read.
    Damaged,
    /// The file is gone, though snapshots show there was a library.
    Missing,
}

/// What's wrong with a saved file, if anything.
pub fn health(data: &Path, name: &str) -> Option<Problem> {
    match fs::read(data.join(format!("{name}.json"))) {
        Ok(bytes) => serde_json::from_slice::<serde_json::Value>(&bytes).is_err().then_some(Problem::Damaged),
        // Settings and collections can be missing for good reason; a library or installs that snapshots have can't.
        Err(_) if matches!(name, "library" | "installs") && all(data).iter().any(|(_, dir)| dir.join(format!("{name}.json")).exists()) => Some(Problem::Missing),
        Err(_) => None,
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    pub id: String,
    /// When it was taken, in milliseconds since 1970.
    pub at: u64,
    /// How many apps its library has.
    pub apps: usize,
}

/// How many apps the library in a folder has (the data folder, or a snapshot).
fn apps(dir: &Path) -> usize {
    fs::read(dir.join("library.json"))
        .ok()
        .and_then(|b| serde_json::from_slice::<Vec<serde_json::Value>>(&b).ok())
        .map_or(0, |items| items.iter().filter(|i| i.get("removed") != Some(&serde_json::Value::Bool(true))).count())
}

/// How many installed apps a folder's installs file lists.
fn installed(dir: &Path) -> usize {
    fs::read(dir.join("installs.json")).ok().and_then(|b| serde_json::from_slice::<serde_json::Map<String, serde_json::Value>>(&b).ok()).map_or(0, |m| m.len())
}

pub fn list(data: &Path) -> Vec<Snapshot> {
    all(data).into_iter().map(|(at, dir)| Snapshot { id: at.to_string(), at, apps: apps(&dir) }).collect()
}

/// Puts a snapshot's files back. The files it replaces are snapshotted first
/// when they can be read, and kept beside as `<name>.damaged-<time>.json` when they can't.
pub fn restore(data: &Path, id: &str) -> Result<(), String> {
    let dir = folder(data).join(id.parse::<u64>().map_err(|_| "That snapshot isn't there.")?.to_string());
    if !dir.is_dir() {
        return Err("That snapshot isn't there any more.".into());
    }
    let _ = take(data, true);
    set_aside_damaged(data);
    for name in FILES {
        let (from, to) = (dir.join(format!("{name}.json")), data.join(format!("{name}.json")));
        let temp = data.join(format!("{name}.json.tmp"));
        let result = if from.exists() { fs::copy(&from, &temp).and_then(|_| fs::rename(&temp, &to)) } else { fs::remove_file(&to).or(Ok(())) };
        result.map_err(|e| format!("Couldn't restore {name}: {e}"))?;
    }
    Ok(())
}

/// Starts again without the files that can't be read, keeping them aside.
pub fn start_empty(data: &Path) -> Result<(), String> {
    set_aside_damaged(data);
    for name in ["library", "installs"] {
        let file = data.join(format!("{name}.json"));
        if !file.exists() {
            fs::write(&file, if name == "library" { "[]" } else { "{}" }).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

fn set_aside_damaged(data: &Path) {
    for name in FILES {
        if health(data, name) == Some(Problem::Damaged) {
            let _ = fs::rename(data.join(format!("{name}.json")), data.join(format!("{name}.damaged-{}.json", now())));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn save(data: &Path, name: &str, text: &str) {
        fs::write(data.join(format!("{name}.json")), text).unwrap();
    }

    #[test]
    fn takes_snapshots_when_due_and_only_of_changes() {
        let data = tempfile::tempdir().unwrap();
        let data = data.path();
        save(data, "library", r#"[{"id":"a"},{"id":"b","removed":true}]"#);
        assert!(take(data, true).unwrap());
        // Not due yet, then due but unchanged.
        save(data, "library", r#"[{"id":"c"}]"#);
        assert!(!take(data, false).unwrap());
        save(data, "library", r#"[{"id":"a"},{"id":"b","removed":true}]"#);
        assert!(!take(data, true).unwrap());
        save(data, "library", r#"[{"id":"c"}]"#);
        assert!(take(data, true).unwrap());
        // An app added, then installed: taken straight away.
        save(data, "library", r#"[{"id":"c"},{"id":"d"}]"#);
        assert!(take(data, false).unwrap());
        save(data, "installs", r#"{"d":{}}"#);
        assert!(take(data, false).unwrap());
        let list = list(data);
        assert_eq!(list.iter().map(|s| s.apps).collect::<Vec<_>>(), [2, 2, 1, 1]);
        // A damaged file is never snapshotted.
        save(data, "settings", "{oops");
        assert!(!take(data, true).unwrap());
    }

    #[test]
    fn notices_a_damaged_or_missing_library_and_restores_it() {
        let data = tempfile::tempdir().unwrap();
        let data = data.path();
        assert_eq!(health(data, "library"), None, "a new player has nothing to miss");
        save(data, "library", r#"[{"id":"a"}]"#);
        save(data, "installs", "{}");
        take(data, true).unwrap();
        save(data, "library", r#"[{"id":"a"#);
        assert_eq!(health(data, "library"), Some(Problem::Damaged));
        let id = list(data)[0].id.clone();
        restore(data, &id).unwrap();
        assert_eq!(fs::read_to_string(data.join("library.json")).unwrap(), r#"[{"id":"a"}]"#);
        assert!(fs::read_dir(data).unwrap().flatten().any(|e| e.file_name().to_string_lossy().starts_with("library.damaged-")));
        fs::remove_file(data.join("library.json")).unwrap();
        assert_eq!(health(data, "library"), Some(Problem::Missing));
        assert_eq!(health(data, "settings"), None);
        start_empty(data).unwrap();
        assert_eq!(health(data, "library"), None);
        assert_eq!(fs::read_to_string(data.join("library.json")).unwrap(), "[]");
    }

    #[test]
    fn keeps_the_newest_fifty() {
        let data = tempfile::tempdir().unwrap();
        let data = data.path();
        for i in 0..55 {
            save(data, "library", &format!(r#"[{{"id":"{i}"}}]"#));
            take(data, true).unwrap();
        }
        assert_eq!(list(data).len(), 50);
    }
}
