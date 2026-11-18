//! TV mode: a fullscreen, controller-first mode (the page's src/tv). Its own
//! commands live here; lib.rs registers them. Its settings are the launcher's.
pub mod art;
#[cfg(windows)]
mod front;
#[cfg(target_os = "linux")]
pub mod pointer;
pub mod programs;
pub mod session;

/// How the launcher was started: `--tv-mode` opens straight into TV mode.
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct LaunchArgs {
    tv_mode: bool,
    /// `QUIVER_TV_DEBUG=1`: an overlay of controller events, for testing controllers.
    debug: bool,
}

#[tauri::command]
pub fn tv_launch_args() -> LaunchArgs {
    LaunchArgs {
        tv_mode: std::env::args().any(|a| a == "--tv-mode"),
        debug: std::env::var_os("QUIVER_TV_DEBUG").is_some_and(|v| v != "0"),
    }
}

/// Brings the launcher's window in front and gives it the keyboard: controllers
/// and keys only reach the window in front. Windows lets a window that isn't in
/// front take focus only in some cases, so this can still leave it flashing in
/// the taskbar; it says whether it worked.
/// Async so it runs off the main thread: window calls from a synchronous command can deadlock on Windows.
#[tauri::command]
pub async fn tv_focus(window: tauri::WebviewWindow) -> bool {
    let _ = window.unminimize();
    let _ = window.show();
    // tao tries SetForegroundWindow, then again after a simulated Alt press.
    let _ = window.set_focus();
    // Only Windows needs (and can check) more. On Linux the window can't say whether it's in front: on CEF it hands
    // the X focus to Chromium's own window at once (gamepad.rs), so minimizing and restoring would follow every game.
    if cfg!(not(windows)) || window.is_focused().unwrap_or(false) {
        return true;
    }
    // Last resort: minimizing and restoring brings a window in front.
    let _ = window.minimize();
    std::thread::sleep(std::time::Duration::from_millis(150));
    let _ = window.unminimize();
    let _ = window.set_focus();
    std::thread::sleep(std::time::Duration::from_millis(150));
    window.is_focused().unwrap_or(false)
}

/// TV mode's screen: fullscreen on the monitor named, or the one the window is on
/// when there's none (or it's gone). Resolves to the monitor used.
#[tauri::command]
pub async fn tv_use_monitor(window: tauri::WebviewWindow, name: Option<String>) -> Result<Option<String>, String> {
    let monitors = window.available_monitors().map_err(|e| e.to_string())?;
    let target = name.and_then(|n| monitors.into_iter().find(|m| m.name().is_some_and(|mn| *mn == n)));
    let Some(monitor) = target else {
        return Ok(window.current_monitor().ok().flatten().and_then(|m| m.name().cloned()));
    };
    // Out of fullscreen to move, then back in on the new monitor.
    window.set_fullscreen(false).map_err(|e| e.to_string())?;
    window.set_position(*monitor.position()).map_err(|e| e.to_string())?;
    window.set_fullscreen(true).map_err(|e| e.to_string())?;
    Ok(monitor.name().cloned())
}

/// The monitors there are, by name, for the setting.
#[tauri::command]
pub async fn tv_monitors(window: tauri::WebviewWindow) -> Vec<String> {
    window.available_monitors().unwrap_or_default().iter().filter_map(|m| m.name().cloned()).collect()
}
