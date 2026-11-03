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
    /// The catalog entry 3.x knew it as (its catalogEntryId), which finds it in the catalog when its repository moved.
    catalog_id: Option<String>,
    /// The installed copy, when there is one.
    dir: Option<String>,
    /// Its version, whenever there's an installed copy: UNKNOWN_VERSION when 3.x didn't know it.
    version: Option<String>,
    /// The program a Windows installer (MSI) put on the computer, for an app 3.x installed that way.
    program: Option<String>,
    /// Its tags as 3.x showed them: the player's, and the catalog's among them.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    tags: Vec<String>,
    /// The catalog's tags when 3.x last followed its entry, which tells the player's own apart.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    catalog_tags: Vec<String>,
    /// The player hid it in 3.x's library.
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    hidden: bool,
    /// 3.x says it's installed in a folder that's gone, and it wasn't found anywhere else.
    #[serde(skip)]
    lost: bool,
}

/// A 3.x library: the folder holding its apps.json, its apps, and the player's tag filters.
#[derive(Serialize)]
pub struct Library {
    folder: String,
    apps: Vec<OldApp>,
    #[serde(skip_serializing_if = "Vec::is_empty")]
    filters: Vec<TagFilter>,
    /// A folder 3.x's files name for its installs is gone, so some have likely moved. An app that simply isn't found
    /// says nothing: most never were installed, as a library lists apps the player only added.
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    moved: bool,
}

/// A tag filter the player made in 3.x's library (its TagDisplayFilter): the
/// apps with any of its tags, or all of them, less those with any (or all) of
/// the tags it leaves out.
#[derive(Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct TagFilter {
    name: String,
    tags: Vec<String>,
    all: bool,
    exclude: Vec<String>,
    exclude_all: bool,
}

/// The version 3.x gave an install it couldn't tell the version of
/// (GameStatusService's DefaultInstalledVersion). It has no release date, so
/// any verified release is an update from it.
const UNKNOWN_VERSION: &str = "0.0.0";

/// Where 3.x keeps apps.json: its installer's folder, or the XDG / Application
/// Support fallback; then where a portable copy usually is, since a portable
/// 3.x keeps its library beside itself (the folder of its AppImage, its .app or
/// its unzipped Windows folder): the home folder, Downloads, Desktop,
/// Applications, Games, Documents, and each folder in them.
fn data_dirs() -> Vec<PathBuf> {
    let env = |k: &str| std::env::var_os(k).map(PathBuf::from);
    let home = env("HOME").or_else(|| env("USERPROFILE"));
    let known = [
        env("QUIVER_V3_DATA"),
        env("LOCALAPPDATA").map(|d| d.join("QuiverLauncher")),
        env("XDG_DATA_HOME").map(|d| d.join("QuiverLauncher")),
        // A Flatpak's own XDG_DATA_HOME is in ~/.var/app; this is the system's.
        env("HOST_XDG_DATA_HOME").map(|d| d.join("QuiverLauncher")),
        home.as_ref().map(|h| h.join(".local/share/QuiverLauncher")),
        home.as_ref().map(|h| h.join("Library/Application Support/QuiverLauncher")),
    ];
    let portable = home.iter().flat_map(|h| {
        ["", "Downloads", "Desktop", "Applications", "Games", "Documents", "Apps", "AppImages"].map(|d| h.join(d))
    });
    let nearby = portable.flat_map(|d| {
        let children = std::fs::read_dir(&d).into_iter().flatten().flatten().map(|e| e.path()).filter(|p| p.is_dir());
        // A folder of thousands (Downloads) is only looked through so far.
        std::iter::once(d.clone()).chain(children.take(200))
    });
    known.into_iter().flatten().chain(nearby).collect()
}

/// A value by name in any letter case (3.x wrote both camelCase and PascalCase).
fn get<'a>(v: &'a Value, name: &str) -> Option<&'a Value> {
    v.as_object()?.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v)
}

/// A text field by name, when it says something.
fn field<'a>(v: &'a Value, name: &str) -> Option<&'a str> {
    get(v, name)?.as_str().filter(|s| !s.trim().is_empty())
}

/// A list of texts, trimmed, without the empty ones.
fn texts(v: Option<&Value>) -> Vec<String> {
    let list = v.and_then(Value::as_array).into_iter().flatten();
    list.filter_map(Value::as_str).map(str::trim).filter(|s| !s.is_empty()).map(str::to_string).collect()
}

