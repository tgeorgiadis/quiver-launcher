//! Releases that ship as a Windows Installer package (.msi). Windows owns
//! what it installs: the package runs in its own setup window, installing
//! wherever the player chooses, and the app's folder keeps only Quiver's
//! record of it (`FILE`, with the product's code) and the installer's log.
//! Play starts the program the setup installed; Uninstall asks Windows to
//! remove the product.
use std::path::{Path, PathBuf};
use std::process::Command;

/// Holds the installed product's code (`{GUID}`) in the app's folder; empty when the log didn't name it.
pub const FILE: &str = ".quiver-msi";
/// Quiver Launcher 3's record of an MSI install, in a folder adopted from it.
pub const V3_FILE: &str = "windows-installer.json";
const LOG: &str = "installer.log";
const ONLY_WINDOWS: &str = "This download is a Windows installer (MSI), which only runs on Windows.";
/// Windows Installer's exit codes.
const SUCCESS: i32 = 0;
const RESTART_NEEDED: i32 = 3010;
const USER_CANCELLED: i32 = 1602;
/// The Windows prompt to allow the install was declined.
const DECLINED: i32 = 1223;
const ANOTHER_RUNNING: i32 = 1618;
const NOT_INSTALLED: i32 = 1605;

/// Windows Installer runs one setup at a time, so Quiver's wait their turn.
static RUNNING: std::sync::Mutex<()> = std::sync::Mutex::new(());

/// What a setup put on the computer.
#[derive(Debug, PartialEq)]
pub struct Setup {
    /// The program that starts the app, when Quiver could tell.
    pub program: Option<PathBuf>,
    /// Where it was installed, to start looking from when the player chooses its program.
    pub location: Option<PathBuf>,
    /// Windows asked for a restart to finish.
    pub restart: bool,
}

pub fn is_package(name: &str) -> bool {
    name.to_ascii_lowercase().ends_with(".msi")
}

/// `msiexec`, or a stand-in (`QUIVER_MSIEXEC`) in tests.
fn msiexec() -> PathBuf {
    std::env::var_os("QUIVER_MSIEXEC").map(PathBuf::from).unwrap_or_else(|| {
        let windows = std::env::var_os("SystemRoot").map(PathBuf::from).unwrap_or_else(|| PathBuf::from(r"C:\Windows"));
        windows.join("System32").join("msiexec.exe")
    })
}

/// Runs a package's setup window and waits for it; `dir` is the app's folder,
/// `keep` the program the app started with before (kept when it's still there).
pub fn install(package: &Path, dir: &Path, preferred: &[String], keep: Option<&Path>) -> Result<Setup, String> {
    if !cfg!(windows) {
        return Err(ONLY_WINDOWS.into());
    }
    run(&msiexec(), package, dir, preferred, keep)
}

fn run(msiexec: &Path, package: &Path, dir: &Path, preferred: &[String], keep: Option<&Path>) -> Result<Setup, String> {
    let log = dir.join(LOG);
    let _ = std::fs::remove_file(&log);
    let code = {
        let _turn = RUNNING.lock().unwrap_or_else(|e| e.into_inner());
        // Its full setup window, as the developer made it: the player picks where it goes.
        let status = Command::new(msiexec)
            .arg("/i")
            .arg(package)
            .args(["/qf", "/norestart", "/L*V"])
            .arg(&log)
            .status()
            .map_err(|e| format!("Windows Installer couldn't start: {e}"))?;
        status.code().unwrap_or(-1)
    };
    let restart = outcome(code, &log)?;
    let found = parse_log(&read_log(&log));
    std::fs::write(dir.join(FILE), found.product_code.as_deref().unwrap_or_default()).map_err(|e| e.to_string())?;
    let program = keep.filter(|p| p.is_file()).map(Path::to_path_buf).or_else(|| program_in(&found.files, preferred));
    Ok(Setup { location: program.as_ref().and_then(|p| p.parent()).map(Path::to_path_buf).or_else(|| location(&found.files)), program, restart })
}

/// Whether a setup that ended with `code` needs a restart; an error when it didn't install.
fn outcome(code: i32, log: &Path) -> Result<bool, String> {
    match code {
        SUCCESS => Ok(false),
        RESTART_NEEDED => Ok(true),
        USER_CANCELLED | DECLINED => Err(crate::install::CANCELLED.into()),
        ANOTHER_RUNNING => Err("Another installation is running on this computer. Let it finish, then try again.".into()),
        code => Err(format!("Windows Installer stopped with error {code}, so it didn't install. Its log is {}.", log.display())),
    }
}

