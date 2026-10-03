//! Add to Steam: a non-Steam game in Steam's shortcuts.vdf (binary VDF),
//! with the app's catalog artwork in Steam's grid folder. On Linux a Windows
//! game is set to run with Proton.
//!
//! Steam rewrites shortcuts.vdf from memory when it exits, so while Steam is
//! running the write waits in a separate copy of the launcher
//! (`--add-to-steam <file>`) until Steam closes.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Duration;

const TAG: &str = "QuiverLauncher";

#[derive(Debug, Clone, PartialEq)]
pub enum Vdf {
    Map(Vec<(String, Vdf)>),
    Str(String),
    Int(u32),
    U64(u64),
}

fn cstr(b: &[u8], i: &mut usize) -> Option<String> {
    let end = b[*i..].iter().position(|&c| c == 0)? + *i;
    let s = String::from_utf8_lossy(&b[*i..end]).into_owned();
    *i = end + 1;
    Some(s)
}

fn parse_map(b: &[u8], i: &mut usize) -> Option<Vec<(String, Vdf)>> {
    let mut items = Vec::new();
    loop {
        let kind = *b.get(*i)?;
        *i += 1;
        if kind == 8 {
            return Some(items);
        }
        let key = cstr(b, i)?;
        let value = match kind {
            0 => Vdf::Map(parse_map(b, i)?),
            1 => Vdf::Str(cstr(b, i)?),
            2 => {
                let v = u32::from_le_bytes(b.get(*i..*i + 4)?.try_into().ok()?);
                *i += 4;
                Vdf::Int(v)
            }
            7 => {
                let v = u64::from_le_bytes(b.get(*i..*i + 8)?.try_into().ok()?);
                *i += 8;
                Vdf::U64(v)
            }
            _ => return None,
        };
        items.push((key, value));
    }
}

pub fn parse(b: &[u8]) -> Option<Vec<(String, Vdf)>> {
    let mut i = 0;
    parse_map(b, &mut i)
}

fn write_map(items: &[(String, Vdf)], out: &mut Vec<u8>) {
    for (key, value) in items {
        let kind = match value {
            Vdf::Map(_) => 0,
            Vdf::Str(_) => 1,
            Vdf::Int(_) => 2,
            Vdf::U64(_) => 7,
        };
        out.push(kind);
        out.extend(key.as_bytes());
        out.push(0);
        match value {
            Vdf::Map(m) => write_map(m, out),
            Vdf::Str(s) => {
                out.extend(s.as_bytes());
                out.push(0);
            }
            Vdf::Int(v) => out.extend(v.to_le_bytes()),
            Vdf::U64(v) => out.extend(v.to_le_bytes()),
        }
    }
    out.push(8);
}

pub fn write(items: &[(String, Vdf)]) -> Vec<u8> {
    let mut out = Vec::new();
    write_map(items, &mut out);
    out
}

/// Steam's id for a non-Steam game: CRC-32 of its quoted program and name, top bit set.
pub fn app_id(exe: &str, name: &str) -> u32 {
    let crc = crc32fast::hash(format!("{exe}{name}").as_bytes()) | 0x8000_0000;
    if crc == 0x8000_0000 { 0x8000_0001 } else { crc }
}

/// What gets written, worked out up front so a waiting copy needs nothing else.
#[derive(Serialize, Deserialize)]
pub struct Shortcut {
    pub name: String,
    /// Quoted, as Steam stores it.
    pub exe: String,
    pub start_dir: String,
    pub launch_options: String,
    pub icon: String,
    /// Run with Proton (a Windows game on Linux).
    pub proton: bool,
    /// Downloaded artwork: grid file suffix ("", "p", "_hero", "_logo") to a PNG.
    pub art: Vec<(String, PathBuf)>,
}

fn quoted(path: &Path) -> String {
    format!("\"{}\"", path.to_string_lossy())
}

