//! Windows: a game's window in front of Quiver. Windows lets a new window come in front only soon after a key or click
//! in the window in front, and only for what that program started, so a game started from a controller, or through
//! a script or a launcher of its own, often opened behind Quiver (TV mode's full screen hiding it). While Quiver is
//! still in front, it gives the front to the game's window, which the program in front may; until the game has kept
//! it a moment, or the player went to another program, or the game never showed one.
use std::path::PathBuf;
use std::time::{Duration, Instant};
use sysinfo::{Pid, System};

type Hwnd = isize;

#[link(name = "user32")]
extern "system" {
    fn EnumWindows(callback: extern "system" fn(Hwnd, isize) -> i32, data: isize) -> i32;
    fn GetWindowThreadProcessId(window: Hwnd, process: *mut u32) -> u32;
    fn IsWindowVisible(window: Hwnd) -> i32;
    fn GetWindow(window: Hwnd, which: u32) -> Hwnd;
    fn GetForegroundWindow() -> Hwnd;
    fn SetForegroundWindow(window: Hwnd) -> i32;
    fn IsIconic(window: Hwnd) -> i32;
    fn ShowWindow(window: Hwnd, how: i32) -> i32;
    fn GetClassNameW(window: Hwnd, name: *mut u16, size: i32) -> i32;
}

const GW_OWNER: u32 = 4;
const SW_RESTORE: i32 = 9;
/// How long a game has to show its window (shaders compiling, a launcher's own window first).
const WAIT: Duration = Duration::from_secs(30);
/// How long the game keeps the front before it's left to it.
const KEPT: Duration = Duration::from_secs(3);
const POLL: Duration = Duration::from_millis(400);

fn process_of(window: Hwnd) -> u32 {
    let mut process = 0;
    // SAFETY: a window handle (any, even a closed one) and a place for the answer.
    unsafe { GetWindowThreadProcessId(window, &mut process) };
    process
}

/// A console's window (a script's): not the game's own.
fn console(window: Hwnd) -> bool {
    let mut name = [0u16; 32];
    // SAFETY: the buffer's length is given.
    let n = unsafe { GetClassNameW(window, name.as_mut_ptr(), name.len() as i32) };
    String::from_utf16_lossy(&name[..n.max(0) as usize]) == "ConsoleWindowClass"
}

/// The windows a program shows as its own: visible, top-level, not a console.
fn windows() -> Vec<Hwnd> {
    extern "system" fn each(window: Hwnd, found: isize) -> i32 {
        // SAFETY: `found` is the Vec below, for the length of EnumWindows.
        let found = unsafe { &mut *(found as *mut Vec<Hwnd>) };
        found.push(window);
        1
    }
    let mut all: Vec<Hwnd> = Vec::new();
    // SAFETY: the callback only pushes to `all`, which outlives the call.
    unsafe { EnumWindows(each, &mut all as *mut Vec<Hwnd> as isize) };
    // SAFETY: plain questions about window handles.
    all.into_iter().filter(|&w| unsafe { IsWindowVisible(w) != 0 && GetWindow(w, GW_OWNER) == 0 } && !console(w)).collect()
}

/// Gives the game's window the front while Quiver has it; see the module's comment.
pub fn bring_forward(root: Option<u32>, dir: PathBuf, since: u64) {
    let me = std::process::id();
    let started = Instant::now();
    let mut kept: Option<Instant> = None;
    let mut system = System::new();
    while started.elapsed() < WAIT {
        std::thread::sleep(POLL);
        let game: Vec<u32> = super::session::game_processes(&mut system, root.map(Pid::from_u32), &dir, since).iter().map(|p| p.as_u32()).collect();
        // SAFETY: no arguments.
        let front = unsafe { GetForegroundWindow() };
        let owner = process_of(front);
        if game.contains(&owner) && !console(front) {
            if kept.get_or_insert_with(Instant::now).elapsed() >= KEPT {
                return;
            }
            continue;
        }
        kept = None;
        // Another program in front: the player went there.
        if owner != me && !(game.contains(&owner) && console(front)) {
            return;
        }
        if let Some(&window) = windows().iter().find(|&&w| game.contains(&process_of(w))) {
            // SAFETY: a window handle EnumWindows gave.
            unsafe {
                if IsIconic(window) != 0 {
                    ShowWindow(window, SW_RESTORE);
                }
                SetForegroundWindow(window);
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn this_tests_own_process_shows_no_window() {
        let me = std::process::id();
        assert!(!windows().iter().any(|&w| process_of(w) == me));
    }
}
