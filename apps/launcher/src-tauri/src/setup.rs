//! Releases that ship as a Windows setup program: an .exe that installs the
//! app, by itself or inside an archive. Quiver runs it in its own window, as
//! the developer made it, suggesting the app's folder where the installer
//! takes a suggestion (Inno Setup, NSIS). When it closes, Quiver finds what it
//! installed: in the suggested folder, or among the apps Windows lists as
//! installed, for one the player put elsewhere. Play then starts the installed
//! program, never the setup again; Uninstall runs the app's own uninstaller
//! first. The app's folder keeps Quiver's record of it (`FILE`).
use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};

/// Says an app was installed by its setup program, and how to find and uninstall it.
pub const FILE: &str = ".quiver-setup";

/// What made a setup program, which decides what it can be told.
#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind {
    /// Inno Setup: takes `/DIR=`, and its uninstaller `/VERYSILENT`.
    Inno,
    /// NSIS: takes `/D=`, and its uninstaller `/S`.
    Nsis,
    /// Any other: installs where it chooses, and Windows' list of installed apps says where.
    Other,
}

#[derive(Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct Record {
    kind: Option<Kind>,
    /// Where it installed, when that isn't the app's folder.
    location: Option<String>,
    /// Its entry in Windows' list of installed apps (`HKCU\...\Uninstall\Name`).
    key: Option<String>,
}

/// What a setup put on the computer.
#[derive(Debug, PartialEq)]
pub struct Setup {
    /// The program that starts the app, when Quiver could tell.
    pub program: Option<PathBuf>,
    /// Where it was installed.
    pub location: PathBuf,
    /// Installed into the app's folder, as the files of any other download are.
    pub here: bool,
}

#[derive(Debug, PartialEq)]
pub enum Outcome {
    Installed(Setup),
    /// It ran and installed nothing Quiver can find: the download is the app itself (a portable program named like a
    /// setup, say), and is installed as files.
    Nothing,
}

/// An app in Windows' list of installed apps.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Entry {
    pub key: String,
    pub location: Option<String>,
    pub icon: Option<String>,
    pub uninstall: Option<String>,
    pub quiet: Option<String>,
    /// Changes whenever the entry is written.
    pub stamp: String,
}

fn lower_name(path: &Path) -> String {
    path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default()
}

/// Named as a setup program is: "Game-Setup.exe", "Installer.exe", "Instalar-Juego.exe".
fn named_setup(name: &str) -> bool {
    name.contains("setup") || name.contains("instal")
}

/// Installers of something else that come with an app (a runtime's, the app's own uninstaller).
fn runtime(name: &str) -> bool {
    ["unins", "vc_redist", "vcredist", "dxsetup", "dxwebsetup", "directx", "dotnet", "windowsdesktop-runtime", "oalinst", "crash"].iter().any(|r| name.starts_with(r))
}

/// How far into a program its maker's marks are looked for.
const SCAN: usize = 32 << 20;

/// What kind of setup program a file is, by the marks its maker leaves in it; None for a program that isn't one.
/// `alone` is whether it's the only program in its download, which a setup with no known maker has to be.
pub fn kind_of(file: &Path, alone: bool) -> Option<Kind> {
    let name = lower_name(file);
    if !name.ends_with(".exe") || name.contains("portable") {
        return None;
    }
    let mut bytes = Vec::new();
    fs::File::open(file).ok()?.take(SCAN as u64).read_to_end(&mut bytes).ok()?;
    let has = |mark: &[u8]| bytes.windows(mark.len()).any(|w| w == mark);
    let wide = |mark: &str| mark.encode_utf16().flat_map(u16::to_le_bytes).collect::<Vec<u8>>();
    if has(b"Inno Setup") {
        return Some(Kind::Inno);
    }
    if has(b"Nullsoft") {
        return Some(Kind::Nsis);
    }
    // Other makers' setups, and ones written for one app: named as a setup, alone in the download, and either a known
    // maker's or one that lists itself among Windows' installed apps.
    const MAKERS: [&[u8]; 6] = [b"InstallShield", b".wixburn", b"Advanced Installer", b"InstallAware", b"Velopack", b"Squirrel"];
    const LISTS_ITSELF: &str = r"CurrentVersion\Uninstall";
    let other = alone && named_setup(&name) && (MAKERS.iter().any(|m| has(m)) || has(LISTS_ITSELF.as_bytes()) || has(&wide(LISTS_ITSELF)));
    other.then_some(Kind::Other)
}

