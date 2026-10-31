//! Android apps: installing, starting and removing APKs through the system's
//! package installer (ApkPlugin.kt in gen/android). An app installed from an
//! APK keeps only its package name in its folder; Android holds the app.
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;
use tauri::plugin::mobile::PluginInvokeError;
use tauri::plugin::{PluginHandle, TauriPlugin};
use tauri::Wry;

/// Written into an installed app's folder; holds its package name.
pub const FILE: &str = ".quiver-package";

static PLUGIN: OnceLock<PluginHandle<Wry>> = OnceLock::new();

pub fn init() -> TauriPlugin<Wry> {
    tauri::plugin::Builder::new("apk")
        .setup(|_, api| {
            let _ = PLUGIN.set(api.register_android_plugin("com.quiverlauncher.app", "ApkPlugin")?);
            Ok(())
        })
        .build()
}

/// Runs one of the plugin's commands; blocks until it answers, which can
/// wait on the player (installing and uninstalling ask them first).
fn run<T: DeserializeOwned>(command: &str, payload: impl Serialize) -> Result<T, String> {
    let plugin = PLUGIN.get().ok_or("Android's package installer isn't ready yet.")?;
    plugin.run_mobile_plugin(command, payload).map_err(|e| match e {
        PluginInvokeError::InvokeRejected(e) => e.message.unwrap_or_else(|| "Android refused.".into()),
        e => e.to_string(),
    })
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Package<'a> {
    package_name: &'a str,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct App {
    pub package_name: String,
}

/// Installs an APK, once the player allows it; fails with "cancelled" when they don't.
pub fn install(apk: &Path) -> Result<App, String> {
    #[derive(Serialize)]
    struct Apk {
        path: String,
    }
    run("install", Apk { path: apk.to_string_lossy().into_owned() })
}

pub fn launch(package: &str) -> Result<(), String> {
    run("launch", Package { package_name: package })
}

/// Asks Android to uninstall the app; fails with "cancelled" when the player says no.
pub fn uninstall(package: &str) -> Result<(), String> {
    run("uninstall", Package { package_name: package })
}

pub fn open_url(url: &str) -> Result<(), String> {
    #[derive(Serialize)]
    struct Url<'a> {
        url: &'a str,
    }
    run("openUrl", Url { url })
}

/// Puts Quiver behind the other apps, as Home does: still running.
pub fn to_background() -> Result<(), String> {
    run("toBackground", serde_json::json!({}))
}

/// The package an app's folder was installed as, if it was installed from an APK.
pub fn package(dir: &Path) -> Option<String> {
    fs::read_to_string(dir.join(FILE)).ok().map(|p| p.trim().to_string()).filter(|p| !p.is_empty())
}

/// The APK among an install's files.
pub fn apk_in(dir: &Path) -> Option<PathBuf> {
    fs::read_dir(dir)
        .ok()?
        .flatten()
        .map(|e| e.path())
        .find(|p| p.is_file() && p.extension().is_some_and(|e| e.eq_ignore_ascii_case("apk")))
}
