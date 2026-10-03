//! Controllers, read natively: the Linux webview's Gamepad API is unreliable
//! (Steam Deck included). Sends each control press and release as a "pad"
//! event named after the control ("South", "DPadUp", "LeftStickLeft"…); the
//! UI maps them to actions with the player's bindings. "pads" says which
//! controllers are connected whenever that changes.
use gilrs::{Axis, EventType, Gilrs};
use std::collections::HashSet;
use std::sync::Mutex;
use std::time::Duration;
use tauri::{AppHandle, Emitter, Manager};

#[derive(serde::Serialize, Clone)]
struct Pad {
    key: String,
    down: bool,
}

/// Names of the connected controllers.
#[derive(Default)]
pub struct Pads(Mutex<Vec<String>>);

#[tauri::command]
pub fn controllers(pads: tauri::State<Pads>) -> Vec<String> {
    pads.0.lock().unwrap().clone()
}

fn connected(gilrs: &Gilrs) -> Vec<String> {
    gilrs.gamepads().map(|(_, pad)| pad.name().to_string()).collect()
}

pub fn start(app: AppHandle) {
    std::thread::spawn(move || {
        let Ok(mut gilrs) = Gilrs::new() else { return };
        let update = |gilrs: &Gilrs| {
            let names = connected(gilrs);
            *app.state::<Pads>().0.lock().unwrap() = names.clone();
            let _ = app.emit("pads", names);
        };
        update(&gilrs);
        let mut held = HashSet::new();
        let mut send = |key: String, down: bool| {
            // Only changes: a stick resting past the threshold isn't a new press.
            if (down && held.insert(key.clone())) || (!down && held.remove(&key)) {
                let _ = app.emit("pad", Pad { key, down });
            }
        };
        loop {
            while let Some(event) = gilrs.next_event_blocking(Some(Duration::from_millis(250))) {
                match event.event {
                    EventType::Connected | EventType::Disconnected => update(&gilrs),
                    EventType::ButtonPressed(b, _) => send(format!("{b:?}"), true),
                    EventType::ButtonReleased(b, _) => send(format!("{b:?}"), false),
                    // Up is positive here, unlike the web's Gamepad API.
                    EventType::AxisChanged(axis, value, _) => {
                        let (stick, negative, positive) = match axis {
                            Axis::LeftStickX => ("LeftStick", "Left", "Right"),
                            Axis::LeftStickY => ("LeftStick", "Down", "Up"),
                            Axis::RightStickX => ("RightStick", "Left", "Right"),
                            Axis::RightStickY => ("RightStick", "Down", "Up"),
                            _ => continue,
                        };
                        send(format!("{stick}{negative}"), value < -0.5);
                        send(format!("{stick}{positive}"), value > 0.5);
                    }
                    _ => {}
                }
            }
        }
    });
}
