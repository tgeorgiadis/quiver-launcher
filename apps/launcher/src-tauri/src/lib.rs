#[cfg(target_os = "android")]
mod android;
mod apps_folder;
mod browser;
mod cache;
mod custom_art;
mod device;
mod discord;
#[cfg(windows)]
mod elevate;
mod flatpak;
mod gamepad;
mod host;
mod install;
mod keyboard;
mod launch;
mod mods;
mod move_journal;
mod msi;
mod own_copy;
mod picker;
mod provider_http;
#[cfg_attr(target_os = "android", allow(dead_code))]
mod sandbox;
mod script;
mod saves;
mod secrets;
mod session;
mod setup;
mod shortcut;
mod snapshots;
mod specs;
mod steam;
mod sync_folder;
#[cfg(windows)]
mod tray;
mod update;
mod v3;
mod window_state;
#[cfg(all(feature = "cef", target_os = "linux"))]
mod x11_wheel;

use serde::Serialize;
use std::ffi::OsStr;
use std::path::{Path, PathBuf};
use std::process::Command;
use tauri::{Emitter, Manager, State};

/// Where the launcher keeps its files: `QUIVER_DATA`, else a `data` folder
/// beside a portable install (one with `portable.txt` next to it), else the
/// OS's app data folder.
/// The data folder, and the apps folder (apps_folder.rs), which can change.
struct Data(PathBuf, std::sync::Mutex<PathBuf>);

impl Data {
    fn apps(&self) -> PathBuf {
        self.1.lock().unwrap().clone()
    }
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Config {
    api: String,
    /// The Convex deployment for sign-in, sync and reviews.
    convex: String,
    /// A stand-in account service, for end-to-end tests.
    account_api: Option<String>,
    /// GitHub's API, for custom apps (overridden in end-to-end tests).
    github_api: String,
    /// GitLab's API, for custom apps (overridden in end-to-end tests).
    gitlab_api: String,
    /// Where GitHub and Discord sign-in return to.
    return_to: &'static str,
    os: &'static str,
    arch: &'static str,
    apps_dir: String,
    /// The download cache's folder unless the player picks another: `cache` in the data folder (beside a portable copy).
    cache_dir: String,
    /// Where anonymous usage data goes: the website, or a stand-in in end-to-end tests.
    usage_host: String,
    /// This build's version, sent with usage data.
    version: String,
    /// The home folder and the user's name on this computer, which usage data
    /// must never contain: the UI takes them out of anything it sends.
    home: Option<String>,
    user: Option<String>,
    /// Under gamescope, as a Steam Deck's Game Mode runs it: TV mode comes on by itself (tv.tsx).
    game_mode: bool,
    /// An X11 desktop on Linux, where the page moves the window itself (`x11_session`).
    x11: bool,
    /// Steam's on-screen keyboard can be asked for (keyboard.rs): what a controller types with, so TV mode offers its
    /// search here.
    steam_keyboard: bool,
    /// Steam is running Quiver, so Add to Steam can't work now (add_to_steam): the UI greys it out.
    steam_runs_us: bool,
    /// The system's version, sent with usage data (device.rs).
    os_version: Option<String>,
    /// A Steam Deck, sent with usage data.
    steam_deck: bool,
    /// How this copy was installed, sent with usage data (update.rs).
    package: &'static str,
}

/// Steam is running Quiver as one of its games, so it would overwrite a new shortcut as it closes.
fn steam_runs_us() -> bool {
    std::env::var_os("SteamGameId").is_some()
}

const OS: &str = match std::env::consts::OS.as_bytes() {
    b"macos" => "macos",
    b"windows" => "windows",
    b"android" => "android",
    b"ios" => "ios",
    _ => "linux",
};
const ARCH: &str = match std::env::consts::ARCH.as_bytes() {
    b"x86_64" => "x64",
    b"aarch64" => "arm64",
    b"x86" => "x86",
    _ => "arm",
};

#[tauri::command]
fn config(app: tauri::AppHandle, data: State<Data>) -> Config {
    Config {
        api: std::env::var("QUIVER_API").unwrap_or_else(|_| "https://api.quiverlauncher.com/api/v1".into()),
        convex: std::env::var("QUIVER_CONVEX").unwrap_or_else(|_| "https://convex.quiverlauncher.com".into()),
        account_api: std::env::var("QUIVER_ACCOUNT_API").ok(),
        github_api: std::env::var("QUIVER_GITHUB_API").unwrap_or_else(|_| "https://api.github.com".into()),
        gitlab_api: gitlab_api(),
        return_to: browser::RETURN_TO,
        os: OS,
        arch: ARCH,
        apps_dir: data.apps().to_string_lossy().into(),
        cache_dir: data.0.join("cache").to_string_lossy().into(),
        usage_host: std::env::var("QUIVER_USAGE_HOST").unwrap_or_else(|_| "https://quiverlauncher.com".into()),
        version: app.package_info().version.to_string(),
        home: std::env::var(if OS == "windows" { "USERPROFILE" } else { "HOME" }).ok().filter(|h| !h.is_empty()),
        user: std::env::var("USERNAME").or_else(|_| std::env::var("USER")).ok().filter(|u| !u.is_empty()),
        game_mode: keyboard::game_mode(|name| std::env::var(name).ok()),
        x11: OS == "linux" && x11_session(|name| std::env::var(name).ok()),
        steam_keyboard: cfg!(target_os = "linux") && keyboard::offered(|name| std::env::var(name).ok()),
        steam_runs_us: steam_runs_us(),
        os_version: device::os_version(),
        steam_deck: device::steam_deck(|name| std::env::var(name).ok(), device::board_vendor()),
        package: update::package(),
    }
}

/// The computer's processor, graphics card and memory, for feedback (specs.rs). Read off the main thread: on Linux and
/// macOS it reads files and runs sysctl.
#[tauri::command(async)]
fn pc_specs() -> specs::Specs {
    specs::read()
}

/// The launcher's own JSON files: the library, installs and the catalog cache.
fn state_file(data: &Data, name: &str) -> Result<PathBuf, String> {
    match name {
        "library" | "installs" | "catalog" | "settings" | "collections" => Ok(data.0.join(format!("{name}.json"))),
        _ => Err(format!("Unknown state file {name}")),
    }
}

#[tauri::command]
fn read_state(data: State<Data>, name: String) -> Result<Option<serde_json::Value>, String> {
    match std::fs::read(state_file(&data, &name)?) {
        Ok(bytes) => Ok(serde_json::from_slice(&bytes).ok()),
        Err(_) => Ok(None),
    }
}

#[tauri::command]
fn write_state(app: tauri::AppHandle, data: State<Data>, snapshotted: State<Snapshotted>, name: String, value: serde_json::Value) -> Result<(), String> {
    let path = state_file(&data, &name)?;
    let temp = path.with_extension("json.tmp");
    std::fs::create_dir_all(&data.0).map_err(|e| e.to_string())?;
    std::fs::write(&temp, serde_json::to_vec_pretty(&value).map_err(|e| e.to_string())?)
        .and_then(|_| std::fs::rename(&temp, &path))
        .map_err(|e| e.to_string())?;
    // A moved app's old copy goes only now its new place is saved.
    if name == "installs" {
        delete_moved(Some(app), &data.0, move_journal::recorded(&data.0, &value, &data.apps()));
    }
    if snapshots::FILES.contains(&name.as_str()) {
        let first = !snapshotted.0.swap(true, std::sync::atomic::Ordering::Relaxed);
        if let Err(e) = snapshots::take(&data.0, first) {
            append_log(&data.0.join("quiver.log"), &format!("Couldn't take a library snapshot: {e}"));
        }
    }
    Ok(())
}

/// Deletes moved apps' old copies, off the window's thread: one big one takes a while. One that can't all go is
/// said ("app-move-left"), and logged.
fn delete_moved(app: Option<tauri::AppHandle>, data: &Path, old: Vec<PathBuf>) {
    if old.is_empty() {
        return;
    }
    let log = data.join("quiver.log");
    std::thread::spawn(move || {
        for path in old {
            if let Err(e) = std::fs::remove_dir_all(&path) {
                append_log(&log, &format!("Couldn't delete a moved app's old copy at {}: {e}", path.display()));
                if let Some(app) = &app {
                    let _ = app.emit("app-move-left", path.to_string_lossy());
                }
            }
        }
    });
}

/// Whether this run of the launcher has saved yet: its first save takes a snapshot.
struct Snapshotted(std::sync::atomic::AtomicBool);

#[derive(Serialize)]
struct StateProblem {
    name: &'static str,
    problem: snapshots::Problem,
}

/// Saved files that can't be read, or that are gone though snapshots show a library: the launcher
/// won't start over them, since its first save would replace what could still be restored.
#[tauri::command]
fn state_problems(data: State<Data>) -> Vec<StateProblem> {
    snapshots::FILES.iter().filter_map(|&name| Some(StateProblem { name, problem: snapshots::health(&data.0, name)? })).collect()
}

#[tauri::command]
fn list_snapshots(data: State<Data>) -> Vec<snapshots::Snapshot> {
    snapshots::list(&data.0)
}

#[tauri::command]
fn restore_snapshot(data: State<Data>, id: String) -> Result<(), String> {
    snapshots::restore(&data.0, &id)
}

#[tauri::command]
fn start_empty_library(data: State<Data>) -> Result<(), String> {
    snapshots::start_empty(&data.0)
}

#[tauri::command]
async fn install(app: tauri::AppHandle, request: install::InstallRequest) -> Result<install::Installed, String> {
    let apps = app.state::<Data>().apps();
    let id = request.id.clone();
    let (cancel, cancelled) = tokio::sync::watch::channel(false);
    app.state::<Installing>().0.lock().unwrap().insert(id.clone(), cancel);
    let result = install::install(&apps, request, cancelled, |p| {
        let _ = app.emit("install-progress", p);
    })
    .await;
    app.state::<Installing>().0.lock().unwrap().remove(&id);
    result
}

/// The download cache's fetches (cache.rs), one at a time: sending true cancels the one under way.
#[derive(Default)]
struct CacheFetching(std::sync::Mutex<Option<tokio::sync::watch::Sender<bool>>>);

/// Downloads a release file into the download cache, without installing it. Its progress comes as `cache-progress`.
#[tauri::command]
async fn cache_fetch(app: tauri::AppHandle, request: install::InstallRequest) -> Result<(), String> {
    let (cancel, cancelled) = tokio::sync::watch::channel(false);
    *app.state::<CacheFetching>().0.lock().unwrap() = Some(cancel);
    let result = install::fetch_to_cache(request, cancelled, |p| {
        let _ = app.emit("cache-progress", p);
    })
    .await;
    *app.state::<CacheFetching>().0.lock().unwrap() = None;
    result
}

/// Stops the download cache's fetch under way; it fails with "cancelled".
#[tauri::command]
fn cache_fetch_cancel(fetching: State<CacheFetching>) {
    if let Some(cancel) = fetching.0.lock().unwrap().as_ref() {
        let _ = cancel.send(true);
    }
}

/// Clears what a launcher closed mid-download left in the download cache (cache.rs's `clean`): only while no fetch is
/// under way, so one coming in is never taken from under it. How many files went.
#[tauri::command]
async fn cache_clean(app: tauri::AppHandle, dir: String) -> Result<usize, String> {
    if app.state::<CacheFetching>().0.lock().unwrap().is_some() {
        return Ok(0);
    }
    tauri::async_runtime::spawn_blocking(move || cache::clean(Path::new(&dir))).await.map_err(|e| e.to_string())
}

/// What's in the download cache. `names`: apps' ids to their slugs, for files an older build kept without one, which
/// move to their app's folder.
#[tauri::command]
async fn cache_status(dir: String, names: Option<std::collections::HashMap<String, String>>) -> Result<cache::Status, String> {
    tauri::async_runtime::spawn_blocking(move || cache::status(Path::new(&dir), &names.unwrap_or_default())).await.map_err(|e| e.to_string())
}

/// Which of these files (`sha256:<hex>`) the download cache has.
#[tauri::command]
fn cache_has(dir: String, checksums: Vec<String>) -> Vec<bool> {
    cache::has(Path::new(&dir), &checksums)
}

/// Rotates the download cache down to `limit` bytes (none for no limit), keeping `protect`'s files.
#[tauri::command]
async fn cache_trim(dir: String, limit: Option<u64>, protect: Vec<String>) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || cache::trim(Path::new(&dir), limit, &protect)).await.map_err(|e| e.to_string())?.map_err(|e| e.to_string())
}

/// Removes these files from the download cache, or all of them when `hashes` is none.
#[tauri::command]
async fn cache_remove(dir: String, hashes: Option<Vec<String>>) -> Result<(), String> {
    tauri::async_runtime::spawn_blocking(move || cache::remove(Path::new(&dir), hashes.as_deref())).await.map_err(|e| e.to_string())?.map_err(|e| e.to_string())
}

/// Shows the download cache's folder: the one settings.json names, else `cache` in the data folder. Never another
/// path, as the page can't name one.
#[tauri::command]
fn cache_open(data: State<Data>) -> Result<(), String> {
    let settings: serde_json::Value = std::fs::read(state_file(&data, "settings")?)
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .unwrap_or_default();
    let dir = settings["cache"]["dir"].as_str().map(str::trim).filter(|d| !d.is_empty()).map(PathBuf::from).unwrap_or_else(|| data.0.join("cache"));
    std::fs::create_dir_all(&dir).map_err(|_| "Couldn't open the cache folder. Is its drive connected?".to_string())?;
    reveal(&dir)
}

/// Shows an app's folder in the download cache: in the cache folder settings.json names (as `cache_open`), where the
/// index says its files are. Never another path.
#[tauri::command]
fn cache_open_app(data: State<Data>, app: String) -> Result<(), String> {
    let settings: serde_json::Value = std::fs::read(state_file(&data, "settings")?)
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .unwrap_or_default();
    let dir = settings["cache"]["dir"].as_str().map(str::trim).filter(|d| !d.is_empty()).map(PathBuf::from).unwrap_or_else(|| data.0.join("cache"));
    let folder = cache::app_folder(&dir, &app).ok_or("None of this app's files are in the download cache yet.")?;
    reveal(&folder)
}

/// Installs under way, by app id: sending true cancels one.
#[derive(Default)]
struct Installing(std::sync::Mutex<std::collections::HashMap<String, tokio::sync::watch::Sender<bool>>>);

/// Cancels an app's install while it downloads; it then fails with "cancelled".
#[tauri::command]
fn cancel_install(installing: State<Installing>, id: String) {
    if let Some(cancel) = installing.0.lock().unwrap().get(&id) {
        let _ = cancel.send(true);
    }
}

/// An app's folder: one in `apps`, or an install adopted from 3.x (`dir`).
fn app_dir(data: &Data, folder: &str, dir: Option<String>) -> Result<PathBuf, String> {
    match dir {
        Some(dir) => Ok(PathBuf::from(dir)),
        None => Ok(data.apps().join(install::plain_name(folder)?)),
    }
}