impl Shortcut {
    pub fn new(name: &str, target: &crate::shortcut::Target, icon: Option<&Path>) -> Shortcut {
        let proton = cfg!(target_os = "linux") && target.exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe"));
        let program = if proton { &target.exe } else { &target.program };
        // The game starts as itself; anything else goes in launch options.
        let mut options: Vec<String> = Vec::new();
        if target.extract_appimage {
            options.push("APPIMAGE_EXTRACT_AND_RUN=1 %command%".into());
        }
        if !proton {
            options.extend(target.args.iter().map(|a| format!("\"{a}\"")));
        }
        Shortcut {
            name: name.into(),
            exe: quoted(program),
            start_dir: quoted(&target.dir),
            launch_options: options.join(" "),
            icon: icon.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default(),
            proton,
            art: Vec::new(),
        }
    }
}

/// Adds or updates the shortcut in a shortcuts.vdf's contents; returns its app id.
pub fn upsert(file: Option<&[u8]>, s: &Shortcut) -> Result<(Vec<u8>, u32), String> {
    let mut root = match file {
        Some(bytes) => parse(bytes).ok_or("Steam's shortcuts file couldn't be read.")?,
        None => vec![("shortcuts".into(), Vdf::Map(Vec::new()))],
    };
    let Some((_, Vdf::Map(list))) = root.iter_mut().find(|(k, _)| k.eq_ignore_ascii_case("shortcuts")) else {
        return Err("Steam's shortcuts file couldn't be read.".into());
    };
    let get = |m: &[(String, Vdf)], key: &str| m.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)).map(|(_, v)| v.clone());
    let ours = |m: &[(String, Vdf)]| {
        let tagged = matches!(get(m, "tags"), Some(Vdf::Map(t)) if t.iter().any(|(_, v)| *v == Vdf::Str(TAG.into())));
        get(m, "appname") == Some(Vdf::Str(s.name.clone())) && (get(m, "exe") == Some(Vdf::Str(s.exe.clone())) || tagged)
    };
    let existing = list.iter_mut().find_map(|(_, v)| match v {
        Vdf::Map(m) if ours(m) => Some(m),
        _ => None,
    });
    let id = match existing {
        Some(m) => {
            // Keep its id, so Steam keeps its artwork, settings and play time.
            let id = match get(m, "appid") {
                Some(Vdf::Int(id)) => id,
                _ => app_id(&s.exe, &s.name),
            };
            set(m, "appid", Vdf::Int(id));
            set(m, "exe", Vdf::Str(s.exe.clone()));
            set(m, "StartDir", Vdf::Str(s.start_dir.clone()));
            set(m, "LaunchOptions", Vdf::Str(s.launch_options.clone()));
            if !s.icon.is_empty() {
                set(m, "icon", Vdf::Str(s.icon.clone()));
            }
            id
        }
        None => {
            let id = app_id(&s.exe, &s.name);
            let str = |v: &str| Vdf::Str(v.into());
            list.push((
                String::new(),
                Vdf::Map(vec![
                    ("appid".into(), Vdf::Int(id)),
                    ("appname".into(), str(&s.name)),
                    ("exe".into(), str(&s.exe)),
                    ("StartDir".into(), str(&s.start_dir)),
                    ("icon".into(), str(&s.icon)),
                    ("ShortcutPath".into(), str("")),
                    ("LaunchOptions".into(), str(&s.launch_options)),
                    ("IsHidden".into(), Vdf::Int(0)),
                    ("AllowDesktopConfig".into(), Vdf::Int(1)),
                    ("AllowOverlay".into(), Vdf::Int(1)),
                    ("OpenVR".into(), Vdf::Int(0)),
                    ("Devkit".into(), Vdf::Int(0)),
                    ("DevkitGameID".into(), str("")),
                    ("DevkitOverrideAppID".into(), Vdf::Int(0)),
                    ("LastPlayTime".into(), Vdf::Int(0)),
                    ("FlatpakAppID".into(), str("")),
                    ("sortas".into(), str("")),
                    ("tags".into(), Vdf::Map(vec![("0".into(), str(TAG))])),
                ]),
            ));
            id
        }
    };
    // Steam numbers its shortcuts 0, 1, 2…
    for (n, (key, _)) in list.iter_mut().enumerate() {
        *key = n.to_string();
    }
    Ok((write(&root), id))
}

