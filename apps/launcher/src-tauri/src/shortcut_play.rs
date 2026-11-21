//! Shortcuts that start a game through Quiver, so its saves sync before it starts and are backed up and sent after
//! (docs/cloud-saves.md, "From a shortcut"). A desktop or Steam shortcut runs `quiver --launch <app id>`, which is
//! this file's helper: a small program with no window that
//!
//! 1. finds the Quiver that's open, or starts one with no window (`--shortcut-host`), and asks it for the app;
//! 2. waits while Quiver brings the app's saves in line, as Play does (and asks, if both sides changed);
//! 3. starts the game itself, with the command Quiver gives it, and waits for it;
//! 4. tells Quiver when the game closed, and Quiver backs up and sends the saves.
//!
//! The helper starts the game rather than Quiver because Steam ties its overlay, its controller settings, its
//! "playing" status and its Stop to the program it started and what that starts: a game Quiver started, outside
//! that tree, has none of them, and in Game Mode isn't even shown (tested on Windows and a Steam Deck).
//!
//! Steam's Stop (Windows) and Exit Game (Linux) end the helper and the game without warning on Windows, and two
//! seconds after a SIGTERM on Linux. So:
//! - On Windows, a Quiver the helper starts runs outside Steam's processes (it breaks away from Steam's job), and a
//!   dropped connection counts as the game closing.
//! - On Linux, it runs inside them, where Game Mode shows its window for the question about which saves to keep. On
//!   SIGTERM it backs up at once and hands the upload to a Quiver started outside Steam's processes
//!   (`--upload-saves`, by systemd-run, or `flatpak run` on the host), which sends it and quits.
//!
//! Quiver listens on 127.0.0.1 at a port it writes to `shortcuts.json` in its data folder, with a token only the
//! player's own programs can read there.
#[cfg(target_os = "linux")]
use crate::Watching;
use crate::{Data, Running, SavesOf};
use serde::{Deserialize, Serialize};
use std::io::{BufRead, BufReader, Write};
use std::net::{TcpListener, TcpStream};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use tauri::{AppHandle, Emitter, Manager};

/// The shortcut's argument: `--launch <app id> [-- arguments for the game]`.
pub const LAUNCH: &str = "--launch";
/// Quiver started by a shortcut: no window, and it quits once the game's saves are sent.
pub const HOST: &str = "--shortcut-host";
/// Quiver started to send an app's saves after Steam stopped the game: `--upload-saves <app id> [seconds played]`, the
/// seconds when this session's play time is still to count.
pub const UPLOAD: &str = "--upload-saves";
/// The data folder a Quiver was given (`QUIVER_DATA`), which a shortcut, and the Quivers it starts, are given too.
pub const DATA: &str = "--quiver-data";
const FILE: &str = "shortcuts.json";
/// How long a shortcut waits for a Quiver it started to answer: a first start reads the library and opens the page.
const STARTING: Duration = Duration::from_secs(90);
/// How long a Quiver with no window waits for its page to say the saves are sent before it quits anyway.
const SENDING: Duration = Duration::from_secs(120);
/// On Linux, of the two seconds Steam gives after its SIGTERM, how long the game has to close before its saves are
/// backed up anyway.
#[cfg(target_os = "linux")]
const CLOSING: Duration = Duration::from_millis(1200);

/// Where Quiver listens, in its data folder.
#[derive(Serialize, Deserialize)]
struct Listening {
    port: u16,
    token: String,
}

/// What a shortcut asks Quiver for.
#[derive(Serialize, Deserialize)]
struct Ask {
    token: String,
    app: String,
    /// Arguments the player gave the shortcut after Quiver's own, which go after the game's.
    #[serde(default)]
    args: Vec<String>,
    /// The helper started this Quiver to answer it.
    #[serde(default)]
    hidden: bool,
}

/// The game's command, as Play would start it (lib.rs `target`).
#[derive(Serialize, Deserialize, Debug, PartialEq)]
pub struct Run {
    pub program: PathBuf,
    pub args: Vec<String>,
    pub env: Vec<(String, String)>,
    pub dir: PathBuf,
}

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
enum Reply {
    Start(Run),
    /// Not started: the player said no, or Quiver can't start it (and says why in its window).
    Refuse(String),
}

