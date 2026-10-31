//! Programs outside the launcher, started with the system's environment. An
//! AppImage runs the launcher with its own libraries first: LD_LIBRARY_PATH,
//! PATH and GTK's and GIO's paths point into the AppImage, for the launcher
//! and WebKit's helpers. The system's programs mustn't load those: on a Steam
//! Deck, Flatpak stops at a symbol the AppImage's older GLib lacks, so a page
//! never reached the browser (a Flatpak), a Flatpak app wouldn't install, and
//! an app started from Quiver ran with the wrong libraries.
use std::ffi::OsStr;
use std::process::{Command, Stdio};

/// What the AppImage set, to take out for a program of the system's: each a
/// variable and its value as the system had it, or None to remove it. None
/// outside an AppImage.
pub fn changes() -> Vec<(String, Option<String>)> {
    match std::env::var("APPDIR") {
        Ok(appdir) if !appdir.trim_end_matches('/').is_empty() => restored(std::env::vars(), &appdir),
        _ => Vec::new(),
    }
}

fn restored(vars: impl Iterator<Item = (String, String)>, appdir: &str) -> Vec<(String, Option<String>)> {
    let appdir = appdir.trim_end_matches('/');
    let inside = |entry: &str| entry == appdir || entry.starts_with(&format!("{appdir}/"));
    vars.filter_map(|(name, value)| {
        // The AppImage's own, which a program it starts would take as about itself.
        if ["APPDIR", "APPIMAGE", "ARGV0", "OWD"].contains(&name.as_str()) {
            return Some((name, None));
        }
        if !value.contains(appdir) {
            return None;
        }
        // A list of paths, the AppImage's put in front: the rest, as the system had them. A path that's
        // there again later was put in front with them (XDG_DATA_DIRS gets "/usr/share", ahead of the
        // Flatpak apps', whose Firefox SteamOS's stand-in in /usr/share would otherwise beat).
        let entries: Vec<&str> = value.split(':').filter(|entry| !entry.is_empty() && !inside(entry)).collect();
        let rest: Vec<&str> = entries.iter().enumerate().filter(|&(i, entry)| !entries[i + 1..].contains(entry)).map(|(_, entry)| *entry).collect();
        Some((name, (!rest.is_empty()).then(|| rest.join(":"))))
    })
    .collect()
}

/// A program of the system's, to start with the system's environment.
#[cfg_attr(target_os = "android", allow(dead_code))]
pub fn command(program: impl AsRef<OsStr>) -> Command {
    let mut command = Command::new(program);
    for (name, value) in changes() {
        match value {
            Some(value) => command.env(name, value),
            None => command.env_remove(name),
        };
    }
    command
}

/// Opens a file, folder or address with what the system opens it with, as
/// `open::that_detached` does, with the system's environment. `activation`
/// is Wayland's leave for what opens it to come to the front. From a Flatpak,
/// the system's opener on the host (sandbox.rs), as an AppImage would have it:
/// the portal would ask which app opens a steam:// address, and pass no leave.
pub fn open(target: impl AsRef<OsStr>, activation: Option<String>) -> std::io::Result<()> {
    let ours = std::env::var_os("APPDIR").is_some() || activation.is_some() || crate::sandbox::id().is_some();
    if !cfg!(target_os = "linux") || !ours {
        return open::that_detached(target);
    }
    let mut error = None;
    for mut opener in open::commands(target) {
        for (name, value) in changes() {
            match value {
                Some(value) => opener.env(name, value),
                None => opener.env_remove(name),
            };
        }
        if let Some(token) = &activation {
            opener.env("XDG_ACTIVATION_TOKEN", token);
        }
        let mut opener = crate::sandbox::host(opener);
        opener.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
        #[cfg(unix)]
        std::os::unix::process::CommandExt::process_group(&mut opener, 0);
        match opener.spawn() {
            Ok(mut child) => {
                // xdg-open hands it on and ends; waited for, so it doesn't linger.
                std::thread::spawn(move || child.wait());
                return Ok(());
            }
            Err(e) => error = Some(e),
        }
    }
    Err(error.unwrap_or_else(|| std::io::Error::other("nothing to open it with")))
}

#[cfg(test)]
mod tests {
    use super::restored;

    #[test]
    fn takes_the_appimages_paths_out_and_leaves_the_rest() {
        let vars = [
            ("LD_LIBRARY_PATH", "/tmp/.mount_Q/usr/lib/:/tmp/.mount_Q/lib/x86_64-linux-gnu/"),
            ("PATH", "/tmp/.mount_Q/usr/bin/:/usr/local/bin:/usr/bin"),
            ("XDG_DATA_DIRS", "/tmp/.mount_Q/usr/share:/usr/share:/var/lib/flatpak/exports/share:/usr/share"),
            ("GDK_PIXBUF_MODULE_FILE", "/tmp/.mount_Q//usr/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"),
            ("APPDIR", "/tmp/.mount_Q"),
            ("HOME", "/home/deck"),
            // Another folder whose name starts the same.
            ("SOMEWHERE", "/tmp/.mount_Quiver/x"),
        ]
        .map(|(n, v)| (n.to_string(), v.to_string()));
        let mut changes = restored(vars.into_iter(), "/tmp/.mount_Q/");
        changes.sort();
        assert_eq!(
            changes,
            vec![
                ("APPDIR".into(), None),
                ("GDK_PIXBUF_MODULE_FILE".into(), None),
                ("LD_LIBRARY_PATH".into(), None),
                ("PATH".into(), Some("/usr/local/bin:/usr/bin".into())),
                ("SOMEWHERE".into(), Some("/tmp/.mount_Quiver/x".into())),
                ("XDG_DATA_DIRS".into(), Some("/var/lib/flatpak/exports/share:/usr/share".into())),
            ]
        );
    }
}