fn exes(dir: &Path, depth: usize, out: &mut Vec<(usize, PathBuf)>) {
    let Ok(entries) = fs::read_dir(dir) else { return };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            if depth < 3 {
                exes(&path, depth + 1, out);
            }
        } else if lower_name(&path).ends_with(".exe") && !runtime(&lower_name(&path)) {
            out.push((depth, path));
        }
    }
}

/// The setup program a download is, when it is one: the only program at its top, a setup by its maker's marks, and
/// either the only program in it or named as a setup. A game with tools beside it is never one.
pub fn installer_in(out: &Path) -> Option<(PathBuf, Kind)> {
    let mut found = Vec::new();
    exes(out, 0, &mut found);
    let top = found.iter().map(|(depth, _)| *depth).min()?;
    let mut at_top = found.iter().filter(|(depth, _)| *depth == top);
    let (_, candidate) = at_top.next()?;
    if at_top.next().is_some() {
        return None;
    }
    let alone = found.len() == 1;
    let kind = kind_of(candidate, alone)?;
    (alone || named_setup(&lower_name(candidate))).then(|| (candidate.clone(), kind))
}

/// What a setup is started with so it suggests `dir`. Each maker reads its own way: Inno Setup takes quotes, NSIS
/// takes none and wants it last.
fn arguments(kind: Kind, dir: &Path) -> String {
    match kind {
        Kind::Inno => format!("/DIR=\"{}\"", dir.display()),
        Kind::Nsis => format!("/D={}", dir.display()),
        Kind::Other => String::new(),
    }
}

fn read(dir: &Path) -> Option<Record> {
    serde_json::from_slice(&fs::read(dir.join(FILE)).ok()?).ok()
}

/// Whether an app's folder records a setup program's install.
pub fn installed(dir: &Path) -> bool {
    dir.join(FILE).is_file()
}

/// Whether an app's setup installed it somewhere other than its folder.
pub fn elsewhere(dir: &Path) -> bool {
    read(dir).is_some_and(|r| r.location.is_some())
}

fn has_programs(dir: &Path) -> bool {
    !crate::install::programs(dir, "windows").is_empty()
}

fn helper(name: &str) -> bool {
    crate::install::HELPERS.iter().any(|h| name.starts_with(h))
}

/// A path as the registry gives it: `"C:\Game\game.exe",0`, quoted or not, with or without an icon's number.
fn plain_path(value: &str) -> Option<PathBuf> {
    let value = value.trim();
    let value = match value.strip_prefix('"') {
        Some(rest) => rest.split('"').next().unwrap_or(rest),
        None => value.rsplit_once(',').filter(|(_, n)| n.trim().parse::<i32>().is_ok()).map_or(value, |(path, _)| path),
    };
    let value = value.trim().trim_end_matches(['\\', '/']);
    (!value.is_empty()).then(|| PathBuf::from(value))
}

/// The program and arguments of an uninstall command: `"C:\Game\unins000.exe" /SILENT`, or unquoted, spaces and all.
fn command(line: &str) -> Option<(PathBuf, String)> {
    let line = line.trim();
    if let Some(rest) = line.strip_prefix('"') {
        let (program, args) = rest.split_once('"')?;
        return Some((PathBuf::from(program), args.trim().to_string()));
    }
    let end = line.to_ascii_lowercase().find(".exe")? + 4;
    Some((PathBuf::from(&line[..end]), line[end..].trim().to_string()))
}

/// Where a listed app is: the folder it names, else its icon's or its uninstaller's.
fn location_of(entry: &Entry) -> Option<PathBuf> {
    let named = entry.location.as_deref().and_then(plain_path).filter(|p| p.is_dir());
    let beside = |file: Option<PathBuf>| file.and_then(|f| f.parent().map(Path::to_path_buf)).filter(|p| p.is_dir());
    named
        .or_else(|| beside(entry.icon.as_deref().and_then(plain_path)))
        .or_else(|| beside(entry.uninstall.as_deref().and_then(command).map(|(program, _)| program)))
}

