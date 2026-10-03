mod browser;
mod install;
mod v3;

use serde::Serialize;
use std::path::{Path, PathBuf};
use std::process::Command;
use tauri::{Emitter, Manager, State};

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
    /// Where GitHub and Discord sign-in return to.
    return_to: &'static str,
    os: &'static str,
    arch: &'static str,
    apps_dir: String,
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
fn config(data: State<Data>) -> Config {
    Config {
        api: std::env::var("QUIVER_API").unwrap_or_else(|_| "https://api.quiverlauncher.com/api/v1".into()),
        convex: std::env::var("QUIVER_CONVEX").unwrap_or_else(|_| "https://convex.quiverlauncher.com".into()),
        account_api: std::env::var("QUIVER_ACCOUNT_API").ok(),
        return_to: browser::RETURN_TO,
        os: OS,
        arch: ARCH,
        apps_dir: data.apps().to_string_lossy().into(),
    }
}

/// The launcher's own JSON files: the library, installs and the catalog cache.
fn state_file(data: &Data, name: &str) -> Result<PathBuf, String> {
    match name {
        "library" | "installs" | "catalog" => Ok(data.0.join(format!("{name}.json"))),
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

#[tauri::command]
fn launch(data: State<Data>, folder: String, dir: Option<String>, preferred: Vec<String>, wine: bool) -> Result<(), String> {
    let dir = app_dir(&data, &folder, dir)?;
    let exe = install::find_executable(&dir, &preferred, if wine { "windows" } else { OS })
        .ok_or("Couldn't find a program to start in this app's folder.")?;
    let mut command = if wine {
        let mut wine = Command::new("wine");
        wine.arg(&exe);
        wine
    } else if OS == "macos" && exe.extension().is_some_and(|e| e == "app") {
        let mut open = Command::new("open");
        open.arg(&exe);
        open
    } else {
        Command::new(&exe)
    };
    command
        .current_dir(exe.parent().unwrap_or(Path::new(&dir)))
        .spawn()
        .map(|_| ())
        .map_err(|e| match wine {
            true if e.kind() == std::io::ErrorKind::NotFound => "This is a Windows app. Install Wine to play it on Linux.".into(),
            _ => format!("Couldn't start {}: {e}", exe.display()),
        })
}

#[tauri::command]
fn uninstall(data: State<Data>, folder: String, dir: Option<String>) -> Result<(), String> {
    let adopted = dir.is_some();
    let dir = app_dir(&data, &folder, dir)?;
    // An adopted folder is only removed while it still looks like an install.
    if dir.exists() && (!adopted || dir.join("version.txt").is_file()) {
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

/// Opens a GitHub or Discord sign-in page and waits for it to come back.
#[tauri::command]
async fn browser_sign_in(url: String) -> Result<browser::Callback, String> {
    tauri::async_runtime::spawn_blocking(move || browser::sign_in(&url)).await.map_err(|e| e.to_string())?
}

#[tauri::command]
fn find_v3_library() -> Vec<v3::OldApp> {
    v3::find()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
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
            app.manage(Data(dir));
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
            browser_sign_in
        ])
        .run(tauri::generate_context!())
        .expect("error while running Quiver Launcher");
}
