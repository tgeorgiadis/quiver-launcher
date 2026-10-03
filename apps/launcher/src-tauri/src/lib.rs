mod install;

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

/// An app folder this launcher manages; refuses anything outside `apps`.
fn app_dir(data: &Data, folder: &str) -> Result<PathBuf, String> {
    Ok(data.apps().join(install::plain_name(folder)?))
}

#[tauri::command]
fn launch(data: State<Data>, folder: String, preferred: Vec<String>, wine: bool) -> Result<(), String> {
    let dir = app_dir(&data, &folder)?;
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
fn uninstall(data: State<Data>, folder: String) -> Result<(), String> {
    let dir = app_dir(&data, &folder)?;
    if dir.exists() {
        trash::delete(&dir).or_else(|_| std::fs::remove_dir_all(&dir)).map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[tauri::command]
fn installed_version(data: State<Data>, folder: String) -> Option<String> {
    let dir = app_dir(&data, &folder).ok()?;
    std::fs::read_to_string(dir.join(install::VERSION_FILE)).ok()
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
            installed_version
        ])
        .run(tauri::generate_context!())
        .expect("error while running Quiver Launcher");
}
