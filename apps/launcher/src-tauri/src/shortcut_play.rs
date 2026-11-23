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
//! - On Windows, Steam's Stop ends the program it started and every process whose parent leads back to it (no job:
//!   tested). So a Quiver the helper starts is started by a middleman (`--shortcut-hop`) that exits at once, leaving
//!   it no living parent to be found by; a dropped connection counts as the game closing.
//! - On Linux, it runs inside them, where Game Mode shows its window for the question about which saves to keep. On
//!   SIGTERM the helper, which is already waiting on the game, starts a Quiver outside Steam's processes
//!   (`--upload-saves`, by systemd-run, or `flatpak run` on the host) that backs the saves up once the game has
//!   closed, sends them and quits. The Quiver inside can't do it: an AppImage's mount goes with the same SIGTERM.
//!
//! Each step is noted in quiver.log ("shortcut: …"), for bug reports.
//!
//! Quiver listens on 127.0.0.1 at a port it writes to `shortcuts.json` in its data folder, with a token only the
//! player's own programs can read there.
use crate::{Data, Running};
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
/// The middleman a shortcut starts its Quiver through on Windows, which starts it and exits at once (start_host).
pub const HOP: &str = "--shortcut-hop";
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
struct Session {
    started: Instant,
}

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

/// Notes a step in quiver.log, for bug reports: which app, and no paths but the program's name.
fn note(data: &Path, what: &str) {
    crate::append_log(&data.join("quiver.log"), &format!("shortcut: {what}"));
}

/// A program and its folder for a note: only the program's own name.
fn run_line(program: &Path, dir: &Path) -> String {
    let name = |p: &Path| p.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    format!("{} to start, in {}", name(program), name(dir))
}

// ---- Quiver's side ----

