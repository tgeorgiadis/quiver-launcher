//! The window's size and place, kept from one run to the next in
//! `window.json` (in the launcher's data, so a portable copy keeps its own).
//! It goes back where it was only on a screen that's still connected, and
//! a maximized window comes back maximized at its earlier size.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;
use tauri::{Monitor, PhysicalPosition, PhysicalSize, WebviewWindow};

const FILE: &str = "window.json";

#[derive(Serialize, Deserialize, Clone, Copy, Debug, PartialEq)]
pub struct Rect {
    pub x: i32,
    pub y: i32,
    pub width: u32,
    pub height: u32,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Saved {
    /// Its size and place when not maximized.
    pub rect: Rect,
    pub maximized: bool,
    /// The screen it was on, by name.
    pub monitor: Option<String>,
}

/// How much of the window must be on the screen to go back there.
const VISIBLE: (i32, i32) = (120, 60);

/// Where the saved window goes: on its own screen if that's still there and
/// shows enough of it, made to fit; otherwise nowhere (the default place).
pub fn placed(saved: &Saved, screens: &[(Option<String>, Rect)]) -> Option<Rect> {
    let (_, screen) = screens.iter().find(|(name, _)| saved.monitor.is_none() || *name == saved.monitor)?;
    let r = saved.rect;
    let width = r.width.min(screen.width);
    let height = r.height.min(screen.height);
    let overlap_x = (r.x + width as i32).min(screen.x + screen.width as i32) - r.x.max(screen.x);
    let overlap_y = (r.y + height as i32).min(screen.y + screen.height as i32) - r.y.max(screen.y);
    (overlap_x >= VISIBLE.0 && overlap_y >= VISIBLE.1).then_some(Rect { x: r.x, y: r.y, width, height })
}

fn screens(window: &WebviewWindow) -> Vec<(Option<String>, Rect)> {
    let rect = |m: &Monitor| Rect { x: m.position().x, y: m.position().y, width: m.size().width, height: m.size().height };
    window.available_monitors().unwrap_or_default().iter().map(|m| (m.name().cloned(), rect(m))).collect()
}

fn read(data: &Path) -> Option<Saved> {
    serde_json::from_slice(&std::fs::read(data.join(FILE)).ok()?).ok()
}

/// Whether a window `size` big fills `screen`: there it would run under a taskbar or panel (as the first window, at
/// its 1280×800, did on a Steam Deck's 1280×800 screen, hiding the bottom of the page), so it's maximized instead,
/// which the desktop fits beside them.
fn fills(size: (u32, u32), screen: Rect) -> bool {
    size.0 >= screen.width && size.1 >= screen.height
}

/// Puts a new window back where it was last time. True when it's to be maximized, which the caller does again once
/// it shows: a hidden window's can go unheard (GTK's on Wayland).
pub fn restore(window: &WebviewWindow, data: &Path) -> bool {
    let saved = read(data);
    let screens = screens(window);
    let place = saved.as_ref().and_then(|saved| placed(saved, &screens));
    if let Some(r) = place {
        let _ = window.set_size(PhysicalSize::new(r.width, r.height));
        let _ = window.set_position(PhysicalPosition::new(r.x, r.y));
    }
    // Its screen: the one it was on, else the first (a new window's place, where Wayland has no primary screen to ask for).
    let screen = screens.iter().find(|(name, _)| saved.as_ref().is_some_and(|s| s.monitor.is_some() && *name == s.monitor)).or(screens.first()).map(|(_, r)| *r);
    let size = place.map(|r| (r.width, r.height)).or_else(|| window.inner_size().ok().map(|s| (s.width, s.height)));
    let maximize = size.zip(screen).is_some_and(|(size, screen)| fills(size, screen)) || saved.is_some_and(|saved| saved.maximized);
    #[cfg(desktop)]
    if maximize {
        let _ = window.maximize();
    }
    maximize
}

/// Saves the window's size and place as it changes, a moment after it stops.
pub fn track(window: &WebviewWindow, data: PathBuf) {
    let last = Arc::new(Mutex::new(read(&data)));
    let changes = Arc::new(AtomicU64::new(0));
    let w = window.clone();
    window.on_window_event(move |event| {
        if !matches!(event, tauri::WindowEvent::Moved(_) | tauri::WindowEvent::Resized(_)) {
            return;
        }
        // Full screen is the player's setting, not a size to keep; minimized has none.
        if w.is_fullscreen().unwrap_or(false) || w.is_minimized().unwrap_or(false) {
            return;
        }
        let maximized = w.is_maximized().unwrap_or(false);
        let (Ok(position), Ok(size)) = (w.outer_position(), w.inner_size()) else { return };
        let mut saved = last.lock().unwrap();
        // A maximized window keeps the size it had before.
        let rect = match (&*saved, maximized) {
            (Some(before), true) => before.rect,
            _ => Rect { x: position.x, y: position.y, width: size.width, height: size.height },
        };
        let monitor = w.current_monitor().ok().flatten().and_then(|m| m.name().cloned());
        *saved = Some(Saved { rect, maximized, monitor });
        let change = changes.fetch_add(1, Ordering::SeqCst) + 1;
        let (last, changes, data) = (last.clone(), changes.clone(), data.clone());
        std::thread::spawn(move || {
            std::thread::sleep(Duration::from_millis(400));
            if changes.load(Ordering::SeqCst) != change {
                return;
            }
            if let Some(saved) = &*last.lock().unwrap() {
                let _ = std::fs::create_dir_all(&data);
                let temp = data.join(format!("{FILE}.tmp"));
                let _ = serde_json::to_vec_pretty(saved)
                    .map_err(std::io::Error::other)
                    .and_then(|bytes| std::fs::write(&temp, bytes))
                    .and_then(|_| std::fs::rename(&temp, data.join(FILE)));
            }
        });
    });
}

#[cfg(test)]
mod tests {
    use super::*;

