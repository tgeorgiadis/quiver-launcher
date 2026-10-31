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
}

/// A 3.x library: the folder holding its apps.json, and its apps.
#[derive(Serialize)]
pub struct Library {
    folder: String,
    apps: Vec<OldApp>,
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

/// A field by name in any letter case (3.x wrote both camelCase and PascalCase).
fn field<'a>(v: &'a Value, name: &str) -> Option<&'a str> {
    v.as_object()?.iter().find(|(k, _)| k.eq_ignore_ascii_case(name))?.1.as_str().filter(|s| !s.trim().is_empty())
}

fn read(path: &Path) -> Option<Value> {
    serde_json::from_slice(&std::fs::read(path).ok()?).ok()
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
        .filter_map(|d| Some((saved(&d), library_in(&d, None)?)))
        .max_by_key(|(saved, library)| (library.apps.iter().filter(|a| a.dir.is_some()).count(), *saved))
        .map(|(_, library)| library)
}

/// The apps in a 3.x library folder: the one holding its apps.json, a folder
/// holding that one (a portable Windows copy's folder around `current`, or
/// the folder of its .app), or the folder around it (the player picked a
/// portable Windows copy's `current` folder). None when it isn't one.
/// `apps` is its Apps folder, when the player says where it is: one moved
/// by hand, or on a drive that has another letter now, where 3.x's settings
/// don't point any more.
pub fn find_in(dir: &Path, apps: Option<&Path>) -> Option<Library> {
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
fn library_in(dir: &Path, apps: Option<&Path>) -> Option<Library> {
    let apps = read_library(dir, apps);
    (!apps.is_empty()).then(|| Library { folder: dir.to_string_lossy().into(), apps })
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

/// The apps in a folder's library file, if it's a 3.x one: each has a name,
/// and at least one has a repository.
fn read_library(dir: &Path, apps: Option<&Path>) -> Vec<OldApp> {
    let Some(doc) = library_file(dir).and_then(|f| read(&f)) else { return vec![] };
    // Set when 3.x moved its data to ~/.local/share, to where its Apps folder stayed.
    let apps_dir = apps.map(Path::to_path_buf).unwrap_or_else(|| {
        read(&dir.join("settings.json"))
            .and_then(|s| field(&s, "appsPath").map(|p| resolve(dir, p)))
            .unwrap_or_else(|| dir.join("Apps"))
    });
    let list = doc.as_object().and_then(|o| o.iter().find(|(k, _)| k.eq_ignore_ascii_case("apps"))).map(|(_, v)| v);
    let apps = list
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(|app| {
            let name = field(app, "name")?.to_string();
            let folder = field(app, "installPath")
                .map(|p| resolve(dir, p))
                .or_else(|| field(app, "folderName").map(|f| apps_dir.join(f)));
            let install = folder.and_then(|d| installed(&d).map(|(version, program)| (d, version, program)));
            let (dir, version, program) = match install {
                Some((d, version, program)) => (Some(d.to_string_lossy().into()), Some(version), program),
                None => (None, None, None),
            };
            Some(OldApp {
                name,
                repository: field(app, "repository").map(str::to_string),
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
        find_in(dir, None).map(|l| l.apps).unwrap_or_default()
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
            let Library { folder, apps } = find_in(dir, None).unwrap();
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
        let apps = find_in(&data, Some(&moved)).unwrap().apps;
        assert_eq!(at(&apps[0]), Some(moved.join("TestPort").as_path()));
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
    fn another_apps_json_isnt_a_library() {
        let root = tempfile::tempdir().unwrap();
        library(root.path(), r#"{"apps":[{"name":"Something"}]}"#);
        assert!(apps_in(root.path()).is_empty());
        library(root.path(), r#"[1, 2, 3]"#);
        assert!(apps_in(root.path()).is_empty());
    }
}