#[derive(Serialize, Deserialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
enum Report {
    Started,
    Failed(String),
    /// `stopped`: the helper was asked to stop before the game closed (Steam's Exit Game).
    Closed { code: Option<i32>, stopped: bool },
}

/// A shortcut waiting for its page to answer, for `shortcut_requests` and the `shortcut-play` event.
#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Request {
    pub request: u64,
    pub app: String,
    pub hidden: bool,
}

/// How this Quiver was started, for the page (lib.rs `config`).
#[derive(Serialize, Clone, Default)]
#[serde(rename_all = "camelCase")]
pub struct Started {
    /// Started by a shortcut, with no window: it quits once the saves are sent.
    pub host: bool,
    /// Started to send this app's saves, after Steam stopped it: the app, and the seconds it was played when that's
    /// still to count.
    pub upload: Option<(String, Option<u64>)>,
}

/// The data folder named after `--quiver-data`, if one is: Quiver uses it as it does `QUIVER_DATA` (lib.rs run).
pub fn data_arg(args: &[String]) -> Option<PathBuf> {
    let at = args.iter().position(|a| a == DATA)?;
    args.get(at + 1).map(PathBuf::from)
}

/// `--quiver-data` and the folder, when this Quiver was given one: for the shortcuts and Quivers it starts.
fn data_args() -> Vec<String> {
    std::env::var("QUIVER_DATA").ok().filter(|d| !d.is_empty()).map(|d| vec![DATA.to_string(), d]).unwrap_or_default()
}

/// How this Quiver was started, from its arguments.
pub fn started(args: &[String]) -> Started {
    let host = args.iter().any(|a| a == HOST);
    let upload = args.iter().position(|a| a == UPLOAD).and_then(|at| {
        let id = args.get(at + 1)?.clone();
        Some((id, args.get(at + 2).and_then(|s| s.parse().ok())))
    });
    Started { host: host || upload.is_some(), upload }
}

struct Pending {
    app: String,
    hidden: bool,
    args: Vec<String>,
    stream: TcpStream,
}

/// A game a shortcut started, until it closes.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
struct Session {
    id: String,
    saves: Option<SavesOf>,
    started: Instant,
    /// It closed as Steam stopped this Quiver (on_stop): how long it ran.
    ended: Option<u64>,
}

/// Steam is stopping this Quiver (on_stop): the games closing now are left to that, and none reported as closed.
static STOPPING: AtomicBool = AtomicBool::new(false);

/// Quiver's side: the shortcuts waiting, the games they started, and whether to quit when they're done.
#[derive(Default)]
pub struct Host {
    pending: Mutex<Vec<(u64, Pending)>>,
    sessions: Mutex<Vec<(u64, Session)>>,
    next: AtomicU64,
    /// Started by a shortcut with no window: quits when nothing's left to do, unless the player opened its window.
    quits: AtomicBool,
    /// Games closed whose saves the page is still sending.
    sending: AtomicUsize,
    /// Those games, should Steam stop this Quiver before they're sent.
    #[cfg_attr(not(target_os = "linux"), allow(dead_code))]
    closed: Mutex<Vec<(String, Option<SavesOf>)>>,
    token: Mutex<String>,
}

/// A token for the port: 128 bits from the system's random keys, which std's hasher is seeded with.
fn token() -> String {
    use std::hash::{BuildHasher, Hasher};
    let mut out = String::new();
    for _ in 0..2 {
        let mut hasher = std::collections::hash_map::RandomState::new().build_hasher();
        hasher.write_u128(std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos());
        hasher.write_u32(std::process::id());
        out.push_str(&format!("{:016x}", hasher.finish()));
    }
    out
}

fn send(stream: &mut TcpStream, what: &impl Serialize) -> std::io::Result<()> {
    let mut line = serde_json::to_vec(what).map_err(std::io::Error::other)?;
    line.push(b'\n');
    stream.write_all(&line)
}

fn read<T: for<'a> Deserialize<'a>>(reader: &mut impl BufRead) -> std::io::Result<T> {
    let mut line = String::new();
    if reader.read_line(&mut line)? == 0 {
        return Err(std::io::ErrorKind::UnexpectedEof.into());
    }
    serde_json::from_str(&line).map_err(std::io::Error::other)
}

// ---- Quiver's side ----

