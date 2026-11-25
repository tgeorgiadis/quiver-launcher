//! What a game says as it fails. Its output goes to a log of its own (game-logs/ in the data folder) instead of
//! nowhere, and a game that closes with an error before it really started, or crashes, is told to the page
//! ("app-failed"), which offers to report it with the end of that log (the page's game-report-prompt.tsx). The game's
//! session itself is session.rs.
use serde::Serialize;
use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::process::{ExitStatus, Stdio};
use std::time::Duration;

/// A log past this is started again: a game that writes all the time mustn't fill the disk. Only while the launcher
/// runs (`keep_small`): a game left running after it quits writes on until the launcher is next started.
const MAX_LOG: u64 = 8 * 1024 * 1024;
/// How often the logs are measured.
const MEASURE: Duration = Duration::from_secs(30);
/// How much of a log's end a report takes.
const TAIL: u64 = 16 * 1024;

/// Where a game's output goes: `game-logs/<app>.log` in the data folder, started afresh at each launch.
pub struct GameLog {
    path: Option<PathBuf>,
    file: Option<File>,
}

fn logs_dir(data_dir: &Path) -> PathBuf {
    data_dir.join("game-logs")
}

/// An app's log: named for its id (its install's folder, for one started without an id), with nothing a file's name
/// can't have. The id rather than the folder, which two apps the player added can share.
fn path_for(data_dir: &Path, app: &str) -> PathBuf {
    let name: String = app.chars().map(|c| if c.is_alphanumeric() || "-_. ".contains(c) { c } else { '_' }).collect();
    logs_dir(data_dir).join(format!("{}.log", name.trim_start_matches('.')))
}

impl GameLog {
    /// `fresh`: emptied first. Not for an app that's already running, which is still writing to it.
    pub fn new(data_dir: &Path, app: &str, fresh: bool) -> GameLog {
        let path = path_for(data_dir, app);
        // Only ever added to at its end: the game and `keep_small` share it, and a log cut short is written on from
        // its new end rather than from where the game had got to.
        let file = path
            .parent()
            .and_then(|dir| std::fs::create_dir_all(dir).ok())
            .and_then(|_| if fresh { File::create(&path).map(|_| ()).ok() } else { Some(()) })
            .and_then(|_| std::fs::OpenOptions::new().create(true).append(true).open(&path).ok());
        GameLog { path: file.as_ref().map(|_| path), file }
    }

    /// The log for stdout or stderr; nowhere if it couldn't be made.
    pub fn stdio(&self) -> Stdio {
        self.file.as_ref().and_then(|f| f.try_clone().ok()).map_or_else(Stdio::null, Stdio::from)
    }
}

/// Starts any log again once it passes `MAX_LOG`, for as long as the launcher runs: those of games it started, and
/// of games an earlier launcher left running.
pub fn keep_small(data_dir: &Path) {
    let dir = logs_dir(data_dir);
    let _ = std::thread::Builder::new().name("game-logs".into()).spawn(move || loop {
        trim_all(&dir, MAX_LOG);
        std::thread::sleep(MEASURE);
    });
}

fn trim_all(dir: &Path, max: u64) {
    for entry in std::fs::read_dir(dir).into_iter().flatten().flatten() {
        if entry.path().extension().is_some_and(|e| e == "log") {
            trim(&entry.path(), max);
        }
    }
}

/// Empties a log longer than `max`; whether it did.
fn trim(path: &Path, max: u64) -> bool {
    let long = std::fs::metadata(path).is_ok_and(|m| m.len() > max);
    long && std::fs::OpenOptions::new().write(true).open(path).and_then(|f| f.set_len(0)).is_ok()
}

/// A game the launcher started went wrong, for the page.
#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct Failure {
    /// The app's id, when the launcher started it for one.
    id: Option<String>,
    /// The install's folder name, as the page knows it.
    folder: String,
    /// "early": it closed with an error right after starting. "crashed": the system ended it, whenever that was.
    kind: &'static str,
    /// What it closed with: its exit code, or on Windows the crash's status.
    code: Option<i32>,
    /// What kind of crash, in a few plain English words for the report ("segmentation fault").
    what: Option<&'static str>,
    /// The last line of its output that says what went wrong.
    line: Option<String>,
}

