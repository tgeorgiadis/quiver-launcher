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
    if id().is_none() {
        return command;
    }
    let envs = command.get_envs().filter_map(|(name, value)| Some((name, value?)));
    // Otherwise it starts in the launcher's folder, which may be the sandbox's own.
    let home = std::env::var_os("HOME").map(PathBuf::from);
    let dir = command.get_current_dir().or(home.as_deref());
    let mut spawn = Command::new("flatpak-spawn");
    spawn.args(spawn_args(command.get_program(), command.get_args(), envs, dir));
    spawn
}

/// `flatpak-spawn`'s arguments for a program on the host. Its environment is
/// the host's, with only what the launcher set on top.
fn spawn_args<'a>(program: &OsStr, args: impl Iterator<Item = &'a OsStr>, envs: impl Iterator<Item = (&'a OsStr, &'a OsStr)>, dir: Option<&Path>) -> Vec<OsString> {
    let mut out: Vec<OsString> = vec!["--host".into()];
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

/// The flatpak option for the installation the launcher's Flatpak is in:
/// `--system` for one in /var/lib/flatpak, as GNOME Software may install a
/// bundle, else `--user`. Flatpak notes where the app is in /.flatpak-info.
pub fn installation() -> &'static str {
    let info = std::fs::read_to_string("/.flatpak-info").unwrap_or_default();
    installation_of(&info)
}

fn installation_of(info: &str) -> &'static str {
    let app = info.lines().find_map(|line| line.strip_prefix("app-path=")).unwrap_or_default();
    if app.starts_with("/var/lib/flatpak/") {
        "--system"
    } else {
        "--user"
    }
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
    use super::{installation_of, spawn_args};
    use std::ffi::OsStr;
    use std::path::Path;

    #[test]
    fn updates_the_installation_the_flatpak_is_in() {
        let info = |path: &str| format!("[Application]\nname=com.quiverlauncher.QuiverLauncher\n\n[Instance]\napp-path={path}\nbranch=stable\n");
        assert_eq!(installation_of(&info("/var/lib/flatpak/app/com.quiverlauncher.QuiverLauncher/x86_64/stable/abc/files")), "--system");
        assert_eq!(installation_of(&info("/home/deck/.local/share/flatpak/app/com.quiverlauncher.QuiverLauncher/x86_64/stable/abc/files")), "--user");
        assert_eq!(installation_of(""), "--user");
    }

    #[test]
    fn carries_the_folder_and_environment_over_and_keeps_the_programs_arguments() {
        let args = [OsStr::new("run"), OsStr::new("--windowed")];
        let envs = [(OsStr::new("DXVK_HUD"), OsStr::new("fps,gpu load"))];
        let out = spawn_args(OsStr::new("/home/deck/Games/G/game"), args.into_iter(), envs.into_iter(), Some(Path::new("/home/deck/Games/G")));
        assert_eq!(out, ["--host", "--directory=/home/deck/Games/G", "--env=DXVK_HUD=fps,gpu load", "/home/deck/Games/G/game", "run", "--windowed"]);
    }

    #[test]
    fn a_program_alone() {
        let out = spawn_args(OsStr::new("xdg-open"), [OsStr::new("steam://open/keyboard")].into_iter(), std::iter::empty(), None);
        assert_eq!(out, ["--host", "xdg-open", "steam://open/keyboard"]);
    }
}