/// Listens for shortcuts, from setup. `quits`: this Quiver was started by one, with no window.
pub fn listen(app: &AppHandle, data: &Path, quits: bool) {
    let host = app.state::<Host>();
    host.quits.store(quits, Ordering::SeqCst);
    let Ok(listener) = TcpListener::bind(("127.0.0.1", 0)) else { return };
    let Ok(port) = listener.local_addr().map(|a| a.port()) else { return };
    let token = token();
    *host.token.lock().unwrap() = token.clone();
    let file = data.join(FILE);
    if std::fs::write(&file, serde_json::to_vec(&Listening { port, token }).unwrap_or_default()).is_err() {
        return;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = std::fs::set_permissions(&file, std::fs::Permissions::from_mode(0o600));
    }
    let answers = app.clone();
    std::thread::Builder::new()
        .name("shortcuts".into())
        .spawn(move || {
            for stream in listener.incoming().flatten() {
                let app = answers.clone();
                std::thread::spawn(move || asked(&app, stream));
            }
        })
        .ok();
    // One started to send saves is busy until its page says they're sent.
    let uploads = started(&std::env::args().collect::<Vec<_>>()).upload.is_some();
    if uploads {
        host.sending.store(1, Ordering::SeqCst);
    }
    // One started for a shortcut that never asks (it gave up waiting, say) doesn't stay; nor one sending saves whose
    // page never says it's done.
    if quits {
        let app = app.clone();
        std::thread::spawn(move || {
            std::thread::sleep(STARTING);
            quit_if_done(&app);
            if uploads {
                std::thread::sleep(SENDING);
                if !app.get_webview_window("main").is_some_and(|w| w.is_visible().unwrap_or(false)) {
                    app.exit(0);
                }
            }
        });
    }
    #[cfg(target_os = "linux")]
    if quits {
        on_stop(app.clone());
    }
}

/// Removes the port's file as Quiver closes, if it's still this one's.
pub fn stop_listening(app: &AppHandle) {
    let (Some(host), Some(data)) = (app.try_state::<Host>(), app.try_state::<Data>()) else { return };
    let file = data.0.join(FILE);
    let ours = std::fs::read(&file).ok().and_then(|b| serde_json::from_slice::<Listening>(&b).ok()).is_some_and(|l| l.token == *host.token.lock().unwrap());
    if ours {
        let _ = std::fs::remove_file(file);
    }
}

fn asked(app: &AppHandle, stream: TcpStream) {
    let host = app.state::<Host>();
    let Ok(clone) = stream.try_clone() else { return };
    let mut reader = BufReader::new(clone);
    let Ok(ask) = read::<Ask>(&mut reader) else { return };
    if ask.token != *host.token.lock().unwrap() || ask.token.is_empty() {
        return;
    }
    let request = host.next.fetch_add(1, Ordering::SeqCst) + 1;
    let shown = Request { request, app: ask.app.clone(), hidden: ask.hidden };
    host.pending.lock().unwrap().push((request, Pending { app: ask.app, hidden: ask.hidden, args: ask.args, stream }));
    let _ = app.emit("shortcut-play", shown);
}

/// The shortcuts waiting for an answer: for a page that's only now ready, which missed their events.
#[tauri::command]
pub fn shortcut_requests(host: tauri::State<'_, Host>) -> Vec<Request> {
    host.pending.lock().unwrap().iter().map(|(request, p)| Request { request: *request, app: p.app.clone(), hidden: p.hidden }).collect()
}

fn take(host: &Host, request: u64) -> Option<Pending> {
    let mut pending = host.pending.lock().unwrap();
    let at = pending.iter().position(|(r, _)| *r == request)?;
    Some(pending.remove(at).1)
}

/// Doesn't start the app a shortcut asked for: the player said no, or it can't (the page says why).
#[tauri::command]
pub fn shortcut_refuse(app: AppHandle, request: u64, why: String) {
    let host = app.state::<Host>();
    if let Some(mut pending) = take(&host, request) {
        let _ = send(&mut pending.stream, &Reply::Refuse(why));
    }
    quit_if_done(&app);
}

