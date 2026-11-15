//! The Flatpak's sandbox. Inside it, what the launcher starts would run on
//! the GNOME runtime's libraries rather than the system's, without Wine,
//! Proton, Steam or Flatpak itself, so programs of the system's (games, Wine,
//! `flatpak`, `xdg-open`, Steam, the launcher's own next copy) start on the
//! host through `flatpak-spawn --host`, which the manifest's
//! org.freedesktop.Flatpak permission allows. The home folder is the same path
//! in and out, so the apps folder, the data folder and Steam's are too.
use std::ffi::{OsStr, OsString};
use std::path::{Path, PathBuf};
use std::process::Command;

/// The Flatpak's app id, when the launcher runs as one.
pub fn id() -> Option<String> {
    std::env::var("FLATPAK_ID").ok().filter(|id| !id.is_empty())
}

/// The command to start on the host: the same command outside a Flatpak.
/// Its program, arguments, environment and folder carry over; set its
/// standard input and output on what this returns.
pub fn host(command: Command) -> Command {
    spawn(command, false)
}

/// `watched`: the program ends when the launcher does, rather than outliving it.
fn spawn(command: Command, watched: bool) -> Command {
    if id().is_none() {
        return command;
    }
    let envs = command.get_envs().filter_map(|(name, value)| Some((name, value?)));
    // Otherwise it starts in the launcher's folder, which may be the sandbox's own.
    let home = std::env::var_os("HOME").map(PathBuf::from);
    let dir = command.get_current_dir().or(home.as_deref());
    let mut spawn = Command::new("flatpak-spawn");
    spawn.args(spawn_args(command.get_program(), command.get_args(), envs, dir, watched));
    spawn
}

/// A game to start on the host: the same command outside a Flatpak. `flatpak-spawn` starts it with the session's
/// environment, not the launcher's, and as a process of Flatpak's, not of what started the launcher. In Steam's Game
/// Mode that left a game with no display (or Steam's own, not the games'), without what Steam sets for a game (its
/// controller settings, gamescope's), and not one of Steam's processes, which are all gamescope shows: it ran unseen
/// behind the launcher, and went on running, holding the controller, after the launcher closed. So it gets the
/// launcher's display and what Steam set, and in Game Mode starts as Steam's own games do and ends with the launcher.
pub fn game(mut command: Command) -> Command {
    if id().is_none() {
        return command;
    }
    let var = |name: &str| std::env::var(name).ok();
    let own: Vec<OsString> = command.get_envs().map(|(name, _)| name.to_os_string()).collect();
    let vars = std::env::vars_os().filter_map(|(name, value)| Some((name.into_string().ok()?, value)));
    let display = display().map(|display| ("DISPLAY".to_string(), OsString::from(display)));
    // What the launcher sets for the game itself stays as it is.
    for (name, value) in carried(vars).chain(display).filter(|(name, _)| !own.contains(&OsString::from(name))) {
        command.env(name, value);
    }
    let steam = if crate::keyboard::game_mode(var) { steam_app(var).and_then(|app| Some((reaper(app)?, app))) } else { None };
    let Some((reaper, app)) = steam else { return host(command) };
    let mut tracked = Command::new("sh");
    tracked.args(tracked_args(&reaper, app)).arg(command.get_program()).args(command.get_args());
    for (name, value) in command.get_envs() {
        match value {
            Some(value) => tracked.env(name, value),
            None => tracked.env_remove(name),
        };
    }
    if let Some(dir) = command.get_current_dir() {
        tracked.current_dir(dir);
    }
    spawn(tracked, true)
}

/// What Steam and gamescope set for the launcher that a game it starts needs too, as it has from an AppImage: which
/// app it's part of, the controllers to leave to Steam Input, gamescope's and the graphics drivers' settings. Not the
/// rest: the sandbox's paths aren't the host's.
fn carried(vars: impl Iterator<Item = (String, OsString)>) -> impl Iterator<Item = (String, OsString)> {
    const PREFIXES: [&str; 10] = ["Steam", "STEAM", "GAMESCOPE", "SDL_", "ENABLE_", "DISABLE_", "MANGOHUD", "XCURSOR_", "RADV_", "__GL_"];
    vars.filter(|(name, _)| PREFIXES.iter().any(|prefix| name.starts_with(prefix)))
}

