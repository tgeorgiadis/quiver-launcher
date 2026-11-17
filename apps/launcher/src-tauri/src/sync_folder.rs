//! The sync folder: a folder of the player's own that save backups are also kept in, so that whatever syncs it
//! (Dropbox, OneDrive, Syncthing, a USB stick carried between computers) takes their saves to their other computers.
//! It needs no account, and works for apps that aren't in the catalog too. It rides on save backups (saves.rs) the
//! way cloud saves do: what goes in is a backup's own zip, and what comes out is taken in as a backup and put in
//! place by the same Restore, with the same checks.
//!
//! `<folder>/<App name> (<app>)/<number>-<checksum>.zip`
//!
//! - **A file is written once and never changed.** Its name holds the first 8 characters of its saves' checksum, so
//!   two computers never write different things under one name, and a sync program has nothing to merge. Its
//!   "conflicted copy" files, and anything else that isn't named this way, are never read and never removed.
//! - **The number orders them.** It's when the backup was made, or one more than the newest number in the folder
//!   when that's higher: a backup always sorts after everything its computer had seen, even when the clock of one
//!   computer is behind another's.
//! - **A file that isn't whole isn't there.** One still arriving has no manifest yet, or files that don't match
//!   their checksums: it's passed over, and found whole a little later.
//! - **Anything can be in the folder.** Another program, or another person the folder is shared with, can put a
//!   file there. A backup is only taken when it adds up and names the app it's filed under, and where its files go
//!   is never its to say (`saves::restore`).
use crate::saves::{self, Backup, Manifest};
use serde::Serialize;
use std::fs;
use std::io::{self, Read};
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime};

/// The newest backups kept in the folder, per app.
pub const KEEP: usize = 5;
/// No backup is bigger: sixteen places of saves don't come to this.
const MAX_ZIP: u64 = 512 * 1024 * 1024;
/// Entries looked at in one folder, and backups read for one app: a folder filled with other things costs no more.
const MAX_LOOKED: usize = 5_000;
const MAX_READ: usize = 40;
/// The folder made inside the one the player picked, when that one holds other things.
pub const HOME: &str = "Quiver Saves";
/// Says what the folder is to someone who finds it, and marks it as Quiver's.
const NOTE: &str = "About this folder.txt";
const NOTE_TEXT: &str = "Quiver Launcher keeps backups of your saves in this folder.\r\n\r\nEach app has a folder of its own, with its newest backups as zip files. Point Quiver Launcher on your other\r\ncomputers at this same folder (Settings, Cloud saves, Sync folder) and it offers your newest saves when you start an\r\napp there.\r\n\r\nYou can copy the zip files somewhere else to keep them. Renaming or changing them here makes Quiver pass them over.\r\n";

pub const GONE: &str = "Your sync folder isn't there. Check the drive is plugged in, or choose the folder again in Settings.";
const CANT_WRITE: &str = "Quiver couldn't write to your sync folder. Check it has room and you're allowed to write there.";
pub const NOT_WHOLE: &str = "That backup hasn't finished syncing to this computer yet. Try again in a moment.";
const NOT_OURS: &str = "That isn't one of this app's backups in your sync folder.";

/// A backup in the sync folder.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Kept {
    pub name: String,
    /// When its computer made it, by that computer's clock.
    pub at: u64,
    pub size: u64,
    pub files: usize,
    pub version: Option<String>,
    pub reason: String,
    pub device: String,
    pub os: String,
    pub hash: String,
    #[serde(skip)]
    number: u64,
    #[serde(skip)]
    file: PathBuf,
}

/// `<number>-<8 of the checksum>.zip`, and nothing else: not "… (conflicted copy).zip", not a file half arrived
/// under another name.
fn parse(name: &str) -> Option<(u64, &str)> {
    let (number, hash) = name.strip_suffix(".zip")?.split_once('-')?;
    let ok = (1..=16).contains(&number.len()) && number.bytes().all(|b| b.is_ascii_digit()) && hash.len() == 8 && hash.bytes().all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b));
    ok.then(|| number.parse().ok()).flatten().map(|n| (n, hash))
}