fn set(m: &mut Vec<(String, Vdf)>, key: &str, value: Vdf) {
    match m.iter_mut().find(|(k, _)| k.eq_ignore_ascii_case(key)) {
        Some((_, v)) => *v = value,
        None => m.push((key.into(), value)),
    }
}

/// Steam's install folders on this computer.
fn steam_roots() -> Vec<PathBuf> {
    let mut roots = Vec::new();
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        if let Ok(out) = std::process::Command::new("reg")
            .args(["query", r"HKCU\Software\Valve\Steam", "/v", "SteamPath"])
            .creation_flags(0x0800_0000)
            .output()
        {
            let text = String::from_utf8_lossy(&out.stdout);
            if let Some(path) = text.lines().find_map(|l| l.split("REG_SZ").nth(1)) {
                roots.push(PathBuf::from(path.trim()));
            }
        }
        for var in ["ProgramFiles(x86)", "ProgramFiles"] {
            if let Some(dir) = std::env::var_os(var) {
                roots.push(PathBuf::from(dir).join("Steam"));
            }
        }
    }
    #[cfg(not(windows))]
    if let Some(home) = dirs::home_dir() {
        roots.push(home.join(".steam/steam"));
        roots.push(home.join(".local/share/Steam"));
        roots.push(home.join(".var/app/com.valvesoftware.Steam/.local/share/Steam"));
    }
    let mut seen = Vec::new();
    roots.into_iter().filter_map(|r| r.canonicalize().ok()).filter(|r| r.join("userdata").is_dir() && !seen.contains(r) && {
        seen.push(r.clone());
        true
    }).collect()
}

/// The Steam account last used here: its userdata folder and Steam's own folder.
pub fn account() -> Result<(PathBuf, PathBuf), String> {
    let mut best: Option<(std::time::SystemTime, PathBuf, PathBuf)> = None;
    for root in steam_roots() {
        let Ok(users) = std::fs::read_dir(root.join("userdata")) else { continue };
        for user in users.flatten() {
            let name = user.file_name();
            if name == "0" || !name.to_string_lossy().chars().all(|c| c.is_ascii_digit()) {
                continue;
            }
            let config = user.path().join("config");
            let seen = std::fs::metadata(config.join("shortcuts.vdf"))
                .or_else(|_| std::fs::metadata(&config))
                .and_then(|m| m.modified());
            if let Ok(at) = seen {
                if best.as_ref().is_none_or(|(b, _, _)| at > *b) {
                    best = Some((at, user.path(), root.clone()));
                }
            }
        }
    }
    best.map(|(_, user, root)| (user, root))
        .ok_or_else(|| "Couldn't find Steam on this computer. Open Steam once and sign in, then try again.".into())
}

pub fn running() -> bool {
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        std::process::Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq steam.exe", "/NH"])
            .creation_flags(0x0800_0000)
            .output()
            .is_ok_and(|o| String::from_utf8_lossy(&o.stdout).to_lowercase().contains("steam.exe"))
    }
    #[cfg(not(windows))]
    {
        std::fs::read_dir("/proc").is_ok_and(|procs| {
            procs.flatten().any(|p| std::fs::read_to_string(p.path().join("comm")).is_ok_and(|c| c.trim() == "steam"))
        })
    }
}