const NO_PROGRAM: &str = "Couldn't find a program to start in this app's folder.";
/// The page offers Locate existing install on these words (`folderGone`).
const FOLDER_GONE: &str = "This app's folder isn't there any more.";

/// An app's folder, as long as it's still there.
fn present_app_dir(data: &Data, folder: &str, dir: Option<String>) -> Result<PathBuf, String> {
    let dir = app_dir(data, folder, dir)?;
    if dir.is_dir() {
        Ok(dir)
    } else {
        Err(FOLDER_GONE.into())
    }
}

/// What starts an installed app: its program, or Wine (on Linux) or `open` (a macOS .app) with it.
/// `program` is one the player picked: exactly that file, never a guess.
fn target(data: &Data, folder: &str, dir: Option<String>, preferred: &[String], wine: bool, program: Option<String>, options: &launch::LaunchOptions) -> Result<shortcut::Target, String> {
    let (exe, wine) = match (&options.executable, program) {
        // The program the player chose in Launch options.
        // A full path is one anywhere, such as a game an installer in the app's folder put on another drive: it starts
        // whether or not the folder is still there.
        (Some(chosen), program) => {
            let anywhere = program.is_some() || Path::new(chosen).is_absolute();
            let exe = if anywhere { PathBuf::from(chosen) } else { present_app_dir(data, folder, dir)?.join(chosen) };
            if !exe.exists() {
                return Err(if anywhere {
                    launch::program_gone(&exe)
                } else {
                    format!("Can't find {chosen} in the app's folder. Choose the executable again in Launch options.")
                });
            }
            let windows = exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe") || e.eq_ignore_ascii_case("bat"));
            (exe, OS == "linux" && windows)
        }
        (None, Some(program)) => {
            let exe = PathBuf::from(&program);
            if !exe.exists() {
                let user = std::env::var("USER").or_else(|_| std::env::var("LOGNAME")).ok();
                if let Some(drive) = launch::missing_drive(&exe, user.as_deref(), Path::exists) {
                    return Err(launch::drive_gone(&exe, &drive));
                }
                // A Windows installer's program: the player can choose it again, or run the installer again.
                if app_dir(data, folder, dir).is_ok_and(|d| msi::installed(&d) || setup::elsewhere(&d)) {
                    return Err(format!("Can't find the executable at {program}. Choose it again in Launch options, or run the installer again."));
                }
                return Err(format!("The program isn't at {program} any more. Remove this app and add it again from where it is now."));
            }
            (exe, wine)
        }
        (None, None) => {
            let dir = present_app_dir(data, folder, dir)?;
            if msi::installed(&dir) || setup::elsewhere(&dir) {
                return Err("Quiver doesn't know which program its installer put on this computer yet. Press Play to choose it.".into());
            }
            if let Some(id) = flatpak::installed(&dir) {
                let flatpak = flatpak::program();
                let options = launch::parse(options.command.as_deref().unwrap_or_default())?;
                return Ok(shortcut::Target { exe: flatpak.clone(), program: flatpak, args: vec!["run".into(), id], env: vec![], dir, extract_appimage: false, steam_proton: false, options });
            }
            match install::find_executable(&dir, preferred, if wine { "windows" } else { OS }) {
                // A script that installs the app somewhere else: what it installed, once that's there (script.rs).
                Some(exe) if cfg!(windows) && script::installs(&dir, &exe) => (script::installed(&dir).unwrap_or(exe), wine),
                Some(exe) => (exe, wine),
                // A folder with only a Windows build runs through Wine on Linux.
                None if OS == "linux" && !wine => (install::find_executable(&dir, preferred, "windows").ok_or(NO_PROGRAM)?, true),
                None => return Err(NO_PROGRAM.into()),
            }
        }
    };
    let quoted = || vec![exe.to_string_lossy().into_owned()];
    let mut env = Vec::new();
    // Through a Proton, each app gets its own Wine prefix, as Steam gives each game.
    let mut through_proton = |path: &str| -> Result<(PathBuf, Vec<String>, bool), String> {
        let prefix = data.0.join("proton").join(install::plain_name(folder)?);
        std::fs::create_dir_all(&prefix).map_err(|e| format!("Couldn't make a folder for Proton: {e}"))?;
        env.push(("STEAM_COMPAT_DATA_PATH".to_string(), prefix.to_string_lossy().into_owned()));
        env.push(("STEAM_COMPAT_CLIENT_INSTALL_PATH".to_string(), launch::steam_client().to_string_lossy().into_owned()));
        Ok((launch::proton_script(path)?, [vec!["run".to_string()], quoted()].concat(), true))
    };
    let (program, args, steam_proton) = match &options.runner {
        Some(launch::Runner::Direct) => (exe.clone(), vec![], false),
        Some(launch::Runner::Wine) => (PathBuf::from("wine"), quoted(), false),
        Some(launch::Runner::Proton { path }) => through_proton(path)?,
        None if wine => match &options.windows_runner {
            // What Settings runs every Windows program with.
            Some(launch::Runner::Proton { path }) => through_proton(path)?,
            Some(launch::Runner::Wine) => (PathBuf::from("wine"), quoted(), false),
            // Automatic: a Proton Steam has, which brings what games need that Wine alone often lacks; with none, Wine.
            _ => match automatic_proton() {
                Some(proton) => through_proton(&proton.path)?,
                None => (PathBuf::from("wine"), quoted(), true),
            },
        },
        None if OS == "macos" && exe.extension().is_some_and(|e| e == "app") => (PathBuf::from("open"), quoted(), false),
        // A shortcut the player picked: Explorer starts what it points at.
        None if OS == "windows" && exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("lnk")) => (PathBuf::from("explorer"), quoted(), false),
        None => (exe.clone(), vec![], false),
    };
    let copies = &options.copies;
    let mut options = launch::parse(options.command.as_deref().unwrap_or_default())?;
    // The player's own copies of the game, as the catalog says to give them (own_copy.rs), before their own options.
    // Through Wine or Proton (Quiver's or Steam's), the game reads a Windows path.
    let (copy_args, copy_env) = launch::copy_words(copies, OS == "linux" && (program != exe || steam_proton));
    options.after.splice(0..0, copy_args);
    options.env.splice(0..0, copy_env);
    // `open` keeps the arguments it's given for itself unless they follow --args.
    if program == Path::new("open") && !options.after.is_empty() && !options.after.iter().any(|a| a == "--args") {
        options.after.insert(0, "--args".into());
    }
    // Many distros no longer ship FUSE 2, which AppImages mount themselves with.
    let extract_appimage = exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("appimage")) && !has_fuse2();
    Ok(shortcut::Target { dir: exe.parent().unwrap_or(Path::new(".")).to_path_buf(), exe, program, args, env, extract_appimage, steam_proton, options })
}

/// An app the launcher started has closed: how long it ran, for its play time.
#[derive(Clone, Serialize)]
struct Played {
    id: String,
    seconds: u64,
}

/// Not on the window's thread: on Android, starting an app goes through the system, which answers on that thread.
#[tauri::command]
#[allow(clippy::too_many_arguments)]
async fn launch(
    app: tauri::AppHandle,
    data: State<'_, Data>,
    id: Option<String>,
    folder: String,
    dir: Option<String>,
    preferred: Vec<String>,
    wine: bool,
    program: Option<String>,
    launch: Option<launch::LaunchOptions>,
    presence: Option<discord::Game>,
) -> Result<(), String> {
    #[cfg(target_os = "android")]
    {
        let _ = (preferred, wine, program, launch, presence);
        let package = android::package(&app_dir(&data, &folder, dir)?).ok_or("This app has no Android version installed. Get its Android download to play it here.")?;
        android::launch(&package)?;
        // Its play time: until the player comes back to Quiver.
        *app.state::<Playing>().0.lock().unwrap() = id.map(|id| (id, std::time::Instant::now()));
        Ok(())
    }
    #[cfg(not(target_os = "android"))]
    start(app, &data, id, &folder, dir, &preferred, wine, program, &launch.unwrap_or_default(), presence)
}

/// Waits for a started program to close; whether that could be told. On Android too, as session.rs names it.
type Waiting = Box<dyn FnOnce() -> bool + Send>;
/// Whether Android no longer has an app Quiver installed from an APK: the player removed it in the system's settings.
/// False for an app that wasn't installed that way, and everywhere else. Not on the window's thread, as `launch` isn't.
#[tauri::command]
async fn android_app_gone(data: State<'_, Data>, folder: String, dir: Option<String>) -> Result<bool, String> {
    #[cfg(target_os = "android")]
    {
        let Some(package) = android::package(&app_dir(&data, &folder, dir)?) else { return Ok(false) };
        android::is_installed(&package).map(|installed| !installed)
    }
    #[cfg(not(target_os = "android"))]
    {
        let _ = (data, folder, dir);
        Ok(false)
    }
}

#[cfg(not(target_os = "android"))]
#[allow(clippy::too_many_arguments)]
fn start(
    app: tauri::AppHandle,
    data: &Data,
    id: Option<String>,
    folder: &str,
    dir: Option<String>,
    preferred: &[String],
    wine: bool,
    program: Option<String>,
    launch: &launch::LaunchOptions,
    presence: Option<discord::Game>,
) -> Result<(), String> {
    let app_folder = app_dir(data, folder, dir.clone()).ok();
    let target = target(data, folder, dir, preferred, wine, program, launch)?;
    let (program, args, env) = target.command();
    let wine = program == Path::new("wine");
    let mut command = host::command(&program);
    command.args(&args).envs(env).current_dir(&target.dir);
    let spawned: std::io::Result<session::Started> = sandbox::check(&program)
        .and_then(|_| {
            sandbox::game(command)
                // The game outlives the launcher's console; it shouldn't hold it open.
                .stdin(std::process::Stdio::null())
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .spawn()
        })
        .map(session::Started::Child);
    // One that needs administrator rights: Windows asks the player, as a double click would.
    #[cfg(windows)]
    let spawned = spawned.or_else(|e| match e.raw_os_error() {
        Some(elevate::REQUIRED) => elevate::start(&program, &args, &target.dir)
            .map(|process| session::Started::Elsewhere(Box::new(move || process.is_some_and(|p| p.wait())) as Waiting)),
        _ => Err(e),
    });
    spawned
        .map(|game| {
            // The app's install script: Quiver looks for what it installs from now on.
            if let Some(folder) = app_folder.as_ref().filter(|f| cfg!(windows) && target.exe.starts_with(f) && script::installs(f, &target.exe)) {
                script::ran(folder);
            }
            // Its play time: from now until it closes, or until a program it handed off to does (session.rs).
            let shown = id.as_ref().is_some_and(|id| {
                app.state::<Running>().started(id);
                // On the player's Discord profile while it runs (discord.rs).
                presence.map(|game| app.state::<discord::Presence>().started(id, game)).is_some()
            });
            // Its folder tells the game's other processes apart only when it's the app's own: a program elsewhere may sit
            // in a folder others share (/usr/bin, say), whose every program would count as the game.
            let own = app_folder.as_ref().is_some_and(|f| session::inside(&target.exe, f)).then(|| target.dir.clone());
            session::track(game, own, move |seconds| {
                let Some(id) = id else { return };
                app.state::<Running>().closed(&id);
                if shown {
                    app.state::<discord::Presence>().closed(&id);
                }
                if let Some(seconds) = seconds {
                    let _ = app.emit("app-closed", Played { id, seconds });
                }
            });
        })
        .map_err(|e| match e.kind() {
            // No, at Windows' prompt for administrator rights.
            #[cfg(windows)]
            _ if e.raw_os_error() == Some(elevate::DECLINED) => "cancelled".into(),
            // Automatic found no Proton either; or the player chose Wine.
            std::io::ErrorKind::NotFound if wine && target.steam_proton => "This is a Windows app. Install Steam (it brings Proton) or Wine to play it on Linux.".into(),
            std::io::ErrorKind::NotFound if wine => "Wine isn't installed. Install it, or choose a Proton under Run with, in Settings or the app's launch options.".into(),
            std::io::ErrorKind::NotFound if target.program.ends_with("flatpak") => "This app is a Flatpak, and Flatpak isn't installed on this computer.".into(),
            std::io::ErrorKind::NotFound if program != target.exe => format!("Couldn't find {} on this computer. Check the app's launch options.", program.display()),
            // A downloaded AppImage or program isn't executable until it's allowed to run.
            std::io::ErrorKind::PermissionDenied if OS != "windows" => format!(
                "{} isn't allowed to run as a program. Allow it in its file properties (Permissions), or run chmod +x on it, then press Play again.",
                target.exe.display()
            ),
            _ => format!("Couldn't start {}: {e}", target.exe.display()),
        })
}

/// The Android app the player last started from Quiver, and when.
/// Apps the launcher started that haven't closed yet, by app id: how many of each.
#[derive(Default)]
struct Running(std::sync::Mutex<std::collections::HashMap<String, usize>>);

impl Running {
    fn started(&self, id: &str) {
        *self.0.lock().unwrap().entry(id.to_string()).or_default() += 1;
    }

    fn closed(&self, id: &str) {
        let mut running = self.0.lock().unwrap();
        if let Some(n) = running.get_mut(id) {
            *n -= 1;
            if *n == 0 {
                running.remove(id);
            }
        }
    }

    fn has(&self, id: &str) -> bool {
        self.0.lock().unwrap().contains_key(id)
    }
}

/// An app, as the window names it for its saves (saves.rs).
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
struct SavesOf {
    id: String,
    folder: String,
    dir: Option<String>,
    /// A Windows build run through Wine or Proton here: its saves are in its prefix.
    #[serde(default)]
    wine: bool,
    /// Where the catalog says it keeps its saves (`launcher.saves.locations`).
    #[serde(default)]
    locations: Vec<saves::Location>,
    version: Option<String>,
    /// What it's called, to find folders named like it: its name, its folder, its repository.
    #[serde(default)]
    names: Vec<String>,
}

/// What an app's Saves tab shows.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SavesInfo {
    places: Vec<saves::Shown>,
    /// Who said where they are: "you", "catalog", "found" or "none".
    from: &'static str,
    backups: Vec<saves::Backup>,
    /// The player switched cloud saves off for this app.
    cloud_off: bool,
    /// Signs that the places hold more than saves (saves::looks), for the tab to ask about.
    look: Option<saves::Look>,
}

/// One backup or restore at a time, so two never read and write the same saves.
static SAVES_BUSY: std::sync::Mutex<()> = std::sync::Mutex::new(());
/// What was in each running app's likely save places when it started, by app id.
#[derive(Default)]
struct Watching(std::sync::Mutex<std::collections::HashMap<String, saves::Watch>>);

const CLOSE_FIRST: &str = "Close the app first.";