/// Whether a folder in the sync folder is this app's: named for it alone, or with a name for people in front.
fn dir_of(name: &str, key: &str) -> bool {
    name == key || name.strip_suffix(')').and_then(|n| n.strip_suffix(key)).is_some_and(|n| n.ends_with(" ("))
}

/// What an app's folder is called when it's made: "Zelda 64 Recompiled (zelda64)". Only the part in brackets finds
/// it again, so an app that's renamed, or named differently on another computer, keeps its folder.
fn dir_name(label: &str, key: &str) -> String {
    let plain: String = label.chars().map(|c| if c.is_ascii_alphanumeric() || c == '-' || c == '_' { c } else { ' ' }).collect();
    let plain = plain.split_whitespace().collect::<Vec<_>>().join(" ");
    let plain: String = plain.chars().take(40).collect();
    let plain = plain.trim();
    if plain.is_empty() || plain.eq_ignore_ascii_case(key) {
        return key.to_string();
    }
    format!("{plain} ({key})")
}

/// The folders, and never links to folders, in one folder.
fn dirs_in(folder: &Path) -> Vec<(String, PathBuf)> {
    let mut out: Vec<(String, PathBuf)> = fs::read_dir(folder)
        .into_iter()
        .flatten()
        .flatten()
        .take(MAX_LOOKED)
        .filter(|e| fs::symlink_metadata(e.path()).is_ok_and(|m| m.is_dir()))
        .map(|e| (e.file_name().to_string_lossy().into_owned(), e.path()))
        .collect();
    out.sort();
    out
}

/// An app's folders in the sync folder. More than one when two computers each made it before the other's arrived.
fn app_dirs(folder: &Path, key: &str) -> Vec<PathBuf> {
    dirs_in(folder).into_iter().filter(|(name, _)| dir_of(name, key)).map(|(_, path)| path).collect()
}

/// The files in these folders named like backups, newest number first.
fn named(dirs: &[PathBuf]) -> Vec<(u64, String, PathBuf, u64)> {
    let mut out = vec![];
    for dir in dirs {
        for entry in fs::read_dir(dir).into_iter().flatten().flatten().take(MAX_LOOKED) {
            let name = entry.file_name().to_string_lossy().into_owned();
            let Some((number, _)) = parse(&name) else { continue };
            // A file itself, never a link to one somewhere else.
            let Some(meta) = fs::symlink_metadata(entry.path()).ok().filter(|m| m.is_file() && m.len() > 0 && m.len() <= MAX_ZIP) else { continue };
            out.push((number, name, entry.path(), meta.len()));
        }
    }
    out.sort_by(|a, b| b.0.cmp(&a.0).then_with(|| b.1.cmp(&a.1)));
    out
}

/// A backup's manifest when it's this app's and is what its name says: read only, or checked file by file (`whole`).
fn manifest_of(file: &Path, name: &str, id: Option<&str>, whole: bool) -> Option<Manifest> {
    let manifest = if whole { saves::verify(file)? } else { saves::read_manifest(file).filter(|m| saves::sized_like_saves(m) && saves::set_hash(&m.files) == m.hash)? };
    let (_, hash) = parse(name)?;
    (id.is_none_or(|id| manifest.app == id) && manifest.hash.starts_with(hash)).then_some(manifest)
}

fn kept(number: u64, name: String, file: PathBuf, size: u64, m: Manifest) -> Kept {
    Kept { name, at: m.at, size, files: m.files.len(), version: m.version, reason: m.reason, device: m.device, os: m.os, hash: m.hash, number, file }
}

