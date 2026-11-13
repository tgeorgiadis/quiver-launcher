//! Add to Steam: a non-Steam game in Steam's shortcuts.vdf (binary VDF),
//! with the app's catalog artwork in Steam's grid folder. On Linux a Windows
//! game is set to run with Proton.
//!
//! Steam rewrites shortcuts.vdf from memory when it exits, so a write while
//! Steam is running is lost. On Linux the running client can add a shortcut
//! itself, at once: `steam://addnonsteamgame/<path>` (add_now), which is what
//! a Steam Deck's own Add to Steam uses. The path is a small `.desktop` naming
//! the app; a shortcut made that way survives Steam's exit, because Steam is
//! the one that wrote it. The desktop runs a script that starts the app
//! (Launcher::write), except for a Windows game Steam itself runs with Proton:
//! there the desktop names the game (Launcher::write_proton), so Steam stores
//! the game as the program and applies a compat tool to it. Where the live add
//! isn't available (Windows, or a Steam without the handler), the write still
//! waits in a separate copy of the launcher (`--add-to-steam <file>`) until
//! Steam closes.
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
    /// Steam's `ShortcutPath`: the launcher file a Linux shortcut came from.
    #[serde(default)]
    pub shortcut_path: String,
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
        let proton = cfg!(target_os = "linux") && target.steam_proton;
        let program = if proton { &target.exe } else { &target.program };
        // The game starts as itself; anything else goes in launch options,
        // written as Steam reads them.
        let word = |w: &str| if !w.is_empty() && w.chars().all(|c| c.is_ascii_alphanumeric() || "_=./:,+-%".contains(c)) { w.to_string() } else { format!("\"{}\"", w.replace('\\', "\\\\").replace('"', "\\\"")) };
        let mut env: Vec<(String, String)> = Vec::new();
        if target.extract_appimage {
            env.push(("APPIMAGE_EXTRACT_AND_RUN".into(), "1".into()));
        }
        if !proton {
            env.extend(target.env.iter().cloned());
        }
        env.extend(target.options.env.iter().cloned());
        let mut before: Vec<String> = env.iter().map(|(k, v)| format!("{k}={}", word(v))).collect();
        before.extend(target.options.before.iter().map(|w| word(w)));
        let mut after: Vec<String> = if proton { vec![] } else { target.args.iter().map(|a| format!("\"{a}\"")).collect() };
        after.extend(target.options.after.iter().map(|w| word(w)));
        let mut options = before;
        if !options.is_empty() {
            options.push("%command%".into());
        }
        options.extend(after);
        Shortcut {
            name: name.into(),
            exe: quoted(program),
            start_dir: quoted(&target.dir),
            launch_options: options.join(" "),
            shortcut_path: String::new(),
            icon: icon.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default(),
            proton,
            art: Vec::new(),
        }
    }
}

/// A Linux shortcut's wrapper: a `.desktop` naming the app, running a script
/// that starts it. Steam reads the desktop's `Name` (the exact title) and runs
/// its `Exec`; the script carries the folder, environment and whole command.
/// A game Steam runs with its own Proton instead names the game in the desktop,
/// so Steam stores the game as the program and sets a compat tool for it.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
pub struct Launcher {
    desktop: PathBuf,
    /// What Steam stores as the shortcut's program.
    exe: String,
    /// The script a wrapper runs; none when Steam runs the game itself.
    script: Option<PathBuf>,
}

