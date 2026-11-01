//! How the player wants an app started, when the launcher's own choice isn't
//! right: which of its programs, run directly, through Wine or through a
//! Proton they have, and launch options written the way Steam takes them.
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

#[derive(Deserialize, Default, Clone)]
#[serde(rename_all = "camelCase")]
pub struct LaunchOptions {
    /// The program to start, inside the app's folder (relative), or anywhere for an app the player added.
    #[serde(default)]
    pub executable: Option<String>,
    #[serde(default)]
    pub runner: Option<Runner>,
    /// What Settings runs every Windows program with on Linux, where the app has no runner of its own.
    #[serde(default)]
    pub windows_runner: Option<Runner>,
    /// Steam-style: `NAME=value` words first set the environment, `%command%`
    /// marks where the game goes, and without it everything is arguments.
    #[serde(default)]
    pub command: Option<String>,
}

#[derive(Deserialize, Clone, PartialEq, Debug)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum Runner {
    /// The program itself, even a Windows one.
    Direct,
    Wine,
    /// A Proton: its folder, or its `proton` script.
    Proton { path: String },
}

/// The player's launch options, read.
#[derive(Default, Clone, Debug, PartialEq)]
pub struct Command {
    pub env: Vec<(String, String)>,
    /// What goes before the game (a wrapper such as gamemoderun), when `%command%` is used.
    pub before: Vec<String>,
    /// Arguments after the game.
    pub after: Vec<String>,
}

pub fn parse(text: &str) -> Result<Command, String> {
    let words = split(text)?;
    let named = |w: &str| {
        w.split_once('=').is_some_and(|(name, _)| {
            !name.is_empty() && !name.starts_with(|c: char| c.is_ascii_digit()) && name.chars().all(|c| c.is_ascii_alphanumeric() || c == '_')
        })
    };
    let first = words.iter().position(|w| !named(w)).unwrap_or(words.len());
    let env = words[..first].iter().map(|w| w.split_once('=').map(|(k, v)| (k.to_string(), v.to_string())).unwrap()).collect();
    let rest = &words[first..];
    Ok(match rest.iter().position(|w| w == "%command%") {
        Some(at) => Command { env, before: rest[..at].to_vec(), after: rest[at + 1..].to_vec() },
        None => Command { env, before: vec![], after: rest.to_vec() },
    })
}

/// Splits launch options into words as a shell would: quotes group, backslash escapes.
pub fn split(text: &str) -> Result<Vec<String>, String> {
    let (mut words, mut word, mut started) = (Vec::new(), String::new(), false);
    let mut chars = text.chars();
    let mut quote: Option<char> = None;
    while let Some(c) = chars.next() {
        match (quote, c) {
            (Some(q), c) if c == q => quote = None,
            (Some('"'), '\\') => match chars.next() {
                Some(n @ ('"' | '\\' | '$' | '`')) => word.push(n),
                Some(n) => {
                    word.push('\\');
                    word.push(n);
                }
                None => word.push('\\'),
            },
            (Some(_), c) => word.push(c),
            (None, '"' | '\'') => {
                quote = Some(c);
                started = true;
            }
            (None, '\\') => {
                if let Some(n) = chars.next() {
                    word.push(n);
                }
                started = true;
            }
            (None, c) if c.is_whitespace() => {
                if started || !word.is_empty() {
                    words.push(std::mem::take(&mut word));
                }
                started = false;
            }
            (None, c) => word.push(c),
        }
    }
    if quote.is_some() {
        return Err("The launch options have a quote that isn't closed.".into());
    }
    if started || !word.is_empty() {
        words.push(word);
    }
    Ok(words)
}

/// A Proton's script, from its folder or the script itself.
pub fn proton_script(path: &str) -> Result<PathBuf, String> {
    let path = PathBuf::from(path);
    let script = if path.is_dir() { path.join("proton") } else { path };
    if script.is_file() {
        Ok(script)
    } else {
        Err(format!("There's no Proton at {} any more. Choose another in Launch options.", script.display()))
    }
}

/// Where Steam keeps itself, for Proton (it wants to know, though games run without Steam open).
pub fn steam_client() -> PathBuf {
    let home = dirs::home_dir().unwrap_or_default();
    [home.join(".steam/steam"), home.join(".local/share/Steam")]
        .into_iter()
        .find(|p| p.is_dir())
        .unwrap_or_else(|| home.join(".steam/steam"))
}

#[derive(Serialize, PartialEq, Debug)]
pub struct Proton {
    pub name: String,
    pub path: String,
}

/// The Protons on this computer: Valve's from Steam libraries, and custom
/// builds (such as GE-Proton) in compatibilitytools.d.
pub fn protons(home: &Path) -> Vec<Proton> {
    let mut steams: Vec<PathBuf> = vec![home.join(".steam/steam"), home.join(".local/share/Steam"), home.join(".var/app/com.valvesoftware.Steam/.local/share/Steam")];
    steams.retain(|p| p.is_dir());
    let mut places: Vec<(PathBuf, bool)> = Vec::new();
    for steam in &steams {
        places.push((steam.join("compatibilitytools.d"), false));
        places.push((steam.join("steamapps/common"), true));
        for library in libraries(steam) {
            places.push((library.join("steamapps/common"), true));
        }
    }
    places.push((PathBuf::from("/usr/share/steam/compatibilitytools.d"), false));
    let mut found: Vec<(PathBuf, Proton)> = Vec::new();
    for (place, valve) in places {
        let Ok(entries) = std::fs::read_dir(&place) else { continue };
        for entry in entries.flatten() {
            let dir = entry.path();
            let name = entry.file_name().to_string_lossy().into_owned();
            if (valve && !name.starts_with("Proton")) || !dir.join("proton").is_file() {
                continue;
            }
            let real = dir.canonicalize().unwrap_or(dir.clone());
            if !found.iter().any(|(r, _)| *r == real) {
                found.push((real, Proton { name, path: dir.to_string_lossy().into_owned() }));
            }
        }
    }
    let mut list: Vec<Proton> = found.into_iter().map(|(_, p)| p).collect();
    list.sort_by(|a, b| b.name.cmp(&a.name));
    list
}

