//! The port's own setup step (`prepare` in its setup file, port_setup.rs): a run of its program, with no window, that
//! makes what the game needs before it can start, mostly from the player's copy. Play runs it first when it hasn't
//! run for the release installed, when the player's copy changed since, or when a file it leaves (`done`) is gone. It
//! prints `progress 0.4 Unpacking menu art` lines as it goes, and exits with 0 when it worked; anything else is a
//! failed setup, shown with the last lines it printed. Quiver keeps which release it ran for, and from which copies,
//! in `port-prepared.json` in its data folder.
use crate::port_setup::{self, Manifest};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, VecDeque};
use std::io::{BufRead, BufReader, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc;
use std::time::{Duration, Instant};

const RAN_FILE: &str = "port-prepared.json";
/// The step's own temp folder, in the app's folder (on the game's drive): made empty for each run, gone after.
pub const TEMP: &str = ".setup-temp";
/// A step that prints nothing for this long is taken as stuck. A whole build can take hours, printing as it goes.
const STUCK: Duration = Duration::from_secs(60 * 60);
/// What the step needs free on the game's drive to start.
pub const MIN_FREE: u64 = 1 << 30;
/// The lines a failed step shows.
const SHOWN: usize = 10;

/// The step, as the file gives it.
#[derive(Debug, Default, Clone)]
pub struct Step {
    programs: Vec<String>,
    args: Vec<String>,
    env: BTreeMap<String, String>,
    /// What it makes, for the player ("Menu art and fonts from your disc").
    pub label: Option<String>,
    /// It can run again from what it kept of the copy, without the copy.
    keeps_copy: bool,
}

fn strings(v: Option<&Value>) -> Vec<String> {
    match v {
        Some(Value::String(s)) => vec![s.clone()],
        Some(Value::Array(list)) => list.iter().filter_map(|s| s.as_str().map(str::to_string)).collect(),
        _ => Vec::new(),
    }
}

/// The port's setup step, when it has one Quiver can run.
pub fn step(m: &Manifest) -> Option<Step> {
    let p = m.prepare.as_object()?;
    let programs: Vec<String> = strings(p.get("program")).into_iter().filter(|s| !s.trim().is_empty()).collect();
    if programs.is_empty() {
        return None;
    }
    let env = p
        .get("env")
        .and_then(Value::as_object)
        .map(|e| e.iter().filter_map(|(k, v)| Some((k.clone(), v.as_str()?.to_string()))).collect())
        .unwrap_or_default();
    let label = p.get("label").and_then(Value::as_str).map(str::trim).filter(|s| !s.is_empty()).map(|s| s.chars().take(120).collect());
    Some(Step { programs, args: strings(p.get("args")), env, label, keeps_copy: p.get("keeps_copy").and_then(Value::as_bool) == Some(true) })
}

/// The files the step leaves when it has finished (`prepare.done`), as the file writes them.
pub fn done(m: &Manifest) -> Vec<String> {
    strings(m.prepare.get("done"))
}

/// Which system a program the file names is for, by its name.
fn system(name: &str) -> &'static str {
    let lower = name.to_lowercase();
    if lower.ends_with(".exe") || lower.ends_with(".bat") || lower.ends_with(".cmd") {
        "windows"
    } else if lower.ends_with(".app") {
        "macos"
    } else {
        "linux"
    }
}

/// A program the file names, where it is in the app's folder. An AppImage is renamed by downloads and updates, so any
/// AppImage at the top of the folder stands for one the file names; a Mac app runs the program inside it.
fn find(name: &str, app_dir: &Path) -> Option<PathBuf> {
    let relative = name.trim_start_matches("./").replace('\\', "/");
    if relative.split('/').any(|part| part == "..") || Path::new(&relative).is_absolute() {
        return None;
    }
    let path = app_dir.join(&relative);
    if relative.to_lowercase().ends_with(".appimage") && !path.is_file() {
        let mut found: Vec<PathBuf> = std::fs::read_dir(app_dir).ok()?.flatten().map(|e| e.path()).filter(|p| p.is_file() && p.extension().is_some_and(|e| e.eq_ignore_ascii_case("appimage"))).collect();
        found.sort();
        return found.into_iter().next();
    }
    if relative.to_lowercase().ends_with(".app") {
        let inside = path.join("Contents/MacOS");
        let mut found: Vec<PathBuf> = std::fs::read_dir(inside).ok()?.flatten().map(|e| e.path()).filter(|p| p.is_file()).collect();
        found.sort();
        return found.into_iter().next();
    }
    path.is_file().then_some(path)
}

