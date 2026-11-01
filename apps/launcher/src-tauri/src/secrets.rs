//! Sign-in tokens, which Convex Auth keeps through `secret_get` and
//! `secret_set`: in the OS keychain, or in `account.json` in the data folder.
//!
//! Where they go is the same in every session of a computer, as a sign-in
//! has to be found in all of them. The website rotates the refresh token on
//! each refresh (every minute or so) and ends the session when a spent one is
//! presented, so a copy left behind where another session reads doesn't just
//! fail: it signs the player out everywhere.
use serde_json::{Map, Value};
use std::fs;
use std::io::{self, Write};
use std::path::Path;
use std::sync::OnceLock;

const FILE: &str = "account.json";

/// Where a computer keeps tokens.
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Store {
    /// The keychain, or the file where it can't be reached. A token in the
    /// file stays there until sign-out, as a session without the keychain
    /// saved it and reads it there.
    Keychain,
    /// The file, never the keychain.
    File,
    /// The file, as on a computer with a Game Mode; but a token Quiver
    /// Launcher 4.0 left in the keychain, where it could reach it (Desktop
    /// Mode, with KWallet open), moves to the file the first time it's read
    /// where the keychain can be reached, so the player stays signed in. It
    /// leaves the keychain as it goes: one copy, which each refresh rotates.
    FileFromKeychain,
}

/// The OS keychain; a stand-in in the tests.
pub trait Keychain {
    /// The saved value (`None` where there's none), or why the keychain can't be reached.
    fn get(&self, key: &str) -> Result<Option<String>, String>;
    fn set(&self, key: &str, value: &str) -> Result<(), String>;
    fn delete(&self, key: &str) -> Result<(), String>;
}

pub struct OsKeychain;

fn entry(key: &str) -> Result<keyring::Entry, String> {
    keyring::Entry::new("Quiver Launcher", key).map_err(|e| e.to_string())
}

impl Keychain for OsKeychain {
    fn get(&self, key: &str) -> Result<Option<String>, String> {
        match entry(key)?.get_password() {
            Ok(value) => Ok(Some(value)),
            Err(keyring::Error::NoEntry) => Ok(None),
            Err(e) => Err(e.to_string()),
        }
    }
    fn set(&self, key: &str, value: &str) -> Result<(), String> {
        entry(key)?.set_password(value).map_err(|e| e.to_string())
    }
    fn delete(&self, key: &str) -> Result<(), String> {
        match entry(key)?.delete_credential() {
            Ok(()) | Err(keyring::Error::NoEntry) => Ok(()),
            Err(e) => Err(e.to_string()),
        }
    }
}

/// This computer's store. The file on Android, where keyring has only its
/// mock, which keeps nothing (the app's data folder is private to it there);
/// with `QUIVER_DATA`, so tests never touch the real keychain; and on a
/// computer with a Game Mode.
pub fn store() -> Store {
    static STORE: OnceLock<Store> = OnceLock::new();
    *STORE.get_or_init(|| {
        let game_mode = cfg!(target_os = "linux") && {
            let os_release = ["/run/host/os-release", "/etc/os-release", "/usr/lib/os-release"].iter().find_map(|p| fs::read_to_string(p).ok());
            let sessions: Vec<String> = fs::read_dir("/usr/share/wayland-sessions").into_iter().flatten().flatten().map(|e| e.file_name().to_string_lossy().into_owned()).collect();
            has_game_mode(&std::env::var("XDG_CURRENT_DESKTOP").unwrap_or_default(), &os_release.unwrap_or_default(), &sessions)
        };
        if cfg!(target_os = "android") || std::env::var_os("QUIVER_DATA").is_some() {
            Store::File
        } else if game_mode {
            Store::FileFromKeychain
        } else {
            Store::Keychain
        }
    })
}

/// Whether this computer has a Game Mode: a gamescope session, as on a Steam
/// Deck. Steam runs there without any keychain, so a sign-in from either
/// mode has to be in the file. (SteamOS's Desktop Mode, signed in without a
/// password, doesn't open KWallet either.) A Flatpak can't see the system's
/// sessions, only its os-release (/run/host/os-release).
fn has_game_mode(desktop: &str, os_release: &str, sessions: &[String]) -> bool {
    let steamos = os_release
        .lines()
        .filter_map(|line| line.strip_prefix("ID=").or_else(|| line.strip_prefix("ID_LIKE=")))
        .any(|ids| ids.trim_matches('"').split_whitespace().any(|id| id == "steamos"));
    steamos || desktop.split(':').any(|d| d.eq_ignore_ascii_case("gamescope")) || sessions.iter().any(|s| s.starts_with("gamescope"))
}

fn read(dir: &Path) -> Map<String, Value> {
    fs::read(dir.join(FILE)).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default()
}

