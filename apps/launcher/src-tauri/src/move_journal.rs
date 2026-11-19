//! The app moves under way, in `moves.json` in the data folder, so the launcher never loses track of an app it moved.
//!
//! A move is written down before its app's folder leaves the old place. Its old copy (set aside when it was copied to
//! another drive) is only deleted once `installs.json` records the app in its new place (`recorded`). A launcher
//! closed or stopped in between finds the move here when it starts again (`recover`): an app whose folder got there
//! is recorded there, and one whose folder didn't is left as it was.
use serde::{Deserialize, Serialize};
use serde_json::{Map, Value};
use std::fs;
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

const FILE: &str = "moves.json";
const INSTALLS: &str = "installs.json";

/// One read-change-write of the journal at a time: moves run off the window's thread while it saves the installs.
static LOCK: Mutex<()> = Mutex::new(());

/// A move of one app's folder.
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    pub id: String,
    pub from: PathBuf,
    pub to: PathBuf,
    /// Where the old copy is set aside once copied (to another drive), until the new place is recorded.
    pub retired: PathBuf,
    /// The new place is in an apps folder.
    #[serde(default)]
    pub in_apps: bool,
    /// The console folder it's in there ("Ports/N64").
    #[serde(default)]
    pub group: Option<String>,
}

fn read(data: &Path) -> Vec<Entry> {
    fs::read(data.join(FILE)).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default()
}

/// Written whole and flushed to the drive before it replaces the last one, so a move is never on its way without it.
fn write_file(path: &Path, bytes: &[u8]) -> io::Result<()> {
    let temp = path.with_extension("json.tmp");
    let mut file = fs::File::create(&temp)?;
    file.write_all(bytes)?;
    file.sync_all()?;
    fs::rename(temp, path)
}

fn write(data: &Path, entries: &[Entry]) -> io::Result<()> {
    if entries.is_empty() {
        return match fs::remove_file(data.join(FILE)) {
            Err(e) if e.kind() != io::ErrorKind::NotFound => Err(e),
            _ => Ok(()),
        };
    }
    fs::create_dir_all(data)?;
    write_file(&data.join(FILE), &serde_json::to_vec_pretty(entries)?)
}

/// Writes a move down, before anything of it happens.
pub fn begin(data: &Path, entry: Entry) -> Result<(), String> {
    let _held = LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let mut entries: Vec<Entry> = read(data).into_iter().filter(|e| e.id != entry.id).collect();
    entries.push(entry);
    write(data, &entries).map_err(|e| format!("Couldn't note the move down: {e}"))
}

/// Forgets a move that didn't happen: its app is whole where it was.
pub fn cancel(data: &Path, id: &str) {
    let _held = LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let entries: Vec<Entry> = read(data).into_iter().filter(|e| e.id != id).collect();
    let _ = write(data, &entries);
}

/// Whether two paths name one folder, however they're written.
fn same(a: &Path, b: &Path) -> bool {
    a == b || a.canonicalize().ok().zip(b.canonicalize().ok()).is_some_and(|(a, b)| a == b) || (cfg!(windows) && a.to_string_lossy().eq_ignore_ascii_case(&b.to_string_lossy()))
}

/// Whether an app's record has it at `to`: its own path, or, with none, its folder in the apps folder.
fn records_at(record: &Value, to: &Path, apps_dir: &Path) -> bool {
    match record.get("dir").and_then(Value::as_str) {
        Some(dir) => same(Path::new(dir), to),
        None => record.get("folder").and_then(Value::as_str).is_some_and(|folder| same(&apps_dir.join(folder), to)),
    }
}

/// Takes the moves `installs` (what was just saved as `installs.json`) records as done off the journal, and gives
/// their old copies, for deleting now.
pub fn recorded(data: &Path, installs: &Value, apps_dir: &Path) -> Vec<PathBuf> {
    let _held = LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let entries = read(data);
    if entries.is_empty() {
        return vec![];
    }
    let (done, waiting): (Vec<Entry>, Vec<Entry>) = entries.into_iter().partition(|e| installs.get(&e.id).is_some_and(|r| records_at(r, &e.to, apps_dir)));
    if done.is_empty() || write(data, &waiting).is_err() {
        return vec![];
    }
    done.into_iter().map(|e| e.retired).filter(|r| r.exists()).collect()
}