/// The program the step runs on this computer: one for this system, or (on Linux) a Windows one, which runs through
/// Wine or Proton as the game does. `windows`: the game plays its Windows build here, so its Windows program comes first.
pub fn program(step: &Step, app_dir: &Path, windows: bool) -> Option<PathBuf> {
    let os = std::env::consts::OS;
    let wanted = if windows && os == "linux" { "windows" } else { os };
    let pick = |system_wanted: &str| step.programs.iter().filter(|p| system(p) == system_wanted).find_map(|p| find(p, app_dir));
    pick(wanted).or_else(|| match os {
        "linux" if wanted == "windows" => pick("linux"),
        "linux" => pick("windows"),
        _ => None,
    })
}

/// `{input:<id>}` (and the older `{game}`) in `text`: the inputs it names.
fn named_inputs(text: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut rest = text;
    while let Some(open) = rest.find('{') {
        let Some(close) = rest[open..].find('}').map(|c| c + open) else { break };
        let name = &rest[open + 1..close];
        if let Some(id) = name.strip_prefix("input:") {
            out.push(id.to_string());
        } else if name == "game" {
            out.push("game".into());
        }
        rest = &rest[close + 1..];
    }
    out
}

/// Every input the step is given.
fn inputs_used(step: &Step) -> Vec<String> {
    let mut all: Vec<String> = step.args.iter().chain(step.env.values()).flat_map(|t| named_inputs(t)).collect();
    all.sort();
    all.dedup();
    all
}

/// `text` with its inputs, then its places, filled in; None when it names an input the player hasn't given, or a
/// place Quiver can't give.
fn fill(text: &str, app_dir: &Path, inputs: &BTreeMap<String, PathBuf>, windows: bool) -> Option<String> {
    let mut out = text.to_string();
    for id in named_inputs(text) {
        let path = port_setup::for_game(inputs.get(&id)?, windows);
        out = out.replace(&format!("{{input:{id}}}"), &path);
        if id == "game" {
            out = out.replace("{game}", &path);
        }
    }
    port_setup::fill(&out, app_dir, windows)
}

/// The step's arguments and environment. An argument for an input the player hasn't given is left out, with its
/// flag before it ("--patch", "{input:patch}").
pub fn words(step: &Step, app_dir: &Path, inputs: &BTreeMap<String, PathBuf>, windows: bool) -> (Vec<String>, Vec<(String, String)>) {
    let mut args: Vec<String> = Vec::new();
    let mut flag_before = false;
    for arg in &step.args {
        match fill(arg, app_dir, inputs, windows) {
            Some(filled) => {
                flag_before = arg.starts_with('-');
                args.push(filled);
            }
            None => {
                let alone = arg.starts_with('{') && arg.ends_with('}');
                if alone && flag_before && !named_inputs(arg).is_empty() {
                    args.pop();
                }
                flag_before = false;
            }
        }
    }
    let env = step.env.iter().filter(|(name, _)| crate::launch::env_ok(name)).filter_map(|(name, value)| Some((name.clone(), fill(value, app_dir, inputs, windows)?))).collect();
    (args, env)
}

// ---- When it runs ----

#[derive(Serialize, Deserialize, Default, Debug, PartialEq)]
struct Ran {
    /// The release it ran for: the step as the file gives it, and its programs as they are.
    release: String,
    /// The player's copies it ran with.
    copies: String,
}

fn hash(parts: &[String]) -> String {
    let mut h = Sha256::new();
    for p in parts {
        h.update(p.as_bytes());
        h.update([0]);
    }
    hex::encode(&h.finalize()[..12])
}