/// Replaces the file whole, so another copy of the launcher reading it never
/// finds it half written. Only its owner can read it.
fn write(dir: &Path, all: &Map<String, Value>) -> io::Result<()> {
    fs::create_dir_all(dir)?;
    let temp = dir.join(format!("{FILE}.{}.tmp", std::process::id()));
    let mut file = fs::File::create(&temp)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        file.set_permissions(fs::Permissions::from_mode(0o600))?;
    }
    file.write_all(&serde_json::to_vec(all)?)?;
    drop(file);
    fs::rename(&temp, dir.join(FILE))
}

/// The file's token first: where it has one, that's the latest.
pub fn get(dir: &Path, keychain: &impl Keychain, store: Store, key: &str) -> Option<String> {
    let mut all = read(dir);
    if let Some(value) = all.get(key) {
        return value.as_str().map(str::to_string);
    }
    match store {
        Store::Keychain => keychain.get(key).ok().flatten(),
        Store::File => None,
        Store::FileFromKeychain => {
            let value = keychain.get(key).ok().flatten()?;
            all.insert(key.into(), value.as_str().into());
            // Out of the keychain only once it's safely in the file.
            write(dir, &all).ok()?;
            let _ = keychain.delete(key);
            Some(value)
        }
    }
}

/// Saves a token, or with `None` (signed out) removes it from both.
pub fn set(dir: &Path, keychain: &impl Keychain, store: Store, key: &str, value: Option<&str>) -> Result<(), String> {
    let mut all = read(dir);
    let Some(value) = value else {
        if store != Store::File {
            let _ = keychain.delete(key);
        }
        return match all.remove(key) {
            Some(_) => write(dir, &all).map_err(|e| e.to_string()),
            None => Ok(()),
        };
    };
    if store == Store::Keychain && !all.contains_key(key) && keychain.set(key, value).is_ok() {
        return Ok(());
    }
    all.insert(key.into(), value.into());
    write(dir, &all).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::{Cell, RefCell};
    use std::collections::HashMap;

    /// A keychain in memory that can be out of reach, as KWallet is in a
    /// Steam Deck's Game Mode.
    #[derive(Default)]
    struct Fake {
        items: RefCell<HashMap<String, String>>,
        unreachable: Cell<bool>,
    }

    impl Keychain for Fake {
        fn get(&self, key: &str) -> Result<Option<String>, String> {
            self.reach()?;
            Ok(self.items.borrow().get(key).cloned())
        }
        fn set(&self, key: &str, value: &str) -> Result<(), String> {
            self.reach()?;
            self.items.borrow_mut().insert(key.into(), value.into());
            Ok(())
        }
        fn delete(&self, key: &str) -> Result<(), String> {
            self.reach()?;
            self.items.borrow_mut().remove(key);
            Ok(())
        }
    }

    impl Fake {
        fn reach(&self) -> Result<(), String> {
            if self.unreachable.get() {
                Err("The name is not activatable".into())
            } else {
                Ok(())
            }
        }
    }

    const KEY: &str = "__convexAuthRefreshToken_httpsconvexquiverlaunchercom";

    /// Signed in in one mode, then each refresh alternating between the
    /// modes: every read finds the latest token.
    fn switch_modes(store: Store, keychain_first: bool) {
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        keychain.unreachable.set(!keychain_first);
        set(data.path(), &keychain, store, KEY, Some("token 0")).unwrap();
        for n in 1..6 {
            keychain.unreachable.set(!keychain.unreachable.get());
            assert_eq!(get(data.path(), &keychain, store, KEY).as_deref(), Some(format!("token {}", n - 1).as_str()));
            set(data.path(), &keychain, store, KEY, Some(&format!("token {n}"))).unwrap();
        }
        set(data.path(), &keychain, store, KEY, None).unwrap();
        keychain.unreachable.set(false);
        assert_eq!(get(data.path(), &keychain, store, KEY), None);
    }

    #[test]
    fn a_steam_deck_keeps_one_sign_in_across_its_modes() {
        // The file, whichever mode signed in.
        switch_modes(Store::File, false);
        switch_modes(Store::File, true);
        switch_modes(Store::FileFromKeychain, false);
        switch_modes(Store::FileFromKeychain, true);
    }

    #[test]
    fn a_steam_deck_moves_a_sign_in_from_the_keychain_to_the_file() {
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        keychain.items.borrow_mut().insert(KEY.into(), "from 4.0".into());
        // Game Mode, with no keychain: not found yet.
        keychain.unreachable.set(true);
        assert_eq!(get(data.path(), &keychain, Store::FileFromKeychain, KEY), None);
        // Desktop Mode, KWallet open: it moves to the file, and out of the keychain.
        keychain.unreachable.set(false);
        assert_eq!(get(data.path(), &keychain, Store::FileFromKeychain, KEY).as_deref(), Some("from 4.0"));
        assert!(keychain.items.borrow().is_empty());
        // Game Mode finds it there, and the next refresh's token replaces it.
        keychain.unreachable.set(true);
        assert_eq!(get(data.path(), &keychain, Store::FileFromKeychain, KEY).as_deref(), Some("from 4.0"));
        set(data.path(), &keychain, Store::FileFromKeychain, KEY, Some("refreshed")).unwrap();
        assert_eq!(get(data.path(), &keychain, Store::FileFromKeychain, KEY).as_deref(), Some("refreshed"));
    }

    #[test]
    fn a_token_saved_where_the_keychain_was_out_of_reach_stays_in_the_file() {
        // Elsewhere: signed in without the keychain, the file is kept on even where the keychain is back.
        switch_modes(Store::Keychain, false);
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        keychain.unreachable.set(true);
        set(data.path(), &keychain, Store::Keychain, KEY, Some("a")).unwrap();
        keychain.unreachable.set(false);
        set(data.path(), &keychain, Store::Keychain, KEY, Some("b")).unwrap();
        assert!(keychain.items.borrow().is_empty());
        keychain.unreachable.set(true);
        assert_eq!(get(data.path(), &keychain, Store::Keychain, KEY).as_deref(), Some("b"));
    }

    #[test]
    fn the_keychain_where_it_can_be_reached() {
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        set(data.path(), &keychain, Store::Keychain, KEY, Some("a")).unwrap();
        assert_eq!(keychain.items.borrow().get(KEY).map(String::as_str), Some("a"));
        assert!(!data.path().join(FILE).exists());
        assert_eq!(get(data.path(), &keychain, Store::Keychain, KEY).as_deref(), Some("a"));
        set(data.path(), &keychain, Store::Keychain, KEY, None).unwrap();
        assert!(keychain.items.borrow().is_empty());
        // Other keys in the file are left as they are.
        fs::write(data.path().join(FILE), r#"{"other":"x"}"#).unwrap();
        set(data.path(), &keychain, Store::Keychain, KEY, Some("b")).unwrap();
        assert_eq!(read(data.path()).get("other"), Some(&Value::from("x")));
        assert_eq!(read(data.path()).get(KEY), None);
    }

    #[test]
    fn the_file_never_touches_the_keychain() {
        // As with QUIVER_DATA in the end-to-end tests, a token in the real keychain stays out of it.
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        keychain.items.borrow_mut().insert(KEY.into(), "someone's".into());
        assert_eq!(get(data.path(), &keychain, Store::File, KEY), None);
        set(data.path(), &keychain, Store::File, KEY, Some("a")).unwrap();
        set(data.path(), &keychain, Store::File, KEY, None).unwrap();
        assert_eq!(keychain.items.borrow().get(KEY).map(String::as_str), Some("someone's"));
    }

    #[test]
    fn signing_out_removes_both_copies() {
        let data = tempfile::tempdir().unwrap();
        let keychain = Fake::default();
        keychain.items.borrow_mut().insert(KEY.into(), "old".into());
        fs::write(data.path().join(FILE), format!(r#"{{"{KEY}":"new"}}"#)).unwrap();
        assert_eq!(get(data.path(), &keychain, Store::Keychain, KEY).as_deref(), Some("new"));
        set(data.path(), &keychain, Store::Keychain, KEY, None).unwrap();
        assert_eq!(get(data.path(), &keychain, Store::Keychain, KEY), None);
    }

    #[cfg(unix)]
    #[test]
    fn only_its_owner_can_read_the_file() {
        use std::os::unix::fs::PermissionsExt;
        let data = tempfile::tempdir().unwrap();
        fs::write(data.path().join(FILE), "{}").unwrap();
        fs::set_permissions(data.path().join(FILE), fs::Permissions::from_mode(0o644)).unwrap();
        set(data.path(), &Fake::default(), Store::File, KEY, Some("a")).unwrap();
        assert_eq!(fs::metadata(data.path().join(FILE)).unwrap().permissions().mode() & 0o777, 0o600);
        let left: Vec<_> = fs::read_dir(data.path()).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(left, [FILE]);
    }

    #[test]
    fn finds_a_game_mode() {
        let steamos = "NAME=\"SteamOS\"\nID=steamos\nID_LIKE=arch\nVARIANT_ID=steamdeck\n";
        let fedora = "NAME=\"Fedora Linux\"\nID=fedora\n";
        let desktop = ["plasma.desktop".to_string()];
        // SteamOS, from the Flatpak too, whose desktop sessions it can't see.
        assert!(has_game_mode("KDE", steamos, &[]));
        // In Game Mode itself.
        assert!(has_game_mode("gamescope", fedora, &desktop));
        // A gamescope session to switch to, as other Steam Deck systems have.
        assert!(has_game_mode("KDE", fedora, &["gamescope-session-steam.desktop".into(), "plasma.desktop".into()]));
        assert!(has_game_mode("KDE", "ID=\"holo\"\nID_LIKE=\"steamos arch\"\n", &[]));
        assert!(!has_game_mode("KDE", fedora, &desktop));
        assert!(!has_game_mode("ubuntu:GNOME", "ID=ubuntu\nID_LIKE=debian\n", &["ubuntu.desktop".into()]));
    }
}