/// The launcher's display, as the host names it. Flatpak may number it differently inside the sandbox, and the
/// session's own may be another: Game Mode has one for Steam and one for games. Found by its socket, the same file
/// in and out.
fn display() -> Option<String> {
    static DISPLAY: std::sync::OnceLock<Option<String>> = std::sync::OnceLock::new();
    DISPLAY
        .get_or_init(|| {
            let socket = socket_id(&display_socket(&std::env::var("DISPLAY").ok()?)?)?;
            let mut find = Command::new("sh");
            find.args(["-c", r#"for s in /tmp/.X11-unix/X*; do [ "$(stat -c %d:%i "$s" 2> /dev/null)" = "$1" ] && echo "${s##*/X}" && exit 0; done; exit 1"#, "sh", &socket]);
            let out = host(find).output().ok().filter(|o| o.status.success())?;
            let number = String::from_utf8_lossy(&out.stdout).trim().to_string();
            (!number.is_empty() && number.bytes().all(|b| b.is_ascii_digit())).then(|| format!(":{number}"))
        })
        .clone()
}

/// A local display's socket: ":1" and ":1.0" have /tmp/.X11-unix/X1. None for a display on another computer.
fn display_socket(display: &str) -> Option<PathBuf> {
    let number = display.strip_prefix(':')?.split('.').next()?;
    (!number.is_empty() && number.bytes().all(|b| b.is_ascii_digit())).then(|| PathBuf::from(format!("/tmp/.X11-unix/X{number}")))
}

/// What tells a file from every other, as `stat -c %d:%i` prints it.
fn socket_id(socket: &Path) -> Option<String> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::MetadataExt;
        let meta = std::fs::metadata(socket).ok()?;
        Some(format!("{}:{}", meta.dev(), meta.ino()))
    }
    #[cfg(not(unix))]
    {
        let _ = socket;
        None
    }
}

/// The Steam app the launcher runs as: the one whose windows gamescope shows. A non-Steam game's id is the upper
/// half of SteamGameId; SteamAppId has it too, or 0.
fn steam_app(var: impl Fn(&str) -> Option<String>) -> Option<u32> {
    let number = |name: &str| var(name).and_then(|v| v.trim().parse::<u64>().ok());
    let game = number("SteamGameId").map(|id| if id > u64::from(u32::MAX) { id >> 32 } else { id });
    game.into_iter().chain(number("SteamAppId")).filter_map(|id| u32::try_from(id).ok()).find(|id| *id != 0)
}

/// Steam's program that starts each of its games and keeps what they start together: the one running the launcher.
/// gamescope takes a window to be an app's when its process descends from that app's reaper, so a game started
/// under one of its own is shown as the launcher is, in its place.
fn reaper(app: u32) -> Option<PathBuf> {
    static REAPER: std::sync::OnceLock<Option<PathBuf>> = std::sync::OnceLock::new();
    REAPER
        .get_or_init(|| {
            let script = r#"for p in /proc/[0-9]*; do read -r c < "$p/comm" 2> /dev/null || continue; [ "$c" = reaper ] || continue; tr '\0' '\n' < "$p/cmdline" 2> /dev/null | grep -qx "AppId=$1" && readlink "$p/exe" && exit 0; done; exit 1"#;
            let mut find = Command::new("sh");
            find.args(["-c", script, "sh", &app.to_string()]);
            let out = host(find).output().ok().filter(|o| o.status.success())?;
            let path = String::from_utf8_lossy(&out.stdout).trim().to_string();
            path.starts_with('/').then(|| PathBuf::from(path))
        })
        .clone()
}

/// Ends everything the game started when the launcher ends. Flatpak then interrupts the game's own process group
/// only, and Wine's processes each have one of their own: they went on running, unseen, after the launcher closed.
/// All of them descend from the reaper, which is what it's for.
const ENDS_TOGETHER: &str = r#""$@" & r=$!
tree() { for c in $(pgrep -P "$1"); do echo "$c"; tree "$c"; done; }
end() { trap '' INT TERM HUP; kill $(tree $r) 2> /dev/null; sleep 2; kill -KILL $(tree $r) $r 2> /dev/null; exit 130; }
trap end INT TERM HUP
wait $r"#;