/// Asks Windows to uninstall the product an app's folder records, in
/// Windows' own window, which asks the player first.
pub fn uninstall(dir: &Path) -> Result<(), String> {
    let Some(code) = product_code(dir) else {
        // Quiver Launcher 3 didn't record which product it installed; Windows' list of apps has it.
        if cfg!(windows) {
            let _ = open::that_detached("ms-settings:appsfeatures");
        }
        return Err("Quiver doesn't know which Windows program this installed. Uninstall it in Windows Settings, under Apps > Installed apps, then remove it here.".into());
    };
    let _turn = RUNNING.lock().unwrap_or_else(|e| e.into_inner());
    let log = dir.join("uninstall.log");
    let status = Command::new(msiexec())
        .args(["/x", &code, "/norestart", "/L*V"])
        .arg(&log)
        .status()
        .map_err(|e| format!("Windows Installer couldn't start: {e}"))?;
    match status.code().unwrap_or(-1) {
        // Already gone counts too: the player may have uninstalled it in Windows.
        SUCCESS | RESTART_NEEDED | NOT_INSTALLED => Ok(()),
        code => outcome(code, &log).map(|_| ()),
    }
}

/// Whether an app's folder holds an MSI install, Quiver's or one adopted from 3.x.
pub fn installed(dir: &Path) -> bool {
    dir.join(FILE).is_file() || dir.join(V3_FILE).is_file()
}

/// The installed product's code: as recorded, else from the folder's installer logs (3.x kept one per run).
fn product_code(dir: &Path) -> Option<String> {
    let recorded = std::fs::read_to_string(dir.join(FILE)).ok().map(|s| s.trim().to_string()).filter(|s| !s.is_empty());
    recorded.or_else(|| {
        let mut logs: Vec<_> = std::fs::read_dir(dir).ok()?.flatten().map(|e| e.path()).filter(|p| p.extension().is_some_and(|e| e.eq_ignore_ascii_case("log"))).collect();
        // Newest name first: 3.x named them by when they ran.
        logs.sort();
        logs.iter().rev().find_map(|log| parse_log(&read_log(log)).product_code)
    })
    .filter(|code| is_product_code(code))
}

/// Whether it reads as a product's code, `{8-4-4-4-12 hex digits}`, and so
/// never as a package or one of msiexec's options.
fn is_product_code(code: &str) -> bool {
    let Some(guid) = code.strip_prefix('{').and_then(|c| c.strip_suffix('}')) else { return false };
    let parts: Vec<&str> = guid.split('-').collect();
    parts.iter().map(|p| p.len()).eq([8, 4, 4, 4, 12]) && parts.iter().all(|p| p.bytes().all(|b| b.is_ascii_hexdigit()))
}

/// The program a 3.x MSI install started, and the version it installed.
pub fn v3_program(dir: &Path) -> Option<(String, Option<String>)> {
    let receipt: serde_json::Value = serde_json::from_slice(&std::fs::read(dir.join(V3_FILE)).ok()?).ok()?;
    let program = receipt["ExecutablePath"].as_str().filter(|p| Path::new(p).is_file())?;
    Some((program.to_string(), receipt["ReleaseTag"].as_str().map(str::to_string)))
}

/// What a log says was installed.
#[derive(Debug, Default, PartialEq)]
struct Log {
    product_code: Option<String>,
    files: Vec<PathBuf>,
}

/// A log's text: Windows Installer writes UTF-16 or the system's code page.
fn read_log(path: &Path) -> String {
    let Ok(bytes) = std::fs::read(path) else { return String::new() };
    if bytes.starts_with(&[0xff, 0xfe]) || bytes.get(1) == Some(&0) {
        let units: Vec<u16> = bytes.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
        String::from_utf16_lossy(&units).trim_start_matches('\u{feff}').to_string()
    } else {
        String::from_utf8_lossy(&bytes).into_owned()
    }
}

/// The product's code and the files it installed, from a verbose log (`/L*V`):
/// `File: <path>; To be installed` lines, and the folder and name of each copy.
fn parse_log(text: &str) -> Log {
    let mut log = Log::default();
    let mut folder: Option<&str> = None;
    for line in text.lines() {
        if let Some((_, code)) = line.split_once(": ProductCode = {") {
            if let Some((code, _)) = code.split_once('}') {
                log.product_code = Some(format!("{{{code}}}"));
            }
        } else if let Some((_, rest)) = line.split_once("Executing op: SetTargetFolder(Folder=") {
            folder = rest.trim_end().strip_suffix(')');
        } else if let Some((_, rest)) = line.split_once("Executing op: FileCopy(") {
            let name = rest.split(',').find_map(|f| f.strip_prefix("DestName="));
            if let (Some(folder), Some(name)) = (folder, name) {
                // "SHORT~1.EXE|Long Name.exe": the long name.
                let name = name.rsplit('|').next().unwrap_or(name);
                let sep = if folder.ends_with(['\\', '/']) { "" } else { "\\" };
                log.files.push(PathBuf::from(format!("{folder}{sep}{name}")));
            }
        } else if let Some((_, rest)) = line.split_once("]: File: ") {
            if let Some((path, _)) = rest.split_once(';') {
                log.files.push(PathBuf::from(path.trim()));
            }
        }
    }
    log.files.sort();
    log.files.dedup();
    log
}

