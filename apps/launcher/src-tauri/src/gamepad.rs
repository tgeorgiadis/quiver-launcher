//! Controllers, read natively with SDL: the Linux webview's Gamepad API is
//! unreliable (Steam Deck included). SDL knows the most controllers and, when
//! Steam starts Quiver, skips the ones Steam Input stands in for, so a
//! controller Steam translates isn't seen twice; with Steam running but not
//! having started Quiver, Valve's own controllers are left to Steam's desktop
//! layout (keys and a mouse) for the same reason. Sends each control press and
//! release as a "pad" event named after the control ("South", "DPadUp",
//! "LeftStickLeft"…, the names gilrs gave them, which saved bindings use); the
//! UI maps them to actions with the player's bindings. "pads" says which
//! controllers are connected whenever that changes.
//!
//! Only while Quiver's window is in front: behind it (a game started from
//! Quiver, say) the controllers are let go, so the game has them to itself.
//! On Windows SDL holds some (DirectInput ones, wheels among them) exclusively.
//! Except while a game Quiver started runs, for the buttons that close it (Start + Select unless others are chosen)
//! (session.rs), and on Linux the mouse pointer from the controller (tv/pointer.rs): then they're read in the
//! background, sending nothing, and without DirectInput.
use std::sync::Mutex;

/// Names of the connected controllers, and the labels of the one used last.
#[derive(Default)]
pub struct Pads(Mutex<Vec<String>>, Mutex<Option<Labels>>);

/// What a controller's buttons say, for the UI's hints and bindings: its kind
/// ("xbox", "playstation", "nintendo", "steam" or "other") and the labels on
/// the face buttons, by place ("A", "Cross"…). Sent as "pad-labels" when the
/// controller used last changes.
#[derive(serde::Serialize, Clone, PartialEq)]
#[cfg_attr(target_os = "android", allow(dead_code))]
pub struct Labels {
    kind: &'static str,
    south: &'static str,
    east: &'static str,
    west: &'static str,
    north: &'static str,
    /// Its presses come as Steam's desktop layout's keys, not to Quiver itself (a Steam Deck's own controls in its desktop).
    desktop: bool,
}

#[tauri::command]
pub fn controllers(pads: tauri::State<Pads>) -> Vec<String> {
    pads.0.lock().unwrap().clone()
}

#[tauri::command]
pub fn controller_labels(pads: tauri::State<Pads>) -> Option<Labels> {
    pads.1.lock().unwrap().clone()
}

/// The page gaining or losing the focus (main.tsx), which says whether Quiver is in front on CEF (see sdl::start).
#[tauri::command]
pub fn page_focus(focused: bool) {
    #[cfg(all(feature = "cef", target_os = "linux"))]
    sdl::FOCUSED.store(focused, std::sync::atomic::Ordering::Relaxed);
    let _ = focused;
}

/// None on Android: SDL reads controllers there through its own Java
/// activity, which Quiver's app doesn't have.
#[cfg(target_os = "android")]
pub fn start(_app: tauri::AppHandle) {}

#[cfg(not(target_os = "android"))]
pub use sdl::start;

#[cfg(not(target_os = "android"))]
mod sdl {
    use super::{Labels, Pads};
    use sdl3_sys::events::*;
    use sdl3_sys::gamepad::*;
    use sdl3_sys::hints::*;
    use sdl3_sys::init::*;
    use sdl3_sys::joystick::SDL_JoystickID;
    use sdl3_sys::stdinc::SDL_free;
    use std::collections::{BTreeMap, HashMap, HashSet};
    use std::ffi::CStr;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::time::{Duration, Instant};
    use tauri::{AppHandle, Emitter, Manager, WindowEvent};

    #[derive(serde::Serialize, Clone)]
    struct Pad {
        key: &'static str,
        down: bool,
    }

    /// Whether Quiver's window is in front. Until it says otherwise it is, so a
    /// window that never reports its focus still has controllers.
    pub(super) static FOCUSED: AtomicBool = AtomicBool::new(true);