/// Hands a shortcut the app's command, as Play would start it, with the shortcut's own arguments after its own, and
/// follows the game until it closes: its play time, Discord, and `app-closed` with how it ended.
#[tauri::command]
#[allow(clippy::too_many_arguments)]
pub fn shortcut_start(
    app: AppHandle,
    data: tauri::State<'_, Data>,
    request: u64,
    id: String,
    folder: String,
    dir: Option<String>,
    preferred: Vec<String>,
    wine: bool,
    program: Option<String>,
    launch: Option<crate::launch::LaunchOptions>,
    presence: Option<crate::discord::Game>,
    saves: Option<SavesOf>,
) -> Result<(), String> {
    let host = app.state::<Host>();
    let Some(mut pending) = take(&host, request) else { return Err("The shortcut stopped waiting.".into()) };
    let target = match crate::target(&data, &folder, dir, &preferred, wine, program, &launch.unwrap_or_default()) {
        Ok(target) => target,
        Err(e) => {
            let _ = send(&mut pending.stream, &Reply::Refuse(e.clone()));
            return Err(e);
        }
    };
    let (program, mut args, env) = target.command();
    args.extend(pending.args.iter().cloned());
    send(&mut pending.stream, &Reply::Start(Run { program, args, env, dir: target.dir.clone() })).map_err(|e| e.to_string())?;
    let hidden = pending.hidden;
    host.sessions.lock().unwrap().push((request, Session { id: id.clone(), saves, started: Instant::now(), ended: None }));
    let stream = pending.stream;
    std::thread::spawn(move || follow(app, request, id, hidden, stream, presence));
    Ok(())
}

/// The payload of `app-closed` for a game a shortcut started.
#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct Closed {
    id: String,
    seconds: u64,
    /// "shortcut", or "shortcut_hidden" when the shortcut started this Quiver.
    via: &'static str,
    /// "closed" (it quit), or "stopped" (Steam's Stop or Exit Game ended it).
    ended: &'static str,
}

fn follow(app: AppHandle, request: u64, id: String, hidden: bool, stream: TcpStream, presence: Option<crate::discord::Game>) {
    let mut reader = BufReader::new(stream);
    let mut playing = false;
    let ended = loop {
        match read::<Report>(&mut reader) {
            Ok(Report::Started) => {
                playing = true;
                app.state::<Running>().started(&id);
                if let Some(game) = presence.clone() {
                    app.state::<crate::discord::Presence>().started(&id, game);
                }
            }
            Ok(Report::Failed(why)) => {
                crate::append_log(&app.state::<Data>().0.join("quiver.log"), &format!("A shortcut couldn't start {id}: {why}"));
                break None;
            }
            Ok(Report::Closed { stopped, .. }) => break Some(if stopped { "stopped" } else { "closed" }),
            // The helper ended without saying: Steam's Stop took it and the game with it.
            Err(_) => break Some("stopped"),
        }
    };
    let host = app.state::<Host>();
    let session = {
        let mut sessions = host.sessions.lock().unwrap();
        let Some(at) = sessions.iter().position(|(r, _)| *r == request) else { return };
        // Steam is stopping this Quiver: it backs the saves up and hands them on, with the play time (on_stop).
        if STOPPING.load(Ordering::SeqCst) {
            let session = &mut sessions[at].1;
            session.ended = Some(session.started.elapsed().as_secs());
            return;
        }
        sessions.remove(at).1
    };
    if playing {
        app.state::<Running>().closed(&id);
        app.state::<crate::discord::Presence>().closed(&id);
    }
    match (playing, ended) {
        (true, Some(ended)) => {
            let seconds = session.started.elapsed().as_secs();
            host.sending.fetch_add(1, Ordering::SeqCst);
            host.closed.lock().unwrap().push((id.clone(), session.saves));
            let _ = app.emit("app-closed", Closed { id, seconds, via: if hidden { "shortcut_hidden" } else { "shortcut" }, ended });
            // The page says when the saves are sent (shortcut_done); a page that never does doesn't keep it open.
            let app = app.clone();
            std::thread::spawn(move || {
                std::thread::sleep(SENDING);
                let host = app.state::<Host>();
                if host.sending.load(Ordering::SeqCst) > 0 {
                    shortcut_done(app.clone());
                }
            });
        }
        _ => quit_if_done(&app),
    }
}