/// `sh`'s arguments for a game under the reaper, with the reaper's own as Steam gives them: the game's command follows.
fn tracked_args(reaper: &Path, app: u32) -> [OsString; 7] {
    ["-c".into(), ENDS_TOGETHER.into(), "sh".into(), reaper.into(), "SteamLaunch".into(), format!("AppId={app}").into(), "--".into()]
}

/// `flatpak-spawn`'s arguments for a program on the host. Its environment is
/// the host's, with only what the launcher set on top.
fn spawn_args<'a>(program: &OsStr, args: impl Iterator<Item = &'a OsStr>, envs: impl Iterator<Item = (&'a OsStr, &'a OsStr)>, dir: Option<&Path>, watched: bool) -> Vec<OsString> {
    let mut out: Vec<OsString> = if watched { vec!["--watch-bus".into(), "--host".into()] } else { vec!["--host".into()] };
    if let Some(dir) = dir {
        let mut flag = OsString::from("--directory=");
        flag.push(dir);
        out.push(flag);
    }
    for (name, value) in envs {
        let mut flag = OsString::from("--env=");
        flag.push(name);
        flag.push("=");
        flag.push(value);
        out.push(flag);
    }
    // What follows the program is its own, even if it starts with "--".
    out.push(program.into());
    out.extend(args.map(OsStr::to_os_string));
    out
}

/// Why a program can't start on the host, as starting it would say outside
/// a Flatpak: flatpak-spawn itself always starts, so it's asked first. Fine
/// outside a Flatpak, where starting it says.
pub fn check(program: &Path) -> std::io::Result<()> {
    if id().is_none() {
        return Ok(());
    }
    // A path must be a program; a name, one on the host's PATH.
    let mut test = Command::new("sh");
    test.args(["-c", "case \"$1\" in */*) test -e \"$1\" || exit 2; test -x \"$1\" || exit 3;; *) command -v \"$1\" > /dev/null || exit 2;; esac", "sh"]).arg(program);
    match host(test).output().map(|o| o.status.code()) {
        Ok(Some(2)) => Err(std::io::ErrorKind::NotFound.into()),
        Ok(Some(3)) => Err(std::io::ErrorKind::PermissionDenied.into()),
        _ => Ok(()),
    }
}

/// A program's full path on the host, such as `flatpak`'s, for a Steam
/// shortcut, which needs one.
pub fn which(program: &str) -> Option<PathBuf> {
    let mut find = Command::new("sh");
    find.args(["-c", "command -v \"$1\"", "sh", program]);
    let out = host(find).output().ok().filter(|o| o.status.success())?;
    let path = String::from_utf8_lossy(&out.stdout).trim().to_string();
    path.starts_with('/').then(|| PathBuf::from(path))
}

/// The data folder an AppImage of the launcher keeps, so the Flatpak shares
/// its library rather than starting an empty one in ~/.var/app. The
/// manifest gives it the home folder.
pub fn data_dir(identifier: &str) -> Option<PathBuf> {
    id()?;
    let env = |name: &str| std::env::var_os(name).map(PathBuf::from).filter(|p| p.is_absolute());
    let share = env("HOST_XDG_DATA_HOME").or_else(|| env("HOME").map(|home| home.join(".local/share")))?;
    Some(share.join(identifier))
}

/// Starts this launcher again on the host, in a new sandbox of its own, so
/// it outlives this one: `flatpak run` with the given arguments.
pub fn relaunch(args: &[&OsStr]) -> Option<Command> {
    let mut command = Command::new("flatpak");
    command.arg("run").arg(id()?).args(args);
    Some(host(command))
}

#[cfg(test)]
mod tests {
    use super::{carried, display_socket, spawn_args, steam_app, tracked_args, ENDS_TOGETHER};
    use std::ffi::{OsStr, OsString};
    use std::path::Path;