#[derive(Debug, PartialEq)]
enum Problem {
    /// Closed with an error before it really started (no stub handing over).
    ClosedEarly(i32),
    /// Ended by a crash, whenever it came: what kind, and on Windows its status.
    Crashed(&'static str, Option<i32>),
}

/// What to tell the page about a game that ended (`ended`, session.rs); nothing for one that just closed, one the
/// player closed from the controller, or one whose end couldn't be seen.
pub fn failure(ended: &crate::session::Ended, id: Option<String>, folder: &str, log: &GameLog) -> Option<Failure> {
    if ended.closed_here {
        return None;
    }
    let problem = problem(ended.status?, ended.early)?;
    let line = log.path.as_deref().and_then(|path| telling_line(&tail(path, TAIL)));
    let (kind, code, what) = match problem {
        Problem::ClosedEarly(code) => ("early", Some(code), None),
        Problem::Crashed(what, code) => ("crashed", code, Some(what)),
    };
    Some(Failure { id, folder: folder.to_string(), kind, code, what, line })
}

/// What went wrong, if anything: `early` is an exit within the stub time with nothing left running. Signals that
/// stop a game on purpose aren't problems.
fn problem(status: ExitStatus, early: bool) -> Option<Problem> {
    #[cfg(unix)]
    {
        use std::os::unix::process::ExitStatusExt;
        if let Some(signal) = status.signal() {
            return crash_signal(signal).map(|what| Problem::Crashed(what, None));
        }
    }
    match status.code() {
        Some(code) if cfg!(windows) && crash_status(code).is_some() => crash_status(code).map(|what| Problem::Crashed(what, Some(code))),
        Some(code) if code != 0 && early => Some(Problem::ClosedEarly(code)),
        _ => None,
    }
}

#[cfg(unix)]
fn crash_signal(signal: i32) -> Option<&'static str> {
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

/// The statuses Windows ends a crashed program with, which come back as its exit code. Not the ones for a program
/// closed on purpose (Ctrl+C, its console closed).
fn crash_status(code: i32) -> Option<&'static str> {
    match code as u32 {
        0xC000_0005 => Some("access violation"),
        0xC000_001D => Some("illegal instruction"),
        0xC000_0094 => Some("integer division by zero"),
        0xC000_00FD => Some("stack overflow"),
        0xC000_0135 => Some("a DLL it needs wasn't found"),
        0xC000_0139 => Some("a DLL it needs is the wrong version"),
        0xC000_0374 => Some("heap corruption"),
        0xC000_0409 => Some("it aborted"),
        _ => None,
    }
}

/// The end of an app's log (by its id), for a report; empty when there's none. Colour codes removed.
#[tauri::command]
pub fn game_log(data: tauri::State<crate::Data>, app: String) -> String {
    tail(&path_for(&data.0, &app), TAIL).lines().map(plain).collect::<Vec<_>>().join("\n").trim().to_string()
}

/// The end of the log, at most `max` bytes, from the start of a line when it was cut.
fn tail(path: &Path, max: u64) -> String {
    let mut out = Vec::new();
    let mut cut = false;
    if let Ok(mut file) = File::open(path) {
        let size = file.metadata().map(|m| m.len()).unwrap_or(0);
        cut = size > max;
        let _ = file.seek(SeekFrom::Start(size.saturating_sub(max)));
        let _ = file.read_to_end(&mut out);
    }
    let text = String::from_utf8_lossy(&out);
    match text.find('\n') {
        Some(line) if cut => text[line + 1..].to_string(),
        _ => text.into_owned(),
    }
}