/// The page sent the saves of a game a shortcut started: a Quiver with no window can quit.
#[tauri::command]
pub fn shortcut_done(app: AppHandle) {
    let host = app.state::<Host>();
    let mut n = host.sending.load(Ordering::SeqCst);
    while n > 0 {
        match host.sending.compare_exchange(n, n - 1, Ordering::SeqCst, Ordering::SeqCst) {
            Ok(_) => break,
            Err(now) => n = now,
        }
    }
    if n <= 1 {
        host.closed.lock().unwrap().clear();
    }
    quit_if_done(&app);
}

/// Shows the window of a Quiver a shortcut started, for a question (which saves to keep) or to say why the game
/// didn't start; or hides it again once answered.
#[tauri::command]
pub fn shortcut_window(app: AppHandle, show: bool) {
    let Some(window) = app.get_webview_window("main") else { return };
    if show {
        let _ = window.show();
        let _ = window.unminimize();
        let _ = window.set_focus();
        if crate::in_game_mode() {
            let _ = window.set_fullscreen(true);
        }
    } else {
        let _ = window.hide();
    }
}

/// Quits a Quiver a shortcut started once it has nothing left to do: no shortcut waiting, no game running, no saves
/// being sent, and its window not open.
fn quit_if_done(app: &AppHandle) {
    let host = app.state::<Host>();
    if !host.quits.load(Ordering::SeqCst) {
        return;
    }
    let busy = !host.pending.lock().unwrap().is_empty() || !host.sessions.lock().unwrap().is_empty() || host.sending.load(Ordering::SeqCst) > 0;
    let shown = app.get_webview_window("main").is_some_and(|w| w.is_visible().unwrap_or(false));
    if !busy && !shown {
        app.exit(0);
    }
}

/// Linux: Steam's Exit Game sends SIGTERM to the shortcut's processes, this Quiver's included, and kills them two
/// seconds later. In that time the saves are backed up here, and sending them is handed to a Quiver started outside
/// Steam's processes, which then quits.
#[cfg(target_os = "linux")]
fn on_stop(app: AppHandle) {
    static STOPPED: AtomicBool = AtomicBool::new(false);
    extern "C" fn stopped(_: libc::c_int) {
        STOPPED.store(true, Ordering::SeqCst);
    }
    unsafe { libc::signal(libc::SIGTERM, stopped as extern "C" fn(libc::c_int) as libc::sighandler_t) };
    std::thread::spawn(move || {
        while !STOPPED.load(Ordering::SeqCst) {
            std::thread::sleep(Duration::from_millis(20));
        }
        STOPPING.store(true, Ordering::SeqCst);
        let asked = Instant::now();
        let host = app.state::<Host>();
        // The games have a moment to close and write their saves, as they're asked to as well.
        while asked.elapsed() < CLOSING && host.sessions.lock().unwrap().iter().any(|(_, s)| s.ended.is_none()) {
            std::thread::sleep(Duration::from_millis(20));
        }
        let (data, running, watching) = (app.state::<Data>(), app.state::<Running>(), app.state::<Watching>());
        // The games running or just closed, with their play time to count; and those closed already, whose saves the
        // page may not have sent yet.
        let mut hand: Vec<(String, Option<SavesOf>, Option<u64>)> = Vec::new();
        for (_, session) in host.sessions.lock().unwrap().drain(..) {
            running.closed(&session.id);
            let seconds = session.ended.unwrap_or_else(|| session.started.elapsed().as_secs());
            hand.push((session.id, session.saves, Some(seconds)));
        }
        if host.sending.load(Ordering::SeqCst) > 0 {
            hand.extend(host.closed.lock().unwrap().drain(..).map(|(id, saves)| (id, saves, None)));
        }
        let log = data.0.join("quiver.log");
        for (id, saves, seconds) in hand {
            if let Some(of) = &saves {
                if let Err(e) = crate::played(&data, &running, &watching, of) {
                    crate::append_log(&log, &format!("Couldn't back up {id}'s saves as Steam stopped it: {e}"));
                }
            }
            let seconds = seconds.map(|s| s.to_string());
            let mut args = vec![UPLOAD, id.as_str()];
            args.extend(seconds.as_deref());
            match outside_steam(&args).map(|mut c| c.spawn()) {
                Some(Ok(_)) => {}
                Some(Err(e)) => crate::append_log(&log, &format!("Couldn't hand {id}'s saves on as Steam stopped it: {e}")),
                None => crate::append_log(&log, &format!("Couldn't hand {id}'s saves on as Steam stopped it: no systemd-run")),
            }
        }
        std::process::exit(0);
    });
}

