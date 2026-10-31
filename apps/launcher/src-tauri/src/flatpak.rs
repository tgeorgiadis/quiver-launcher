//! Releases that ship as a Flatpak bundle (`.flatpak`, or one in a zip), as
//! Bazzite and Steam Deck players often get them. The bundle is installed
//! for the player with `flatpak install --user --bundle`, its app id is kept
//! in the app's folder, and Play starts it with `flatpak run`.
use std::path::{Path, PathBuf};
use std::process::Command;

/// Holds the installed Flatpak's app id, in the app's folder.
pub const FILE: &str = ".quiver-flatpak";
const MISSING: &str = "This app comes as a Flatpak, and Flatpak isn't installed on this computer. Install Flatpak, then try again.";

/// A Flatpak bundle in an installed app's folder (or a folder inside it).
pub fn bundle_in(dir: &Path) -> Option<PathBuf> {
    let found = |dir: &Path| {
        std::fs::read_dir(dir)
            .ok()?
            .flatten()
            .map(|e| e.path())
            .find(|p| p.is_file() && p.extension().is_some_and(|e| e.eq_ignore_ascii_case("flatpak")))
    };
    found(dir).or_else(|| std::fs::read_dir(dir).ok()?.flatten().map(|e| e.path()).filter(|p| p.is_dir()).find_map(|d| found(&d)))
}

/// The app id a bundle installs (`com.example.Game`), read from its ref
/// (`app/com.example.Game/x86_64/stable`), which bundles keep uncompressed.
pub fn app_id(bundle: &[u8]) -> Option<String> {
    let mut at = 0;
    while let Some(found) = bundle[at..].windows(4).position(|w| w == b"app/") {
        let start = at + found + 4;
        let len = bundle[start..].iter().take_while(|&&b| id_char(b)).count();
        let id = &bundle[start..start + len];
        let arch = &bundle[(start + len).min(bundle.len())..];
        // A ref goes on with its architecture; an id has at least two dots.
        if id.iter().filter(|&&b| b == b'.').count() >= 2 && arch.first() == Some(&b'/') && (arch[1..].starts_with(b"x86_64/") || arch[1..].starts_with(b"aarch64/")) {
            return String::from_utf8(id.to_vec()).ok();
        }
        at = start;
    }
    None
}

fn id_char(b: u8) -> bool {
    b.is_ascii_alphanumeric() || b == b'_' || b == b'-' || b == b'.'
}

/// Whether it reads as an app id (`com.example.Game`), and so never as one of
/// flatpak's options (`--all`).
fn is_app_id(id: &str) -> bool {
    id.split('.').count() >= 3 && id.split('.').all(|part| part.bytes().next().is_some_and(|b| b.is_ascii_alphabetic() || b == b'_') && part.bytes().all(id_char))
}

fn flatpak() -> Command {
    crate::host::command("flatpak")
}

/// Runs it, on the host when the launcher is a Flatpak itself (sandbox.rs).
fn run(command: Command) -> Result<String, String> {
    let out = crate::sandbox::host(command).output().map_err(|e| if e.kind() == std::io::ErrorKind::NotFound { MISSING.to_string() } else { e.to_string() })?;
    if out.status.success() {
        Ok(String::from_utf8_lossy(&out.stdout).into())
    } else {
        let error = String::from_utf8_lossy(&out.stderr);
        Err(format!("Flatpak couldn't install it: {}", error.trim().lines().last().unwrap_or("it stopped with an error")))
    }
}

/// Installs a bundle for this player; returns its app id.
pub fn install(bundle: &Path) -> Result<String, String> {
    let id = std::fs::read(bundle).ok().and_then(|bytes| app_id(&bytes)).ok_or("This Flatpak bundle can't be read.")?;
    // Its runtime comes from the remote the bundle names, such as Flathub.
    let mut install = flatpak();
    install.args(["install", "--user", "--noninteractive", "-y", "--bundle"]).arg(bundle);
    run(install)?;
    Ok(id)
}

/// Installs a newer bundle of the launcher's own Flatpak over this one, in
/// the installation it's in. The running copy carries on until it closes.
pub fn update(bundle: &Path) -> Result<(), String> {
    let mut install = flatpak();
    install.args(["install", crate::sandbox::installation(), "--noninteractive", "-y", "--bundle"]).arg(bundle);
    run(install).map(|_| ()).map_err(|e| e.replace("couldn't install it", "couldn't install the update"))
}

/// Removes an installed Flatpak; one already gone is fine.
pub fn uninstall(id: &str) {
    let mut uninstall = flatpak();
    uninstall.args(["uninstall", "--user", "--noninteractive", "-y", id]);
    let _ = crate::sandbox::host(uninstall).output();
}

/// The installed Flatpak's app id for an app's folder, if it's one.
pub fn installed(dir: &Path) -> Option<String> {
    std::fs::read_to_string(dir.join(FILE)).ok().map(|s| s.trim().to_string()).filter(|s| is_app_id(s))
}

/// `flatpak`, by its full path where it can be found (Steam needs one): the
/// system's, when the launcher is a Flatpak and has none of its own.
pub fn program() -> PathBuf {
    if crate::sandbox::id().is_some() {
        return crate::sandbox::which("flatpak").unwrap_or_else(|| PathBuf::from("/usr/bin/flatpak"));
    }
    std::env::var_os("PATH")
        .and_then(|paths| std::env::split_paths(&paths).map(|p| p.join("flatpak")).find(|p| p.is_file()))
        .unwrap_or_else(|| PathBuf::from("flatpak"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_app_id_from_a_bundles_ref() {
        let bundle = b"\x00\x01metadata\x00app/x\x00ref\x00app/com.example.Game/x86_64/stable\x00more";
        assert_eq!(app_id(bundle).as_deref(), Some("com.example.Game"));
        assert_eq!(app_id(b"runtime/org.freedesktop.Platform/x86_64/23.08").as_deref(), None);
        assert_eq!(app_id(b"app/com.example.Arm-Game/aarch64/master").as_deref(), Some("com.example.Arm-Game"));
    }

    #[test]
    fn only_an_app_id_goes_to_flatpak() {
        let dir = tempfile::tempdir().unwrap();
        for id in ["--all", "-y", "com.example", "com.example.Game --all", ".com.example.Game", "com.0example.Game", ""] {
            std::fs::write(dir.path().join(FILE), id).unwrap();
            assert_eq!(installed(dir.path()), None, "{id}");
        }
        std::fs::write(dir.path().join(FILE), "com.example.Arm-Game\n").unwrap();
        assert_eq!(installed(dir.path()).as_deref(), Some("com.example.Arm-Game"));
    }

    #[test]
    fn finds_a_bundle_in_an_apps_folder() {
        let dir = tempfile::tempdir().unwrap();
        assert!(bundle_in(dir.path()).is_none());
        std::fs::create_dir_all(dir.path().join("Game")).unwrap();
        std::fs::write(dir.path().join("Game/Game.flatpak"), "").unwrap();
        assert_eq!(bundle_in(dir.path()).unwrap(), dir.path().join("Game/Game.flatpak"));
    }
}
