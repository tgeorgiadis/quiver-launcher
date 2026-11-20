//! A game that closed at once because it needs the player's own copy of the
//! original game (a ROM), which no release includes. Most ports ask for it
//! with a file picker; some only say so in their output and quit. GoldenEye
//! 007's PC port: "no ROM found (tried ge007.ntsc-final.z64|baserom.u.z64 in
//! data/ and exe dir)". The output names the file it wants and where, so the
//! launcher can say it plainly and put the player's copy there (session.rs, the
//! page's tv/programs/RomDialog.tsx).
use serde::Serialize;
use std::path::{Path, PathBuf};

#[derive(Clone, Debug, PartialEq, Serialize)]
pub struct RomNeed {
    /// The file name the game looks for, when its output names one.
    pub file: Option<String>,
    /// The folder it looks in, relative to the game's program ("" is beside it).
    pub into: String,
    /// The folder of the game's program.
    pub dir: String,
    /// The game's instructions, when there's a README.
    pub readme: Option<String>,
}

/// Files a ROM or disc image comes as.
const EXTENSIONS: [&str; 22] = [
    "z64", "n64", "v64", "nes", "sfc", "smc", "gb", "gbc", "gba", "nds", "3ds", "iso", "bin", "cue", "chd", "cso", "img", "gcm", "rvz", "wad",
    "xex", "xci",
];
/// Words that, beside "ROM", say it wasn't found.
const MISSING: [&str; 13] = [
    "no ", "not found", "missing", "couldn't", "could not", "cannot", "can't", "failed", "unable", "tried", "invalid", "not exist", "doesn't exist",
];

/// What the game's output says it needs; None when it doesn't talk about a missing ROM.
pub fn from_output(output: &str, dir: &Path) -> Option<RomNeed> {
    let lines: Vec<&str> = output.lines().filter(|l| about_missing_rom(l)).collect();
    if lines.is_empty() {
        return None;
    }
    let file = lines.iter().find_map(|l| rom_file(l));
    let into = lines.iter().find_map(|l| folder(l)).unwrap_or_default();
    Some(RomNeed { file, into, dir: dir.to_string_lossy().into_owned(), readme: readme(dir).map(|p| p.to_string_lossy().into_owned()) })
}

/// The message for the app's error, as the desktop card shows it.
pub fn message(need: &RomNeed, log: Option<&Path>) -> String {
    let mut text = String::from("It needs your own copy of the original game (a ROM), which isn't included.");
    match (&need.file, need.into.as_str()) {
        (Some(file), "") => text.push_str(&format!(" It looks for {file} in its folder.")),
        (Some(file), into) => text.push_str(&format!(" It looks for {file} in its {into} folder.")),
        (None, _) if need.readme.is_some() => text.push_str(" Its instructions say where it goes."),
        (None, _) => {}
    }
    if let Some(path) = log {
        text.push_str(&format!(" Full log: {}", path.display()));
    }
    text
}

fn about_missing_rom(line: &str) -> bool {
    let lower = line.to_lowercase();
    // .bin and .img are too common to mean a ROM on their own ("failed to load shaders.bin").
    let rom_named = rom_file(line).is_some_and(|f| !f.to_lowercase().ends_with(".bin") && !f.to_lowercase().ends_with(".img"));
    let rom = has_word(&lower, "rom") || has_word(&lower, "roms") || lower.contains("baserom") || rom_named;
    rom && MISSING.iter().any(|w| lower.contains(w))
}

/// `word` with no letter or digit either side.
fn has_word(text: &str, word: &str) -> bool {
    text.match_indices(word).any(|(at, _)| {
        let before = text[..at].chars().next_back();
        let after = text[at + word.len()..].chars().next();
        !before.is_some_and(|c| c.is_alphanumeric()) && !after.is_some_and(|c| c.is_alphanumeric())
    })
}

/// The first ROM file name in the line ("a.z64|b.z64" gives a.z64).
fn rom_file(line: &str) -> Option<String> {
    line.split(|c: char| !(c.is_alphanumeric() || "._-".contains(c)))
        .map(|t| t.trim_matches('.'))
        .find(|t| {
            t.rsplit_once('.').is_some_and(|(stem, ext)| !stem.is_empty() && EXTENSIONS.contains(&ext.to_lowercase().as_str()))
        })
        .map(str::to_owned)
}

/// A folder named like "data/" on its own: "in data/ and exe dir", "the data/ dir".
fn folder(line: &str) -> Option<String> {
    line.split_whitespace().find_map(|word| {
        let word = word.trim_start_matches(['(', '"', '\'']).trim_end_matches([')', ',', '.', '"', '\'', ':']);
        let name = word.strip_suffix('/').or_else(|| word.strip_suffix('\\'))?;
        simple_name(name).then(|| name.to_owned())
    })
}