/// Every backup of an app in the folder that reads as one, newest first, the same saves twice included.
fn scan(folder: &Path, id: &str, whole_newest: bool) -> Vec<Kept> {
    let mut out = vec![];
    for (number, name, file, size) in named(&app_dirs(folder, &saves::store_name(id))).into_iter().take(MAX_READ) {
        // The newest is checked through, so what's offered before an app starts is whole. An older one is checked
        // when it's taken.
        let whole = whole_newest && out.is_empty();
        if let Some(manifest) = manifest_of(&file, &name, Some(id), whole) {
            out.push(kept(number, name, file, size, manifest));
        }
    }
    out
}

/// An app's backups in the sync folder, newest first. With `whole_newest`, the first is one that arrived whole: a
/// newer one still on its way isn't listed yet.
pub fn list(folder: &Path, id: &str, whole_newest: bool) -> Vec<Kept> {
    let mut out: Vec<Kept> = vec![];
    for copy in scan(folder, id, whole_newest) {
        if !out.iter().any(|c| c.hash == copy.hash) {
            out.push(copy);
        }
    }
    out
}

/// Keeps one of this computer's backups in the sync folder too, as its newest. Nothing is written when the newest
/// there is the same saves. `label`: what the app is called, for its folder's name.
pub fn put(folder: &Path, id: &str, label: &str, zip: &Path) -> Result<Kept, String> {
    if !folder.is_dir() {
        return Err(GONE.into());
    }
    let manifest = saves::read_manifest(zip).ok_or(saves::DAMAGED)?;
    let before = scan(folder, id, false);
    if let Some(same) = before.first().filter(|c| c.hash == manifest.hash) {
        return Ok(same.clone());
    }
    let key = saves::store_name(id);
    let dir = app_dirs(folder, &key).into_iter().next().unwrap_or_else(|| folder.join(dir_name(label, &key)));
    fs::create_dir_all(&dir).map_err(|_| CANT_WRITE.to_string())?;
    // After everything seen here, whatever this computer's clock says.
    let number = manifest.at.max(before.first().map_or(0, |c| c.number + 1));
    let name = format!("{number}-{}.zip", &manifest.hash[..8.min(manifest.hash.len())]);
    if parse(&name).is_none() {
        return Err(saves::DAMAGED.into());
    }
    // Under a name sync programs leave alone until it's whole, then its own.
    let (temp, target) = (dir.join(format!("~{name}.tmp")), dir.join(&name));
    if fs::copy(zip, &temp).and_then(|_| fs::rename(&temp, &target)).is_err() {
        let _ = fs::remove_file(&temp);
        return Err(CANT_WRITE.into());
    }
    let size = fs::metadata(&target).map(|m| m.len()).unwrap_or(0);
    // The newest few stay, each set of saves once. Only files that read as this app's backups are ever removed: one
    // still arriving from another computer, or a sync program's own files, are left as they are.
    let mut distinct = vec![manifest.hash.clone()];
    for old in &before {
        if distinct.contains(&old.hash) || distinct.len() >= KEEP {
            let _ = fs::remove_file(&old.file);
        } else {
            distinct.push(old.hash.clone());
        }
    }
    sweep(&dir);
    Ok(kept(number, name, target, size, manifest))
}

/// Takes away what a `put` that was cut short left behind, once it's old enough not to be one going on now.
fn sweep(dir: &Path) {
    for entry in fs::read_dir(dir).into_iter().flatten().flatten().take(MAX_LOOKED) {
        let name = entry.file_name().to_string_lossy().into_owned();
        let ours = name.strip_prefix('~').and_then(|n| n.strip_suffix(".tmp")).is_some_and(|n| parse(n).is_some());
        let old = entry.metadata().ok().and_then(|m| m.modified().ok()).and_then(|at| SystemTime::now().duration_since(at).ok()).is_some_and(|age| age > Duration::from_secs(3600));
        if ours && old {
            let _ = fs::remove_file(entry.path());
        }
    }
}