fn same(a: &Path, b: &Path) -> bool {
    let plain = |p: &Path| p.canonicalize().unwrap_or_else(|_| p.to_path_buf()).to_string_lossy().to_lowercase();
    plain(a) == plain(b)
}

/// What the setup installed, and its entry among Windows' installed apps: in the folder it was told, else the listed
/// app that's new or changed since `before`.
fn look(kind: Kind, suggested: &Path, before: &[Entry], after: &[Entry]) -> Option<(PathBuf, Option<Entry>)> {
    let listed_at = |dir: &Path| after.iter().find(|e| location_of(e).is_some_and(|l| same(&l, dir))).cloned();
    if kind != Kind::Other && has_programs(suggested) {
        return Some((suggested.to_path_buf(), listed_at(suggested)));
    }
    let mut changed: Vec<(PathBuf, &Entry)> = after
        .iter()
        .filter(|e| !before.iter().any(|b| b.key == e.key && b.stamp == e.stamp))
        .filter_map(|e| Some((location_of(e)?, e)))
        // A runtime the setup brought along (Visual C++'s) lists itself too, from Windows' package cache.
        .filter(|(l, _)| !l.to_string_lossy().to_lowercase().contains("package cache") && has_programs(l))
        .collect();
    // The one that says where it is, before one known only by its icon.
    changed.sort_by_key(|(l, e)| (!same(l, suggested), e.location.as_deref().and_then(plain_path).is_none()));
    changed.into_iter().next().map(|(l, e)| (l, Some(e.clone())))
}

/// The program that starts an installed app: the one it started with before, the first the catalog prefers, the only
/// one that isn't a helper, else the one Windows shows its icon from. None when that's for the player to say.
fn program_in(location: &Path, preferred: &[String], keep: Option<&Path>, entry: Option<&Entry>) -> Option<PathBuf> {
    if let Some(kept) = keep.filter(|p| p.is_file()) {
        return Some(kept.to_path_buf());
    }
    let found = crate::install::programs(location, "windows");
    for pattern in preferred {
        let Ok(glob) = glob::Pattern::new(&pattern.to_lowercase()) else { continue };
        if let Some(hit) = found.iter().find(|p| glob.matches(&lower_name(p))) {
            return Some(hit.clone());
        }
    }
    let own = |p: &Path| lower_name(p).ends_with(".exe") && !helper(&lower_name(p));
    let mine: Vec<&PathBuf> = found.iter().filter(|p| own(p)).collect();
    if let [only] = mine.as_slice() {
        return Some((*only).clone());
    }
    entry.and_then(|e| e.icon.as_deref()).and_then(plain_path).filter(|icon| icon.is_file() && own(icon) && icon.starts_with(location))
}

/// Runs a download's setup program and waits for it. `dir` is the app's folder, `keep` the program the app started
/// with before (kept while it's there), `version` what the folder is marked with once it's in.
pub fn install(installer: &Path, kind: Kind, dir: &Path, preferred: &[String], keep: Option<&Path>, version: &str) -> Result<Outcome, String> {
    run(installer, kind, dir, preferred, keep, version, &start, &registered, 8)
}

