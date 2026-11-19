//! Apps whose release is a Windows script that installs them (`GT5-Installer.bat`): it opens its own window, fetches
//! and builds the app somewhere the player picks, and puts a shortcut to it on the desktop or in the Start menu. Such a
//! script hands off and returns at once, and the install can take an hour, so Quiver can't wait for it as it does for a
//! setup program (setup.rs). Instead Play notes when it first ran the script, and on a later Play looks for a shortcut
//! made since then to a program made since then: that's what the script installed, and Play starts it from then on.
//! Until then, and if it goes, Play runs the script again. The app's folder keeps Quiver's record of it (`FILE`).
use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

/// When the app's install script first ran, and the program it installed once Quiver has found it.
pub const FILE: &str = ".quiver-script";

#[derive(Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
struct Record {
    /// Seconds since 1970 when Play first ran the script.
    started: u64,
    /// The program the script installed.
    program: Option<String>,
}

fn read(dir: &Path) -> Option<Record> {
    serde_json::from_slice(&fs::read(dir.join(FILE)).ok()?).ok()
}

fn write(dir: &Path, record: &Record) {
    if let Ok(bytes) = serde_json::to_vec(record) {
        let _ = fs::write(dir.join(FILE), bytes);
    }
}

fn lower_name(path: &Path) -> String {
    path.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default()
}

/// A Windows batch script.
fn is_script(path: &Path) -> bool {
    let name = lower_name(path);
    name.ends_with(".bat") || name.ends_with(".cmd")
}

/// Whether `program`, which Play found in the app's folder `dir`, is a script that installs the app: named as an
/// installer is, and the folder's only program. A game started by a script of its own ("Start Game.bat") isn't one.
pub fn installs(dir: &Path, program: &Path) -> bool {
    let name = lower_name(program);
    is_script(program) && (name.contains("instal") || name.contains("setup")) && crate::install::programs(dir, "windows").len() == 1
}

/// The program the app's install script put on this computer, when Quiver knows it or can find it now.
pub fn installed(dir: &Path) -> Option<PathBuf> {
    let mut record = read(dir)?;
    if let Some(program) = record.program.as_ref().map(PathBuf::from).filter(|p| p.is_file()) {
        return Some(program);
    }
    if record.started == 0 {
        return None;
    }
    let found = made_since(UNIX_EPOCH + Duration::from_secs(record.started), dir, &places())?;
    record.program = Some(found.to_string_lossy().into_owned());
    write(dir, &record);
    Some(found)
}

/// Play ran the app's install script. The first time, or once what it installed has gone, Quiver looks for what it
/// installs from now on.
pub fn ran(dir: &Path) {
    let mut record = read(dir).unwrap_or_default();
    let gone = record.program.as_ref().is_some_and(|p| !Path::new(p).is_file());
    if record.started != 0 && !gone {
        return;
    }
    record.started = SystemTime::now().duration_since(UNIX_EPOCH).map_or(0, |d| d.as_secs());
    record.program = None;
    write(dir, &record);
}

/// Where installs put their shortcuts: the player's desktop and Start menu, and everyone's.
fn places() -> Vec<PathBuf> {
    let mut places: Vec<PathBuf> = dirs::desktop_dir().into_iter().collect();
    let env = |name: &str| std::env::var_os(name).map(PathBuf::from);
    places.extend(dirs::data_dir().map(|d| d.join(r"Microsoft\Windows\Start Menu\Programs")));
    places.extend(env("PUBLIC").map(|d| d.join("Desktop")));
    places.extend(env("ProgramData").map(|d| d.join(r"Microsoft\Windows\Start Menu\Programs")));
    places
}

fn shortcuts(dir: &Path, depth: usize, out: &mut Vec<PathBuf>) {
    let Ok(entries) = fs::read_dir(dir) else { return };
    for entry in entries.flatten() {
        let path = entry.path();
        if path.is_dir() {
            if depth < 2 {
                shortcuts(&path, depth + 1, out);
            }
        } else if lower_name(&path).ends_with(".lnk") {
            out.push(path);
        }
    }
}

