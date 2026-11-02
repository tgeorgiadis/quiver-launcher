//! The launcher in the notification area, on Windows, as Quiver Launcher 3 had
//! it: with "Close to tray" on, closing the window hides it there, so the
//! launcher keeps running and updating apps; its icon opens it again or quits.
//! Started with Windows, it starts there too (`--tray`), its window unseen
//! until the player opens it. Starting the launcher again, from its shortcut
//! or the Start menu, opens the one already running (lib.rs).
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};
use tauri::{AppHandle, Manager, WebviewWindow, WindowEvent};

/// The argument Windows starts the launcher with: in the tray, not on screen.
pub const ARG: &str = "--tray";
const ID: &str = "main";
/// Where Windows keeps the programs it starts as the player signs in.
const RUN_KEY: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
const RUN_VALUE: &str = "Quiver Launcher";

#[derive(Default)]
pub struct Tray {
    /// Closing the window hides it in the tray (the "closeToTray" setting).
    close_to_tray: AtomicBool,
    /// Started in the tray: its icon stays for this run, the one way back to the window.
    started_there: AtomicBool,
    /// The menu's words, in the player's language once the page says them (set).
    labels: Mutex<Labels>,
    /// What the window does as it first shows, after starting in the tray: done unseen, either would show it.
    on_show: Mutex<Option<OnShow>>,
}

struct Labels {
    open: String,
    quit: String,
}

impl Default for Labels {
    fn default() -> Self {
        Self { open: "Open Quiver Launcher".into(), quit: "Quit".into() }
    }
}

/// The window's place as the player left it: maximized, or full screen (with the title bar setting it needs).
pub struct OnShow {
    pub maximize: bool,
    pub fullscreen: Option<bool>,
}

impl Tray {
    pub fn new(close_to_tray: bool, started_there: bool) -> Self {
        Self { close_to_tray: close_to_tray.into(), started_there: started_there.into(), ..Default::default() }
    }

    pub fn wait_to_show(&self, on_show: OnShow) {
        *self.on_show.lock().unwrap() = Some(on_show);
    }

    fn wanted(&self) -> bool {
        self.close_to_tray.load(Ordering::Relaxed) || self.started_there.load(Ordering::Relaxed)
    }
}

/// The tray's icon, made or taken away as it's wanted.
pub fn refresh(app: &AppHandle) -> tauri::Result<()> {
    let tray = app.state::<Tray>();
    if !tray.wanted() {
        app.remove_tray_by_id(ID);
        return Ok(());
    }
    let labels = tray.labels.lock().unwrap();
    let open = MenuItem::with_id(app, "open", &labels.open, true, None::<&str>)?;
    let quit = MenuItem::with_id(app, "quit", &labels.quit, true, None::<&str>)?;
    let menu = Menu::with_items(app, &[&open, &quit])?;
    if let Some(icon) = app.tray_by_id(ID) {
        return icon.set_menu(Some(menu));
    }
    let mut builder = TrayIconBuilder::with_id(ID)
        .tooltip("Quiver Launcher")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| match event.id.as_ref() {
            "open" => open_window(app),
            "quit" => app.exit(0),
            _ => {}
        })
        .on_tray_icon_event(|icon, event| {
            if let TrayIconEvent::Click { button: MouseButton::Left, button_state: MouseButtonState::Up, .. } = event {
                open_window(icon.app_handle());
            }
        });
    if let Some(image) = app.default_window_icon() {
        builder = builder.icon(image.clone());
    }
    builder.build(app)?;
    Ok(())
}

/// The window, on screen and in front, as the player left it.
pub fn open_window(app: &AppHandle) {
    let Some(window) = app.get_webview_window("main") else { return };
    let _ = window.show();
    let _ = window.unminimize();
    if let Some(OnShow { maximize, fullscreen }) = app.state::<Tray>().on_show.lock().unwrap().take() {
        if let Some(title_bar) = fullscreen {
            let _ = crate::fullscreen(&window, true, title_bar);
        } else if maximize {
            let _ = window.maximize();
        }
    }
    let _ = window.set_focus();
}

/// Closing the window hides it in the tray while Close to tray is on. Shutting Windows down still ends the launcher.
pub fn watch(window: &WebviewWindow) {
    let w = window.clone();
    window.on_window_event(move |event| {
        if let WindowEvent::CloseRequested { api, .. } = event {
            if w.state::<Tray>().close_to_tray.load(Ordering::Relaxed) {
                api.prevent_close();
                let _ = w.hide();
            }
        }
    });
}

/// Close to tray turned on or off, and the menu's words in the player's language.
pub fn set(app: &AppHandle, close_to_tray: bool, open: String, quit: String) -> tauri::Result<()> {
    let tray = app.state::<Tray>();
    tray.close_to_tray.store(close_to_tray, Ordering::Relaxed);
    *tray.labels.lock().unwrap() = Labels { open, quit };
    refresh(app)
}

/// This copy of the launcher, in quotes, starting in the tray: what Windows runs as the player signs in.
fn command() -> Result<String, String> {
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    Ok(format!("\"{}\" {ARG}", exe.display()))
}

/// Whether Windows starts this copy of the launcher as the player signs in (another copy's entry doesn't count).
pub fn starts_with_windows() -> bool {
    let run = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER).open_subkey(RUN_KEY);
    let value = run.and_then(|key| key.get_value::<String, _>(RUN_VALUE));
    matches!((value, command()), (Ok(value), Ok(command)) if value.eq_ignore_ascii_case(&command))
}

pub fn set_start_with_windows(on: bool) -> Result<(), String> {
    let (run, _) = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER).create_subkey(RUN_KEY).map_err(|e| e.to_string())?;
    if on {
        return run.set_value(RUN_VALUE, &command()?).map_err(|e| e.to_string());
    }
    // Only this copy's: turning it off in a portable copy leaves the installed one's.
    if starts_with_windows() {
        run.delete_value(RUN_VALUE).map_err(|e| e.to_string())?;
    }
    Ok(())
}