/// A JSON file, with or without the byte order mark 3.x can start its settings.json with.
fn read(path: &Path) -> Option<Value> {
    let bytes = std::fs::read(path).ok()?;
    serde_json::from_slice(bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(&bytes)).ok()
}

/// The library file in a folder: apps.json, or games.json from before 3.x renamed it.
fn library_file(dir: &Path) -> Option<PathBuf> {
    ["apps.json", "games.json"].iter().map(|f| dir.join(f)).find(|f| f.is_file())
}

/// The 3.x library in the usual places. One beside this launcher comes first:
/// the player put it there, beside the copy they're coming from.
pub fn find() -> Option<Library> {
    let program = std::env::var_os("APPIMAGE").map(PathBuf::from).or_else(|| std::env::current_exe().ok());
    program.and_then(|p| best(around(&p))).or_else(|| best(data_dirs()))
}

/// The folders a 3.x copy beside this program keeps its library in: the
/// folder of the exe, the AppImage or the .app (3.x keeps its own beside
/// itself, as a Windows copy's beside its `current` folder), and the folder
/// around that, since Windows' Extract All unzips into a folder of the zip's name.
fn around(program: &Path) -> Vec<PathBuf> {
    // A Mac's program is inside its .app; an AppImage's is the AppImage.
    let program = program.ancestors().find(|a| a.extension().is_some_and(|e| e.eq_ignore_ascii_case("app"))).unwrap_or(program);
    program.ancestors().skip(1).take(2).map(Path::to_path_buf).collect()
}

/// The library among these folders' to bring over. A computer can have more
/// than one, such as a portable copy's beside an installed one's, or one left
/// behind: the one with the most installed apps, then the one saved last, as
/// 3.x itself goes by its newest apps.json (UserDataRescue).
fn best(dirs: impl IntoIterator<Item = PathBuf>) -> Option<Library> {
    let saved = |d: &Path| library_file(d).and_then(|f| f.metadata().and_then(|m| m.modified()).ok());
    dirs.into_iter()
        .filter_map(|d| Some((saved(&d), library_in(&d, &[])?)))
        .max_by_key(|(saved, library)| (library.apps.iter().filter(|a| a.dir.is_some()).count(), *saved))
        .map(|(_, library)| library)
}

/// The apps in a 3.x library folder: the one holding its apps.json, a folder
/// holding that one (a portable Windows copy's folder around `current`, or
/// the folder of its .app), or the folder around it (the player picked a
/// portable Windows copy's `current` folder). None when it isn't one.
/// `apps` are Apps folders the player says its installs are in: one moved by
/// hand, or on a drive that has another letter now, where 3.x's settings don't
/// point any more.
pub fn find_in(dir: &Path, apps: &[PathBuf]) -> Option<Library> {
    if let Some(library) = library_in(dir, apps) {
        return Some(library);
    }
    std::fs::read_dir(dir)
        .into_iter()
        .flatten()
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.is_dir())
        .take(200)
        .chain(dir.parent().map(Path::to_path_buf))
        .find_map(|p| library_in(&p, apps))
}

/// The library in this very folder, if it holds one.
fn library_in(dir: &Path, apps: &[PathBuf]) -> Option<Library> {
    let settings = read(&dir.join("settings.json")).unwrap_or(Value::Null);
    let apps = read_library(dir, apps, &settings);
    let moved = field(&settings, "appsPath").is_some_and(|p| !resolve(dir, p).is_dir()) || apps.iter().any(|a| a.lost);
    (!apps.is_empty()).then(|| Library { folder: dir.to_string_lossy().into(), apps, filters: tag_filters(&settings), moved })
}

/// The tag filters in 3.x's settings, leaving out any that filter nothing.
fn tag_filters(settings: &Value) -> Vec<TagFilter> {
    // 3.x saved how a filter matches as its number, 1 for All.
    let all = |v: Option<&Value>| v.is_some_and(|v| v.as_u64() == Some(1) || v.as_str().is_some_and(|s| s.eq_ignore_ascii_case("all")));
    let list = get(settings, "tagDisplayFilters").and_then(Value::as_array).into_iter().flatten();
    list.filter_map(|f| {
        let filter = TagFilter {
            name: field(f, "name")?.trim().to_string(),
            tags: texts(get(f, "tags")),
            all: all(get(f, "matchMode")),
            exclude: texts(get(f, "excludeTags")),
            exclude_all: all(get(f, "excludeMatchMode")),
        };
        (!filter.tags.is_empty() || !filter.exclude.is_empty()).then_some(filter)
    })
    .collect()
}

