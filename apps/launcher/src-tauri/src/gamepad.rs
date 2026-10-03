//! Controllers, read natively: the Linux webview's Gamepad API is unreliable
//! (Steam Deck included). Sends "pad" events the UI's spatial navigation uses.
use gilrs::{Axis, Button, EventType, Gilrs};
use std::collections::HashSet;
use std::time::Duration;
use tauri::{AppHandle, Emitter};

#[derive(serde::Serialize, Clone)]
struct Pad {
    key: &'static str,
    down: bool,
}

pub fn start(app: AppHandle) {
    std::thread::spawn(move || {
        let Ok(mut gilrs) = Gilrs::new() else { return };
        let mut held = HashSet::new();
        let mut send = |key: &'static str, down: bool| {
            // Only changes: a stick resting past the threshold isn't a new press.
            if (down && held.insert(key)) || (!down && held.remove(key)) {
                let _ = app.emit("pad", Pad { key, down });
            }
        };
        loop {
            while let Some(event) = gilrs.next_event_blocking(Some(Duration::from_millis(250))) {
                match event.event {
                    EventType::ButtonPressed(b, _) | EventType::ButtonReleased(b, _) => {
                        let down = matches!(event.event, EventType::ButtonPressed(..));
                        let key = match b {
                            Button::DPadUp => "up",
                            Button::DPadDown => "down",
                            Button::DPadLeft => "left",
                            Button::DPadRight => "right",
                            Button::South => "a",
                            Button::East => "b",
                            _ => continue,
                        };
                        send(key, down);
                    }
                    EventType::AxisChanged(Axis::LeftStickX, x, _) => {
                        send("left", x < -0.5);
                        send("right", x > 0.5);
                    }
                    // Up is positive here, unlike the web's Gamepad API.
                    EventType::AxisChanged(Axis::LeftStickY, y, _) => {
                        send("up", y > 0.5);
                        send("down", y < -0.5);
                    }
                    _ => {}
                }
            }
        }
    });
}
