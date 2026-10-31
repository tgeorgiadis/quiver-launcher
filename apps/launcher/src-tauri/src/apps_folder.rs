//! Where apps are installed: `apps` in the data folder unless the player
//! picked another folder, which `apps-folder.json` remembers. Changing it
//! moves the installed apps there first. An app's own folder in it can be
//! renamed too.
use serde::{Deserialize, Serialize};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

const FILE: &str = "apps-folder.json";

#[derive(Serialize, Deserialize)]
struct Saved {
    path: PathBuf,
}

/// The apps folder: the one the player picked, else `apps` in the data folder.
pub fn current(data: &Path) -> PathBuf {
    fs::read(data.join(FILE))
        .ok()
        .and_then(|b| serde_json::from_slice::<Saved>(&b).ok())
        .map(|s| s.path)
        .filter(|p| p.is_absolute())
        .unwrap_or_else(|| data.join("apps"))
}

fn save(data: &Path, path: &Path) -> io::Result<()> {
    fs::create_dir_all(data)?;
    let temp = data.join(format!("{FILE}.tmp"));
    fs::write(&temp, serde_json::to_vec_pretty(&Saved { path: path.to_path_buf() })?)?;
    fs::rename(temp, data.join(FILE))
}

#[derive(Serialize, Clone)]
pub struct Moving {
    pub done: usize,
    pub total: usize,
    pub folder: String,
}

/// Why apps can't move from `from` to `to`, if they can't.
pub fn refuse(from: &Path, to: &Path, folders: &[String]) -> Option<String> {
    if !to.is_absolute() {
        return Some("Choose a folder on this computer.".into());
    }
    let same = |a: &Path, b: &Path| a.canonicalize().ok().zip(b.canonicalize().ok()).is_some_and(|(a, b)| a == b) || a == b;
    if same(from, to) {
        return Some("Apps are already installed there.".into());
    }
    if resolved(to).starts_with(resolved(from)) {
        return Some("Choose a folder outside the apps folder.".into());
    }
    if let Some(taken) = folders.iter().find(|f| from.join(f).exists() && to.join(f).exists()) {
        return Some(format!("That folder already has a folder named {taken}. Choose another folder, or move it out of the way first."));
    }
    None
}

/// The path as the file system names it, for a folder that doesn't exist yet
/// too: its nearest existing parent, resolved, with the rest added back. So
/// both sides of a comparison are written alike, as Windows' `\\?\` form or
/// its long names are once resolved.
fn resolved(path: &Path) -> PathBuf {
    let mut rest = vec![];
    let mut at = path;
    loop {
        if let Ok(found) = at.canonicalize() {
            return rest.iter().rev().fold(found, |path, part| path.join(part));
        }
        match (at.parent(), at.file_name()) {
            (Some(parent), Some(name)) => {
                rest.push(name.to_os_string());
                at = parent;
            }
            _ => return path.to_path_buf(),
        }
    }
}

/// Moves each app's folder from `from` into `to`, then makes `to` the apps
/// folder. A move across drives copies, then deletes the original only once
/// the copy is complete. Stops at the first failure: what moved stays moved,
/// and the apps folder stays `from` with the rest.
pub fn move_apps(data: &Path, from: &Path, to: &Path, folders: &[String], progress: impl Fn(Moving)) -> Result<(), String> {
    if let Some(why) = refuse(from, to, folders) {
        return Err(why);
    }
    fs::create_dir_all(to).map_err(|e| format!("Couldn't use that folder: {e}"))?;
    let present: Vec<&String> = folders.iter().filter(|f| from.join(f).exists()).collect();
    for (done, folder) in present.iter().enumerate() {
        progress(Moving { done, total: present.len(), folder: folder.to_string() });
        let (old, new) = (from.join(folder), to.join(folder));
        if fs::rename(&old, &new).is_err() {
            copy(&old, &new).and_then(|_| fs::remove_dir_all(&old)).map_err(|e| {
                let _ = fs::remove_dir_all(&new);
                format!("Couldn't move {folder}: {e}. It's still in the old folder.")
            })?;
        }
    }
    // Downloads under way are never moved; a stale staging folder is just left out.
    let _ = fs::remove_dir_all(from.join(".staging"));
    save(data, to).map_err(|e| e.to_string())?;
    progress(Moving { done: present.len(), total: present.len(), folder: String::new() });
    Ok(())
}