/// A folder 3.x's settings or an app named, which can be relative to where 3.x
/// ran: its data folder or, for a Windows copy, the `current` folder in it.
fn resolve(dir: &Path, path: &str) -> PathBuf {
    let path = Path::new(path.trim());
    if path.is_absolute() {
        return path.to_path_buf();
    }
    [dir.join(path), dir.join("current").join(path)].into_iter().find(|p| p.is_dir()).unwrap_or_else(|| dir.join(path))
}

/// The files 3.x keeps in an app's folder about the app, which aren't the app.
const BOOKKEEPING: [&str; 7] = [
    "version.txt",
    "installed-build.txt",
    "android-package.txt",
    "quiver-launch.log",
    "Place app files here.txt",
    ".quiver-mods.json",
    crate::msi::V3_FILE,
];

/// Whether an app's folder holds the app: a file, anywhere in it, that isn't
/// 3.x's bookkeeping. Never one 3.x was still installing into.
fn has_app(dir: &Path) -> bool {
    fn any_file(dir: &Path, depth: usize) -> bool {
        std::fs::read_dir(dir).into_iter().flatten().flatten().take(1000).any(|e| match e.file_type() {
            Ok(kind) if kind.is_dir() => depth < 8 && any_file(&e.path(), depth + 1),
            Ok(_) => depth > 0 || !BOOKKEEPING.iter().any(|b| e.file_name().eq_ignore_ascii_case(b)),
            Err(_) => false,
        })
    }
    !dir.join("install-incomplete.txt").exists() && any_file(dir, 0)
}

/// An install in an app's folder: its version, and the program a Windows
/// installer (MSI) put on the computer. An MSI install keeps both in its
/// receipt; any other is installed when its folder holds the app, as 3.x
/// counts it (GameStatusService), version.txt or not, since 3.x writes that
/// only once it has looked. A Flatpak 3.x installed doesn't start from its
/// folder, so it comes over to install again.
fn installed(dir: &Path) -> Option<(String, Option<String>)> {
    if dir.join(crate::msi::V3_FILE).is_file() {
        let (program, tag) = crate::msi::v3_program(dir)?;
        return Some((tag.unwrap_or_else(|| UNKNOWN_VERSION.into()), Some(program)));
    }
    if dir.join("flatpak-install.json").exists() || !has_app(dir) {
        return None;
    }
    let version = std::fs::read_to_string(dir.join("version.txt"))
        .ok()
        .map(|v| v.trim().to_string())
        // 3.x's own words for not knowing.
        .filter(|v| !v.is_empty() && !v.eq_ignore_ascii_case("unknown"))
        .unwrap_or_else(|| UNKNOWN_VERSION.into());
    Some((version, None))
}

/// What's installed in a folder the player says holds an app.
#[derive(Serialize)]
pub struct Existing {
    version: String,
    program: Option<String>,
}

/// The install in a folder the player points at, as 3.x's Locate existing
/// install took one; none when it doesn't hold an app. One Quiver installed
/// and the player moved has its version in Quiver's own file.
pub fn existing(dir: &Path) -> Option<Existing> {
    let (version, program) = installed(dir)?;
    let own = std::fs::read_to_string(dir.join(crate::install::VERSION_FILE)).ok().map(|v| v.trim().to_string()).filter(|v| !v.is_empty());
    Some(Existing { version: own.unwrap_or(version), program })
}

