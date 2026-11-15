//! Game sessions: every app the launcher starts (lib.rs `start`), until it ends,
//! for its play time and so TV mode can come back in front and listen again.
//!
//! The game's own process is waited on. A launcher stub (one that starts the
//! real game and exits within a few seconds) hands over to processes running
//! from the game's folder, which are then watched until none is left. Those are
//! programs inside the folder, and ones started since the game that work in it
//! (Bloodborne's launch_gui.bat starts python.exe there).
//!
//! A game that closes with an error before it really started, or crashes, is
//! reported ("app-failed") with the last telling line of its output, which
//! goes to a log in the data folder instead of nowhere.
//!
//! A game with no way to quit can be closed from the controller (gamepad.rs:
//! hold Start + Select): it and everything it started are asked to quit, then forced.
use serde::Serialize;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::process::{Child, ExitStatus, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use sysinfo::{Pid, ProcessRefreshKind, ProcessesToUpdate, Signal, System, UpdateKind};
use tauri::{AppHandle, Emitter};

/// A newer game supersedes an older one's tracking.
static LATEST: AtomicU64 = AtomicU64::new(0);

/// A process that exits sooner than this was a launcher stub.
const STUB_TIME: Duration = Duration::from_secs(10);
const POLL: Duration = Duration::from_secs(2);

/// The game being played: its session, process (unknown for one Windows started), folder and start (Unix seconds).
static PLAYING: Mutex<Option<(u64, Option<u32>, PathBuf, u64)>> = Mutex::new(None);
/// The launcher's setting "Close a game with Start + Select" (Settings → Controls, and TV mode's Settings).
pub static CLOSE_COMBO: AtomicBool = AtomicBool::new(true);
/// How long a game has to quit when asked, before it's forced.
const GRACE: Duration = Duration::from_secs(3);

/// Where a game's output goes: `game-logs/<folder>.log` in the data folder,
/// started afresh at each launch.
pub struct GameLog {
    path: Option<PathBuf>,
    file: Option<File>,
}

impl GameLog {
    pub fn new(data_dir: &Path, folder: &str) -> GameLog {
        let name: String = folder.chars().map(|c| if c.is_alphanumeric() || "-_. ".contains(c) { c } else { '_' }).collect();
        let path = data_dir.join("game-logs").join(format!("{}.log", name.trim_start_matches('.')));
        let file = path.parent().and_then(|dir| std::fs::create_dir_all(dir).ok()).and_then(|_| File::create(&path).ok());
        GameLog { path: file.as_ref().map(|_| path), file }
    }

    /// The log for stdout or stderr; nowhere if it couldn't be made.
    pub fn stdio(&self) -> Stdio {
        self.file.as_ref().and_then(|f| f.try_clone().ok()).map_or_else(Stdio::null, Stdio::from)
    }
}

#[derive(Clone, Serialize)]
struct Failure {
    /// The app's id, when the launcher started it for one.
    id: Option<String>,
    /// The install's folder name, as the page knows it.
    folder: String,
    message: String,
    /// Closed right after starting, rather than crashing.
    early: bool,
    /// It said it needs the player's own copy of the game (romneed.rs).
    rom: Option<super::romneed::RomNeed>,
}

/// A game the launcher started: its process, or one Windows started for it (with administrator rights, elevate.rs),
/// which can only be waited on.
#[cfg_attr(not(windows), allow(dead_code))]
pub enum Started {
    Child(Child),
    Elsewhere(crate::Waiting),
}

/// Called with each game the launcher starts; `ended` is told how long it ran, a launcher's hand-over included, or
/// nothing when it couldn't be waited on.
pub fn track(app: &AppHandle, game: Started, dir: PathBuf, id: Option<String>, folder: String, log: GameLog, ended: impl FnOnce(Option<u64>) + Send + 'static) {
    let session = LATEST.fetch_add(1, Ordering::SeqCst) + 1;
    // A second early, as process start times are in whole seconds.
    let since = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_secs()).saturating_sub(1);
    let root = match &game {
        Started::Child(child) => Some(child.id()),
        Started::Elsewhere(_) => None,
    };
    *PLAYING.lock().unwrap() = Some((session, root, dir.clone(), since));
    if let Some(id) = &id {
        let _ = app.emit("app-started", id);
    }
    #[cfg(windows)]
    {
        let dir = dir.clone();
        let _ = std::thread::Builder::new().name("game-to-front".into()).spawn(move || super::front::bring_forward(root, dir, since));
    }
    let app = app.clone();
    std::thread::Builder::new()
        .name("tv-session".into())
        .spawn(move || {
            let started = Instant::now();
            let status = match game {
                Started::Child(mut child) => child.wait().ok(),
                // Windows started it: whether it ended, not how.
                Started::Elsewhere(wait) => wait().then(ExitStatus::default),
            };
            let quick = started.elapsed() < STUB_TIME;
            let mut handed_over = false;
            if quick {
                let mut system = System::new();
                while game_running(&mut system, &dir, since) {
                    handed_over = true;
                    std::thread::sleep(POLL);
                }
            }
            if let Some(problem) = status.and_then(|s| problem(s, quick && !handed_over)) {
                let early = matches!(problem, Problem::ClosedEarly(_));
                let rom = if early { log.path.as_deref().and_then(|path| super::romneed::from_output(&tail(path), &dir)) } else { None };
                let message = match &rom {
                    Some(need) => super::romneed::message(need, log.path.as_deref()),
                    None => message(&problem, log.path.as_deref()),
                };
                    let _ = app.emit("app-failed", Failure { id, folder, message, early, rom });
            }
            if LATEST.load(Ordering::SeqCst) == session {
                PLAYING.lock().unwrap().take_if(|(s, _, _, _)| *s == session);
            }
            ended(status.map(|_| started.elapsed().as_secs()));
        })
        .ok();
}