/// A Windows user's folder in the prefix a Windows app runs in here: the one Quiver keeps for Proton, else Wine's own.
fn wine_user(data: &Data, folder: &str) -> Option<PathBuf> {
    let users = Path::new("drive_c").join("users");
    let proton = install::plain_name(folder).ok().map(|name| data.0.join("proton").join(name).join("pfx").join(&users).join("steamuser"));
    let wine = || {
        let prefix = std::env::var_os("WINEPREFIX").map(PathBuf::from).or_else(|| own_copy::host_home().map(|h| h.join(".wine")))?;
        Some(prefix.join(&users).join(std::env::var("USER").ok()?))
    };
    proton.filter(|p| p.is_dir()).or_else(|| wine().filter(|p| p.is_dir()))
}

/// Where an app's save roots are on this computer, its backups, and the system its saves are for.
fn saves_at(data: &Data, of: &SavesOf) -> Result<(saves::Roots, PathBuf, &'static str), String> {
    let app = app_dir(data, &of.folder, of.dir.clone())?;
    let store = data.0.join("saves").join(saves::store_name(&of.id));
    if of.wine && OS != "windows" {
        let roots = match wine_user(data, &of.folder) {
            Some(user) => saves::Roots::windows_user(app, &user),
            // No prefix yet: only its own folder is known.
            None => saves::Roots { app, ..Default::default() },
        };
        return Ok((roots, store, "windows"));
    }
    let root = |name: &str| own_copy::root_dir(Some(name), &app, false).ok();
    let home = own_copy::host_home();
    let documents = if sandbox::id().is_some() { home.as_ref().map(|h| h.join("Documents")) } else { dirs::document_dir().or_else(|| home.as_ref().map(|h| h.join("Documents"))) };
    let roots = saves::Roots { data: root("data"), config: root("config"), local_data: root("localData"), documents, home, app };
    Ok((roots, store, OS))
}

fn saves_shown(data: &Data, of: &SavesOf) -> Result<SavesInfo, String> {
    let (roots, store, os) = saves_at(data, of)?;
    let state = saves::state(&store);
    let (places, from) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let look = saves::looks(&store, &state, &places);
    Ok(SavesInfo { places: saves::shown(&places, &roots, &saves::chosen_abs(&state)), from, backups: saves::list(&store), cloud_off: state.cloud_off, look })
}

/// Where an app's saves are and its backups, for its Saves tab.
#[tauri::command]
async fn saves_info(data: State<'_, Data>, of: SavesOf) -> Result<SavesInfo, String> {
    saves_shown(&data, &of)
}

/// Backs up an app's saves now, if they changed since the last backup: "manual" from its Saves tab, "update" and
/// "uninstall" before those. Null when there was nothing new to keep. Only a manual one says there are no saves.
#[tauri::command]
async fn saves_backup(data: State<'_, Data>, running: State<'_, Running>, of: SavesOf, reason: String) -> Result<Option<saves::Backup>, String> {
    if !["manual", "update", "uninstall"].contains(&reason.as_str()) {
        return Err("Unknown reason for a backup.".into());
    }
    if running.has(&of.id) {
        return Err(CLOSE_FIRST.into());
    }
    let (roots, store, os) = saves_at(&data, &of)?;
    let state = saves::state(&store);
    let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let made = saves::backup(&store, &places, &roots, &saves::chosen_abs(&state), &saves::Stamp { app: &of.id, version: of.version.as_deref(), reason: &reason, os })?;
    if made.is_none() && reason == "manual" && saves::shown(&places, &roots, &saves::chosen_abs(&state)).iter().all(|p| !p.exists) {
        return Err(saves::NO_SAVES.into());
    }
    Ok(made)
}