    const SCREEN: Rect = Rect { x: 0, y: 0, width: 1920, height: 1080 };
    const SIDE: Rect = Rect { x: 1920, y: 0, width: 1280, height: 1024 };

    fn saved(x: i32, y: i32, width: u32, height: u32, monitor: &str) -> Saved {
        Saved { rect: Rect { x, y, width, height }, maximized: false, monitor: Some(monitor.into()) }
    }

    #[test]
    fn goes_back_to_its_screen_while_its_connected() {
        let screens = [(Some("DP-1".to_string()), SCREEN), (Some("HDMI-1".to_string()), SIDE)];
        assert_eq!(placed(&saved(2000, 100, 1000, 700, "HDMI-1"), &screens), Some(Rect { x: 2000, y: 100, width: 1000, height: 700 }));
        // Its screen is gone: the default place instead.
        assert_eq!(placed(&saved(2000, 100, 1000, 700, "HDMI-1"), &screens[..1]), None);
    }

    #[test]
    fn a_window_as_big_as_its_screen_is_maximized_instead() {
        let deck = Rect { x: 0, y: 0, width: 1280, height: 800 };
        assert!(fills((1280, 800), deck));
        assert!(fills((1920, 1080), SCREEN));
        assert!(!fills((1280, 800), Rect { x: 0, y: 0, width: 1280, height: 1024 }), "as wide, not as tall");
        assert!(!fills((1280, 800), SCREEN));
        assert!(!fills((1000, 700), deck));
    }

    #[test]
    fn fits_the_screen_and_never_goes_off_it() {
        let screens = [(Some("DP-1".to_string()), SCREEN)];
        assert_eq!(placed(&saved(0, 0, 4000, 3000, "DP-1"), &screens), Some(Rect { x: 0, y: 0, width: 1920, height: 1080 }));
        assert_eq!(placed(&saved(1900, 1050, 800, 600, "DP-1"), &screens), None);
        assert_eq!(placed(&saved(-600, 40, 800, 600, "DP-1"), &screens).map(|r| r.x), Some(-600));
    }
}