#[derive(Debug, PartialEq)]
enum Problem {
    /// Closed with an error before it really started (no stub handing over).
    ClosedEarly(i32),
    /// Ended by a crash signal, whenever it came (Unix only).
    #[cfg_attr(not(unix), allow(dead_code))]
    Crashed(&'static str),
}

/// What went wrong, if anything: `early` is an exit within the stub time with
/// nothing left running. Signals that stop a game on purpose aren't problems.
fn problem(status: ExitStatus, early: bool) -> Option<Problem> {
    #[cfg(unix)]
    {
        use std::os::unix::process::ExitStatusExt;
        if let Some(signal) = status.signal() {
            return crash_name(signal).map(Problem::Crashed);
        }
    }
    match status.code() {
        Some(code) if code != 0 && early => Some(Problem::ClosedEarly(code)),
        _ => None,
    }
}

#[cfg(unix)]
fn crash_name(signal: i32) -> Option<&'static str> {
    // Linux numbering (SIGBUS differs on macOS, so it's left out there).
    match signal {
        4 => Some("illegal instruction"),
        6 => Some("it aborted"),
        7 if cfg!(target_os = "linux") => Some("bus error"),
        8 => Some("arithmetic error"),
        11 => Some("segmentation fault"),
        _ => None,
    }
}

fn message(problem: &Problem, log: Option<&Path>) -> String {
    let mut text = match problem {
        Problem::ClosedEarly(code) => format!("It closed right after starting (exit code {code})."),
        Problem::Crashed(what) => format!("It crashed ({what})."),
    };
    if let Some(line) = log.and_then(|path| telling_line(&tail(path))) {
        text.push_str(&format!(" Its last message: \u{201c}{line}\u{201d}"));
    }
    if let Some(path) = log {
        text.push_str(&format!(" Full log: {}", path.display()));
    }
    text
}