// ---- Shortcuts ----

/// What a desktop or Steam shortcut to an app starts: this Quiver with `--launch` and the app, from wherever the
/// system starts it (the Flatpak through `flatpak run`, an AppImage as itself). Quiver works out the game's command
/// each time, so a shortcut follows the player's launch options and the program they chose as those change.
pub fn shortcut_target(id: &str) -> crate::shortcut::Target {
    let (program, mut args) = match (crate::sandbox::id(), crate::update::kind()) {
        (Some(flatpak), _) => (crate::flatpak::program(), vec!["run".to_string(), flatpak]),
        (None, crate::update::Kind::AppImage(image)) => (image, vec![]),
        _ => (std::env::current_exe().unwrap_or_else(|_| PathBuf::from("quiver-launcher")), vec![]),
    };
    args.extend(data_args());
    args.extend([LAUNCH.to_string(), id.to_string()]);
    let dir = if crate::sandbox::id().is_some() { dirs::home_dir().unwrap_or_default() } else { program.parent().map(Path::to_path_buf).unwrap_or_default() };
    crate::shortcut::Target { exe: program.clone(), program, args, env: vec![], dir, extract_appimage: false, steam_proton: false, options: Default::default() }
}

// ---- Starting Quiver ----

/// This Quiver again, with these arguments: its own AppImage (not the copy mounted for this one, which goes when it
/// does), or its own program.
fn quiver(args: &[&str]) -> Command {
    let program = match crate::update::kind() {
        crate::update::Kind::AppImage(image) => image,
        _ => std::env::current_exe().unwrap_or_else(|_| PathBuf::from("quiver-launcher")),
    };
    let mut command = crate::host::command(program);
    command.args(args);
    command
}

/// Linux: this Quiver, started so it's no part of the processes Steam ends: by systemd for the player's session, or
/// for the Flatpak, `flatpak run` on the host. None without either.
#[cfg(target_os = "linux")]
fn outside_steam(args: &[&str]) -> Option<Command> {
    // Started by systemd or on the host, it has none of this one's environment.
    let data = data_args();
    let args: Vec<&str> = data.iter().map(String::as_str).chain(args.iter().copied()).collect();
    let args = args.as_slice();
    let owned: Vec<&std::ffi::OsStr> = args.iter().map(std::ffi::OsStr::new).collect();
    if let Some(relaunch) = crate::sandbox::relaunch(&owned) {
        return Some(relaunch);
    }
    let systemd = crate::sandbox::which("systemd-run")?;
    let inner = quiver(args);
    let mut command = crate::host::command(systemd);
    command.args(["--user", "--collect", "--quiet"]).arg(inner.get_program()).args(inner.get_args());
    Some(command)
}

/// Starts a Quiver with no window to answer a shortcut. On Windows it breaks away from Steam's job, so Steam's Stop
/// doesn't end it before the saves are sent; on Linux it stays among Steam's processes (see the top of this file), and
/// the returned process is waited for.
fn start_host() -> std::io::Result<Option<std::process::Child>> {
    let mut command = quiver(&[HOST]);
    command.stdin(std::process::Stdio::null()).stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null());
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        const BREAKAWAY: u32 = 0x0100_0000; // CREATE_BREAKAWAY_FROM_JOB
        const DETACHED: u32 = 0x0000_0008; // DETACHED_PROCESS
        command.creation_flags(BREAKAWAY | DETACHED);
        // A job that doesn't allow breaking away refuses it: started inside, the saves are still backed up next time.
        return match command.spawn() {
            Ok(_) => Ok(None),
            Err(_) => {
                command.creation_flags(DETACHED);
                command.spawn().map(|_| None)
            }
        };
    }
    #[cfg(target_os = "linux")]
    {
        return command.spawn().map(Some);
    }
    #[allow(unreachable_code)]
    {
        #[cfg(unix)]
        {
            use std::os::unix::process::CommandExt;
            command.process_group(0);
        }
        command.spawn().map(|_| None)
    }
}

// ---- The shortcut's side ----