/// One folder or file name: no separators, not "." or "..".
fn simple_name(name: &str) -> bool {
    !name.is_empty() && name != "." && name != ".." && name.chars().all(|c| c.is_alphanumeric() || "._- ".contains(c))
}

fn readme(dir: &Path) -> Option<PathBuf> {
    let mut found: Vec<PathBuf> = std::fs::read_dir(dir)
        .ok()?
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.is_file() && p.file_name().is_some_and(|n| n.to_string_lossy().to_lowercase().starts_with("readme")))
        .collect();
    found.sort();
    found.into_iter().next()
}

/// The player picks their copy (picker.rs: the system's dialog, or the page's own in Game Mode); it's copied to where
/// the game looks, under the name it wants. False when they cancelled.
#[tauri::command]
pub async fn tv_place_rom(app: tauri::AppHandle, window: tauri::WebviewWindow, dir: String, into: String, file: String) -> Result<bool, String> {
    if !simple_name(&file) || !(into.is_empty() || simple_name(&into)) {
        return Err("That isn't a file name Quiver can use.".into());
    }
    let dir = PathBuf::from(dir);
    if !dir.is_dir() {
        return Err("The game's folder isn't there any more.".into());
    }
    // Any file: a copy is often named otherwise (a .n64 for a .z64), and it goes under the name the game wants.
    let title = format!("Choose your copy of the game ({file})");
    let Some(picked) = crate::picker::choose(&app, &window, crate::picker::Want::File, &title, None, None).await else { return Ok(false) };
    let folder = if into.is_empty() { dir } else { dir.join(&into) };
    std::fs::create_dir_all(&folder).map_err(|e| format!("Couldn't make the folder {}: {e}", folder.display()))?;
    let target = folder.join(&file);
    std::fs::copy(&picked, &target).map_err(|e| format!("Couldn't copy it to {}: {e}", target.display()))?;
    Ok(true)
}

/// Opens the game's README with the program the computer opens it with.
#[tauri::command]
pub fn tv_open_readme(path: String) -> Result<(), String> {
    let path = PathBuf::from(path);
    let is_readme = path.file_name().is_some_and(|n| n.to_string_lossy().to_lowercase().starts_with("readme"));
    if !is_readme || !path.is_file() {
        return Err("The instructions aren't there any more.".into());
    }
    open::that_detached(&path).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    const GOLDENEYE: &str = "[INFO ] GoldenEye 007 PC port starting (ntsc-final, unknown larkspur)
[WARN ] config: cannot write E:\\QuiverLauncher\\Apps\\GoldenEye007-GoldenEye007PCPort\\data\\ge007.ini
[ERROR] romdataInit: no ROM found (tried ge007.ntsc-final.z64|baserom.u.z64 in data/ and exe dir)
[ERROR] Failed to load the ROM (no .z64 in the data/ dir, asset conversion failed, or it could not be mapped): see the earlier log lines for the cause";

    #[test]
    fn goldeneye_wants_its_rom_in_data() {
        let need = from_output(GOLDENEYE, Path::new("/no/such/folder")).unwrap();
        assert_eq!(need.file.as_deref(), Some("ge007.ntsc-final.z64"));
        assert_eq!(need.into, "data");
        assert_eq!(need.readme, None);
        assert_eq!(
            message(&need, None),
            "It needs your own copy of the original game (a ROM), which isn't included. It looks for ge007.ntsc-final.z64 in its data folder."
        );
    }

    #[test]
    fn other_failures_and_words_that_contain_rom_arent_a_missing_rom() {
        assert_eq!(from_output("Loading from cache\nerror: no Vulkan device found\nchrome not found", Path::new(".")), None);
        assert_eq!(from_output("ROM loaded: game.z64\nerror: audio device missing", Path::new(".")), None);
        assert_eq!(from_output("error: failed to load shaders.bin", Path::new(".")), None);
        assert!(from_output("error: Game.iso not found", Path::new(".")).is_some());
    }

    #[test]
    fn a_rom_without_a_name_or_folder_still_counts() {
        let need = from_output("Fatal: could not open the ROM", Path::new(".")).unwrap();
        assert_eq!((need.file, need.into), (None, String::new()));
    }

    #[test]
    fn file_names_are_kept_to_one_name() {
        assert!(simple_name("ge007.ntsc-final.z64"));
        assert!(!simple_name("..") && !simple_name("a/b") && !simple_name("a\\b") && !simple_name(""));
        assert_eq!(folder("put it in ../ or C:\\Games\\ or roms/"), Some("roms".into()));
    }
}
