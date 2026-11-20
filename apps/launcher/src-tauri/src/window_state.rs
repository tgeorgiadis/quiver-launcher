//! The window's size and place, kept from one run to the next in
//! `window.json` (in the launcher's data, so a portable copy keeps its own).
//! It goes back where it was only on a screen that's still connected, and
//! a maximized window comes back maximized at its earlier size. The very
//! first window, with nothing saved yet, opens maximized. A window left where a
//! screen was turned off or unplugged is brought onto one still connected as it
//! comes in front again.
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

/// Whether `screen` shows enough of a window at `r`.
fn shows(r: Rect, screen: Rect) -> bool {
    let overlap_x = (r.x + r.width as i32).min(screen.x + screen.width as i32) - r.x.max(screen.x);
    let overlap_y = (r.y + r.height as i32).min(screen.y + screen.height as i32) - r.y.max(screen.y);
    overlap_x >= VISIBLE.0 && overlap_y >= VISIBLE.1
}

/// Where the saved window goes: on its own screen if that's still there and
/// shows enough of it, made to fit; otherwise nowhere (the default place). A
/// window saved without a screen's name may go back on any screen that shows it.
pub fn placed(saved: &Saved, screens: &[(Option<String>, Rect)]) -> Option<Rect> {
    let r = saved.rect;
    screens.iter().filter(|(name, _)| saved.monitor.is_none() || *name == saved.monitor).find_map(|(_, screen)| {
        let fitted = Rect { x: r.x, y: r.y, width: r.width.min(screen.width), height: r.height.min(screen.height) };
        shows(fitted, *screen).then_some(fitted)
    })
}

/// Where a window at `r` goes when no screen shows enough of it (its screen was
/// turned off or unplugged while it was open): the middle of `home`, made to fit.
/// None when a screen still shows it, or there's no screen to ask about.
pub fn rescued(r: Rect, screens: &[Rect], home: Option<Rect>) -> Option<Rect> {
    if screens.iter().any(|screen| shows(r, *screen)) {
        return None;
    }
    let home = home.or(screens.first().copied())?;
    let (width, height) = (r.width.min(home.width), r.height.min(home.height));
    Some(Rect { x: home.x + ((home.width - width) / 2) as i32, y: home.y + ((home.height - height) / 2) as i32, width, height })
}

fn rect_of(m: &Monitor) -> Rect {
    Rect { x: m.position().x, y: m.position().y, width: m.size().width, height: m.size().height }
}

fn screens(window: &WebviewWindow) -> Vec<(Option<String>, Rect)> {
    window.available_monitors().unwrap_or_default().iter().map(|m| (m.name().cloned(), rect_of(m))).collect()
}

/// Brings the window onto a connected screen when it's on none: the one it was
/// on may have been turned off or unplugged while the launcher was open (a TV
/// on HDMI), and some desktops leave the window where that screen was. A
/// maximized window is maximized again on its new screen. Where the desktop
/// places windows itself (Wayland), the move does nothing.
pub fn bring_on_screen(window: &WebviewWindow) {
    if window.is_fullscreen().unwrap_or(false) || window.is_minimized().unwrap_or(false) {
        return;
    }
    let (Ok(position), Ok(size)) = (window.outer_position(), window.outer_size()) else { return };
    let here = Rect { x: position.x, y: position.y, width: size.width, height: size.height };
    let screens: Vec<Rect> = screens(window).into_iter().map(|(_, r)| r).collect();
    let home = window.primary_monitor().ok().flatten().map(|m| rect_of(&m)).filter(|r| screens.contains(r));
    let Some(to) = rescued(here, &screens, home) else { return };
    #[cfg(desktop)]
    let maximized = window.is_maximized().unwrap_or(false);
    #[cfg(desktop)]
    if maximized {
        let _ = window.unmaximize();
    }
    let _ = window.set_position(PhysicalPosition::new(to.x, to.y));
    if let Ok(inner) = window.inner_size() {
        // The window's own size, less its frame, so the whole of it fits.
        let frame = (size.width.saturating_sub(inner.width), size.height.saturating_sub(inner.height));
        let _ = window.set_size(PhysicalSize::new(to.width.saturating_sub(frame.0).max(1), to.height.saturating_sub(frame.1).max(1)));
    }
    #[cfg(desktop)]
    if maximized {
        let _ = window.maximize();
    }
}

/// Brings the window back onto a screen whenever it comes in front: from the
/// taskbar or Alt+Tab, it may still be where a screen that's gone was.
pub fn keep_on_screen(window: &WebviewWindow) {
    let w = window.clone();
    window.on_window_event(move |event| {
        if matches!(event, tauri::WindowEvent::Focused(true)) {
            bring_on_screen(&w);
        }
    });
}