/// Quiver's data folder, found as Quiver finds it (lib.rs setup) without starting it.
fn data_dir() -> Option<PathBuf> {
    let identifier = serde_json::from_str::<serde_json::Value>(include_str!("../tauri.conf.json")).ok()?["identifier"].as_str()?.to_string();
    std::env::var_os("QUIVER_DATA")
        .map(PathBuf::from)
        .or_else(crate::portable_data)
        .or_else(|| crate::sandbox::data_dir(&identifier))
        .or_else(|| Some(dirs::data_dir()?.join(identifier)))
}

/// A connection to the Quiver that's listening, if one is.
fn connect(data: &Path) -> Option<(TcpStream, String)> {
    let listening: Listening = serde_json::from_slice(&std::fs::read(data.join(FILE)).ok()?).ok()?;
    let stream = TcpStream::connect_timeout(&(std::net::Ipv4Addr::LOCALHOST, listening.port).into(), Duration::from_secs(2)).ok()?;
    Some((stream, listening.token))
}

/// The shortcut's arguments: the app, and what follows `--` (or anything else after it) for the game.
pub fn parse(args: &[String]) -> Option<(String, Vec<String>)> {
    let at = args.iter().position(|a| a == LAUNCH)?;
    let app = args.get(at + 1)?.clone();
    let rest = &args[at + 2..];
    let rest = if rest.first().is_some_and(|a| a == "--") { &rest[1..] } else { rest };
    Some((app, rest.to_vec()))
}

/// The polite stop requests the helper has had (Steam's Exit Game on Linux): the game is asked too, and its closing
/// is still reported.
static ASKED_TO_STOP: AtomicBool = AtomicBool::new(false);

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn ignore_polite_stops() {
    extern "C" fn asked(_: libc::c_int) {
        ASKED_TO_STOP.store(true, Ordering::SeqCst);
    }
    for signal in [libc::SIGTERM, libc::SIGINT, libc::SIGHUP] {
        unsafe { libc::signal(signal, asked as extern "C" fn(libc::c_int) as libc::sighandler_t) };
    }
}

#[cfg(not(any(target_os = "linux", target_os = "macos")))]
fn ignore_polite_stops() {}

/// The helper a shortcut runs (`--launch`): the game's exit code, or 1 when it didn't start.
pub fn helper(args: &[String]) -> i32 {
    let Some(data) = data_dir() else { return 1 };
    let log = data.join("quiver.log");
    let fail = |why: String| {
        crate::append_log(&log, &format!("Shortcut: {why}"));
        1
    };
    let Some((app, extra)) = parse(args) else { return fail("no app named after --launch".into()) };
    ignore_polite_stops();
    let mut host = None;
    let (mut stream, token, hidden) = match connect(&data) {
        Some((stream, token)) => (stream, token, false),
        None => {
            match start_host() {
                Ok(child) => host = child,
                Err(e) => return fail(format!("couldn't start Quiver: {e}")),
            }
            let since = Instant::now();
            loop {
                if let Some((stream, token)) = connect(&data) {
                    break (stream, token, true);
                }
                if since.elapsed() > STARTING {
                    return fail("Quiver didn't answer".into());
                }
                std::thread::sleep(Duration::from_millis(200));
            }
        }
    };
    let code = play(&mut stream, Ask { token, app, args: extra, hidden });
    // On Linux the Quiver it started runs among Steam's processes, and from its AppImage or Flatpak sandbox: it ends
    // when this does, so it's given the time to send the saves.
    if let Some(mut host) = host {
        drop(stream);
        let _ = host.wait();
    }
    match code {
        Ok(code) => code,
        Err(why) => fail(why),
    }
}

