//! Steam's on-screen keyboard, for a Steam Deck or another Steam session
//! played with a controller. The webview's text fields don't ask Steam for
//! it themselves, so the launcher does while one is being edited.
use std::process::Stdio;
use std::sync::mpsc::{channel, Sender};
use std::sync::{Mutex, OnceLock};

/// Steam started the launcher, or this is a Steam Deck or Gamescope session.
pub fn offered(var: impl Fn(&str) -> Option<String>) -> bool {
    crate::steam::started_us(&var) || var("SteamDeck").is_some_and(|v| !v.trim().is_empty()) || game_mode(var)
}

/// Steam's Game Mode, or another Gamescope session: only what Steam starts is shown.
pub fn game_mode(var: impl Fn(&str) -> Option<String>) -> bool {
    let gamescope = |name: &str| var(name).is_some_and(|v| v.trim().eq_ignore_ascii_case("gamescope"));
    var("GAMESCOPE_WAYLAND_DISPLAY").is_some_and(|v| !v.trim().is_empty())
        || ["XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP"].into_iter().any(gamescope)
}

/// Asks Steam to show or hide its keyboard; false where there's none to ask for.
pub fn request(open: bool) -> bool {
    if !cfg!(target_os = "linux") || !offered(|name| std::env::var(name).ok()) {
        return false;
    }
    let uri = if open { "steam://open/keyboard" } else { "steam://close/keyboard" };
    // One at a time, in the order asked, by one thread: each on its own, a press's open could reach Steam after
    // the close that followed it (xdg-open took seconds in Game Mode), and the keyboard came and went at random.
    static ASKS: OnceLock<Mutex<Sender<&'static str>>> = OnceLock::new();
    let asks = ASKS.get_or_init(|| {
        let (tx, rx) = channel::<&'static str>();
        std::thread::spawn(move || {
            while let Ok(mut uri) = rx.recv() {
                // Only the last of what's waited meanwhile matters.
                while let Ok(next) = rx.try_recv() {
                    uri = next;
                }
                send(uri);
            }
        });
        Mutex::new(tx)
    });
    asks.lock().is_ok_and(|tx| tx.send(uri).is_ok())
}

fn send(uri: &str) {
    if to_pipe(uri) {
        return;
    }
    // The system's, from a Flatpak too: its portal would ask which app opens steam://.
    let quiet = |program: &str| {
        let mut command = crate::host::command(program);
        command.arg(uri);
        let mut command = crate::sandbox::host(command);
        command.stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::null());
        command
    };
    // xdg-open hands it to Steam; where it can't, Steam takes it directly.
    if !quiet("xdg-open").status().is_ok_and(|s| s.success()) {
        let _ = quiet("steam").spawn();
    }
}

/// Straight to the Steam that's running, on the pipe its `steam` command hands links over on (as
/// steam-runtime-steam-remote writes them): at once, where xdg-open under a game's environment in Game Mode
/// took seconds. False when no Steam is reading one (Steam's Flatpak has its own).
#[cfg(target_os = "linux")]
pub fn to_pipe(uri: &str) -> bool {
    use std::io::Write;
    use std::os::unix::fs::{FileTypeExt, OpenOptionsExt};
    let Some(home) = std::env::var_os("HOME").map(std::path::PathBuf::from) else { return false };
    [".steam/steam.pipe", ".var/app/com.valvesoftware.Steam/.steam/steam.pipe"].into_iter().any(|path| {
        let path = home.join(path);
        if !std::fs::metadata(&path).is_ok_and(|m| m.file_type().is_fifo()) {
            return false;
        }
        // Without waiting: with nothing reading it, opening it fails rather than blocks.
        std::fs::OpenOptions::new()
            .write(true)
            .custom_flags(libc::O_NONBLOCK)
            .open(&path)
            .and_then(|mut pipe| pipe.write_all(pipe_line(uri).as_bytes()))
            .is_ok()
    })
}

#[cfg(not(target_os = "linux"))]
pub fn to_pipe(_: &str) -> bool {
    false
}

/// A command line as Steam reads one from its pipe: the steam command and its arguments, each in single quotes.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn pipe_line(uri: &str) -> String {
    format!("'~/.steam/root/ubuntu12_32/steam' '{}'\n", uri.replace('\'', r"'\''"))
}

/// Steam's keyboard over a full-screen Quiver on KDE (a Steam Deck's desktop), where KWin otherwise keeps the
/// full-screen window above it and the keyboard opens unseen: a script of KWin's own (keyboard-kwin.js) tells
/// Quiver when the keyboard shows and goes, and Quiver steps out of full screen while it shows. Loaded as the
/// launcher starts, unloaded as it quits.
#[cfg(target_os = "linux")]
pub mod kwin {
    use gtk::gio;
    use gtk::glib::{ToVariant, Variant, VariantTy};
    use std::sync::atomic::{AtomicBool, Ordering};
    use tauri::Manager;

    const NAME: &str = "quiver-launcher-keyboard";
    /// Where the script tells Quiver: a name a Flatpak may own (under its app id) without asking.
    const SERVICE: &str = "com.quiverlauncher.QuiverLauncher.Keyboard";
    const INTERFACE: &str = "com.quiverlauncher.Keyboard";
    const XML: &str = r#"<node><interface name="com.quiverlauncher.Keyboard"><method name="Shown"><arg name="shown" type="b" direction="in"/></method></interface></node>"#;
    /// Quiver stepped out of full screen for the keyboard, so goes back once it's gone.
    static STEPPED: AtomicBool = AtomicBool::new(false);