/// The folders restoring a backup would write to that this computer doesn't keep the app's saves in yet, for the
/// player to agree to before `saves_restore`. None for a backup of the places known here; refused when one of its
/// places is nowhere an app saves.
#[tauri::command]
async fn saves_restore_plan(data: State<'_, Data>, of: SavesOf, name: String) -> Result<Vec<saves::Shown>, String> {
    let (roots, store, os) = saves_at(&data, &of)?;
    let state = saves::state(&store);
    let (known, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    saves::restore_plan(&store, &name, &roots, &saves::chosen_abs(&state), &saves::Trust { known: &known, confirmed: &[], names: &of.names })
}

/// Puts a backup back, after backing up what's there. Resolves to how many files it put back. Its files only go to
/// the places this computer keeps the app's saves in, and to those of `saves_restore_plan` the player agreed to
/// (`confirmed`), which are remembered as places the app saves in.
#[tauri::command]
async fn saves_restore(data: State<'_, Data>, running: State<'_, Running>, of: SavesOf, name: String, confirmed: Option<Vec<String>>) -> Result<usize, String> {
    if running.has(&of.id) {
        return Err(CLOSE_FIRST.into());
    }
    let (roots, store, os) = saves_at(&data, &of)?;
    let mut state = saves::state(&store);
    let (known, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let confirmed = confirmed.unwrap_or_default();
    let trust = saves::Trust { known: &known, confirmed: &confirmed, names: &of.names };
    let abs = saves::chosen_abs(&state);
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    // Only the places the backup really has, whatever the window sent.
    let agreed: Vec<String> = saves::restore_plan(&store, &name, &roots, &abs, &trust)?.into_iter().map(|p| p.template).filter(|t| confirmed.contains(t)).collect();
    let put = saves::restore(&store, &name, &roots, &abs, &saves::Stamp { app: &of.id, version: of.version.as_deref(), reason: "restore", os }, &trust)?;
    if !agreed.is_empty() {
        state.found = saves::merge(&state.found, &agreed);
        saves::set_state(&store, &state)?;
    }
    Ok(put)
}

/// Removes one of an app's backups.
#[tauri::command]
async fn saves_remove(data: State<'_, Data>, id: String, name: String) -> Result<(), String> {
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    saves::remove(&data.0.join("saves").join(saves::store_name(&id)), &name)
}

/// The player's own answer to where an app saves (a folder they picked), or none to go back to what Quiver works out.
#[tauri::command]
async fn saves_choose(data: State<'_, Data>, of: SavesOf, path: Option<String>) -> Result<SavesInfo, String> {
    let (roots, store, _) = saves_at(&data, &of)?;
    let mut state = saves::state(&store);
    state.chosen = match path {
        Some(path) => {
            let path = PathBuf::from(path);
            if !path.is_dir() {
                return Err("That folder isn't there.".into());
            }
            vec![saves::templatize(&path, &roots)?]
        }
        None => vec![],
    };
    saves::set_state(&store, &state)?;
    saves_shown(&data, &of)
}

/// Opens one of an app's save places in the file manager: one of the places its Saves tab lists, never any other path.
#[tauri::command]
async fn saves_open(data: State<'_, Data>, of: SavesOf, template: String) -> Result<(), String> {
    let (roots, store, os) = saves_at(&data, &of)?;
    let state = saves::state(&store);
    let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    if !places.contains(&template) {
        return Err("That isn't one of this app's save places.".into());
    }
    let folder = saves::folder_of(&saves::resolve(&template, &roots, &saves::chosen_abs(&state))?);
    if !folder.is_dir() {
        return Err("This folder isn't there yet. It's made when the app first saves.".into());
    }
    reveal(&folder)
}

/// Before an app starts: what's in its likely save places, to tell afterwards where it saved (`saves_played`).
#[tauri::command]
async fn saves_watch(data: State<'_, Data>, watching: State<'_, Watching>, of: SavesOf) -> Result<(), String> {
    let (roots, _, _) = saves_at(&data, &of)?;
    let watch = saves::watch_start(&roots, &of.names);
    watching.0.lock().unwrap().insert(of.id.clone(), watch);
    Ok(())
}

/// After an app closed: the places it wrote saves to are remembered, and its saves backed up if they changed.
#[tauri::command]
async fn saves_played(data: State<'_, Data>, running: State<'_, Running>, watching: State<'_, Watching>, of: SavesOf) -> Result<Option<saves::Backup>, String> {
    // Started again already (or a second copy still runs): the next close backs up.
    if running.has(&of.id) {
        return Ok(None);
    }
    let (roots, store, os) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let mut state = saves::state(&store);
    let watch = watching.0.lock().unwrap().remove(&of.id);
    if let Some(watch) = watch {
        let found = saves::merge(&state.found, &saves::watch_end(watch, &roots));
        if found != state.found {
            state.found = found;
            saves::set_state(&store, &state)?;
        }
    }
    let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    saves::backup(&store, &places, &roots, &saves::chosen_abs(&state), &saves::Stamp { app: &of.id, version: of.version.as_deref(), reason: "played", os })
}

/// What to tell the catalog about where an app saves.
#[derive(Serialize)]
struct SavesReport {
    /// The system the places are for: "windows" for a Windows build run through Wine or Proton.
    os: &'static str,
    places: Vec<String>,
}

/// An app's saves as cloud saves compares them (cloud-saves.ts).
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SavesCloud {
    /// The checksum of the saves as they are now; none when there are none here.
    hash: Option<String>,
    /// Their checksum when they last matched the account's newest copy.
    synced: Option<String>,
    /// The player switched cloud saves off for this app.
    off: bool,
    os: &'static str,
}

#[tauri::command]
async fn saves_cloud(data: State<'_, Data>, of: SavesOf) -> Result<SavesCloud, String> {
    let (roots, store, os) = saves_at(&data, &of)?;
    let state = saves::state(&store);
    let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let hash = saves::current_hash(&places, &roots, &saves::chosen_abs(&state))?;
    Ok(SavesCloud { hash, synced: state.synced, off: state.cloud_off, os })
}

/// Remembers that the saves with this checksum match the account's newest copy, or with none that nothing does.
#[tauri::command]
async fn saves_synced(data: State<'_, Data>, of: SavesOf, hash: Option<String>) -> Result<(), String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let mut state = saves::state(&store);
    if state.synced != hash {
        state.synced = hash;
        saves::set_state(&store, &state)?;
    }
    Ok(())
}

/// Switches cloud saves off, or back on, for one app.
#[tauri::command]
async fn saves_cloud_off(data: State<'_, Data>, of: SavesOf, off: bool) -> Result<(), String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let mut state = saves::state(&store);
    state.cloud_off = off;
    saves::set_state(&store, &state)
}

/// The biggest copy the account takes, which is also the most a download may hold.
const MAX_CLOUD_COPY: u64 = 100 * 1024 * 1024;
const CLOUD_UNREACHABLE: &str = "Couldn't reach cloud saves. Check your connection and try again.";

/// A link the site signed for one copy: https, or this computer itself (tests).
fn cloud_link(url: &str) -> Result<reqwest::Url, String> {
    let link = reqwest::Url::parse(url).map_err(|_| CLOUD_UNREACHABLE.to_string())?;
    let local = matches!(link.host_str(), Some("127.0.0.1" | "localhost"));
    if link.scheme() == "https" || (link.scheme() == "http" && local) {
        Ok(link)
    } else {
        Err(CLOUD_UNREACHABLE.into())
    }
}

fn cloud_client() -> Result<reqwest::Client, String> {
    reqwest::Client::builder().user_agent("QuiverLauncher/4").connect_timeout(std::time::Duration::from_secs(15)).build().map_err(|e| e.to_string())
}

/// Sends one of an app's backups to the link the site signed for it, with the headers that link was signed with.
#[tauri::command]
async fn saves_upload(data: State<'_, Data>, id: String, name: String, url: String, headers: std::collections::HashMap<String, String>) -> Result<(), String> {
    let file = saves::file_of(&data.0.join("saves").join(saves::store_name(&id)), &name)?;
    let body = tokio::fs::read(&file).await.map_err(|e| format!("Couldn't read that backup: {e}"))?;
    if body.len() as u64 > MAX_CLOUD_COPY {
        return Err("That backup is too big for cloud saves.".into());
    }
    let mut request = cloud_client()?.put(cloud_link(&url)?);
    // Its length is the body's own; the rest is what the link was signed with.
    for (key, value) in headers.iter().filter(|(key, _)| !key.eq_ignore_ascii_case("content-length")) {
        request = request.header(key.as_str(), value.as_str());
    }
    let response = request.body(body).send().await.map_err(|_| CLOUD_UNREACHABLE.to_string())?;
    if !response.status().is_success() {
        return Err(format!("Cloud saves didn't take that backup ({}).", response.status().as_u16()));
    }
    Ok(())
}

/// Downloads a copy from the account's link and takes it in among the app's backups, checked against the checksum
/// the account has for it. Nothing of the player's saves is touched, and nothing it says about where the app saves
/// is taken up: `saves_restore` puts it in place, in the places this computer knows or the player agrees to.
#[tauri::command]
async fn saves_download(data: State<'_, Data>, of: SavesOf, url: String, size: u64, hash: String) -> Result<saves::Backup, String> {
    let (_, store, _) = saves_at(&data, &of)?;
    if size == 0 || size > MAX_CLOUD_COPY {
        return Err(saves::BAD_COPY.into());
    }
    let mut response = cloud_client()?.get(cloud_link(&url)?).send().await.map_err(|_| CLOUD_UNREACHABLE.to_string())?;
    if !response.status().is_success() {
        return Err(CLOUD_UNREACHABLE.into());
    }
    std::fs::create_dir_all(&store).map_err(|e| e.to_string())?;
    // Never a name a backup has, so half a download is never listed as one.
    let part = store.join(format!(".cloud-{}.part", std::process::id()));
    let written = async {
        use tokio::io::AsyncWriteExt;
        let mut file = tokio::fs::File::create(&part).await.ok()?;
        let mut total = 0u64;
        while let Some(chunk) = response.chunk().await.ok()? {
            total += chunk.len() as u64;
            if total > size {
                return None;
            }
            file.write_all(&chunk).await.ok()?;
        }
        file.flush().await.ok()?;
        (total == size).then_some(())
    }
    .await;
    if written.is_none() {
        let _ = std::fs::remove_file(&part);
        return Err(saves::BAD_COPY.into());
    }
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    saves::adopt(&store, &part, &hash)
}

/// The folder save backups are also kept in for the folder the player picked (sync_folder.rs): that one, or a
/// "Quiver Saves" folder inside it.
#[tauri::command]
async fn saves_folder_choose(data: State<'_, Data>, path: String) -> Result<String, String> {
    let (picked, own) = (PathBuf::from(path), data.0.clone());
    let folder = tauri::async_runtime::spawn_blocking(move || sync_folder::choose(&picked, &own)).await.map_err(|e| e.to_string())??;
    Ok(folder.to_string_lossy().into())
}

/// What the sync folder holds, by app, for Settings. `there`: false when the folder can't be found now.
#[derive(Serialize)]
struct SyncFolderKept {
    there: bool,
    apps: Vec<sync_folder::AppKept>,
}

#[tauri::command]
async fn saves_folder_summary(folder: String) -> Result<SyncFolderKept, String> {
    // Off the window's thread: a folder on a drive that's asleep, or on the network, can take a while.
    tauri::async_runtime::spawn_blocking(move || {
        let folder = PathBuf::from(folder);
        SyncFolderKept { there: folder.is_dir(), apps: sync_folder::summary(&folder) }
    })
    .await
    .map_err(|e| e.to_string())
}

#[tauri::command]
fn saves_folder_open(folder: String) -> Result<(), String> {
    let folder = PathBuf::from(folder);
    if !folder.is_dir() {
        return Err(sync_folder::GONE.into());
    }
    reveal(&folder)
}

/// An app's saves beside the sync folder's backups of them, as they're compared before it starts (sync-folder.ts).
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SavesFolder {
    /// The checksum of the saves as they are now; none when there are none here.
    hash: Option<String>,
    /// The checksum they had when they last matched the newest backup in this folder.
    synced: Option<String>,
    os: &'static str,
    /// False when the folder can't be found now: a drive that isn't plugged in.
    there: bool,
    /// The folder's backups of the app, newest first. The first arrived whole.
    copies: Vec<sync_folder::Kept>,
}

#[tauri::command]
async fn saves_folder_state(data: State<'_, Data>, of: SavesOf, folder: String) -> Result<SavesFolder, String> {
    let (roots, store, os) = saves_at(&data, &of)?;
    let state = saves::state(&store);
    let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let hash = saves::current_hash(&places, &roots, &saves::chosen_abs(&state))?;
    let synced = state.folder_synced.filter(|s| s.folder == folder).map(|s| s.hash);
    let at = Path::new(&folder);
    Ok(SavesFolder { hash, synced, os, there: at.is_dir(), copies: sync_folder::list(at, &of.id, true) })
}

/// Remembers that the saves with this checksum match the newest backup in this sync folder, or with none that
/// nothing does.
fn folder_synced(store: &Path, folder: &str, hash: Option<String>) -> Result<(), String> {
    let mut state = saves::state(store);
    let synced = hash.map(|hash| saves::FolderSynced { folder: folder.to_string(), hash });
    if state.folder_synced != synced {
        state.folder_synced = synced;
        saves::set_state(store, &state)?;
    }
    Ok(())
}

#[tauri::command]
async fn saves_folder_synced(data: State<'_, Data>, of: SavesOf, folder: String, hash: Option<String>) -> Result<(), String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    folder_synced(&store, &folder, hash)
}

/// Keeps one of an app's backups in the sync folder too, as the newest there, and remembers the two match.
#[tauri::command]
async fn saves_folder_put(data: State<'_, Data>, of: SavesOf, folder: String, name: String) -> Result<sync_folder::Kept, String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let file = saves::file_of(&store, &name)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let label = of.names.first().map(String::as_str).unwrap_or_default();
    let kept = sync_folder::put(Path::new(&folder), &of.id, label, &file)?;
    folder_synced(&store, &folder, Some(kept.hash.clone()))?;
    Ok(kept)
}

/// Takes one of the sync folder's backups in among the app's own. Nothing of the player's saves is touched, and
/// nothing it says about where the app saves is taken up: `saves_restore` puts it in place, in the places this
/// computer knows or the player agrees to.
#[tauri::command]
async fn saves_folder_take(data: State<'_, Data>, of: SavesOf, folder: String, name: String) -> Result<saves::Backup, String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    sync_folder::take(Path::new(&folder), &of.id, &name, &store)
}

/// The places the launcher saw an app save in that the catalog hasn't been told about (`saves::to_report`). None
/// for an app whose saves the catalog lists.
#[tauri::command]
async fn saves_to_report(data: State<'_, Data>, of: SavesOf) -> Result<SavesReport, String> {
    let (roots, store, os) = saves_at(&data, &of)?;
    let places = if saves::catalog_templates(&of.locations, os).is_empty() { saves::to_report(&saves::state(&store), &roots) } else { vec![] };
    Ok(SavesReport { os, places })
}

/// The player said the places Quiver backs up hold their saves, though a backup of them looked like more.
#[tauri::command]
async fn saves_fine(data: State<'_, Data>, of: SavesOf) -> Result<SavesInfo, String> {
    let (_, store, os) = saves_at(&data, &of)?;
    {
        let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
        let mut state = saves::state(&store);
        let (places, _) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
        state.fine = Some(saves::places_key(&places));
        saves::set_state(&store, &state)?;
    }
    saves_shown(&data, &of)
}

/// What to tell the catalog about how an app's backups look (saves::Look).
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SavesWarning {
    os: &'static str,
    /// Where the places came from: "catalog", "found" or "player".
    source: &'static str,
    #[serde(flatten)]
    look: saves::Look,
    /// The player said the places hold their saves.
    fine: bool,
    /// What it's remembered by once sent (`saves_warned`).
    key: String,
}

/// How an app's backups look, when the catalog hasn't been told yet: their signs, or that the player said they're
/// saves. None when there's nothing to tell.
#[tauri::command]
async fn saves_warning(data: State<'_, Data>, of: SavesOf) -> Result<Option<SavesWarning>, String> {
    let (_, store, os) = saves_at(&data, &of)?;
    let mut state = saves::state(&store);
    let (places, from) = saves::places(&state, &saves::catalog_templates(&of.locations, os));
    let source = match from {
        "catalog" => "catalog",
        "found" => "found",
        "you" => "player",
        _ => return Ok(None),
    };
    let key = saves::places_key(&places);
    let fine = state.fine.as_deref() == Some(key.as_str());
    // Asked about what the newest backup shows, without the player's word.
    state.fine = None;
    let Some(look) = saves::looks(&store, &state, &places) else { return Ok(None) };
    let key = if fine { format!("fine:{key}") } else { look.report_key(&key) };
    if state.warned.contains(&key) {
        return Ok(None);
    }
    Ok(Some(SavesWarning { os, source, look, fine, key }))
}

/// The catalog took (or refused) a report of how an app's backups look: it isn't sent again.
#[tauri::command]
async fn saves_warned(data: State<'_, Data>, of: SavesOf, key: String) -> Result<(), String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let mut state = saves::state(&store);
    if !state.warned.contains(&key) {
        state.warned.push(key);
        // A few sets of places and signs per app; the oldest are long out of date.
        if state.warned.len() > 20 {
            state.warned.remove(0);
        }
        saves::set_state(&store, &state)?;
    }
    Ok(())
}

/// The catalog took (or refused) a report of these places: they aren't sent again.
#[tauri::command]
async fn saves_reported(data: State<'_, Data>, of: SavesOf, places: Vec<String>) -> Result<(), String> {
    let (_, store, _) = saves_at(&data, &of)?;
    let _busy = SAVES_BUSY.lock().unwrap_or_else(|e| e.into_inner());
    let mut state = saves::state(&store);
    let before = state.reported.len();
    for place in places {
        if state.found.contains(&place) && !state.reported.contains(&place) {
            state.reported.push(place);
        }
    }
    if state.reported.len() != before {
        saves::set_state(&store, &state)?;
    }
    Ok(())
}

#[cfg(target_os = "android")]
#[derive(Default)]
struct Playing(std::sync::Mutex<Option<(String, std::time::Instant)>>);

/// The app's icon, saved for shortcuts; none if it can't be fetched.
async fn shortcut_icon(data: &Data, request: &shortcut::ShortcutRequest) -> Option<PathBuf> {
    let url = request.art.icon.as_deref()?;
    shortcut::save_icon(url, &data.0, &data.0.join("icons").join(install::plain_name(&request.folder).ok()?)).await.ok()
}

/// Puts a shortcut that starts the game on the desktop.
#[tauri::command]
async fn create_shortcut(app: tauri::AppHandle, request: shortcut::ShortcutRequest) -> Result<String, String> {
    let data = app.state::<Data>();
    let target = target(&data, &request.folder, request.dir.clone(), &request.preferred, request.wine, request.program.clone(), &request.launch)?;
    let icon = shortcut_icon(&data, &request).await;
    let path = shortcut::desktop(&target, &request.name, icon.as_deref())?;
    Ok(format!("{} is on your desktop.", path.file_name().unwrap_or_default().to_string_lossy()))
}

/// Adds the game to Steam as a non-Steam game, with its artwork; says what happened.
#[tauri::command]
async fn add_to_steam(app: tauri::AppHandle, request: shortcut::ShortcutRequest) -> Result<String, String> {
    let data = app.state::<Data>();
    let target = target(&data, &request.folder, request.dir.clone(), &request.preferred, request.wine, request.program.clone(), &request.launch)?;
    steam::account()?;
    let icon = shortcut_icon(&data, &request).await;
    let folder = install::plain_name(&request.folder)?;
    let mut art: Vec<(String, PathBuf)> = Vec::new();
    let pictures = [("", &request.art.header), ("p", &request.art.capsule), ("_hero", &request.art.hero), ("_logo", &request.art.logo), ("_icon", &request.art.icon)];
    for (suffix, url) in pictures {
        let Some(url) = url else { continue };
        let path = data.0.join("steam-art").join(format!("{folder}{suffix}.png"));
        if let Ok(image) = shortcut::download_image(url, &data.0).await {
            if std::fs::create_dir_all(path.parent().unwrap()).is_ok() && image.save(&path).is_ok() {
                art.push((suffix.into(), path));
            }
        }
    }
    // On Linux a `.desktop` names the app's own program, so Steam stores it as
    // the shortcut's program and starts it in the game's folder (a game that
    // needs environment variables is named through a script instead). A Windows
    // game Steam itself runs with Proton is named the same way, so Steam keeps
    // its Proton and the player's launch options.
    #[cfg(target_os = "linux")]
    {
        if target.steam_proton {
            return add_proton(&data, &request.name, folder, &target, icon.as_deref(), art);
        }
        return add_native(&data, &request.name, folder, &target, icon.as_deref(), art);
    }
    #[cfg(not(target_os = "linux"))]
    {
        let mut shortcut = steam::Shortcut::new(&request.name, &target, icon.as_deref());
        shortcut.art = art;
        let proton = if shortcut.proton { " It's set to run with Proton." } else { "" };
        if !steam::running() {
            steam::apply(&shortcut)?;
            return Ok(format!("{} is in Steam.{proton} Start Steam to see it.", request.name));
        }
        // Steam overwrites its shortcuts as it closes, so a waiting copy writes them after.
        if steam_runs_us() {
            return Err("Steam is running Quiver, so it can't add games now. Switch to Desktop Mode (or close Steam) and add it from there.".into());
        }
        queue_steam(&data, folder, &shortcut)?;
        Ok(format!("{} will be added to Steam when Steam closes.{proton} Restart Steam to see it.", request.name))
    }
}

/// Adds a Windows game Steam runs with its own Proton: a `.desktop` naming the
/// game, so Steam stores the game as the program and applies a compat tool. It
/// is added at once through the running Steam, or written directly once Steam
/// closes. Linux only.
#[cfg(target_os = "linux")]
fn add_proton(data: &Data, name: &str, folder: &str, target: &shortcut::Target, icon: Option<&Path>, art: Vec<(String, PathBuf)>) -> Result<String, String> {
    let launcher = steam::Launcher::write_proton(&data.0, folder, name, target, icon)?;
    let mut shortcut = steam::Shortcut::new(name, target, icon);
    shortcut.shortcut_path = launcher.desktop().to_string_lossy().into_owned();
    shortcut.art = art;
    // Steam reads the grid folder as it loads, so artwork added now shows after a restart; the shortcut itself is already there.
    let art_note = if shortcut.art.is_empty() { "" } else { " Artwork will appear after restarting Steam." };
    if !steam::running() {
        steam::apply(&shortcut)?;
        return Ok(format!("{name} is in Steam. It's set to run with Proton. Start Steam to see it."));
    }
    // Steam is running: it adds the shortcut itself, at once, and its own write is what lasts.
    let ids = match steam::existing_id(&launcher) {
        Some(id) => vec![id],
        None => match steam::add_now(launcher.desktop()) {
            Ok(()) => steam_ids(&launcher, name),
            // Steam wouldn't take it now: a waiting copy adds it once Steam closes.
            Err(_) => {
                queue_steam(data, folder, &shortcut)?;
                return Ok(format!("{name} will be added to Steam when Steam closes. Restart Steam to see it."));
            }
        },
    };
    if let Ok((user, _)) = steam::account() {
        for id in ids {
            let _ = steam::place_art(&user, id, &shortcut.art);
        }
    }
    Ok(format!("{name} is in Steam. It's set to run with Proton.{art_note}"))
}

/// Adds a native Linux shortcut: a `.desktop` naming the game's own program, so
/// Steam stores the program and starts it in the game's folder. Added at once
/// through the running Steam, which writes it itself, or written directly once
/// Steam closes. Linux only.
#[cfg(target_os = "linux")]
fn add_native(data: &Data, name: &str, folder: &str, target: &shortcut::Target, icon: Option<&Path>, art: Vec<(String, PathBuf)>) -> Result<String, String> {
    let launcher = steam::Launcher::write(&data.0, folder, name, target, icon)?;
    let shortcut = launcher.shortcut(name, target, icon, art);
    // Steam reads the grid folder as it loads, so artwork added now shows after a restart; the shortcut itself is already there.
    let art_note = if shortcut.art.is_empty() { "" } else { " Artwork will appear after restarting Steam." };
    if !steam::running() {
        steam::apply(&shortcut)?;
        return Ok(format!("{name} is in Steam. Start Steam to see it."));
    }
    // Steam is running: it adds the shortcut itself, at once, and its own write is what lasts.
    let ids = match steam::existing_id(&launcher) {
        Some(id) => vec![id],
        None => match steam::add_now(launcher.desktop()) {
            Ok(()) => steam_ids(&launcher, name),
            // Steam wouldn't take it now: a waiting copy adds it once Steam closes.
            Err(_) => {
                queue_steam(data, folder, &shortcut)?;
                return Ok(format!("{name} will be added to Steam when Steam closes. Restart Steam to see it."));
            }
        },
    };
    if let Ok((user, _)) = steam::account() {
        for id in ids {
            let _ = steam::place_art(&user, id, &shortcut.art);
        }
    }
    Ok(format!("{name} is in Steam.{art_note}"))
}

/// The app ids to name a live add's artwork for. Steam assigns a shortcut's id
/// itself when it adds one, and it need not be the CRC of the program and name,
/// so its own write of shortcuts.vdf is read back, for a few seconds, and the
/// id it chose is used. Without one in time, the ids the shortcut might get are
/// guessed instead, so the artwork lands whichever way Steam wrote it.
#[cfg(target_os = "linux")]
fn steam_ids(launcher: &steam::Launcher, name: &str) -> Vec<u32> {
    for _ in 0..16 {
        if let Some(id) = steam::existing_id(launcher).or_else(|| steam::id_of(name, &launcher.exe())) {
            return vec![id];
        }
        std::thread::sleep(std::time::Duration::from_millis(250));
    }
    launcher.app_ids(name)
}

/// Leaves a shortcut for a separate copy of the launcher to add once Steam closes.
fn queue_steam(data: &Data, folder: &str, shortcut: &steam::Shortcut) -> Result<(), String> {
    let queued = data.0.join(format!("steam-{folder}.json"));
    std::fs::write(&queued, serde_json::to_vec(shortcut).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    let args = [OsStr::new("--add-to-steam"), queued.as_os_str()];
    // A Flatpak's copy in a sandbox of its own: this one's ends with the launcher.
    let mut waiting = match sandbox::relaunch(&args) {
        Some(command) => command,
        None => {
            let exe = std::env::var_os("APPIMAGE").map(PathBuf::from).or_else(|| std::env::current_exe().ok()).ok_or("Couldn't find Quiver Launcher's program.")?;
            let mut command = Command::new(exe);
            command.args(args);
            command
        }
    };
    waiting
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .spawn()
        .map_err(|e| e.to_string())?;
    Ok(())
}

/// Opens an installed app's folder in the file manager: the folder of an app
/// in installs.json, by the app's id.
#[tauri::command]
fn open_folder(data: State<Data>, id: String) -> Result<(), String> {
    let installs: serde_json::Value = std::fs::read(state_file(&data, "installs")?)
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .unwrap_or_default();
    let install = installs.get(&id).ok_or("This app isn't installed.")?;
    // A Windows installer's files are where its program is, not in the app's folder.
    if let Some(program) = install["program"].as_str().filter(|_| install["installer"] == "msi" || install["installer"] == "setup") {
        if let Some(parent) = Path::new(program).parent().filter(|p| p.is_dir()) {
            return reveal(parent);
        }
    }
    let folder = install["folder"].as_str().unwrap_or_default();
    reveal(&present_app_dir(&data, folder, install["dir"].as_str().map(str::to_string))?)
}

/// Opens an apps folder in the file manager: the one apps install in, or one
/// an installed app is kept in (installs.json), never any other path.
#[tauri::command]
fn open_apps_folder(data: State<Data>, path: String) -> Result<(), String> {
    let path = PathBuf::from(path);
    let installs: serde_json::Value = std::fs::read(state_file(&data, "installs")?)
        .ok()
        .and_then(|b| serde_json::from_slice(&b).ok())
        .unwrap_or_default();
    let holds_one = installs
        .as_object()
        .into_iter()
        .flat_map(|o| o.values())
        .filter_map(|i| i["dir"].as_str())
        .any(|d| Path::new(d).parent().is_some_and(|p| p == path));
    if path != data.apps() && !holds_one {
        return Err("That isn't one of your apps folders.".into());
    }
    if !path.is_dir() {
        return Err("This folder isn't there. Is its drive connected?".into());
    }
    reveal(&path)
}

/// Shows a folder in Explorer, Files or Finder. End-to-end tests note it in
/// `QUIVER_OPENED` instead of opening a window.
fn reveal(dir: &Path) -> Result<(), String> {
    if let Some(log) = std::env::var_os("QUIVER_OPENED") {
        append_log(Path::new(&log), &dir.to_string_lossy());
        return Ok(());
    }
    if cfg!(target_os = "android") {
        return Err("Android keeps an app's files to itself, so there's no folder to open.".into());
    }
    // The system's file manager, from a Flatpak too, rather than the portal's choice of app (host.rs).
    host::open(dir, None).map_err(|e| e.to_string())
}

/// Makes a new folder in the apps folder for an app the player fills
/// themselves, opens it for them, and resolves to its name. Never one that's
/// already there: it could be another app's.
#[tauri::command]
fn create_app_folder(data: State<Data>, name: String) -> Result<String, String> {
    let name = install::plain_name(&name)?.to_string();
    let dir = data.apps().join(&name);
    if dir.exists() {
        return Err(format!("There's already a folder named {name} in your apps folder. Choose another name."));
    }
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    reveal(&dir)?;
    Ok(name)
}

/// Renames an installed app's folder, and its Proton prefix (named `from`, the
/// folder name the launcher keeps for it) with it: one at the top of the apps
/// folder, or `dir`, where it is (its console's folder, or a folder the player
/// located), whatever that's called. Not while the launcher has it running: its
/// files are in use.
#[tauri::command]
fn rename_app_folder(data: State<Data>, running: State<Running>, id: String, from: String, to: String, dir: Option<String>) -> Result<(), String> {
    if running.has(&id) {
        return Err("Close the app first, then rename its folder.".into());
    }
    let from = install::plain_name(&from)?;
    let to = install::plain_name(&to)?;
    // One kept elsewhere (a console folder, or one the player located) is renamed where it is, whatever it's called.
    let old = match dir {
        None => data.apps().join(from),
        Some(dir) => PathBuf::from(dir),
    };
    apps_folder::rename(&old, &data.0.join("proton"), from, to)
}

/// Those of `names` that are files in an installed app's folder.
#[tauri::command]
fn app_files(data: State<Data>, folder: String, dir: Option<String>, names: Vec<String>) -> Result<Vec<String>, String> {
    Ok(install::files_present(&present_app_dir(&data, &folder, dir)?, &names))
}

/// Adds and takes away the empty files in an installed app's folder (Add files
/// to folder); returns those that stayed because they weren't empty. Not while
/// the launcher has it running: it wouldn't see them until its next start.
#[tauri::command]
fn set_app_files(data: State<Data>, running: State<Running>, id: String, folder: String, dir: Option<String>, add: Vec<String>, remove: Vec<String>) -> Result<Vec<String>, String> {
    if running.has(&id) {
        return Err("Close the app first, then change the files in its folder.".into());
    }
    install::set_files(&present_app_dir(&data, &folder, dir)?, &add, &remove)
}

/// Asks the player for a program on this computer, looking in `dir` first; null if they cancel.
/// `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_program(app: tauri::AppHandle, window: tauri::WebviewWindow, dir: Option<String>) -> Option<String> {
    if let Some(path) = std::env::var_os("QUIVER_PICK") {
        return Some(path.to_string_lossy().into());
    }
    let filter: Option<(&str, &[&str])> = match OS {
        "windows" => Some(("Programs", &["exe", "lnk", "bat", "cmd"])),
        "macos" => Some(("Apps", &["app"])),
        _ => None,
    };
    let path = picker::choose(&app, &window, picker::Want::File, "Choose the executable that starts the app", dir.as_deref().map(Path::new), filter).await?;
    Some(path.to_string_lossy().into())
}

/// The kinds of sound file the launcher's music can be: what WebView2, Chromium (CEF) and a Mac's WebKit all play.
const MUSIC_EXTENSIONS: &[&str] = &["mp3", "ogg", "opus", "flac", "wav", "m4a"];
/// More than an hour of music at a high bitrate: past it, it's not a file to keep in memory.
const MUSIC_MAX_BYTES: u64 = 200 * 1024 * 1024;

/// Asks the player for a sound file to play behind the launcher (music.ts), as Quiver Launcher 3 did; null if they
/// cancel. `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_music(app: tauri::AppHandle, window: tauri::WebviewWindow, title: String, dir: Option<String>) -> Option<String> {
    if let Some(path) = std::env::var_os("QUIVER_PICK") {
        return Some(path.to_string_lossy().into());
    }
    let path = picker::choose(&app, &window, picker::Want::File, &title, dir.as_deref().map(Path::new), Some(("Music", MUSIC_EXTENSIONS))).await?;
    Some(path.to_string_lossy().into())
}

/// The launcher's music file, for the page to play: only a sound file, and not one too large to hold in memory.
#[tauri::command]
async fn read_music(path: String) -> Result<tauri::ipc::Response, String> {
    let path = PathBuf::from(path);
    let kind = path.extension().and_then(|e| e.to_str()).map(str::to_ascii_lowercase);
    if !kind.is_some_and(|k| MUSIC_EXTENSIONS.contains(&k.as_str())) {
        return Err("That isn't a music file Quiver can play.".into());
    }
    let size = tokio::fs::metadata(&path).await.map_err(|e| e.to_string())?.len();
    if size > MUSIC_MAX_BYTES {
        return Err("That music file is too large to play.".into());
    }
    Ok(tauri::ipc::Response::new(tokio::fs::read(&path).await.map_err(|e| e.to_string())?))
}

/// The kinds of picture the library's background can be, as in Quiver Launcher 3: what WebView2, Chromium (CEF) and a Mac's WebKit all show.
const BACKGROUND_EXTENSIONS: &[&str] = &["png", "jpg", "jpeg", "gif", "bmp", "webp"];
/// A long animated GIF or a very large photo: past it, it's not a file to keep in memory.
const BACKGROUND_MAX_BYTES: u64 = 100 * 1024 * 1024;

/// Asks the player for a picture to show behind the library's grid (library-background.tsx), as Quiver Launcher 3 did;
/// null if they cancel. `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_background(app: tauri::AppHandle, window: tauri::WebviewWindow, title: String, dir: Option<String>) -> Option<String> {
    if let Some(path) = std::env::var_os("QUIVER_PICK") {
        return Some(path.to_string_lossy().into());
    }
    let path = picker::choose(&app, &window, picker::Want::File, &title, dir.as_deref().map(Path::new), Some(("Pictures", BACKGROUND_EXTENSIONS))).await?;
    Some(path.to_string_lossy().into())
}