/// The apps in a folder's library file, if it's a 3.x one: each has a name,
/// and at least one has a repository.
fn read_library(dir: &Path, apps: &[PathBuf], settings: &Value) -> Vec<OldApp> {
    let Some(doc) = library_file(dir).and_then(|f| read(&f)) else { return vec![] };
    // Where an app's folder can be: those the player named, the one 3.x was set to, and its default, since changing
    // the setting in 3.x (or moving its data to ~/.local/share) leaves apps already installed where they were.
    let folders: Vec<PathBuf> = apps.iter().cloned().chain(field(settings, "appsPath").map(|p| resolve(dir, p))).chain([dir.join("Apps")]).collect();
    // 3.x kept the apps the player hid in its settings, and the tags they gave a catalog app.
    let hidden_keys = texts(get(settings, "manuallyHiddenApps"));
    let set_tags = get(settings, "userAppTags");
    let list = doc.as_object().and_then(|o| o.iter().find(|(k, _)| k.eq_ignore_ascii_case("apps"))).map(|(_, v)| v);
    let apps = list
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(|app| {
            let name = field(app, "name")?.to_string();
            let own = field(app, "installPath").map(|p| resolve(dir, p));
            let named = field(app, "folderName").into_iter().flat_map(|f| folders.iter().map(move |d| d.join(f)));
            let gone = own.as_ref().is_some_and(|d| !d.is_dir());
            let install = own.into_iter().chain(named).find_map(|d| installed(&d).map(|(version, program)| (d, version, program)));
            let lost = gone && install.is_none();
            let (dir, version, program) = match install {
                Some((d, version, program)) => (Some(d.to_string_lossy().into()), Some(version), program),
                None => (None, None, None),
            };
            let repository = field(app, "repository");
            // How 3.x knew a hidden app (GameManager.GetHiddenGameKey): by its folder, else its repository, else
            // its name; or by its name alone.
            let key = match (field(app, "folderName"), repository) {
                (Some(f), _) => format!("folder:{f}"),
                (None, Some(r)) => format!("repo:{r}"),
                (None, None) => format!("name:{name}"),
            };
            let hidden = hidden_keys.iter().any(|h| *h == key || *h == name);
            // Tags set in 3.x's settings stand in for the app's own (AppCatalogService.ApplyUserAppTags).
            let set = repository.and_then(|r| set_tags.and_then(|t| get(t, r)));
            Some(OldApp {
                tags: texts(set.or_else(|| get(app, "tags"))),
                catalog_tags: texts(get(app, "catalog").and_then(|c| get(c, "tags"))),
                hidden,
                lost,
                name,
                repository: repository.map(str::to_string),
                provider: field(app, "repositorySource").unwrap_or("github").to_lowercase(),
                catalog_id: field(app, "catalogEntryId").map(str::to_string),
                dir,
                version,
                program,
            })
        })
        .collect::<Vec<_>>();
    if apps.iter().any(|a| a.repository.is_some()) { apps } else { vec![] }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn library(dir: &Path, apps: &str) {
        std::fs::create_dir_all(dir).unwrap();
        std::fs::write(dir.join("apps.json"), apps).unwrap();
    }

    /// An app's folder with these files in it.
    fn folder(dir: &Path, files: &[(&str, &str)]) {
        std::fs::create_dir_all(dir).unwrap();
        for (name, text) in files {
            let path = dir.join(name);
            std::fs::create_dir_all(path.parent().unwrap()).unwrap();
            std::fs::write(path, text).unwrap();
        }
    }

    /// The apps of the library find_in finds from a folder.
    fn apps_in(dir: &Path) -> Vec<OldApp> {
        find_in(dir, &[]).map(|l| l.apps).unwrap_or_default()
    }

    /// Where an app's install came over from.
    fn at(app: &OldApp) -> Option<&Path> {
        app.dir.as_deref().map(Path::new)
    }

    const PORT: &str = r#"{"Apps":[{"Name":"Test Port","Repository":"quiver/test-port","FolderName":"TestPort"}]}"#;

    #[test]
    fn finds_a_library_in_a_folder_or_the_folders_around_it() {
        let root = tempfile::tempdir().unwrap();
        let portable = root.path().join("QuiverLauncher-win-Portable");
        library(&portable, PORT);
        folder(&portable.join("Apps/TestPort"), &[("version.txt", "v1.0.0\n"), ("port.exe", "")]);
        std::fs::create_dir_all(portable.join("current")).unwrap();
        // The player can pick the copy's own `current` folder, as well as the folder around the copy.
        for dir in [portable.as_path(), root.path(), &portable.join("current")] {
            let Library { folder, apps, .. } = find_in(dir, &[]).unwrap();
            // Where its apps.json is, whichever of them was picked: the folder picker opens there next time.
            assert_eq!(Path::new(&folder), portable);
            assert_eq!(apps.len(), 1);
            assert_eq!(apps[0].repository.as_deref(), Some("quiver/test-port"));
            assert_eq!(apps[0].version.as_deref(), Some("v1.0.0"));
        }
    }

    #[test]
    fn an_install_without_version_txt_comes_over_with_an_unknown_version() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), PORT);
        let installed = root.path().join("Apps/TestPort");
        folder(&installed, &[("bin/port.x86_64", "")]);
        let apps = apps_in(root.path());
        assert_eq!(at(&apps[0]), Some(installed.as_path()));
        assert_eq!(apps[0].version.as_deref(), Some(UNKNOWN_VERSION));
        // 3.x's word for it, too.
        folder(&installed, &[("version.txt", "Unknown")]);
        assert_eq!(apps_in(root.path())[0].version.as_deref(), Some(UNKNOWN_VERSION));
    }

    #[test]
    fn a_folder_without_the_app_isnt_an_install() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), PORT);
        let installed = root.path().join("Apps/TestPort");
        let none = |files: &[(&str, &str)]| {
            let _ = std::fs::remove_dir_all(&installed);
            folder(&installed, files);
            apps_in(root.path())[0].dir.is_none()
        };
        assert!(none(&[]), "an empty folder");
        assert!(none(&[("version.txt", "v1.0.0"), ("Place app files here.txt", "")]), "only 3.x's own files");
        assert!(none(&[("version.txt", "v1.0.0"), ("port.exe", ""), ("install-incomplete.txt", "")]), "an install cut short");
        assert!(none(&[("flatpak-install.json", "{}")]), "a Flatpak");
        assert!(!none(&[("version.txt", "v1.0.0"), ("port.exe", "")]));
    }

    #[test]
    fn follows_the_apps_folder_3x_was_set_to() {
        let root = tempfile::tempdir().unwrap();
        // Its data moved to ~/.local/share while its Apps folder stayed beside the AppImage, which 3.x saved as AppsPath.
        let data = root.path().join("home/.local/share/QuiverLauncher");
        let beside = root.path().join("home/Applications/Apps");
        library(&data, PORT);
        folder(&beside.join("TestPort"), &[("version.txt", "v1.0.0"), ("port.sh", "")]);
        std::fs::write(data.join("settings.json"), serde_json::json!({ "AppsPath": beside }).to_string()).unwrap();
        assert_eq!(at(&apps_in(&data)[0]), Some(beside.join("TestPort").as_path()));

        // A relative one: from the data folder, or the Windows copy's `current` folder that 3.x ran in.
        let windows = root.path().join("QuiverLauncher");
        library(&windows, PORT);
        folder(&windows.join("current/Games/TestPort"), &[("port.exe", "")]);
        std::fs::write(windows.join("settings.json"), r#"{"AppsPath":"Games"}"#).unwrap();
        assert_eq!(at(&apps_in(&windows)[0]), Some(windows.join("current/Games/TestPort").as_path()));
        folder(&windows.join("Games/TestPort"), &[("port.exe", "")]);
        assert_eq!(at(&apps_in(&windows)[0]), Some(windows.join("Games/TestPort").as_path()));
    }

    #[test]
    fn follows_an_apps_own_install_path() {
        let root = tempfile::tempdir().unwrap();
        let elsewhere = root.path().join("Games/Test Port");
        folder(&elsewhere, &[("port.exe", "")]);
        let apps = serde_json::json!({ "apps": [{ "name": "Test Port", "repository": "quiver/test-port", "folderName": "TestPort", "installPath": elsewhere }] });
        library(&root.path().join("data"), &apps.to_string());
        assert_eq!(at(&apps_in(&root.path().join("data"))[0]), Some(elsewhere.as_path()));
    }

    #[test]
    fn keeps_the_catalog_entry_3x_knew_an_app_as() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), r#"{"apps":[{"name":"Test Port","repository":"gone/test-port","catalogEntryId":"k179cb3m"}]}"#);
        assert_eq!(apps_in(root.path())[0].catalog_id.as_deref(), Some("k179cb3m"));
    }

    #[test]
    fn reads_a_library_from_before_apps_json() {
        let root = tempfile::tempdir().unwrap();
        std::fs::write(root.path().join("games.json"), PORT).unwrap();
        folder(&root.path().join("Apps/TestPort"), &[("port.exe", "")]);
        assert!(apps_in(root.path())[0].dir.is_some());
    }

    #[test]
    fn of_two_libraries_brings_over_the_one_with_installs() {
        let root = tempfile::tempdir().unwrap();
        let left = root.path().join("left-behind");
        let used = root.path().join("used");
        library(&left, PORT);
        library(&used, PORT);
        folder(&used.join("Apps/TestPort"), &[("port.exe", "")]);
        for dirs in [[left.clone(), used.clone()], [used.clone(), left.clone()]] {
            assert!(best(dirs).unwrap().apps[0].dir.is_some());
        }
    }

    #[test]
    fn finds_the_installs_in_an_apps_folder_the_player_points_at() {
        let root = tempfile::tempdir().unwrap();
        let data = root.path().join("data");
        library(&data, PORT);
        // 3.x's settings point at a drive that has another letter now.
        std::fs::write(data.join("settings.json"), r#"{"AppsPath":"Z:\Games"}"#).unwrap();
        let moved = root.path().join("Games");
        folder(&moved.join("TestPort"), &[("port.exe", "")]);
        assert!(apps_in(&data)[0].dir.is_none());
        let apps = find_in(&data, std::slice::from_ref(&moved)).unwrap().apps;
        assert_eq!(at(&apps[0]), Some(moved.join("TestPort").as_path()));
    }

    /// A library of two apps, with this in its settings.json as 3.x writes it: with a byte order mark.
    fn two_apps(data: &Path, settings: &Value) {
        library(data, r#"{"apps":[
            {"name":"One","repository":"quiver/one","folderName":"One"},
            {"name":"Two","repository":"quiver/two","folderName":"Two"}
        ]}"#);
        std::fs::write(data.join("settings.json"), format!("\u{FEFF}{settings}")).unwrap();
    }

    #[test]
    fn finds_apps_left_in_3xs_default_folder_when_its_setting_changed() {
        let root = tempfile::tempdir().unwrap();
        let data = root.path().join("data");
        let custom = root.path().join("Games");
        // 3.x doesn't move apps when the setting changes: One from before, Two from since.
        two_apps(&data, &serde_json::json!({ "AppsPath": custom }));
        folder(&data.join("Apps/One"), &[("one.exe", "")]);
        folder(&custom.join("Two"), &[("two.exe", "")]);
        let apps = apps_in(&data);
        assert_eq!(at(&apps[0]), Some(data.join("Apps/One").as_path()));
        assert_eq!(at(&apps[1]), Some(custom.join("Two").as_path()));
        // The folder it's set to comes first, as it does in 3.x.
        folder(&custom.join("One"), &[("one.exe", "")]);
        assert_eq!(at(&apps_in(&data)[0]), Some(custom.join("One").as_path()));
    }

    #[test]
    fn adds_each_apps_folder_the_player_points_at() {
        let root = tempfile::tempdir().unwrap();
        let data = root.path().join("data");
        two_apps(&data, &serde_json::json!({}));
        let (first, second) = (root.path().join("First"), root.path().join("Second"));
        folder(&first.join("One"), &[("one.exe", "")]);
        folder(&second.join("Two"), &[("two.exe", "")]);
        let apps = find_in(&data, &[first.clone(), second.clone()]).unwrap().apps;
        assert_eq!(at(&apps[0]), Some(first.join("One").as_path()));
        assert_eq!(at(&apps[1]), Some(second.join("Two").as_path()));
    }

    #[test]
    fn an_install_path_thats_gone_falls_back_to_the_apps_folder() {
        let root = tempfile::tempdir().unwrap();
        let apps = serde_json::json!({ "apps": [{ "name": "Test Port", "repository": "quiver/test-port", "folderName": "TestPort", "installPath": root.path().join("gone") }] });
        library(root.path(), &apps.to_string());
        folder(&root.path().join("Apps/TestPort"), &[("port.exe", "")]);
        assert_eq!(at(&apps_in(root.path())[0]), Some(root.path().join("Apps/TestPort").as_path()));
    }

    #[test]
    fn a_located_folder_has_to_hold_an_app() {
        let root = tempfile::tempdir().unwrap();
        assert!(existing(root.path()).is_none());
        folder(root.path(), &[("version.txt", "v1.0.0")]);
        assert!(existing(root.path()).is_none(), "only 3.x's own files");
        folder(root.path(), &[("port.exe", "")]);
        assert_eq!(existing(root.path()).unwrap().version, "v1.0.0");
    }

    #[test]
    fn says_a_library_moved_only_when_a_folder_it_names_is_gone() {
        let moved = |settings: &str, apps: &str| {
            let root = tempfile::tempdir().unwrap();
            library(root.path(), apps);
            std::fs::write(root.path().join("settings.json"), settings).unwrap();
            find_in(root.path(), &[]).unwrap().moved
        };
        // Apps it only lists, as most of a library's are.
        assert!(!moved("{}", PORT));
        assert!(!moved(r#"{"AppsPath":""}"#, PORT));
        assert!(moved(r#"{"AppsPath":"Z:\\Games"}"#, PORT));
        assert!(moved("{}", r#"{"Apps":[{"Name":"Test Port","Repository":"quiver/test-port","InstallPath":"Z:\\Games\\TestPort"}]}"#));
    }

    #[test]
    fn a_located_folder_quiver_installed_keeps_its_version() {
        let root = tempfile::tempdir().unwrap();
        folder(root.path(), &[("port.exe", ""), (crate::install::VERSION_FILE, "v2.0.0\n")]);
        assert_eq!(existing(root.path()).unwrap().version, "v2.0.0");
        // A folder the player fills has the file, empty.
        folder(root.path(), &[(crate::install::VERSION_FILE, "")]);
        assert_eq!(existing(root.path()).unwrap().version, UNKNOWN_VERSION);
    }

    #[test]
    fn finds_the_library_of_a_3x_copy_this_launcher_was_put_beside() {
        let root = tempfile::tempdir().unwrap();
        let copy = root.path().join("Quiver");
        library(&copy, PORT);
        folder(&copy.join("Apps/TestPort"), &[("port.exe", "")]);
        let found = |program: PathBuf| best(around(&program)).map(|l| PathBuf::from(l.folder));
        for program in [
            // Windows: the portable exe dropped into the 3.x folder, or unzipped there into a folder of its own.
            copy.join("QuiverLauncher.exe"),
            copy.join("QuiverLauncher-win-Portable/QuiverLauncher.exe"),
            // Linux: the AppImage beside the 3.x one.
            copy.join("QuiverLauncher_4.0.0_amd64.AppImage"),
            // macOS: the program inside the .app beside the 3.x one.
            copy.join("Quiver Launcher.app/Contents/MacOS/quiver-launcher"),
        ] {
            assert_eq!(found(program.clone()), Some(copy.clone()), "{}", program.display());
        }
        // Anywhere else, there's nothing beside it.
        assert_eq!(found(root.path().join("Elsewhere/QuiverLauncher.exe")), None);
    }

    #[test]
    fn brings_over_an_msi_install_with_its_program() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), r#"{"apps":[{"name":"Setup Game","repository":"quiver/setup-game","folderName":"SetupGame"}]}"#);
        let folder = root.path().join("Apps/SetupGame");
        let program = root.path().join("Program Files/Setup Game/Game.exe");
        std::fs::create_dir_all(&folder).unwrap();
        std::fs::create_dir_all(program.parent().unwrap()).unwrap();
        std::fs::write(&program, "").unwrap();
        let receipt = serde_json::json!({ "Phase": "Linked", "ReleaseTag": "v2.0", "ExecutablePath": program });
        std::fs::write(folder.join("windows-installer.json"), receipt.to_string()).unwrap();
        let apps = apps_in(root.path());
        assert_eq!(apps[0].version.as_deref(), Some("v2.0"));
        assert_eq!(apps[0].program.as_deref(), Some(program.to_string_lossy().as_ref()));
        // Without its version, as an install without version.txt.
        let receipt = serde_json::json!({ "Phase": "Linked", "ExecutablePath": program });
        std::fs::write(folder.join("windows-installer.json"), receipt.to_string()).unwrap();
        assert_eq!(apps_in(root.path())[0].version.as_deref(), Some(UNKNOWN_VERSION));
        // Its program gone: there's nothing to start, so it comes over to install again.
        std::fs::remove_file(&program).unwrap();
        assert_eq!(apps_in(root.path())[0].version, None);
        assert_eq!(apps_in(root.path())[0].dir, None);
    }

    #[test]
    fn brings_over_the_tags_and_what_the_catalog_set_of_them() {
        let root = tempfile::tempdir().unwrap();
        let apps = serde_json::json!({ "Apps": [
            { "Name": "Test Port", "Repository": "quiver/test-port", "Tags": ["n64", " favourite ", ""], "catalog": { "tags": ["n64"] } },
            { "Name": "Other Port", "Repository": "quiver/other-port", "Tags": ["n64"] },
        ] });
        library(root.path(), &apps.to_string());
        let apps = apps_in(root.path());
        assert_eq!(apps[0].tags, ["n64", "favourite"]);
        assert_eq!(apps[0].catalog_tags, ["n64"]);
        assert!(apps[1].catalog_tags.is_empty());
        // Tags the player set in 3.x's settings, by repository, are the ones it showed.
        let settings = serde_json::json!({ "UserAppTags": { "quiver/other-port": ["mine"] } });
        std::fs::write(root.path().join("settings.json"), settings.to_string()).unwrap();
        let apps = apps_in(root.path());
        assert_eq!(apps[0].tags, ["n64", "favourite"]);
        assert_eq!(apps[1].tags, ["mine"]);
    }

    #[test]
    fn brings_over_which_apps_the_player_hid() {
        let root = tempfile::tempdir().unwrap();
        let apps = serde_json::json!({ "apps": [
            { "name": "By Folder", "repository": "quiver/by-folder", "folderName": "ByFolder" },
            { "name": "By Repository", "repository": "quiver/by-repository" },
            { "name": "By Name", "repository": "quiver/by-name", "folderName": "ByName" },
            { "name": "Shown", "repository": "quiver/shown", "folderName": "Shown" },
            // Its folder is how 3.x knew it, not its repository.
            { "name": "Not By Repository", "repository": "quiver/not", "folderName": "NotByRepository" },
        ] });
        library(root.path(), &apps.to_string());
        let hidden = ["folder:ByFolder", "repo:quiver/by-repository", "By Name", "repo:quiver/not"];
        std::fs::write(root.path().join("settings.json"), serde_json::json!({ "ManuallyHiddenApps": hidden }).to_string()).unwrap();
        let hid: Vec<bool> = apps_in(root.path()).iter().map(|a| a.hidden).collect();
        assert_eq!(hid, [true, true, true, false, false]);
    }

    #[test]
    fn brings_over_the_players_tag_filters() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), PORT);
        let filters = serde_json::json!({ "TagDisplayFilters": [
            { "Id": "a", "Name": " Recomps ", "Tags": ["recomp", "n64"], "MatchMode": 0, "ExcludeTags": [], "ExcludeMatchMode": 0 },
            { "Id": "b", "Name": "Finished", "Tags": ["n64", "done"], "MatchMode": 1, "ExcludeTags": ["broken", "wip"], "ExcludeMatchMode": "All" },
            // One that filters nothing, and one without a name.
            { "Id": "c", "Name": "Everything", "Tags": [], "ExcludeTags": [] },
            { "Id": "d", "Name": "", "Tags": ["n64"] },
        ] });
        std::fs::write(root.path().join("settings.json"), filters.to_string()).unwrap();
        let filter = |name: &str, tags: &[&str], all, exclude: &[&str], exclude_all| TagFilter {
            name: name.into(),
            tags: tags.iter().map(|t| t.to_string()).collect(),
            all,
            exclude: exclude.iter().map(|t| t.to_string()).collect(),
            exclude_all,
        };
        assert_eq!(
            find_in(root.path(), &[]).unwrap().filters,
            [filter("Recomps", &["recomp", "n64"], false, &[], false), filter("Finished", &["n64", "done"], true, &["broken", "wip"], true)]
        );
    }

    #[test]
    fn reads_files_that_start_with_a_byte_order_mark() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), &format!("\u{FEFF}{PORT}"));
        let settings = serde_json::json!({ "ManuallyHiddenApps": ["folder:TestPort"] });
        std::fs::write(root.path().join("settings.json"), format!("\u{FEFF}{settings}")).unwrap();
        assert!(apps_in(root.path())[0].hidden);
    }

    #[test]
    fn another_apps_json_isnt_a_library() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), r#"{"apps":[{"name":"Something"}]}"#);
        assert!(apps_in(root.path()).is_empty());
        library(root.path(), r#"[1, 2, 3]"#);
        assert!(apps_in(root.path()).is_empty());
    }
}