    /// Hears from the script, on GTK's thread (the launcher's setup): Quiver steps out of full screen and back
    /// itself, as KWin's own change to a window in full screen goes unheard by a Flatpak's GTK.
    pub fn listen(app: tauri::AppHandle) {
        if !kde() {
            return;
        }
        let Ok(bus) = gio::bus_get_sync(gio::BusType::Session, None::<&gio::Cancellable>) else { return };
        let Some(interface) = gio::DBusNodeInfo::for_xml(XML).ok().and_then(|node| node.lookup_interface(INTERFACE)) else { return };
        let heard = bus.register_object(
            "/Keyboard",
            &interface,
            move |_, _, _, _, method, args, invocation| {
                if let (true, Some((shown,))) = (method == "Shown", args.get::<(bool,)>()) {
                    step(&app, shown);
                }
                invocation.return_value(None);
            },
            // It has no properties.
            |_, _, _, _, _| ().to_variant(),
            |_, _, _, _, _, _| false,
        );
        if heard.is_ok() {
            gio::bus_own_name_on_connection(&bus, SERVICE, gio::BusNameOwnerFlags::NONE, |_, _| {}, |_, _| {});
        }
    }

    fn step(app: &tauri::AppHandle, shown: bool) {
        let Some(window) = app.get_webview_window("main") else { return };
        let full = if shown {
            if !window.is_fullscreen().unwrap_or(false) {
                return;
            }
            STEPPED.store(true, Ordering::Relaxed);
            false
        } else if STEPPED.swap(false, Ordering::Relaxed) {
            true
        } else {
            return;
        };
        let _ = window.set_fullscreen(full);
        // GTK puts the change in on its next frame, and a launcher that's still draws none: it stayed in full
        // screen until the pointer moved over it. So one is asked for, once the change is made.
        let on = window.clone();
        let _ = window.run_on_main_thread(move || {
            if let Ok(gtk) = on.gtk_window() {
                gtk::prelude::WidgetExt::queue_draw(&gtk);
            }
        });
    }

    fn kde() -> bool {
        std::env::var("XDG_CURRENT_DESKTOP").is_ok_and(|d| d.split(':').any(|d| d.eq_ignore_ascii_case("KDE")))
    }

    fn call(bus: &gio::DBusConnection, path: &str, interface: &str, method: &str, args: Option<Variant>, reply: Option<&VariantTy>) -> Option<Variant> {
        bus.call_sync(Some("org.kde.KWin"), path, interface, method, args.as_ref(), reply, gio::DBusCallFlags::NONE, 2000, None::<&gio::Cancellable>).ok()
    }

    fn unload(bus: &gio::DBusConnection) {
        call(bus, "/Scripting", "org.kde.kwin.Scripting", "unloadScript", Some((NAME,).to_variant()), None);
    }

    /// Off the main thread: KWin's answers can take a moment.
    pub fn load() {
        if !kde() {
            return;
        }
        std::thread::spawn(|| {
            let Ok(bus) = gio::bus_get_sync(gio::BusType::Session, None::<&gio::Cancellable>) else { return };
            // Where KWin can read it: a Flatpak's /tmp is its own, but its cache folder is the same path on the host.
            let folder = crate::sandbox::id().and_then(|_| dirs::cache_dir()).unwrap_or_else(std::env::temp_dir);
            let file = folder.join(format!("{NAME}.js"));
            if std::fs::write(&file, include_str!("keyboard-kwin.js")).is_err() {
                return;
            }
            // One a launcher left loaded, quitting without letting go of it.
            unload(&bus);
            let loaded = call(&bus, "/Scripting", "org.kde.kwin.Scripting", "loadScript", Some((file.to_string_lossy().as_ref(), NAME).to_variant()), VariantTy::new("(i)").ok());
            let Some((id,)) = loaded.and_then(|v| v.get::<(i32,)>()).filter(|(id,)| *id >= 0) else { return };
            // Plasma 6's path for it, else Plasma 5's.
            if call(&bus, &format!("/Scripting/Script{id}"), "org.kde.kwin.Script", "run", None, None).is_none() {
                call(&bus, &format!("/{id}"), "org.kde.kwin.Script", "run", None, None);
            }
        });
    }

    pub fn unload_now() {
        if let (true, Ok(bus)) = (kde(), gio::bus_get_sync(gio::BusType::Session, None::<&gio::Cancellable>)) {
            unload(&bus);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{offered, pipe_line};

    #[test]
    fn a_link_for_steams_pipe_as_its_steam_command_writes_it() {
        assert_eq!(pipe_line("steam://open/keyboard"), "'~/.steam/root/ubuntu12_32/steam' 'steam://open/keyboard'\n");
        assert_eq!(pipe_line("it's"), "'~/.steam/root/ubuntu12_32/steam' 'it'\\''s'\n");
    }

    fn with(vars: &[(&str, &str)]) -> bool {
        offered(|name| vars.iter().find(|(n, _)| *n == name).map(|(_, v)| v.to_string()))
    }

    #[test]
    fn offered_under_steam_and_gamescope_only() {
        assert!(with(&[("SteamDeck", "1")]));
        assert!(with(&[("SteamAppId", "0")]));
        assert!(with(&[("XDG_CURRENT_DESKTOP", "gamescope")]));
        assert!(with(&[("GAMESCOPE_WAYLAND_DISPLAY", "gamescope-0")]));
        assert!(!with(&[("XDG_CURRENT_DESKTOP", "KDE")]));
        assert!(!with(&[("SteamDeck", " ")]));
        assert!(!with(&[]));
    }
}