    #[test]
    fn carries_the_folder_and_environment_over_and_keeps_the_programs_arguments() {
        let args = [OsStr::new("run"), OsStr::new("--windowed")];
        let envs = [(OsStr::new("DXVK_HUD"), OsStr::new("fps,gpu load"))];
        let out = spawn_args(OsStr::new("/home/deck/Games/G/game"), args.into_iter(), envs.into_iter(), Some(Path::new("/home/deck/Games/G")), false);
        assert_eq!(out, ["--host", "--directory=/home/deck/Games/G", "--env=DXVK_HUD=fps,gpu load", "/home/deck/Games/G/game", "run", "--windowed"]);
    }

    #[test]
    fn a_program_alone() {
        let out = spawn_args(OsStr::new("xdg-open"), [OsStr::new("steam://open/keyboard")].into_iter(), std::iter::empty(), None, false);
        assert_eq!(out, ["--host", "xdg-open", "steam://open/keyboard"]);
    }

    #[test]
    fn a_game_in_game_mode_starts_under_steams_reaper_and_ends_with_the_launcher() {
        let reaper = Path::new("/home/deck/.local/share/Steam/steamrt64/reaper");
        let args: Vec<OsString> = tracked_args(reaper, 2587746966).into_iter().chain([OsString::from("/home/deck/Games/G/game")]).collect();
        let out = spawn_args(OsStr::new("sh"), args.iter().map(OsString::as_os_str), std::iter::empty(), None, true);
        assert_eq!(out, ["--watch-bus", "--host", "sh", "-c", ENDS_TOGETHER, "sh", "/home/deck/.local/share/Steam/steamrt64/reaper", "SteamLaunch", "AppId=2587746966", "--", "/home/deck/Games/G/game"]);
    }

    #[test]
    fn carries_what_steam_set_and_none_of_the_sandboxs_paths() {
        let vars = [
            ("SteamAppId", "2587746966"),
            ("SteamDeck", "1"),
            ("STEAM_COMPAT_CLIENT_INSTALL_PATH", "/home/deck/.local/share/Steam"),
            ("GAMESCOPE_WAYLAND_DISPLAY", "gamescope-0"),
            ("SDL_GAMECONTROLLER_IGNORE_DEVICES", "0x28de/0x1205"),
            ("ENABLE_GAMESCOPE_WSI", "1"),
            ("PATH", "/app/bin:/usr/bin"),
            ("XDG_DATA_DIRS", "/app/share:/usr/share"),
            ("DISPLAY", ":99.0"),
            ("XAUTHORITY", "/run/flatpak/Xauthority"),
            ("FLATPAK_ID", "com.quiverlauncher.QuiverLauncher"),
        ];
        let kept: Vec<String> = carried(vars.into_iter().map(|(n, v)| (n.to_string(), OsString::from(v)))).map(|(name, _)| name).collect();
        assert_eq!(kept, ["SteamAppId", "SteamDeck", "STEAM_COMPAT_CLIENT_INSTALL_PATH", "GAMESCOPE_WAYLAND_DISPLAY", "SDL_GAMECONTROLLER_IGNORE_DEVICES", "ENABLE_GAMESCOPE_WSI"]);
    }

    #[test]
    fn a_local_displays_socket() {
        assert_eq!(display_socket(":1"), Some("/tmp/.X11-unix/X1".into()));
        assert_eq!(display_socket(":99.0"), Some("/tmp/.X11-unix/X99".into()));
        assert_eq!(display_socket("tv:0"), None);
        assert_eq!(display_socket(":"), None);
    }

    #[test]
    fn the_steam_app_the_launcher_runs_as() {
        let with = |vars: &'static [(&'static str, &'static str)]| steam_app(move |name| vars.iter().find(|(n, _)| *n == name).map(|(_, v)| v.to_string()));
        // A non-Steam game, as the launcher is: its id is the upper half.
        assert_eq!(with(&[("SteamGameId", "11114288589326778368"), ("SteamAppId", "0")]), Some(2587746966));
        assert_eq!(with(&[("SteamGameId", "620")]), Some(620));
        assert_eq!(with(&[("SteamGameId", "0"), ("SteamAppId", "2587746966")]), Some(2587746966));
        assert_eq!(with(&[]), None);
    }
}
