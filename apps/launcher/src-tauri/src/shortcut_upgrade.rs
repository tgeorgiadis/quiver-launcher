//! Shortcuts Quiver made before its shortcuts started games through it (shortcut_play.rs) name the game itself, so
//! their saves don't sync. As Quiver starts, those it can tell are its own and an installed app's are changed to start
//! the game through Quiver (`quiver --launch <app id>`), as new ones do (docs/cloud-saves.md, "From a shortcut").
//!
//! - **Desktop shortcuts**: the `.lnk` (Windows) or `.desktop` (Linux) on the desktop under the name Quiver gives an
//!   app's (shortcut::file_name), that Quiver wrote ("Play <name>") and that points into the app's folder or at its
//!   program. Rewritten in place, keeping its icon.
//! - **Steam shortcuts**: those in shortcuts.vdf tagged `QuiverLauncher`, or added from a launcher under Quiver's
//!   `data/steam` (Linux, where Steam's own add doesn't tag them), that don't start through Quiver yet. An entry goes
//!   to its app by its launcher (`data/steam/<folder>.desktop`), or else by the paths it names: its program, its folder
//!   or its launch options inside the app's folder, or at its program. The entry keeps its app id, so its artwork,
//!   collections and play time stay; launch options the player added after Quiver's are kept. Steam must be closed for
//!   the write, which goes through steam.rs's guarded one (backups, never fewer shortcuts); while it's open a waiting
//!   copy of Quiver makes it once Steam closes, as Add to Steam does. On Linux the launcher's files are rewritten at
//!   once, and an old shortcut that runs the launcher's script starts through Quiver straight away.
//!
//! Nothing is changed that Quiver can't tie to exactly one installed app, and what's found is worked out from the
//! files each time, so it's done once in effect: a shortcut changed is through Quiver and isn't found again.
use crate::shortcut::{self, Target};
use crate::steam::{self, Vdf};
use std::path::{Path, PathBuf};

/// An installed app, as far as finding its old shortcuts needs.
pub struct App {
    pub id: String,
    pub name: String,
    /// Its folder's plain name: what its Linux Steam launcher is named for.
    pub folder: String,
    /// The app's folder.
    pub dir: Option<PathBuf>,
    /// What Play would start, when it can start now.
    pub game: Option<Target>,
}

/// A path as compared: no quotes, forward slashes, no slash at the end, and on Windows any case.
fn norm(path: &str) -> String {
    let p = path.trim().trim_matches('"').replace('\\', "/");
    let p = p.trim_end_matches('/');
    if cfg!(windows) { p.to_lowercase() } else { p.to_string() }
}

fn within(path: &str, dir: &str) -> bool {
    !dir.is_empty() && (path == dir || path.starts_with(&format!("{dir}/")))
}

/// The words of a command line as Steam and `.desktop` files quote them: split on spaces outside double quotes, with a
/// backslash escaping the next character inside them.
fn words(line: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut word = String::new();
    let (mut quoted, mut any) = (false, false);
    let mut chars = line.chars();
    while let Some(c) = chars.next() {
        match c {
            '"' => {
                quoted = !quoted;
                any = true;
            }
            '\\' if quoted => word.extend(chars.next()),
            c if c.is_whitespace() && !quoted => {
                if any || !word.is_empty() {
                    out.push(std::mem::take(&mut word));
                }
                any = false;
            }
            c => word.push(c),
        }
    }
    if any || !word.is_empty() {
        out.push(word);
    }
    out
}

/// One of the paths names something of the app's: inside its folder, or its program or that program's folder.
fn points_at(paths: &[String], app: &App) -> bool {
    let dir = app.dir.as_deref().map(|d| norm(&d.to_string_lossy()));
    let exe = app.game.as_ref().map(|g| norm(&g.exe.to_string_lossy()));
    let game_dir = app.game.as_ref().map(|g| norm(&g.dir.to_string_lossy()));
    paths.iter().map(|p| norm(p)).filter(|p| !p.is_empty()).any(|p| {
        dir.as_deref().is_some_and(|d| within(&p, d)) || exe.as_deref() == Some(p.as_str()) || game_dir.as_deref() == Some(p.as_str())
    })
}

/// The one app the paths name, by its name among several.
fn pick(paths: &[String], name: &str, apps: &[App]) -> Option<usize> {
    let found: Vec<usize> = (0..apps.len()).filter(|&i| points_at(paths, &apps[i])).collect();
    match found.as_slice() {
        [one] => Some(*one),
        [] => None,
        many => match many.iter().filter(|&&i| apps[i].name == name).collect::<Vec<_>>().as_slice() {
            [one] => Some(**one),
            _ => None,
        },
    }
}

