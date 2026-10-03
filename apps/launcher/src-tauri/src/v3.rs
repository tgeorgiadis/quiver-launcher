//! Finds a Quiver Launcher 3 library on this computer so its apps can move
//! over, installed copies included.
use serde::Serialize;
use serde_json::Value;
use std::path::{Path, PathBuf};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct OldApp {
    name: String,
    repository: Option<String>,
    provider: String,
    /// The installed copy, when there is one.
    dir: Option<String>,
    version: Option<String>,
}

/// Where 3.x keeps apps.json: its installer's folder, or the XDG / Application Support fallback.
fn data_dirs() -> Vec<PathBuf> {
    let env = |k: &str| std::env::var_os(k).map(PathBuf::from);
    let home = env("HOME").or_else(|| env("USERPROFILE"));
    [
        env("QUIVER_V3_DATA"),
        env("LOCALAPPDATA").map(|d| d.join("QuiverLauncher")),
        env("XDG_DATA_HOME").map(|d| d.join("QuiverLauncher")),
        home.as_ref().map(|h| h.join(".local/share/QuiverLauncher")),
        home.as_ref().map(|h| h.join("Library/Application Support/QuiverLauncher")),
    ]
    .into_iter()
    .flatten()
    .collect()
}

/// A field by name in any letter case (3.x wrote both camelCase and PascalCase).
fn field<'a>(v: &'a Value, name: &str) -> Option<&'a str> {
    v.as_object()?.iter().find(|(k, _)| k.eq_ignore_ascii_case(name))?.1.as_str().filter(|s| !s.trim().is_empty())
}

fn read(path: &Path) -> Option<Value> {
    serde_json::from_slice(&std::fs::read(path).ok()?).ok()
}

pub fn find() -> Vec<OldApp> {
    let Some(dir) = data_dirs().into_iter().find(|d| d.join("apps.json").is_file()) else { return vec![] };
    let Some(doc) = read(&dir.join("apps.json")) else { return vec![] };
    let apps_dir = read(&dir.join("settings.json"))
        .and_then(|s| field(&s, "appsPath").map(PathBuf::from))
        .unwrap_or_else(|| dir.join("Apps"));
    let list = doc.as_object().and_then(|o| o.iter().find(|(k, _)| k.eq_ignore_ascii_case("apps"))).map(|(_, v)| v);
    list.and_then(Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(|app| {
            let name = field(app, "name")?.to_string();
            let install = field(app, "installPath")
                .map(PathBuf::from)
                .or_else(|| field(app, "folderName").map(|f| apps_dir.join(f)))
                .filter(|d| d.is_dir());
            Some(OldApp {
                name,
                repository: field(app, "repository").map(str::to_string),
                provider: field(app, "repositorySource").unwrap_or("github").to_lowercase(),
                version: install.as_ref().and_then(|d| std::fs::read_to_string(d.join("version.txt")).ok()).map(|v| v.trim().to_string()),
                dir: install.map(|d| d.to_string_lossy().into()),
            })
        })
        .collect()
}