/// The Proton a Windows app runs through when the player chose none and there's no Wine, as on a Steam Deck: Valve's
/// newest numbered Proton, else Experimental, else the newest of the rest (such as GE-Proton), Hotfix last, as it's
/// for particular games.
pub fn automatic_proton(mut list: Vec<Proton>) -> Option<Proton> {
    let numbers = |name: &str| name.split(|c: char| !c.is_ascii_digit()).filter_map(|n| n.parse::<u32>().ok()).collect::<Vec<_>>();
    let rank = |p: &Proton| match p.name.strip_prefix("Proton ") {
        Some(rest) if rest.starts_with(|c: char| c.is_ascii_digit()) => 0,
        _ if p.name.contains("Experimental") => 1,
        _ if p.name.contains("Hotfix") => 3,
        _ => 2,
    };
    list.sort_by(|a, b| rank(a).cmp(&rank(b)).then_with(|| numbers(&b.name).cmp(&numbers(&a.name))));
    list.into_iter().next()
}

/// The other Steam libraries listed in a Steam folder's libraryfolders.vdf.
fn libraries(steam: &Path) -> Vec<PathBuf> {
    let Ok(text) = std::fs::read_to_string(steam.join("steamapps/libraryfolders.vdf")) else { return vec![] };
    text.lines()
        .filter_map(|line| {
            let words = split(line).ok()?;
            (words.len() == 2 && words[0] == "path").then(|| PathBuf::from(&words[1]))
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_launch_options_as_steam_does() {
        let s = |v: &[&str]| v.iter().map(|w| w.to_string()).collect::<Vec<_>>();
        assert_eq!(parse("--fullscreen -w 2").unwrap(), Command { after: s(&["--fullscreen", "-w", "2"]), ..Default::default() });
        assert_eq!(
            parse(r#"DXVK_HUD=1 MY_DIR="/a b" gamemoderun %command% --x 'y z'"#).unwrap(),
            Command { env: vec![("DXVK_HUD".into(), "1".into()), ("MY_DIR".into(), "/a b".into())], before: s(&["gamemoderun"]), after: s(&["--x", "y z"]) }
        );
        // A word with = that isn't first, or isn't a name, is an argument.
        assert_eq!(parse("--size=2 A=1").unwrap().after, s(&["--size=2", "A=1"]));
        assert_eq!(parse("").unwrap(), Command::default());
        assert_eq!(split(r#"a\ b "" "c\"d""#).unwrap(), s(&["a b", "", "c\"d"]));
        assert!(parse("\"open").is_err());
    }

    #[test]
    fn finds_protons_in_steam_and_compatibility_tools() {
        let home = tempfile::tempdir().unwrap();
        let steam = home.path().join(".steam/steam");
        let library = home.path().join("Games/SteamLibrary");
        for dir in [steam.join("steamapps/common/Proton 9.0"), steam.join("compatibilitytools.d/GE-Proton9-20"), library.join("steamapps/common/Proton - Experimental")] {
            std::fs::create_dir_all(&dir).unwrap();
            std::fs::write(dir.join("proton"), "").unwrap();
        }
        // Not Protons: a game, and a tool folder without the script.
        std::fs::create_dir_all(steam.join("steamapps/common/Celeste")).unwrap();
        std::fs::write(steam.join("steamapps/common/Celeste/proton"), "").unwrap();
        std::fs::create_dir_all(steam.join("compatibilitytools.d/Empty")).unwrap();
        std::fs::write(steam.join("steamapps/libraryfolders.vdf"), format!("\"libraryfolders\"\n{{\n\t\"1\"\n\t{{\n\t\t\"path\"\t\t\"{}\"\n\t}}\n}}\n", library.display())).unwrap();
        let names: Vec<String> = protons(home.path()).into_iter().map(|p| p.name).collect();
        assert_eq!(names, ["Proton 9.0", "Proton - Experimental", "GE-Proton9-20"]);
        assert!(proton_script(&steam.join("steamapps/common/Proton 9.0").to_string_lossy()).is_ok());
        assert!(proton_script(&steam.join("nothing").to_string_lossy()).is_err());
    }

    #[test]
    fn without_wine_a_windows_app_runs_through_valves_newest_proton() {
        let list = |names: &[&str]| names.iter().map(|n| Proton { name: n.to_string(), path: format!("/p/{n}") }).collect::<Vec<_>>();
        let pick = |names: &[&str]| automatic_proton(list(names)).map(|p| p.name);
        // A Steam Deck's.
        assert_eq!(pick(&["Proton - Experimental", "Proton 9.0 (Beta)", "Proton Hotfix", "GE-Proton9-26"]).as_deref(), Some("Proton 9.0 (Beta)"));
        assert_eq!(pick(&["Proton 9.0", "Proton 10.0", "Proton 8.0"]).as_deref(), Some("Proton 10.0"));
        assert_eq!(pick(&["Proton Hotfix", "GE-Proton9-26", "Proton - Experimental"]).as_deref(), Some("Proton - Experimental"));
        assert_eq!(pick(&["Proton Hotfix", "GE-Proton9-26", "GE-Proton10-3"]).as_deref(), Some("GE-Proton10-3"));
        assert_eq!(pick(&["Proton Hotfix"]).as_deref(), Some("Proton Hotfix"));
        assert_eq!(pick(&[]), None);
    }
}