/// Listens for shortcuts, from setup. `quits`: this Quiver was started by one, with no window.
pub fn listen(app: &AppHandle, data: &Path, quits: bool) {
    let host = app.state::<Host>();
    host.quits.store(quits, Ordering::SeqCst);
    // No window on screen: the controllers are the game's, not this Quiver's hidden library's.
    crate::gamepad::unseen(quits);
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
    note(data, &format!("listening on port {port}{}", if quits { ", with no window, for a shortcut" } else { "" }));
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
    if let Some(data) = app.try_state::<Data>() {
        note(&data.0, &format!("{}: a shortcut asks to play it", ask.app));
    }
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
        note(&app.state::<Data>().0, &format!("{}: not started{}{why}", pending.app, if why.is_empty() { "" } else { ": " }));
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
    note(&data.0, &format!("{id}: sent the shortcut {}", run_line(&target.program, &target.dir)));
    host.sessions.lock().unwrap().push((request, Session { started: Instant::now() }));
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
        sessions.remove(at).1
    };
    let data = app.state::<Data>().0.clone();
    match (playing, ended) {
        (true, Some(ended)) => note(&data, &format!("{id}: the game {} after {}s", if ended == "closed" { "closed" } else { "was stopped" }, session.started.elapsed().as_secs())),
        (false, _) => note(&data, &format!("{id}: the game didn't start")),
        _ => {}
    }
    if playing {
        app.state::<Running>().closed(&id);
        app.state::<crate::discord::Presence>().closed(&id);
    }
    match (playing, ended) {
        (true, Some(ended)) => {
            let seconds = session.started.elapsed().as_secs();
            host.sending.fetch_add(1, Ordering::SeqCst);
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
    quit_if_done(&app);
}

/// Shows the window of a Quiver a shortcut started, for a question (which saves to keep) or to say why the game
/// didn't start; or hides it again once answered.
#[tauri::command]
pub fn shortcut_window(app: AppHandle, show: bool) {
    let Some(window) = app.get_webview_window("main") else { return };
    crate::gamepad::unseen(!show);
    if show {
        let _ = window.show();
        #[cfg(desktop)]
        let _ = window.unminimize();
        let _ = window.set_focus();
        #[cfg(desktop)]
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
        if let Some(data) = app.try_state::<Data>() {
            note(&data.0, "nothing left to do: quitting");
        }
        app.exit(0);
    }
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

/// What a Quiver started outside Steam's processes needs of the session: its display, its bus, and what says it's
/// Game Mode (keyboard::game_mode) and a Steam Deck.
#[cfg(target_os = "linux")]
const SESSION: &[&str] = &[
    "DISPLAY",
    "WAYLAND_DISPLAY",
    "XAUTHORITY",
    "XDG_RUNTIME_DIR",
    "DBUS_SESSION_BUS_ADDRESS",
    "XDG_SESSION_TYPE",
    "XDG_CURRENT_DESKTOP",
    "XDG_SESSION_DESKTOP",
    "GAMESCOPE_WAYLAND_DISPLAY",
    "SteamDeck",
    "LANG",
    "LC_ALL",
];

/// Linux: this Quiver, started so it's no part of the processes Steam ends: by systemd for the player's session, or
/// for the Flatpak, `flatpak run` on the host. None without either.
#[cfg(target_os = "linux")]
fn outside_steam(args: &[&str]) -> Option<Command> {
    // Started by systemd or on the host, it has none of this one's environment.
    let data = data_args();
    let args: Vec<&str> = data.iter().map(String::as_str).chain(args.iter().copied()).collect();
    let args = args.as_slice();
    // Nor the session's display: without it, it can't open even the window it keeps hidden. Nor what says it's Game
    // Mode. Not what Steam set for the game, though: it's no part of it.
    let session: Vec<&str> = SESSION.iter().copied().filter(|name| std::env::var_os(name).is_some()).collect();
    if let Some(flatpak) = crate::sandbox::id() {
        let mut command = Command::new("flatpak");
        command.arg("run").arg(flatpak).args(args);
        for name in &session {
            command.env(name, std::env::var_os(name).unwrap_or_default());
        }
        return Some(crate::sandbox::host(command));
    }
    let systemd = crate::sandbox::which("systemd-run")?;
    let inner = quiver(args);
    let mut command = crate::host::command(systemd);
    command.args(["--user", "--collect", "--quiet"]);
    // A name alone: systemd-run takes its value from here.
    command.args(session.iter().map(|name| format!("--setenv={name}")));
    command.arg(inner.get_program()).args(inner.get_args());
    Some(command)
}

/// Starts this Quiver with `args` and no console, outside a job if it can be (none of Steam's is, tested, but a job
/// that's there may refuse it).
#[cfg(windows)]
fn spawn_detached(args: &[&str]) -> std::io::Result<std::process::Child> {
    use std::os::windows::process::CommandExt;
    const BREAKAWAY: u32 = 0x0100_0000; // CREATE_BREAKAWAY_FROM_JOB
    const DETACHED: u32 = 0x0000_0008; // DETACHED_PROCESS
    let mut command = quiver(args);
    command.stdin(std::process::Stdio::null()).stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null());
    command.creation_flags(BREAKAWAY | DETACHED);
    command.spawn().or_else(|_| {
        command.creation_flags(DETACHED);
        command.spawn()
    })
}

/// The middleman (`--shortcut-hop`): starts the Quiver a shortcut needs and exits at once, so that Quiver's parent is
/// gone and Steam's Stop, which ends what its program started, doesn't find it.
pub fn hop() -> i32 {
    #[cfg(windows)]
    return spawn_detached(&[HOST]).map_or(1, |_| 0);
    #[allow(unreachable_code)]
    1
}

/// Starts a Quiver with no window to answer a shortcut. On Windows through the middleman, so Steam's Stop doesn't end
/// it before the saves are sent; on Linux it stays among Steam's processes (see the top of this file), and the returned
/// process is waited for.
fn start_host() -> std::io::Result<Option<std::process::Child>> {
    #[cfg(windows)]
    {
        let status = spawn_detached(&[HOP])?.wait()?;
        return if status.success() { Ok(None) } else { Err(std::io::Error::other("the middleman couldn't start it")) };
    }
    #[allow(unreachable_code)]
    let mut command = quiver(&[HOST]);
    command.stdin(std::process::Stdio::null()).stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null());
    // An AppImage's runtime lets go of its mount as soon as Quiver closes, rather than keeping Steam's shortcut
    // "running" for seconds after (scripts/tauri-cef.sh puts this in the AppImage too).
    command.env("REUSE_CHECK_DELAY", "0");
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

/// The game this helper started: when, and whether it has closed. For a handoff as Steam stops it (hand_off_on_stop).
#[derive(Default)]
struct Game {
    started: Mutex<Option<Instant>>,
    closed: AtomicBool,
}

static GAME: std::sync::OnceLock<Game> = std::sync::OnceLock::new();

/// Linux: when Steam's Exit Game asks the shortcut to stop, the Quiver this helper started goes with it (an AppImage's
/// mount ends on the same signal). So the helper, which is still running, starts one outside Steam's processes that
/// backs up the saves once the game has closed, sends them, and quits; with the play time when the game was still
/// running, which no one else will count.
#[cfg(target_os = "linux")]
fn hand_off_on_stop(data: PathBuf, app: String) {
    std::thread::spawn(move || {
        while !ASKED_TO_STOP.load(Ordering::SeqCst) {
            std::thread::sleep(Duration::from_millis(20));
        }
        let game = GAME.get_or_init(Game::default);
        let playing = (!game.closed.load(Ordering::SeqCst)).then(|| *game.started.lock().unwrap()).flatten();
        let seconds = playing.map(|at| at.elapsed().as_secs().to_string());
        let mut args = vec![UPLOAD, app.as_str()];
        args.extend(seconds.as_deref());
        match outside_steam(&args).map(|mut c| c.stdin(std::process::Stdio::null()).stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null()).spawn()) {
            Some(Ok(_)) => note(&data, &format!("{app}: Steam is stopping it; started a Quiver outside Steam to back up and send the saves")),
            Some(Err(e)) => note(&data, &format!("{app}: Steam is stopping it, and the saves couldn't be handed on: {e}")),
            None => note(&data, &format!("{app}: Steam is stopping it, and the saves couldn't be handed on: no systemd-run")),
        }
    });
}