/// An app's record once its folder is at `entry.to`: one of the apps folder's own when it's there under its own name,
/// else with that path.
fn moved_record(record: &Map<String, Value>, entry: &Entry, apps_dir: &Path) -> Map<String, Value> {
    let mut record = record.clone();
    for field in ["dir", "inApps", "group"] {
        record.remove(field);
    }
    let own = entry.group.is_none()
        && entry.to.parent().is_some_and(|p| same(p, apps_dir))
        && record.get("folder").and_then(Value::as_str).is_some_and(|f| entry.to.file_name().is_some_and(|n| n == f));
    if !own {
        record.insert("dir".into(), Value::String(entry.to.to_string_lossy().into_owned()));
        if entry.in_apps {
            record.insert("inApps".into(), Value::Bool(true));
        }
        if let Some(group) = &entry.group {
            record.insert("group".into(), Value::String(group.clone()));
        }
    }
    record
}

/// At start, before the launcher reads `installs.json`: records each app a move got to its new place without the
/// launcher recording it there, and forgets the moves that didn't get that far, whose apps are where they were. Gives
/// the old copies to delete, and says what it did.
pub fn recover(data: &Path, apps_dir: &Path) -> (Vec<PathBuf>, Vec<String>) {
    let _held = LOCK.lock().unwrap_or_else(|e| e.into_inner());
    let entries = read(data);
    let (mut retired, mut said) = (vec![], vec![]);
    if entries.is_empty() {
        return (retired, said);
    }
    let path = data.join(INSTALLS);
    let mut installs: Map<String, Value> = match fs::read(&path).ok().and_then(|b| serde_json::from_slice(&b).ok()) {
        Some(Value::Object(map)) => map,
        // Nothing to record them in, or a file it can't read: the journal waits, the folders untouched.
        _ => return (retired, vec!["Moves under way, but no installs to record them in.".into()]),
    };
    let mut changed = false;
    for entry in &entries {
        let Some(record) = installs.get(&entry.id).and_then(Value::as_object) else {
            // Uninstalled since: whatever's in either place stays, for the player to see to.
            said.push(format!("A move of {} whose app isn't installed any more: left {} as it is.", entry.id, entry.to.display()));
            continue;
        };
        if records_at(&Value::Object(record.clone()), &entry.to, apps_dir) {
            retired.push(entry.retired.clone());
        } else if entry.to.is_dir() && !entry.from.exists() {
            let next = moved_record(record, entry, apps_dir);
            installs.insert(entry.id.clone(), Value::Object(next));
            changed = true;
            retired.push(entry.retired.clone());
            said.push(format!("Recorded {} in {}, where a move had put it.", entry.id, entry.to.display()));
        } else if entry.to.exists() {
            // Copied but not finished: the original's whole where it was and recorded there; the copy is spare.
            said.push(format!("A move of {} that didn't finish: it stays in {}, and {} is a spare copy.", entry.id, entry.from.display(), entry.to.display()));
        }
    }
    if changed {
        let bytes = match serde_json::to_vec_pretty(&Value::Object(installs)) {
            Ok(b) => b,
            Err(e) => return (vec![], vec![format!("Couldn't record the moved apps: {e}")]),
        };
        if let Err(e) = write_file(&path, &bytes) {
            // The journal stays for the next start.
            return (vec![], vec![format!("Couldn't record the moved apps: {e}")]);
        }
    }
    let _ = write(data, &[]);
    (retired.into_iter().filter(|r| r.exists()).collect(), said)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn entry(id: &str, from: &Path, to: &Path) -> Entry {
        Entry { id: id.into(), from: from.into(), to: to.into(), retired: from.with_file_name(".Game.quiver-moved"), in_apps: true, group: None }
    }

    #[test]
    fn a_move_goes_off_the_journal_once_its_new_place_is_saved() {
        let data = tempfile::tempdir().unwrap();
        let apps = data.path().join("apps");
        let (from, to) = (apps.join("Game"), data.path().join("Games/Game"));
        begin(data.path(), entry("g", &from, &to)).unwrap();
        fs::create_dir_all(from.with_file_name(".Game.quiver-moved")).unwrap();
        // Saved before the move's recorded: still waiting, the old copy kept.
        assert!(recorded(data.path(), &json!({ "g": { "folder": "Game" } }), &apps).is_empty());
        assert_eq!(read(data.path()).len(), 1);
        let old = recorded(data.path(), &json!({ "g": { "folder": "Game", "dir": to } }), &apps);
        assert_eq!(old, [from.with_file_name(".Game.quiver-moved")]);
        assert!(read(data.path()).is_empty() && !data.path().join(FILE).exists());
        // One that didn't happen is just forgotten.
        begin(data.path(), entry("h", &from, &to)).unwrap();
        cancel(data.path(), "h");
        assert!(read(data.path()).is_empty());
    }

    #[test]
    fn a_launcher_closed_mid_move_records_the_app_where_it_got_to() {
        let data = tempfile::tempdir().unwrap();
        let apps = data.path().join("apps");
        let elsewhere = data.path().join("D/Games");
        // Moved, not recorded: Game to another folder in its console's; Kart into the apps folder under its own name.
        let (game_from, game_to) = (apps.join("Game"), elsewhere.join("Ports/N64/Game"));
        fs::create_dir_all(game_to.join("saves")).unwrap();
        fs::create_dir_all(apps.join(".Game.quiver-moved")).unwrap();
        let (kart_from, kart_to) = (data.path().join("old/Kart"), apps.join("Kart"));
        fs::create_dir_all(&kart_to).unwrap();
        // Copied, not finished: Zelda's still whole where it was.
        let (zelda_from, zelda_to) = (apps.join("Zelda"), elsewhere.join("Zelda"));
        fs::create_dir_all(&zelda_from).unwrap();
        fs::create_dir_all(&zelda_to).unwrap();
        let installs = json!({
            "game": { "folder": "Game", "version": "1" },
            "kart": { "folder": "Kart", "dir": kart_from, "inApps": true },
            "zelda": { "folder": "Zelda" },
        });
        fs::write(data.path().join(INSTALLS), serde_json::to_vec(&installs).unwrap()).unwrap();
        begin(data.path(), Entry { group: Some("Ports/N64".into()), ..entry("game", &game_from, &game_to) }).unwrap();
        begin(data.path(), entry("kart", &kart_from, &kart_to)).unwrap();
        begin(data.path(), entry("zelda", &zelda_from, &zelda_to)).unwrap();
        let (old, said) = recover(data.path(), &apps);
        assert_eq!(old, [apps.join(".Game.quiver-moved")], "{said:?}");
        let now: Value = serde_json::from_slice(&fs::read(data.path().join(INSTALLS)).unwrap()).unwrap();
        assert_eq!(now["game"], json!({ "folder": "Game", "version": "1", "dir": game_to, "inApps": true, "group": "Ports/N64" }));
        assert_eq!(now["kart"], json!({ "folder": "Kart" }));
        assert_eq!(now["zelda"], json!({ "folder": "Zelda" }));
        assert!(zelda_from.is_dir() && zelda_to.is_dir());
        assert!(read(data.path()).is_empty());
    }

    #[test]
    fn with_no_installs_to_record_in_the_journal_waits() {
        let data = tempfile::tempdir().unwrap();
        let apps = data.path().join("apps");
        begin(data.path(), entry("g", &apps.join("Game"), &data.path().join("Games/Game"))).unwrap();
        let (old, _) = recover(data.path(), &apps);
        assert!(old.is_empty());
        assert_eq!(read(data.path()).len(), 1);
    }
}