/// The release installed, as the step sees it: what the file says the step is, and the size and time of each of its
/// programs here. A new release (or an older one put back) changes it; installing the same one again doesn't.
fn release_key(m: &Manifest, step: &Step, app_dir: &Path) -> String {
    let mut parts = vec![m.prepare.to_string()];
    for p in &step.programs {
        if let Some(meta) = find(p, app_dir).and_then(|f| std::fs::metadata(f).ok()) {
            let at = meta.modified().ok().and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok()).map_or(0, |d| d.as_secs());
            parts.push(format!("{p}:{}:{at}", meta.len()));
        }
    }
    hash(&parts)
}

fn copies_key(step: &Step, inputs: &BTreeMap<String, PathBuf>) -> String {
    let used = inputs_used(step);
    hash(&inputs.iter().filter(|(id, _)| used.contains(id)).map(|(id, p)| format!("{id}={}", p.display())).collect::<Vec<_>>())
}

fn all_ran(data_dir: &Path) -> BTreeMap<String, Ran> {
    std::fs::read_to_string(data_dir.join(RAN_FILE)).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or_default()
}

fn key(app_dir: &Path) -> String {
    app_dir.to_string_lossy().into_owned()
}

/// Whether Quiver ran the step for the release installed (from any copies).
pub fn ran(data_dir: &Path, m: &Manifest, app_dir: &Path) -> bool {
    let Some(step) = step(m) else { return false };
    all_ran(data_dir).get(&key(app_dir)).is_some_and(|r| r.release == release_key(m, &step, app_dir))
}

/// The step, when Play should run it before the game starts: it hasn't run for this release, or ran from other copies
/// than the player's now, or a file it leaves is gone. Not when it builds from the player's copy and they gave none
/// (Play without it): it couldn't do its work. One set up another way (its files are all there, and Quiver never ran
/// it) is left as it is.
pub fn needed(data_dir: &Path, m: &Manifest, app_dir: &Path, inputs: &BTreeMap<String, PathBuf>) -> Option<Step> {
    let step = step(m)?;
    let used = inputs_used(&step);
    if used.iter().any(|id| id == "game") && !inputs.contains_key("game") && !step.keeps_copy {
        return None;
    }
    let before = all_ran(data_dir).remove(&key(app_dir));
    let fresh = before.as_ref().is_some_and(|r| r.release == release_key(m, &step, app_dir));
    // Copies the player took away since don't count: one that keeps what it needs builds again without them.
    let same_copies = before.as_ref().is_some_and(|r| r.copies == copies_key(&step, inputs)) || !used.iter().any(|id| inputs.contains_key(id));
    let done = done(m);
    let all_there = !done.is_empty() && done.iter().all(|p| crate::port_config::host_path(p, app_dir, false).is_some_and(|p| p.exists()));
    let run = if done.is_empty() {
        !(fresh && same_copies)
    } else {
        !all_there || (before.is_some() && !(fresh && same_copies))
    };
    run.then_some(step)
}

/// The step worked, for this release and these copies.
pub fn record(data_dir: &Path, m: &Manifest, app_dir: &Path, inputs: &BTreeMap<String, PathBuf>) {
    let Some(step) = step(m) else { return };
    let mut all = all_ran(data_dir);
    all.insert(key(app_dir), Ran { release: release_key(m, &step, app_dir), copies: copies_key(&step, inputs) });
    if let Ok(text) = serde_json::to_string_pretty(&all) {
        let path = data_dir.join(RAN_FILE);
        let tmp = path.with_extension("json.tmp");
        if std::fs::write(&tmp, text).is_ok() {
            let _ = std::fs::rename(&tmp, &path);
        }
    }
}

// ---- Running it ----

/// Where the step has got to.
#[derive(Debug, Clone, PartialEq, Serialize)]
pub struct Progress {
    /// 0 to 1, when it said.
    pub fraction: Option<f64>,
    pub text: String,
}