#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
impl Launcher {
    /// Writes the script and its `.desktop` under `data/steam`, returning them.
    pub fn write(data: &Path, folder: &str, name: &str, target: &crate::shortcut::Target, icon: Option<&Path>) -> Result<Launcher, String> {
        let dir = data.join("steam");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        let script = dir.join(format!("{folder}.sh"));
        std::fs::write(&script, script_text(target, name)).map_err(|e| e.to_string())?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&script, std::fs::Permissions::from_mode(0o755)).map_err(|e| e.to_string())?;
        }
        // The desktop runs only the script: desktop_entry's quoting and its Name,
        // Path and Icon fields are exactly what Steam reads.
        let runs = crate::shortcut::Target {
            exe: script.clone(),
            program: script.clone(),
            args: Vec::new(),
            env: Vec::new(),
            dir: target.dir.clone(),
            extract_appimage: false,
            steam_proton: false,
            options: Default::default(),
        };
        let desktop = dir.join(format!("{folder}.desktop"));
        std::fs::write(&desktop, crate::shortcut::desktop_entry(&runs, name, icon)).map_err(|e| e.to_string())?;
        Ok(Launcher { desktop, exe: quoted(&script), script: Some(script) })
    }

    /// Writes a `.desktop` naming the game itself, under `data/steam`: Steam
    /// stores the game as the program and runs it with its own Proton. The
    /// player's launch options follow the game in the `Exec`, as Steam reads
    /// them for its LaunchOptions.
    pub fn write_proton(data: &Path, folder: &str, name: &str, target: &crate::shortcut::Target, icon: Option<&Path>) -> Result<Launcher, String> {
        let dir = data.join("steam");
        std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
        // Steam only keeps a shortcut's program when it can be executed, so a
        // game whose archive never marked it runnable is made so here, whether
        // it was installed before or after Quiver started doing it (install.rs).
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            if let Ok(meta) = std::fs::metadata(&target.exe) {
                let mode = meta.permissions().mode();
                if mode & 0o111 == 0 {
                    let _ = std::fs::set_permissions(&target.exe, std::fs::Permissions::from_mode(mode | 0o755));
                }
            }
        }
        let shortcut = Shortcut::new(name, target, icon);
        let desktop = dir.join(format!("{folder}.desktop"));
        std::fs::write(&desktop, proton_desktop(&shortcut, &target.dir, icon)).map_err(|e| e.to_string())?;
        Ok(Launcher { desktop, exe: shortcut.exe, script: None })
    }

    pub fn desktop(&self) -> &Path {
        &self.desktop
    }

    /// What Steam stores as the shortcut's program: the quoted script or game.
    fn exe(&self) -> String {
        self.exe.clone()
    }

    /// The app id of a shortcut that runs this script: what its artwork is named for.
    pub fn app_id(&self, name: &str) -> u32 {
        app_id(&self.exe(), name)
    }

    /// Every app id this shortcut might get. Steam quotes the program it stores,
    /// so the quoted form is the likely one; the plain and resolved paths cost
    /// nothing and cover the rest, so the artwork lands whichever way it wrote it.
    pub fn app_ids(&self, name: &str) -> Vec<u32> {
        let mut ids = vec![self.app_id(name)];
        let mut programs = vec![self.exe.clone()];
        // The same program without the quotes Steam may or may not have kept.
        if let Some(bare) = self.exe.strip_prefix('"').and_then(|p| p.strip_suffix('"')) {
            programs.push(bare.to_string());
            if let Ok(real) = std::fs::canonicalize(bare) {
                programs.push(real.to_string_lossy().into_owned());
            }
        }
        if let Some(script) = &self.script {
            if let Ok(real) = std::fs::canonicalize(script) {
                programs.push(quoted(&real));
            }
        }
        for program in programs {
            let id = app_id(&program, name);
            if !ids.contains(&id) {
                ids.push(id);
            }
        }
        ids
    }

    /// The shortcut to write directly, when Steam is closed.
    pub fn shortcut(&self, name: &str, target: &crate::shortcut::Target, icon: Option<&Path>, art: Vec<(String, PathBuf)>) -> Shortcut {
        Shortcut {
            name: name.into(),
            exe: self.exe(),
            start_dir: quoted(&target.dir),
            launch_options: String::new(),
            shortcut_path: self.desktop.to_string_lossy().into_owned(),
            icon: icon.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default(),
            proton: false,
            art,
        }
    }
}

/// A `.desktop` for Steam to run with its own Proton: the `Exec` is the game,
/// so Steam stores it as the program and applies a compat tool; the player's
/// launch options follow, as Steam reads them for its LaunchOptions.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn proton_desktop(s: &Shortcut, dir: &Path, icon: Option<&Path>) -> String {
    let value = |x: &str| x.replace('\\', "\\\\").replace('\n', "\\n").replace('\r', "\\r").replace('\t', "\\t");
    let exec = if s.launch_options.is_empty() { s.exe.clone() } else { format!("{} {}", s.exe, s.launch_options) };
    format!(
        "[Desktop Entry]\nType=Application\nName={}\nExec={}\nPath={}\nIcon={}\nTerminal=false\nCategories=Game;\nComment=Play {}\n",
        value(&s.name),
        value(&exec),
        value(&dir.to_string_lossy()),
        value(&icon.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default()),
        value(&s.name),
    )
}