/// The library's background picture, for the page to show: only a picture, and not one too large to hold in memory.
#[tauri::command]
async fn read_background(path: String) -> Result<tauri::ipc::Response, String> {
    let path = PathBuf::from(path);
    let kind = path.extension().and_then(|e| e.to_str()).map(str::to_ascii_lowercase);
    if !kind.is_some_and(|k| BACKGROUND_EXTENSIONS.contains(&k.as_str())) {
        return Err("That isn't a picture Quiver can show.".into());
    }
    let size = tokio::fs::metadata(&path).await.map_err(|e| e.to_string())?.len();
    if size > BACKGROUND_MAX_BYTES {
        return Err("That picture is too large to show.".into());
    }
    Ok(tauri::ipc::Response::new(tokio::fs::read(&path).await.map_err(|e| e.to_string())?))
}

/// Asks the player for a picture of an app and keeps a copy sized for `slot` (custom_art.rs): the copy's name, null
/// if they cancel. `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_artwork(app: tauri::AppHandle, window: tauri::WebviewWindow, title: String, slot: String) -> Result<Option<String>, String> {
    let path = match std::env::var_os("QUIVER_PICK") {
        Some(path) => Some(PathBuf::from(path)),
        None => picker::choose(&app, &window, picker::Want::File, &title, None, Some(("Pictures", custom_art::EXTENSIONS))).await,
    };
    let Some(path) = path else { return Ok(None) };
    let data = app.state::<Data>().0.clone();
    tauri::async_runtime::spawn_blocking(move || custom_art::import(&path, &data, &slot)).await.map_err(|e| e.to_string())?.map(Some)
}

/// Keeps a copy of a picture already on the computer, sized for `slot`: the one the player chose for an app in
/// Quiver Launcher 3, brought over with it (v3.rs). Its copy's name (custom_art.rs).
#[tauri::command]
async fn import_artwork(app: tauri::AppHandle, path: String, slot: String) -> Result<String, String> {
    let data = app.state::<Data>().0.clone();
    tauri::async_runtime::spawn_blocking(move || custom_art::import(Path::new(&path), &data, &slot)).await.map_err(|e| e.to_string())?
}

/// Deletes the kept pictures other than `files`.
#[tauri::command]
fn keep_artwork(data: State<Data>, files: Vec<String>) {
    custom_art::keep_only(&data.0, &files);
}

/// A newer Quiver Launcher, if there is one. Debug builds only look when
/// `QUIVER_UPDATE_API` says where; `QUIVER_SKIP_UPDATES` turns checking off.
#[tauri::command]
async fn check_update(app: tauri::AppHandle, preview: bool) -> Result<Option<update::Available>, String> {
    if std::env::var_os("QUIVER_SKIP_UPDATES").is_some() || (cfg!(debug_assertions) && std::env::var_os("QUIVER_UPDATE_API").is_none()) {
        return Ok(None);
    }
    let releases = update::releases().await?;
    let current = app.package_info().version.to_string();
    Ok(update::newest(&releases, &current, preview).map(|r| update::available(r, &update::kind())))
}

#[derive(Serialize, Clone)]
struct UpdateProgress {
    done: u64,
    total: Option<u64>,
}

/// Downloads `version`, checks its signature, backs up the saved files, puts
/// it in place and starts it; this copy then closes.
#[tauri::command]
async fn install_update(app: tauri::AppHandle, version: String) -> Result<(), String> {
    let kind = update::kind();
    let releases = update::releases().await?;
    let release = releases.iter().find(|r| r.tag_name.trim_start_matches('v') == version).ok_or("That update isn't available any more.")?;
    let key = update::public_key().ok_or("This copy of Quiver Launcher can't check an update's signature.")?;
    let asset = update::asset(release, &kind).ok_or("This release has no download for this computer.")?;
    let signature = asset_url(&format!("{}.sig", asset.name), release).ok_or("The update isn't signed, so it wasn't installed.")?;
    let signature = String::from_utf8(update::download(&signature, |_, _| {}).await?).map_err(|_| "The update's signature can't be read.")?;
    let bytes = update::download(&asset.browser_download_url, |done, total| {
        let _ = app.emit("update-progress", UpdateProgress { done, total });
    })
    .await?;
    update::verify(&key, &bytes, &signature)?;
    let data = app.state::<Data>().0.clone();
    update::back_up(&data, &app.package_info().version.to_string(), &version).map_err(|e| format!("Couldn't back up your library before updating: {e}"))?;
    let failed = |e: std::io::Error| format!("Couldn't put the update in place: {e}");
    let mut start = match &kind {
        update::Kind::AppImage(image) => {
            update::swap(image, &bytes).map_err(failed)?;
            Command::new(image)
        }
        update::Kind::Portable(exe) => {
            update::swap(exe, &update::exe_from_zip(&bytes)?).map_err(failed)?;
            Command::new(exe)
        }
        update::Kind::Installer => {
            let installer = std::env::temp_dir().join(&asset.name);
            std::fs::write(&installer, &bytes).map_err(failed)?;
            // Passive, then start the launcher again when it's done.
            let mut command = Command::new(installer);
            command.args(["/P", "/R"]);
            command
        }
        update::Kind::Apk => {
            let apk = app.path().app_cache_dir().map_err(|e| e.to_string())?.join(&asset.name);
            std::fs::create_dir_all(apk.parent().unwrap_or(Path::new("."))).and_then(|_| std::fs::write(&apk, &bytes)).map_err(failed)?;
            // Android closes this copy as it installs the new one, once the player confirms it.
            #[cfg(target_os = "android")]
            return tauri::async_runtime::spawn_blocking(move || android::install(&apk).map(|_| ())).await.map_err(|e| e.to_string())?;
            #[cfg(not(target_os = "android"))]
            return Err("Only Android installs an APK.".into());
        }
        update::Kind::Flatpak => {
            // Where the system's flatpak can read it: the cache folder is the same path outside the sandbox.
            let bundle = app.path().app_cache_dir().map_err(|e| e.to_string())?.join(&asset.name);
            std::fs::create_dir_all(bundle.parent().unwrap_or(Path::new("."))).and_then(|_| std::fs::write(&bundle, &bytes)).map_err(failed)?;
            let installed = tauri::async_runtime::spawn_blocking({
                let bundle = bundle.clone();
                move || flatpak::update(&bundle)
            })
            .await
            .map_err(|e| e.to_string())?;
            let _ = std::fs::remove_file(&bundle);
            installed?;
            sandbox::relaunch(&[]).ok_or("Quiver Launcher isn't running as a Flatpak.")?
        }
        update::Kind::Elsewhere => return Err("Update Quiver Launcher the way you installed it.".into()),
    };
    start.stdin(std::process::Stdio::null()).stdout(std::process::Stdio::null()).stderr(std::process::Stdio::null());
    start.spawn().map_err(|e| format!("Couldn't start the new version: {e}"))?;
    app.exit(0);
    Ok(())
}

