//! Which program starts a game, when the catalog doesn't name one for this
//! computer. The launcher then takes the first program it finds, which can be
//! a tool beside the game: Star Fox Enhanced's Linux build has
//! starfox_asset_builder before starfox_pc, and Bloodborne's Windows port has
//! five programs in out\ (bb-gpu-capabilities.exe first) and starts from
//! launch_gui.bat. TV mode asks the player instead, when it's a real choice.
//!
//! The candidates are the launcher's own (install::programs). On Windows the
//! launcher is only guessing when several programs sit side by side at the
//! shallowest level that has any; crash handlers don't count.
use serde::Serialize;
use std::path::{Path, PathBuf};
use tauri::State;

use crate::Data;

#[derive(Serialize)]
pub struct Programs {
    /// The file name of the first program matching the hints, if any.
    matched: Option<String>,
    /// Every program in the folder, relative to it, shallowest first.
    choices: Vec<String>,
    /// Windows: several programs at the shallowest level, so the launcher's pick is a guess.
    unsure: bool,
}

#[tauri::command]
pub fn tv_programs(data: State<Data>, folder: String, dir: Option<String>, hints: Vec<String>) -> Result<Programs, String> {
    let dir = crate::app_dir(&data, &folder, dir)?;
    Ok(programs(&dir, crate::install::programs(&dir, crate::OS), &hints, crate::OS))
}

fn programs(dir: &Path, found: Vec<PathBuf>, hints: &[String], os: &str) -> Programs {
    let found: Vec<PathBuf> = found.into_iter().filter(|p| !crash_handler(p)).collect();
    let name = |p: &Path| p.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let matched = hints.iter().find_map(|hint| {
        let glob = glob::Pattern::new(&hint.to_lowercase()).ok()?;
        found.iter().map(|p| name(p)).find(|n| glob.matches(&n.to_lowercase()))
    });
    let relative: Vec<&Path> = found.iter().filter_map(|p| p.strip_prefix(dir).ok()).collect();
    let unsure = os == "windows" && {
        let depths: Vec<usize> = relative.iter().filter(|p| !is_script(p)).map(|p| p.components().count()).collect();
        depths.iter().min().is_some_and(|top| depths.iter().filter(|d| *d == top).count() > 1)
    };
    let choices = relative.iter().map(|p| p.to_string_lossy().into_owned()).collect();
    Programs { matched, choices, unsure }
}

/// A Windows script that can start a game, as Bloodborne's launch_gui.bat does.
fn is_script(p: &Path) -> bool {
    p.extension().is_some_and(|e| e.eq_ignore_ascii_case("bat") || e.eq_ignore_ascii_case("cmd"))
}

/// Unity's UnityCrashHandler64.exe and Chromium's crashpad_handler.exe sit beside many games.
fn crash_handler(p: &Path) -> bool {
    let name = p.file_name().map(|n| n.to_string_lossy().to_lowercase()).unwrap_or_default();
    name.contains("crashhandler") || name.contains("crashpad")
}

#[cfg(test)]
mod tests {
    use super::*;

    fn folder(files: &[&str]) -> tempfile::TempDir {
        let dir = tempfile::tempdir().unwrap();
        for name in files {
            let path = dir.path().join(name);
            std::fs::create_dir_all(path.parent().unwrap()).unwrap();
            std::fs::write(&path, b"x").unwrap();
        }
        dir
    }

    fn windows(dir: &Path, hints: &[String]) -> Programs {
        programs(dir, crate::install::programs(dir, "windows"), hints, "windows")
    }

    #[test]
    fn several_programs_side_by_side_are_a_guess_and_scripts_are_offered() {
        let dir = folder(&["launch_gui.bat", "README.md", "out/bb-gpu-capabilities.exe", "out/bb-probe.exe", "out/bbport.exe"]);
        let found = windows(dir.path(), &[]);
        assert!(found.unsure);
        assert_eq!(found.choices.len(), 4);
        assert!(found.choices[0].ends_with("launch_gui.bat"));
    }

    #[test]
    fn one_program_above_the_rest_and_a_crash_handler_are_no_guess() {
        let unreal = folder(&["Game.exe", "Game/Binaries/Win64/Game-Win64-Shipping.exe", "Game/Binaries/Win64/Tool.exe"]);
        assert!(!windows(unreal.path(), &[]).unsure);
        let unity = folder(&["Daggerfall.exe", "UnityCrashHandler64.exe"]);
        let found = windows(unity.path(), &[]);
        assert!(!found.unsure);
        assert_eq!(found.choices, ["Daggerfall.exe"]);
    }

    #[test]
    fn a_picked_script_is_matched() {
        let dir = folder(&["launch_gui.bat", "out/a.exe", "out/b.exe"]);
        assert_eq!(windows(dir.path(), &["launch_gui.bat".into()]).matched.as_deref(), Some("launch_gui.bat"));
    }
}
