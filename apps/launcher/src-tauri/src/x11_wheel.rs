//! The mouse wheel on Linux's Chromium build, where some window managers make Chromium lose every turn of it.
//!
//! XFCE's (xfwm4, with "Raise windows when any mouse button is pressed", its default) and others keep a passive grab
//! on the mouse buttons of the windows they manage, and a wheel's step comes to X as a scroll plus a pretend press of
//! button 4 or 5. That press sets off the grab: the pointer goes to the window manager for an instant and comes back,
//! and Chromium, which sits in a window of its own inside Quiver's, takes the pointer's coming back as it arriving and
//! forgets where the wheel was, so it measures every step as nothing and never scrolls. (A browser on its own isn't
//! hit: there the window that's grabbed is Chromium's own.)
//!
//! Here Quiver watches its window for the same thing: a step of the wheel (from X's raw events, which no grab holds
//! back) at the very moment the pointer is grabbed from it. Only such a step is lost, and only such a step is handed
//! to the page, through the DevTools protocol's own wheel event, where the pointer is: it scrolls what's under the
//! pointer as the wheel would, at Settings' wheel speed (spatial.ts). A desktop with no such grab never sees one, and
//! nothing changes there.

#![allow(non_upper_case_globals)]

use raw_window_handle::{HasWindowHandle, RawWindowHandle};
use std::collections::HashMap;
use std::ffi::{c_int, c_uchar};
use std::ptr;
use tauri::{AppHandle, Manager, WebviewWindow};
use tauri_runtime_cef::{allocate_devtools_message_id, WebviewCefExt};
use x11_dl::xinput2::*;
use x11_dl::xlib::{self, Xlib};

/// How far one step of a wheel scrolls, in the page's pixels: what Chromium gives a step that reaches it (120).
const STEP: f64 = 120.0;
/// How close in time (ms, the X server's clock) the grab comes after the step it was set off by: the same instant on
/// the server, give or take.
const SAME_MOMENT: u64 = 20;

/// Up and down, and sideways, of one device's scroll valuators: their numbers, and how much of each is one step.
#[derive(Clone, Copy, Default)]
struct Scrolls {
    vertical: Option<(i32, f64)>,
    horizontal: Option<(i32, f64)>,
}

/// Starts watching, on a thread of its own and its own connection to X. Nothing on a desktop without X.
pub fn start(app: AppHandle) {
    std::thread::spawn(move || {
        let Some(window) = app.get_webview_window("main") else { return };
        // Asked off the main thread: the runtime answers from its event loop.
        let Some(xid) = toplevel(&window) else { return };
        let log = app.state::<crate::Data>().0.join("quiver.log");
        if let Err(e) = watch(window, xid) {
            crate::append_log(&log, &format!("Not watching the mouse wheel for window managers' grabs: {e}"));
        }
    });
}

fn toplevel(window: &WebviewWindow) -> Option<u64> {
    match window.window_handle().ok()?.as_raw() {
        RawWindowHandle::Xlib(handle) => Some(handle.window as u64),
        RawWindowHandle::Xcb(handle) => Some(handle.window.get() as u64),
        _ => None,
    }
}

fn watch(window: WebviewWindow, xid: u64) -> Result<(), String> {
    let xlib = Xlib::open().map_err(|e| e.to_string())?;
    let xi = XInput2::open().map_err(|e| e.to_string())?;
    unsafe {
        let display = (xlib.XOpenDisplay)(ptr::null());
        if display.is_null() {
            return Err("no X display".into());
        }
        let (mut opcode, mut first_event, mut first_error) = (0, 0, 0);
        if (xlib.XQueryExtension)(display, c"XInputExtension".as_ptr(), &mut opcode, &mut first_event, &mut first_error) == 0 {
            return Err("no XInput extension".into());
        }
        // 2.1 is the first with scroll valuators.
        let (mut major, mut minor) = (2, 2);
        if (xi.XIQueryVersion)(display, &mut major, &mut minor) != 0 || (major, minor) < (2, 1) {
            return Err(format!("XInput {major}.{minor}"));
        }
        // The pointer grabbed from Quiver's window (an enter or leave of it, as the grab's window is it or around it),
        // and every device's raw motion, which comes whoever has the pointer.
        select(&xi, display, xid, &[XI_Enter, XI_Leave]);
        let root = (xlib.XDefaultRootWindow)(display);
        select(&xi, display, root, &[XI_RawMotion, XI_HierarchyChanged, XI_DeviceChanged]);
        (xlib.XFlush)(display);

        let mut devices: HashMap<c_int, Scrolls> = HashMap::new();
        // The last step of a wheel: when, and how many steps down and to the right.
        let mut step: Option<(u64, f64, f64)> = None;
        let mut event: xlib::XEvent = std::mem::zeroed();
        loop {
            (xlib.XNextEvent)(display, &mut event);
            let cookie = &mut event.generic_event_cookie;
            if cookie.type_ != xlib::GenericEvent || cookie.extension != opcode || (xlib.XGetEventData)(display, cookie) == 0 {
                continue;
            }
            match cookie.evtype {
                XI_HierarchyChanged | XI_DeviceChanged => devices.clear(),
                XI_RawMotion => {
                    let raw = &*(cookie.data as *const XIRawEvent);
                    let scrolls = *devices.entry(raw.sourceid).or_insert_with(|| scrolls_of(&xi, display, raw.sourceid));
                    let (down, right) = steps(raw, scrolls);
                    if down != 0.0 || right != 0.0 {
                        step = Some((raw.time, down, right));
                    }
                }
                XI_Enter | XI_Leave => {
                    let crossing = &*(cookie.data as *const XIEnterEvent);
                    if crossing.mode == XINotifyGrab {
                        if let Some((time, down, right)) = step.take() {
                            if crossing.time.wrapping_sub(time) <= SAME_MOMENT {
                                scroll(&window, crossing, down, right);
                            }
                        }
                    }
                }
                _ => {}
            }
            (xlib.XFreeEventData)(display, cookie);
        }
    }
}

