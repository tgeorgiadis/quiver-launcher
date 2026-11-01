//! Mods for an installed app, from the Thunderstore communities and
//! GameBanana games its catalog entry names. Installed mods are recorded in
//! `.quiver-mods.json` in the app's folder (as Quiver Launcher 3 does), with
//! the files each one put in the mods folder, so they update and uninstall
//! cleanly and nothing else there is touched.
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Component, Path, PathBuf};

pub const RECORD: &str = ".quiver-mods.json";

/// Where an app's mods go, from its catalog entry.
#[derive(Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Config {
    /// Inside the app's folder, such as `mods`.
    pub path: String,
    /// `flat` (every mod's files straight in the folder) or `folderPerMod`.
    #[serde(default)]
    pub layout: Option<String>,
}

#[derive(Deserialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct Source {
    pub provider: String,
    pub source_url: String,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct ModFile {
    pub id: String,
    pub name: String,
    pub url: String,
    #[serde(default)]
    pub size: u64,
    #[serde(default)]
    pub description: String,
}

#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct Package {
    /// `thunderstore:Owner-Name` or `gamebanana:123`.
    pub key: String,
    pub provider: String,
    pub name: String,
    pub author: String,
    pub description: String,
    /// A version number, or for a GameBanana mod without one, when it last changed.
    pub version: String,
    /// When it last changed, in milliseconds.
    pub updated: u64,
    pub downloads: u64,
    pub rating: u64,
    pub icon: Option<String>,
    pub url: String,
    pub nsfw: bool,
    /// Keys of the mods it needs, in this source.
    pub dependencies: Vec<String>,
    /// Its download, when there's one (Thunderstore); GameBanana mods list theirs with `files`.
    pub files: Vec<ModFile>,
}

#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct Installed {
    pub key: String,
    pub name: String,
    pub version: String,
    /// What it put in the mods folder, relative to it.
    pub files: Vec<String>,
    /// The file it came from, so an update can pick the same one.
    #[serde(default)]
    pub file: String,
}

#[derive(Serialize, Deserialize, Default)]
struct Record {
    mods: Vec<Installed>,
}

fn thunderstore_base() -> String {
    std::env::var("QUIVER_THUNDERSTORE").unwrap_or_else(|_| "https://thunderstore.io".into())
}
fn gamebanana_base() -> String {
    std::env::var("QUIVER_GAMEBANANA").unwrap_or_else(|_| "https://gamebanana.com".into())
}

/// A Thunderstore community from `thunderstore.io/c/<slug>/`, or the slug itself.
pub fn community(url: &str) -> Option<String> {
    let slug = url.split("/c/").nth(1).unwrap_or(url).split('/').next()?.trim();
    (!slug.is_empty() && slug.chars().all(|c| c.is_ascii_alphanumeric() || "-_.".contains(c))).then(|| slug.to_string())
}

/// A GameBanana game id from `gamebanana.com/games/<id>`, `/mods/games/<id>`, or the id.
pub fn game(url: &str) -> Option<String> {
    let id = url.trim().trim_end_matches('/').rsplit('/').next()?;
    (!id.is_empty() && id.chars().all(|c| c.is_ascii_digit())).then(|| id.to_string())
}

fn client() -> Result<reqwest::Client, String> {
    reqwest::Client::builder().user_agent("QuiverLauncher").build().map_err(|e| e.to_string())
}

async fn get_json(url: &str) -> Result<serde_json::Value, String> {
    let response = client()?.get(url).send().await.and_then(|r| r.error_for_status()).map_err(|e| format!("Couldn't load mods: {e}"))?;
    let bytes = response.bytes().await.map_err(|e| format!("Couldn't load mods: {e}"))?;
    serde_json::from_slice(&bytes).map_err(|e| format!("Couldn't read the mod list: {e}"))
}