#[allow(clippy::too_many_arguments)]
fn run(
    installer: &Path,
    kind: Kind,
    dir: &Path,
    preferred: &[String],
    keep: Option<&Path>,
    version: &str,
    start: &dyn Fn(&Path, &str, &Path) -> Result<Option<i32>, String>,
    registered: &dyn Fn() -> Vec<Entry>,
    patience: u32,
) -> Result<Outcome, String> {
    let was = read(dir);
    // An update goes where the app is; a new install is suggested the app's folder.
    let suggested = was.as_ref().and_then(|r| r.location.as_ref()).map(PathBuf::from).filter(|l| l.is_dir()).unwrap_or_else(|| dir.to_path_buf());
    // An installer asks before using a folder that's there already, so an empty one isn't left for it to find.
    let _ = fs::remove_dir(dir);
    let before = registered();
    let cwd = installer.parent().unwrap_or(dir);
    // Marked as Quiver's once anything is in it, so an install cut short can still be retried or removed.
    let mark = |version: &str| {
        let marker = dir.join(crate::install::VERSION_FILE);
        if dir.is_dir() && (!version.is_empty() || !marker.exists()) {
            let _ = fs::write(marker, version);
        }
        let _ = fs::remove_dir(dir);
    };
    let code = start(installer, &arguments(kind, &suggested), cwd).inspect_err(|_| mark(""))?;
    let stopped = match (kind, code) {
        // The player closed it: Inno Setup ends with 2 (before installing) or 5 (during), NSIS with 1.
        (Kind::Inno, Some(2 | 5)) | (Kind::Nsis, Some(1)) => Some(crate::install::CANCELLED.to_string()),
        (Kind::Inno | Kind::Nsis, Some(code)) if code != 0 => Some(format!("Its installer stopped with error {code}, so it didn't install.")),
        _ => None,
    };
    if let Some(why) = stopped {
        mark("");
        return Err(why);
    }
    let mut found = look(kind, &suggested, &before, &registered());
    // A setup can close before what it started has finished (one that hands over to an updater, say).
    for _ in 0..patience {
        if found.is_some() || kind != Kind::Other {
            break;
        }
        std::thread::sleep(std::time::Duration::from_secs(1));
        found = look(kind, &suggested, &before, &registered());
    }
    let Some((location, entry)) = found else {
        mark("");
        // An update that changed nothing was closed; a new download that installs nothing is the app itself.
        return if was.is_some() { Err(crate::install::CANCELLED.into()) } else { Ok(Outcome::Nothing) };
    };
    let here = same(&location, dir);
    fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let record = Record { kind: Some(kind), location: (!here).then(|| location.to_string_lossy().into_owned()), key: entry.as_ref().map(|e| e.key.clone()) };
    fs::write(dir.join(FILE), serde_json::to_vec(&record).map_err(|e| e.to_string())?).map_err(|e| e.to_string())?;
    mark(version);
    let program = program_in(&location, preferred, keep, entry.as_ref());
    Ok(Outcome::Installed(Setup { program, location, here }))
}

/// The app's own uninstaller and what to start it with: the one Windows lists for it, else the one in its folder.
/// Only ever a program inside the folder the app was installed in.
fn uninstaller(record: &Record, location: &Path, listed: &[Entry]) -> Option<(PathBuf, String)> {
    let kind = record.kind.unwrap_or(Kind::Other);
    let entry = record.key.as_ref().and_then(|key| listed.iter().find(|e| &e.key == key));
    let from_entry = entry.and_then(|e| {
        let quiet = e.quiet.as_deref().and_then(command);
        let (program, args) = e.uninstall.as_deref().and_then(command).or_else(|| quiet.clone())?;
        // Another maker's own quiet command, when it has one.
        let args = quiet.filter(|(p, _)| same(p, &program)).map_or(args, |(_, args)| args);
        Some((program, args))
    });
    let in_folder = || {
        let mut found: Vec<PathBuf> = fs::read_dir(location).ok()?.flatten().map(|e| e.path()).filter(|p| lower_name(p).starts_with("unins") && lower_name(p).ends_with(".exe")).collect();
        // Inno Setup numbers them, and the highest is the newest.
        found.sort();
        found.pop().map(|p| (p, String::new()))
    };
    let (program, args) = from_entry.filter(|(p, _)| p.is_file() && p.starts_with(location)).or_else(in_folder)?;
    let args = match kind {
        Kind::Inno => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART".to_string(),
        // In place and waited for, not from a copy of itself that outlives it; unquoted and last, as NSIS reads it.
        Kind::Nsis => format!("/S _?={}", location.display()),
        Kind::Other => args,
    };
    Some((program, args))
}

/// Runs the uninstaller of an app its setup installed. The player already said to uninstall it, so Inno Setup's and
/// NSIS's run without asking again. Fails with "cancelled" when the app is still there afterwards.
pub fn uninstall(dir: &Path) -> Result<(), String> {
    remove(dir, &start, &registered)
}