fn play(stream: &mut TcpStream, ask: Ask) -> Result<i32, String> {
    send(stream, &ask).map_err(|e| e.to_string())?;
    let mut reader = BufReader::new(stream.try_clone().map_err(|e| e.to_string())?);
    let run = match read::<Reply>(&mut reader) {
        Ok(Reply::Start(run)) => run,
        Ok(Reply::Refuse(why)) => return Err(format!("not started: {why}")),
        Err(e) => return Err(format!("Quiver didn't say how to start it: {e}")),
    };
    let mut command = crate::host::command(&run.program);
    command.args(&run.args).envs(run.env.iter().map(|(k, v)| (k, v))).current_dir(&run.dir);
    let spawned = crate::sandbox::check(&run.program).and_then(|_| crate::sandbox::game(command).spawn());
    let status: Option<i32> = match spawned {
        Ok(mut child) => {
            let _ = send(stream, &Report::Started);
            child.wait().ok().and_then(|s| s.code())
        }
        // One that needs administrator rights: Windows asks the player, as a double click would.
        #[cfg(windows)]
        Err(e) if e.raw_os_error() == Some(crate::elevate::REQUIRED) => match crate::elevate::start(&run.program, &run.args, &run.dir) {
            Ok(process) => {
                let _ = send(stream, &Report::Started);
                process.map(|p| p.wait());
                Some(0)
            }
            Err(e) => {
                let _ = send(stream, &Report::Failed(e.to_string()));
                return Err(format!("couldn't start {}: {e}", run.program.display()));
            }
        },
        Err(e) => {
            let _ = send(stream, &Report::Failed(e.to_string()));
            return Err(format!("couldn't start {}: {e}", run.program.display()));
        }
    };
    let _ = send(stream, &Report::Closed { code: status, stopped: ASKED_TO_STOP.load(Ordering::SeqCst) });
    Ok(status.unwrap_or(0))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn s(words: &[&str]) -> Vec<String> {
        words.iter().map(|w| w.to_string()).collect()
    }

    #[test]
    fn the_shortcut_names_the_app_and_what_the_game_gets() {
        assert_eq!(parse(&s(&["quiver", "--launch", "abc"])), Some(("abc".into(), vec![])));
        assert_eq!(parse(&s(&["quiver", "--launch", "abc", "--", "-w", "2"])), Some(("abc".into(), s(&["-w", "2"]))));
        // Steam puts the player's own launch options after the shortcut's.
        assert_eq!(parse(&s(&["quiver", "--launch", "abc", "--fullscreen"])), Some(("abc".into(), s(&["--fullscreen"]))));
        assert_eq!(parse(&s(&["quiver", "--launch"])), None);
        assert_eq!(parse(&s(&["quiver"])), None);
    }

    #[test]
    fn a_shortcut_carries_the_data_folder_quiver_was_given() {
        assert_eq!(data_arg(&s(&["quiver", "--quiver-data", "/home/deck/QuiverDev/data", "--launch", "abc"])), Some("/home/deck/QuiverDev/data".into()));
        assert_eq!(data_arg(&s(&["quiver", "--launch", "abc"])), None);
        // It comes before --launch, so it's never taken for the game's.
        assert_eq!(parse(&s(&["quiver", "--quiver-data", "/d", "--launch", "abc"])), Some(("abc".into(), vec![])));
    }

    #[test]
    fn how_quiver_was_started() {
        let plain = started(&s(&["quiver"]));
        assert!(!plain.host && plain.upload.is_none());
        assert!(started(&s(&["quiver", "--shortcut-host"])).host);
        let upload = started(&s(&["quiver", "--upload-saves", "abc", "42"]));
        assert!(upload.host);
        assert_eq!(upload.upload, Some(("abc".into(), Some(42))));
        // Its play time counted already: only the saves to send.
        assert_eq!(started(&s(&["quiver", "--upload-saves", "abc"])).upload, Some(("abc".into(), None)));
    }

    #[test]
    fn messages_go_both_ways_as_json_lines() {
        let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let port = listener.local_addr().unwrap().port();
        let mut out = TcpStream::connect(("127.0.0.1", port)).unwrap();
        let (incoming, _) = listener.accept().unwrap();
        let mut reader = BufReader::new(incoming);
        let run = Run { program: "/games/g/game".into(), args: s(&["a b", "--x"]), env: vec![("K".into(), "v\nw".into())], dir: "/games/g".into() };
        send(&mut out, &Reply::Start(run)).unwrap();
        send(&mut out, &Report::Closed { code: Some(3), stopped: true }).unwrap();
        let Reply::Start(got) = read::<Reply>(&mut reader).unwrap() else { panic!() };
        assert_eq!(got.args, s(&["a b", "--x"]));
        assert_eq!(got.env, vec![("K".to_string(), "v\nw".to_string())]);
        assert_eq!(read::<Report>(&mut reader).unwrap(), Report::Closed { code: Some(3), stopped: true });
        drop(out);
        assert!(read::<Report>(&mut reader).is_err());
    }

    #[test]
    fn tokens_differ() {
        assert_ne!(token(), token());
        assert_eq!(token().len(), 32);
    }
}
