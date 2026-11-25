//! The setup file some ports ship for other launchers (a JSON file with `"format": 1`): in the app's folder, next to
//! its program, or else as the port's repository holds it (the catalog's `/apps/{slug}/setup-file`). The catalog's
//! own setup (setupFiles, start programs, saves) comes first; from this file Quiver takes only what the catalog can't
//! say: the arguments and environment the port's program starts with. The player's copies are left to the catalog's
//! setupFiles (own_copy.rs), so an argument that carries one is never passed from here.
use serde::Deserialize;
use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

/// The file's name, at the top of the app's folder.
pub const FILE: &str = "recomplauncher.json";
/// Where the file's `{data}` is: everything the port writes, inside its folder.
pub const DATA: &str = "recompdata";
/// Bigger than any real one.
const LIMIT: u64 = 1 << 20;

#[derive(Deserialize, Default, Debug)]
pub struct Manifest {
    #[serde(default)]
    format: Option<u32>,
    #[serde(default)]
    start: Start,
}

#[derive(Deserialize, Default, Debug)]
struct Start {
    #[serde(default, deserialize_with = "one_or_many")]
    program: Vec<String>,
    #[serde(default)]
    args: Vec<String>,
    #[serde(default)]
    env: BTreeMap<String, String>,
}

fn one_or_many<'de, D: serde::Deserializer<'de>>(d: D) -> Result<Vec<String>, D::Error> {
    #[derive(Deserialize)]
    #[serde(untagged)]
    enum Either {
        One(String),
        Many(Vec<String>),
    }
    Ok(match Either::deserialize(d)? {
        Either::One(s) => vec![s],
        Either::Many(v) => v,
    })
}

fn parse(text: &str) -> Option<Manifest> {
    serde_json::from_str::<Manifest>(text.trim_start_matches('\u{feff}')).ok().filter(|m| m.format == Some(1))
}

/// The app's own file when it ships one (it belongs to the version installed), else the catalog's.
pub fn read(app_dir: &Path, catalog: Option<&str>) -> Option<Manifest> {
    let path = app_dir.join(FILE);
    let own = std::fs::metadata(&path).ok().filter(|m| m.is_file() && m.len() <= LIMIT).and_then(|_| std::fs::read_to_string(&path).ok()).and_then(|t| parse(&t));
    own.or_else(|| catalog.and_then(parse))
}

/// What the port's program starts with: arguments, after the catalog's copies and before the player's own options,
/// and its environment.
#[derive(Debug, Default, PartialEq)]
pub struct Words {
    pub args: Vec<String>,
    pub env: Vec<(String, String)>,
}

/// Whether `exe` is one of the programs the file starts: by its path in the app's folder, or its name. An AppImage
/// is renamed by downloads and updates, so any AppImage stands for one the file names.
fn starts(m: &Manifest, app_dir: &Path, exe: &Path) -> bool {
    let relative = exe.strip_prefix(app_dir).ok().map(|p| p.to_string_lossy().replace('\\', "/").to_lowercase());
    let name = exe.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    let appimage = name.ends_with(".appimage");
    m.start.program.iter().any(|p| {
        let p = p.trim_start_matches("./").replace('\\', "/").to_lowercase();
        let p_name = p.rsplit('/').next().unwrap_or(&p).to_string();
        relative.as_deref() == Some(p.as_str()) || p_name == name || (appimage && p.ends_with(".appimage"))
    })
}

/// A path for the game: through Wine or Proton, as Windows names a Linux path.
fn for_game(path: &Path, windows: bool) -> String {
    let text = path.to_string_lossy().into_owned();
    if windows && text.starts_with('/') {
        format!("Z:{}", text.replace('/', "\\"))
    } else {
        text
    }
}

/// The system's folders the file's places name, on this computer. None through Wine or Proton, whose Windows
/// folders are inside the prefix: an argument naming one is left out there.
fn place(name: &str, windows: bool) -> Option<PathBuf> {
    if windows {
        return None;
    }
    match name {
        "documents" => dirs::document_dir(),
        "home" => dirs::home_dir(),
        "local" => dirs::data_local_dir(),
        "config" if cfg!(windows) || cfg!(target_os = "macos") => dirs::data_dir(),
        "config" => dirs::config_dir(),
        _ => None,
    }
}

/// `text` with its places filled in; None when it names one Quiver can't give (a player's copy, or a place unknown).
fn fill(text: &str, app_dir: &Path, windows: bool) -> Option<String> {
    let mut out = String::new();
    let mut rest = text;
    while let Some(open) = rest.find('{') {
        out.push_str(&rest[..open]);
        let close = rest[open..].find('}')? + open;
        let name = &rest[open + 1..close];
        let value = match name {
            "install" => for_game(app_dir, windows),
            "data" => for_game(&app_dir.join(DATA), windows),
            "pad" => "xbox".into(),
            _ => for_game(&place(name, windows)?, windows),
        };
        out.push_str(&value);
        rest = &rest[close + 1..];
    }
    out.push_str(rest);
    Some(out)
}