/// Why the step didn't finish.
#[derive(Debug, Default, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Failed {
    /// Its exit code, when it closed with one.
    pub code: Option<i32>,
    /// The last lines it printed, without its progress lines and colour codes.
    pub lines: Vec<String>,
    /// It printed nothing for an hour, and was stopped.
    pub stuck: bool,
    /// The player stopped it.
    pub stopped: bool,
    /// What Quiver can tell the player to do about it.
    pub hint: Option<String>,
    /// It never started: why.
    pub problem: Option<String>,
}

impl Failed {
    pub fn problem(text: impl Into<String>) -> Failed {
        Failed { problem: Some(text.into()), ..Default::default() }
    }
}

/// A line without its terminal colour codes and the like.
fn plain(line: &str) -> String {
    let mut out = String::new();
    let mut chars = line.chars().peekable();
    while let Some(c) = chars.next() {
        if c == '\u{1b}' {
            if chars.peek() == Some(&'[') {
                chars.next();
                for c in chars.by_ref() {
                    if c.is_ascii_alphabetic() {
                        break;
                    }
                }
            }
            continue;
        }
        if !c.is_control() || c == '\t' {
            out.push(c);
        }
    }
    out.trim_end().to_string()
}

/// A `progress 0.4 Unpacking menu art` line, read.
fn progress(line: &str) -> Option<Progress> {
    let rest = line.trim().strip_prefix("progress")?;
    if !rest.starts_with([' ', '\t']) {
        return None;
    }
    let rest = rest.trim_start();
    let (number, text) = rest.split_once(char::is_whitespace).unwrap_or((rest, ""));
    let fraction = number.parse::<f64>().ok().filter(|f| f.is_finite())?.clamp(0.0, 1.0);
    Some(Progress { fraction: Some(fraction), text: text.trim().chars().take(200).collect() })
}

/// What Quiver can say about a step that failed, from what it printed.
fn hint(lines: &[String]) -> Option<String> {
    let long = lines.iter().any(|l| l.contains("PathTooLongException") || l.to_lowercase().contains("filename or extension is too long"));
    long.then(|| "A path was too long for Windows. Move the app to a library folder with a shorter path, or turn on Windows' long paths setting, then try again.".into())
}

/// Sends each line `from` prints, split at carriage returns too (a line redrawn in place).
fn lines_of(from: impl Read + Send + 'static, to: mpsc::Sender<String>) {
    std::thread::spawn(move || {
        let mut reader = BufReader::new(from);
        let mut buf = Vec::new();
        loop {
            buf.clear();
            match reader.read_until(b'\n', &mut buf) {
                Ok(0) | Err(_) => break,
                Ok(_) => {
                    for part in String::from_utf8_lossy(&buf).split(['\r', '\n']).filter(|p| !p.is_empty()) {
                        if to.send(part.to_string()).is_err() {
                            return;
                        }
                    }
                }
            }
        }
    });
}

/// Runs the step's command to its end, writing all it prints to `log` and telling `on_progress` where it has got to.
/// `stop` set stops it.
pub fn run(mut command: Command, log: &Path, stop: &AtomicBool, mut on_progress: impl FnMut(Progress)) -> Result<(), Failed> {
    run_with(&mut command, log, stop, STUCK, &mut on_progress)
}