/// A file attached to a release, by name.
fn asset_url(name: &str, release: &update::Release) -> Option<String> {
    release.assets.iter().find(|a| a.name == name).map(|a| a.browser_download_url.clone())
}

/// The executable Play starts when the player hasn't chosen one, as `target`
/// finds it, for Launch options to name: relative to the app's folder when
/// it's inside, else its full path (one a setup script installed). None when
/// there's none, or it isn't a file Quiver picks (a Windows installer's or a
/// Flatpak's).
#[tauri::command]
fn found_program(data: State<'_, Data>, folder: String, dir: Option<String>, preferred: Vec<String>, wine: bool) -> Option<String> {
    let dir = app_dir(&data, &folder, dir).ok().filter(|d| d.is_dir())?;
    if msi::installed(&dir) || setup::elsewhere(&dir) || flatpak::installed(&dir).is_some() {
        return None;
    }
    let exe = install::find_executable(&dir, &preferred, if wine { "windows" } else { OS }).or_else(|| (OS == "linux" && !wine).then(|| install::find_executable(&dir, &preferred, "windows")).flatten())?;
    let exe = if cfg!(windows) && script::installs(&dir, &exe) { script::installed(&dir).unwrap_or(exe) } else { exe };
    Some(exe.strip_prefix(&dir).unwrap_or(&exe).to_string_lossy().into_owned())
}

/// The programs in an installed app's folder, relative to it, for the player
/// to pick which ones Play starts.
#[tauri::command]
fn list_programs(data: State<'_, Data>, folder: String, dir: Option<String>) -> Result<Vec<String>, String> {
    let root = app_dir(&data, &folder, dir)?;
    Ok(install::programs(&root, OS)
        .iter()
        .filter_map(|p| p.strip_prefix(&root).ok())
        .map(|p| p.to_string_lossy().into_owned())
        .collect())
}

/// The kinds of file in an installed app's folder, for an error report (`install::file_kinds`).
#[tauri::command]
fn file_kinds(data: State<'_, Data>, folder: String, dir: Option<String>) -> Result<Vec<String>, String> {
    Ok(install::file_kinds(&app_dir(&data, &folder, dir)?))
}

/// The programs in an installed app's folder when none is one the catalog prefers, for the catalog to learn which
/// starts it (`install::preferred_missed`); None when the catalog prefers none, or one is there. A Windows build run
/// through Wine is looked at as Windows'.
#[tauri::command]
fn missed_programs(data: State<'_, Data>, folder: String, dir: Option<String>, preferred: Vec<String>, wine: bool) -> Result<Option<Vec<String>>, String> {
    let os = if wine { "windows" } else { OS };
    Ok(install::preferred_missed(&app_dir(&data, &folder, dir)?, &preferred, os))
}

/// Asks which program starts an installed app, starting in its folder: one
/// inside it is kept relative to it, so it survives the folder moving; one
/// anywhere else by its full path (`launch::chosen_program`), such as a game
/// the app's own installer put on another drive. An app the player added from
/// elsewhere (`program`) keeps the full path either way.
/// `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_executable(app: tauri::AppHandle, window: tauri::WebviewWindow, folder: String, dir: Option<String>, program: Option<String>) -> Result<Option<String>, String> {
    let data = app.state::<Data>();
    let root = match &program {
        Some(program) => Path::new(program).parent().map(Path::to_path_buf).unwrap_or_default(),
        None => app_dir(&data, &folder, dir)?,
    };
    let picked = match std::env::var_os("QUIVER_PICK") {
        Some(path) => Some(PathBuf::from(path)),
        None => picker::choose(&app, &window, picker::Want::File, "Choose the executable that starts the app", Some(&root), None).await,
    };
    let Some(picked) = picked else { return Ok(None) };
    if program.is_some() {
        return Ok(Some(picked.to_string_lossy().into()));
    }
    launch::chosen_program(&picked, &root).map(Some)
}

/// The Protons on this computer, newest name first.
#[tauri::command]
fn list_protons() -> Vec<launch::Proton> {
    dirs::home_dir().map(|home| launch::protons(&home)).unwrap_or_default()
}

/// The Proton that Automatic runs a Windows app with, if Steam has one.
fn automatic_proton() -> Option<launch::Proton> {
    dirs::home_dir().and_then(|home| launch::automatic_proton(launch::protons(&home)))
}

/// What Automatic runs a Windows app with on Linux, by name: a Proton, else Wine, else nothing (None). Looked for
/// off the window's thread, as it reads Steam's folders and asks the shell; never on another system, where looking
/// for Wine through a shell would flash a console window.
#[tauri::command]
async fn automatic_runner() -> Option<String> {
    if !cfg!(target_os = "linux") {
        return None;
    }
    tauri::async_runtime::spawn_blocking(|| automatic_proton().map(|p| p.name).or_else(|| sandbox::which("wine").map(|_| "Wine".into())))
        .await
        .ok()
        .flatten()
}

/// Asks for a Proton the launcher didn't find: its folder.
#[tauri::command]
async fn pick_proton(app: tauri::AppHandle, window: tauri::WebviewWindow) -> Result<Option<String>, String> {
    let picked = match std::env::var_os("QUIVER_PICK_FOLDER") {
        Some(path) => Some(PathBuf::from(path)),
        None => picker::choose(&app, &window, picker::Want::Folder, "Choose a Proton folder", None, None).await,
    };
    let Some(picked) = picked else { return Ok(None) };
    launch::proton_script(&picked.to_string_lossy()).map_err(|_| "That folder isn't a Proton: it has no proton script in it.".to_string())?;
    Ok(Some(picked.to_string_lossy().into()))
}

/// How far reading or putting in place the player's copy has got (own_copy.rs).
#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct CopyProgress {
    id: String,
    /// "check" or "place".
    step: &'static str,
    done: u64,
    total: u64,
}

/// Copies of the game that could be the player's, in `folders`: the ones they keep its console's copies in (their
/// settings). Nowhere else, so Quiver only looks where the player said; and never in Quiver's apps folder.
#[tauri::command]
async fn find_copies(app: tauri::AppHandle, file: own_copy::SetupFile, folders: Option<Vec<String>>) -> Vec<own_copy::Found> {
    let skip = vec![app.state::<Data>().apps()];
    let own: Vec<PathBuf> = folders.unwrap_or_default().into_iter().map(PathBuf::from).filter(|p| p.is_absolute()).collect();
    tauri::async_runtime::spawn_blocking(move || own_copy::find(&file, &own, &[], &skip)).await.unwrap_or_default()
}

/// Asks the player for their copy: a file of the kinds it can be, or a folder. `QUIVER_PICK` (a file) and
/// `QUIVER_PICK_FOLDER` answer instead in end-to-end tests.
#[tauri::command]
async fn pick_copy(app: tauri::AppHandle, window: tauri::WebviewWindow, file: own_copy::SetupFile, dir: Option<String>) -> Option<String> {
    let folder = file.kind.as_deref() == Some("folder");
    if let Some(path) = std::env::var_os(if folder { "QUIVER_PICK_FOLDER" } else { "QUIVER_PICK" }) {
        return Some(path.to_string_lossy().into());
    }
    let title = format!("Choose your copy: {}", file.label);
    let extensions: Vec<String> = file.extensions.iter().map(|e| e.to_ascii_lowercase()).collect();
    let mut wanted: Vec<&str> = extensions.iter().map(String::as_str).collect();
    if wanted.iter().any(|e| ["z64", "n64", "v64"].contains(e)) {
        wanted.extend(["z64", "n64", "v64"]);
        wanted.dedup();
    }
    let filter = (!folder && !wanted.is_empty()).then_some(("Your copy", wanted.as_slice()));
    let want = if folder { picker::Want::Folder } else { picker::Want::File };
    let path = picker::choose(&app, &window, want, &title, dir.as_deref().map(Path::new), filter).await?;
    Some(path.to_string_lossy().into())
}

/// Whether `path` is a copy the app takes, reading it through once.
#[tauri::command]
async fn check_copy(app: tauri::AppHandle, file: own_copy::SetupFile, path: String) -> Result<own_copy::Checked, String> {
    tauri::async_runtime::spawn_blocking(move || {
        let id = file.id.clone();
        let progress = |done, total| {
            let _ = app.emit("copy-progress", CopyProgress { id: id.clone(), step: "check", done, total });
        };
        own_copy::check(&file, Path::new(&path), &progress)
    })
    .await
    .map_err(|e| e.to_string())
}

/// Puts a checked copy where the app reads it, when the catalog says where: the path it went to, or null when the
/// app is only told where it is (by flag or environment variable). `wine`: a Windows build run through Wine or Proton.
#[tauri::command]
async fn place_copy(app: tauri::AppHandle, folder: String, dir: Option<String>, wine: bool, file: own_copy::SetupFile, path: String) -> Result<Option<String>, String> {
    let app_dir = present_app_dir(&app.state::<Data>(), &folder, dir)?;
    tauri::async_runtime::spawn_blocking(move || {
        let id = file.id.clone();
        let progress = |done, total| {
            let _ = app.emit("copy-progress", CopyProgress { id: id.clone(), step: "place", done, total });
        };
        own_copy::place(&file, Path::new(&path), &app_dir, wine, &progress).map(|to| to.map(|p| p.to_string_lossy().into_owned()))
    })
    .await
    .map_err(|e| e.to_string())?
}

/// Whether the player's copy is still where the app reads it.
#[tauri::command]
fn copy_present(data: State<'_, Data>, folder: String, dir: Option<String>, wine: bool, file: own_copy::SetupFile, path: String) -> bool {
    present_app_dir(&data, &folder, dir).is_ok_and(|d| own_copy::present(&file, Path::new(&path), &d, wine))
}

/// Asks the player for a folder on this computer, opening in `dir` when it's
/// one; null if they cancel. `QUIVER_PICK_FOLDER` answers instead in
/// end-to-end tests.
#[tauri::command]
async fn pick_folder(app: tauri::AppHandle, window: tauri::WebviewWindow, title: String, dir: Option<String>) -> Option<String> {
    if let Some(path) = std::env::var_os("QUIVER_PICK_FOLDER") {
        return Some(path.to_string_lossy().into());
    }
    let path = picker::choose(&app, &window, picker::Want::Folder, &title, dir.as_deref().map(Path::new), None).await?;
    Some(path.to_string_lossy().into())
}

/// How each apps folder is now: there or not, and the room on its drive. Off the window's thread, as a drive that
/// isn't plugged in, or one on the network, can take a while to answer.
#[tauri::command]
async fn apps_folder_states(paths: Vec<PathBuf>) -> Vec<apps_folder::FolderState> {
    tauri::async_runtime::spawn_blocking(move || apps_folder::states(&paths)).await.unwrap_or_default()
}

/// Removes a console folder the player's apps all moved out of, once nothing's left in it (`apps_folder::remove_empty`).
#[tauri::command]
fn remove_empty_console_folder(data: State<Data>, path: String) {
    apps_folder::remove_empty(Path::new(&path), &data.apps());
}

/// Removes the default apps folder's system folders that are empty (`apps_folder::remove_empty_system_folders`).
#[tauri::command]
fn remove_empty_system_folders(data: State<Data>) {
    apps_folder::remove_empty_system_folders(&data.apps());
}

/// Why the installed apps can't move to `to`, if they can't: those in the
/// apps folder, and those kept elsewhere (with a `dir`), as 3.x's are.
#[tauri::command]
fn apps_folder_refusal(data: State<Data>, running: State<Running>, to: String, apps: Vec<apps_folder::App>) -> Option<String> {
    in_use(&running, &apps).or_else(|| apps_folder::refuse(&data.apps(), Path::new(&to), &apps))
}

/// Why apps can't move while one of them is running, if one is.
fn in_use(running: &Running, apps: &[apps_folder::App]) -> Option<String> {
    apps.iter().find(|a| running.has(&a.id)).map(|a| format!("Close {} first, then move your apps.", a.folder))
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct AppsMoved {
    apps_dir: String,
    #[serde(flatten)]
    done: apps_folder::Done,
}

/// Moves the installed apps to `to` and installs there from now on: those in
/// the apps folder, and those kept elsewhere, as 3.x's are. Reports how far
/// it's got ("apps-moving") and each app once it's in its new folder
/// ("app-moved"), so the page records it then and there.
#[tauri::command]
async fn move_apps_folder(app: tauri::AppHandle, to: String, apps: Vec<apps_folder::App>) -> Result<AppsMoved, String> {
    if let Some(why) = in_use(&app.state::<Running>(), &apps) {
        return Err(why);
    }
    let handle = app.clone();
    tauri::async_runtime::spawn_blocking(move || {
        let data = handle.state::<Data>();
        let from = data.apps();
        let to = PathBuf::from(&to);
        let done = apps_folder::move_apps(
            &data.0,
            &from,
            &to,
            &apps,
            |m| {
                let _ = handle.emit("apps-moving", m);
            },
            |m| {
                let _ = handle.emit("app-moved", m);
            },
        )?;
        *data.1.lock().unwrap() = to.clone();
        Ok(AppsMoved { apps_dir: to.to_string_lossy().into(), done })
    })
    .await
    .map_err(|e| e.to_string())?
}

#[derive(Serialize, Clone)]
struct AppMoving {
    id: String,
    copied: u64,
    size: u64,
}

/// Moves one app's folder into `to`, the safe way the apps folder moves (apps_folder::move_app), reporting how far
/// it's got ("app-moving") when it's copied to another drive.
#[tauri::command]
async fn move_app(app: tauri::AppHandle, id: String, folder: String, dir: Option<String>, to: String, in_apps: bool, group: Option<String>) -> Result<apps_folder::AppMoved, String> {
    if app.state::<Running>().has(&id) {
        return Err("Close it first, then move it.".into());
    }
    let from = present_app_dir(&app.state::<Data>(), &folder, dir)?;
    let name = from.file_name().and_then(|n| n.to_str()).unwrap_or(&folder).to_string();
    let handle = app.clone();
    let data = app.state::<Data>().0.clone();
    let record = apps_folder::Record { in_apps, group };
    tauri::async_runtime::spawn_blocking(move || {
        apps_folder::move_app(&data, &id, &from, Path::new(&to), &name, &record, |copied, size| {
            let _ = handle.emit("app-moving", AppMoving { id: id.clone(), copied, size });
        })
    })
    .await
    .map_err(|e| e.to_string())?
}

/// Opens a web page in the browser.
#[tauri::command]
async fn open_url(window: tauri::WebviewWindow, url: String) -> Result<(), String> {
    if !url.starts_with("https://") && !url.starts_with("http://") {
        return Err("Only web addresses can be opened.".into());
    }
    browser::open_from(&window, &url)
}

/// Android's Back with nothing left to go back from (main.tsx): the launcher goes behind the other apps, not away.
/// Not on the window's thread, which the system answers on.
#[tauri::command]
async fn to_background() -> Result<(), String> {
    #[cfg(target_os = "android")]
    return android::to_background();
    #[cfg(not(target_os = "android"))]
    Ok(())
}

/// Discord's Rich Presence on or off (discord.rs), and its words in the player's language.
#[tauri::command]
fn set_discord_presence(presence: State<discord::Presence>, enabled: bool, texts: discord::Texts) {
    presence.set(enabled, texts);
}

/// Close to tray on or off (tray.rs), and its menu's words in the player's language. Windows only: elsewhere, nothing.
#[tauri::command]
fn set_tray(app: tauri::AppHandle, close_to_tray: bool, open: String, quit: String) -> Result<(), String> {
    #[cfg(windows)]
    return tray::set(&app, close_to_tray, open, quit).map_err(|e| e.to_string());
    #[cfg(not(windows))]
    {
        let _ = (app, close_to_tray, open, quit);
        Ok(())
    }
}

/// Whether Windows starts the launcher, in the tray, as the player signs in.
#[tauri::command]
fn start_with_windows() -> bool {
    #[cfg(windows)]
    return tray::starts_with_windows();
    #[cfg(not(windows))]
    false
}

#[tauri::command]
fn set_start_with_windows(on: bool) -> Result<(), String> {
    #[cfg(windows)]
    return tray::set_start_with_windows(on);
    #[cfg(not(windows))]
    {
        let _ = on;
        Err("Only Windows starts the launcher as you sign in.".into())
    }
}

/// The data folder beside a portable copy of the launcher (one with `portable.txt` next to it).
fn portable_data() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf))
        .filter(|dir| dir.join("portable.txt").exists())
        .map(|dir| dir.join("data"))
}