/// Whether a shortcut or the program it points at is named for the app in the folder `dir`: a word of the folder's
/// name, other than common ones ("recomp", "port"), in the shortcut's name or the program's path ("GT5-PC").
fn named_for(dir: &Path, link: &Path, program: &Path) -> bool {
    use crate::install::{words, COMMON_WORDS};
    let own: Vec<String> = words(&dir.file_name().unwrap_or_default().to_string_lossy()).into_iter().filter(|w| !COMMON_WORDS.contains(&w.as_str())).collect();
    let stem = |p: &Path| p.file_stem().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let mut theirs = words(&stem(link));
    for part in program.components().rev().take(3) {
        theirs.extend(words(&stem(Path::new(part.as_os_str()))));
    }
    own.iter().any(|w| theirs.contains(w))
}

/// The program a shortcut made since `since` points at, when it's one that was made since then too (built, or put
/// there by the install), is named for the app, and isn't in the app's own folder `dir`. Only when there's one such
/// program: with several, which is the app's is for the player to say. (An update of another app, a browser's say,
/// can rewrite its shortcut and program in the meantime.)
fn made_since(since: SystemTime, dir: &Path, places: &[PathBuf]) -> Option<PathBuf> {
    // File times can be a little behind the clock.
    let since = since - Duration::from_secs(5);
    let new = |path: &Path| fs::metadata(path).is_ok_and(|m| m.modified().is_ok_and(|t| t >= since) || m.created().is_ok_and(|t| t >= since));
    let mut links = Vec::new();
    for place in places {
        shortcuts(place, 0, &mut links);
    }
    let mut found: Vec<PathBuf> = links
        .iter()
        .filter(|link| new(link))
        .filter_map(|link| Some((link, target(&fs::read(link).ok()?, link.parent()?)?)))
        .filter(|(link, program)| {
            let name = lower_name(program);
            let runs = name.ends_with(".exe") || name.ends_with(".bat") || name.ends_with(".cmd");
            runs && !name.starts_with("unins") && program.is_file() && !program.starts_with(dir) && new(program) && named_for(dir, link, program)
        })
        .map(|(_, program)| program)
        .collect();
    found.sort_by_key(|p| p.to_string_lossy().to_lowercase());
    found.dedup_by_key(|p| p.to_string_lossy().to_lowercase());
    match found.as_slice() {
        [only] => Some(only.clone()),
        _ => None,
    }
}

fn u16_at(bytes: &[u8], at: usize) -> Option<usize> {
    Some(u16::from_le_bytes(bytes.get(at..at + 2)?.try_into().ok()?) as usize)
}

fn u32_at(bytes: &[u8], at: usize) -> Option<usize> {
    Some(u32::from_le_bytes(bytes.get(at..at + 4)?.try_into().ok()?) as usize)
}

/// A NUL-ended string: ANSI, which is only trusted when it's ASCII and has no "?" (what Windows writes for a
/// character its code page lacks), or UTF-16.
fn ansi_at(bytes: &[u8], at: usize) -> Option<String> {
    let rest = bytes.get(at..)?;
    let text = &rest[..rest.iter().position(|b| *b == 0)?];
    (text.is_ascii() && !text.contains(&b'?')).then(|| String::from_utf8_lossy(text).into_owned())
}

fn wide_at(bytes: &[u8], at: usize) -> Option<String> {
    let units: Vec<u16> = bytes.get(at..)?.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).take_while(|u| *u != 0).collect();
    String::from_utf16(&units).ok()
}