    /// How far a stick or trigger goes before it counts as pressed: half way.
    const HALF: i16 = i16::MAX / 2;

    pub fn start(app: AppHandle) {
        if let Some(window) = app.get_webview_window("main") {
            window.on_window_event(|event| {
                // Not on CEF: there the window hands the X focus to Chromium's own window as soon as it gets it, and
                // so reports losing it while it's in front. The page's focus says instead (page_focus).
                if let (false, WindowEvent::Focused(focused)) = (cfg!(all(feature = "cef", target_os = "linux")), event) {
                    FOCUSED.store(*focused, Ordering::Relaxed);
                }
            });
        }
        std::thread::spawn(move || loop {
            let behind = !FOCUSED.load(Ordering::Relaxed);
            if !behind || crate::session::watching() {
                if !read(&app, behind) {
                    return;
                }
            } else {
                std::thread::sleep(Duration::from_millis(100));
            }
        });
    }

    /// Whether to go on reading: in front, or `behind` while a game wants watching.
    fn reading(behind: bool) -> bool {
        let focused = FOCUSED.load(Ordering::Relaxed);
        if behind {
            !focused && crate::session::watching()
        } else {
            focused
        }
    }

    /// Reads controllers while Quiver is in front, or `behind` it during a game; false if SDL can't start.
    /// SDL and every call to it stay on this one thread.
    fn read(app: &AppHandle, behind: bool) -> bool {
        // Steam running turns a Steam Deck's controls (and a Steam Controller's) into its desktop layout's keys and
        // mouse for a program it didn't start: the d-pad the arrow keys, A Enter, B Escape. Read here as well, each
        // press came twice, and with Steam's keyboard or overlay up, a press there moved Quiver too. So they're left
        // to Steam, as for any other program; started by Steam, SDL already sees Steam's own pad.
        let steams = !crate::steam::started_us(|name| std::env::var(name).ok()) && crate::steam::running();
        // A Steam Deck's own controls are there all the same, for the UI's hints of what its buttons do.
        let deck = steams && on_deck();
        // SAFETY: the strings are 'static and NUL-terminated; SDL copies them.
        // Set before every start: SDL_Quit forgets them.
        unsafe {
            // Otherwise SDL catches Ctrl+C and SIGTERM as quit events of its own, and the launcher wouldn't stop for them.
            SDL_SetHint(SDL_HINT_NO_SIGNAL_HANDLERS, c"1".as_ptr());
            // Otherwise SDL sets Windows' timer to 1 ms, which costs battery for nothing here.
            SDL_SetHint(SDL_HINT_TIMER_RESOLUTION, c"0".as_ptr());
            if steams {
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_STEAMDECK, c"0".as_ptr());
                SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_STEAM, c"0".as_ptr());
            }
            // Behind a game: SDL hears controllers though Quiver isn't in front, and leaves DirectInput's alone, which it
            // would hold for itself (Xbox, PlayStation and Switch pads come through its other ways).
            SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, if behind { c"1".as_ptr() } else { c"0".as_ptr() });
            #[cfg(windows)]
            SDL_SetHint(SDL_HINT_JOYSTICK_DIRECTINPUT, if behind { c"0".as_ptr() } else { c"1".as_ptr() });
            if !SDL_Init(SDL_INIT_GAMEPAD) {
                return false;
            }
        }
        let mut open = BTreeMap::<SDL_JoystickID, *mut SDL_Gamepad>::new();
        let mut held = Held::default();
        let mut in_game = InGame::default();
        // Behind a game, nothing goes to the page: the game has the controllers.
        let send = |key: &'static str, down: bool| {
            if !behind {
                let _ = app.emit("pad", Pad { key, down });
            }
        };
        // The controller used last, whose labels the UI shows: the first connected until another's button is pressed.
        let mut active = None::<SDL_JoystickID>;
        let update = |open: &BTreeMap<SDL_JoystickID, *mut SDL_Gamepad>, active: &mut Option<SDL_JoystickID>| {
            if behind {
                return;
            }
            if !active.is_some_and(|id| open.contains_key(&id)) {
                *active = open.keys().next().copied();
            }
            let state = app.state::<Pads>();
            let names: Vec<String> = deck.then(|| "Steam Deck".to_string()).into_iter().chain(open.values().map(|&pad| name(pad))).collect();
            let mut pads = state.0.lock().unwrap();
            if *pads != names {
                *pads = names.clone();
                let _ = app.emit("pads", names);
            }
            let now = active.map(|id| labels(open[&id])).or_else(|| deck.then(steam_desktop));
            let mut shown = state.1.lock().unwrap();
            if *shown != now {
                *shown = now.clone();
                let _ = app.emit("pad-labels", now);
            }
        };
        // The ones connected already. SDL also says they've been added, but
        // opening one twice is skipped below.
        let mut count = 0;
        // SAFETY: SDL_GetGamepads returns `count` ids (or null), freed with SDL_free.
        unsafe {
            let ids = SDL_GetGamepads(&mut count);
            if !ids.is_null() {
                for &id in std::slice::from_raw_parts(ids, count.max(0) as usize) {
                    connect(&mut open, id);
                }
                SDL_free(ids.cast());
            }
        }
        update(&open, &mut active);
        while reading(behind) {
            let mut event = SDL_Event::default();
            // SAFETY: SDL fills in the event; its type says which of the union's fields it is.
            while unsafe { SDL_PollEvent(&mut event) } {
                match SDL_EventType(unsafe { event.r#type }) {
                    SDL_EVENT_GAMEPAD_ADDED => {
                        connect(&mut open, unsafe { event.gdevice.which });
                        update(&open, &mut active);
                    }
                    SDL_EVENT_GAMEPAD_REMOVED => {
                        let id = unsafe { event.gdevice.which };
                        if let Some(pad) = open.remove(&id) {
                            // SAFETY: opened by connect and not closed since.
                            unsafe { SDL_CloseGamepad(pad) };
                        }
                        for key in held.forget(id.0) {
                            send(key, false);
                        }
                        update(&open, &mut active);
                    }
                    SDL_EVENT_GAMEPAD_BUTTON_DOWN | SDL_EVENT_GAMEPAD_BUTTON_UP => {
                        let event = unsafe { event.gbutton };
                        if event.down && active != Some(event.which) && open.contains_key(&event.which) {
                            active = Some(event.which);
                            update(&open, &mut active);
                        }
                        if let Some(key) = button(SDL_GamepadButton(event.button.into())) {
                            if held.set(event.which.0, key, event.down) {
                                send(key, event.down);
                            }
                        }
                    }
                    SDL_EVENT_GAMEPAD_AXIS_MOTION => {
                        let event = unsafe { event.gaxis };
                        in_game.axis(event.which.0, SDL_GamepadAxis(event.axis.into()), event.value);
                        for (key, down) in axis(SDL_GamepadAxis(event.axis.into()), event.value) {
                            if held.set(event.which.0, key, down) {
                                send(key, down);
                            }
                        }
                    }
                    _ => {}
                }
            }
            in_game.tick(app, &held);
            // Often enough to feel instant; with none connected, often enough to see one arrive.
            std::thread::sleep(Duration::from_millis(if open.is_empty() { 250 } else { 10 }));
        }
        // Behind another window: nothing stays held, and the controllers go.
        for key in held.all() {
            send(key, false);
        }
        // SAFETY: each was opened by connect and not closed since; nothing of SDL's is used after SDL_Quit.
        unsafe {
            for pad in open.into_values() {
                SDL_CloseGamepad(pad);
            }
            SDL_Quit();
        }
        true
    }

    /// How long the buttons are held to close the game.
    const CLOSE_HOLD: Duration = Duration::from_secs(1);

    /// While a game the launcher started runs (session.rs): the buttons chosen in Settings (Start + Select at first),
    /// held together on one controller for a second, close it, for games with no way to quit; and on Linux, holding
    /// Select makes the right stick a mouse pointer (RT clicks, LT right-clicks, the left stick scrolls), for launchers
    /// that only take a mouse.
    #[derive(Default)]
    struct InGame {
        /// Since when the buttons have been held, and whether that hold has acted already.
        combo: Option<(Instant, bool)>,
        #[cfg(target_os = "linux")]
        pointer: Pointer,
    }

    impl InGame {
        #[cfg_attr(not(target_os = "linux"), allow(unused_variables))]
        fn axis(&mut self, pad: u32, axis: SDL_GamepadAxis, value: i16) {
            #[cfg(target_os = "linux")]
            self.pointer.axis(pad, axis, value);
        }

        fn tick(&mut self, app: &AppHandle, held: &Held) {
            #[cfg(target_os = "linux")]
            self.pointer.tick(held, |line| crate::append_log(&app.state::<crate::Data>().0.join("quiver.log"), line));
            let buttons = crate::session::close_buttons();
            if !held.combo(&buttons) {
                self.combo = None;
            } else if let Some((since, done)) = self.combo.as_mut() {
                if !*done && since.elapsed() >= CLOSE_HOLD {
                    *done = true;
                    // What it did, in quiver.log: Steam's Game Mode can't be watched any other way.
                    let path = app.state::<crate::Data>().0.join("quiver.log");
                    let what = if !crate::session::CLOSE_COMBO.load(Ordering::Relaxed) {
                        "closing a game with them is off in Settings"
                    } else if crate::session::close_game(&path) {
                        "closing the game"
                    } else {
                        "no game started from Quiver is running"
                    };
                    crate::append_log(&path, &format!("{} held: {what}", buttons.join(" + ")));
                }
            } else {
                self.combo = Some((Instant::now(), false));
            }
        }
    }

    /// The mouse pointer from a controller (Linux, tv/pointer.rs).
    #[cfg(target_os = "linux")]
    #[derive(Default)]
    struct Pointer {
        /// Each controller's right stick and left stick's y, -1 to 1.
        sticks: HashMap<u32, (f32, f32, f32)>,
        mouse: Option<crate::tv::pointer::VirtualMouse>,
        /// Making the mouse failed once (no access to /dev/uinput): not tried again.
        failed: bool,
        /// The left and right buttons, held.
        clicks: (bool, bool),
        last: Option<Instant>,
    }

    #[cfg(target_os = "linux")]
    impl Pointer {
        fn axis(&mut self, pad: u32, axis: SDL_GamepadAxis, value: i16) {
            let value = value as f32 / i16::MAX as f32;
            let stick = self.sticks.entry(pad).or_default();
            match axis {
                SDL_GAMEPAD_AXIS_RIGHTX => stick.0 = value,
                SDL_GAMEPAD_AXIS_RIGHTY => stick.1 = value,
                SDL_GAMEPAD_AXIS_LEFTY => stick.2 = value,
                _ => {}
            }
        }

        fn tick(&mut self, held: &Held, log: impl Fn(&str)) {
            use crate::tv::pointer::{BTN_LEFT, BTN_RIGHT};
            let seconds = self.last.replace(Instant::now()).map_or(0.0, |t| t.elapsed().as_secs_f32().min(0.05));
            let on = crate::session::POINTER.load(Ordering::Relaxed) && crate::session::playing();
            let pad = held.holding("Select").filter(|_| on);
            if pad.is_some() && self.mouse.is_none() && !self.failed {
                match crate::tv::pointer::VirtualMouse::new() {
                    Ok(mouse) => self.mouse = Some(mouse),
                    Err(e) => {
                        self.failed = true;
                        log(&format!("The pointer from the controller is unavailable: {e}"));
                    }
                }
            }
            let Some(mouse) = self.mouse.as_mut() else { return };
            let wanted = pad.map_or((false, false), |p| (held.has(p, "RightTrigger2"), held.has(p, "LeftTrigger2")));
            if wanted.0 != self.clicks.0 {
                mouse.button(BTN_LEFT, wanted.0);
            }
            if wanted.1 != self.clicks.1 {
                mouse.button(BTN_RIGHT, wanted.1);
            }
            self.clicks = wanted;
            if let Some(&(x, y, scroll)) = pad.and_then(|p| self.sticks.get(&p)) {
                mouse.steer((x, y), scroll, seconds);
            }
        }
    }

    /// Opens a controller to hear from it, unless it's open already.
    fn connect(open: &mut BTreeMap<SDL_JoystickID, *mut SDL_Gamepad>, id: SDL_JoystickID) {
        if open.contains_key(&id) {
            return;
        }
        // SAFETY: an id SDL gave; null when it can't be opened.
        let pad = unsafe { SDL_OpenGamepad(id) };
        if !pad.is_null() {
            open.insert(id, pad);
        }
    }

    fn name(pad: *mut SDL_Gamepad) -> String {
        // SAFETY: an open controller; the name is SDL's, valid until it closes, or null.
        let name = unsafe { SDL_GetGamepadName(pad) };
        if name.is_null() {
            return "Controller".into();
        }
        unsafe { CStr::from_ptr(name) }.to_string_lossy().into_owned()
    }

    /// What a controller's buttons say, from its type as SDL knows it.
    fn labels(pad: *mut SDL_Gamepad) -> Labels {
        // SAFETY: an open controller.
        let kind = match unsafe { SDL_GetGamepadType(pad) } {
            SDL_GAMEPAD_TYPE_XBOX360 | SDL_GAMEPAD_TYPE_XBOXONE => "xbox",
            SDL_GAMEPAD_TYPE_PS3 | SDL_GAMEPAD_TYPE_PS4 | SDL_GAMEPAD_TYPE_PS5 => "playstation",
            SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_PRO
            | SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_LEFT
            | SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_RIGHT
            | SDL_GAMEPAD_TYPE_NINTENDO_SWITCH_JOYCON_PAIR
            | SDL_GAMEPAD_TYPE_GAMECUBE => "nintendo",
            SDL_GAMEPAD_TYPE_STEAM => "steam",
            _ => "other",
        };
        // SAFETY: as above. One SDL can't tell is labelled as an Xbox pad's, by place.
        let face = |button, otherwise| match unsafe { SDL_GetGamepadButtonLabel(pad, button) } {
            SDL_GAMEPAD_BUTTON_LABEL_A => "A",
            SDL_GAMEPAD_BUTTON_LABEL_B => "B",
            SDL_GAMEPAD_BUTTON_LABEL_X => "X",
            SDL_GAMEPAD_BUTTON_LABEL_Y => "Y",
            SDL_GAMEPAD_BUTTON_LABEL_CROSS => "Cross",
            SDL_GAMEPAD_BUTTON_LABEL_CIRCLE => "Circle",
            SDL_GAMEPAD_BUTTON_LABEL_SQUARE => "Square",
            SDL_GAMEPAD_BUTTON_LABEL_TRIANGLE => "Triangle",
            _ => otherwise,
        };
        Labels {
            kind,
            south: face(SDL_GAMEPAD_BUTTON_SOUTH, "A"),
            east: face(SDL_GAMEPAD_BUTTON_EAST, "B"),
            west: face(SDL_GAMEPAD_BUTTON_WEST, "X"),
            north: face(SDL_GAMEPAD_BUTTON_NORTH, "Y"),
            desktop: false,
        }
    }

    /// A Steam Deck's own controls, as Steam's desktop layout has them.
    fn steam_desktop() -> Labels {
        Labels { kind: "steam", south: "A", east: "B", west: "X", north: "Y", desktop: true }
    }

    /// This is a Steam Deck (or another of Valve's handhelds), whose controls are part of it.
    fn on_deck() -> bool {
        cfg!(target_os = "linux") && std::fs::read_to_string("/sys/class/dmi/id/board_vendor").is_ok_and(|v| v.trim() == "Valve")
    }

    /// A button's name, as gilrs named it for those it knew.
    fn button(button: SDL_GamepadButton) -> Option<&'static str> {
        Some(match button {
            SDL_GAMEPAD_BUTTON_SOUTH => "South",
            SDL_GAMEPAD_BUTTON_EAST => "East",
            SDL_GAMEPAD_BUTTON_WEST => "West",
            SDL_GAMEPAD_BUTTON_NORTH => "North",
            SDL_GAMEPAD_BUTTON_BACK => "Select",
            SDL_GAMEPAD_BUTTON_GUIDE => "Mode",
            SDL_GAMEPAD_BUTTON_START => "Start",
            SDL_GAMEPAD_BUTTON_LEFT_STICK => "LeftThumb",
            SDL_GAMEPAD_BUTTON_RIGHT_STICK => "RightThumb",
            SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => "LeftTrigger",
            SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => "RightTrigger",
            SDL_GAMEPAD_BUTTON_DPAD_UP => "DPadUp",
            SDL_GAMEPAD_BUTTON_DPAD_DOWN => "DPadDown",
            SDL_GAMEPAD_BUTTON_DPAD_LEFT => "DPadLeft",
            SDL_GAMEPAD_BUTTON_DPAD_RIGHT => "DPadRight",
            // Share on Xbox, Capture on Switch, Mic on PlayStation 5.
            SDL_GAMEPAD_BUTTON_MISC1 => "Misc1",
            SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1 => "RightPaddle1",
            SDL_GAMEPAD_BUTTON_LEFT_PADDLE1 => "LeftPaddle1",
            SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2 => "RightPaddle2",
            SDL_GAMEPAD_BUTTON_LEFT_PADDLE2 => "LeftPaddle2",
            SDL_GAMEPAD_BUTTON_TOUCHPAD => "Touchpad",
            _ => return None,
        })
    }

    /// The presses a stick or trigger's position makes. Down is positive here,
    /// as in the web's Gamepad API (unlike gilrs).
    fn axis(axis: SDL_GamepadAxis, value: i16) -> Vec<(&'static str, bool)> {
        let stick = |negative, positive| vec![(negative, value < -HALF), (positive, value > HALF)];
        match axis {
            SDL_GAMEPAD_AXIS_LEFTX => stick("LeftStickLeft", "LeftStickRight"),
            SDL_GAMEPAD_AXIS_LEFTY => stick("LeftStickUp", "LeftStickDown"),
            SDL_GAMEPAD_AXIS_RIGHTX => stick("RightStickLeft", "RightStickRight"),
            SDL_GAMEPAD_AXIS_RIGHTY => stick("RightStickUp", "RightStickDown"),
            SDL_GAMEPAD_AXIS_LEFT_TRIGGER => vec![("LeftTrigger2", value > HALF)],
            SDL_GAMEPAD_AXIS_RIGHT_TRIGGER => vec![("RightTrigger2", value > HALF)],
            _ => vec![],
        }
    }

    /// The controls held down, by controller. The UI hears only changes: a
    /// control goes down when a first controller presses it (a stick resting
    /// past half way isn't a new press) and up when the last lets go.
    #[derive(Default)]
    struct Held(HashMap<u32, HashSet<&'static str>>);

    impl Held {
        /// Whether that changed the control for the UI.
        fn set(&mut self, pad: u32, key: &'static str, down: bool) -> bool {
            let before = self.any(key);
            let keys = self.0.entry(pad).or_default();
            if down {
                keys.insert(key);
            } else {
                keys.remove(key);
            }
            before != self.any(key)
        }

        fn any(&self, key: &str) -> bool {
            self.0.values().any(|keys| keys.contains(key))
        }

        /// A controller gone: the controls only it held, now up.
        fn forget(&mut self, pad: u32) -> Vec<&'static str> {
            let keys = self.0.remove(&pad).unwrap_or_default();
            keys.into_iter().filter(|key| !self.any(key)).collect()
        }

        /// Everything held, now up.
        fn all(&mut self) -> HashSet<&'static str> {
            self.0.drain().flat_map(|(_, keys)| keys).collect()
        }

        /// All these buttons held together on one controller.
        fn combo(&self, buttons: &[String]) -> bool {
            !buttons.is_empty() && self.0.values().any(|keys| buttons.iter().all(|b| keys.contains(b.as_str())))
        }

        /// The controller holding `key`, if one is.
        #[cfg(target_os = "linux")]
        fn holding(&self, key: &str) -> Option<u32> {
            self.0.iter().find(|(_, keys)| keys.contains(key)).map(|(&pad, _)| pad)
        }

        #[cfg(target_os = "linux")]
        fn has(&self, pad: u32, key: &str) -> bool {
            self.0.get(&pad).is_some_and(|keys| keys.contains(key))
        }
    }

    #[cfg(test)]
    mod tests {
        use super::*;

        #[test]
        fn names_controls_as_saved_bindings_do() {
            assert_eq!(button(SDL_GAMEPAD_BUTTON_SOUTH), Some("South"));
            assert_eq!(button(SDL_GAMEPAD_BUTTON_LEFT_SHOULDER), Some("LeftTrigger"));
            assert_eq!(button(SDL_GAMEPAD_BUTTON_RIGHT_STICK), Some("RightThumb"));
            assert_eq!(button(SDL_GAMEPAD_BUTTON_INVALID), None);
            assert_eq!(axis(SDL_GAMEPAD_AXIS_LEFTY, i16::MIN), vec![("LeftStickUp", true), ("LeftStickDown", false)]);
            assert_eq!(axis(SDL_GAMEPAD_AXIS_LEFTX, i16::MAX), vec![("LeftStickLeft", false), ("LeftStickRight", true)]);
            assert_eq!(axis(SDL_GAMEPAD_AXIS_RIGHTY, 100), vec![("RightStickUp", false), ("RightStickDown", false)]);
            assert_eq!(axis(SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, i16::MAX), vec![("RightTrigger2", true)]);
        }

        #[test]
        fn sends_a_control_down_once_and_up_when_the_last_controller_lets_go() {
            let mut held = Held::default();
            assert!(held.set(1, "South", true));
            assert!(!held.set(1, "South", true), "a stick resting past half way");
            assert!(!held.set(2, "South", true));
            assert!(!held.set(1, "South", false));
            assert!(held.set(2, "South", false));
            assert!(!held.set(1, "DPadUp", false), "never down");
        }

        #[test]
        fn the_buttons_close_a_game_only_held_on_one_controller() {
            let start_select = ["Start".to_string(), "Select".to_string()];
            let mut held = Held::default();
            held.set(1, "Start", true);
            held.set(2, "Select", true);
            assert!(!held.combo(&start_select));
            held.set(1, "Select", true);
            assert!(held.combo(&start_select));
            let sticks = ["LeftThumb".to_string(), "RightThumb".to_string()];
            assert!(!held.combo(&sticks), "other buttons chosen");
            held.set(2, "LeftThumb", true);
            held.set(2, "RightThumb", true);
            assert!(held.combo(&sticks));
        }

        #[test]
        fn lets_go_of_what_a_controller_held_when_it_goes() {
            let mut held = Held::default();
            held.set(1, "South", true);
            held.set(1, "DPadUp", true);
            held.set(2, "South", true);
            assert_eq!(held.forget(1), vec!["DPadUp"]);
            assert!(held.forget(1).is_empty());
            held.set(3, "East", true);
            assert_eq!(held.all(), HashSet::from(["South", "East"]));
            assert!(!held.any("South"));
        }
    }
}