fn has_fuse2() -> bool {
    // Without ldconfig, extracting still works, just more slowly. The system's, where the AppImage runs.
    let mut ldconfig = Command::new("/sbin/ldconfig");
    ldconfig.arg("-p");
    sandbox::host(ldconfig)
        .output()
        .is_ok_and(|o| String::from_utf8_lossy(&o.stdout).contains("libfuse.so.2"))
}

/// Shows Steam's on-screen keyboard while a text field is being edited, and hides it after.
#[tauri::command]
fn steam_keyboard(open: bool) -> bool {
    keyboard::request(open)
}

#[tauri::command]
async fn mod_packages(source: mods::Source) -> Result<Vec<mods::Package>, String> {
    mods::packages(&source).await
}

#[tauri::command]
async fn mod_files(package: mods::Package) -> Result<Vec<mods::ModFile>, String> {
    mods::files(&package).await
}

#[tauri::command]
fn installed_mods(data: State<'_, Data>, folder: String, dir: Option<String>) -> Result<Vec<mods::Installed>, String> {
    Ok(mods::installed(&app_dir(&data, &folder, dir)?))
}

#[tauri::command]
async fn install_mod(
    data: State<'_, Data>,
    folder: String,
    dir: Option<String>,
    config: mods::Config,
    package: mods::Package,
    file: mods::ModFile,
) -> Result<mods::Installed, String> {
    let app = app_dir(&data, &folder, dir)?;
    mods::download_and_install(&app, &config, &package, &file).await
}

#[tauri::command]
fn uninstall_mod(data: State<'_, Data>, folder: String, dir: Option<String>, config: mods::Config, key: String) -> Result<(), String> {
    mods::uninstall(&app_dir(&data, &folder, dir)?, &config, &key)
}

/// Removes an installed app; fails with "cancelled" when the player says no to Windows uninstalling it.
#[tauri::command]
async fn uninstall(app: tauri::AppHandle, folder: String, dir: Option<String>) -> Result<(), String> {
    let dir = app_dir(&app.state::<Data>(), &folder, dir)?;
    // Windows' uninstaller waits for the player, so not on the window's thread.
    tauri::async_runtime::spawn_blocking(move || {
        // Windows removes what its installer put on the computer, first.
        if msi::installed(&dir) {
            msi::uninstall(&dir)?;
        }
        // An app its own setup program installed has its own uninstaller, which goes first too.
        if setup::installed(&dir) {
            setup::uninstall(&dir)?;
        }
        // Android removes an app installed from an APK, once the player confirms it.
        #[cfg(target_os = "android")]
        if let Some(package) = android::package(&dir) {
            android::uninstall(&package)?;
        }
        // Only a folder that still looks like an install is removed: one Quiver
        // installed into, or a 3.x install. Never a folder of the player's own.
        if dir.exists() && install::may_remove(&dir) {
            if let Some(id) = flatpak::installed(&dir) {
                flatpak::uninstall(&id);
            }
            #[cfg(not(target_os = "android"))]
            install::remove_install(&dir, |d| trash::delete(d).or_else(|_| std::fs::remove_dir_all(d))).map_err(|e| e.to_string())?;
            #[cfg(target_os = "android")]
            install::remove_install(&dir, |d| std::fs::remove_dir_all(d)).map_err(|e| e.to_string())?;
        }
        Ok(())
    })
    .await
    .map_err(|e| e.to_string())?
}

/// Sign-in tokens: in the OS keychain or the data folder (secrets.rs).
#[tauri::command]
fn secret_get(data: State<Data>, key: String) -> Option<String> {
    secrets::get(&data.0, &secrets::OsKeychain, secrets::store(), &key)
}

#[tauri::command]
fn secret_set(data: State<Data>, key: String, value: Option<String>) -> Result<(), String> {
    secrets::set(&data.0, &secrets::OsKeychain, secrets::store(), &key, value.as_deref())
}

/// What GitLab says about the player's request limit. A page can't read GitLab's
/// rate-limit headers (GitLab doesn't expose them to pages), so the Rust side
/// asks instead: `/user` with a token, which also checks it, or a public project
/// list without one. It also says when a refused request may be tried again.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct GitlabLimit {
    status: u16,
    limit: Option<u64>,
    remaining: Option<u64>,
    reset: Option<u64>,
    retry_after: Option<u64>,
}

/// GitLab's API, for custom apps (overridden in end-to-end tests).
fn gitlab_api() -> String {
    std::env::var("QUIVER_GITLAB_API").unwrap_or_else(|_| "https://gitlab.com/api/v4".into())
}

/// Whether `url` is on GitLab's API, the only place the player's GitLab token may go.
fn on_gitlab_api(url: &str, api: &str) -> bool {
    url.strip_prefix(api.trim_end_matches('/')).is_some_and(|rest| rest.starts_with('/'))
}

#[tauri::command]
async fn gitlab_limit(url: String, token: Option<String>) -> Result<GitlabLimit, String> {
    if !on_gitlab_api(&url, &gitlab_api()) {
        return Err("Only GitLab's API can be asked about its limit.".into());
    }
    static CLIENT: std::sync::OnceLock<reqwest::Client> = std::sync::OnceLock::new();
    let client = match CLIENT.get() {
        Some(client) => client,
        None => {
            let client = reqwest::Client::builder().user_agent("QuiverLauncher/4").build().map_err(|e| e.to_string())?;
            CLIENT.get_or_init(|| client)
        }
    };
    let mut request = client.get(&url);
    if let Some(token) = token.as_deref().map(str::trim).filter(|t| !t.is_empty()) {
        request = request.header("PRIVATE-TOKEN", token);
    }
    let response = request.send().await.map_err(|_| "Couldn't reach GitLab. Check your connection and try again.".to_string())?;
    let status = response.status().as_u16();
    let header = |name: &str| response.headers().get(name).and_then(|v| v.to_str().ok()).and_then(|v| v.parse::<u64>().ok());
    Ok(GitlabLimit {
        status,
        limit: header("ratelimit-limit"),
        remaining: header("ratelimit-remaining"),
        reset: header("ratelimit-reset"),
        retry_after: header("retry-after"),
    })
}

#[cfg(test)]
mod gitlab_limit_tests {
    use super::on_gitlab_api;

    #[test]
    fn the_token_only_goes_to_gitlabs_api() {
        let api = "https://gitlab.com/api/v4";
        assert!(on_gitlab_api("https://gitlab.com/api/v4/user", api));
        assert!(on_gitlab_api("https://gitlab.com/api/v4/projects?per_page=1", "https://gitlab.com/api/v4/"));
        assert!(!on_gitlab_api("https://gitlab.com/api/v4.evil.test/user", api));
        assert!(!on_gitlab_api("https://evil.test/user", api));
        assert!(!on_gitlab_api("https://gitlab.com/api/v4", api));
    }
}

/// Opens a GitHub or Discord sign-in page and waits for it to come back,
/// then brings the launcher back in front of the browser.
#[tauri::command]
async fn browser_sign_in(window: tauri::WebviewWindow, url: String) -> Result<browser::Callback, String> {
    let opener = window.clone();
    let result = tauri::async_runtime::spawn_blocking(move || browser::sign_in(&url, |url| browser::open_from(&opener, url)))
        .await
        .map_err(|e| e.to_string())?;
    // Not for one let go of: the player is still choosing, in Quiver or another sign-in's page.
    if result.is_ok() && !matches!(&result, Ok(browser::Callback::Error(e)) if e == "cancelled") {
        #[cfg(desktop)]
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
    result
}

/// Stops waiting for a GitHub or Discord sign-in: its dialog closed.
#[tauri::command]
fn browser_sign_in_cancel() {
    browser::cancel();
}

fn append_log(path: &Path, line: &str) {
    use std::io::Write;
    let stamp = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0);
    if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
        let _ = writeln!(file, "{stamp} {line}");
    }
}

#[tauri::command]
fn log_error(data: State<Data>, message: String) {
    append_log(&data.0.join("quiver.log"), &message.chars().take(4000).collect::<String>());
}

/// Whether the page should measure its frame rate (fps-probe.ts): QUIVER_FPS_LOG names the file.
#[tauri::command]
fn fps_probe() -> bool {
    std::env::var_os("QUIVER_FPS_LOG").is_some()
}

#[tauri::command]
fn fps_report(line: String) {
    use std::io::Write;
    if let Some(path) = std::env::var_os("QUIVER_FPS_LOG") {
        if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(path) {
            let _ = writeln!(file, "{line}");
        }
    }
}

#[tauri::command]
fn set_fullscreen(window: tauri::WebviewWindow, on: bool, title_bar: bool) -> Result<(), String> {
    fullscreen(&window, on, title_bar).map_err(|e| e.to_string())
}

/// The system's title bar, or none and the launcher's own buttons in its top
/// bar (the default). A Mac keeps its own, with its buttons where Mac players
/// look for them.
/// Running in Steam's Game Mode (gamescope), which shows one app at a time on the whole screen. A window there fills
/// it: gamescope shrinks a larger one to fit, and a size kept from Desktop Mode (docked to a bigger screen, say) put
/// the whole interface at a fraction of its size.
fn in_game_mode() -> bool {
    keyboard::game_mode(|name| std::env::var(name).ok())
}

#[cfg_attr(mobile, allow(dead_code))]
fn system_title_bar(on: bool) -> bool {
    on || OS == "macos"
}

/// On Windows a maximized window without the system's title bar is kept off
/// the taskbar (tao's WM_NCCALCSIZE), and full screen kept that too: the
/// taskbar went, and an empty strip stayed where it was. So full screen there
/// has the title bar's frame, which full screen takes off anyway.
///
/// In Steam's Game Mode it's always full screen (`in_game_mode`), whatever leaves it.
#[cfg(desktop)]
fn fullscreen(window: &tauri::WebviewWindow, on: bool, title_bar: bool) -> tauri::Result<()> {
    let on = on || in_game_mode();
    if OS == "windows" && !system_title_bar(title_bar) {
        if on {
            window.set_fullscreen(true)?;
            return window.set_decorations(true);
        }
        window.set_decorations(false)?;
    }
    window.set_fullscreen(on)
}

/// A phone's app is always full screen.
#[cfg(mobile)]
fn fullscreen(_window: &tauri::WebviewWindow, _on: bool, _title_bar: bool) -> tauri::Result<()> {
    Ok(())
}

#[tauri::command]
fn set_system_title_bar(window: tauri::WebviewWindow, on: bool) -> Result<(), String> {
    // Full screen on Windows keeps the frame (see `fullscreen`); leaving it puts this one on.
    if OS == "windows" && window.is_fullscreen().unwrap_or(false) {
        return Ok(());
    }
    #[cfg(mobile)]
    {
        let _ = on;
        Ok(())
    }
    #[cfg(desktop)]
    window.set_decorations(system_title_bar(on)).map_err(|e| e.to_string())
}

/// "light", "dark", or anything else to follow the system.
fn theme_of(name: Option<&str>) -> Option<tauri::Theme> {
    match name {
        Some("light") => Some(tauri::Theme::Light),
        Some("dark") => Some(tauri::Theme::Dark),
        _ => None,
    }
}

/// The page's background, shown before it draws (no white flash in dark, or dark flash in light).
fn backdrop(theme: tauri::Theme) -> tauri::window::Color {
    match theme {
        tauri::Theme::Light => tauri::window::Color(0xf4, 0xf5, 0xf7, 0xff),
        _ => tauri::window::Color(0x0e, 0x0e, 0x10, 0xff),
    }
}

/// The title bar, scrollbars and the page's colour scheme follow the launcher's theme.
#[tauri::command]
fn set_theme(window: tauri::WebviewWindow, theme: Option<String>) -> Result<(), String> {
    let theme = theme_of(theme.as_deref());
    window.set_theme(theme).map_err(|e| e.to_string())?;
    let shown = theme.or_else(|| window.theme().ok()).unwrap_or(tauri::Theme::Dark);
    let _ = window.set_background_color(Some(backdrop(shown)));
    Ok(())
}

/// The window's place and size in the screen's pixels, and the smallest it goes, for resizing it from the page's edges
/// (`ResizeEdges` in window-bar.tsx).
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct WindowRect {
    x: i32,
    y: i32,
    width: u32,
    height: u32,
    min_width: u32,
    min_height: u32,
}

#[tauri::command]
fn window_rect(window: tauri::WebviewWindow) -> Result<WindowRect, String> {
    let position = window.outer_position().map_err(|e| e.to_string())?;
    let size = window.inner_size().map_err(|e| e.to_string())?;
    let scale = window.scale_factor().map_err(|e| e.to_string())?;
    let config = window.config().app.windows.first();
    let min = |v: Option<f64>| (v.unwrap_or(0.0) * scale).round() as u32;
    Ok(WindowRect {
        x: position.x,
        y: position.y,
        width: size.width,
        height: size.height,
        min_width: min(config.and_then(|c| c.min_width)),
        min_height: min(config.and_then(|c| c.min_height)),
    })
}