/// The shell script a Linux shortcut runs: its folder, environment and command.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
pub fn script_text(target: &crate::shortcut::Target, name: &str) -> String {
    let (program, args, env) = target.command();
    let mut out = format!("#!/bin/sh\n# Quiver Launcher: {}\n", name.replace(['\n', '\r'], " "));
    out.push_str(&format!("cd {} || exit 1\n", shell_word(&target.dir.to_string_lossy())));
    out.push_str("exec");
    if !env.is_empty() {
        out.push_str(" env");
        for (key, value) in &env {
            out.push(' ');
            out.push_str(&shell_word(&format!("{key}={value}")));
        }
    }
    out.push(' ');
    out.push_str(&shell_word(&program.to_string_lossy()));
    for arg in &args {
        out.push(' ');
        out.push_str(&shell_word(arg));
    }
    out.push('\n');
    out
}

/// One word of an `sh` command: bare when it's plainly safe, quoted otherwise.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn shell_word(word: &str) -> String {
    if !word.is_empty() && word.chars().all(|c| c.is_ascii_alphanumeric() || "_=./:,+-%@".contains(c)) {
        word.to_string()
    } else {
        format!("'{}'", word.replace('\'', "'\\''"))
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
            if !s.shortcut_path.is_empty() {
                set(m, "ShortcutPath", Vdf::Str(s.shortcut_path.clone()));
            }
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
                    ("ShortcutPath".into(), str(&s.shortcut_path)),
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

/// Steam started the launcher (as a game of its own, or a non-Steam game).
pub fn started_us(var: impl Fn(&str) -> Option<String>) -> bool {
    ["SteamGameId", "SteamAppId", "SteamOverlayGameId"].into_iter().any(|name| var(name).is_some_and(|v| !v.trim().is_empty()))
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
        // A Flatpak's /proc has only its own sandbox's processes: the host's are looked through there.
        if crate::sandbox::id().is_some() {
            let mut look = std::process::Command::new("sh");
            look.args(["-c", "for f in /proc/[0-9]*/comm; do read -r c < \"$f\" 2> /dev/null && [ \"$c\" = steam ] && exit 0; done; exit 1"]);
            return crate::sandbox::host(look).output().is_ok_and(|o| o.status.success());
        }
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
    let _ = place_art(&user, id, &s.art);
    if s.proton {
        let _ = use_proton(&root.join("config/config.vdf"), id);
    }
    Ok(())
}

/// Copies downloaded artwork into Steam's grid folder, named for the app id.
pub fn place_art(user: &Path, id: u32, art: &[(String, PathBuf)]) -> Result<(), String> {
    if art.is_empty() {
        return Ok(());
    }
    let grid = user.join("config/grid");
    std::fs::create_dir_all(&grid).map_err(|e| e.to_string())?;
    for (suffix, path) in art {
        std::fs::copy(path, grid.join(format!("{id}{suffix}.png"))).map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// The app id Steam already gave this launcher's shortcut, if it's there: a
/// live add shouldn't be asked for twice.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
pub fn existing_id(launcher: &Launcher) -> Option<u32> {
    let (user, _) = account().ok()?;
    let bytes = std::fs::read(user.join("config/shortcuts.vdf")).ok()?;
    let root = parse(&bytes)?;
    let (_, Vdf::Map(list)) = root.iter().find(|(k, _)| k.eq_ignore_ascii_case("shortcuts"))? else { return None };
    let path = launcher.desktop.to_string_lossy().into_owned();
    let ours = |m: &[(String, Vdf)]| {
        m.iter().any(|(k, v)| k.eq_ignore_ascii_case("ShortcutPath") && matches!(v, Vdf::Str(p) if *p == path))
    };
    list.iter().find_map(|(_, v)| match v {
        Vdf::Map(m) if ours(m) => m.iter().find_map(|(k, v)| match (k.to_ascii_lowercase().as_str(), v) {
            ("appid", Vdf::Int(id)) => Some(*id),
            _ => None,
        }),
        _ => None,
    })
}

/// Adds a shortcut to the running Steam at once, through its URL handler. The
/// path is the `.desktop`: Steam reads its Name and Exec itself, and its own
/// write is what lasts. Steam's handler needs its sentinel file first.
#[cfg(target_os = "linux")]
pub fn add_now(desktop: &Path) -> Result<(), String> {
    let mut touch = std::process::Command::new("touch");
    touch.arg("/tmp/addnonsteamgamefile");
    let touched = crate::sandbox::host(touch).status().map_err(|e| e.to_string())?;
    if !touched.success() {
        return Err("Steam's add file couldn't be made.".into());
    }
    let url = format!("steam://addnonsteamgame/{}", percent_encode(&desktop.to_string_lossy()));
    let command = match crate::sandbox::which("steam") {
        Some(steam) => {
            let mut command = std::process::Command::new(steam);
            command.arg(url);
            command
        }
        None => {
            let mut command = std::process::Command::new("flatpak");
            command.args(["run", "com.valvesoftware.Steam"]).arg(url);
            command
        }
    };
    crate::sandbox::host(command).status().map_err(|e| e.to_string())?.success().then_some(()).ok_or_else(|| "Steam didn't take the shortcut.".into())
}

/// Percent-encodes a path for a `steam://` URL: everything but `A-Za-z0-9-._~`.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn percent_encode(path: &str) -> String {
    let mut out = String::with_capacity(path.len());
    for byte in path.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~') {
            out.push(byte as char);
        } else {
            out.push_str(&format!("%{byte:02X}"));
        }
    }
    out
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

    #[test]
    fn launch_options_go_in_steams_launch_options() {
        let target = |steam_proton, options: &str| crate::shortcut::Target {
            exe: "/games/G/game.exe".into(),
            program: "/protons/GE/proton".into(),
            args: vec!["run".into(), "/games/G/game.exe".into()],
            env: vec![("STEAM_COMPAT_DATA_PATH".into(), "/data/proton/G".into())],
            dir: "/games/G".into(),
            extract_appimage: false,
            steam_proton,
            options: crate::launch::parse(options).unwrap(),
        };
        let s = Shortcut::new("G", &target(false, "DXVK_HUD=\"fps,gpu load\" gamemoderun %command% -w"), None);
        assert_eq!(s.exe, "\"/protons/GE/proton\"");
        assert_eq!(s.launch_options, "STEAM_COMPAT_DATA_PATH=/data/proton/G DXVK_HUD=\"fps,gpu load\" gamemoderun %command% \"run\" \"/games/G/game.exe\" -w");
        if cfg!(target_os = "linux") {
            // Steam's own Proton runs it: the game itself, with only the player's options.
            let s = Shortcut::new("G", &target(true, "-w"), None);
            assert_eq!((s.exe.as_str(), s.launch_options.as_str(), s.proton), ("\"/games/G/game.exe\"", "-w", true));
        }
    }

    fn shortcut(name: &str) -> Shortcut {
        Shortcut {
            name: name.into(),
            exe: "\"/games/a/run\"".into(),
            start_dir: "\"/games/a\"".into(),
            launch_options: String::new(),
            shortcut_path: String::new(),
            icon: String::new(),
            proton: false,
            art: vec![],
        }
    }

    fn target() -> crate::shortcut::Target {
        crate::shortcut::Target {
            exe: "/games/G/game.exe".into(),
            program: "/protons/GE/proton".into(),
            args: vec!["run".into(), "/games/G/game.exe".into()],
            env: vec![("STEAM_COMPAT_DATA_PATH".into(), "/data/proton/G".into())],
            dir: "/games/G".into(),
            extract_appimage: false,
            steam_proton: false,
            options: crate::launch::parse("DXVK_HUD=1 %command% -w").unwrap(),
        }
    }

    #[test]
    fn a_linux_shortcut_runs_a_script_the_desktop_names() {
        let text = script_text(&target(), "G");
        assert!(text.starts_with("#!/bin/sh\n"), "{text}");
        assert!(text.contains("\ncd /games/G || exit 1\n"), "{text}");
        assert!(text.contains("\nexec env STEAM_COMPAT_DATA_PATH=/data/proton/G DXVK_HUD=1 /protons/GE/proton run /games/G/game.exe -w\n"), "{text}");
        assert_eq!(shell_word("My Game's"), "'My Game'\\''s'");

        let dir = tempfile::tempdir().unwrap();
        let launcher = Launcher::write(dir.path(), "G", "G", &target(), None).unwrap();
        assert_eq!(launcher.app_id("G"), app_id(&launcher.exe(), "G"));
        // The plain script path is covered too, whichever way Steam stored it.
        let ids = launcher.app_ids("G");
        assert!(ids.contains(&launcher.app_id("G")));
        assert!(ids.contains(&app_id(launcher.exe().trim_matches('"'), "G")));
        let desktop = std::fs::read_to_string(launcher.desktop()).unwrap();
        assert!(desktop.contains("Name=G\n"), "{desktop}");
        assert!(desktop.contains("Exec=\"") && desktop.contains("G.sh"), "{desktop}");
        assert!(desktop.contains("Path=/games/G\n"), "{desktop}");
    }

    #[test]
    #[cfg(target_os = "linux")]
    fn a_steam_proton_shortcut_names_the_game_so_steam_runs_it() {
        let target = |options: &str| crate::shortcut::Target {
            exe: "/games/G/game.exe".into(),
            program: "/protons/GE/proton".into(),
            args: vec!["run".into(), "/games/G/game.exe".into()],
            env: vec![("STEAM_COMPAT_DATA_PATH".into(), "/data/proton/G".into())],
            dir: "/games/G".into(),
            extract_appimage: false,
            steam_proton: true,
            options: crate::launch::parse(options).unwrap(),
        };

        let dir = tempfile::tempdir().unwrap();
        let launcher = Launcher::write_proton(dir.path(), "G", "G", &target(""), None).unwrap();
        // Steam stores the game itself, so it applies its own Proton to it.
        assert_eq!(launcher.exe(), "\"/games/G/game.exe\"");
        assert!(launcher.app_ids("G").contains(&app_id("\"/games/G/game.exe\"", "G")));
        assert!(launcher.app_ids("G").contains(&app_id("/games/G/game.exe", "G")));
        let desktop = std::fs::read_to_string(launcher.desktop()).unwrap();
        assert!(desktop.contains("Name=G\n"), "{desktop}");
        assert!(desktop.contains("Exec=\"/games/G/game.exe\"\n"), "{desktop}");
        assert!(desktop.contains("Path=/games/G\n"), "{desktop}");

        // The player's launch options follow the game, for Steam's LaunchOptions.
        let launcher = Launcher::write_proton(dir.path(), "G", "G", &target("DXVK_HUD=1 %command% -w"), None).unwrap();
        let desktop = std::fs::read_to_string(launcher.desktop()).unwrap();
        assert!(desktop.contains("Exec=\"/games/G/game.exe\" DXVK_HUD=1 %command% -w\n"), "{desktop}");
    }

    #[test]
    #[cfg(unix)]
    fn a_steam_proton_shortcut_makes_the_game_runnable_for_steam() {
        use std::os::unix::fs::PermissionsExt;
        let dir = tempfile::tempdir().unwrap();
        let exe = dir.path().join("Game.exe");
        std::fs::write(&exe, b"MZ").unwrap();
        std::fs::set_permissions(&exe, std::fs::Permissions::from_mode(0o644)).unwrap();
        let target = crate::shortcut::Target {
            exe: exe.clone(),
            program: "/protons/GE/proton".into(),
            args: vec![],
            env: vec![],
            dir: dir.path().to_path_buf(),
            extract_appimage: false,
            steam_proton: true,
            options: Default::default(),
        };
        // Steam stores the desktop's program as the shortcut only when it can run it.
        Launcher::write_proton(dir.path(), "G", "G", &target, None).unwrap();
        assert_eq!(std::fs::metadata(&exe).unwrap().permissions().mode() & 0o111, 0o111, "Steam can run it");
    }

    #[test]
    fn a_path_is_percent_encoded_for_the_steam_url() {
        assert_eq!(percent_encode("/home/deck/Games/My Game/run.sh"), "%2Fhome%2Fdeck%2FGames%2FMy%20Game%2Frun.sh");
        assert_eq!(percent_encode("/home/deck/a+b~c.d-e_f"), "%2Fhome%2Fdeck%2Fa%2Bb~c.d-e_f");
        assert_eq!(percent_encode("/home/deck/café"), "%2Fhome%2Fdeck%2Fcaf%C3%A9");
    }

    #[test]
    fn a_shortcut_records_the_desktop_it_came_from() {
        let mut s = shortcut("Game");
        s.shortcut_path = "/data/steam/Game.desktop".into();
        let (bytes, _) = upsert(None, &s).unwrap();
        let root = parse(&bytes).unwrap();
        let Vdf::Map(list) = &root[0].1 else { panic!() };
        assert!(matches!(&list[0].1, Vdf::Map(m) if m.contains(&("ShortcutPath".into(), Vdf::Str("/data/steam/Game.desktop".into())))));
        // An update keeps it too.
        let (bytes, _) = upsert(Some(&bytes), &s).unwrap();
        let root = parse(&bytes).unwrap();
        let Vdf::Map(list) = &root[0].1 else { panic!() };
        assert!(matches!(&list[0].1, Vdf::Map(m) if m.contains(&("ShortcutPath".into(), Vdf::Str("/data/steam/Game.desktop".into())))));
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