fn read(data: &Path) -> Option<Saved> {
    serde_json::from_slice(&std::fs::read(data.join(FILE)).ok()?).ok()
}

fn write(data: &Path, saved: &Saved) {
    let _ = std::fs::create_dir_all(data);
    let temp = data.join(format!("{FILE}.tmp"));
    let _ = serde_json::to_vec_pretty(saved)
        .map_err(std::io::Error::other)
        .and_then(|bytes| std::fs::write(&temp, bytes))
        .and_then(|_| std::fs::rename(&temp, data.join(FILE)));
}

/// Whether the window opens maximized: always the first time, when nothing is saved (a new player's screen may be
/// smaller than the window's 1280×800, squeezing the setup wizard), else as the player left it, or when its size
/// would fill its screen.
fn maximized(saved: Option<&Saved>, size: Option<(u32, u32)>, screen: Option<Rect>) -> bool {
    saved.is_none_or(|saved| saved.maximized) || size.zip(screen).is_some_and(|(size, screen)| fills(size, screen))
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
    let maximize = maximized(saved.as_ref(), size, screen);
    // The first time, keep its own size and place as the one it goes back to when the player restores it down,
    // rather than the maximized one that tracking would save.
    if saved.is_none() {
        if let (Ok(position), Ok(size)) = (window.outer_position(), window.inner_size()) {
            let rect = Rect { x: position.x, y: position.y, width: size.width, height: size.height };
            let monitor = window.current_monitor().ok().flatten().and_then(|m| m.name().cloned());
            write(data, &Saved { rect, maximized: true, monitor });
        }
    }
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
                write(&data, saved);
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

    #[test]
    fn a_window_saved_without_a_screen_name_goes_back_on_any_screen_that_shows_it() {
        let screens = [(None, SCREEN), (None, SIDE)];
        let unnamed = Saved { monitor: None, ..saved(2000, 100, 1000, 700, "") };
        assert_eq!(placed(&unnamed, &screens), Some(Rect { x: 2000, y: 100, width: 1000, height: 700 }));
        // Where no screen is now: the default place.
        assert_eq!(placed(&unnamed, &screens[..1]), None);
    }

    #[test]
    fn a_window_left_where_a_screen_was_comes_to_the_middle_of_one_still_there() {
        let tv = Rect { x: 1920, y: 0, width: 3840, height: 2160 };
        let on_tv = Rect { x: 2400, y: 300, width: 1280, height: 800 };
        assert_eq!(rescued(on_tv, &[SCREEN, tv], Some(SCREEN)), None, "the TV is still on");
        assert_eq!(rescued(on_tv, &[SCREEN], Some(SCREEN)), Some(Rect { x: 320, y: 140, width: 1280, height: 800 }));
        // No primary screen to ask about (Wayland): the first one.
        let on_left_tv = Rect { x: -3000, ..on_tv };
        assert_eq!(rescued(on_left_tv, &[SIDE], None), Some(Rect { x: 1920, y: 112, width: 1280, height: 800 }));
        // Bigger than the screen it comes to: made to fit.
        let deck = Rect { x: 0, y: 0, width: 1280, height: 800 };
        assert_eq!(rescued(Rect { width: 1920, height: 1080, ..on_tv }, &[deck], Some(deck)), Some(deck));
        // Nothing to ask about at all: left alone.
        assert_eq!(rescued(on_tv, &[], None), None);
        // Mostly off its screen, but still enough of it shows to drag it back.
        assert_eq!(rescued(Rect { x: 1800, y: 900, width: 1280, height: 800 }, &[SCREEN], Some(SCREEN)), None);
    }

    #[test]
    fn opens_maximized_the_first_time_then_as_the_player_left_it() {
        let size = Some((1280, 800));
        assert!(maximized(None, size, Some(SCREEN)), "nothing saved yet");
        assert!(maximized(None, None, None), "nothing saved, and no screen to ask about");
        assert!(!maximized(Some(&saved(100, 100, 1280, 800, "DP-1")), size, Some(SCREEN)), "restored down last time");
        assert!(maximized(Some(&Saved { maximized: true, ..saved(100, 100, 1280, 800, "DP-1") }), size, Some(SCREEN)));
        let deck = Rect { x: 0, y: 0, width: 1280, height: 800 };
        assert!(maximized(Some(&saved(0, 0, 1280, 800, "DP-1")), size, Some(deck)), "fills its screen");
    }
}