/// The helper a shortcut runs (`--launch`): the game's exit code, or 1 when it didn't start.
pub fn helper(args: &[String]) -> i32 {
    let Some(data) = data_dir() else { return 1 };
    let fail = |why: String| {
        note(&data, &why);
        1
    };
    let Some((app, extra)) = parse(args) else { return fail("no app named after --launch".into()) };
    ignore_polite_stops();
    note(&data, &format!("{app}: started"));
    let mut host = None;
    let (mut stream, token, hidden) = match connect(&data) {
        Some((stream, token)) => (stream, token, false),
        None => {
            match start_host() {
                Ok(child) => host = child,
                Err(e) => return fail(format!("{app}: couldn't start Quiver: {e}")),
            }
            note(&data, &format!("{app}: started Quiver with no window to answer"));
            let since = Instant::now();
            loop {
                if let Some((stream, token)) = connect(&data) {
                    break (stream, token, true);
                }
                if since.elapsed() > STARTING {
                    return fail(format!("{app}: Quiver didn't answer"));
                }
                std::thread::sleep(Duration::from_millis(200));
            }
        }
    };
    // A Quiver that was open already isn't among Steam's processes: it hears the game closed, or the connection drop.
    #[cfg(target_os = "linux")]
    if hidden {
        hand_off_on_stop(data.clone(), app.clone());
    }
    let code = play(&mut stream, Ask { token, app: app.clone(), args: extra, hidden });
    // On Linux the Quiver it started runs among Steam's processes, and from its AppImage or Flatpak sandbox: it ends
    // when this does, so it's given the time to send the saves.
    if let Some(mut host) = host {
        drop(stream);
        note(&data, &format!("{app}: waiting for Quiver to send the saves"));
        let _ = host.wait();
    }
    match code {
        Ok(code) => {
            note(&data, &format!("{app}: done"));
            code
        }
        Err(why) => fail(format!("{app}: {why}")),
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
    let game = GAME.get_or_init(Game::default);
    let status: Option<i32> = match spawned {
        Ok(mut child) => {
            *game.started.lock().unwrap() = Some(Instant::now());
            let _ = send(stream, &Report::Started);
            child.wait().ok().and_then(|s| s.code())
        }
        // One that needs administrator rights: Windows asks the player, as a double click would.
        #[cfg(windows)]
        Err(e) if e.raw_os_error() == Some(crate::elevate::REQUIRED) => match crate::elevate::start(&run.program, &run.args, &run.dir) {
            Ok(process) => {
                *game.started.lock().unwrap() = Some(Instant::now());
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
    game.closed.store(true, Ordering::SeqCst);
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