/// Puts the window at a place and size together, as an edge of the page is pulled. Linux's Chromium window has no
/// resize borders of its own, and the window manager can't take over the pointer to resize it on X11.
#[tauri::command]
fn set_window_rect(window: tauri::WebviewWindow, x: i32, y: i32, width: u32, height: u32) -> Result<(), String> {
    window.set_size(tauri::PhysicalSize::new(width, height)).map_err(|e| e.to_string())?;
    window.set_position(tauri::PhysicalPosition::new(x, y)).map_err(|e| e.to_string())
}

/// Puts the window somewhere, as the top bar is dragged on an X11 desktop (`moveWindow` in window-bar.tsx).
#[tauri::command]
fn set_window_position(window: tauri::WebviewWindow, x: i32, y: i32) -> Result<(), String> {
    window.set_position(tauri::PhysicalPosition::new(x, y)).map_err(|e| e.to_string())
}

/// The desktop is X11, not Wayland. There a window manager moves a window by taking the pointer, and it can't: the
/// press that started the drag gave the pointer to Chromium's own connection to the X server, which only Chromium can
/// let go of (Tauri lets go on its own connection, to no effect). Wayland's compositors move the window themselves.
fn x11_session(var: impl Fn(&str) -> Option<String>) -> bool {
    let wayland = var("XDG_SESSION_TYPE").is_some_and(|v| v.trim().eq_ignore_ascii_case("wayland"))
        || var("WAYLAND_DISPLAY").is_some_and(|v| !v.trim().is_empty());
    !wayland
}

#[tauri::command]
fn find_v3_library() -> Option<v3::Library> {
    v3::find()
}

/// Every Quiver Launcher 3 apps folder on this computer, whichever library is brought over.
#[tauri::command]
async fn v3_apps_folders() -> Vec<String> {
    tauri::async_runtime::spawn_blocking(v3::apps_folders).await.unwrap_or_default()
}

/// The 3.x library in a folder the player picked, or the folder around it;
/// with `apps`, its installs in those Apps folders too.
#[tauri::command]
fn find_v3_library_in(path: String, apps: Option<Vec<PathBuf>>) -> Option<v3::Library> {
    v3::find_in(Path::new(&path), &apps.unwrap_or_default())
}

/// The install in a folder the player says holds an app; null when it doesn't.
#[tauri::command]
fn find_existing_install(path: String) -> Option<v3::Existing> {
    v3::existing(Path::new(&path))
}

/// The window from tauri.conf.json. End-to-end tests on Windows set
/// QUIVER_DEBUG_PORT on a debug build and attach to WebView2 there, since its
/// runtime no longer takes the debugging port WebDriver passes it.
/// The launcher's window, back where it was: on screen unless `shown` is false (started in the tray), when it's
/// whether to maximize it as it shows.
fn main_window(app: &tauri::App, dir: &Path, theme: Option<tauri::Theme>, title_bar: bool, shown: bool) -> tauri::Result<bool> {
    let builder = tauri::WebviewWindowBuilder::from_config(app.handle(), &app.config().app.windows[0])?.theme(theme);
    #[cfg(desktop)]
    let builder = builder.decorations(system_title_bar(title_bar));
    #[cfg(mobile)]
    let _ = title_bar;
    #[cfg(all(windows, debug_assertions))]
    let builder = match std::env::var("QUIVER_DEBUG_PORT") {
        Ok(port) => builder
            .additional_browser_args(&format!(
                "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection --remote-debugging-port={port}"
            ))
            .data_directory(dir.join("webview")),
        Err(_) => builder,
    };
    // Hidden until it's back where it was, so it doesn't jump.
    let window = builder.visible(false).build()?;
    // Game Mode's window fills the screen (run below), and keeps Desktop Mode's size and place as they were.
    let game_mode = in_game_mode();
    let maximize = !game_mode && window_state::restore(&window, dir);
    if !game_mode {
        window_state::track(&window, dir.to_path_buf());
        window_state::keep_on_screen(&window);
    }
    #[cfg(windows)]
    tray::watch(&window);
    if !shown {
        return Ok(maximize);
    }
    window.show()?;
    #[cfg(desktop)]
    if maximize {
        let _ = window.maximize();
    }
    // However it was placed (where it was saved, or where the desktop put it), it may still be off every connected
    // screen: brought onto one.
    if !game_mode {
        window_state::bring_on_screen(&window);
    }
    Ok(maximize)
}

/// What `tauri::AppHandle` and the rest mean without a runtime named: wry, or on
/// Tauri's feat/cef branch (the Linux CEF build) the type-erased runtime that
/// run() hands CEF.
#[cfg(not(all(feature = "cef", target_os = "linux")))]
type AppRuntime = tauri::Wry;
#[cfg(all(feature = "cef", target_os = "linux"))]
type AppRuntime = tauri::DynRuntime;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
// CEF starts its helper processes from this binary; this sends them off to CEF before anything below runs.
#[cfg_attr(all(feature = "cef", target_os = "linux"), tauri_runtime_cef::cef_entry_point)]
pub fn run() {
    // WebKitGTK 2.50 paints its GPU tiles on threads of their own, and on a Steam Deck some were drawn from an earlier
    // frame: a shelf of Browse's showed one app's art and name in another's place, the same app twice, in 4 of 40
    // screenshots as the d-pad was held along it. Painted without those threads (0), none in 40, at the same frame
    // rate. Set before WebKit starts, and not over a value the player set.
    #[cfg(target_os = "linux")]
    if std::env::var_os("WEBKIT_SKIA_GPU_PAINTING_THREADS").is_none() {
        std::env::set_var("WEBKIT_SKIA_GPU_PAINTING_THREADS", "0");
    }
    let args: Vec<String> = std::env::args().collect();
    if let [_, flag, file] = args.as_slice() {
        if flag == "--add-to-steam" {
            return steam::wait_and_apply(Path::new(file));
        }
    }
    let builder = tauri::Builder::default();
    // Chromium without its sandbox, as WebKitGTK ran here: with it, CEF aborts at start where unprivileged user
    // namespaces are restricted (Ubuntu 24.04 on) unless a setuid chrome-sandbox is installed, which only a deb
    // can do. QUIVER_CDP_PORT opens the DevTools protocol on 127.0.0.1, for tests and debugging.
    #[cfg(all(feature = "cef", target_os = "linux"))]
    let builder = {
        // Scrollbars over the page, as WebKitGTK draws them (Chromium's take 15 pixels: a column fewer in the
        // library). No extensions: Chromium took up KDE's Plasma browser integration and started its two helpers.
        // No Safe Browsing: the page is only ever Quiver's own.
        let cef = tauri_runtime_cef::Cef::default()
            .sandbox(tauri_runtime_cef::SandboxPolicy::Disabled)
            .enable_features(["OverlayScrollbar"])
            .command_line_arg("--disable-extensions", None::<String>)
            .profile_preference("safebrowsing.enabled", false);
        let cef = match std::env::var("QUIVER_CDP_PORT").map(|port| port.parse::<u16>()) {
            Ok(Ok(port)) => cef.remote_debugging(tauri_runtime_cef::RemoteDebugging::Port { port, allowed_origins: Vec::new() }),
            _ => cef,
        };
        builder.runtime(cef)
    };
    // One launcher at a time on its data: starting it again, as one hidden in the tray can be, opens the one running
    // (unless that's Windows starting it as the player signs in). A portable copy keeps its own data, and so do tests.
    #[cfg(windows)]
    let builder = if std::env::var_os("QUIVER_DATA").is_none() && portable_data().is_none() {
        builder.plugin(tauri_plugin_single_instance::init(|app, args, _| {
            if !args.iter().any(|a| a == tray::ARG) {
                tray::open_window(app);
            }
        }))
    } else {
        builder
    };
    let builder = builder.plugin(tauri_plugin_dialog::init());
    // The player's own pictures, off the page's thread: a library shows many at once.
    let builder = builder.register_asynchronous_uri_scheme_protocol(custom_art::PROTOCOL, |context, request, responder| {
        let data = context.app_handle().try_state::<Data>().map(|data| data.0.clone());
        let path = request.uri().path().to_string();
        tauri::async_runtime::spawn_blocking(move || {
            let response = data.map_or_else(|| tauri::http::Response::builder().status(503).body(Vec::new()).unwrap(), |data| custom_art::respond(&data, &path));
            responder.respond(response);
        });
    });
    #[cfg(target_os = "android")]
    let builder = builder.plugin(android::init());
    builder
        .setup(|app| {
            let portable = portable_data();
            // What an earlier update moved aside.
            match update::kind() {
                update::Kind::AppImage(image) => update::clean_up(&image),
                _ => {
                    if let Ok(exe) = std::env::current_exe() {
                        update::clean_up(&exe);
                    }
                }
            }
            let dir = std::env::var_os("QUIVER_DATA")
                .map(PathBuf::from)
                .or(portable)
                .or_else(|| sandbox::data_dir(&app.config().identifier))
                .unwrap_or(app.path().app_data_dir()?);
            // Crashes and UI errors go to quiver.log in the data folder, for bug reports.
            let log = dir.join("quiver.log");
            let previous = std::panic::take_hook();
            std::panic::set_hook(Box::new(move |info| {
                append_log(&log, &format!("crash: {info}"));
                previous(info);
            }));
            let settings: serde_json::Value = std::fs::read(dir.join("settings.json"))
                .ok()
                .and_then(|b| serde_json::from_slice(&b).ok())
                .unwrap_or_default();
            let title_bar = settings["systemTitleBar"] == true;
            let full = settings["fullscreen"] == true || std::env::args().any(|a| a == "--fullscreen") || in_game_mode();
            // Started by Windows as the player signs in: in the tray, the window unseen till they open it.
            #[cfg(windows)]
            let unseen = std::env::args().any(|a| a == tray::ARG);
            #[cfg(not(windows))]
            let unseen = false;
            #[cfg(windows)]
            app.manage(tray::Tray::new(settings["closeToTray"] == true, unseen));
            let maximize = main_window(app, &dir, theme_of(settings["theme"].as_str()), title_bar, !unseen)?;
            #[cfg(not(windows))]
            let _ = maximize;
            // Paint behind the page in the theme it will draw in.
            if let Some(window) = app.get_webview_window("main") {
                if let Ok(theme) = window.theme() {
                    let _ = window.set_background_color(Some(backdrop(theme)));
                }
            }
            #[cfg(windows)]
            if unseen {
                app.state::<tray::Tray>().wait_to_show(tray::OnShow { maximize, fullscreen: full.then_some(title_bar) });
            }
            #[cfg(windows)]
            if let Err(e) = tray::refresh(app.handle()) {
                append_log(&dir.join("quiver.log"), &format!("Couldn't show the tray icon: {e}"));
            }
            if full && !unseen {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = fullscreen(&window, true, title_bar);
                }
            }
            let apps = apps_folder::current(&dir);
            // The data folder's own apps folder is there from the start, so Settings never takes it for a drive
            // that's gone before the first app installs. One the player chose is never made: if it's gone, they hear.
            if apps == dir.join("apps") {
                let _ = std::fs::create_dir_all(&apps);
            }
            // A move the launcher stopped during is finished before it reads where its apps are.
            let (old, said) = move_journal::recover(&dir, &apps);
            for line in said {
                append_log(&dir.join("quiver.log"), &line);
            }
            delete_moved(None, &dir, old);
            app.manage(Data(dir, std::sync::Mutex::new(apps)));
            app.manage(Snapshotted(Default::default()));
            app.manage(gamepad::Pads::default());
            app.manage(Installing::default());
            app.manage(CacheFetching::default());
            app.manage(Running::default());
            app.manage(Watching::default());
            app.manage(discord::Presence::new());
            #[cfg(target_os = "android")]
            app.manage(Playing::default());
            app.manage(picker::Picks::default());
            gamepad::start(app.handle().clone());
            #[cfg(all(feature = "cef", target_os = "linux"))]
            x11_wheel::start(app.handle().clone());
            #[cfg(target_os = "linux")]
            {
                keyboard::kwin::listen(app.handle().clone());
                keyboard::kwin::load();
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            session::close_combo,
            config,
            pc_specs,
            cancel_install,
            cache_fetch,
            cache_fetch_cancel,
            cache_status,
            cache_clean,
            cache_has,
            cache_trim,
            cache_remove,
            cache_open,
            cache_open_app,
            read_state,
            write_state,
            state_problems,
            list_snapshots,
            restore_snapshot,
            start_empty_library,
            install,
            launch,
            uninstall,
            find_v3_library,
            v3_apps_folders,
            find_v3_library_in,
            find_existing_install,
            pick_folder,
            picker::picked,
            picker::browse,
            apps_folder_refusal,
            apps_folder_states,
            remove_empty_console_folder,
            remove_empty_system_folders,
            move_app,
            open_apps_folder,
            move_apps_folder,
            secret_get,
            secret_set,
            gitlab_limit,
            provider_http::provider_http,
            browser_sign_in,
            browser_sign_in_cancel,
            log_error,
            fps_probe,
            fps_report,
            set_fullscreen,
            set_system_title_bar,
            set_theme,
            window_rect,
            set_window_rect,
            set_window_position,
            create_shortcut,
            add_to_steam,
            open_url,
            to_background,
            set_tray,
            set_discord_presence,
            start_with_windows,
            set_start_with_windows,
            check_update,
            install_update,
            open_folder,
            saves_info,
            saves_backup,
            saves_restore,
            saves_restore_plan,
            saves_remove,
            saves_choose,
            saves_open,
            saves_watch,
            saves_played,
            saves_to_report,
            saves_reported,
            saves_fine,
            saves_warning,
            saves_warned,
            saves_cloud,
            saves_synced,
            saves_cloud_off,
            saves_upload,
            saves_download,
            saves_folder_choose,
            saves_folder_summary,
            saves_folder_open,
            saves_folder_state,
            saves_folder_synced,
            saves_folder_put,
            saves_folder_take,
            create_app_folder,
            rename_app_folder,
            app_files,
            set_app_files,
            pick_program,
            pick_music,
            read_music,
            pick_background,
            read_background,
            pick_artwork,
            import_artwork,
            keep_artwork,
            mod_packages,
            mod_files,
            installed_mods,
            install_mod,
            uninstall_mod,
            steam_keyboard,
            pick_executable,
            list_programs,
            found_program,
            find_copies,
            pick_copy,
            check_copy,
            place_copy,
            copy_present,
            android_app_gone,
            file_kinds,
            missed_programs,
            list_protons,
            automatic_runner,
            pick_proton,
            gamepad::controllers,
            gamepad::page_focus,
            gamepad::controller_labels
        ])
        .build(tauri::generate_context!())
        .expect("error while running Quiver Launcher")
        .run(|_app, _event| {
            #[cfg(target_os = "linux")]
            if let tauri::RunEvent::Exit = _event {
                keyboard::kwin::unload_now();
            }
            // Back from an app started from Quiver: that's its play time.
            #[cfg(target_os = "android")]
            if let tauri::RunEvent::Resumed = _event {
                if let Some((id, started)) = _app.state::<Playing>().0.lock().unwrap().take() {
                    let _ = _app.emit("app-closed", Played { id, seconds: started.elapsed().as_secs() });
                }
            }
        });
}