/// The end of the log, at most 16 KB.
fn tail(path: &Path) -> String {
    let mut out = Vec::new();
    if let Ok(mut file) = File::open(path) {
        let size = file.metadata().map(|m| m.len()).unwrap_or(0);
        let _ = file.seek(SeekFrom::Start(size.saturating_sub(16 * 1024)));
        let _ = file.read_to_end(&mut out);
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// The last line that says what went wrong, else the last line; colour codes removed.
fn telling_line(output: &str) -> Option<String> {
    const TELLING: [&str; 15] = [
        "error", "fail", "usage", "not found", "missing", "cannot", "can't", "couldn't", "unable", "denied", "invalid", "no such", "exception",
        "panicked", "fatal",
    ];
    let lines: Vec<String> = output.lines().map(plain).filter(|l| !l.is_empty()).collect();
    let line = lines.iter().rev().find(|l| TELLING.iter().any(|w| l.to_lowercase().contains(w))).or(lines.last())?;
    let mut line: String = line.chars().take(200).collect();
    if line.chars().count() == 200 {
        line.push('…');
    }
    Some(line)
}

/// A line without terminal colour codes (ESC [ … letter), trimmed.
fn plain(line: &str) -> String {
    let mut out = String::new();
    let mut chars = line.chars();
    while let Some(c) = chars.next() {
        if c == '\u{1b}' {
            if chars.next() == Some('[') {
                for c in chars.by_ref() {
                    if c.is_ascii_alphabetic() {
                        break;
                    }
                }
            }
        } else {
            out.push(c);
        }
    }
    out.trim().to_string()
}

/// The launcher's setting "Mouse pointer with Select" (pointer.rs, Linux).
pub static POINTER: AtomicBool = AtomicBool::new(true);

#[tauri::command]
pub fn select_pointer(on: bool) {
    POINTER.store(on, Ordering::Relaxed);
}

/// Whether a game the launcher started is running.
pub fn playing() -> bool {
    PLAYING.lock().unwrap().is_some()
}

/// Whether the controllers are worth reading while a game is in front: for Start + Select, or the pointer.
pub fn watching() -> bool {
    playing() && (CLOSE_COMBO.load(Ordering::Relaxed) || (cfg!(target_os = "linux") && POINTER.load(Ordering::Relaxed)))
}

/// The launcher's setting: whether holding Start + Select closes the game being played.
#[tauri::command]
pub fn close_combo(on: bool) {
    CLOSE_COMBO.store(on, Ordering::Relaxed);
}

/// Closes the game being played and everything it started (in the background);
/// false when none is.
pub fn close_game(log: &Path) -> bool {
    let Some((_, pid, dir, since)) = PLAYING.lock().unwrap().clone() else { return false };
    let log = log.to_path_buf();
    std::thread::Builder::new()
        .name("tv-close-game".into())
        .spawn(move || {
            let outcome = close_tree(pid.map(Pid::from_u32), &dir, since, GRACE);
            let pid = pid.map_or("unknown".into(), |pid| pid.to_string());
            crate::append_log(&log, &format!("Closing the game (process {pid}, {}): {outcome}", dir.display()));
        })
        .is_ok()
}

/// Asks the game's processes to quit, then forces whichever are left after `grace`; says how it went.
fn close_tree(root: Option<Pid>, dir: &Path, since: u64, grace: Duration) -> String {
    let mut system = System::new();
    let targets = game_processes(&mut system, root, dir, since);
    for pid in &targets {
        if let Some(process) = system.process(*pid) {
            // Windows has no "please quit" signal for another program: it's forced there.
            if process.kill_with(Signal::Term).is_none() {
                process.kill();
            }
        }
    }
    let started = Instant::now();
    while started.elapsed() < grace {
        std::thread::sleep(Duration::from_millis(100));
        system.refresh_processes(ProcessesToUpdate::Some(&targets), true);
        if targets.iter().all(|pid| system.process(*pid).is_none()) {
            return format!("{} processes quit when asked", targets.len());
        }
    }
    let forced: Vec<String> = targets
        .iter()
        .filter_map(|pid| system.process(*pid).filter(|p| p.kill()).map(|p| format!("{pid} {}", p.name().to_string_lossy())))
        .collect();
    format!("{} processes, {} forced: {}", targets.len(), forced.len(), forced.join(", "))
}

/// The game's process, everything it started, and anything of the game's (`of_game`).
pub(super) fn game_processes(system: &mut System, root: Option<Pid>, dir: &Path, since: u64) -> Vec<Pid> {
    refresh_for_game(system);
    let me = Pid::from_u32(std::process::id());
    let mut found: Vec<Pid> = root.into_iter().collect();
    loop {
        let more: Vec<Pid> = system
            .processes()
            .iter()
            .filter(|(pid, p)| !found.contains(pid) && p.parent().is_some_and(|parent| found.contains(&parent)))
            .map(|(pid, _)| *pid)
            .collect();
        if more.is_empty() {
            break;
        }
        found.extend(more);
    }
    for (pid, p) in system.processes() {
        if !found.contains(pid) && of_game(p, dir, since) {
            found.push(*pid);
        }
    }
    found.retain(|pid| *pid != me);
    found
}

fn refresh_for_game(system: &mut System) {
    let kind = ProcessRefreshKind::nothing().with_exe(UpdateKind::OnlyIfNotSet).with_cwd(UpdateKind::OnlyIfNotSet);
    system.refresh_processes_specifics(ProcessesToUpdate::All, true, kind);
}

/// Whether any process of the game's is running (`of_game`).
fn game_running(system: &mut System, dir: &Path, since: u64) -> bool {
    refresh_for_game(system);
    system.processes().values().any(|p| of_game(p, dir, since))
}

/// A program from inside the game's folder, or one started since the game
/// (not a terminal opened there earlier) whose working folder is the game's.
fn of_game(p: &sysinfo::Process, dir: &Path, since: u64) -> bool {
    p.exe().is_some_and(|exe| inside(exe, dir)) || (p.start_time() >= since && p.cwd().is_some_and(|cwd| same(cwd, dir) || inside(cwd, dir)))
}

/// The same folder, give or take a final separator (and case, on Windows).
fn same(a: &Path, b: &Path) -> bool {
    let norm = |p: &Path| {
        let s = p.to_string_lossy();
        let s = s.trim_end_matches(['\\', '/']);
        if cfg!(windows) { s.to_lowercase() } else { s.to_owned() }
    };
    norm(a) == norm(b)
}

/// `exe` is in `dir` or below it. Windows paths compare without regard to case.
fn inside(exe: &Path, dir: &Path) -> bool {
    if cfg!(windows) {
        let (exe, dir) = (exe.to_string_lossy().to_lowercase(), dir.to_string_lossy().to_lowercase());
        let dir = dir.trim_end_matches(['\\', '/']);
        exe.len() > dir.len() && exe.starts_with(dir) && matches!(exe.as_bytes()[dir.len()], b'\\' | b'/')
    } else {
        exe.starts_with(dir)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_program_belongs_to_the_folder_it_runs_from_and_not_to_a_neighbour() {
        let dir = if cfg!(windows) { Path::new(r"C:\Apps\Game") } else { Path::new("/apps/Game") };
        let inner = if cfg!(windows) { Path::new(r"c:\apps\game\bin\game.exe") } else { Path::new("/apps/Game/bin/game") };
        let neighbour = if cfg!(windows) { Path::new(r"C:\Apps\GameTwo\game.exe") } else { Path::new("/apps/GameTwo/game") };
        assert!(inside(inner, dir));
        assert!(!inside(neighbour, dir));
    }

    #[test]
    fn a_folder_is_the_same_with_or_without_its_final_separator() {
        let (dir, slash) = if cfg!(windows) { (Path::new(r"C:\Apps\Game"), Path::new(r"c:\apps\game\")) } else { (Path::new("/apps/Game"), Path::new("/apps/Game/")) };
        assert!(same(dir, slash));
        assert!(!same(dir, Path::new(if cfg!(windows) { r"C:\Apps\GameTwo" } else { "/apps/GameTwo" })));
    }

    #[cfg(windows)]
    #[test]
    fn a_script_that_hands_over_to_a_program_elsewhere_is_still_the_game() {
        // As Bloodborne's launch_gui.bat: `start` a program from outside the folder (python.exe there), working in it, and exit.
        let dir = tempfile::tempdir().unwrap();
        let script = dir.path().join("launch.bat");
        std::fs::write(&script, "@echo off\r\ncd /d \"%~dp0\"\r\nstart \"\" /b ping -n 5 127.0.0.1\r\n").unwrap();
        let since = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_secs() - 1;
        let status = std::process::Command::new(&script).stdout(Stdio::null()).status().unwrap();
        assert!(status.success());
        let mut system = System::new();
        assert!(game_running(&mut system, dir.path(), since), "ping, working in the folder, counts");
        assert!(!game_running(&mut system, dir.path(), u64::MAX), "but not when it was there before the game");
        let started = Instant::now();
        while game_running(&mut system, dir.path(), since) && started.elapsed() < Duration::from_secs(15) {
            std::thread::sleep(Duration::from_millis(200));
        }
        assert!(!game_running(&mut system, dir.path(), since), "and the game ends with it");
    }

    #[cfg(unix)]
    #[test]
    fn closing_a_game_ends_what_it_started_too() {
        // A "game" that starts another process and ignores being asked to quit, so it has to be forced.
        let mut game = std::process::Command::new("sh").args(["-c", "trap '' TERM; sleep 30 & wait"]).spawn().unwrap();
        std::thread::sleep(Duration::from_millis(300));
        let mut system = System::new();
        let root = Pid::from_u32(game.id());
        let started = game_processes(&mut system, Some(root), Path::new("/no/such/folder"), u64::MAX);
        assert!(started.len() >= 2, "{started:?}");
        let outcome = close_tree(Some(root), Path::new("/no/such/folder"), u64::MAX, Duration::from_millis(500));
        assert!(outcome.contains(&format!("{} forced", started.len())), "{outcome}");
        assert!(game.wait().is_ok());
        system.refresh_processes(ProcessesToUpdate::All, true);
        assert!(started.iter().all(|pid| system.process(*pid).is_none_or(|p| p.status() == sysinfo::ProcessStatus::Zombie)), "left running");
    }

    #[test]
    fn the_telling_line_is_the_last_one_that_says_what_went_wrong() {
        let output = "WARNING: radv is not a conformant Vulkan implementation\n\
                      starfox_pc failed: runtime input selection was canceled\n\
                      \u{1b}[33m(zenity:27455): Adwaita-WARNING **: Using GtkSettings\u{1b}[0m\n\n";
        assert_eq!(telling_line(output).as_deref(), Some("starfox_pc failed: runtime input selection was canceled"));
        assert_eq!(telling_line("usage: starfox_asset_builder RETAIL_ROM\n").as_deref(), Some("usage: starfox_asset_builder RETAIL_ROM"));
        assert_eq!(telling_line("Loading…\nBye\n").as_deref(), Some("Bye"));
        assert_eq!(telling_line("\n  \n"), None);
    }

    #[cfg(unix)]
    #[test]
    fn closing_with_an_error_early_or_crashing_is_a_problem_and_quitting_is_not() {
        use std::os::unix::process::ExitStatusExt;
        let exited = |code: i32| ExitStatus::from_raw(code << 8);
        let signalled = ExitStatus::from_raw;
        assert_eq!(problem(exited(2), true), Some(Problem::ClosedEarly(2)));
        assert_eq!(problem(exited(2), false), None);
        assert_eq!(problem(exited(0), true), None);
        assert_eq!(problem(signalled(11), false), Some(Problem::Crashed("segmentation fault")));
        assert_eq!(problem(signalled(15), true), None);
        assert_eq!(problem(signalled(9), true), None);
    }

    #[test]
    fn the_message_says_what_happened_and_where_the_log_is() {
        let dir = tempfile::tempdir().unwrap();
        let log = dir.path().join("Game.log");
        std::fs::write(&log, "usage: tool ROM\n").unwrap();
        let text = message(&Problem::ClosedEarly(2), Some(&log));
        assert!(text.starts_with("It closed right after starting (exit code 2). Its last message: \u{201c}usage: tool ROM\u{201d} Full log: "), "{text}");
        assert_eq!(message(&Problem::Crashed("segmentation fault"), None), "It crashed (segmentation fault).");
    }
}
