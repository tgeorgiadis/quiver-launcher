//! Asking the player for a file or a folder. On a desktop, the system's file
//! dialog. In Steam's Game Mode, the page's own browser (file-picker.tsx)
//! instead: GTK's dialog there is a desktop window gamescope squeezes onto
//! the screen, whose buttons a controller can't reach and a trackpad's
//! clicks miss, so it couldn't even be closed.
use serde::Serialize;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use tauri::{Emitter, Manager};
use tauri_plugin_dialog::DialogExt;

/// What's asked for.
#[derive(Clone, Copy, PartialEq)]
pub enum Want {
    File,
    Folder,
}

/// The page's browser asked, waiting for its answer (`picked`).
#[derive(Default)]
pub struct Picks(Mutex<HashMap<u64, tokio::sync::oneshot::Sender<Option<PathBuf>>>>, AtomicU64);

/// What the page's browser is asked to show ("pick").
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Ask<'a> {
    id: u64,
    folder: bool,
    title: &'a str,
    dir: Option<String>,
    /// The files it may pick, by extension (lowercase, no dot); empty for any.
    extensions: &'a [&'a str],
}

/// Whether the page's browser asks: in Steam's Game Mode, or with
/// `QUIVER_OWN_PICKER` set (end-to-end tests).
fn own() -> bool {
    std::env::var_os("QUIVER_OWN_PICKER").is_some() || crate::keyboard::game_mode(|name| std::env::var(name).ok())
}

/// A file dialog, over the launcher's window on a computer (Android has no
/// windows to sit over).
fn file_dialog(app: &tauri::AppHandle, window: &tauri::WebviewWindow) -> tauri_plugin_dialog::FileDialogBuilder<crate::AppRuntime> {
    let dialog = app.dialog().file();
    #[cfg(desktop)]
    let dialog = dialog.set_parent(window);
    #[cfg(mobile)]
    let _ = window;
    dialog
}

/// Asks for a file or a folder, opening in `dir` when it's one; files only
/// of `filter`'s extensions, when given. None when the player cancels.
pub async fn choose(
    app: &tauri::AppHandle,
    window: &tauri::WebviewWindow,
    want: Want,
    title: &str,
    dir: Option<&Path>,
    filter: Option<(&str, &[&str])>,
) -> Option<PathBuf> {
    let dir = dir.filter(|d| d.is_dir());
    let (tx, rx) = tokio::sync::oneshot::channel::<Option<PathBuf>>();
    if own() {
        let picks = app.state::<Picks>();
        let id = picks.1.fetch_add(1, Ordering::Relaxed);
        picks.0.lock().unwrap().insert(id, tx);
        let ask = Ask { id, folder: want == Want::Folder, title, dir: dir.map(|d| d.to_string_lossy().into()), extensions: filter.map_or(&[], |(_, e)| e) };
        if window.emit("pick", ask).is_err() {
            picks.0.lock().unwrap().remove(&id);
            return None;
        }
        return rx.await.ok().flatten();
    }
    let mut dialog = file_dialog(app, window).set_title(title);
    if let Some(dir) = dir {
        dialog = dialog.set_directory(dir);
    }
    if let Some((name, extensions)) = filter {
        dialog = dialog.add_filter(name, extensions);
    }
    let send = move |path: Option<tauri_plugin_dialog::FilePath>| {
        let _ = tx.send(path.and_then(|p| p.into_path().ok()));
    };
    match want {
        Want::File => dialog.pick_file(send),
        // Android has no folder picker; with nothing sent, it reads as a cancel.
        #[cfg(desktop)]
        Want::Folder => dialog.pick_folder(send),
        #[cfg(mobile)]
        Want::Folder => drop(send),
    }
    rx.await.ok().flatten()
}

/// The page's browser's answer to "pick" `id`: the path chosen, or None for Cancel.
#[tauri::command]
pub fn picked(picks: tauri::State<Picks>, id: u64, path: Option<String>) {
    if let Some(tx) = picks.0.lock().unwrap().remove(&id) {
        let _ = tx.send(path.map(PathBuf::from));
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    name: String,
    folder: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Listing {
    path: String,
    parent: Option<String>,
    /// Folders first, then files, each by name; never hidden ones.
    entries: Vec<Entry>,
    /// Where to start looking: the home folder, its Downloads and Desktop, and drives plugged in (a Steam Deck's microSD card).
    places: Vec<Place>,
}

#[derive(Serialize)]
pub struct Place {
    name: String,
    path: String,
}

/// What's in a folder, for the page's browser: `path`, or the home folder.
#[tauri::command]
pub fn browse(path: Option<String>) -> Result<Listing, String> {
    let home = dirs::home_dir();
    let path = path.map(PathBuf::from).filter(|p| p.is_dir()).or_else(|| home.clone()).ok_or("There's no folder to start in.")?;
    let read = std::fs::read_dir(&path).map_err(|e| format!("That folder can't be opened: {e}"))?;
    let mut entries: Vec<Entry> = read
        .flatten()
        .filter(|e| !e.file_name().to_string_lossy().starts_with('.'))
        // A link to a folder is a folder to go into.
        .map(|e| Entry { name: e.file_name().to_string_lossy().into(), folder: e.path().is_dir() })
        .take(5000)
        .collect();
    entries.sort_by(|a, b| b.folder.cmp(&a.folder).then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase())));
    Ok(Listing { path: path.to_string_lossy().into(), parent: path.parent().map(|p| p.to_string_lossy().into()), entries, places: places(home.as_deref()) })
}

fn places(home: Option<&Path>) -> Vec<Place> {
    let mut places = Vec::new();
    let mut add = |name: String, path: PathBuf| {
        if path.is_dir() {
            places.push(Place { name, path: path.to_string_lossy().into() });
        }
    };
    if let Some(home) = home {
        add("Home".into(), home.to_path_buf());
        for name in ["Downloads", "Desktop", "Games", "Documents"] {
            add(name.into(), home.join(name));
        }
    }
    // Drives plugged in, as SteamOS and other Linux desktops mount them: a Steam Deck's microSD card is one.
    let user = std::env::var("USER").unwrap_or_default();
    for root in [PathBuf::from("/run/media").join(&user), PathBuf::from("/run/media"), PathBuf::from("/media").join(&user)] {
        for drive in std::fs::read_dir(&root).into_iter().flatten().flatten() {
            let path = drive.path();
            if path.is_dir() && path.file_name().is_some_and(|n| *n != *user) {
                add(drive.file_name().to_string_lossy().into(), path);
            }
        }
    }
    places
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lists_folders_then_files_by_name_without_hidden_ones() {
        let root = tempfile::tempdir().unwrap();
        for dir in ["b-folder", "A-folder", ".hidden"] {
            std::fs::create_dir(root.path().join(dir)).unwrap();
        }
        for file in ["c.txt", "B.sh", ".env"] {
            std::fs::write(root.path().join(file), "").unwrap();
        }
        let listing = browse(Some(root.path().to_string_lossy().into())).unwrap();
        let names: Vec<_> = listing.entries.iter().map(|e| (e.name.as_str(), e.folder)).collect();
        assert_eq!(names, [("A-folder", true), ("b-folder", true), ("B.sh", false), ("c.txt", false)]);
        assert_eq!(listing.parent.as_deref().map(Path::new), root.path().parent());
    }

    #[test]
    fn a_folder_that_isnt_there_starts_at_home() {
        let listing = browse(Some("/no/such/folder".into()));
        assert!(listing.is_ok() == dirs::home_dir().is_some());
    }
}
