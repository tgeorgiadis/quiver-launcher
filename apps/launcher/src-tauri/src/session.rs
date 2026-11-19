//! Game sessions: every app the launcher starts (lib.rs `start`), until it ends,
//! for its play time and so a controller can close it.
//!
//! The game's own process is waited on. A launcher stub (one that starts the
//! real game and exits within a few seconds) hands over to processes running
//! from the game's folder, which are then watched until none is left. Those are
//! programs inside the folder, and ones started since the game that work in it
//! (a .bat that starts python.exe there, say).
//!
//! A game with no way to quit can be closed from the controller (gamepad.rs:
//! hold Start + Select): it and everything it started are asked to quit, then forced.
use std::path::{Path, PathBuf};
use std::process::Child;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use sysinfo::{Pid, ProcessRefreshKind, ProcessesToUpdate, Signal, System, UpdateKind};

/// A newer game supersedes an older one's tracking.
static LATEST: AtomicU64 = AtomicU64::new(0);

/// A process that exits sooner than this was a launcher stub.
const STUB_TIME: Duration = Duration::from_secs(10);
const POLL: Duration = Duration::from_secs(2);

/// The game being played: its session, process (unknown for one Windows started), folder and start (Unix seconds).
static PLAYING: Mutex<Option<(u64, Option<u32>, PathBuf, u64)>> = Mutex::new(None);
/// The launcher's setting "Close a game with Start + Select" (Settings → Controls).
pub static CLOSE_COMBO: AtomicBool = AtomicBool::new(true);
/// How long a game has to quit when asked, before it's forced.
const GRACE: Duration = Duration::from_secs(3);

/// A game the launcher started: its process, or one Windows started for it (with administrator rights, elevate.rs),
/// which can only be waited on.
#[cfg_attr(not(windows), allow(dead_code))]
pub enum Started {
    Child(Child),
    Elsewhere(crate::Waiting),
}

/// Called with each game the launcher starts; `ended` is told how long it ran, a launcher's hand-over included, or
/// nothing when it couldn't be waited on.
pub fn track(game: Started, dir: PathBuf, ended: impl FnOnce(Option<u64>) + Send + 'static) {
    let session = LATEST.fetch_add(1, Ordering::SeqCst) + 1;
    // A second early, as process start times are in whole seconds.
    let since = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_or(0, |d| d.as_secs()).saturating_sub(1);
    let root = match &game {
        Started::Child(child) => Some(child.id()),
        Started::Elsewhere(_) => None,
    };
    *PLAYING.lock().unwrap() = Some((session, root, dir.clone(), since));
    std::thread::Builder::new()
        .name("game-session".into())
        .spawn(move || {
            let started = Instant::now();
            let closed = match game {
                Started::Child(mut child) => child.wait().is_ok(),
                Started::Elsewhere(wait) => wait(),
            };
            if started.elapsed() < STUB_TIME {
                let mut system = System::new();
                while game_running(&mut system, &dir, since) {
                    std::thread::sleep(POLL);
                }
            }
            if LATEST.load(Ordering::SeqCst) == session {
                PLAYING.lock().unwrap().take_if(|(s, _, _, _)| *s == session);
            }
            ended(closed.then(|| started.elapsed().as_secs()));
        })
        .ok();
}

/// Whether the controllers are worth reading while a game is in front: for Start + Select.
pub fn watching() -> bool {
    CLOSE_COMBO.load(Ordering::Relaxed) && PLAYING.lock().unwrap().is_some()
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
        .name("close-game".into())
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
fn game_processes(system: &mut System, root: Option<Pid>, dir: &Path, since: u64) -> Vec<Pid> {
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
    use std::process::Stdio;

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
        // A launch script that `start`s a program from outside the folder, working in it, and exits.
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
        let mut game = std::process::Command::new("sh").args(["-c", "trap '' TERM; sleep 30 & wait"]).stdout(Stdio::null()).spawn().unwrap();
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
}