fn remove(dir: &Path, start: &dyn Fn(&Path, &str, &Path) -> Result<Option<i32>, String>, registered: &dyn Fn() -> Vec<Entry>) -> Result<(), String> {
    let Some(record) = read(dir) else { return Ok(()) };
    let elsewhere = record.location.is_some();
    let location = record.location.as_ref().map(PathBuf::from).unwrap_or_else(|| dir.to_path_buf());
    // Already gone: the player uninstalled it in Windows.
    if !location.is_dir() || (elsewhere && !has_programs(&location)) {
        return Ok(());
    }
    let Some((program, args)) = uninstaller(&record, &location, &registered()) else {
        // In the app's folder, its files go with the folder.
        if !elsewhere {
            return Ok(());
        }
        if cfg!(windows) {
            let _ = open::that_detached("ms-settings:appsfeatures");
        }
        return Err("Quiver couldn't find this app's uninstaller. Uninstall it in Windows Settings, under Apps > Installed apps, then remove it here.".into());
    };
    // Not from inside the folder it removes.
    start(&program, &args, &std::env::temp_dir())?;
    if elsewhere && has_programs(&location) {
        return Err(crate::install::CANCELLED.into());
    }
    Ok(())
}

/// Starts a program as a double click would and waits for it to close; what it ended with, when Windows says.
#[cfg(windows)]
fn start(program: &Path, line: &str, dir: &Path) -> Result<Option<i32>, String> {
    match crate::elevate::open(program, line, dir) {
        Ok(process) => Ok(process.and_then(|p| p.code())),
        // The player said no when Windows asked to allow it.
        Err(e) if e.raw_os_error() == Some(crate::elevate::DECLINED) => Err(crate::install::CANCELLED.into()),
        Err(e) => Err(format!("{} couldn't start: {e}", program.file_name().unwrap_or_default().to_string_lossy())),
    }
}

#[cfg(not(windows))]
fn start(_: &Path, _: &str, _: &Path) -> Result<Option<i32>, String> {
    Err("This download is a Windows setup program, which only runs on Windows.".into())
}

/// The apps Windows lists as installed, for this player and for everyone.
#[cfg(windows)]
fn registered() -> Vec<Entry> {
    use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE, KEY_READ, KEY_WOW64_32KEY, KEY_WOW64_64KEY};
    const PATH: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    let mut entries = Vec::new();
    for (hive, name, view) in [(HKEY_CURRENT_USER, "HKCU", 0), (HKEY_LOCAL_MACHINE, "HKLM", KEY_WOW64_64KEY), (HKEY_LOCAL_MACHINE, "HKLM32", KEY_WOW64_32KEY)] {
        let Ok(list) = winreg::RegKey::predef(hive).open_subkey_with_flags(PATH, KEY_READ | view) else { continue };
        for sub in list.enum_keys().flatten() {
            let Ok(key) = list.open_subkey_with_flags(&sub, KEY_READ | view) else { continue };
            let value = |name: &str| key.get_value::<String, _>(name).ok().filter(|v| !v.trim().is_empty());
            let written = key.query_info().map(|i| format!("{}:{}", i.last_write_time.dwHighDateTime, i.last_write_time.dwLowDateTime)).unwrap_or_default();
            entries.push(Entry {
                key: format!(r"{name}\{sub}"),
                location: value("InstallLocation"),
                icon: value("DisplayIcon"),
                uninstall: value("UninstallString"),
                quiet: value("QuietUninstallString"),
                stamp: format!("{written}|{}|{}", value("DisplayVersion").unwrap_or_default(), value("InstallDate").unwrap_or_default()),
            });
        }
    }
    entries
}