fn run_with(command: &mut Command, log: &Path, stop: &AtomicBool, stuck_after: Duration, on_progress: &mut dyn FnMut(Progress)) -> Result<(), Failed> {
    if let Some(dir) = log.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    let mut file = std::fs::File::create(log).ok();
    let mut child = command.stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).spawn().map_err(|e| Failed::problem(format!("Couldn't start its setup step: {e}")))?;
    let (tx, rx) = mpsc::channel();
    if let Some(out) = child.stdout.take() {
        lines_of(out, tx.clone());
    }
    if let Some(err) = child.stderr.take() {
        lines_of(err, tx);
    }
    let mut last: VecDeque<String> = VecDeque::new();
    let mut heard = Instant::now();
    let (mut stuck, mut stopped) = (false, false);
    loop {
        match rx.recv_timeout(Duration::from_millis(250)) {
            Ok(line) => {
                heard = Instant::now();
                if let Some(f) = file.as_mut() {
                    let _ = writeln!(f, "{line}");
                }
                let line = plain(&line);
                if let Some(p) = progress(&line) {
                    on_progress(p);
                } else if !line.trim().is_empty() {
                    last.push_back(line.chars().take(400).collect());
                    if last.len() > SHOWN {
                        last.pop_front();
                    }
                }
            }
            Err(mpsc::RecvTimeoutError::Timeout) => {
                if !stopped && !stuck && stop.load(Ordering::Relaxed) {
                    stopped = true;
                    let _ = child.kill();
                } else if !stopped && !stuck && heard.elapsed() >= stuck_after {
                    stuck = true;
                    let _ = child.kill();
                }
            }
            // Both its outputs closed: it has ended, or left them to a program it started, which goes on its own.
            Err(mpsc::RecvTimeoutError::Disconnected) => break,
        }
    }
    let status = child.wait().map_err(|e| Failed::problem(e.to_string()))?;
    if status.success() && !stopped && !stuck {
        return Ok(());
    }
    let lines: Vec<String> = last.into_iter().collect();
    Err(Failed { code: if stopped || stuck { None } else { status.code() }, hint: hint(&lines), lines, stuck, stopped, problem: None })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn manifest(json: &str) -> Manifest {
        serde_json::from_str(json).unwrap()
    }

    fn inputs(list: &[(&str, &str)]) -> BTreeMap<String, PathBuf> {
        list.iter().map(|(k, v)| (k.to_string(), PathBuf::from(v))).collect()
    }

    #[test]
    fn reads_the_step_and_its_progress_lines() {
        let m = manifest(r#"{"format":1,"prepare":{"program":"game.exe","args":["--prepare"],"label":" Menu art ","keeps_copy":true}}"#);
        let s = step(&m).unwrap();
        assert_eq!(s.programs, ["game.exe"]);
        assert_eq!(s.label.as_deref(), Some("Menu art"));
        assert!(s.keeps_copy);
        assert!(step(&manifest(r#"{"format":1,"prepare":{"args":["x"]}}"#)).is_none());
        assert!(step(&manifest(r#"{"format":1}"#)).is_none());
        assert_eq!(progress("progress 0.4 Unpacking menu art"), Some(Progress { fraction: Some(0.4), text: "Unpacking menu art".into() }));
        assert_eq!(progress("progress 1.5"), Some(Progress { fraction: Some(1.0), text: String::new() }));
        assert_eq!(progress("progressing nicely"), None);
        assert_eq!(progress("progress soon"), None);
        assert_eq!(plain("\u{1b}[31mMissing: the Vulkan SDK\u{1b}[0m  "), "Missing: the Vulkan SDK");
    }

    #[test]
    fn fills_in_the_players_copies_and_leaves_out_ones_not_given() {
        let s = step(&manifest(
            r#"{"format":1,"prepare":{"program":"g.exe","args":["--prepare","--game","{input:game}","--patch","{input:patch}","--out={install}/build","{game}"],"env":{"GAME_SRC":"{input:game}","EXTRA":"{input:dlc}","PATH":"x"}}}"#,
        ))
        .unwrap();
        let app = Path::new("/games/G");
        let (args, env) = words(&s, app, &inputs(&[("game", "/roms/g.iso")]), false);
        assert_eq!(args, ["--prepare", "--game", "/roms/g.iso", "--out=/games/G/build", "/roms/g.iso"]);
        assert_eq!(env, [("GAME_SRC".to_string(), "/roms/g.iso".to_string())]);
        let (args, _) = words(&s, app, &inputs(&[("game", "/roms/g.iso")]), true);
        assert_eq!(args[2], "Z:\\roms\\g.iso");
        assert_eq!(args[3], "--out=Z:\\games\\G/build");
    }

    #[test]
    fn picks_a_program_for_this_system_and_finds_a_renamed_appimage() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        std::fs::write(app.join("Game.exe"), "").unwrap();
        std::fs::write(app.join("Game-1.2-x86_64.AppImage"), "").unwrap();
        let s = step(&manifest(r#"{"format":1,"prepare":{"program":["Game.exe","Game-x86_64.AppImage","../escape"]}}"#)).unwrap();
        let picked = program(&s, app, false).unwrap();
        match std::env::consts::OS {
            "linux" => {
                assert_eq!(picked, app.join("Game-1.2-x86_64.AppImage"));
                assert_eq!(program(&s, app, true).unwrap(), app.join("Game.exe"));
            }
            "windows" => assert_eq!(picked, app.join("Game.exe")),
            _ => {}
        }
        assert!(find("../escape", app).is_none());
    }

    #[test]
    fn runs_for_a_new_release_new_copies_or_a_missing_file_and_not_otherwise() {
        let data = tempfile::tempdir().unwrap();
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        std::fs::write(app.join("game.exe"), "v1").unwrap();
        let m = manifest(r#"{"format":1,"prepare":{"program":"game.exe","args":["--game={input:game}"]}}"#);
        let iso = inputs(&[("game", "/roms/a.iso")]);
        // Not without the copy it builds from.
        assert!(needed(data.path(), &m, app, &BTreeMap::new()).is_none());
        assert!(needed(data.path(), &m, app, &iso).is_some());
        assert!(!ran(data.path(), &m, app));
        record(data.path(), &m, app, &iso);
        assert!(ran(data.path(), &m, app));
        assert!(needed(data.path(), &m, app, &iso).is_none());
        assert!(needed(data.path(), &m, app, &inputs(&[("game", "/roms/b.iso")])).is_some());
        // A new release.
        std::fs::write(app.join("game.exe"), "v2 is bigger").unwrap();
        assert!(!ran(data.path(), &m, app));
        assert!(needed(data.path(), &m, app, &iso).is_some());

        // With `done`: what it leaves says, and one set up another way is left alone.
        let other = tempfile::tempdir().unwrap();
        let app = other.path();
        std::fs::write(app.join("game.exe"), "v1").unwrap();
        let m = manifest(r#"{"format":1,"prepare":{"program":"game.exe","args":["--game={input:game}"],"done":["{install}/build/game.pak"],"keeps_copy":true}}"#);
        assert!(needed(data.path(), &m, app, &BTreeMap::new()).is_some());
        std::fs::create_dir_all(app.join("build")).unwrap();
        std::fs::write(app.join("build/game.pak"), "").unwrap();
        assert!(needed(data.path(), &m, app, &iso).is_none());
        record(data.path(), &m, app, &iso);
        std::fs::write(app.join("game.exe"), "v2 is bigger").unwrap();
        // Built again from what it kept, without the copy.
        assert!(needed(data.path(), &m, app, &BTreeMap::new()).is_some());
    }

    #[cfg(unix)]
    #[test]
    fn a_run_reports_progress_and_on_failure_its_last_lines() {
        let dir = tempfile::tempdir().unwrap();
        let log = dir.path().join("logs/setup.log");
        let stop = AtomicBool::new(false);
        let mut seen = Vec::new();
        let mut ok = Command::new("sh");
        ok.args(["-c", "echo 'progress 0.5 Half way'; echo working; echo 'progress 1 Done' >&2"]);
        assert_eq!(run_with(&mut ok, &log, &stop, STUCK, &mut |p| seen.push(p.text)), Ok(()));
        assert_eq!(seen.len(), 2);
        assert!(std::fs::read_to_string(&log).unwrap().contains("working"));
        let mut bad = Command::new("sh");
        bad.args(["-c", "for i in $(seq 1 15); do echo line $i; done; printf '\\033[31mMissing: the SDK\\033[0m\\n'; exit 3"]);
        let failed = run_with(&mut bad, &log, &stop, STUCK, &mut |_| {}).unwrap_err();
        assert_eq!(failed.code, Some(3));
        assert_eq!(failed.lines.len(), SHOWN);
        assert_eq!(failed.lines.last().unwrap(), "Missing: the SDK");
        let mut quiet = Command::new("sh");
        quiet.args(["-c", "exec sleep 5"]);
        let failed = run_with(&mut quiet, &log, &stop, Duration::from_millis(300), &mut |_| {}).unwrap_err();
        assert!(failed.stuck);
    }
}