/// An argument that carries a player's copy: `{input:game}`, the older `{game}`, or one joined to its flag.
fn carries_copy(arg: &str) -> bool {
    arg.contains("{input:") || arg.contains("{game}")
}

/// The words for `exe`, when it's one of the programs the file starts (a settings tool the player picked isn't).
/// `windows`: it runs through Wine or Proton. Makes the `{data}` folder, which the port may expect to be there.
pub fn start_words(m: &Manifest, app_dir: &Path, exe: &Path, windows: bool) -> Option<Words> {
    if !starts(m, app_dir, exe) {
        return None;
    }
    let mut args = Vec::new();
    let given = &m.start.args;
    for (i, arg) in given.iter().enumerate() {
        // "--iso", "{input:game}": the flag goes with the copy the catalog passes.
        let next_is_copy = given.get(i + 1).is_some_and(|n| n.starts_with('{') && carries_copy(n) && n.ends_with('}'));
        if carries_copy(arg) || (arg.starts_with('-') && next_is_copy) {
            continue;
        }
        if let Some(filled) = fill(arg, app_dir, windows) {
            args.push(filled);
        }
    }
    let mut env: Vec<(String, String)> = m
        .start
        .env
        .iter()
        .filter(|(name, value)| crate::launch::env_ok(name) && !carries_copy(value))
        .filter_map(|(name, value)| fill(value, app_dir, windows).map(|v| (name.clone(), v)))
        .collect();
    // Ports that ship the file look for these, to leave out their own launcher window and updater.
    let data = app_dir.join(DATA);
    let _ = std::fs::create_dir_all(&data);
    env.push(("RECOMPLAUNCHER".into(), "1".into()));
    env.push(("RECOMPLAUNCHER_DATA".into(), for_game(&data, windows)));
    env.push(("RECOMPLAUNCHER_MODE".into(), "desktop".into()));
    Some(Words { args, env })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn manifest(json: &str) -> Manifest {
        parse(json).expect("a format 1 file")
    }

    #[test]
    fn reads_format_1_only_and_prefers_the_apps_own_file() {
        assert!(parse(r#"{"format":2}"#).is_none());
        assert!(parse("{").is_none());
        assert!(parse("\u{feff}{\"format\":1}").is_some());
        let dir = tempfile::tempdir().unwrap();
        let catalog = r#"{"format":1,"start":{"program":"catalog.exe"}}"#;
        assert_eq!(read(dir.path(), Some(catalog)).unwrap().start.program, vec!["catalog.exe"]);
        std::fs::write(dir.path().join(FILE), r#"{"format":1,"start":{"program":["own.exe"]}}"#).unwrap();
        assert_eq!(read(dir.path(), Some(catalog)).unwrap().start.program, vec!["own.exe"]);
        assert!(read(&dir.path().join("none"), None).is_none());
    }

    #[test]
    fn passes_arguments_and_environment_but_never_a_copy() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        let m = manifest(
            r#"{"format":1,"start":{"program":["bin/Game.exe","game"],
                "args":["--no-update-check","--iso","{input:game}","--dlc1={input:dlc1}","--data","{data}","{game}","--odd={nope}"],
                "env":{"GAME_HOME":"{install}/home","GAME_ISO":"{input:game}","PATH":"x"}}}"#,
        );
        let words = start_words(&m, app, &app.join("bin/Game.exe"), false).unwrap();
        let data = app.join(DATA).to_string_lossy().into_owned();
        assert_eq!(words.args, vec!["--no-update-check".to_string(), "--data".into(), data.clone()]);
        assert_eq!(
            words.env,
            vec![
                ("GAME_HOME".to_string(), format!("{}/home", app.to_string_lossy())),
                ("RECOMPLAUNCHER".into(), "1".into()),
                ("RECOMPLAUNCHER_DATA".into(), data),
                ("RECOMPLAUNCHER_MODE".into(), "desktop".into()),
            ]
        );
        assert!(app.join(DATA).is_dir());
        // By name anywhere in the folder, and not for another program.
        assert!(start_words(&m, app, &app.join("game"), false).is_some());
        assert!(start_words(&m, app, &app.join("Settings.exe"), false).is_none());
    }

    #[test]
    fn any_appimage_stands_for_the_one_named_and_wine_gets_windows_paths() {
        let m = manifest(r#"{"format":1,"start":{"program":["Game-x86_64.AppImage"],"args":["--data={data}","--docs={documents}"]}}"#);
        let app = Path::new("/games/Game");
        let words = start_words(&m, app, &app.join("Game-1.2-x86_64.AppImage"), false);
        assert!(words.is_some());
        let m = manifest(r#"{"format":1,"start":{"program":["Game.exe"],"args":["--data={data}","--docs={documents}"]}}"#);
        let dir = tempfile::tempdir().unwrap();
        let words = start_words(&m, dir.path(), &dir.path().join("Game.exe"), true).unwrap();
        let expected = format!("--data=Z:{}", dir.path().join(DATA).to_string_lossy().replace('/', "\\"));
        if cfg!(unix) {
            assert_eq!(words.args, vec![expected]);
        }
    }
}
