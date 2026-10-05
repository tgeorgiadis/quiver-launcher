mod browser;
mod gamepad;
mod install;
mod shortcut;
mod steam;
mod v3;

use serde::Serialize;
use std::path::{Path, PathBuf};
use std::process::Command;
use tauri::{Emitter, Manager, State};
use tauri_plugin_dialog::DialogExt;

/// Where the launcher keeps its files: `QUIVER_DATA`, else a `data` folder
/// beside a portable install (one with `portable.txt` next to it), else the
/// OS's app data folder.
struct Data(PathBuf);

impl Data {
    fn apps(&self) -> PathBuf {
        self.0.join("apps")
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
fn write_state(data: State<Data>, name: String, value: serde_json::Value) -> Result<(), String> {
    let path = state_file(&data, &name)?;
    let temp = path.with_extension("json.tmp");
    std::fs::create_dir_all(&data.0).map_err(|e| e.to_string())?;
    std::fs::write(&temp, serde_json::to_vec_pretty(&value).map_err(|e| e.to_string())?)
        .and_then(|_| std::fs::rename(&temp, &path))
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn install(app: tauri::AppHandle, request: install::InstallRequest) -> Result<install::Installed, String> {
    let apps = app.state::<Data>().apps();
    install::install(&apps, request, |p| {
        let _ = app.emit("install-progress", p);
    })
    .await
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
fn target(data: &Data, folder: &str, dir: Option<String>, preferred: &[String], wine: bool, program: Option<String>) -> Result<shortcut::Target, String> {
    let (exe, wine) = match program {
        Some(program) => {
            let exe = PathBuf::from(&program);
            if !exe.exists() {
                return Err(format!("The program isn't at {program} any more. Remove this app and add it again from where it is now."));
            }
            (exe, wine)
        }
        None => {
            let dir = app_dir(data, folder, dir)?;
            match install::find_executable(&dir, preferred, if wine { "windows" } else { OS }) {
                Some(exe) => (exe, wine),
                // A folder with only a Windows build runs through Wine on Linux.
                None if OS == "linux" && !wine => (install::find_executable(&dir, preferred, "windows").ok_or(NO_PROGRAM)?, true),
                None => return Err(NO_PROGRAM.into()),
            }
        }
    };
    let (program, args) = if wine {
        (PathBuf::from("wine"), vec![exe.to_string_lossy().into_owned()])
    } else if OS == "macos" && exe.extension().is_some_and(|e| e == "app") {
        (PathBuf::from("open"), vec![exe.to_string_lossy().into_owned()])
    } else if OS == "windows" && exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("lnk")) {
        // A shortcut the player picked: Explorer starts what it points at.
        (PathBuf::from("explorer"), vec![exe.to_string_lossy().into_owned()])
    } else {
        (exe.clone(), vec![])
    };
    // Many distros no longer ship FUSE 2, which AppImages mount themselves with.
    let extract_appimage = exe.extension().is_some_and(|e| e.eq_ignore_ascii_case("appimage")) && !has_fuse2();
    Ok(shortcut::Target { dir: exe.parent().unwrap_or(Path::new(".")).to_path_buf(), exe, program, args, extract_appimage })
}

#[tauri::command]
fn launch(data: State<Data>, folder: String, dir: Option<String>, preferred: Vec<String>, wine: bool, program: Option<String>) -> Result<(), String> {
    let target = target(&data, &folder, dir, &preferred, wine, program)?;
    let mut command = Command::new(&target.program);
    command.args(&target.args);
    if target.extract_appimage {
        command.env("APPIMAGE_EXTRACT_AND_RUN", "1");
    }
    command
        .current_dir(&target.dir)
        // The game outlives the launcher's console; it shouldn't hold it open.
        .stdin(std::process::Stdio::null())
        .stdout(std::process::Stdio::null())
        .stderr(std::process::Stdio::null())
        .spawn()
        .map(|_| ())
        .map_err(|e| match e.kind() {
            std::io::ErrorKind::NotFound if wine => "This is a Windows app. Install Wine to play it on Linux.".into(),
            // A downloaded AppImage or program isn't executable until it's allowed to run.
            std::io::ErrorKind::PermissionDenied if OS != "windows" => format!(
                "{} isn't allowed to run as a program. Allow it in its file properties (Permissions), or run chmod +x on it, then press Play again.",
                target.exe.display()
            ),
            _ => format!("Couldn't start {}: {e}", target.exe.display()),
        })
}

/// The app's icon, saved for shortcuts; none if it can't be fetched.
async fn shortcut_icon(data: &Data, request: &shortcut::ShortcutRequest) -> Option<PathBuf> {
    let url = request.art.icon.as_deref()?;
    shortcut::save_icon(url, &data.0.join("icons").join(install::plain_name(&request.folder).ok()?)).await.ok()
}

/// Puts a shortcut that starts the game on the desktop.
#[tauri::command]
async fn create_shortcut(app: tauri::AppHandle, request: shortcut::ShortcutRequest) -> Result<String, String> {
    let data = app.state::<Data>();
    let target = target(&data, &request.folder, request.dir.clone(), &request.preferred, request.wine, request.program.clone())?;
    let icon = shortcut_icon(&data, &request).await;
    let path = shortcut::desktop(&target, &request.name, icon.as_deref())?;
    Ok(format!("{} is on your desktop.", path.file_name().unwrap_or_default().to_string_lossy()))
}

/// Adds the game to Steam as a non-Steam game, with its artwork; says what happened.
#[tauri::command]
async fn add_to_steam(app: tauri::AppHandle, request: shortcut::ShortcutRequest) -> Result<String, String> {
    let data = app.state::<Data>();
    let target = target(&data, &request.folder, request.dir.clone(), &request.preferred, request.wine, request.program.clone())?;
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
    let exe = std::env::var_os("APPIMAGE").map(PathBuf::from).or_else(|| std::env::current_exe().ok()).ok_or("Couldn't find Quiver Launcher's program.")?;
    Command::new(exe)
        .arg("--add-to-steam")
        .arg(&queued)
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
    open::that_detached(dir).map_err(|e| e.to_string())
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

/// Asks the player for a program on this computer; null if they cancel.
/// `QUIVER_PICK` answers instead in end-to-end tests.
#[tauri::command]
async fn pick_program(app: tauri::AppHandle, window: tauri::WebviewWindow) -> Option<String> {
    if let Some(path) = std::env::var_os("QUIVER_PICK") {
        return Some(path.to_string_lossy().into());
    }
    let (tx, rx) = tokio::sync::oneshot::channel();
    let dialog = app.dialog().file().set_parent(&window).set_title("Choose the program that starts the app");
    let dialog = match OS {
        "windows" => dialog.add_filter("Programs", &["exe", "lnk", "bat", "cmd"]),
        "macos" => dialog.add_filter("Apps", &["app"]),
        _ => dialog,
    };
    dialog.pick_file(move |path| {
        let _ = tx.send(path);
    });
    let path = rx.await.ok().flatten()?.into_path().ok()?;
    Some(path.to_string_lossy().into())
}

/// Opens a web page in the browser.
#[tauri::command]
fn open_url(url: String) -> Result<(), String> {
    if !url.starts_with("https://") && !url.starts_with("http://") {
        return Err("Only web addresses can be opened.".into());
    }
    open::that_detached(&url).map_err(|e| e.to_string())
}

fn has_fuse2() -> bool {
    // Without ldconfig, extracting still works, just more slowly.
    Command::new("/sbin/ldconfig")
        .arg("-p")
        .output()
        .is_ok_and(|o| String::from_utf8_lossy(&o.stdout).contains("libfuse.so.2"))
}

#[tauri::command]
fn uninstall(data: State<Data>, folder: String, dir: Option<String>) -> Result<(), String> {
    let adopted = dir.is_some();
    let dir = app_dir(&data, &folder, dir)?;
    // Only a folder that still looks like an install is removed: one Quiver
    // installed into, or a 3.x install. Never a folder of the player's own.
    if dir.exists() && install::may_remove(&dir, adopted) {
        trash::delete(&dir).or_else(|_| std::fs::remove_dir_all(&dir)).map_err(|e| e.to_string())?;
    }
    Ok(())
}

/// Sign-in tokens: in the OS keychain, or a file in the data folder where
/// there's none (some Linux desktops, and when QUIVER_DATA is set).
fn secret_file(data: &Data) -> PathBuf {
    data.0.join("account.json")
}
fn keychain(key: &str) -> Option<keyring::Entry> {
    if std::env::var_os("QUIVER_DATA").is_some() {
        return None;
    }
    keyring::Entry::new("Quiver Launcher", key).ok()
}
fn secrets(data: &Data) -> serde_json::Map<String, serde_json::Value> {
    std::fs::read(secret_file(data)).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default()
}

#[tauri::command]
fn secret_get(data: State<Data>, key: String) -> Option<String> {
    keychain(&key)
        .and_then(|e| e.get_password().ok())
        .or_else(|| secrets(&data).get(&key)?.as_str().map(str::to_string))
}

#[tauri::command]
fn secret_set(data: State<Data>, key: String, value: Option<String>) -> Result<(), String> {
    if let Some(entry) = keychain(&key) {
        let saved = match &value {
            Some(v) => entry.set_password(v),
            None => entry.delete_credential().or_else(|e| match e {
                keyring::Error::NoEntry => Ok(()),
                e => Err(e),
            }),
        };
        if saved.is_ok() {
            return Ok(());
        }
    }
    let mut all = secrets(&data);
    match value {
        Some(v) => all.insert(key, v.into()),
        None => all.remove(&key),
    };
    std::fs::create_dir_all(&data.0).map_err(|e| e.to_string())?;
    std::fs::write(secret_file(&data), serde_json::to_vec(&all).map_err(|e| e.to_string())?).map_err(|e| e.to_string())
}

/// Opens a GitHub or Discord sign-in page and waits for it to come back,
/// then brings the launcher back in front of the browser.
#[tauri::command]
async fn browser_sign_in(window: tauri::WebviewWindow, url: String) -> Result<browser::Callback, String> {
    let result = tauri::async_runtime::spawn_blocking(move || browser::sign_in(&url)).await.map_err(|e| e.to_string())?;
    if result.is_ok() {
        let _ = window.unminimize();
        let _ = window.set_focus();
    }
    result
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

#[tauri::command]
fn set_fullscreen(window: tauri::WebviewWindow, on: bool) -> Result<(), String> {
    window.set_fullscreen(on).map_err(|e| e.to_string())
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
fn find_v3_library() -> Vec<v3::OldApp> {
    v3::find()
}

/// The window from tauri.conf.json. End-to-end tests on Windows set
/// QUIVER_DEBUG_PORT on a debug build and attach to WebView2 there, since its
/// runtime no longer takes the debugging port WebDriver passes it.
fn main_window(app: &tauri::App, dir: &Path, theme: Option<tauri::Theme>) -> tauri::Result<()> {
    let builder = tauri::WebviewWindowBuilder::from_config(app.handle(), &app.config().app.windows[0])?.theme(theme);
    #[cfg(all(windows, debug_assertions))]
    let builder = match std::env::var("QUIVER_DEBUG_PORT") {
        Ok(port) => builder
            .additional_browser_args(&format!(
                "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection --remote-debugging-port={port}"
            ))
            .data_directory(dir.join("webview")),
        Err(_) => builder,
    };
    let _ = dir;
    builder.build()?;
    Ok(())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let args: Vec<String> = std::env::args().collect();
    if let [_, flag, file] = args.as_slice() {
        if flag == "--add-to-steam" {
            return steam::wait_and_apply(Path::new(file));
        }
    }
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let portable = std::env::current_exe()
                .ok()
                .and_then(|exe| exe.parent().map(Path::to_path_buf))
                .filter(|dir| dir.join("portable.txt").exists())
                .map(|dir| dir.join("data"));
            let dir = std::env::var_os("QUIVER_DATA")
                .map(PathBuf::from)
                .or(portable)
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
            main_window(app, &dir, theme_of(settings["theme"].as_str()))?;
            // Paint behind the page in the theme it will draw in.
            if let Some(window) = app.get_webview_window("main") {
                if let Ok(theme) = window.theme() {
                    let _ = window.set_background_color(Some(backdrop(theme)));
                }
            }
            if settings["fullscreen"] == true || std::env::args().any(|a| a == "--fullscreen") {
                if let Some(window) = app.get_webview_window("main") {
                    let _ = window.set_fullscreen(true);
                }
            }
            app.manage(Data(dir));
            app.manage(gamepad::Pads::default());
            gamepad::start(app.handle().clone());
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            config,
            read_state,
            write_state,
            install,
            launch,
            uninstall,
            find_v3_library,
            secret_get,
            secret_set,
            browser_sign_in,
            log_error,
            set_fullscreen,
            set_theme,
            create_shortcut,
            add_to_steam,
            open_url,
            open_folder,
            create_app_folder,
            pick_program,
            gamepad::controllers
        ])
        .run(tauri::generate_context!())
        .expect("error while running Quiver Launcher");
}