/// Takes one of the folder's backups in among this computer's (`store`), where Restore can put it back. It's copied
/// out first and checked through there, so what's restored is what was checked, whatever happens to the folder
/// meanwhile. Nothing of the player's saves is touched, and the folder is left as it is.
pub fn take(folder: &Path, id: &str, name: &str, store: &Path) -> Result<Backup, String> {
    parse(name).ok_or(NOT_OURS)?;
    if !folder.is_dir() {
        return Err(GONE.into());
    }
    let (_, _, file, _) = named(&app_dirs(folder, &saves::store_name(id))).into_iter().find(|(_, n, _, _)| n == name).ok_or(NOT_OURS)?;
    fs::create_dir_all(store).map_err(|e| format!("Couldn't take that backup in: {e}"))?;
    let part = store.join("folder.part");
    let copied = fs::File::open(&file).and_then(|from| io::copy(&mut from.take(MAX_ZIP + 1), &mut fs::File::create(&part)?));
    let manifest = copied.ok().filter(|size| *size <= MAX_ZIP).and_then(|_| manifest_of(&part, name, Some(id), true));
    let Some(manifest) = manifest else {
        let _ = fs::remove_file(&part);
        return Err(NOT_WHOLE.into());
    };
    saves::adopt(store, &part, &manifest.hash)
}

/// The folder backups go in for the folder the player picked: that folder when it's Quiver's already, or empty,
/// else a "Quiver Saves" folder inside it, so picking all of Dropbox doesn't fill it with a folder per app. Picking
/// the same folder on another computer comes to the same place. `data`: the launcher's own folder, which is refused.
pub fn choose(picked: &Path, data: &Path) -> Result<PathBuf, String> {
    if !picked.is_absolute() || !picked.is_dir() {
        return Err("Choose a folder on this computer.".into());
    }
    let inside = |a: &Path, b: &Path| a.starts_with(b) || fs::canonicalize(a).ok().zip(fs::canonicalize(b).ok()).is_some_and(|(a, b)| a.starts_with(b));
    if inside(picked, data) {
        return Err("That's Quiver's own folder. Choose one that syncs, like a folder in Dropbox or OneDrive.".into());
    }
    let named_ours = picked.file_name().is_some_and(|n| n.to_string_lossy().eq_ignore_ascii_case(HOME));
    let empty = fs::read_dir(picked).is_ok_and(|mut entries| entries.next().is_none());
    let folder = if named_ours || empty || picked.join(NOTE).is_file() { picked.to_path_buf() } else { picked.join(HOME) };
    let note = folder.join(NOTE);
    let written = fs::create_dir_all(&folder).and_then(|_| if note.is_file() { Ok(()) } else { fs::write(&note, NOTE_TEXT) });
    // Writing the note is also how it's known that backups can be written.
    written.map_err(|_| "Quiver can't write to that folder. Choose another.".to_string())?;
    Ok(folder)
}

/// What the sync folder holds for one app, as Settings lists it.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AppKept {
    pub id: String,
    /// What its folder there is called, for an app this computer doesn't know.
    pub label: String,
    pub copies: usize,
    pub size: u64,
    pub newest: Kept,
}