/// Renames an app's folder in `apps` from `from` to `to`, and its Proton
/// prefix in `proton` with it, so it keeps its Wine settings and saves. Never
/// onto a folder that's already there; a change of case alone is the same
/// folder, so it goes ahead. Fails with nothing changed.
pub fn rename(apps: &Path, proton: &Path, from: &str, to: &str) -> Result<(), String> {
    if from == to {
        return Ok(());
    }
    let (old, new) = (apps.join(from), apps.join(to));
    if !old.is_dir() {
        return Err("This app's folder isn't there any more.".into());
    }
    let same = |a: &Path, b: &Path| a.canonicalize().ok().zip(b.canonicalize().ok()).is_some_and(|(a, b)| a == b);
    if new.exists() && !same(&old, &new) {
        return Err(format!("There's already a folder named {to} in your apps folder. Choose another name."));
    }
    let (old_prefix, new_prefix) = (proton.join(from), proton.join(to));
    let prefix = old_prefix.is_dir();
    if prefix && new_prefix.exists() && !same(&old_prefix, &new_prefix) {
        return Err(format!("There's already a Proton folder named {to}. Choose another name."));
    }
    fs::rename(&old, &new).map_err(|e| format!("Couldn't rename {from}: {e}. Close the app, and anything with its files open, then try again."))?;
    if prefix {
        if let Err(e) = fs::rename(&old_prefix, &new_prefix) {
            let _ = fs::rename(&new, &old);
            return Err(format!("Couldn't rename {from}'s Proton folder: {e}. Close the app, then try again."));
        }
    }
    Ok(())
}