/// The last line that says what went wrong, else the last line; colour codes removed.
fn telling_line(output: &str) -> Option<String> {
    const TELLING: [&str; 15] = [
        "error", "fail", "usage", "not found", "missing", "cannot", "can't", "couldn't", "unable", "denied", "invalid", "no such", "exception",
        "panicked", "fatal",
    ];
    let lines: Vec<String> = output.lines().map(plain).filter(|l| !l.is_empty()).collect();
    let line = lines.iter().rev().find(|l| TELLING.iter().any(|w| l.to_lowercase().contains(w))).or(lines.last())?;
    let mut short: String = line.chars().take(200).collect();
    if line.chars().count() > 200 {
        short.push('…');
    }
    Some(short)
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::session::Ended;

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
        assert_eq!(problem(signalled(11), false), Some(Problem::Crashed("segmentation fault", None)));
        assert_eq!(problem(signalled(15), true), None);
        assert_eq!(problem(signalled(9), true), None);
    }

    #[test]
    fn windows_crash_statuses_are_crashes_and_a_program_closed_on_purpose_is_not() {
        assert_eq!(crash_status(0xC000_0005_u32 as i32), Some("access violation"));
        assert_eq!(crash_status(0xC000_0135_u32 as i32), Some("a DLL it needs wasn't found"));
        // Ctrl+C, or its console window closed.
        assert_eq!(crash_status(0xC000_013A_u32 as i32), None);
        assert_eq!(crash_status(1), None);
    }

    #[cfg(unix)]
    #[test]
    fn a_game_that_fails_is_told_with_its_last_message_and_one_the_player_closed_is_not() {
        use std::os::unix::process::ExitStatusExt;
        let data = tempfile::tempdir().unwrap();
        let log = GameLog::new(data.path(), "entry_test-port", true);
        // The game's own output, as its process would write it.
        let mut game = std::process::Command::new("sh").args(["-c", "echo Loading; echo 'usage: port ROM' >&2; exit 2"]).stdout(log.stdio()).stderr(log.stdio()).spawn().unwrap();
        let status = game.wait().unwrap();
        let ended = |closed_here| Ended { seconds: Some(1), status: Some(status), early: true, closed_here };
        assert_eq!(
            failure(&ended(false), Some("app".into()), "Test Port", &log),
            Some(Failure { id: Some("app".into()), folder: "Test Port".into(), kind: "early", code: Some(2), what: None, line: Some("usage: port ROM".into()) })
        );
        assert_eq!(failure(&ended(true), None, "Test Port", &log), None);
        let quit = Ended { seconds: Some(40), status: Some(ExitStatus::from_raw(0)), early: false, closed_here: false };
        assert_eq!(failure(&quit, None, "Test Port", &log), None);
        assert_eq!(tail(&path_for(data.path(), "entry_test-port"), TAIL), "Loading\nusage: port ROM\n");
    }

    #[test]
    fn a_log_is_named_for_its_app_and_started_again_at_each_launch_and_when_it_grows_too_long() {
        use std::io::Write;
        let data = tempfile::tempdir().unwrap();
        let path = path_for(data.path(), "..\\local: One/Two");
        assert_eq!(path, data.path().join("game-logs").join("_local_ One_Two.log"));
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, "from the last launch").unwrap();
        // Started while the app is already running: what it wrote stays.
        drop(GameLog::new(data.path(), "..\\local: One/Two", false));
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "from the last launch");
        let log = GameLog::new(data.path(), "..\\local: One/Two", true);
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "");
        let mut out = log.file.as_ref().unwrap().try_clone().unwrap();
        out.write_all(b"0123456789").unwrap();
        trim_all(&logs_dir(data.path()), 10);
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "0123456789");
        trim_all(&logs_dir(data.path()), 9);
        // Written on from its new end, not from where the game had got to.
        out.write_all(b"more").unwrap();
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "more");
    }

    #[test]
    fn the_end_of_a_long_log_starts_at_a_line_and_a_long_last_message_is_cut_with_a_mark() {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.path().join("x.log");
        std::fs::write(&path, "first line\nsecond line\nthird\n").unwrap();
        assert_eq!(tail(&path, 14), "third\n");
        assert_eq!(tail(&path, 1000), "first line\nsecond line\nthird\n");
        assert_eq!(telling_line(&"e".repeat(200)), Some("e".repeat(200)));
        assert_eq!(telling_line(&"e".repeat(201)), Some(format!("{}…", "e".repeat(200))));
    }
}