fn text<'a>(m: &'a [(String, Vdf)], key: &str) -> &'a str {
    match m.iter().find(|(k, _)| k.eq_ignore_ascii_case(key)) {
        Some((_, Vdf::Str(s))) => s,
        _ => "",
    }
}

/// A Steam shortcut Quiver made that still names its game, and the app it's for.
#[derive(Debug, PartialEq)]
pub struct OldSteam {
    pub appid: u32,
    pub app: usize,
    /// Its launch options as they are, for any the player added.
    pub launch_options: String,
}

/// The shortcuts in Steam's list that Quiver made for an installed app and that don't start through Quiver yet.
/// `launchers` is Quiver's `data/steam` folder.
pub fn old_in_steam(list: &[Vec<(String, Vdf)>], launchers: &Path, apps: &[App]) -> Vec<OldSteam> {
    let launchers = norm(&launchers.to_string_lossy());
    let mut out = Vec::new();
    for m in list {
        let tagged = matches!(m.iter().find(|(k, _)| k.eq_ignore_ascii_case("tags")), Some((_, Vdf::Map(t))) if t.iter().any(|(_, v)| *v == Vdf::Str(steam::TAG.into())));
        let from = norm(text(m, "ShortcutPath"));
        let launched = within(&from, &launchers) && from != launchers;
        let (Some(appid), true, false) = (steam::id_in(m), tagged || launched, steam::through_quiver(m)) else { continue };
        let by_launcher = (0..apps.len()).find(|&i| from == norm(&format!("{launchers}/{}.desktop", apps[i].folder)));
        let app = by_launcher.or_else(|| {
            let mut paths = vec![text(m, "exe").to_string(), text(m, "StartDir").to_string()];
            paths.extend(words(text(m, "LaunchOptions")));
            pick(&paths, text(m, "appname"), apps)
        });
        if let Some(app) = app {
            out.push(OldSteam { appid, app, launch_options: text(m, "LaunchOptions").to_string() });
        }
    }
    out
}

/// What an old Steam shortcut's launch options were as Quiver wrote them, by the ways it has: for the player's own after.
fn written_before(game: &Target) -> Vec<String> {
    let (_, args, env) = game.command();
    let launcher = if env.is_empty() { steam::options_of(&args) } else { String::new() };
    vec![steam::Shortcut::new("", game, None).launch_options, launcher]
}

/// The launch options the player added after Quiver's own, which go after the new shortcut's for the game. None that
/// can't be told apart, and none that wrap the command, which would wrap Quiver now.
fn extras(current: &str, before: &[String]) -> String {
    let current = current.trim();
    let rest = before.iter().find_map(|b| {
        let b = b.trim();
        if b.is_empty() {
            Some(current)
        } else {
            current.strip_prefix(b).filter(|r| r.is_empty() || r.starts_with(' ')).map(str::trim)
        }
    });
    rest.filter(|r| !r.contains("%command%")).unwrap_or_default().to_string()
}

/// The Steam shortcut an old one becomes: through Quiver, as Add to Steam makes it now, keeping its app id. On Linux
/// this rewrites the app's launcher under `data/steam` too, and its script, if an old shortcut runs one.
pub fn new_steam(data: &Path, old: &OldSteam, app: &App) -> Result<steam::Shortcut, String> {
    let target = crate::shortcut_play::shortcut_target(&app.id);
    #[cfg(target_os = "linux")]
    let mut s = {
        let icon = Some(data.join("icons").join(format!("{}.png", app.folder))).filter(|p| p.is_file());
        let launcher = steam::Launcher::write(data, &app.folder, &app.name, &target, icon.as_deref())?;
        // A shortcut that runs the old script starts through Quiver at once, before Steam's file is changed.
        let script = data.join("steam").join(format!("{}.sh", app.folder));
        if script.is_file() {
            std::fs::write(&script, steam::script_text(&target, &app.name)).map_err(|e| e.to_string())?;
        }
        launcher.shortcut(&app.name, &target, None, vec![])
    };
    #[cfg(not(target_os = "linux"))]
    let mut s = {
        let _ = data;
        steam::Shortcut::new(&app.name, &target, None)
    };
    s.appid = Some(old.appid);
    // Quiver applies the player's launch options itself now; those added in Steam after Quiver's go to the game.
    let before = app.game.as_ref().map(written_before).unwrap_or_default();
    let extra = extras(&old.launch_options, &before);
    if !extra.is_empty() {
        s.launch_options = if s.launch_options.is_empty() { extra } else { format!("{} {extra}", s.launch_options) };
    }
    Ok(s)
}