/// The programs among installed files, shallowest first.
fn programs(files: &[PathBuf]) -> Vec<&PathBuf> {
    let mut found: Vec<&PathBuf> = files.iter().filter(|p| p.extension().is_some_and(|e| e.eq_ignore_ascii_case("exe")) && p.is_file()).collect();
    found.sort_by_key(|p| (p.components().count(), p.to_string_lossy().to_lowercase()));
    found
}

/// The program that starts the app: the first the catalog prefers, else the
/// only one that isn't a helper (an uninstaller, a crash reporter). None
/// when that's for the player to say.
fn program_in(files: &[PathBuf], preferred: &[String]) -> Option<PathBuf> {
    let found = programs(files);
    let name = |p: &PathBuf| p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    for pattern in preferred {
        let Ok(glob) = glob::Pattern::new(&pattern.to_lowercase()) else { continue };
        if let Some(hit) = found.iter().find(|p| glob.matches(&name(p))) {
            return Some((*hit).clone());
        }
    }
    match found.into_iter().filter(|p| !crate::install::HELPERS.iter().any(|h| name(p).starts_with(h))).collect::<Vec<_>>().as_slice() {
        [only] => Some((*only).clone()),
        _ => None,
    }
}

/// Where the setup installed: the folder of its shallowest program, else of its shallowest file.
fn location(files: &[PathBuf]) -> Option<PathBuf> {
    let shallowest = |list: Vec<&PathBuf>| list.into_iter().min_by_key(|p| p.components().count()).and_then(|p| p.parent()).map(Path::to_path_buf);
    shallowest(programs(files)).or_else(|| shallowest(files.iter().collect()))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn line(text: &str) -> String {
        format!("MSI (s) (7C:40) [14:33:08:362]: {text}\r\n")
    }

    #[test]
    fn reads_the_product_and_its_files_from_a_verbose_log() {
        let text = [
            line(r"Executing op: SetTargetFolder(Folder=C:\Program Files\Game\)"),
            line("Executing op: FileCopy(SourceName=game.exe,SourceCabKey=game.exe,DestName=GAME~1.EXE|Game Port.exe,Attributes=512,FileSize=10)"),
            line(r"Executing op: SetTargetFolder(Folder=C:\Program Files\Game\data)"),
            line("Executing op: FileCopy(SourceName=a.pak,SourceCabKey=a.pak,DestName=a.pak,Attributes=512,FileSize=10)"),
            line("File: C:\\Program Files\\Game\\unins000.exe;\tTo be installed;\tWon't patch;\tNo existing file"),
            line("Property(S): UpgradeCode = {11111111-1111-1111-1111-111111111111}"),
            line("Property(S): ProductCode = {2F1C3D4E-0000-4000-8000-ABCDEF012345}"),
        ]
        .concat();
        let log = parse_log(&text);
        assert_eq!(log.product_code.as_deref(), Some("{2F1C3D4E-0000-4000-8000-ABCDEF012345}"));
        let files: Vec<String> = log.files.iter().map(|p| p.to_string_lossy().into_owned()).collect();
        assert_eq!(files, [r"C:\Program Files\Game\Game Port.exe", r"C:\Program Files\Game\data\a.pak", r"C:\Program Files\Game\unins000.exe"]);
    }

    #[test]
    fn reads_logs_in_utf16_too() {
        let dir = tempfile::tempdir().unwrap();
        let text = line("Property(S): ProductCode = {2F1C3D4E-0000-4000-8000-ABCDEF012345}");
        let utf16: Vec<u8> = [0xff, 0xfe].into_iter().chain(text.encode_utf16().flat_map(u16::to_le_bytes)).collect();
        std::fs::write(dir.path().join("a.log"), utf16).unwrap();
        assert_eq!(read_log(&dir.path().join("a.log")), text);
        // A 3.x install's logs name the product, though it didn't record it.
        std::fs::write(dir.path().join(V3_FILE), "{}").unwrap();
        assert!(installed(dir.path()));
        assert_eq!(product_code(dir.path()).as_deref(), Some("{2F1C3D4E-0000-4000-8000-ABCDEF012345}"));
    }

    #[test]
    fn only_a_product_code_goes_to_msiexec() {
        let dir = tempfile::tempdir().unwrap();
        for code in [r"C:\Games\other.msi", "/qn", "{ABC}", "{2F1C3D4E-0000-4000-8000-ABCDEF01234G}"] {
            std::fs::write(dir.path().join(FILE), code).unwrap();
            assert_eq!(product_code(dir.path()), None, "{code}");
        }
        std::fs::write(dir.path().join(FILE), "{2f1c3d4e-0000-4000-8000-abcdef012345}\n").unwrap();
        assert_eq!(product_code(dir.path()).as_deref(), Some("{2f1c3d4e-0000-4000-8000-abcdef012345}"));
    }

    #[test]
    fn picks_the_program_the_catalog_prefers_or_the_only_one() {
        let dir = tempfile::tempdir().unwrap();
        let file = |name: &str| {
            let p = dir.path().join(name);
            std::fs::create_dir_all(p.parent().unwrap()).unwrap();
            std::fs::write(&p, "").unwrap();
            p
        };
        let (game, uninstaller, tool, data) = (file("Game.exe"), file("unins000.exe"), file("tools/Editor.exe"), file("data/a.pak"));
        let missing = dir.path().join("Gone.exe");
        assert_eq!(program_in(&[game.clone(), uninstaller.clone(), data.clone(), missing.clone()], &[]), Some(game.clone()));
        // Two programs: the player says which, unless the catalog does.
        let all = [game.clone(), uninstaller, tool.clone(), data];
        assert_eq!(program_in(&all, &[]), None);
        assert_eq!(program_in(&all, &["editor*.exe".into()]), Some(tool));
        assert_eq!(location(&all), Some(dir.path().to_path_buf()));
        assert_eq!(location(&[missing]), Some(dir.path().to_path_buf()));
    }

    #[test]
    fn a_cancelled_setup_or_one_that_failed_doesnt_count_as_installed() {
        let log = Path::new("installer.log");
        assert_eq!(outcome(0, log), Ok(false));
        assert_eq!(outcome(3010, log), Ok(true));
        assert_eq!(outcome(1602, log), Err(crate::install::CANCELLED.to_string()));
        assert_eq!(outcome(1223, log), Err(crate::install::CANCELLED.to_string()));
        assert!(outcome(1603, log).unwrap_err().contains("error 1603"));
        assert!(outcome(1618, log).unwrap_err().contains("Another installation"));
    }

    #[cfg(unix)]
    #[test]
    fn runs_the_setup_and_records_what_it_installed() {
        use std::os::unix::fs::PermissionsExt;
        let root = tempfile::tempdir().unwrap();
        let (dir, target) = (root.path().join("app"), root.path().join("Program Files/Game"));
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::create_dir_all(&target).unwrap();
        // A stand-in msiexec: installs one program and logs it, as /L*V does (the log is its last argument).
        let msiexec = root.path().join("msiexec");
        let script = format!(
            "#!/bin/sh\necho \"$@\" > \"{args}\"\ntouch \"{exe}\"\nfor log; do :; done\nprintf '%s\\n' 'MSI (s) (1:2) [3]: File: {exe};\tTo be installed' 'MSI (s) (1:2) [3]: Property(S): ProductCode = {{CODE}}' > \"$log\"\n",
            args = root.path().join("args").display(),
            exe = target.join("Game.exe").display(),
        );
        std::fs::write(&msiexec, script).unwrap();
        std::fs::set_permissions(&msiexec, std::fs::Permissions::from_mode(0o755)).unwrap();
        let package = root.path().join("Game.msi");
        let setup = run(&msiexec, &package, &dir, &[], None).unwrap();
        assert_eq!(setup, Setup { program: Some(target.join("Game.exe")), location: Some(target.clone()), restart: false });
        assert_eq!(std::fs::read_to_string(dir.join(FILE)).unwrap(), "{CODE}");
        let args = std::fs::read_to_string(root.path().join("args")).unwrap();
        assert_eq!(args.trim(), format!("/i {} /qf /norestart /L*V {}", package.display(), dir.join(LOG).display()));
        // An update keeps the program the app started with, while it's there.
        let mine = target.join("Mine.exe");
        std::fs::write(&mine, "").unwrap();
        assert_eq!(run(&msiexec, &package, &dir, &[], Some(&mine)).unwrap().program, Some(mine));
    }
}
