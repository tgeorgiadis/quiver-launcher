#[cfg(target_os = "android")]
mod android;
mod apps_folder;
mod browser;
mod flatpak;
mod gamepad;
mod host;
mod install;
mod keyboard;
mod launch;
mod mods;
mod msi;
mod picker;
#[cfg_attr(target_os = "android", allow(dead_code))]
mod sandbox;
mod secrets;
mod shortcut;
mod snapshots;
mod steam;
#[cfg(windows)]
mod tray;
mod update;
mod v3;
mod window_state;

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
    /// Where anonymous usage data goes: PostHog, or a stand-in in end-to-end tests.
    posthog_host: String,
    /// This build's version, sent with usage data.
    version: String,
    /// The home folder and the user's name on this computer, which usage data
    /// must never contain: the UI takes them out of anything it sends.
    home: Option<String>,
    user: Option<String>,
    /// Under gamescope, as a Steam Deck's Game Mode runs it: TV mode comes on by itself (tv.tsx).
    game_mode: bool,
    /// Steam's on-screen keyboard can be asked for (keyboard.rs): what a controller types with, so TV mode offers its
    /// search here.
    steam_keyboard: bool,
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
        gitlab_api: std::env::var("QUIVER_GITLAB_API").unwrap_or_else(|_| "https://gitlab.com/api/v4".into()),
        return_to: browser::RETURN_TO,
        os: OS,
        arch: ARCH,
        apps_dir: data.apps().to_string_lossy().into(),
        posthog_host: std::env::var("QUIVER_POSTHOG_HOST").unwrap_or_else(|_| "https://us.i.posthog.com".into()),
        version: app.package_info().version.to_string(),
        home: std::env::var(if OS == "windows" { "USERPROFILE" } else { "HOME" }).ok().filter(|h| !h.is_empty()),
        user: std::env::var("USERNAME").or_else(|_| std::env::var("USER")).ok().filter(|u| !u.is_empty()),
        game_mode: keyboard::game_mode(|name| std::env::var(name).ok()),
        steam_keyboard: cfg!(target_os = "linux") && keyboard::offered(|name| std::env::var(name).ok()),
    }
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
fn write_state(data: State<Data>, snapshotted: State<Snapshotted>, name: String, value: serde_json::Value) -> Result<(), String> {
    let path = state_file(&data, &name)?;
    let temp = path.with_extension("json.tmp");
    std::fs::create_dir_all(&data.0).map_err(|e| e.to_string())?;
    std::fs::write(&temp, serde_json::to_vec_pretty(&value).map_err(|e| e.to_string())?)
        .and_then(|_| std::fs::rename(&temp, &path))
        .map_err(|e| e.to_string())?;
    if snapshots::FILES.contains(&name.as_str()) {
        let first = !snapshotted.0.swap(true, std::sync::atomic::Ordering::Relaxed);
        if let Err(e) = snapshots::take(&data.0, first) {
            append_log(&data.0.join("quiver.log"), &format!("Couldn't take a library snapshot: {e}"));
        }
    }
    Ok(())
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

/// What starts an installed app: its program, or Wine (on Linux) or `open` (a macOS .app) with it.
/// `program` is one the player picked: exactly that file, never a guess.
fn target(data: &Data, folder: &str, dir: Option<String>, preferred: &[String], wine: bool, program: Option<String>, options: &launch::LaunchOptions) -> Result<shortcut::Target, String> {
    let (exe, wine) = match (&options.executable, program) {
        // The program the player chose in Launch options.
        (Some(chosen), program) => {
            let exe = match program {
                Some(_) => PathBuf::from(chosen),
                None => app_dir(data, folder, dir)?.join(chosen),
            };
            if !exe.exists() {
                return Err(format!("{chosen} isn't in the app's folder any more. Choose its program again in Launch options."));
            }
            let windows = exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe") || e.eq_ignore_ascii_case("bat"));
            (exe, OS == "linux" && windows)
        }
        (None, Some(program)) => {
            let exe = PathBuf::from(&program);
            if !exe.exists() {
                // A Windows installer's program: the player can choose it again, or run the installer again.
                if app_dir(data, folder, dir).is_ok_and(|d| msi::installed(&d)) {
                    return Err(format!("The program isn't at {program} any more. Choose it again in Launch options, or run its installer again."));
                }
                return Err(format!("The program isn't at {program} any more. Remove this app and add it again from where it is now."));
            }
            (exe, wine)
        }
        (None, None) => {
            let dir = app_dir(data, folder, dir)?;
            if msi::installed(&dir) {
                return Err("Quiver doesn't know which program its installer put on this computer yet. Press Play to choose it.".into());
            }
            if let Some(id) = flatpak::installed(&dir) {
                let flatpak = flatpak::program();
                let options = launch::parse(options.command.as_deref().unwrap_or_default())?;
                return Ok(shortcut::Target { exe: flatpak.clone(), program: flatpak, args: vec!["run".into(), id], env: vec![], dir, extract_appimage: false, steam_proton: false, options });
            }
            match install::find_executable(&dir, preferred, if wine { "windows" } else { OS }) {
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
    let options = launch::parse(options.command.as_deref().unwrap_or_default())?;
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
async fn launch(app: tauri::AppHandle, data: State<'_, Data>, id: Option<String>, folder: String, dir: Option<String>, preferred: Vec<String>, wine: bool, program: Option<String>, launch: Option<launch::LaunchOptions>) -> Result<(), String> {
    #[cfg(target_os = "android")]
    {
        let _ = (preferred, wine, program, launch);
        let package = android::package(&app_dir(&data, &folder, dir)?).ok_or("This app has no Android version installed. Get its Android download to play it here.")?;
        android::launch(&package)?;
        // Its play time: until the player comes back to Quiver.
        *app.state::<Playing>().0.lock().unwrap() = id.map(|id| (id, std::time::Instant::now()));
        Ok(())
    }
    #[cfg(not(target_os = "android"))]
    start(app, &data, id, &folder, dir, &preferred, wine, program, &launch.unwrap_or_default())
}

#[cfg(not(target_os = "android"))]
#[allow(clippy::too_many_arguments)]
fn start(app: tauri::AppHandle, data: &Data, id: Option<String>, folder: &str, dir: Option<String>, preferred: &[String], wine: bool, program: Option<String>, launch: &launch::LaunchOptions) -> Result<(), String> {
    let target = target(data, folder, dir, preferred, wine, program, launch)?;
    let (program, args, env) = target.command();
    let wine = program == Path::new("wine");
    let mut command = host::command(&program);
    command.args(&args).envs(env).current_dir(&target.dir);
    sandbox::check(&program)
        .and_then(|_| {
            sandbox::host(command)
                // The game outlives the launcher's console; it shouldn't hold it open.
                .stdin(std::process::Stdio::null())
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .spawn()
        })
        .map(|mut child| {
            // Its play time: from now until it closes. A program that hands off to another and returns at once counts only its own time.
            let Some(id) = id else { return };
            let started = std::time::Instant::now();
            app.state::<Running>().started(&id);
            std::thread::spawn(move || {
                let closed = child.wait().is_ok();
                app.state::<Running>().closed(&id);
                if closed {
                    let _ = app.emit("app-closed", Played { id, seconds: started.elapsed().as_secs() });
                }
            });
        })
        .map_err(|e| match e.kind() {
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

#[cfg(target_os = "android")]
#[derive(Default)]
struct Playing(std::sync::Mutex<Option<(String, std::time::Instant)>>);

/// The app's icon, saved for shortcuts; none if it can't be fetched.
async fn shortcut_icon(data: &Data, request: &shortcut::ShortcutRequest) -> Option<PathBuf> {
    let url = request.art.icon.as_deref()?;
    shortcut::save_icon(url, &data.0.join("icons").join(install::plain_name(&request.folder).ok()?)).await.ok()
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
    let mut shortcut = steam::Shortcut::new(&request.name, &target, icon.as_deref());
    let folder = install::plain_name(&request.folder)?;
    let art = [("", &request.art.header), ("p", &request.art.capsule), ("_hero", &request.art.hero), ("_logo", &request.art.logo)];
    for (suffix, url) in art {
        let Some(url) = url else { continue };
        let path = data.0.join("steam-art").join(format!("{folder}{suffix}.png"));
        if let Ok(image) = shortcut::download_image(url).await {
            if std::fs::create_dir_all(path.parent().unwrap()).is_ok() && image.save(&path).is_ok() {
                shortcut.art.push((suffix.into(), path));
            }
        }
    }
    let proton = if shortcut.proton { " It's set to run with Proton." } else { "" };
    if !steam::running() {
        steam::apply(&shortcut)?;
        return Ok(format!("{} is in Steam.{proton} Start Steam to see it.", request.name));
    }
    // Steam overwrites its shortcuts as it closes, so a waiting copy writes them after.
    if std::env::var_os("SteamGameId").is_some() {
        return Err("Steam is running Quiver, so it can't add games now. Switch to Desktop Mode (or close Steam) and add it from there.".into());
    }
    let queued = data.0.join(format!("steam-{folder}.json"));
    std::fs::write(&queued, serde_json::to_vec(&shortcut).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
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
    Ok(format!("{} will be added to Steam when Steam closes.{proton} Restart Steam to see it.", request.name))
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
    if let Some(program) = install["program"].as_str().filter(|_| install["installer"] == "msi") {
        if let Some(parent) = Path::new(program).parent().filter(|p| p.is_dir()) {
            return reveal(parent);
        }
    }
    let folder = install["folder"].as_str().unwrap_or_default();
    let dir = app_dir(&data, folder, install["dir"].as_str().map(str::to_string))?;
    if !dir.is_dir() {
        return Err("This app's folder isn't there any more.".into());
    }
    reveal(&dir)
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

/// Renames an installed app's folder in the apps folder, and its Proton
/// prefix with it. Not while the launcher has it running: its files are in use.
#[tauri::command]
fn rename_app_folder(data: State<Data>, running: State<Running>, id: String, from: String, to: String) -> Result<(), String> {
    if running.has(&id) {
        return Err("Close the app first, then rename its folder.".into());
    }
    let from = install::plain_name(&from)?;
    let to = install::plain_name(&to)?;
    apps_folder::rename(&data.apps(), &data.0.join("proton"), from, to)
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
    let path = picker::choose(&app, &window, picker::Want::File, "Choose the program that starts the app", dir.as_deref().map(Path::new), filter).await?;
    Some(path.to_string_lossy().into())
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

/// Asks which program starts an installed app, starting in its folder; the
/// answer is relative to that folder so it survives the folder moving. An app
/// the player added from elsewhere (`program`) can start anything.
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
        None => picker::choose(&app, &window, picker::Want::File, "Choose the program that starts the app", Some(&root), None).await,
    };
    let Some(picked) = picked else { return Ok(None) };
    if program.is_some() {
        return Ok(Some(picked.to_string_lossy().into()));
    }
    let inside = picked.canonicalize().ok().zip(root.canonicalize().ok()).and_then(|(p, r)| p.strip_prefix(r).ok().map(Path::to_path_buf));
    match inside {
        Some(relative) if !relative.as_os_str().is_empty() => Ok(Some(relative.to_string_lossy().into())),
        _ => Err("Choose a program inside the app's folder.".into()),
    }
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

/// What Automatic runs a Windows app with on Linux, by name: a Proton, else Wine, else nothing (None).
#[tauri::command]
fn automatic_runner() -> Option<String> {
    automatic_proton().map(|p| p.name).or_else(|| sandbox::which("wine").map(|_| "Wine".into()))
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

/// Why the installed apps (`folders`, in the apps folder) can't move to `to`, if they can't.
#[tauri::command]
fn apps_folder_refusal(data: State<Data>, to: String, folders: Vec<String>) -> Option<String> {
    apps_folder::refuse(&data.apps(), Path::new(&to), &folders)
}

/// Moves the installed apps (`folders`, in the apps folder) to `to` and
/// installs there from now on; resolves to the new apps folder. Reports each
/// app as it moves ("apps-moving").
#[tauri::command]
async fn move_apps_folder(app: tauri::AppHandle, to: String, folders: Vec<String>) -> Result<String, String> {
    let handle = app.clone();
    tauri::async_runtime::spawn_blocking(move || {
        let data = handle.state::<Data>();
        let from = data.apps();
        let to = PathBuf::from(&to);
        apps_folder::move_apps(&data.0, &from, &to, &folders, |m| {
            let _ = handle.emit("apps-moving", m);
        })?;
        *data.1.lock().unwrap() = to.clone();
        Ok(to.to_string_lossy().into())
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
    let adopted = dir.is_some();
    let dir = app_dir(&app.state::<Data>(), &folder, dir)?;
    // Windows' uninstaller waits for the player, so not on the window's thread.
    tauri::async_runtime::spawn_blocking(move || {
        // Windows removes what its installer put on the computer, first.
        if msi::installed(&dir) {
            msi::uninstall(&dir)?;
        }
        // Android removes an app installed from an APK, once the player confirms it.
        #[cfg(target_os = "android")]
        if let Some(package) = android::package(&dir) {
            android::uninstall(&package)?;
        }
        // Only a folder that still looks like an install is removed: one Quiver
        // installed into, or a 3.x install. Never a folder of the player's own.
        if dir.exists() && install::may_remove(&dir, adopted) {
            if let Some(id) = flatpak::installed(&dir) {
                flatpak::uninstall(&id);
            }
            #[cfg(not(target_os = "android"))]
            trash::delete(&dir).or_else(|_| std::fs::remove_dir_all(&dir)).map_err(|e| e.to_string())?;
            #[cfg(target_os = "android")]
            std::fs::remove_dir_all(&dir).map_err(|e| e.to_string())?;
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

#[tauri::command]
fn find_v3_library() -> Option<v3::Library> {
    v3::find()
}

/// The 3.x library in a folder the player picked, or the folder around it;
/// with `apps`, its installs in that Apps folder.
#[tauri::command]
fn find_v3_library_in(path: String, apps: Option<String>) -> Option<v3::Library> {
    v3::find_in(Path::new(&path), apps.as_deref().map(Path::new))
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
            app.manage(Data(dir, std::sync::Mutex::new(apps)));
            app.manage(Snapshotted(Default::default()));
            app.manage(gamepad::Pads::default());
            app.manage(Installing::default());
            app.manage(Running::default());
            #[cfg(target_os = "android")]
            app.manage(Playing::default());
            app.manage(picker::Picks::default());
            gamepad::start(app.handle().clone());
            #[cfg(target_os = "linux")]
            {
                keyboard::kwin::listen(app.handle().clone());
                keyboard::kwin::load();
            }
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            config,
            cancel_install,
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
            find_v3_library_in,
            pick_folder,
            picker::picked,
            picker::browse,
            apps_folder_refusal,
            move_apps_folder,
            secret_get,
            secret_set,
            browser_sign_in,
            browser_sign_in_cancel,
            log_error,
            fps_probe,
            fps_report,
            set_fullscreen,
            set_system_title_bar,
            set_theme,
            create_shortcut,
            add_to_steam,
            open_url,
            to_background,
            set_tray,
            start_with_windows,
            set_start_with_windows,
            check_update,
            install_update,
            open_folder,
            create_app_folder,
            rename_app_folder,
            pick_program,
            mod_packages,
            mod_files,
            installed_mods,
            install_mod,
            uninstall_mod,
            steam_keyboard,
            pick_executable,
            list_programs,
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