unsafe fn select(xi: &XInput2, display: *mut xlib::Display, window: u64, events: &[c_int]) {
    let mut mask = [0 as c_uchar; ((XI_LASTEVENT >> 3) + 1) as usize];
    for &e in events {
        XISetMask(&mut mask, e);
    }
    let mut masks = XIEventMask { deviceid: XIAllMasterDevices, mask_len: mask.len() as c_int, mask: mask.as_mut_ptr() };
    // Device changes come for every device, not only the master ones.
    if events.contains(&XI_HierarchyChanged) {
        masks.deviceid = XIAllDevices;
    }
    (xi.XISelectEvents)(display, window as _, &mut masks, 1);
}

/// A device's scroll valuators, from X.
unsafe fn scrolls_of(xi: &XInput2, display: *mut xlib::Display, device: c_int) -> Scrolls {
    let mut scrolls = Scrolls::default();
    let mut count = 0;
    let info = (xi.XIQueryDevice)(display, device, &mut count);
    if info.is_null() {
        return scrolls;
    }
    for d in 0..count as usize {
        let info = &*info.add(d);
        for c in 0..info.num_classes as usize {
            let class = *info.classes.add(c);
            if (*class)._type != XIScrollClass {
                continue;
            }
            let class = &*(class as *const XIScrollClassInfo);
            let increment = if class.increment == 0.0 { 1.0 } else { class.increment };
            if class.scroll_type == XIScrollTypeVertical {
                scrolls.vertical = Some((class.number, increment));
            } else {
                scrolls.horizontal = Some((class.number, increment));
            }
        }
    }
    (xi.XIFreeDeviceInfo)(info);
    scrolls
}

/// How many steps down and to the right a raw motion scrolled: its scroll valuators' change (a raw event carries the
/// change of each it has) over one step's worth of each.
unsafe fn steps(raw: &XIRawEvent, scrolls: Scrolls) -> (f64, f64) {
    let mask = std::slice::from_raw_parts(raw.valuators.mask, raw.valuators.mask_len as usize);
    let (mut down, mut right) = (0.0, 0.0);
    let mut at = 0;
    for number in 0..(mask.len() * 8) as i32 {
        if !XIMaskIsSet(mask, number) {
            continue;
        }
        let value = *raw.valuators.values.add(at);
        at += 1;
        match (scrolls.vertical, scrolls.horizontal) {
            (Some((n, increment)), _) if n == number => down = value / increment,
            (_, Some((n, increment))) if n == number => right = value / increment,
            _ => {}
        }
    }
    (down, right)
}

/// The steps Chromium lost, as a wheel's, where the pointer is on the page.
fn scroll(window: &WebviewWindow, crossing: &XIEnterEvent, down: f64, right: f64) {
    let scale = window.scale_factor().unwrap_or(1.0);
    // The DevTools protocol's modifiers: Alt 1, Ctrl 2, Shift 8.
    let mods = crossing.mods.effective as u32;
    let modifiers = (if mods & xlib::Mod1Mask != 0 { 1 } else { 0 })
        | (if mods & xlib::ControlMask != 0 { 2 } else { 0 })
        | (if mods & xlib::ShiftMask != 0 { 8 } else { 0 });
    let Ok(id) = allocate_devtools_message_id() else { return };
    let message = serde_json::json!({
        "id": id,
        "method": "Input.dispatchMouseEvent",
        "params": {
            "type": "mouseWheel",
            "x": crossing.event_x / scale,
            "y": crossing.event_y / scale,
            "deltaX": right * STEP,
            "deltaY": down * STEP,
            "modifiers": modifiers,
        },
    });
    let _ = window.send_dev_tools_message(message.to_string().as_bytes());
}