#[cfg(not(windows))]
fn registered() -> Vec<Entry> {
    Vec::new()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::RefCell;

    fn file(path: &Path, contents: &[u8]) -> PathBuf {
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, contents).unwrap();
        path.to_path_buf()
    }

    #[test]
    fn tells_a_setup_program_by_its_makers_marks() {
        let dir = tempfile::tempdir().unwrap();
        let at = |name: &str, contents: &[u8]| file(&dir.path().join(name), contents);
        assert_eq!(kind_of(&at("a.exe", b"MZ..JR.Inno.Setup..Inno Setup Setup Data"), false), Some(Kind::Inno));
        assert_eq!(kind_of(&at("b.exe", b"MZ..Nullsoft.NSIS.exehead"), false), Some(Kind::Nsis));
        assert_eq!(kind_of(&at("Game.exe", b"MZ a game"), true), None);
        // A portable program can be packed by a setup's maker: it's the app, and runs as one.
        assert_eq!(kind_of(&at("Launcher-1.0-portable.exe", b"MZ..Nullsoft.NSIS.exehead"), true), None);
        // One written for the app counts when it's named a setup, alone, and lists itself with Windows.
        let lists: Vec<u8> = r"Software\Microsoft\Windows\CurrentVersion\Uninstall\Game".encode_utf16().flat_map(u16::to_le_bytes).collect();
        assert_eq!(kind_of(&at("Game-Setup.exe", &lists), true), Some(Kind::Other));
        assert_eq!(kind_of(&at("Game-Setup.exe", &lists), false), None);
        assert_eq!(kind_of(&at("Game.exe", &lists), true), None);
        assert_eq!(kind_of(&at("Game-Setup.exe", b"MZ builds the game beside itself"), true), None);
        assert_eq!(kind_of(&at("Juego-Instalar.exe", b"MZ Velopack"), true), Some(Kind::Other));
        assert_eq!(kind_of(&at("setup.txt", b"Inno Setup"), true), None);
    }

    #[test]
    fn a_download_is_a_setup_only_when_thats_all_it_is() {
        let inno = b"MZ Inno Setup";
        let root = tempfile::tempdir().unwrap();
        let at = |path: &str, contents: &[u8]| file(&root.path().join(path), contents);
        let setup = at("one/Game-Setup.exe", inno);
        assert_eq!(installer_in(&root.path().join("one")), Some((setup, Kind::Inno)));
        // Its data and a runtime's installer beside it change nothing.
        at("two/Install.exe", inno);
        at("two/data.bin", b"");
        at("two/redist/vc_redist.x64.exe", b"MZ");
        assert!(installer_in(&root.path().join("two")).is_some());
        // A game beside it: the download is the game, and its setup is the player's to run.
        at("three/Game-Setup.exe", inno);
        at("three/Game.exe", b"MZ");
        assert_eq!(installer_in(&root.path().join("three")), None);
        // A setup with what it installs from in a folder beside it.
        at("four/Setup.exe", inno);
        at("four/files/Game.exe", b"MZ");
        assert!(installer_in(&root.path().join("four")).is_some());
        // A game packed by a setup's maker, with its tools: not named a setup, so not one.
        at("five/Game.exe", b"MZ Nullsoft");
        at("five/tools/Editor.exe", b"MZ");
        assert_eq!(installer_in(&root.path().join("five")), None);
        at("six/Game.exe", b"MZ");
        assert_eq!(installer_in(&root.path().join("six")), None);
        assert_eq!(installer_in(&root.path().join("none")), None);
    }

    #[test]
    fn each_maker_is_told_the_folder_its_own_way() {
        let dir = Path::new(r"C:\My Games\Port");
        assert_eq!(arguments(Kind::Inno, dir), r#"/DIR="C:\My Games\Port""#);
        assert_eq!(arguments(Kind::Nsis, dir), r"/D=C:\My Games\Port");
        assert_eq!(arguments(Kind::Other, dir), "");
    }

    #[test]
    fn reads_paths_and_commands_as_the_registry_writes_them() {
        assert_eq!(plain_path(r#""C:\Game\game.exe",0"#), Some(PathBuf::from(r"C:\Game\game.exe")));
        assert_eq!(plain_path(r"C:\Game\game.exe,0"), Some(PathBuf::from(r"C:\Game\game.exe")));
        assert_eq!(plain_path(r"C:\Games, Inc\Game\"), Some(PathBuf::from(r"C:\Games, Inc\Game")));
        assert_eq!(plain_path("  "), None);
        assert_eq!(command(r#""C:\My Game\unins000.exe" /SILENT"#), Some((PathBuf::from(r"C:\My Game\unins000.exe"), "/SILENT".into())));
        assert_eq!(command(r"C:\My Game\Update.exe --uninstall"), Some((PathBuf::from(r"C:\My Game\Update.exe"), "--uninstall".into())));
        assert_eq!(command("rundll32"), None);
    }

    /// A stand-in for the setup's own window: `act` is what the player did in it.
    struct Fake<'a> {
        act: Box<dyn Fn(&str) -> Option<i32> + 'a>,
        started: RefCell<Vec<(PathBuf, String)>>,
        listed: RefCell<Vec<Entry>>,
    }

    impl<'a> Fake<'a> {
        fn new(act: impl Fn(&str) -> Option<i32> + 'a) -> Self {
            Fake { act: Box::new(act), started: RefCell::default(), listed: RefCell::default() }
        }
        fn install(&self, installer: &Path, kind: Kind, dir: &Path, preferred: &[String], keep: Option<&Path>) -> Result<Outcome, String> {
            let start = |program: &Path, line: &str, _: &Path| {
                self.started.borrow_mut().push((program.to_path_buf(), line.to_string()));
                Ok((self.act)(line))
            };
            run(installer, kind, dir, preferred, keep, "1.0", &start, &|| self.listed.borrow().clone(), 0)
        }
        fn uninstall(&self, dir: &Path) -> Result<(), String> {
            let start = |program: &Path, line: &str, _: &Path| {
                self.started.borrow_mut().push((program.to_path_buf(), line.to_string()));
                Ok((self.act)(line))
            };
            remove(dir, &start, &|| self.listed.borrow().clone())
        }
    }

    #[test]
    fn installs_into_the_apps_folder_when_the_setup_takes_the_suggestion() {
        let root = tempfile::tempdir().unwrap();
        let (dir, installer) = (root.path().join("apps/Game"), root.path().join("stage/Game-Setup.exe"));
        let fake = Fake::new(|_| {
            file(&dir.join("Game.exe"), b"MZ");
            file(&dir.join("unins000.exe"), b"MZ");
            Some(0)
        });
        let done = fake.install(&installer, Kind::Inno, &dir, &[], None).unwrap();
        assert_eq!(done, Outcome::Installed(Setup { program: Some(dir.join("Game.exe")), location: dir.clone(), here: true }));
        assert_eq!(fake.started.borrow()[0], (installer.clone(), format!("/DIR=\"{}\"", dir.display())));
        assert_eq!(fs::read_to_string(dir.join(crate::install::VERSION_FILE)).unwrap(), "1.0");
        assert!(installed(&dir) && !elsewhere(&dir));
        // Uninstalling runs its uninstaller without asking again; the folder goes after, as any app's does.
        fake.uninstall(&dir).unwrap();
        assert_eq!(fake.started.borrow()[1], (dir.join("unins000.exe"), "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART".into()));
    }

    #[test]
    fn a_setup_the_player_closes_installs_nothing() {
        let root = tempfile::tempdir().unwrap();
        let (dir, installer) = (root.path().join("apps/Game"), root.path().join("stage/Game-Setup.exe"));
        assert_eq!(Fake::new(|_| Some(2)).install(&installer, Kind::Inno, &dir, &[], None), Err(crate::install::CANCELLED.to_string()));
        assert_eq!(Fake::new(|_| Some(1)).install(&installer, Kind::Nsis, &dir, &[], None), Err(crate::install::CANCELLED.to_string()));
        assert!(!dir.exists(), "no folder is left behind");
        assert!(Fake::new(|_| Some(3)).install(&installer, Kind::Inno, &dir, &[], None).unwrap_err().contains("error 3"));
        // Closed partway: what it left is marked Quiver's, to install over or remove.
        let partway = Fake::new(|_| {
            file(&dir.join("data.bin"), b"");
            Some(5)
        });
        assert_eq!(partway.install(&installer, Kind::Inno, &dir, &[], None), Err(crate::install::CANCELLED.to_string()));
        assert!(crate::install::may_install_into(&dir) && !installed(&dir));
        // One that isn't an installer after all (it ran and closed): the download is the app.
        let other = root.path().join("apps/Other");
        assert_eq!(Fake::new(|_| Some(0)).install(&installer, Kind::Nsis, &other, &[], None), Ok(Outcome::Nothing));
        assert!(!other.exists());
    }

    #[test]
    fn finds_an_app_installed_elsewhere_in_windows_list() {
        let root = tempfile::tempdir().unwrap();
        let (dir, installer, there) = (root.path().join("apps/Game"), root.path().join("stage/Setup.exe"), root.path().join("Program Files/Game"));
        let entry = |stamp: &str| Entry {
            key: r"HKCU\Game".into(),
            location: Some(there.to_string_lossy().into_owned()),
            icon: Some(format!("{},0", there.join("Launcher.exe").display())),
            uninstall: Some(format!("\"{}\" --uninstall", there.join("Update.exe").display())),
            quiet: None,
            stamp: stamp.into(),
        };
        // What the player does in its window: installs it, or (started with its arguments) uninstalls it.
        let fake = Fake::new(|line| {
            if line.is_empty() {
                for name in ["Launcher.exe", "Game.exe", "Update.exe"] {
                    file(&there.join(name), b"MZ");
                }
            } else {
                fs::remove_dir_all(&there).unwrap();
            }
            Some(0)
        });
        let old = Entry { key: r"HKLM\Old".into(), location: Some(root.path().to_string_lossy().into_owned()), ..Default::default() };
        // Listed already and unchanged: nothing was installed, so the download is the app itself.
        *fake.listed.borrow_mut() = vec![old.clone(), entry("1")];
        let unchanged = Fake::new(|_| Some(0));
        *unchanged.listed.borrow_mut() = fake.listed.borrow().clone();
        file(&there.join("Game.exe"), b"MZ");
        assert_eq!(unchanged.install(&installer, Kind::Other, &dir, &[], None), Ok(Outcome::Nothing));
        // New in the list since the setup started.
        let listed = fake.listed.borrow().clone();
        let start = |_: &Path, line: &str, _: &Path| Ok((fake.act)(line));
        let calls = RefCell::new(0);
        let registered = || {
            *calls.borrow_mut() += 1;
            if *calls.borrow() == 1 { listed[..1].to_vec() } else { listed.clone() }
        };
        let done = run(&installer, Kind::Other, &dir, &[], None, "1.0", &start, &registered, 0).unwrap();
        // Three programs: the one Windows shows its icon from.
        assert_eq!(done, Outcome::Installed(Setup { program: Some(there.join("Launcher.exe")), location: there.clone(), here: false }));
        assert!(elsewhere(&dir));
        assert_eq!(fs::read_to_string(dir.join(crate::install::VERSION_FILE)).unwrap(), "1.0");
        // The catalog's preferred program wins.
        *calls.borrow_mut() = 0;
        let again = run(&installer, Kind::Other, &dir, &["game.exe".into()], None, "1.1", &start, &registered, 0).unwrap();
        assert_eq!(again, Outcome::Installed(Setup { program: Some(there.join("Game.exe")), location: there.clone(), here: false }));
        // An update whose setup changed nothing was closed.
        assert_eq!(fake.install(&installer, Kind::Other, &dir, &[], None), Err(crate::install::CANCELLED.to_string()));
        assert_eq!(fs::read_to_string(dir.join(crate::install::VERSION_FILE)).unwrap(), "1.1");
        // Uninstalling runs the uninstaller Windows lists, as it's listed.
        fake.uninstall(&dir).unwrap();
        assert_eq!(fake.started.borrow().last().unwrap(), &(there.join("Update.exe"), "--uninstall".to_string()));
        // Gone already, there's nothing to run.
        fake.uninstall(&dir).unwrap();
        assert_eq!(fake.started.borrow().len(), 2);
    }

    #[test]
    fn an_uninstaller_outside_the_apps_folder_is_never_run() {
        let root = tempfile::tempdir().unwrap();
        let (dir, there, outside) = (root.path().join("apps/Game"), root.path().join("Program Files/Game"), root.path().join("Windows/format.exe"));
        file(&there.join("Game.exe"), b"MZ");
        file(&outside, b"MZ");
        let record = Record { kind: Some(Kind::Nsis), location: Some(there.to_string_lossy().into_owned()), key: Some("HKCU\\Game".into()) };
        file(&dir.join(FILE), &serde_json::to_vec(&record).unwrap());
        let fake = Fake::new(|_| Some(0));
        *fake.listed.borrow_mut() = vec![Entry { key: "HKCU\\Game".into(), uninstall: Some(format!("\"{}\" C:", outside.display())), ..Default::default() }];
        assert!(fake.uninstall(&dir).unwrap_err().contains("Windows Settings"));
        assert!(fake.started.borrow().is_empty());
        // Its own, in its folder, runs in place; still there afterwards, the player said no.
        file(&there.join("Uninstall Game.exe"), b"MZ");
        assert_eq!(fake.uninstall(&dir), Err(crate::install::CANCELLED.to_string()));
        assert_eq!(fake.started.borrow()[0], (there.join("Uninstall Game.exe"), format!("/S _?={}", there.display())));
    }
}