/// Where a Windows shortcut (.lnk, [MS-SHLLINK]) points: the local path its LinkInfo holds, else its relative path from
/// the shortcut's folder `beside`.
fn target(bytes: &[u8], beside: &Path) -> Option<PathBuf> {
    const HAS_ID_LIST: usize = 0x1;
    const HAS_LINK_INFO: usize = 0x2;
    const HAS_NAME: usize = 0x4;
    const HAS_RELATIVE_PATH: usize = 0x8;
    const IS_UNICODE: usize = 0x80;
    if u32_at(bytes, 0)? != 0x4C {
        return None;
    }
    let flags = u32_at(bytes, 0x14)?;
    let mut at = 0x4C;
    if flags & HAS_ID_LIST != 0 {
        at += 2 + u16_at(bytes, at)?;
    }
    if flags & HAS_LINK_INFO != 0 {
        let info = at;
        let size = u32_at(bytes, info)?;
        let header = u32_at(bytes, info + 4)?;
        let local = u32_at(bytes, info + 8)? & 0x1 != 0;
        if local {
            let path = if header >= 0x24 {
                let base = wide_at(bytes, info + u32_at(bytes, info + 0x1C)?);
                let suffix = wide_at(bytes, info + u32_at(bytes, info + 0x20)?).unwrap_or_default();
                base.map(|b| b + &suffix)
            } else {
                None
            };
            let path = path.or_else(|| {
                let base = ansi_at(bytes, info + u32_at(bytes, info + 0x10)?)?;
                let suffix = ansi_at(bytes, info + u32_at(bytes, info + 0x18)?).unwrap_or_default();
                Some(base + &suffix)
            });
            if let Some(path) = path.filter(|p| !p.is_empty()) {
                return Some(PathBuf::from(path));
            }
        }
        at = info + size;
    }
    // StringData: its strings in order, each a count of characters and then them.
    let unit = if flags & IS_UNICODE != 0 { 2 } else { 1 };
    let string = |at: usize| -> Option<(String, usize)> {
        let count = u16_at(bytes, at)?;
        let raw = bytes.get(at + 2..at + 2 + count * unit)?;
        let text = if unit == 2 { String::from_utf16(&raw.chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect::<Vec<_>>()).ok()? } else { String::from_utf8_lossy(raw).into_owned() };
        Some((text, at + 2 + count * unit))
    };
    if flags & HAS_NAME != 0 {
        at = string(at)?.1;
    }
    if flags & HAS_RELATIVE_PATH != 0 {
        let relative = string(at)?.0.replace('\\', std::path::MAIN_SEPARATOR_STR);
        return Some(beside.join(relative));
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A shortcut as Windows writes one: a header, an ID list, LinkInfo with the local path, and its relative path.
    fn shortcut(local: Option<&str>, unicode_local: bool, relative: Option<&str>) -> Vec<u8> {
        let mut flags = 0x1 | 0x80;
        let mut out = vec![0u8; 0x4C];
        out[0..4].copy_from_slice(&0x4Cu32.to_le_bytes());
        // An ID list Quiver skips.
        out.extend(4u16.to_le_bytes());
        out.extend([2, 0, 0, 0]);
        if let Some(local) = local {
            flags |= 0x2;
            let header: u32 = if unicode_local { 0x24 } else { 0x1C };
            let mut info = vec![0u8; header as usize];
            info[4..8].copy_from_slice(&header.to_le_bytes());
            info[8..12].copy_from_slice(&1u32.to_le_bytes());
            let ansi = info.len();
            info.extend(local.bytes().map(|b| if b.is_ascii() { b } else { b'?' }));
            info.push(0);
            let suffix = info.len();
            info.push(0);
            info[0x10..0x14].copy_from_slice(&(ansi as u32).to_le_bytes());
            info[0x18..0x1C].copy_from_slice(&(suffix as u32).to_le_bytes());
            if unicode_local {
                let wide = info.len();
                info.extend(local.encode_utf16().chain([0]).flat_map(u16::to_le_bytes));
                let suffix = info.len();
                info.extend([0, 0]);
                info[0x1C..0x20].copy_from_slice(&(wide as u32).to_le_bytes());
                info[0x20..0x24].copy_from_slice(&(suffix as u32).to_le_bytes());
            }
            let size = info.len() as u32;
            info[0..4].copy_from_slice(&size.to_le_bytes());
            out.extend(info);
        }
        if let Some(relative) = relative {
            flags |= 0x8;
            let units: Vec<u16> = relative.encode_utf16().collect();
            out.extend((units.len() as u16).to_le_bytes());
            out.extend(units.into_iter().flat_map(u16::to_le_bytes));
        }
        out[0x14..0x18].copy_from_slice(&(flags as u32).to_le_bytes());
        out
    }

    #[test]
    fn reads_where_a_shortcut_points() {
        let beside = Path::new("/desktop");
        let launcher = r"C:\Games\GT5-PC\GT5Launcher.exe";
        assert_eq!(target(&shortcut(Some(launcher), false, None), beside), Some(PathBuf::from(launcher)));
        assert_eq!(target(&shortcut(Some(r"C:\Spiele\Größe\Spiel.exe"), true, None), beside), Some(PathBuf::from(r"C:\Spiele\Größe\Spiel.exe")));
        // An ANSI path that isn't plain ASCII can't be trusted: the relative path is.
        let relative = r"..\Games\Größe\Spiel.exe".replace('\\', std::path::MAIN_SEPARATOR_STR);
        assert_eq!(target(&shortcut(Some(r"C:\Games\Größe\Spiel.exe"), false, Some(r"..\Games\Größe\Spiel.exe")), beside), Some(beside.join(relative)));
        assert_eq!(target(&shortcut(None, false, None), beside), None);
        assert_eq!(target(b"not a shortcut", beside), None);
    }

    #[test]
    fn finds_the_one_program_an_install_made_a_shortcut_to() {
        let root = tempfile::tempdir().unwrap();
        let (desktop, menu, app, games) = (root.path().join("Desktop"), root.path().join("Start Menu"), root.path().join("apps/gt5-recomp"), root.path().join("Games/GT5-PC"));
        for d in [&desktop, &menu.join("GT5"), &app, &games] {
            fs::create_dir_all(d).unwrap();
        }
        // Files on disk now are from before the run; what it makes is dated after it.
        let since = SystemTime::now() + Duration::from_secs(60);
        let made = |path: &Path| fs::File::options().write(true).open(path).unwrap().set_modified(since + Duration::from_secs(1)).unwrap();
        let old_game = root.path().join("Old/GT5.exe");
        fs::create_dir_all(old_game.parent().unwrap()).unwrap();
        fs::write(&old_game, "").unwrap();
        let script = app.join("GT5-Installer.bat");
        fs::write(&script, "").unwrap();
        let places = [desktop.clone(), menu.clone()];
        let link = |at: &Path, to: &Path| {
            fs::write(at, shortcut(Some(&to.to_string_lossy()), true, None)).unwrap();
            made(at);
        };
        let new_program = |path: &Path| {
            fs::create_dir_all(path.parent().unwrap()).unwrap();
            fs::write(path, "").unwrap();
            made(path);
        };
        // Nothing yet; a new shortcut to an old program (another app's, say) or to the script itself doesn't count.
        assert_eq!(made_since(since, &app, &places), None);
        link(&desktop.join("Old.lnk"), &old_game);
        made(&script);
        link(&desktop.join("Again.lnk"), &script);
        // A shortcut to a program that isn't there.
        let launcher = games.join("GT5Launcher.exe");
        link(&desktop.join("Gran Turismo 5 (PC).lnk"), &launcher);
        assert_eq!(made_since(since, &app, &places), None);
        new_program(&launcher);
        assert_eq!(made_since(since, &app, &places), Some(launcher.clone()));
        // The same program from the Start menu too is still one.
        link(&menu.join("GT5/Gran Turismo 5.lnk"), &launcher);
        assert_eq!(made_since(since, &app, &places), Some(launcher.clone()));
        // Shortcuts older than the run don't count, whatever they point at.
        assert_eq!(made_since(since + Duration::from_secs(60), &app, &places), None);
        // Another app's program, updated in the meantime with its shortcut (a browser's), isn't named for this one.
        let browser = root.path().join("Browser/Application/browser.exe");
        new_program(&browser);
        link(&desktop.join("Browser.lnk"), &browser);
        assert_eq!(made_since(since, &app, &places), Some(launcher.clone()));
        // Two new programs named for it: which is the app's is the player's to say.
        let other = root.path().join("GT5 Tools/Tool.exe");
        new_program(&other);
        link(&menu.join("Tool.lnk"), &other);
        assert_eq!(made_since(since, &app, &places), None);
    }

    #[test]
    fn play_runs_the_script_until_what_it_installed_is_there() {
        let root = tempfile::tempdir().unwrap();
        let app = root.path().join("gt5");
        fs::create_dir_all(&app).unwrap();
        let script = app.join("GT5-Installer.bat");
        fs::write(&script, "").unwrap();
        assert!(installs(&app, &script));
        assert!(!installs(&app, &app.join("Start Game.bat")));
        // Never ran: nothing to look for.
        assert_eq!(installed(&app), None);
        ran(&app);
        let first = read(&app).unwrap();
        assert!(first.started > 0 && first.program.is_none());
        // Found once: kept, without looking again.
        let game = root.path().join("Game.exe");
        fs::write(&game, "").unwrap();
        write(&app, &Record { started: first.started, program: Some(game.to_string_lossy().into_owned()) });
        assert_eq!(installed(&app), Some(game.clone()));
        ran(&app);
        assert_eq!(read(&app).unwrap().program, Some(game.to_string_lossy().into_owned()));
        // Gone (the player removed the game): the script runs again, and Quiver looks from then on.
        fs::remove_file(&game).unwrap();
        ran(&app);
        assert_eq!(read(&app).unwrap().program, None);
        // With a program of its own beside it, the script isn't what installs the app.
        fs::write(app.join("Game.exe"), "").unwrap();
        assert!(!installs(&app, &script));
    }
}