/// Copies a folder, keeping symbolic links as links.
fn copy(from: &Path, to: &Path) -> io::Result<()> {
    fs::create_dir_all(to)?;
    for entry in fs::read_dir(from)? {
        let entry = entry?;
        let (src, dst) = (entry.path(), to.join(entry.file_name()));
        let kind = entry.file_type()?;
        if kind.is_dir() {
            copy(&src, &dst)?;
        } else if kind.is_symlink() {
            #[cfg(unix)]
            std::os::unix::fs::symlink(fs::read_link(&src)?, &dst)?;
            #[cfg(not(unix))]
            fs::copy(&src, &dst).map(|_| ())?;
        } else {
            fs::copy(&src, &dst)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn moves_installed_apps_and_remembers_the_folder() {
        let data = tempfile::tempdir().unwrap();
        let elsewhere = tempfile::tempdir().unwrap();
        let from = current(data.path());
        assert_eq!(from, data.path().join("apps"));
        fs::create_dir_all(from.join("Game/data")).unwrap();
        fs::write(from.join("Game/data/save.dat"), "saved").unwrap();
        fs::create_dir_all(from.join(".staging/x")).unwrap();
        let to = elsewhere.path().join("Games");
        let steps = std::cell::RefCell::new(vec![]);
        move_apps(data.path(), &from, &to, &["Game".into(), "Gone".into()], |m| steps.borrow_mut().push((m.done, m.total))).unwrap();
        assert_eq!(fs::read_to_string(to.join("Game/data/save.dat")).unwrap(), "saved");
        assert!(!from.join("Game").exists() && !from.join(".staging").exists());
        assert_eq!(current(data.path()), to);
        assert_eq!(steps.into_inner(), vec![(0, 1), (1, 1)]);
    }

    #[test]
    fn refuses_the_same_folder_one_inside_it_or_a_name_already_taken() {
        let root = tempfile::tempdir().unwrap();
        let from = root.path().join("apps");
        fs::create_dir_all(from.join("Game")).unwrap();
        let folders = ["Game".to_string()];
        assert!(refuse(&from, &from, &folders).is_some());
        assert!(refuse(&from, &from.join("More"), &folders).is_some());
        // Named another way (here through a link) while the new folder doesn't exist yet.
        #[cfg(unix)]
        {
            std::os::unix::fs::symlink(root.path(), root.path().join("link")).unwrap();
            assert!(refuse(&from, &root.path().join("link/apps/More"), &folders).is_some());
        }
        assert!(refuse(&from, Path::new("relative"), &folders).is_some());
        let to = root.path().join("Games");
        assert!(refuse(&from, &to, &folders).is_none());
        fs::create_dir_all(to.join("Game")).unwrap();
        assert!(refuse(&from, &to, &folders).unwrap().contains("Game"));
    }

    #[test]
    fn renames_an_app_folder_and_its_proton_prefix() {
        let data = tempfile::tempdir().unwrap();
        let (apps, proton) = (data.path().join("apps"), data.path().join("proton"));
        fs::create_dir_all(apps.join("ssx3/data")).unwrap();
        fs::write(apps.join("ssx3/data/save.dat"), "saved").unwrap();
        fs::create_dir_all(proton.join("ssx3/pfx")).unwrap();
        rename(&apps, &proton, "ssx3", "SSX 3").unwrap();
        assert_eq!(fs::read_to_string(apps.join("SSX 3/data/save.dat")).unwrap(), "saved");
        assert!(proton.join("SSX 3/pfx").is_dir());
        assert!(!apps.join("ssx3").exists() && !proton.join("ssx3").exists());
        // No prefix (never run with Proton): only the folder.
        rename(&apps, &proton, "SSX 3", "Racing").unwrap();
        assert!(apps.join("Racing/data").is_dir() && proton.join("Racing/pfx").is_dir());
        fs::remove_dir_all(proton.join("Racing")).unwrap();
        rename(&apps, &proton, "Racing", "SSX").unwrap();
        assert!(apps.join("SSX/data").is_dir() && !proton.join("SSX").exists());
    }

    #[test]
    fn a_change_of_case_renames_the_same_folder() {
        let data = tempfile::tempdir().unwrap();
        let apps = data.path().join("apps");
        fs::create_dir_all(apps.join("ssx")).unwrap();
        rename(&apps, &data.path().join("proton"), "ssx", "SSX").unwrap();
        let names: Vec<_> = fs::read_dir(&apps).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(names, ["SSX"]);
    }

    #[test]
    fn refuses_a_name_taken_or_a_folder_gone_and_changes_nothing() {
        let data = tempfile::tempdir().unwrap();
        let (apps, proton) = (data.path().join("apps"), data.path().join("proton"));
        fs::create_dir_all(apps.join("Game")).unwrap();
        fs::create_dir_all(apps.join("Other")).unwrap();
        assert!(rename(&apps, &proton, "Game", "Other").unwrap_err().contains("Other"));
        assert!(rename(&apps, &proton, "Gone", "New").unwrap_err().contains("isn't there"));
        // Its prefix's new name is taken: the folder isn't renamed either.
        fs::create_dir_all(proton.join("Game")).unwrap();
        fs::create_dir_all(proton.join("New")).unwrap();
        assert!(rename(&apps, &proton, "Game", "New").unwrap_err().contains("Proton"));
        assert!(apps.join("Game").is_dir() && !apps.join("New").exists());
        rename(&apps, &proton, "Game", "Game").unwrap();
    }

    #[test]
    fn copies_a_folder_with_its_links() {
        let root = tempfile::tempdir().unwrap();
        fs::create_dir_all(root.path().join("a/b")).unwrap();
        fs::write(root.path().join("a/b/f"), "x").unwrap();
        #[cfg(unix)]
        std::os::unix::fs::symlink("b/f", root.path().join("a/link")).unwrap();
        copy(&root.path().join("a"), &root.path().join("c")).unwrap();
        assert_eq!(fs::read_to_string(root.path().join("c/b/f")).unwrap(), "x");
        #[cfg(unix)]
        assert_eq!(fs::read_link(root.path().join("c/link")).unwrap(), Path::new("b/f"));
    }
}