/// Every app with backups in the sync folder, the one backed up last first.
pub fn summary(folder: &Path) -> Vec<AppKept> {
    let mut out: Vec<AppKept> = vec![];
    for (dir_name, dir) in dirs_in(folder) {
        // Which app a folder is for is what its newest backup says, when the folder's name agrees.
        let id = named(std::slice::from_ref(&dir)).into_iter().take(MAX_READ).find_map(|(_, name, file, _)| manifest_of(&file, &name, None, false).map(|m| m.app).filter(|app| dir_of(&dir_name, &saves::store_name(app))));
        let Some(id) = id.filter(|id| !out.iter().any(|app| &app.id == id)) else { continue };
        let copies = list(folder, &id, false);
        let Some(newest) = copies.first().cloned() else { continue };
        let label = dir_name.strip_suffix(')').and_then(|n| n.rsplit_once(" (")).map_or(dir_name.as_str(), |(label, _)| label).to_string();
        out.push(AppKept { id, label, copies: copies.len(), size: copies.iter().map(|c| c.size).sum(), newest });
    }
    out.sort_by(|a, b| b.newest.number.cmp(&a.newest.number));
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::saves::{Roots, Stamp, Trust};

    /// One computer: an app's folder with its saves, and where this computer keeps its backups.
    struct Computer {
        _dir: tempfile::TempDir,
        roots: Roots,
        store: PathBuf,
    }

    const PLACES: [&str; 1] = ["app/saves"];

    fn computer() -> Computer {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path().join("apps/Game");
        fs::create_dir_all(app.join("saves")).unwrap();
        Computer { roots: Roots { app, ..Default::default() }, store: dir.path().join("store"), _dir: dir }
    }

    impl Computer {
        fn save(&self, text: &str) {
            fs::write(self.roots.app.join("saves/slot1.sav"), text).unwrap();
        }
        fn saved(&self) -> String {
            fs::read_to_string(self.roots.app.join("saves/slot1.sav")).unwrap_or_default()
        }
        fn places(&self) -> Vec<String> {
            PLACES.iter().map(|p| p.to_string()).collect()
        }
        /// Plays: the saves change, are backed up, and the backup goes in the sync folder.
        fn play(&self, text: &str, folder: &Path) -> Kept {
            self.save(text);
            let made = saves::backup(&self.store, &self.places(), &self.roots, &[], &Stamp { app: "game", version: Some("1.0"), reason: "played", os: "linux" }).unwrap().unwrap();
            put(folder, "game", "The Game: Recompiled!", &self.store.join(&made.name)).unwrap()
        }
        fn restore(&self, name: &str) -> Result<usize, String> {
            let known = self.places();
            saves::restore(&self.store, name, &self.roots, &[], &Stamp { app: "game", version: None, reason: "restore", os: "linux" }, &Trust { known: &known, ..Default::default() })
        }
    }

    fn folder() -> tempfile::TempDir {
        tempfile::tempdir().unwrap()
    }

    #[test]
    fn only_files_named_like_backups_are_read() {
        assert_eq!(parse("1731832000000-0a1b2c3d.zip"), Some((1731832000000, "0a1b2c3d")));
        for bad in [
            "1731832000000-0a1b2c3d (conflicted copy 2026-11-17).zip",
            "1731832000000-0a1b2c3d (LAPTOP's conflicted copy).zip",
            "1731832000000-0a1b2c3d.sync-conflict-20261117-101500-ABCDEFG.zip",
            "1731832000000-0a1b2c3d-LAPTOP.zip",
            "~1731832000000-0a1b2c3d.zip.tmp",
            ".syncthing.1731832000000-0a1b2c3d.zip.tmp",
            "1731832000000-0A1B2C3D.zip",
            "1731832000000.zip",
            "-0a1b2c3d.zip",
            "1731832000000-0a1b2c3d.zip.part",
            "../1-0a1b2c3d.zip",
        ] {
            assert_eq!(parse(bad), None, "{bad}");
        }
    }

    #[test]
    fn an_apps_folder_is_named_for_people_and_found_by_its_id() {
        assert_eq!(dir_name("The Game: Recompiled!", "game"), "The Game Recompiled (game)");
        assert_eq!(dir_name("", "game"), "game");
        assert_eq!(dir_name("Game", "game"), "game");
        assert!(dir_of("The Game Recompiled (game)", "game"));
        assert!(dir_of("Renamed Since (game)", "game"));
        assert!(dir_of("game", "game"));
        assert!(!dir_of("Another (game2)", "game"));
        assert!(!dir_of("endgame", "game"));
        assert!(!dir_of("Other(game)", "game"));
    }

    #[test]
    fn saves_follow_the_player_to_another_computer() {
        let (desktop, laptop, shared) = (computer(), computer(), folder());
        let first = desktop.play("desktop 1", shared.path());
        assert!(shared.path().join("The Game Recompiled (game)").join(&first.name).is_file());

        let seen = list(shared.path(), "game", true);
        assert_eq!(seen.len(), 1);
        assert_eq!(seen[0].hash, first.hash);
        let taken = take(shared.path(), "game", &seen[0].name, &laptop.store).unwrap();
        assert_eq!(laptop.restore(&taken.name).unwrap(), 1);
        assert_eq!(laptop.saved(), "desktop 1");

        // And back: the laptop's is the newest then, in the same folder of the app's.
        let second = laptop.play("laptop 2", shared.path());
        let seen = list(shared.path(), "game", true);
        assert_eq!(seen.iter().map(|c| c.hash.as_str()).collect::<Vec<_>>(), [second.hash.as_str(), first.hash.as_str()]);
        assert_eq!(dirs_in(shared.path()).len(), 1);
    }

    #[test]
    fn the_same_saves_are_not_written_twice_and_the_newest_few_stay() {
        let (pc, shared) = (computer(), folder());
        let first = pc.play("1", shared.path());
        let again = put(shared.path(), "game", "Game", &pc.store.join(&saves::list(&pc.store)[0].name)).unwrap();
        assert_eq!(again.name, first.name);
        for n in 2..=KEEP + 2 {
            pc.play(&n.to_string(), shared.path());
        }
        let seen = list(shared.path(), "game", true);
        assert_eq!(seen.len(), KEEP);
        assert!(seen.iter().all(|c| c.hash != first.hash));
        // Going back to earlier saves makes them the newest, once.
        pc.save("4");
        let made = saves::backup(&pc.store, &pc.places(), &pc.roots, &[], &Stamp { app: "game", version: None, reason: "manual", os: "linux" }).unwrap().unwrap();
        put(shared.path(), "game", "Game", &pc.store.join(&made.name)).unwrap();
        let seen = scan(shared.path(), "game", false);
        assert_eq!(seen[0].hash, made.hash);
        assert_eq!(seen.iter().filter(|c| c.hash == made.hash).count(), 1);
    }

    #[test]
    fn a_computer_whose_clock_is_behind_still_writes_the_newest() {
        let (pc, shared) = (computer(), folder());
        let first = pc.play("1", shared.path());
        // As if another computer, an hour ahead, had put this one there.
        let dir = shared.path().join("The Game Recompiled (game)");
        let ahead = first.number + 3_600_000;
        fs::rename(dir.join(&first.name), dir.join(format!("{ahead}-{}.zip", &first.hash[..8]))).unwrap();
        let second = pc.play("2", shared.path());
        assert_eq!(second.number, ahead + 1);
        assert_eq!(list(shared.path(), "game", true)[0].hash, second.hash);
    }

    #[test]
    fn what_a_sync_program_leaves_is_neither_read_nor_removed() {
        let (pc, shared) = (computer(), folder());
        let first = pc.play("1", shared.path());
        let dir = shared.path().join("The Game Recompiled (game)");
        let conflicted = dir.join(format!("{} (conflicted copy).zip", first.name.trim_end_matches(".zip")));
        fs::copy(dir.join(&first.name), &conflicted).unwrap();
        fs::write(dir.join(".syncthing.9999999999999-0a1b2c3d.zip.tmp"), "arriving").unwrap();
        fs::write(dir.join("desktop.ini"), "").unwrap();
        for n in 2..=KEEP + 3 {
            pc.play(&n.to_string(), shared.path());
        }
        assert_eq!(list(shared.path(), "game", true).len(), KEEP);
        assert!(conflicted.is_file());
        assert!(dir.join(".syncthing.9999999999999-0a1b2c3d.zip.tmp").is_file());
        assert!(dir.join("desktop.ini").is_file());
    }

    #[test]
    fn a_backup_still_arriving_is_passed_over_and_left_alone() {
        let (desktop, laptop, shared) = (computer(), computer(), folder());
        let first = desktop.play("1", shared.path());
        let second = desktop.play("2 with more in it", shared.path());
        let dir = shared.path().join("The Game Recompiled (game)");
        let whole = fs::read(dir.join(&second.name)).unwrap();

        // Cut short: the end of a zip is what says what's in it.
        fs::write(dir.join(&second.name), &whole[..whole.len() / 2]).unwrap();
        assert_eq!(list(shared.path(), "game", true)[0].hash, first.hash);
        assert_eq!(take(shared.path(), "game", &second.name, &laptop.store).unwrap_err(), NOT_WHOLE);

        // Its full size with a hole in it, as some programs write: the manifest reads, the saves don't add up.
        let mut holed = whole.clone();
        let at = holed.windows(11).position(|w| w == b"0/slot1.sav").unwrap() + 11;
        holed[at..at + 4].iter_mut().for_each(|b| *b = !*b);
        assert!(saves::read_manifest(&dir.join(&second.name)).is_none());
        fs::write(dir.join(&second.name), &holed).unwrap();
        assert!(saves::read_manifest(&dir.join(&second.name)).is_some());
        assert_eq!(list(shared.path(), "game", true)[0].hash, first.hash);
        assert!(take(shared.path(), "game", &second.name, &laptop.store).is_err());
        assert!(saves::list(&laptop.store).is_empty());

        // Nothing was removed for not being whole, and once it is, it's the newest.
        assert!(dir.join(&second.name).is_file());
        fs::write(dir.join(&second.name), &whole).unwrap();
        assert_eq!(list(shared.path(), "game", true)[0].hash, second.hash);
        let taken = take(shared.path(), "game", &second.name, &laptop.store).unwrap();
        laptop.restore(&taken.name).unwrap();
        assert_eq!(laptop.saved(), "2 with more in it");
    }

    #[test]
    fn a_backup_filed_under_another_app_or_another_name_is_not_taken() {
        let (pc, other, shared) = (computer(), computer(), folder());
        let real = pc.play("1", shared.path());
        let dir = shared.path().join("The Game Recompiled (game)");

        // Another app's backup, dropped in this app's folder.
        other.save("other app");
        let made = saves::backup(&other.store, &other.places(), &other.roots, &[], &Stamp { app: "other", version: None, reason: "played", os: "linux" }).unwrap().unwrap();
        let planted = format!("{}-{}.zip", real.number + 10, &made.hash[..8]);
        fs::copy(other.store.join(&made.name), dir.join(&planted)).unwrap();
        // This app's, under a name that says other saves.
        let renamed = format!("{}-00000000.zip", real.number + 20);
        fs::copy(dir.join(&real.name), dir.join(&renamed)).unwrap();
        // Not a zip at all, and a link out of the folder.
        fs::write(dir.join(format!("{}-11111111.zip", real.number + 30)), "not a zip").unwrap();
        #[cfg(unix)]
        std::os::unix::fs::symlink(other.store.join(&made.name), dir.join(format!("{}-{}.zip", real.number + 40, &made.hash[..8]))).unwrap();

        let seen = list(shared.path(), "game", true);
        assert_eq!(seen.len(), 1);
        assert_eq!(seen[0].name, real.name);
        for name in [planted.as_str(), renamed.as_str(), "../../store/1.zip", "nope.zip"] {
            assert!(take(shared.path(), "game", name, &pc.store).is_err(), "{name}");
        }
    }

    #[test]
    fn a_backup_from_the_folder_never_chooses_where_its_files_go() {
        let (pc, stranger, shared) = (computer(), computer(), folder());
        pc.play("mine", shared.path());
        // Made for this app by someone else with the folder, naming a place this computer doesn't keep its saves in.
        fs::create_dir_all(stranger.roots.app.join("bin")).unwrap();
        fs::write(stranger.roots.app.join("bin/settings.cfg"), "theirs").unwrap();
        let made = saves::backup(&stranger.store, &["app/bin".to_string()], &stranger.roots, &[], &Stamp { app: "game", version: None, reason: "played", os: "linux" }).unwrap().unwrap();
        let planted = put(shared.path(), "game", "Game", &stranger.store.join(&made.name)).unwrap();

        // It adds up, so it's listed and can be taken in as a backup; putting it in place is refused.
        let taken = take(shared.path(), "game", &planted.name, &pc.store).unwrap();
        assert!(pc.restore(&taken.name).is_err());
        assert_eq!(pc.saved(), "mine");
        assert!(!pc.roots.app.join("bin").exists());
    }

    #[test]
    fn the_folder_picked_becomes_one_of_quivers_own() {
        let (dir, data) = (folder(), folder());
        let dropbox = dir.path().join("Dropbox");
        fs::create_dir_all(dropbox.join("Photos")).unwrap();
        // All of Dropbox: a folder of Quiver's inside it, the same from every computer and however it's picked.
        let made = choose(&dropbox, data.path()).unwrap();
        assert_eq!(made, dropbox.join(HOME));
        assert!(made.join(NOTE).is_file());
        assert_eq!(choose(&dropbox, data.path()).unwrap(), made);
        assert_eq!(choose(&made, data.path()).unwrap(), made);
        // Before its note has synced here.
        fs::remove_file(made.join(NOTE)).unwrap();
        fs::create_dir_all(made.join("game")).unwrap();
        assert_eq!(choose(&made, data.path()).unwrap(), made);
        // An empty folder made for it is used as it is.
        let own = dir.path().join("My saves");
        fs::create_dir_all(&own).unwrap();
        assert_eq!(choose(&own, data.path()).unwrap(), own);
        assert_eq!(choose(&own, data.path()).unwrap(), own);
        // Never the launcher's own folder, or something that isn't a folder.
        assert!(choose(&data.path().join("saves"), data.path()).is_err());
        fs::create_dir_all(data.path().join("saves")).unwrap();
        assert!(choose(&data.path().join("saves"), data.path()).is_err());
        assert!(choose(Path::new("relative"), data.path()).is_err());
    }

    #[test]
    fn settings_lists_every_app_with_backups_there() {
        let (pc, shared) = (computer(), folder());
        choose(shared.path(), pc.store.as_path()).unwrap();
        assert!(summary(shared.path()).is_empty());
        pc.play("1", shared.path());
        pc.play("2", shared.path());
        // A folder that isn't an app's, and one whose backups are another app's.
        fs::create_dir_all(shared.path().join("Photos")).unwrap();
        fs::create_dir_all(shared.path().join("Other (other)")).unwrap();
        let dir = shared.path().join("The Game Recompiled (game)");
        let name = named(&[dir.clone()])[0].1.clone();
        fs::copy(dir.join(&name), shared.path().join("Other (other)").join(&name)).unwrap();
        let apps = summary(shared.path());
        assert_eq!(apps.len(), 1);
        assert_eq!((apps[0].id.as_str(), apps[0].label.as_str(), apps[0].copies), ("game", "The Game Recompiled", 2));
    }

    #[test]
    fn a_folder_that_is_gone_says_so() {
        let (pc, shared) = (computer(), folder());
        let gone = shared.path().join("unplugged");
        pc.save("1");
        let made = saves::backup(&pc.store, &pc.places(), &pc.roots, &[], &Stamp { app: "game", version: None, reason: "played", os: "linux" }).unwrap().unwrap();
        assert_eq!(put(&gone, "game", "Game", &pc.store.join(&made.name)).unwrap_err(), GONE);
        assert!(list(&gone, "game", true).is_empty());
        assert_eq!(take(&gone, "game", "1-0a1b2c3d.zip", &pc.store).unwrap_err(), GONE);
    }
}