fn millis(iso: &str) -> u64 {
    // "2026-10-10T01:08:18.748256Z": enough to sort and compare by.
    let digits: Vec<u64> = iso.split(|c: char| !c.is_ascii_digit()).filter(|s| !s.is_empty()).take(6).map(|s| s.parse().unwrap_or(0)).collect();
    let [y, mo, d, h, mi, s] = <[u64; 6]>::try_from(digits).unwrap_or([1970, 1, 1, 0, 0, 0]);
    // Days from civil (Howard Hinnant's algorithm).
    let (y, mo) = if mo <= 2 { (y - 1, mo + 9) } else { (y, mo - 3) };
    let era = y / 400;
    let yoe = y - era * 400;
    let doy = (153 * mo + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    let days = era * 146_097 + doe - 719_468;
    ((days * 86_400) + h * 3600 + mi * 60 + s) * 1000
}

/// Every mod in a source, newest version of each.
pub async fn packages(source: &Source) -> Result<Vec<Package>, String> {
    match source.provider.as_str() {
        "thunderstore" => {
            let slug = community(&source.source_url).ok_or("That Thunderstore community can't be read.")?;
            let list = get_json(&format!("{}/c/{slug}/api/v1/package/", thunderstore_base())).await?;
            Ok(thunderstore(&list))
        }
        "gamebanana" => {
            let id = game(&source.source_url).ok_or("That GameBanana game can't be read.")?;
            let mut all = Vec::new();
            for page in 1..=10 {
                let list = get_json(&format!("{}/apiv11/Mod/Index?_nPerpage=50&_aFilters%5BGeneric_Game%5D={id}&_nPage={page}", gamebanana_base())).await?;
                all.extend(gamebanana(&list));
                if list["_aMetadata"]["_bIsComplete"].as_bool().unwrap_or(true) {
                    break;
                }
            }
            Ok(all)
        }
        other => Err(format!("Quiver can't get mods from {other}.")),
    }
}

pub fn thunderstore(list: &serde_json::Value) -> Vec<Package> {
    let items = list.as_array().cloned().unwrap_or_default();
    let names: Vec<String> = items.iter().filter_map(|p| p["full_name"].as_str().map(str::to_string)).collect();
    items
        .iter()
        .filter(|p| !p["is_deprecated"].as_bool().unwrap_or(false))
        .filter_map(|p| {
            let full = p["full_name"].as_str()?;
            let latest = p["versions"].as_array()?.first()?;
            let s = |v: &serde_json::Value| v.as_str().unwrap_or_default().to_string();
            // "Owner-Name-1.0.0": the package, without its version; only ones in this community.
            let dependencies = latest["dependencies"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(|d| d.as_str()?.rsplit_once('-').map(|(name, _)| name.to_string()))
                .filter(|name| names.contains(name))
                .map(|name| format!("thunderstore:{name}"))
                .collect();
            let version = s(&latest["version_number"]);
            Some(Package {
                key: format!("thunderstore:{full}"),
                provider: "thunderstore".into(),
                name: s(&p["name"]).replace('_', " "),
                author: s(&p["owner"]),
                description: s(&latest["description"]),
                updated: millis(&s(&p["date_updated"])),
                downloads: p["versions"].as_array().map_or(0, |v| v.iter().filter_map(|v| v["downloads"].as_u64()).sum()),
                rating: p["rating_score"].as_u64().unwrap_or(0),
                icon: latest["icon"].as_str().map(str::to_string),
                url: s(&p["package_url"]),
                nsfw: p["has_nsfw_content"].as_bool().unwrap_or(false),
                dependencies,
                files: vec![ModFile { id: version.clone(), name: format!("{full}-{version}.zip"), url: s(&latest["download_url"]), size: latest["file_size"].as_u64().unwrap_or(0), description: String::new() }],
                version,
            })
        })
        .collect()
}

pub fn gamebanana(list: &serde_json::Value) -> Vec<Package> {
    list["_aRecords"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|r| r["_bHasFiles"].as_bool().unwrap_or(true) && !r["_bIsObsolete"].as_bool().unwrap_or(false))
        .filter_map(|r| {
            let id = r["_idRow"].as_u64()?;
            let updated = r["_tsDateModified"].as_u64().or_else(|| r["_tsDateAdded"].as_u64()).unwrap_or(0) * 1000;
            let version = r["_sVersion"].as_str().filter(|v| !v.trim().is_empty()).map_or_else(|| updated.to_string(), |v| v.trim().to_string());
            let image = r["_aPreviewMedia"]["_aImages"].get(0);
            Some(Package {
                key: format!("gamebanana:{id}"),
                provider: "gamebanana".into(),
                name: r["_sName"].as_str().unwrap_or_default().into(),
                author: r["_aSubmitter"]["_sName"].as_str().unwrap_or_default().into(),
                description: String::new(),
                version,
                updated,
                downloads: r["_nViewCount"].as_u64().unwrap_or(0),
                rating: r["_nLikeCount"].as_u64().unwrap_or(0),
                icon: image.and_then(|i| Some(format!("{}/{}", i["_sBaseUrl"].as_str()?, i["_sFile220"].as_str().or(i["_sFile"].as_str())?))),
                url: r["_sProfileUrl"].as_str().map_or_else(|| format!("https://gamebanana.com/mods/{id}"), str::to_string),
                nsfw: r["_bHasContentRatings"].as_bool().unwrap_or(false),
                dependencies: vec![],
                files: vec![],
            })
        })
        .collect()
}

/// A GameBanana mod's files; a mod can have several to choose from.
pub async fn files(package: &Package) -> Result<Vec<ModFile>, String> {
    if !package.files.is_empty() {
        return Ok(package.files.clone());
    }
    let id = package.key.strip_prefix("gamebanana:").ok_or("That mod has no files.")?;
    let detail = get_json(&format!("{}/apiv11/Mod/{id}?_csvProperties=_aFiles", gamebanana_base())).await?;
    Ok(detail["_aFiles"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|f| {
            Some(ModFile {
                id: f["_idRow"].as_u64()?.to_string(),
                name: f["_sFile"].as_str()?.to_string(),
                url: f["_sDownloadUrl"].as_str()?.to_string(),
                size: f["_nFilesize"].as_u64().unwrap_or(0),
                description: f["_sDescription"].as_str().unwrap_or_default().to_string(),
            })
        })
        .collect())
}

/// The mods folder inside an app's folder; never outside it.
pub fn folder(app: &Path, config: &Config) -> Result<PathBuf, String> {
    let path = Path::new(config.path.trim());
    if path.as_os_str().is_empty() || !path.components().all(|c| matches!(c, Component::Normal(_))) {
        return Err("This app's mods folder isn't inside its folder.".into());
    }
    Ok(app.join(path))
}

pub fn installed(app: &Path) -> Vec<Installed> {
    fs::read(app.join(RECORD)).ok().and_then(|b| serde_json::from_slice::<Record>(&b).ok()).unwrap_or_default().mods
}

fn save(app: &Path, mods: Vec<Installed>) -> Result<(), String> {
    let temp = app.join(format!("{RECORD}.tmp"));
    fs::write(&temp, serde_json::to_vec_pretty(&Record { mods }).map_err(|e| e.to_string())?)
        .and_then(|_| fs::rename(&temp, app.join(RECORD)))
        .map_err(|e| format!("Couldn't save the list of mods: {e}"))
}

/// A folder name from a mod's name.
fn folder_name(name: &str) -> String {
    let clean: String = name.chars().map(|c| if c.is_alphanumeric() || " -_.()".contains(c) { c } else { '_' }).collect();
    let clean = clean.trim().trim_end_matches('.').to_string();
    if clean.is_empty() { "Mod".into() } else { clean }
}

/// Files Thunderstore puts in every package for itself, not for the game.
const THUNDERSTORE_FILES: [&str; 4] = ["manifest.json", "icon.png", "readme.md", "changelog.md"];

/// Installs (or updates) a mod from its downloaded file: unpacks it, puts its
/// files in the mods folder, removes what an earlier version left that this
/// one doesn't have, and records it.
pub fn install(app: &Path, config: &Config, package: &Package, download: &Path, name: &str) -> Result<Installed, String> {
    let mods = folder(app, config)?;
    let staging = app.join(".quiver-mod-staging");
    let _ = fs::remove_dir_all(&staging);
    let result = (|| {
        crate::install::extract(download, name, &staging).map_err(|e| format!("Couldn't unpack {name}: {e}"))?;
        crate::install::flatten(&staging).map_err(|e| e.to_string())?;
        if package.provider == "thunderstore" {
            for entry in fs::read_dir(&staging).map_err(|e| e.to_string())?.flatten() {
                if THUNDERSTORE_FILES.contains(&entry.file_name().to_string_lossy().to_lowercase().as_str()) {
                    let _ = fs::remove_file(entry.path());
                }
            }
        }
        let base = if config.layout.as_deref() == Some("folderPerMod") { PathBuf::from(folder_name(&package.name)) } else { PathBuf::new() };
        let mut files = Vec::new();
        collect(&staging, &staging, &mut files).map_err(|e| e.to_string())?;
        if files.is_empty() {
            return Err(format!("{name} has nothing in it to install."));
        }
        let mut record = installed(app);
        let before = record.iter().position(|m| m.key == package.key).map(|i| record.remove(i));
        let mut placed = Vec::new();
        for file in &files {
            let relative = base.join(file);
            let to = mods.join(&relative);
            fs::create_dir_all(to.parent().unwrap()).map_err(|e| e.to_string())?;
            crate::install::writable(&to);
            fs::copy(staging.join(file), &to).map_err(|e| format!("Couldn't put {} in the mods folder: {e}", relative.display()))?;
            // A copy keeps a read-only flag the mod's archive gave it.
            crate::install::writable(&to);
            placed.push(relative.to_string_lossy().replace('\\', "/"));
        }
        // What the old version had and this one doesn't.
        if let Some(before) = before {
            for old in before.files.iter().filter(|f| !placed.contains(f)) {
                remove(&mods, old);
            }
        }
        let mod_ = Installed { key: package.key.clone(), name: package.name.clone(), version: package.version.clone(), files: placed, file: name.to_string() };
        record.push(mod_.clone());
        save(app, record)?;
        Ok(mod_)
    })();
    let _ = fs::remove_dir_all(&staging);
    result
}

/// Downloads a mod's file, then installs it from there.
pub async fn download_and_install(app: &Path, config: &Config, package: &Package, file: &ModFile) -> Result<Installed, String> {
    let name = crate::install::plain_name(&file.name)?.to_string();
    let dir = app.join(".quiver-mod-download");
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let path = dir.join(&name);
    let result = async {
        let failed = |e: reqwest::Error| format!("Couldn't download {name}: {e}");
        let mut response = client()?.get(&file.url).send().await.and_then(|r| r.error_for_status()).map_err(failed)?;
        let mut out = fs::File::create(&path).map_err(|e| e.to_string())?;
        while let Some(chunk) = response.chunk().await.map_err(failed)? {
            std::io::Write::write_all(&mut out, &chunk).map_err(|e| e.to_string())?;
        }
        drop(out);
        install(app, config, package, &path, &name)
    }
    .await;
    let _ = fs::remove_dir_all(&dir);
    result
}

fn collect(root: &Path, dir: &Path, out: &mut Vec<PathBuf>) -> std::io::Result<()> {
    for entry in fs::read_dir(dir)?.flatten() {
        let path = entry.path();
        if entry.file_type()?.is_dir() {
            collect(root, &path, out)?;
        } else {
            out.push(path.strip_prefix(root).unwrap().to_path_buf());
        }
    }
    Ok(())
}

/// Removes one recorded file, and the folders it leaves empty, never above the mods folder.
fn remove(mods: &Path, relative: &str) {
    let path = Path::new(relative);
    if !path.components().all(|c| matches!(c, Component::Normal(_))) {
        return;
    }
    crate::install::writable(&mods.join(path));
    let _ = fs::remove_file(mods.join(path));
    let mut parent = path.parent();
    while let Some(dir) = parent.filter(|d| !d.as_os_str().is_empty()) {
        if fs::remove_dir(mods.join(dir)).is_err() {
            break;
        }
        parent = dir.parent();
    }
}

pub fn uninstall(app: &Path, config: &Config, key: &str) -> Result<(), String> {
    let mods = folder(app, config)?;
    let mut record = installed(app);
    if let Some(i) = record.iter().position(|m| m.key == key) {
        for file in &record.remove(i).files {
            remove(&mods, file);
        }
        save(app, record)?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn reads_sources_as_the_catalog_writes_them() {
        assert_eq!(community("https://thunderstore.io/c/banjo-recompiled/").as_deref(), Some("banjo-recompiled"));
        assert_eq!(community("banjo-recompiled").as_deref(), Some("banjo-recompiled"));
        assert_eq!(game("https://gamebanana.com/games/20371").as_deref(), Some("20371"));
        assert_eq!(game("https://gamebanana.com/mods/games/20371/").as_deref(), Some("20371"));
        assert_eq!(game("https://gamebanana.com/games/abc"), None);
        assert_eq!(millis("1970-01-02T00:00:01Z"), 86_401_000);
    }

    #[test]
    fn lists_thunderstore_packages_with_their_dependencies() {
        let list = json!([
            { "name": "Big_Head", "full_name": "Alice-Big_Head", "owner": "Alice", "package_url": "u", "date_updated": "2026-01-01T00:00:00Z", "rating_score": 3, "is_deprecated": false, "has_nsfw_content": false,
              "versions": [{ "version_number": "1.1.0", "description": "Big heads", "icon": "i", "download_url": "d", "downloads": 5, "file_size": 10,
                             "dependencies": ["Alice-Core-1.0.0", "Elsewhere-Pack-2.0.0"] }, { "version_number": "1.0.0", "downloads": 2 }] },
            { "name": "Core", "full_name": "Alice-Core", "owner": "Alice", "versions": [{ "version_number": "1.0.0", "dependencies": [] }] },
            { "name": "Old", "full_name": "Bob-Old", "is_deprecated": true, "versions": [{ "version_number": "1.0.0" }] }
        ]);
        let packages = thunderstore(&list);
        assert_eq!(packages.len(), 2);
        let big = &packages[0];
        assert_eq!((big.key.as_str(), big.name.as_str(), big.version.as_str(), big.downloads), ("thunderstore:Alice-Big_Head", "Big Head", "1.1.0", 7));
        assert_eq!(big.dependencies, ["thunderstore:Alice-Core"]);
        assert_eq!(big.files[0].name, "Alice-Big_Head-1.1.0.zip");
    }

    #[test]
    fn lists_gamebanana_mods() {
        let list = json!({ "_aRecords": [
            { "_idRow": 7, "_sName": "Bushes", "_aSubmitter": { "_sName": "Dana" }, "_tsDateModified": 100, "_sVersion": "", "_nLikeCount": 3, "_nViewCount": 50, "_bHasFiles": true, "_sProfileUrl": "https://gamebanana.com/mods/7",
              "_aPreviewMedia": { "_aImages": [{ "_sBaseUrl": "https://images", "_sFile220": "a.jpg" }] } },
            { "_idRow": 8, "_sName": "No files", "_bHasFiles": false }
        ]});
        let packages = gamebanana(&list);
        assert_eq!(packages.len(), 1);
        assert_eq!((packages[0].key.as_str(), packages[0].version.as_str(), packages[0].icon.as_deref()), ("gamebanana:7", "100000", Some("https://images/a.jpg")));
    }

    fn package(key: &str, provider: &str, version: &str) -> Package {
        Package { key: key.into(), provider: provider.into(), name: "Big Head".into(), author: String::new(), description: String::new(), version: version.into(), updated: 0, downloads: 0, rating: 0, icon: None, url: String::new(), nsfw: false, dependencies: vec![], files: vec![] }
    }

    fn zip_of(dir: &Path, files: &[(&str, &str)]) -> PathBuf {
        let path = dir.join(format!("mod-{}.zip", files.len()));
        let mut zip = zip::ZipWriter::new(fs::File::create(&path).unwrap());
        for (name, body) in files {
            zip.start_file(*name, zip::write::SimpleFileOptions::default()).unwrap();
            std::io::Write::write_all(&mut zip, body.as_bytes()).unwrap();
        }
        zip.finish().unwrap();
        path
    }

    #[test]
    fn installs_updates_and_uninstalls_only_its_own_files() {
        let root = tempfile::tempdir().unwrap();
        let app = root.path().join("app");
        fs::create_dir_all(app.join("mods")).unwrap();
        fs::write(app.join("mods/players-own.nrm"), "mine").unwrap();
        let config = Config { path: "mods".into(), layout: None };
        let v1 = zip_of(root.path(), &[("manifest.json", "{}"), ("icon.png", ""), ("big_head.nrm", "v1"), ("textures/head.png", "t")]);
        let first = install(&app, &config, &package("thunderstore:Alice-Big_Head", "thunderstore", "1.0.0"), &v1, "mod.zip").unwrap();
        assert_eq!(first.files, ["big_head.nrm", "textures/head.png"]);
        assert!(!app.join("mods/manifest.json").exists(), "Thunderstore's own files stay out");
        // The update drops a file the new version doesn't have.
        let v2 = zip_of(root.path(), &[("big_head.nrm", "v2")]);
        install(&app, &config, &package("thunderstore:Alice-Big_Head", "thunderstore", "1.1.0"), &v2, "mod.zip").unwrap();
        assert_eq!(fs::read_to_string(app.join("mods/big_head.nrm")).unwrap(), "v2");
        assert!(!app.join("mods/textures").exists());
        assert_eq!(installed_versions(&app), ["1.1.0"]);
        uninstall(&app, &config, "thunderstore:Alice-Big_Head").unwrap();
        assert!(!app.join("mods/big_head.nrm").exists());
        assert_eq!(fs::read_to_string(app.join("mods/players-own.nrm")).unwrap(), "mine");
        assert!(installed(&app).is_empty());
    }

    fn installed_versions(app: &Path) -> Vec<String> {
        installed(app).into_iter().map(|m| m.version).collect()
    }

    #[test]
    fn a_folder_per_mod_and_never_outside_the_app() {
        let root = tempfile::tempdir().unwrap();
        let app = root.path().join("app");
        fs::create_dir_all(&app).unwrap();
        let file = zip_of(root.path(), &[("Wrapper/a.pak", "a")]);
        let config = Config { path: "Mods/Paks".into(), layout: Some("folderPerMod".into()) };
        install(&app, &config, &package("gamebanana:7", "gamebanana", "1"), &file, "m.zip").unwrap();
        assert_eq!(fs::read_to_string(app.join("Mods/Paks/Big Head/a.pak")).unwrap(), "a");
        assert!(folder(&app, &Config { path: "../elsewhere".into(), layout: None }).is_err());
        assert!(folder(&app, &Config { path: "/abs".into(), layout: None }).is_err());
    }
}
