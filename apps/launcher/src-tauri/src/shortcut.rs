//! Desktop shortcuts and Steam shortcuts for an installed app. Both start the
//! game directly, as Quiver Launcher 3 did, so they work without Quiver.
use serde::Deserialize;
use std::path::{Path, PathBuf};

/// What starts an app: the program, its arguments, and the folder to run in.
pub struct Target {
    /// The game's own program (an .exe, AppImage, binary or .app).
    pub exe: PathBuf,
    /// What runs it: the game itself, Wine, a Proton, `open` or Explorer.
    pub program: PathBuf,
    pub args: Vec<String>,
    /// What the runner needs set (Proton's folders).
    pub env: Vec<(String, String)>,
    pub dir: PathBuf,
    /// Set for an AppImage on a system without FUSE 2.
    pub extract_appimage: bool,
    /// Steam runs this Windows game with its own Proton.
    pub steam_proton: bool,
    /// The player's launch options.
    pub options: crate::launch::Command,
}

impl Target {
    /// The whole command: program, arguments and environment, with the player's launch options.
    pub fn command(&self) -> (PathBuf, Vec<String>, Vec<(String, String)>) {
        let mut env = Vec::new();
        if self.extract_appimage {
            env.push(("APPIMAGE_EXTRACT_AND_RUN".to_string(), "1".to_string()));
        }
        env.extend(self.env.iter().cloned());
        env.extend(self.options.env.iter().cloned());
        let mut args: Vec<String> = Vec::new();
        let program = match self.options.before.split_first() {
            Some((wrapper, rest)) => {
                args.extend(rest.iter().cloned());
                args.push(self.program.to_string_lossy().into_owned());
                PathBuf::from(wrapper)
            }
            None => self.program.clone(),
        };
        args.extend(self.args.iter().cloned());
        args.extend(self.options.after.iter().cloned());
        (program, args, env)
    }
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct Art {
    pub icon: Option<String>,
    pub header: Option<String>,
    pub capsule: Option<String>,
    pub hero: Option<String>,
    pub logo: Option<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ShortcutRequest {
    pub name: String,
    pub folder: String,
    pub dir: Option<String>,
    pub preferred: Vec<String>,
    pub wine: bool,
    /// A program the player picked, started exactly.
    #[serde(default)]
    pub program: Option<String>,
    #[serde(default)]
    pub launch: crate::launch::LaunchOptions,
    #[serde(default)]
    pub art: Art,
}

/// A file name from an app's name: what Windows and Linux both allow.
pub fn file_name(name: &str) -> String {
    let clean: String = name
        .chars()
        .map(|c| if c.is_control() || "<>:\"/\\|?*".contains(c) { ' ' } else { c })
        .collect();
    let clean = clean.trim().trim_end_matches('.').to_string();
    if clean.is_empty() { "Game".into() } else { clean }
}

/// Downloads an image and saves it as PNG (or ICO on Windows), at most 256 pixels.
pub async fn save_icon(url: &str, path: &Path) -> Result<PathBuf, String> {
    let image = download_image(url).await?;
    let image = image.resize(256, 256, image::imageops::FilterType::Lanczos3);
    let path = path.with_extension(if cfg!(windows) { "ico" } else { "png" });
    std::fs::create_dir_all(path.parent().unwrap()).map_err(|e| e.to_string())?;
    // ICO holds at most 256×256, which resize() kept to.
    image.save(&path).map_err(|e| e.to_string())?;
    Ok(path)
}

pub async fn download_image(url: &str) -> Result<image::DynamicImage, String> {
    if !url.starts_with("https://") && !url.starts_with("http://") {
        return Err("Unsupported image address".into());
    }
    let bytes = reqwest::get(url).await.and_then(|r| r.error_for_status()).map_err(|e| e.to_string())?;
    let bytes = bytes.bytes().await.map_err(|e| e.to_string())?;
    image::load_from_memory(&bytes).map_err(|e| e.to_string())
}

/// Puts a shortcut on the desktop; returns where.
pub fn desktop(target: &Target, name: &str, icon: Option<&Path>) -> Result<PathBuf, String> {
    // Without XDG user folders set up, Linux desktops still use ~/Desktop.
    let desktop = dirs::desktop_dir()
        .or_else(|| dirs::home_dir().map(|h| h.join("Desktop")))
        .ok_or("Couldn't find your desktop folder.")?;
    std::fs::create_dir_all(&desktop).map_err(|e| e.to_string())?;
    write_shortcut(&desktop, target, name, icon)
}

#[cfg(windows)]
fn write_shortcut(desktop: &Path, target: &Target, name: &str, icon: Option<&Path>) -> Result<PathBuf, String> {
    let path = desktop.join(format!("{}.lnk", file_name(name)));
    // A shortcut can't carry environment variables; Play and Steam shortcuts do.
    let (program, args, _) = target.command();
    let mut link = mslnk::ShellLink::new(&program).map_err(|e| e.to_string())?;
    if !args.is_empty() {
        link.set_arguments(Some(args.iter().map(|a| windows_quote(a)).collect::<Vec<_>>().join(" ")));
    }
    link.set_working_dir(Some(target.dir.to_string_lossy().into()));
    link.set_name(Some(format!("Play {name}")));
    let icon = icon.map(Path::to_path_buf).unwrap_or_else(|| target.exe.clone());
    link.set_icon_location(Some(icon.to_string_lossy().into()));
    link.create_lnk(&path).map_err(|e| e.to_string())?;
    Ok(path)
}

#[cfg(not(windows))]
fn write_shortcut(desktop: &Path, target: &Target, name: &str, icon: Option<&Path>) -> Result<PathBuf, String> {
    let path = desktop.join(format!("{}.desktop", file_name(name)));
    std::fs::write(&path, desktop_entry(target, name, icon)).map_err(|e| e.to_string())?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let _ = std::fs::set_permissions(&path, std::fs::Permissions::from_mode(0o755));
    }
    Ok(path)
}

/// A freedesktop.org launcher file.
pub fn desktop_entry(target: &Target, name: &str, icon: Option<&Path>) -> String {
    let value = |s: &str| s.replace('\\', "\\\\").replace('\n', "\\n").replace('\r', "\\r").replace('\t', "\\t");
    let quote = |s: &str| {
        let escaped = s.replace('\\', "\\\\").replace('"', "\\\"").replace('$', "\\$").replace('`', "\\`");
        format!("\"{escaped}\"").replace('%', "%%")
    };
    let (program, args, env) = target.command();
    let mut exec: Vec<String> = Vec::new();
    if !env.is_empty() {
        exec.push("env".into());
        let plain = |s: &str| s.chars().all(|c| c.is_ascii_alphanumeric() || "_=./:,+-".contains(c));
        exec.extend(env.iter().map(|(k, v)| format!("{k}={v}")).map(|w| if plain(&w) { w } else { quote(&w) }));
    }
    exec.push(quote(&program.to_string_lossy()));
    exec.extend(args.iter().map(|a| quote(a)));
    format!(
        "[Desktop Entry]\nType=Application\nName={}\nExec={}\nPath={}\nIcon={}\nTerminal=false\nCategories=Game;\nComment=Play {}\n",
        value(name),
        value(&exec.join(" ")),
        value(&target.dir.to_string_lossy()),
        value(&icon.map(|p| p.to_string_lossy().into_owned()).unwrap_or_default()),
        value(name),
    )
}

/// Quotes one command-line argument the way Windows programs read them back.
#[cfg_attr(not(windows), allow(dead_code))]
pub fn windows_quote(arg: &str) -> String {
    if !arg.is_empty() && !arg.contains([' ', '\t', '"']) {
        return arg.into();
    }
    let mut out = String::from("\"");
    let mut slashes = 0;
    for c in arg.chars() {
        match c {
            '\\' => slashes += 1,
            '"' => {
                out.push_str(&"\\".repeat(slashes * 2 + 1));
                out.push('"');
                slashes = 0;
            }
            _ => {
                out.push_str(&"\\".repeat(slashes));
                out.push(c);
                slashes = 0;
            }
        }
    }
    out.push_str(&"\\".repeat(slashes * 2));
    out.push('"');
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn desktop_entry_quotes_paths_for_exec() {
        let target = Target {
            exe: "/games/My $Game/run.AppImage".into(),
            program: "/games/My $Game/run.AppImage".into(),
            args: vec![],
            env: vec![],
            dir: "/games/My $Game".into(),
            extract_appimage: true,
            steam_proton: false,
            options: Default::default(),
        };
        let entry = desktop_entry(&target, "My Game", None);
        assert!(entry.contains("Exec=env APPIMAGE_EXTRACT_AND_RUN=1 \"/games/My \\\\$Game/run.AppImage\"\n"), "{entry}");
        assert_eq!(file_name("Zelda: Majora's Mask?"), "Zelda  Majora's Mask");
        assert_eq!(windows_quote(r#"C:\My Games\a"b\"#), r#""C:\My Games\a\"b\\""#);
    }

    #[test]
    fn launch_options_wrap_the_game_and_set_its_environment() {
        let target = Target {
            exe: "/games/G/game.exe".into(),
            program: "wine".into(),
            args: vec!["/games/G/game.exe".into()],
            env: vec![],
            dir: "/games/G".into(),
            extract_appimage: false,
            steam_proton: true,
            options: crate::launch::parse("DXVK_HUD=1 gamemoderun %command% --windowed").unwrap(),
        };
        let (program, args, env) = target.command();
        assert_eq!(program, Path::new("gamemoderun"));
        assert_eq!(args, ["wine", "/games/G/game.exe", "--windowed"]);
        assert_eq!(env, [("DXVK_HUD".to_string(), "1".to_string())]);
        let entry = desktop_entry(&target, "G", None);
        assert!(entry.contains("Exec=env DXVK_HUD=1 \"gamemoderun\" \"wine\" \"/games/G/game.exe\" \"--windowed\"\n"), "{entry}");
    }
}