/// Writes the shortcut, its artwork and Proton choice. Steam must be closed.
pub fn apply(s: &Shortcut) -> Result<(), String> {
    let (user, root) = account()?;
    let config = user.join("config");
    let file = config.join("shortcuts.vdf");
    let current = std::fs::read(&file).ok();
    let (bytes, id) = upsert(current.as_deref(), s)?;
    let temp = file.with_extension("vdf.quiver.tmp");
    std::fs::write(&temp, bytes).and_then(|_| std::fs::rename(&temp, &file)).map_err(|e| e.to_string())?;
    let grid = config.join("grid");
    if !s.art.is_empty() && std::fs::create_dir_all(&grid).is_ok() {
        for (suffix, path) in &s.art {
            let _ = std::fs::copy(path, grid.join(format!("{id}{suffix}.png")));
        }
    }
    if s.proton {
        let _ = use_proton(&root.join("config/config.vdf"), id);
    }
    Ok(())
}

/// Sets Proton for a shortcut in Steam's text config, unless it already has a choice.
fn use_proton(config: &Path, id: u32) -> Result<(), String> {
    let text = std::fs::read_to_string(config).map_err(|e| e.to_string())?;
    let Some(start) = text.find("\"CompatToolMapping\"") else { return Ok(()) };
    let Some(brace) = text[start..].find('{').map(|b| start + b + 1) else { return Ok(()) };
    if text[brace..].contains(&format!("\"{id}\"")) {
        return Ok(());
    }
    let entry = format!(
        "\n\t\t\t\t\t\"{id}\"\n\t\t\t\t\t{{\n\t\t\t\t\t\t\"name\"\t\t\"proton_experimental\"\n\t\t\t\t\t\t\"config\"\t\t\"\"\n\t\t\t\t\t\t\"priority\"\t\t\"250\"\n\t\t\t\t\t}}"
    );
    let updated = format!("{}{entry}{}", &text[..brace], &text[brace..]);
    let temp = config.with_extension("vdf.quiver.tmp");
    std::fs::write(&temp, updated).and_then(|_| std::fs::rename(&temp, config)).map_err(|e| e.to_string())
}

/// `--add-to-steam <file>`: waits for Steam to close, then writes the shortcut.
pub fn wait_and_apply(file: &Path) {
    let Ok(s) = std::fs::read(file).map_err(|_| ()).and_then(|b| serde_json::from_slice::<Shortcut>(&b).map_err(|_| ())) else {
        return;
    };
    // A day at most; the player may leave Steam open for a while.
    for _ in 0..86_400 {
        if !running() {
            std::thread::sleep(Duration::from_secs(1));
            let _ = apply(&s);
            break;
        }
        std::thread::sleep(Duration::from_secs(1));
    }
    let _ = std::fs::remove_file(file);
}

#[cfg(test)]
mod tests {
    use super::*;

    fn shortcut(name: &str) -> Shortcut {
        Shortcut {
            name: name.into(),
            exe: "\"/games/a/run\"".into(),
            start_dir: "\"/games/a\"".into(),
            launch_options: String::new(),
            icon: String::new(),
            proton: false,
            art: vec![],
        }
    }

    #[test]
    fn adds_then_updates_a_shortcut_in_place() {
        let other = write(&[("shortcuts".into(), Vdf::Map(vec![("0".into(), Vdf::Map(vec![("appname".into(), Vdf::Str("Other".into())), ("LastPlayTime".into(), Vdf::Int(5))]))]))]);
        let (once, id) = upsert(Some(&other), &shortcut("Game")).unwrap();
        assert_eq!(id, app_id("\"/games/a/run\"", "Game"));
        assert!(id & 0x8000_0000 != 0);
        let mut moved = shortcut("Game");
        moved.exe = "\"/elsewhere/run\"".into();
        let (twice, same) = upsert(Some(&once), &moved).unwrap();
        assert_eq!(same, id, "found by its tag, it keeps its id");
        let root = parse(&twice).unwrap();
        let Vdf::Map(list) = &root[0].1 else { panic!() };
        assert_eq!(list.len(), 2);
        assert_eq!(list[1].0, "1");
        assert!(matches!(&list[1].1, Vdf::Map(m) if m.contains(&("exe".into(), Vdf::Str("\"/elsewhere/run\"".into())))));
        assert_eq!(parse(&write(&root)).unwrap(), root, "round trip");
    }
}