/// What a desktop shortcut of Quiver's names: its description, folder, command and icon.
#[derive(Debug, Default, PartialEq)]
pub struct Desktop {
    pub comment: String,
    pub dir: String,
    pub command: Vec<String>,
    pub icon: String,
}

/// A Windows `.lnk`'s strings (MS-SHLLINK StringData): what Quiver wrote in it.
pub fn read_lnk(b: &[u8]) -> Option<Desktop> {
    let u32_at = |at: usize| b.get(at..at + 4).map(|s| u32::from_le_bytes(s.try_into().unwrap()));
    let u16_at = |at: usize| b.get(at..at + 2).map(|s| u16::from_le_bytes(s.try_into().unwrap()) as usize);
    if u32_at(0)? != 0x4C {
        return None;
    }
    let flags = u32_at(0x14)?;
    let mut at = 0x4C;
    if flags & 1 != 0 {
        at += 2 + u16_at(at)?;
    }
    if flags & 2 != 0 {
        at += u32_at(at)? as usize;
    }
    let unicode = flags & 0x80 != 0;
    let mut strings = [String::new(), String::new(), String::new(), String::new(), String::new()];
    // Name, relative path, working folder, arguments, icon: each there when its flag is.
    for (n, string) in strings.iter_mut().enumerate() {
        if flags & (4 << n) == 0 {
            continue;
        }
        let count = u16_at(at)?;
        at += 2;
        if unicode {
            let units: Vec<u16> = b.get(at..at + count * 2)?.chunks(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
            *string = String::from_utf16_lossy(&units);
            at += count * 2;
        } else {
            *string = String::from_utf8_lossy(b.get(at..at + count)?).into_owned();
            at += count;
        }
    }
    let [comment, _, dir, arguments, icon] = strings;
    Some(Desktop { comment, dir, command: words(&arguments), icon })
}

/// A `.desktop` file's entry: what Quiver wrote in it.
pub fn read_desktop(text: &str) -> Option<Desktop> {
    let unescape = |v: &str| {
        let mut out = String::new();
        let mut chars = v.chars();
        while let Some(c) = chars.next() {
            if c != '\\' {
                out.push(c);
                continue;
            }
            match chars.next() {
                Some('n') => out.push('\n'),
                Some('t') => out.push('\t'),
                Some('r') => out.push('\r'),
                Some(o) => out.push(o),
                None => {}
            }
        }
        out
    };
    let mut entry = Desktop::default();
    let mut inside = false;
    let mut exec = None;
    for line in text.lines() {
        let line = line.trim();
        if line.starts_with('[') {
            inside = line == "[Desktop Entry]";
            continue;
        }
        let Some((key, value)) = line.split_once('=').filter(|_| inside) else { continue };
        let value = unescape(value);
        match key.trim() {
            "Comment" => entry.comment = value,
            "Path" => entry.dir = value,
            "Icon" => entry.icon = value,
            "Exec" => exec = Some(value),
            _ => {}
        }
    }
    entry.command = words(&exec?.replace("%%", "%"));
    Some(entry)
}

/// A desktop shortcut is one Quiver made for this app that still names the game.
pub fn old_desktop(entry: &Desktop, app: &App) -> bool {
    if entry.comment != format!("Play {}", app.name) || entry.command.iter().any(|w| w == crate::shortcut_play::LAUNCH) {
        return false;
    }
    let mut paths = entry.command.clone();
    paths.push(entry.dir.clone());
    points_at(&paths, app)
}

/// Rewrites the app's old desktop shortcut in `desktop`, if it has one; says whether it did.
pub fn upgrade_desktop(desktop: &Path, app: &App) -> Result<bool, String> {
    let path = shortcut::desktop_file(desktop, &app.name);
    let Ok(bytes) = std::fs::read(&path) else { return Ok(false) };
    let entry = if cfg!(windows) { read_lnk(&bytes) } else { read_desktop(&String::from_utf8_lossy(&bytes)) };
    let Some(entry) = entry.filter(|e| old_desktop(e, app)) else { return Ok(false) };
    let icon = Some(PathBuf::from(&entry.icon)).filter(|p| !entry.icon.is_empty() && (p.is_file() || !p.is_absolute()));
    let icon = icon.or_else(|| app.game.as_ref().filter(|_| cfg!(windows)).map(|g| g.exe.clone()));
    shortcut::write_at(&path, &crate::shortcut_play::shortcut_target(&app.id), &app.name, icon.as_deref())?;
    Ok(true)
}

/// The desktop shortcut files there are for these apps: whether finding their games is worth it.
pub fn any_on_desktop(desktop: &Path, apps: &[App]) -> bool {
    apps.iter().any(|a| shortcut::desktop_file(desktop, &a.name).is_file())
}

/// Steam's shortcuts that are Quiver's and still name their game, before the apps' games are known: whether finding
/// them is worth it.
pub fn any_in_steam(list: &[Vec<(String, Vdf)>], launchers: &Path) -> bool {
    let launchers = norm(&launchers.to_string_lossy());
    list.iter().any(|m| {
        let tagged = matches!(m.iter().find(|(k, _)| k.eq_ignore_ascii_case("tags")), Some((_, Vdf::Map(t))) if t.iter().any(|(_, v)| *v == Vdf::Str(steam::TAG.into())));
        let from = norm(text(m, "ShortcutPath"));
        (tagged || within(&from, &launchers) && from != launchers) && !steam::through_quiver(m)
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn game(exe: &str) -> Target {
        let exe = PathBuf::from(exe);
        Target { dir: exe.parent().unwrap().to_path_buf(), program: exe.clone(), exe, args: vec![], env: vec![], extract_appimage: false, steam_proton: false, options: Default::default() }
    }

    fn app(id: &str, name: &str, dir: &str, exe: Option<&str>) -> App {
        App { id: id.into(), name: name.into(), folder: name.into(), dir: Some(dir.into()), game: exe.map(game) }
    }

    fn entry(fields: &[(&str, &str)], tagged: bool) -> Vec<(String, Vdf)> {
        let mut m: Vec<(String, Vdf)> = fields.iter().map(|(k, v)| (k.to_string(), Vdf::Str(v.to_string()))).collect();
        m.insert(0, ("appid".into(), Vdf::Int(0x8000_1234)));
        if tagged {
            m.push(("tags".into(), Vdf::Map(vec![("0".into(), Vdf::Str(steam::TAG.into()))])));
        }
        m
    }

    #[test]
    fn command_lines_split_as_steam_quotes_them() {
        assert_eq!(words(r#""run" "/games/My Game/game.exe" -w"#), ["run", "/games/My Game/game.exe", "-w"]);
        assert_eq!(words(r#""a \"b\"" """#), ["a \"b\"", ""]);
        assert!(words("  ").is_empty());
    }

    #[test]
    fn an_old_steam_shortcut_goes_to_the_app_whose_folder_it_names() {
        let apps = [app("a", "Alpha", "/games/Alpha", Some("/games/Alpha/alpha")), app("b", "Beta", "/games/Beta", Some("/games/Beta/bin/beta"))];
        let launchers = Path::new("/data/steam");
        // Windows: the game itself, tagged.
        let native = entry(&[("appname", "Beta"), ("exe", "\"/games/Beta/bin/beta\""), ("StartDir", "\"/games/Beta/bin\""), ("LaunchOptions", "")], true);
        // Through a Proton, which isn't the app's: the game is in its launch options.
        let proton = entry(&[("appname", "My Alpha"), ("exe", "\"/protons/GE/proton\""), ("StartDir", "\"/games/Alpha\""), ("LaunchOptions", "STEAM_COMPAT_DATA_PATH=/d %command% \"run\" \"/games/Alpha/alpha\"")], true);
        let found = old_in_steam(&[native, proton], launchers, &apps);
        assert_eq!(found.iter().map(|o| o.app).collect::<Vec<_>>(), [1, 0]);
        assert_eq!(found[0].appid, 0x8000_1234);
    }

    #[test]
    fn a_linux_shortcut_goes_to_the_app_its_launcher_is_for() {
        let apps = [app("a", "Alpha", "/games/Alpha", None), app("b", "Beta", "/games/Beta", None)];
        // Added by Steam from Quiver's launcher, so untagged, and running its script, outside the app's folder.
        let live = entry(&[("appname", "Beta"), ("exe", "\"/data/steam/Beta.sh\""), ("ShortcutPath", "/data/steam/Beta.desktop")], false);
        let found = old_in_steam(&[live], Path::new("/data/steam"), &apps);
        assert_eq!(found.iter().map(|o| o.app).collect::<Vec<_>>(), [1]);
    }

    #[test]
    fn steam_shortcuts_that_arent_old_or_quivers_are_left() {
        let apps = [app("a", "Alpha", "/games/Alpha", Some("/games/Alpha/alpha"))];
        let launchers = Path::new("/data/steam");
        // The player's own shortcut to the game: not Quiver's.
        let theirs = entry(&[("appname", "Alpha"), ("exe", "\"/games/Alpha/alpha\"")], false);
        // Through Quiver already.
        let new = entry(&[("appname", "Alpha"), ("exe", "\"/apps/quiver\""), ("StartDir", "\"/games/Alpha\""), ("LaunchOptions", "\"--launch\" \"a\"")], true);
        // Quiver's, for an app that isn't installed.
        let gone = entry(&[("appname", "Gamma"), ("exe", "\"/games/Gamma/gamma\"")], true);
        assert!(old_in_steam(&[theirs.clone(), new.clone(), gone.clone()], launchers, &apps).is_empty());
        assert!(any_in_steam(&[gone], launchers));
        assert!(!any_in_steam(&[theirs, new], launchers));
    }

    #[test]
    fn a_shortcut_two_apps_could_be_goes_by_its_name_or_not_at_all() {
        // Two library entries in the one folder.
        let apps = [app("a", "Alpha", "/games/Twin", None), app("b", "Beta", "/games/Twin", None)];
        let named = entry(&[("appname", "Beta"), ("exe", "\"/games/Twin/run\"")], true);
        let renamed = entry(&[("appname", "Mine"), ("exe", "\"/games/Twin/run\"")], true);
        let found = old_in_steam(&[named, renamed], Path::new("/data/steam"), &apps);
        assert_eq!(found.iter().map(|o| o.app).collect::<Vec<_>>(), [1]);
    }

    #[test]
    fn launch_options_the_player_added_are_kept() {
        let before = vec!["\"--fullscreen\"".to_string()];
        assert_eq!(extras("\"--fullscreen\" -nointro", &before), "-nointro");
        assert_eq!(extras("\"--fullscreen\"", &before), "");
        // Not Quiver's at all, so not told apart.
        assert_eq!(extras("-w", &before), "");
        // Quiver wrote none: they're all the player's, unless they wrap the command.
        assert_eq!(extras("-w", &[String::new()]), "-w");
        assert_eq!(extras("gamemoderun %command%", &[String::new()]), "");
    }

    #[test]
    fn an_old_steam_shortcut_becomes_one_through_quiver_with_its_id() {
        let data = tempfile::tempdir().unwrap();
        let a = app("abc", "Alpha", "/games/Alpha", Some("/games/Alpha/alpha"));
        let old = OldSteam { appid: 0x8000_1234, app: 0, launch_options: "-nointro".into() };
        let s = new_steam(data.path(), &old, &a).unwrap();
        assert_eq!(s.appid, Some(0x8000_1234));
        assert!(s.launch_options.contains("\"--launch\" \"abc\""), "{}", s.launch_options);
        assert!(s.launch_options.ends_with(" -nointro"), "{}", s.launch_options);
        assert!(s.icon.is_empty(), "the icon Steam has stays");
        // A shortcuts.vdf with it and another: only it changes, keeping its name and play time.
        let other = entry(&[("appname", "Other")], false);
        let mut alpha = entry(&[("appname", "My Alpha"), ("exe", "\"/games/Alpha/alpha\""), ("LaunchOptions", "-nointro")], true);
        alpha.push(("LastPlayTime".into(), Vdf::Int(99)));
        let mut other = other;
        other[0].1 = Vdf::Int(7);
        let file = steam::write(&[("shortcuts".into(), Vdf::Map(vec![("0".into(), Vdf::Map(other.clone())), ("1".into(), Vdf::Map(alpha))]))]);
        let (bytes, ids) = steam::upsert_all(Some(&file), &[s]).unwrap();
        assert_eq!(ids, [Some(0x8000_1234)]);
        let root = steam::parse(&bytes).unwrap();
        let Vdf::Map(list) = &root[0].1 else { panic!() };
        assert_eq!(list.len(), 2);
        assert_eq!(list[0].1, Vdf::Map(other));
        let Vdf::Map(changed) = &list[1].1 else { panic!() };
        assert_eq!(text(changed, "appname"), "My Alpha");
        assert!(steam::through_quiver(changed));
        assert!(changed.contains(&("LastPlayTime".into(), Vdf::Int(99))));
        assert_eq!(steam::id_in(changed), Some(0x8000_1234));
    }

    #[test]
    fn a_shortcut_to_change_thats_gone_from_steam_isnt_added() {
        let mut s = steam::Shortcut::new("Alpha", &game("/games/Alpha/alpha"), None);
        s.appid = Some(42);
        let file = steam::write(&[("shortcuts".into(), Vdf::Map(vec![]))]);
        let (bytes, ids) = steam::upsert_all(Some(&file), &[s]).unwrap();
        assert_eq!(ids, [None]);
        assert_eq!(bytes, file);
    }

    #[test]
    fn an_old_desktop_file_is_found_and_rewritten() {
        let a = app("abc", "Alpha", "/games/Alpha", Some("/games/Alpha/alpha"));
        let old = shortcut::desktop_entry(a.game.as_ref().unwrap(), "Alpha", Some(Path::new("/data/icons/Alpha.png")));
        let entry = read_desktop(&old).unwrap();
        assert_eq!(entry.icon, "/data/icons/Alpha.png");
        assert!(old_desktop(&entry, &a));
        // Another app's, or one through Quiver already, is left.
        assert!(!old_desktop(&entry, &app("b", "Beta", "/games/Beta", None)));
        let new = shortcut::desktop_entry(&crate::shortcut_play::shortcut_target("abc"), "Alpha", None);
        assert!(!old_desktop(&read_desktop(&new).unwrap(), &a));
        // Not Quiver's: the player's own launcher to the game.
        assert!(!old_desktop(&read_desktop("[Desktop Entry]\nExec=/games/Alpha/alpha\n").unwrap(), &a));
    }

    #[cfg(not(windows))]
    #[test]
    fn an_old_desktop_shortcut_is_rewritten_in_place() {
        let desktop = tempfile::tempdir().unwrap();
        let a = app("abc", "Alpha", "/games/Alpha", Some("/games/Alpha/alpha"));
        let path = shortcut::desktop_file(desktop.path(), "Alpha");
        std::fs::write(&path, shortcut::desktop_entry(a.game.as_ref().unwrap(), "Alpha", Some(Path::new("alpha-icon")))).unwrap();
        assert!(any_on_desktop(desktop.path(), std::slice::from_ref(&a)));
        assert!(upgrade_desktop(desktop.path(), &a).unwrap());
        let now = read_desktop(&std::fs::read_to_string(&path).unwrap()).unwrap();
        assert!(now.command.windows(2).any(|w| w == ["--launch", "abc"]), "{now:?}");
        assert_eq!(now.icon, "alpha-icon");
        assert!(!upgrade_desktop(desktop.path(), &a).unwrap(), "once");
    }

    #[cfg(windows)]
    #[test]
    fn an_old_lnk_is_read_and_rewritten_in_place() {
        let games = tempfile::tempdir().unwrap();
        let exe = games.path().join("Alpha").join("alpha.exe");
        std::fs::create_dir_all(exe.parent().unwrap()).unwrap();
        std::fs::write(&exe, b"MZ").unwrap();
        let a = app("abc", "Alpha", &exe.parent().unwrap().to_string_lossy(), Some(&exe.to_string_lossy()));
        let desktop = tempfile::tempdir().unwrap();
        let path = shortcut::desktop_file(desktop.path(), "Alpha");
        let mut old = game(&exe.to_string_lossy());
        old.args = vec!["--full screen".into()];
        shortcut::write_at(&path, &old, "Alpha", Some(&exe)).unwrap();
        let entry = read_lnk(&std::fs::read(&path).unwrap()).unwrap();
        assert_eq!(entry.comment, "Play Alpha");
        assert_eq!(entry.command, ["--full screen"]);
        assert_eq!(norm(&entry.dir), norm(&exe.parent().unwrap().to_string_lossy()));
        assert!(old_desktop(&entry, &a));
        assert!(upgrade_desktop(desktop.path(), &a).unwrap());
        let now = read_lnk(&std::fs::read(&path).unwrap()).unwrap();
        assert!(now.command.windows(2).any(|w| w == ["--launch", "abc"]), "{now:?}");
        assert_eq!(now.icon, exe.to_string_lossy());
        assert!(!upgrade_desktop(desktop.path(), &a).unwrap(), "once");
    }
}
