//! Save backups: copies of an app's saves kept on this computer, so a bad update, a broken save or a mistake can be
//! undone. Where an app's saves are comes, best first, from the folder the player chose, the places the catalog lists
//! (`launcher.saves`), or the places the launcher saw the app write to while it ran (`Watch`). With none of those
//! there's nothing to back up yet: the app's whole folder is never guessed at, as it holds the game itself.
//!
//! A place is written as a template, `<root>/<path>` (`localData/Game/saves`, `app/save/*.sav`), not this computer's
//! own path, so a backup says where its files go on any computer. A folder the player chose outside every root is
//! `abs:<path>`, which only means something here.
//!
//! A backup is a zip in `<data>/saves/<app>/<time>.zip`: the files under `<n>/<path>` for the n-th place, and
//! `quiver-save.json` (`Manifest`) naming the places, each file's size and checksum, and one checksum for the whole
//! set. The newest `KEEP` are kept. Putting one back first backs up what's there, then makes the places hold exactly
//! what the backup held. Only saves go in or come out: never programs, the game's files, archives, logs or caches.
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::fs;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

/// The newest backups kept, per app.
pub const KEEP: usize = 10;
/// Nothing bigger is a save: it's a disc image, a video or a pack of the game's files.
const MAX_FILE: u64 = 64 * 1024 * 1024;
/// No place holds more than this in saves.
const MAX_PLACE: u64 = 256 * 1024 * 1024;
/// More save files than this in a place means it isn't one.
const MAX_FILES: usize = 5_000;
/// Files and folders looked at in a place before it counts as too big to be only saves.
const MAX_WALK: usize = 50_000;
/// A folder in an app's own folder is taken whole as a save place only when it's this small.
const SMALL_FOLDER: u64 = 32 * 1024 * 1024;
/// Places kept from watching an app.
const MAX_FOUND: usize = 8;
const MANIFEST: &str = "quiver-save.json";

/// The player sees these as they are (docs/specs/save-backups.md on the website).
pub const TOO_BIG: &str = "This folder is too big to be only saves. Choose the folder the app saves in.";
pub const NO_SAVES: &str = "No saves found yet. Play the app, or choose where it saves.";
pub const DAMAGED: &str = "This backup is damaged and can't be restored.";
const FAILED: &str = "Couldn't back up your saves. Check the disk has space and try again.";

/// Never a save: programs, the game's own files, archives, logs, and kinds of file no game saves as: sound, video,
/// fonts, textures, 3D models, shader programs, installers, documents and debug files. Pictures can be: some games keep a
/// small one with each save.
const NOT_SAVES: &[&str] = &[
    // Programs and libraries.
    "exe", "dll", "so", "dylib", "msi", "apk", "appimage", "lib", "a", "o", "ilk", "exp", "pyc", "pyo", "node", "class",
    // The game's own files.
    "iso", "cue", "chd", "cso", "gcm", "rvz", "wbfs", "wad", "nsp", "xci", "xex", "xbe", "z64", "n64", "v64", "sfc", "smc", "gba", "gbc", "nds", "3ds", "elf", "rpx",
    "otr", "o2r", "pak",
    // Archives and installers.
    "zip", "7z", "rar", "gz", "xz", "deb", "rpm", "dmg", "pkg", "msix", "appx", "flatpak", "snap",
    // Logs, debugging and leftovers.
    "pdb", "log", "tmp", "dmp", "mdmp", "lock", "pid",
    // Sound and video.
    "wav", "mp3", "ogg", "flac", "opus", "m4a", "aac", "wma", "mid", "midi", "mp4", "mkv", "webm", "avi", "mov", "wmv", "flv", "m4v", "bik", "bk2", "usm",
    // Fonts, textures and 3D models.
    "ttf", "otf", "woff", "woff2", "dds", "ktx", "ktx2", "astc", "pvr", "basis", "fbx", "obj", "mtl", "glb", "gltf", "blend", "dae",
    // Shader programs.
    "spv", "dxbc", "dxil", "metallib", "glsl", "hlsl", "frag", "vert",
    // Documents.
    "pdf", "md", "htm", "html", "chm", "rtf", "doc", "docx",
];
/// Pictures: a save can keep one, but a backup that's mostly pictures is more likely screenshots or textures.
const PICTURES: [&str; 9] = ["png", "jpg", "jpeg", "bmp", "gif", "webp", "tga", "tif", "tiff"];
/// Folders that never hold saves.
const NOT_SAVE_DIRS: [&str; 18] = [
    "mods", "shadercache", "shader_cache", "shaders", "cache", "caches", "logs", "log", "crashdumps", "crashes", "dumps", "screenshots", "texture_packs", "textures", "temp", "tmp", ".git",
    "__pycache__",
];
/// Files a system runs by themselves or on a double click, or an app starts with: scripts, shortcuts and settings a
/// system loads. No app keeps its saves as these, so they're never backed up, removed or put back: a backup that
/// came from somewhere else can't swap an app's start script, or leave something to be run later.
const SCRIPTS: [&str; 29] = [
    "bat", "cmd", "ps1", "psm1", "psd1", "vbs", "vbe", "jse", "wsf", "wsh", "hta", "lnk", "scr", "com", "pif", "cpl", "reg", "sh", "bash", "zsh", "fish", "command", "desktop", "service", "jar", "url",
    "inf", "py", "pyw",
];
/// Places in one backup: more than the catalog lists for an app or the launcher finds.
const MAX_PLACES: usize = 16;
const PROGRAMS: [&str; 6] = ["exe", "dll", "so", "dylib", "appimage", "msi"];
const ROOTS: [&str; 6] = ["app", "data", "config", "localData", "documents", "home"];

/// Where each root is on this computer for one app. `None`: not known here (a Windows app whose Wine prefix doesn't
/// exist yet).
#[derive(Debug, Clone, Default)]
pub struct Roots {
    pub app: PathBuf,
    pub data: Option<PathBuf>,
    pub config: Option<PathBuf>,
    pub local_data: Option<PathBuf>,
    pub documents: Option<PathBuf>,
    pub home: Option<PathBuf>,
}

impl Roots {
    fn of(&self, root: &str) -> Option<PathBuf> {
        match root {
            "app" => Some(self.app.clone()),
            "data" => self.data.clone(),
            "config" => self.config.clone(),
            "localData" => self.local_data.clone(),
            "documents" => self.documents.clone(),
            "home" => self.home.clone(),
            _ => None,
        }
    }

    /// A Windows user's folders, as they are under its folder in a Wine prefix.
    pub fn windows_user(app: PathBuf, user: &Path) -> Self {
        Roots {
            app,
            data: Some(user.join("AppData").join("Roaming")),
            config: Some(user.join("AppData").join("Roaming")),
            local_data: Some(user.join("AppData").join("Local")),
            documents: Some(user.join("Documents")),
            home: Some(user.to_path_buf()),
        }
    }
}

/// One place the catalog lists (`launcher.saves.locations`).
#[derive(Debug, Clone, Deserialize)]
pub struct Location {
    pub root: String,
    pub path: String,
    #[serde(default)]
    pub os: Option<Vec<String>>,
}

/// The catalog's places for this system, as templates. One that isn't a path Quiver would follow is left out.
pub fn catalog_templates(locations: &[Location], os: &str) -> Vec<String> {
    locations
        .iter()
        .filter(|l| l.os.as_ref().is_none_or(|list| list.iter().any(|o| o == os)))
        .map(|l| format!("{}/{}", l.root, l.path))
        .filter(|t| valid_template(t))
        .collect()
}

/// A name that means the same on every system. Windows drops a dot or a space from the end of one, so "Startup." there
/// is "Startup": such a name is never followed, as it would get past every check made on how it's written.
fn valid_part(part: &str) -> bool {
    !part.is_empty() && part != "." && part != ".." && !part.ends_with(['.', ' ']) && !part.starts_with(' ') && !part.chars().any(|c| c.is_control() || "<>:\"|?\\".contains(c))
}

/// Whether a folder's name could be Windows's short name for another ("MICROS~1" for "Microsoft"), which would get
/// past the folders refused by name.
fn short_name(part: &str) -> bool {
    part.as_bytes().windows(2).any(|pair| pair[0] == b'~' && pair[1].is_ascii_digit())
}

/// Folders no app's saves are in, and that hold what must never be copied into a backup or written over by a restore:
/// keys and passwords, other programs' own data (browsers, mail, chat, wallets) and the places a system starts
/// programs from. The catalog refuses the same ones (convex/saveLocations.ts on the website), and they're refused
/// here too so that a place listed there by mistake, or a backup made elsewhere, can't reach them. Lowercase.
const NEVER_ANYWHERE: [&str; 7] = [".ssh", ".gnupg", "startup", "autostart", "keychains", "windowspowershell", "powershell"];
const NEVER_IN_HOME: [&str; 35] = [
    ".aws", ".azure", ".kube", ".docker", ".mozilla", ".thunderbird", ".password-store", ".pki", ".cert", ".putty", ".netrc", ".git-credentials", ".gitconfig", ".npmrc", ".profile", ".bashrc",
    ".bash_profile", ".bash_history", ".zshrc", ".zsh_history", ".electrum", ".bitcoin", ".ethereum", "applications", ".zshenv", ".zprofile", ".zlogin", ".zlogout", ".bash_login", ".bash_logout", ".xprofile", ".xinitrc", ".xsession", ".xsessionrc",
    ".pam_environment",
];
const NEVER_IN_APP_DATA: [&str; 46] = [
    "google", "google-chrome", "chromium", "mozilla", "firefox", "thunderbird", "microsoft", "microsoft edge", "bravesoftware", "opera software", "vivaldi", "apple", "apple computer", "packages",
    "programs", "temp", "bitwarden", "1password", "keepass", "keepassxc", "keyrings", "kwalletd", "gnupg", "ssh", "signal", "discord", "telegram desktop", "slack", "code", "git", "gh", "gcloud",
    "rclone", "systemd", "dconf", "exodus", "electrum", "bitcoin", "ethereum", "ledger live", "fish", "zsh", "nushell", "environment.d", "applications", "plasma-workspace",
];
/// Folders every program or the player shares: a place can be inside one, never the whole of it.
const SHARED: [&str; 12] = ["documents", "desktop", "downloads", "pictures", "music", "videos", "onedrive", "dropbox", "google drive", "iclouddrive", "my games", "saved games"];
/// Where programs keep their data under the home folder. A place there is held to the rules for app data, and
/// anywhere else under these folders' first parts is refused.
const APP_DATA_IN_HOME: [&[&str]; 11] = [
    &[".config"],
    &[".cache"],
    &[".local", "share"],
    &[".local", "state"],
    &["appdata", "local"],
    &["appdata", "locallow"],
    &["appdata", "roaming"],
    &["library", "application support"],
    &["library", "caches"],
    &["library", "containers"],
    &["library", "preferences"],
];

/// Why a place is never one app's saves.
#[derive(Debug, Clone, Copy, PartialEq)]
enum Refused {
    /// It holds keys or another program's own data.
    Private,
    /// It's a folder every app shares.
    Shared,
}

fn refused(root: &str, path: &str) -> Option<Refused> {
    let lower = path.to_lowercase();
    let parts: Vec<&str> = lower.split('/').collect();
    let other_programs = |folder: &str| NEVER_IN_APP_DATA.contains(&folder) || folder.starts_with("com.apple.");
    if parts.iter().any(|p| NEVER_ANYWHERE.contains(p)) {
        return Some(Refused::Private);
    }
    if root == "app" {
        return None;
    }
    if parts.len() == 1 && SHARED.contains(&parts[0]) {
        return Some(Refused::Shared);
    }
    if root == "home" {
        if NEVER_IN_HOME.contains(&parts[0]) {
            return Some(Refused::Private);
        }
        if APP_DATA_IN_HOME.iter().any(|prefix| prefix[0] == parts[0]) {
            let Some(prefix) = APP_DATA_IN_HOME.iter().find(|prefix| parts.starts_with(prefix)) else { return Some(Refused::Shared) };
            return match parts.get(prefix.len()) {
                None => Some(Refused::Shared),
                Some(folder) if other_programs(folder) => Some(Refused::Private),
                Some(_) => None,
            };
        }
    } else if root != "documents" && other_programs(parts[0]) {
        return Some(Refused::Private);
    }
    None
}

/// Whether a template is one of the roots and a plain path under it: no `..`, no drive, one `*` at most and only in
/// the last part, and not a folder that holds keys or other programs' data. `app` alone is the app's whole folder,
/// which only the player can choose.
pub fn valid_template(template: &str) -> bool {
    let (root, path) = template.split_once('/').unwrap_or((template, ""));
    if !ROOTS.contains(&root) {
        return false;
    }
    if path.is_empty() {
        return root == "app" && !template.contains('/');
    }
    let parts: Vec<&str> = path.split('/').collect();
    let last = parts[parts.len() - 1];
    refused(root, path).is_none() && path.len() <= 200 && parts.iter().all(|p| valid_part(p) && !short_name(p)) && parts[..parts.len() - 1].iter().all(|p| !p.contains('*')) && last.matches('*').count() <= 1 && last != "*"
}

const PRIVATE: &str = "Quiver doesn't back up that folder: it holds keys or another program's own data.";
const SHARED_FOLDER: &str = "Choose the folder this app saves in, not a folder every app shares.";

/// A template on this computer.
#[derive(Debug, Clone, PartialEq)]
pub struct Place {
    /// The folder or file; for a pattern, the folder its files are in.
    pub base: PathBuf,
    /// `*.sav`: only files in `base` whose names match.
    pub pattern: Option<String>,
}

/// `abs`: the folders the player chose outside every root, the only absolute paths followed.
pub fn resolve(template: &str, roots: &Roots, abs: &[String]) -> Result<Place, String> {
    if let Some(path) = template.strip_prefix("abs:") {
        if !abs.iter().any(|a| a == template) || !Path::new(path).is_absolute() {
            return Err("This backup's saves were in a folder Quiver doesn't know on this computer.".into());
        }
        return Ok(Place { base: PathBuf::from(path), pattern: None });
    }
    if !valid_template(template) {
        return Err(format!("{template} isn't a place Quiver keeps saves in."));
    }
    let (root, path) = template.split_once('/').unwrap_or((template, ""));
    let mut base = roots.of(root).ok_or_else(|| format!("Quiver doesn't know where {root} is for this app on this computer."))?;
    let mut pattern = None;
    for part in path.split('/').filter(|p| !p.is_empty()) {
        if part.contains('*') {
            pattern = Some(part.to_string());
        } else {
            base.push(part);
        }
    }
    Ok(Place { base, pattern })
}

/// This computer's folder as a template: under the app's folder or the deepest root that holds it, else `abs:`.
/// A root itself (all of Documents, the home folder) is refused: it's never just one app's saves.
pub fn templatize(path: &Path, roots: &Roots) -> Result<String, String> {
    if !path.is_absolute() {
        return Err("Choose a folder on this computer.".into());
    }
    let rel = |base: &Path| path.strip_prefix(base).ok().map(|r| r.components().map(|c| c.as_os_str().to_string_lossy().into_owned()).collect::<Vec<_>>());
    if let Some(parts) = rel(&roots.app) {
        let template = if parts.is_empty() { "app".to_string() } else { format!("app/{}", parts.join("/")) };
        if refused("app", &parts.join("/")).is_some() {
            return Err(PRIVATE.into());
        }
        return if valid_template(&template) { Ok(template) } else { Ok(format!("abs:{}", path.display())) };
    }
    let mut best: Option<(usize, String)> = None;
    let mut why: Option<Refused> = None;
    for root in ["localData", "data", "config", "documents", "home"] {
        let Some(base) = roots.of(root) else { continue };
        let Some(parts) = rel(&base) else { continue };
        if parts.is_empty() {
            return Err(SHARED_FOLDER.into());
        }
        let depth = base.components().count();
        let template = format!("{root}/{}", parts.join("/"));
        if let Some(refusal) = refused(root, &parts.join("/")) {
            // Keys in one reading of the path outweigh "shared" in another.
            if why != Some(Refused::Private) {
                why = Some(refusal);
            }
        } else if valid_template(&template) && best.as_ref().is_none_or(|(d, _)| depth > *d) {
            best = Some((depth, template));
        }
    }
    match (best, why) {
        (Some((_, template)), _) => Ok(template),
        (None, Some(Refused::Private)) => Err(PRIVATE.into()),
        (None, Some(Refused::Shared)) => Err(SHARED_FOLDER.into()),
        (None, None) => Ok(format!("abs:{}", path.display())),
    }
}

fn extension(name: &str) -> String {
    Path::new(name).extension().map(|e| e.to_string_lossy().to_ascii_lowercase()).unwrap_or_default()
}

fn is_save_file(name: &str, size: u64) -> bool {
    size <= MAX_FILE && !NOT_SAVES.contains(&extension(name).as_str()) && !name.ends_with(".quiver-tmp")
}

fn skip_dir(name: &str) -> bool {
    NOT_SAVE_DIRS.contains(&name.to_ascii_lowercase().as_str())
}

/// `*.sav` against a file name, without case.
fn matches(pattern: &str, name: &str) -> bool {
    let (pattern, name) = (pattern.to_lowercase(), name.to_lowercase());
    match pattern.split_once('*') {
        Some((before, after)) => name.len() >= before.len() + after.len() && name.starts_with(before) && name.ends_with(after),
        None => pattern == name,
    }
}

/// Whether a file is one a restore or a backup leaves alone: a script or a shortcut.
fn left_alone(name: &str) -> bool {
    SCRIPTS.contains(&extension(name).as_str())
}

/// One save file: which place it's in, its path inside it with `/`, and where it is.
#[derive(Debug, Clone)]
pub struct Found {
    pub place: usize,
    pub path: String,
    pub file: PathBuf,
    pub size: u64,
}

/// A place too big to be only saves.
#[derive(Debug, PartialEq)]
pub struct TooBig;

/// Every save file under a folder. Links aren't followed, so one that leads back into itself can't loop.
fn walk(root: &Path, place: usize) -> Result<Vec<Found>, TooBig> {
    let (mut out, mut looked, mut total) = (vec![], 0usize, 0u64);
    let mut stack = vec![(root.to_path_buf(), String::new())];
    while let Some((dir, prefix)) = stack.pop() {
        let Ok(entries) = fs::read_dir(&dir) else { continue };
        for entry in entries.flatten() {
            looked += 1;
            if looked > MAX_WALK {
                return Err(TooBig);
            }
            let Ok(meta) = entry.metadata() else { continue };
            let name = entry.file_name().to_string_lossy().into_owned();
            if meta.file_type().is_symlink() || !valid_part(&name) {
                continue;
            }
            let path = if prefix.is_empty() { name.clone() } else { format!("{prefix}/{name}") };
            if meta.is_dir() {
                if !skip_dir(&name) {
                    stack.push((entry.path(), path));
                }
            } else if meta.is_file() && is_save_file(&name, meta.len()) {
                total += meta.len();
                out.push(Found { place, path, file: entry.path(), size: meta.len() });
                if out.len() > MAX_FILES || total > MAX_PLACE {
                    return Err(TooBig);
                }
            }
        }
    }
    out.sort_by(|a, b| a.path.cmp(&b.path));
    Ok(out)
}

/// How a place was when it was backed up, which is how it's put back.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind {
    Folder,
    File,
    Pattern,
}

fn kind_of(place: &Place) -> Kind {
    match &place.pattern {
        Some(_) => Kind::Pattern,
        None if place.base.is_file() => Kind::File,
        None => Kind::Folder,
    }
}

/// The save files in one place, as it is now. One that isn't there has none.
fn files_in(place: &Place, kind: Kind, index: usize) -> Result<Vec<Found>, TooBig> {
    let mut files = all_files_in(place, kind, index)?;
    files.retain(|f| !left_alone(f.path.rsplit('/').next().unwrap_or(&f.path)));
    Ok(files)
}

fn all_files_in(place: &Place, kind: Kind, index: usize) -> Result<Vec<Found>, TooBig> {
    let one = |file: &Path| {
        let meta = fs::symlink_metadata(file).ok().filter(|m| m.is_file())?;
        let name = file.file_name()?.to_string_lossy().into_owned();
        (valid_part(&name) && is_save_file(&name, meta.len())).then(|| Found { place: index, path: name, file: file.to_path_buf(), size: meta.len() })
    };
    match kind {
        Kind::File => Ok(one(&place.base).into_iter().collect()),
        Kind::Pattern => {
            let pattern = place.pattern.as_deref().unwrap_or("*");
            let mut out: Vec<Found> = fs::read_dir(&place.base).into_iter().flatten().flatten().filter_map(|e| one(&e.path())).filter(|f| matches(pattern, &f.path)).collect();
            if out.len() > MAX_FILES || out.iter().map(|f| f.size).sum::<u64>() > MAX_PLACE {
                return Err(TooBig);
            }
            out.sort_by(|a, b| a.path.cmp(&b.path));
            Ok(out)
        }
        Kind::Folder => walk(&place.base, index),
    }
}

fn sha_file(path: &Path) -> io::Result<String> {
    let mut file = fs::File::open(path)?;
    let mut sha = Sha256::new();
    io::copy(&mut file, &mut sha)?;
    Ok(hex::encode(sha.finalize()))
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct FileInfo {
    pub place: usize,
    pub path: String,
    pub size: u64,
    pub sha256: String,
}

/// The checksum of a set of saves: the same files with the same contents in the same places, the same checksum.
pub fn set_hash(files: &[FileInfo]) -> String {
    let mut lines: Vec<String> = files.iter().map(|f| format!("{}\0{}\0{}\n", f.place, f.path, f.sha256)).collect();
    lines.sort();
    let mut sha = Sha256::new();
    lines.iter().for_each(|l| sha.update(l));
    hex::encode(sha.finalize())
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct PlaceInfo {
    pub template: String,
    pub kind: Kind,
}

/// What `quiver-save.json` in a backup says.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Manifest {
    pub v: u32,
    pub app: String,
    /// When it was made, in milliseconds.
    pub at: u64,
    /// The app's version then, if it was installed from the catalog.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub version: Option<String>,
    /// "played", "manual", "update", "uninstall" or "restore" (what was there before one was put back).
    pub reason: String,
    pub device: String,
    pub os: String,
    pub places: Vec<PlaceInfo>,
    pub files: Vec<FileInfo>,
    pub hash: String,
}

/// A backup, as the Saves tab lists it.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Backup {
    pub name: String,
    pub at: u64,
    /// The zip's size.
    pub size: u64,
    pub files: usize,
    pub version: Option<String>,
    pub reason: String,
    pub device: String,
    /// The system its saves are for: "windows" for a Windows app run through Wine.
    pub os: String,
    pub hash: String,
}

/// What a backup is stamped with.
pub struct Stamp<'a> {
    pub app: &'a str,
    pub version: Option<&'a str>,
    pub reason: &'a str,
    pub os: &'a str,
}

/// This computer's name, so backups from several can be told apart later.
pub fn device_name() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .ok()
        .or_else(|| fs::read_to_string("/etc/hostname").ok())
        .map(|n| n.trim().to_string())
        .filter(|n| !n.is_empty())
        .unwrap_or_else(|| "This computer".into())
}

/// A folder name for an app's id (`github:owner/repo` for one the player added), safe on every system.
pub fn store_name(id: &str) -> String {
    let plain: String = id.chars().map(|c| if c.is_ascii_alphanumeric() || c == '-' || c == '_' { c } else { '_' }).take(60).collect();
    if plain == id && !plain.is_empty() {
        return plain;
    }
    format!("{plain}-{}", &hex::encode(Sha256::digest(id.as_bytes()))[..8])
}

fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(0)
}

pub(crate) fn read_manifest(zip_file: &Path) -> Option<Manifest> {
    let mut archive = zip::ZipArchive::new(fs::File::open(zip_file).ok()?).ok()?;
    let mut text = String::new();
    archive.by_name(MANIFEST).ok()?.take(8 * 1024 * 1024).read_to_string(&mut text).ok()?;
    serde_json::from_str(&text).ok()
}

fn backup_file(store: &Path, name: &str) -> Result<PathBuf, String> {
    let ok = name.strip_suffix(".zip").is_some_and(|stem| !stem.is_empty() && stem.chars().all(|c| c.is_ascii_digit()));
    if !ok {
        return Err("That isn't one of this app's backups.".into());
    }
    Ok(store.join(name))
}

/// An app's backups, newest first. A zip that can't be read isn't listed.
pub fn list(store: &Path) -> Vec<Backup> {
    let mut out: Vec<Backup> = fs::read_dir(store)
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().into_owned();
            let path = backup_file(store, &name).ok()?;
            let m = read_manifest(&path)?;
            Some(Backup { name, at: m.at, size: e.metadata().ok()?.len(), files: m.files.len(), version: m.version, reason: m.reason, device: m.device, os: m.os, hash: m.hash })
        })
        .collect();
    out.sort_by(|a, b| b.at.cmp(&a.at).then_with(|| b.name.cmp(&a.name)));
    out
}

/// The saves as they are now: the places, their files and each file's checksum.
fn current(templates: &[String], roots: &Roots, abs: &[String], kinds: Option<&[Kind]>) -> Result<(Vec<PlaceInfo>, Vec<Found>, Vec<FileInfo>), String> {
    let (mut places, mut found) = (vec![], vec![]);
    for (index, template) in templates.iter().enumerate() {
        // One that can't be worked out here (a Wine prefix that isn't made yet) has nothing in it yet.
        let (kind, files) = match resolve(template, roots, abs) {
            Ok(place) => {
                let kind = kinds.and_then(|k| k.get(index).copied()).unwrap_or_else(|| kind_of(&place));
                (kind, files_in(&place, kind, index).map_err(|_| TOO_BIG.to_string())?)
            }
            Err(_) => (Kind::Folder, vec![]),
        };
        places.push(PlaceInfo { template: template.clone(), kind });
        found.extend(files);
    }
    let mut infos = Vec::with_capacity(found.len());
    for f in &found {
        let sha256 = sha_file(&f.file).map_err(|_| FAILED.to_string())?;
        infos.push(FileInfo { place: f.place, path: f.path.clone(), size: f.size, sha256 });
    }
    Ok((places, found, infos))
}

fn write_zip(target: &Path, manifest: &Manifest, found: &[Found]) -> io::Result<()> {
    let mut zip = zip::ZipWriter::new(fs::File::create(target)?);
    let options = zip::write::SimpleFileOptions::default().compression_method(zip::CompressionMethod::Deflated).large_file(false);
    zip.start_file(MANIFEST, options)?;
    zip.write_all(&serde_json::to_vec_pretty(manifest).map_err(io::Error::other)?)?;
    for f in found {
        zip.start_file(format!("{}/{}", f.place, f.path), options)?;
        io::copy(&mut fs::File::open(&f.file)?, &mut zip)?;
    }
    zip.finish()?.sync_all()
}

/// Backs up the saves as they are now. `None` when there are none, or nothing changed since the newest backup.
pub fn backup(store: &Path, templates: &[String], roots: &Roots, abs: &[String], stamp: &Stamp) -> Result<Option<Backup>, String> {
    backup_as(store, templates, roots, abs, stamp, None)
}

fn backup_as(store: &Path, templates: &[String], roots: &Roots, abs: &[String], stamp: &Stamp, kinds: Option<&[Kind]>) -> Result<Option<Backup>, String> {
    let key = places_key(templates);
    let (places, found, files) = match current(templates, roots, abs, kinds) {
        Err(e) if e == TOO_BIG => {
            let mut state = state(store);
            if state.too_big.as_deref() != Some(key.as_str()) {
                state.too_big = Some(key);
                let _ = set_state(store, &state);
            }
            return Err(e);
        }
        other => other?,
    };
    if state(store).too_big.as_deref() == Some(key.as_str()) {
        let mut state = state(store);
        state.too_big = None;
        let _ = set_state(store, &state);
    }
    if files.is_empty() {
        return Ok(None);
    }
    let hash = set_hash(&files);
    let before = list(store);
    if before.first().is_some_and(|b| b.hash == hash) {
        return Ok(None);
    }
    fs::create_dir_all(store).map_err(|_| FAILED.to_string())?;
    // Never the name of one that's there: two in the same millisecond, or a clock set back.
    let mut at = now();
    while store.join(format!("{at}.zip")).exists() {
        at += 1;
    }
    let manifest = Manifest {
        v: 1,
        app: stamp.app.to_string(),
        at,
        version: stamp.version.map(str::to_string),
        reason: stamp.reason.to_string(),
        device: device_name(),
        os: stamp.os.to_string(),
        places,
        files,
        hash: hash.clone(),
    };
    let name = format!("{at}.zip");
    let (temp, target) = (store.join(format!("{at}.zip.part")), store.join(&name));
    // Whole, or not there: a backup cut short by a full disk would look like a good one.
    if write_zip(&temp, &manifest, &found).and_then(|_| fs::rename(&temp, &target)).is_err() {
        let _ = fs::remove_file(&temp);
        return Err(FAILED.into());
    }
    let size = fs::metadata(&target).map(|m| m.len()).unwrap_or(0);
    for old in before.iter().skip(KEEP - 1) {
        let _ = fs::remove_file(store.join(&old.name));
    }
    Ok(Some(Backup { name, at, size, files: manifest.files.len(), version: manifest.version, reason: manifest.reason, device: manifest.device, os: manifest.os, hash }))
}

/// The checksum of the saves as they are now, as a backup of them would carry it. `None` when there are none.
pub fn current_hash(templates: &[String], roots: &Roots, abs: &[String]) -> Result<Option<String>, String> {
    let (_, _, files) = current(templates, roots, abs, None)?;
    Ok((!files.is_empty()).then(|| set_hash(&files)))
}

/// A backup's file, to send to the account.
pub fn file_of(store: &Path, name: &str) -> Result<PathBuf, String> {
    let file = backup_file(store, name)?;
    read_manifest(&file).map(|_| file).ok_or_else(|| DAMAGED.to_string())
}

pub const BAD_COPY: &str = "That copy couldn't be downloaded whole. Your saves are as they were.";

/// Takes a backup downloaded from the account in among this computer's, where Restore can put it back. It must be the
/// copy that was asked for: a backup whose files add up to `hash`. One that's here already isn't kept twice.
pub fn adopt(store: &Path, downloaded: &Path, hash: &str) -> Result<Backup, String> {
    let manifest = read_manifest(downloaded).filter(|m| m.hash == hash && set_hash(&m.files) == hash && sized_like_saves(m));
    let Some(manifest) = manifest else {
        let _ = fs::remove_file(downloaded);
        return Err(BAD_COPY.into());
    };
    let before = list(store);
    if let Some(same) = before.iter().find(|b| b.hash == hash) {
        let _ = fs::remove_file(downloaded);
        return Ok(same.clone());
    }
    let mut at = now();
    while store.join(format!("{at}.zip")).exists() {
        at += 1;
    }
    let name = format!("{at}.zip");
    fs::rename(downloaded, store.join(&name)).map_err(|_| FAILED.to_string())?;
    // It may be older than every backup here by its own clock, and still mustn't be the one that goes.
    for old in before.iter().skip(KEEP - 1) {
        let _ = fs::remove_file(store.join(&old.name));
    }
    let size = fs::metadata(store.join(&name)).map(|m| m.len()).unwrap_or(0);
    Ok(Backup { name, at: manifest.at, size, files: manifest.files.len(), version: manifest.version, reason: manifest.reason, device: manifest.device, os: manifest.os, hash: manifest.hash })
}

/// A backup that is whole, from wherever it came: its manifest adds up, it holds no more than saves do, and every
/// file it names is in it with the size and checksum it says. `None` for one cut short, still being written by
/// something else, or changed since it was made. Nothing is unpacked to find out.
pub fn verify(zip_file: &Path) -> Option<Manifest> {
    let manifest = read_manifest(zip_file).filter(|m| sized_like_saves(m) && set_hash(&m.files) == m.hash)?;
    let mut archive = zip::ZipArchive::new(fs::File::open(zip_file).ok()?).ok()?;
    for info in &manifest.files {
        let mut entry = archive.by_name(&format!("{}/{}", info.place, info.path)).ok()?;
        let mut sha = Sha256::new();
        // No more than it says it holds: a zip can claim one size and unpack to another.
        let read = io::copy(&mut (&mut entry).take(info.size + 1), &mut sha).ok()?;
        if read != info.size || hex::encode(sha.finalize()) != info.sha256 {
            return None;
        }
    }
    Some(manifest)
}

/// Whether a backup holds no more than saves do: a zip can name any number of files of any size, and unpacking
/// them would fill the disk before anything was found wrong.
pub(crate) fn sized_like_saves(manifest: &Manifest) -> bool {
    let mut places = vec![(0usize, 0u64); manifest.places.len()];
    for file in &manifest.files {
        let Some(place) = places.get_mut(file.place) else { return false };
        *place = (place.0 + 1, place.1.saturating_add(file.size));
    }
    manifest.places.len() <= MAX_PLACES && places.iter().all(|(files, bytes)| *files <= MAX_FILES && *bytes <= MAX_PLACE)
}

/// Where a restore may write. A backup says where its saves were, but it may have been made on another computer, or
/// by someone else with the player's account: its word is only taken for places this computer keeps the app's saves
/// in already. Any other is the player's to agree to, shown as the folder it is here, and only when it's in the
/// app's own folder or a folder named like the app, which is where apps save.
#[derive(Default)]
pub struct Trust<'a> {
    /// The places this computer keeps the app's saves in: the player's own, the catalog's or the ones it found.
    pub known: &'a [String],
    /// The backup's other places the player agreed to (`restore_plan` lists them).
    pub confirmed: &'a [String],
    /// What the app is called, to tell a folder named like it.
    pub names: &'a [String],
}

pub const WRONG_PLACE: &str = "This backup's saves were in a folder Quiver doesn't keep this app's saves in on this computer. Choose where the app saves first, then restore it.";
const NOT_AGREED: &str = "This backup's saves go in a folder Quiver hasn't used for this app on this computer. Restore it from the app's Saves tab.";

/// Whether a known place covers another: the same one, or a folder that holds it.
fn covers(known: &Place, place: &Place) -> bool {
    match &known.pattern {
        None => place.base.starts_with(&known.base),
        Some(pattern) => place.base == known.base && place.pattern.as_ref().is_some_and(|p| p.eq_ignore_ascii_case(pattern)),
    }
}

/// Whether a place can be offered to the player as somewhere to restore to: in the app's own folder, or in a folder
/// named like the app. Never the app's whole folder, which holds the game itself.
fn offerable(template: &str, names: &[String]) -> bool {
    let Some((root, path)) = template.split_once('/') else { return false };
    if !valid_template(template) {
        return false;
    }
    if root == "app" {
        return true;
    }
    let names: Vec<String> = names.iter().map(|n| plain(n)).filter(|n| n.len() >= 3).collect();
    path.split('/').filter(|part| !part.contains('*')).any(|part| named_like(part, &names))
}

/// A backup's places that aren't among the ones this computer knows for the app, each resolved here.
fn unknown_places(manifest: &Manifest, roots: &Roots, abs: &[String], trust: &Trust) -> Result<Vec<(String, Place)>, String> {
    let known: Vec<Place> = trust.known.iter().filter_map(|t| resolve(t, roots, abs).ok()).collect();
    let mut out = vec![];
    for info in &manifest.places {
        // A place that's refused by name is said the same way as any other the app doesn't save in.
        let place = resolve(&info.template, roots, abs).map_err(|e| if info.template.starts_with("abs:") || valid_template(&info.template) { e } else { WRONG_PLACE.to_string() })?;
        if known.iter().any(|k| covers(k, &place)) {
            continue;
        }
        if !offerable(&info.template, trust.names) {
            return Err(WRONG_PLACE.into());
        }
        out.push((info.template.clone(), place));
    }
    Ok(out)
}

/// The folders a restore of this backup would write to that this computer doesn't keep the app's saves in yet, for
/// the player to agree to (`Trust::confirmed`). None for a backup of the places known here. Fails when one of them
/// can't be offered at all.
pub fn restore_plan(store: &Path, name: &str, roots: &Roots, abs: &[String], trust: &Trust) -> Result<Vec<Shown>, String> {
    let manifest = read_manifest(&backup_file(store, name)?).ok_or(DAMAGED)?;
    let unknown = unknown_places(&manifest, roots, abs, trust)?;
    Ok(unknown
        .into_iter()
        .map(|(template, place)| {
            let path = place.pattern.as_ref().map_or(place.base.clone(), |p| place.base.join(p));
            Shown { template, path: path.to_string_lossy().into(), exists: place.base.exists() }
        })
        .collect())
}

pub fn remove(store: &Path, name: &str) -> Result<(), String> {
    fs::remove_file(backup_file(store, name)?).map_err(|e| format!("Couldn't remove that backup: {e}"))
}

fn put(from: &Path, to: &Path) -> io::Result<()> {
    if let Some(parent) = to.parent() {
        fs::create_dir_all(parent)?;
    }
    // Beside it first, so a save is never left half written.
    let mut name = to.file_name().unwrap_or_default().to_os_string();
    name.push(".quiver-tmp");
    let temp = to.with_file_name(name);
    fs::copy(from, &temp).and_then(|_| fs::rename(&temp, to)).inspect_err(|_| {
        let _ = fs::remove_file(&temp);
    })?;
    Ok(())
}

/// Puts a backup's files back where the app reads them. What's there now is backed up first, so this can be undone;
/// then save files that aren't in the backup are taken away, so the app sees exactly what was backed up. Resolves to
/// how many files it put back.
///
/// Files only go to places `trust` allows: the ones this computer knows for the app, and the backup's others the
/// player agreed to. A backup naming any other place isn't restored at all.
pub fn restore(store: &Path, name: &str, roots: &Roots, abs: &[String], stamp: &Stamp, trust: &Trust) -> Result<usize, String> {
    let zip_file = backup_file(store, name)?;
    let manifest = read_manifest(&zip_file).ok_or(DAMAGED)?;
    if unknown_places(&manifest, roots, abs, trust)?.iter().any(|(template, _)| !trust.confirmed.contains(template)) {
        return Err(NOT_AGREED.into());
    }
    let places = manifest.places.iter().map(|p| resolve(&p.template, roots, abs)).collect::<Result<Vec<_>, _>>()?;
    // Everything out and checked before anything of the player's is touched.
    let temp = store.join(".restore");
    let _ = fs::remove_dir_all(&temp);
    fs::create_dir_all(&temp).map_err(|e| format!("Couldn't restore this backup: {e}"))?;
    let unpacked = unpack(&zip_file, &manifest, &places, &temp);
    let done = unpacked.and_then(|files| {
        let templates: Vec<String> = manifest.places.iter().map(|p| p.template.clone()).collect();
        let kinds: Vec<Kind> = manifest.places.iter().map(|p| p.kind).collect();
        backup_as(store, &templates, roots, abs, &Stamp { reason: "restore", ..*stamp }, Some(&kinds))?;
        for (index, (place, info)) in places.iter().zip(&manifest.places).enumerate() {
            let keep: HashSet<String> = files.iter().filter(|f| f.0.place == index).map(|f| f.0.path.to_lowercase()).collect();
            for there in files_in(place, info.kind, index).map_err(|_| TOO_BIG.to_string())? {
                if !keep.contains(&there.path.to_lowercase()) {
                    fs::remove_file(&there.file).map_err(|e| format!("Couldn't restore this backup: {e}"))?;
                }
            }
        }
        for (info, from) in &files {
            let place = &places[info.place];
            let to = match manifest.places[info.place].kind {
                Kind::File => place.base.clone(),
                _ => info.path.split('/').fold(place.base.clone(), |p, part| p.join(part)),
            };
            put(from, &to).map_err(|e| format!("Couldn't restore this backup: {e}"))?;
        }
        Ok(files.len())
    });
    let _ = fs::remove_dir_all(&temp);
    done
}

/// A backup's files written under `temp`, each checked against the manifest. Nothing but saves comes out, whatever
/// the zip holds: its entries are only read by the names the manifest gives, and those are checked first.
fn unpack(zip_file: &Path, manifest: &Manifest, places: &[Place], temp: &Path) -> Result<Vec<(FileInfo, PathBuf)>, String> {
    if !sized_like_saves(manifest) || set_hash(&manifest.files) != manifest.hash {
        return Err(DAMAGED.into());
    }
    let mut archive = zip::ZipArchive::new(fs::File::open(zip_file).map_err(|_| DAMAGED)?).map_err(|_| DAMAGED)?;
    let mut seen = HashSet::new();
    let mut out = vec![];
    for (n, info) in manifest.files.iter().enumerate() {
        let parts: Vec<&str> = info.path.split('/').collect();
        let name = parts[parts.len() - 1];
        let place = places.get(info.place).ok_or(DAMAGED)?;
        // A backup made before these were left out may hold one: it stays in the backup.
        if left_alone(name) {
            continue;
        }
        let fits = match manifest.places[info.place].kind {
            Kind::Folder => !parts[..parts.len() - 1].iter().any(|p| skip_dir(p)),
            Kind::File => parts.len() == 1 && place.base.file_name().is_some_and(|f| f.to_string_lossy() == name),
            Kind::Pattern => parts.len() == 1 && place.pattern.as_deref().is_some_and(|p| matches(p, name)),
        };
        if !fits || !parts.iter().all(|p| valid_part(p)) || !is_save_file(name, info.size) || !seen.insert((info.place, info.path.to_lowercase())) {
            return Err(DAMAGED.into());
        }
        let mut entry = archive.by_name(&format!("{}/{}", info.place, info.path)).map_err(|_| DAMAGED)?;
        let target = temp.join(n.to_string());
        let mut file = fs::File::create(&target).map_err(|e| format!("Couldn't restore this backup: {e}"))?;
        // No more than it says it holds: a zip can claim one size and unpack to another.
        let copied = io::copy(&mut (&mut entry).take(info.size + 1), &mut file).map_err(|_| DAMAGED)?;
        drop(file);
        if copied != info.size || sha_file(&target).ok().as_deref() != Some(info.sha256.as_str()) {
            return Err(DAMAGED.into());
        }
        out.push((info.clone(), target));
    }
    Ok(out)
}

/// What the launcher remembers about an app's saves, in `state.json` beside its backups.
#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq)]
pub struct State {
    /// The folder the player chose, as templates. It wins over everything.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub chosen: Vec<String>,
    /// Places the launcher saw the app write saves to.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub found: Vec<String>,
    /// The found places the catalog was told about already (`to_report`), so each is sent once.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub reported: Vec<String>,
    /// The checksum of the saves when they last matched the account's newest copy: sent there, or put in place from
    /// there. It tells which side changed since (cloud saves, cloud-saves.ts).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub synced: Option<String>,
    /// The player switched cloud saves off for this app.
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub cloud_off: bool,
    /// The same as `synced`, for the player's sync folder (sync_folder.rs).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub folder_synced: Option<FolderSynced>,
    /// The places (`places_key`) the player said hold their saves, though a backup of them looked like more: the Saves
    /// tab stops saying so while they stay the places.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub fine: Option<String>,
    /// The places (`places_key`) last refused as too big to back up, until a backup of them works.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub too_big: Option<String>,
    /// What the catalog was told about how backups look (`Look::report_key`), so each is sent once.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub warned: Vec<String>,
}

/// When the saves last matched the newest backup in a sync folder. It only counts for that folder: another one's
/// backups were never compared with the saves here.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct FolderSynced {
    pub folder: String,
    pub hash: String,
}

pub fn state(store: &Path) -> State {
    fs::read(store.join("state.json")).ok().and_then(|b| serde_json::from_slice(&b).ok()).unwrap_or_default()
}

pub fn set_state(store: &Path, state: &State) -> Result<(), String> {
    let (temp, file) = (store.join("state.json.tmp"), store.join("state.json"));
    fs::create_dir_all(store)
        .and_then(|_| fs::write(&temp, serde_json::to_vec_pretty(state).unwrap_or_default()))
        .and_then(|_| fs::rename(&temp, &file))
        .map_err(|e| format!("Couldn't save where this app's saves are: {e}"))
}

/// One set of places as one string: a backup, a refusal or the player's word is about these places only.
pub fn places_key(templates: &[String]) -> String {
    templates.join("\n")
}

/// Save files adding up to more than this look like more than saves: the biggest saves, whole memory card images, are
/// up to 16 MB, and almost every app's are under 1 MB.
pub const BIG: u64 = 16 * 1024 * 1024;
/// More files than this in one backup look like more than saves.
pub const MANY: usize = 200;
/// A backup this size or more that's mostly pictures looks like screenshots or textures, not saves with a picture each.
const PICTURES_FROM: u64 = 1024 * 1024;
/// Kinds of file named in a look.
const KINDS: usize = 5;

/// A kind of file in a backup, by its extension ("" for none), and its share of the backup's bytes.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Share {
    pub ext: String,
    pub share: f64,
}

/// Signs that the saves Quiver backs up for an app hold more than saves (docs/save-backups.md): "big", "many",
/// "media" (mostly pictures) or "tooBig" (refused, over the place limits).
#[derive(Debug, Clone, Serialize, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Look {
    pub reasons: Vec<&'static str>,
    pub bytes: u64,
    pub files: usize,
    /// What filled it most, biggest first.
    pub kinds: Vec<Share>,
}

impl Look {
    /// What a report of it is remembered by: the signs and the places, so a sign is told once while they stay.
    pub fn report_key(&self, places: &str) -> String {
        format!("{}:{places}", self.reasons.join(","))
    }
}

/// The signs in a set of save files, or none when they look like saves.
pub fn look(files: &[FileInfo]) -> Option<Look> {
    let bytes: u64 = files.iter().map(|f| f.size).sum();
    let mut by_kind: Vec<(String, u64)> = vec![];
    for f in files {
        let ext = extension(&f.path);
        match by_kind.iter_mut().find(|(e, _)| *e == ext) {
            Some((_, n)) => *n += f.size,
            None => by_kind.push((ext, f.size)),
        }
    }
    let pictures: u64 = by_kind.iter().filter(|(e, _)| PICTURES.contains(&e.as_str())).map(|(_, n)| n).sum();
    let mut reasons = vec![];
    if bytes > BIG {
        reasons.push("big");
    }
    if files.len() > MANY {
        reasons.push("many");
    }
    if bytes >= PICTURES_FROM && pictures * 2 > bytes {
        reasons.push("media");
    }
    if reasons.is_empty() {
        return None;
    }
    by_kind.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));
    let kinds = by_kind
        .into_iter()
        .take(KINDS)
        .filter(|(e, _)| e.len() <= 10 && e.chars().all(|c| c.is_ascii_alphanumeric()))
        .map(|(ext, n)| Share { ext, share: if bytes == 0 { 0.0 } else { (n as f64 / bytes as f64 * 100.0).round() / 100.0 } })
        .collect();
    Some(Look { reasons, bytes, files: files.len(), kinds })
}

/// How the app's saves look: refused as too big, or the signs in its newest backup when that was of these places.
/// None when they look like saves, or the player said these places hold their saves.
pub fn looks(store: &Path, state: &State, templates: &[String]) -> Option<Look> {
    let key = places_key(templates);
    if templates.is_empty() || state.fine.as_deref() == Some(key.as_str()) {
        return None;
    }
    if state.too_big.as_deref() == Some(key.as_str()) {
        return Some(Look { reasons: vec!["tooBig"], bytes: 0, files: 0, kinds: vec![] });
    }
    let newest = list(store).into_iter().find(|b| b.reason != "restore")?;
    let manifest = read_manifest(&store.join(&newest.name))?;
    let of: Vec<&str> = manifest.places.iter().map(|p| p.template.as_str()).collect();
    if of != templates.iter().map(String::as_str).collect::<Vec<_>>() {
        return None;
    }
    look(&manifest.files)
}

/// The places to use, best first, and who said: "you", "catalog", "found" or "none".
pub fn places(state: &State, catalog: &[String]) -> (Vec<String>, &'static str) {
    if !state.chosen.is_empty() {
        return (state.chosen.clone(), "you");
    }
    if !catalog.is_empty() {
        return (catalog.to_vec(), "catalog");
    }
    if !state.found.is_empty() {
        return (state.found.clone(), "found");
    }
    (vec![], "none")
}

/// Places in one report to the catalog.
const MAX_REPORT: usize = 8;

/// The found places worth telling the catalog about, so it can list them for every player: folders that are there,
/// not sent before. Never a folder the player chose, a lone file or a pattern (a file's name can be a player's own),
/// and nothing once the player chose a folder: what the launcher found wasn't right then.
pub fn to_report(state: &State, roots: &Roots) -> Vec<String> {
    if !state.chosen.is_empty() {
        return vec![];
    }
    state
        .found
        .iter()
        .filter(|template| !state.reported.contains(template) && !template.starts_with("abs:"))
        .filter(|template| resolve(template, roots, &[]).is_ok_and(|place| place.pattern.is_none() && place.base.is_dir()))
        .take(MAX_REPORT)
        .cloned()
        .collect()
}

/// The chosen folders outside every root: the only absolute paths `resolve` follows.
pub fn chosen_abs(state: &State) -> Vec<String> {
    state.chosen.iter().filter(|t| t.starts_with("abs:")).cloned().collect()
}

/// Whether a folder has programs in it, a few levels down: a runtime or tools, never a save place.
fn holds_programs(dir: &Path) -> bool {
    let mut looked = 0;
    let mut stack = vec![(dir.to_path_buf(), 0)];
    while let Some((dir, depth)) = stack.pop() {
        for entry in fs::read_dir(&dir).into_iter().flatten().flatten() {
            looked += 1;
            if looked > 2_000 {
                return true;
            }
            let Ok(kind) = entry.file_type() else { continue };
            let name = entry.file_name().to_string_lossy().into_owned();
            if kind.is_dir() && depth < 3 {
                stack.push((entry.path(), depth + 1));
            } else if kind.is_file() && PROGRAMS.contains(&extension(&name).as_str()) {
                return true;
            }
        }
    }
    false
}

/// Everything in a folder, when it's small and holds no programs: then it can be a save place whole.
fn small_folder(dir: &Path) -> bool {
    let (mut total, mut looked) = (0u64, 0usize);
    let mut stack = vec![dir.to_path_buf()];
    while let Some(dir) = stack.pop() {
        for entry in fs::read_dir(&dir).into_iter().flatten().flatten() {
            looked += 1;
            let Ok(meta) = entry.metadata() else { continue };
            if meta.is_dir() {
                stack.push(entry.path());
            } else {
                total += meta.len();
            }
            if looked > 2_000 || total > SMALL_FOLDER {
                return false;
            }
        }
    }
    !holds_programs(dir)
}

/// Letters and digits only, lowercase: "Zelda 64: Recompiled" and "Zelda64Recompiled" are the same name.
fn plain(name: &str) -> String {
    name.chars().filter(|c| c.is_alphanumeric()).flat_map(char::to_lowercase).collect()
}

/// Whether a folder is named like the app: the same letters, or one name inside the other when both are long enough
/// not to match by accident.
fn named_like(folder: &str, names: &[String]) -> bool {
    let folder = plain(folder);
    folder.len() >= 3 && names.iter().any(|n| *n == folder || (n.len() >= 6 && folder.contains(n.as_str())) || (folder.len() >= 6 && n.contains(folder.as_str())))
}

/// The folders an app might save in, as templates: its own, and ones named like it where apps keep their data.
fn candidates(roots: &Roots, names: &[String]) -> Vec<(String, PathBuf)> {
    let mut out = vec![("app".to_string(), roots.app.clone())];
    let mut seen: HashSet<PathBuf> = HashSet::new();
    let mut under = |root: &str, sub: &[&str], hidden: bool| {
        let Some(mut base) = roots.of(root) else { return };
        sub.iter().for_each(|s| base.push(s));
        for entry in fs::read_dir(&base).into_iter().flatten().flatten() {
            let name = entry.file_name().to_string_lossy().into_owned();
            // In the home folder only a hidden folder is an app's own (`~/.game`); the rest are the player's.
            if !entry.path().is_dir() || (hidden && !name.starts_with('.')) || !named_like(&name, names) || entry.path().starts_with(&roots.app) {
                continue;
            }
            let template = [root].iter().chain(sub).map(|s| s.to_string()).chain([name]).collect::<Vec<_>>().join("/");
            if valid_template(&template) && seen.insert(entry.path()) {
                out.push((template, entry.path()));
            }
        }
    };
    under("localData", &[], false);
    under("data", &[], false);
    under("config", &[], false);
    under("documents", &[], false);
    under("documents", &["My Games"], false);
    under("home", &["Saved Games"], false);
    under("home", &[], true);
    out
}

fn stamp_of(meta: &fs::Metadata) -> (u64, u128) {
    (meta.len(), meta.modified().ok().and_then(|t| t.duration_since(UNIX_EPOCH).ok()).map(|d| d.as_nanos()).unwrap_or(0))
}

/// Each would-be save file under a folder, with its size and when it changed. Gives up quietly on a huge folder: what
/// it saw is still worth comparing.
fn snapshot(root: &Path, into: &mut HashMap<PathBuf, (u64, u128)>) {
    let mut looked = 0;
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        for entry in fs::read_dir(&dir).into_iter().flatten().flatten() {
            looked += 1;
            if looked > MAX_WALK {
                return;
            }
            let Ok(meta) = entry.metadata() else { continue };
            let name = entry.file_name().to_string_lossy().into_owned();
            if meta.file_type().is_symlink() {
                continue;
            }
            if meta.is_dir() {
                if !skip_dir(&name) {
                    stack.push(entry.path());
                }
            } else if meta.is_file() && is_save_file(&name, meta.len()) && valid_part(&name) {
                into.insert(entry.path(), stamp_of(&meta));
            }
        }
    }
}

/// The likely save places when an app started, and what was in them.
pub struct Watch {
    names: Vec<String>,
    before: HashMap<PathBuf, (u64, u128)>,
}

/// Before an app starts. `names`: what it's called (its folder, its name, its program), to find folders named like it.
pub fn watch_start(roots: &Roots, names: &[String]) -> Watch {
    let names: Vec<String> = names.iter().map(|n| plain(n)).filter(|n| n.len() >= 3).collect();
    let mut before = HashMap::new();
    for (_, dir) in candidates(roots, &names) {
        snapshot(&dir, &mut before);
    }
    Watch { names, before }
}

/// After it closed: the places where it wrote something that could be a save, as templates. In the app's own folder,
/// the smallest thing that holds them: a small folder with no programs in it, else the file itself.
pub fn watch_end(watch: Watch, roots: &Roots) -> Vec<String> {
    let mut out: Vec<String> = vec![];
    let mut small: HashMap<PathBuf, bool> = HashMap::new();
    for (template, dir) in candidates(roots, &watch.names) {
        let mut after = HashMap::new();
        snapshot(&dir, &mut after);
        let mut changed: Vec<&PathBuf> = after.iter().filter(|(path, stamp)| watch.before.get(*path) != Some(stamp)).map(|(path, _)| path).collect();
        changed.sort();
        if changed.is_empty() {
            continue;
        }
        if template != "app" {
            out.push(template);
            continue;
        }
        for file in changed {
            let Ok(rel) = file.strip_prefix(&dir) else { continue };
            let parts: Vec<String> = rel.components().map(|c| c.as_os_str().to_string_lossy().into_owned()).collect();
            // The highest folder under the app's that is small enough to be only saves.
            let folder = (1..parts.len()).find(|n| {
                let path = parts[..*n].iter().fold(dir.clone(), |p, part| p.join(part));
                *small.entry(path.clone()).or_insert_with(|| small_folder(&path))
            });
            let place = format!("app/{}", parts[..folder.unwrap_or(parts.len())].join("/"));
            if valid_template(&place) {
                out.push(place);
            }
        }
    }
    merge(&[], &out)
}

/// The places known already with the newly found ones: each once, none inside another, `MAX_FOUND` at most.
pub fn merge(known: &[String], found: &[String]) -> Vec<String> {
    let mut all: Vec<String> = vec![];
    for place in known.iter().chain(found) {
        if !all.contains(place) {
            all.push(place.clone());
        }
    }
    let inside = |a: &str, b: &str| a != b && a.to_lowercase().starts_with(&format!("{}/", b.to_lowercase()));
    let kept: Vec<String> = all.iter().filter(|a| !all.iter().any(|b| inside(a, b))).cloned().collect();
    kept.into_iter().take(MAX_FOUND).collect()
}

/// A place as the Saves tab shows it.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Shown {
    pub template: String,
    /// Where it is on this computer; empty when that can't be worked out here.
    pub path: String,
    pub exists: bool,
}

pub fn shown(templates: &[String], roots: &Roots, abs: &[String]) -> Vec<Shown> {
    templates
        .iter()
        .map(|template| match resolve(template, roots, abs) {
            Ok(place) => {
                let path = place.pattern.as_ref().map_or(place.base.clone(), |p| place.base.join(p));
                Shown { template: template.clone(), path: path.to_string_lossy().into(), exists: place.base.exists() }
            }
            Err(_) => Shown { template: template.clone(), path: String::new(), exists: false },
        })
        .collect()
}

/// The folder to open for a place: itself, or the one a file or pattern is in.
pub fn folder_of(place: &Place) -> PathBuf {
    if place.pattern.is_none() && place.base.is_file() {
        return place.base.parent().map(Path::to_path_buf).unwrap_or_else(|| place.base.clone());
    }
    place.base.clone()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn write(path: &Path, text: &str) {
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, text).unwrap();
    }

    /// An app's folder, the system's folders and a place for its backups, all under one temporary folder.
    fn world() -> (tempfile::TempDir, Roots, PathBuf) {
        let dir = tempfile::tempdir().unwrap();
        let at = |name: &str| {
            let path = dir.path().join(name);
            fs::create_dir_all(&path).unwrap();
            path
        };
        let roots = Roots { app: at("apps/Game"), data: Some(at("data")), config: Some(at("config")), local_data: Some(at("local")), documents: Some(at("home/Documents")), home: Some(at("home")) };
        let store = dir.path().join("store");
        (dir, roots, store)
    }

    const STAMP: Stamp = Stamp { app: "game", version: Some("1.0"), reason: "manual", os: "linux" };

    /// A restore on a computer that keeps the app's saves in these places.
    fn knowing(places: &[String]) -> Trust<'_> {
        Trust { known: places, ..Default::default() }
    }

    #[test]
    fn templates_are_plain_paths_under_a_root() {
        for good in ["app", "app/saves", "localData/Game/saves", "app/save/*.sav", "home/.game", "documents/My Games/Game"] {
            assert!(valid_template(good), "{good}");
        }
        for good in ["home/Saved Games/Game", "home/Desktop/saves", "home/AppData/LocalLow/Studio/Game", "home/.config/game", "app/microsoft/saves", "documents/Game"] {
            assert!(valid_template(good), "{good}");
        }
        // Keys, other programs' data and folders every app shares, wherever a place came from.
        for bad in [
            "home/.ssh",
            "home/.SSH/id_ed25519",
            "app/backup/.gnupg",
            "home/.aws/credentials",
            "config/autostart",
            "data/Microsoft/Windows/Start Menu/Programs/Startup",
            "data/Mozilla/Firefox/Profiles",
            "localData/Google/Chrome/User Data",
            "data/com.apple.Safari",
            "config/git",
            "home/.config",
            "home/.config/google-chrome/Default",
            "home/.local/bin",
            "home/AppData",
            "home/AppData/Local/Google/Chrome",
            "home/Library/Mail/V10",
            "home/Documents",
            "home/Saved Games",
            "documents/My Games",
        ] {
            assert!(!valid_template(bad), "{bad}");
        }
        for bad in ["", "app/", "data", "home", "temp/x", "app/../x", "app/a//b", "app/a\\b", "app/C:/x", "app/sa*ve/x", "app/*", "app/*.s*v", "/etc", "abs:/etc"] {
            assert!(!valid_template(bad), "{bad}");
        }
    }

    #[test]
    fn resolves_templates_and_only_chosen_absolute_folders() {
        let (dir, roots, _) = world();
        assert_eq!(resolve("localData/Game/saves", &roots, &[]).unwrap(), Place { base: dir.path().join("local/Game/saves"), pattern: None });
        assert_eq!(resolve("app/save/*.sav", &roots, &[]).unwrap(), Place { base: roots.app.join("save"), pattern: Some("*.sav".into()) });
        assert!(resolve("app/../../x", &roots, &[]).is_err());
        // A backup or a catalog place naming a folder of keys is never followed.
        assert!(resolve("home/.ssh", &roots, &[]).is_err());
        assert!(resolve("data/Mozilla/Firefox/Profiles", &roots, &[]).is_err());
        let elsewhere = format!("abs:{}", dir.path().join("elsewhere").display());
        assert!(resolve(&elsewhere, &roots, &[]).is_err());
        assert_eq!(resolve(&elsewhere, &roots, &[elsewhere.clone()]).unwrap().base, dir.path().join("elsewhere"));
        // A root that isn't known here, as in a Wine prefix that isn't made yet.
        assert!(resolve("data/Game", &Roots { app: roots.app.clone(), ..Default::default() }, &[]).is_err());
    }

    #[test]
    fn a_chosen_folder_becomes_a_template() {
        let (dir, roots, _) = world();
        assert_eq!(templatize(&roots.app.join("saves"), &roots).unwrap(), "app/saves");
        assert_eq!(templatize(&roots.app, &roots).unwrap(), "app");
        assert_eq!(templatize(&dir.path().join("local/Game"), &roots).unwrap(), "localData/Game");
        // Documents is inside the home folder: the deeper root wins.
        assert_eq!(templatize(&dir.path().join("home/Documents/Game"), &roots).unwrap(), "documents/Game");
        assert_eq!(templatize(&dir.path().join("home/.game"), &roots).unwrap(), "home/.game");
        assert!(templatize(&dir.path().join("home/Documents"), &roots).is_err());
        assert!(templatize(&dir.path().join("home"), &roots).is_err());
        // Never a folder of keys or another program's data, even though the player chose it.
        for private in ["home/.ssh", "home/.aws", "config/google-chrome/Default", "data/Mozilla/Firefox", "apps/Game/.ssh"] {
            assert_eq!(templatize(&dir.path().join(private), &roots).unwrap_err(), PRIVATE, "{private}");
        }
        assert_eq!(templatize(&dir.path().join("home/Saved Games"), &roots).unwrap_err(), SHARED_FOLDER);
        assert_eq!(templatize(&dir.path().join("home/Saved Games/Game"), &roots).unwrap(), "home/Saved Games/Game");
        let other = dir.path().join("other/drive");
        assert_eq!(templatize(&other, &roots).unwrap(), format!("abs:{}", other.display()));
        assert!(templatize(Path::new("saves"), &roots).is_err());
    }

    #[test]
    fn reads_the_catalogs_places_for_this_system() {
        let location = |root: &str, path: &str, os: Option<&[&str]>| Location { root: root.into(), path: path.into(), os: os.map(|o| o.iter().map(|s| s.to_string()).collect()) };
        let all = [location("localData", "Game", Some(&["windows"])), location("config", "Game", Some(&["linux", "macos"])), location("app", "save.bin", None), location("app", "../x", None), location("home", ".ssh", None), location("config", "autostart", None)];
        assert_eq!(catalog_templates(&all, "linux"), ["config/Game", "app/save.bin"]);
        assert_eq!(catalog_templates(&all, "windows"), ["localData/Game", "app/save.bin"]);
    }

    #[test]
    fn backs_up_only_saves_and_only_when_they_changed() {
        let (_dir, roots, store) = world();
        let saves = roots.app.join("saves");
        write(&saves.join("slot1.sav"), "one");
        write(&saves.join("deep/slot2.sav"), "two");
        write(&saves.join("game.exe"), "program");
        write(&saves.join("cache/shader.bin"), "cache");
        write(&saves.join("crash.log"), "log");
        let places = vec!["app/saves".to_string()];
        let first = backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        assert_eq!(first.files, 2);
        assert_eq!((first.reason.as_str(), first.version.as_deref()), ("manual", Some("1.0")));
        let manifest = read_manifest(&store.join(&first.name)).unwrap();
        assert_eq!(manifest.files.iter().map(|f| f.path.as_str()).collect::<Vec<_>>(), ["deep/slot2.sav", "slot1.sav"]);
        assert_eq!(manifest.places, [PlaceInfo { template: "app/saves".into(), kind: Kind::Folder }]);
        // Nothing changed: no second backup. A program changing isn't a change.
        write(&saves.join("game.exe"), "another program");
        assert!(backup(&store, &places, &roots, &[], &STAMP).unwrap().is_none());
        write(&saves.join("slot1.sav"), "one more");
        assert!(backup(&store, &places, &roots, &[], &STAMP).unwrap().is_some());
        assert_eq!(list(&store).len(), 2);
        // Nothing there, or a place that can't be worked out here: nothing to back up.
        assert!(backup(&store, &["app/none".into(), "abs:/nowhere".into()], &roots, &[], &STAMP).unwrap().is_none());
    }

    #[test]
    fn takes_in_a_copy_from_the_account_and_restores_it() {
        // One computer backs up; another, with no saves yet, takes the copy in and puts it in place.
        let (_dir, roots, store) = world();
        fs::create_dir_all(roots.app.join("saves")).unwrap();
        fs::write(roots.app.join("saves/slot1.sav"), "progress").unwrap();
        let places = vec!["app/saves".to_string()];
        assert_eq!(current_hash(&places, &roots, &[]).unwrap(), None.or(Some(backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap().hash)));
        let made = list(&store).remove(0);
        assert_eq!(file_of(&store, &made.name).unwrap(), store.join(&made.name));
        assert!(file_of(&store, "../state.json").is_err());

        let (_dir2, roots2, store2) = world();
        assert_eq!(current_hash(&places, &roots2, &[]).unwrap(), None);
        fs::create_dir_all(&store2).unwrap();
        let download = |name: &str| {
            let to = store2.join(name);
            fs::copy(store.join(&made.name), &to).unwrap();
            to
        };
        // Not the copy that was asked for, or not a backup at all.
        assert_eq!(adopt(&store2, &download(".cloud.part"), &"0".repeat(64)).unwrap_err(), BAD_COPY);
        fs::write(store2.join(".cloud.part"), "not a zip").unwrap();
        assert_eq!(adopt(&store2, &store2.join(".cloud.part"), &made.hash).unwrap_err(), BAD_COPY);
        assert!(!store2.join(".cloud.part").exists());
        assert!(list(&store2).is_empty());

        let taken = adopt(&store2, &download(".cloud.part"), &made.hash).unwrap();
        assert_eq!((taken.hash.as_str(), taken.files, taken.at), (made.hash.as_str(), 1, made.at));
        // The same copy again is the one that's here.
        assert_eq!(adopt(&store2, &download(".cloud.part"), &made.hash).unwrap().name, taken.name);
        assert_eq!(list(&store2).len(), 1);
        assert_eq!(restore(&store2, &taken.name, &roots2, &[], &STAMP, &knowing(&places)).unwrap(), 1);
        assert_eq!(fs::read_to_string(roots2.app.join("saves/slot1.sav")).unwrap(), "progress");
        assert_eq!(current_hash(&places, &roots2, &[]).unwrap(), Some(made.hash));
    }

    #[test]
    fn remembers_what_was_synced_and_whether_cloud_saves_are_off() {
        let (_dir, _, store) = world();
        assert_eq!(state(&store), State::default());
        let kept = State { synced: Some("a".repeat(64)), cloud_off: true, ..Default::default() };
        set_state(&store, &kept).unwrap();
        assert_eq!(state(&store), kept);
        // A state file from before cloud saves reads as never synced, and on.
        fs::write(store.join("state.json"), r#"{"found":["app/saves"]}"#).unwrap();
        assert_eq!(state(&store), State { found: vec!["app/saves".into()], ..Default::default() });
    }

    #[test]
    fn keeps_the_newest_ten() {
        let (_dir, roots, store) = world();
        let places = vec!["app/save.bin".to_string()];
        for n in 0..KEEP + 3 {
            write(&roots.app.join("save.bin"), &n.to_string());
            backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        }
        let all = list(&store);
        assert_eq!(all.len(), KEEP);
        assert!(all.windows(2).all(|pair| pair[0].at > pair[1].at));
        // The newest is the last one made.
        restore(&store, &all[1].name, &roots, &[], &STAMP, &knowing(&places)).unwrap();
        assert_eq!(fs::read_to_string(roots.app.join("save.bin")).unwrap(), (KEEP + 1).to_string());
    }

    #[test]
    fn restores_exactly_what_was_backed_up_and_keeps_what_was_there() {
        let (dir, roots, store) = world();
        let folder = dir.path().join("local/Game");
        write(&folder.join("slot1.sav"), "old one");
        write(&folder.join("b/slot2.sav"), "old two");
        write(&roots.app.join("settings.ini"), "old settings");
        write(&roots.app.join("save/a.sav"), "old a");
        let places = vec!["localData/Game".to_string(), "app/settings.ini".to_string(), "app/save/*.sav".to_string()];
        let old = backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        // Played on: a save changed, one added, one gone; a program and a log beside them.
        write(&folder.join("slot1.sav"), "new one");
        write(&folder.join("slot3.sav"), "new three");
        fs::remove_file(folder.join("b/slot2.sav")).unwrap();
        write(&folder.join("tool.exe"), "program");
        write(&roots.app.join("settings.ini"), "new settings");
        write(&roots.app.join("save/b.sav"), "new b");
        write(&roots.app.join("save/notes.txt"), "not matched");
        assert_eq!(restore(&store, &old.name, &roots, &[], &STAMP, &knowing(&places)).unwrap(), 4);
        assert_eq!(fs::read_to_string(folder.join("slot1.sav")).unwrap(), "old one");
        assert_eq!(fs::read_to_string(folder.join("b/slot2.sav")).unwrap(), "old two");
        assert!(!folder.join("slot3.sav").exists());
        assert_eq!(fs::read_to_string(folder.join("tool.exe")).unwrap(), "program");
        assert_eq!(fs::read_to_string(roots.app.join("settings.ini")).unwrap(), "old settings");
        assert_eq!(fs::read_to_string(roots.app.join("save/a.sav")).unwrap(), "old a");
        assert!(!roots.app.join("save/b.sav").exists());
        assert_eq!(fs::read_to_string(roots.app.join("save/notes.txt")).unwrap(), "not matched");
        assert!(!store.join(".restore").exists());
        // What was there went into a backup of its own, so the restore can be undone.
        let all = list(&store);
        assert_eq!(all.iter().map(|b| b.reason.as_str()).collect::<Vec<_>>(), ["restore", "manual"]);
        restore(&store, &all[0].name, &roots, &[], &STAMP, &knowing(&places)).unwrap();
        assert_eq!(fs::read_to_string(folder.join("slot1.sav")).unwrap(), "new one");
        assert_eq!(fs::read_to_string(folder.join("slot3.sav")).unwrap(), "new three");
        assert!(!folder.join("b/slot2.sav").exists());
        assert_eq!(fs::read_to_string(roots.app.join("save/b.sav")).unwrap(), "new b");
    }

    /// A backup whose manifest and entries are as given, as one from somewhere else could be.
    fn forged(store: &Path, manifest: &Manifest, entries: &[(&str, &str)]) -> String {
        fs::create_dir_all(store).unwrap();
        let mut zip = zip::ZipWriter::new(fs::File::create(store.join("5.zip")).unwrap());
        let options = zip::write::SimpleFileOptions::default();
        zip.start_file(MANIFEST, options).unwrap();
        zip.write_all(&serde_json::to_vec(manifest).unwrap()).unwrap();
        for (name, text) in entries {
            zip.start_file(*name, options).unwrap();
            zip.write_all(text.as_bytes()).unwrap();
        }
        zip.finish().unwrap();
        "5.zip".into()
    }

    fn manifest_of(places: &[(&str, Kind)], files: &[(usize, &str, &str)]) -> Manifest {
        let files: Vec<FileInfo> = files.iter().map(|(place, path, text)| FileInfo { place: *place, path: path.to_string(), size: text.len() as u64, sha256: hex::encode(Sha256::digest(text.as_bytes())) }).collect();
        Manifest {
            v: 1,
            app: "game".into(),
            at: 5,
            version: None,
            reason: "manual".into(),
            device: "pc".into(),
            os: "linux".into(),
            places: places.iter().map(|(t, kind)| PlaceInfo { template: t.to_string(), kind: *kind }).collect(),
            hash: set_hash(&files),
            files,
        }
    }

    #[test]
    fn refuses_a_backup_that_would_write_outside_its_places_or_isnt_saves() {
        let (dir, roots, store) = world();
        write(&roots.app.join("saves/slot.sav"), "mine");
        let cases: Vec<(Manifest, Vec<(&str, &str)>)> = vec![
            // Out of the place, by the path or by the place itself.
            (manifest_of(&[("app/saves", Kind::Folder)], &[(0, "../evil.sav", "x")]), vec![("0/../evil.sav", "x")]),
            (manifest_of(&[("app/../..", Kind::Folder)], &[(0, "evil.sav", "x")]), vec![("0/evil.sav", "x")]),
            (manifest_of(&[("abs:/tmp", Kind::Folder)], &[(0, "evil.sav", "x")]), vec![("0/evil.sav", "x")]),
            // A program, where saves go.
            (manifest_of(&[("app/saves", Kind::Folder)], &[(0, "run.exe", "x")]), vec![("0/run.exe", "x")]),
            // Not what the manifest says it is.
            (manifest_of(&[("app/saves", Kind::Folder)], &[(0, "slot.sav", "expected")]), vec![("0/slot.sav", "something else")]),
            (manifest_of(&[("app/saves", Kind::Folder)], &[(0, "slot.sav", "x")]), vec![]),
            // A file place takes its own file only, and a pattern only names that match.
            (manifest_of(&[("app/save.bin", Kind::File)], &[(0, "other.bin", "x")]), vec![("0/other.bin", "x")]),
            (manifest_of(&[("app/saves/*.sav", Kind::Pattern)], &[(0, "notes.txt", "x")]), vec![("0/notes.txt", "x")]),
            // The same file twice.
            (manifest_of(&[("app/saves", Kind::Folder)], &[(0, "a.sav", "x"), (0, "A.sav", "x")]), vec![("0/a.sav", "x"), ("0/A.sav", "x")]),
        ];
        let places: Vec<String> = ["app/saves", "app/save.bin"].map(String::from).to_vec();
        for (n, (manifest, entries)) in cases.iter().enumerate() {
            let name = forged(&store, manifest, entries);
            assert!(restore(&store, &name, &roots, &[], &STAMP, &knowing(&places)).is_err(), "case {n}");
            assert_eq!(fs::read_to_string(roots.app.join("saves/slot.sav")).unwrap(), "mine", "case {n}");
        }
        let mut wrong = manifest_of(&[("app/saves", Kind::Folder)], &[(0, "slot.sav", "x")]);
        wrong.hash = "0".repeat(64);
        let name = forged(&store, &wrong, &[("0/slot.sav", "x")]);
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &knowing(&places)).unwrap_err(), DAMAGED);
        assert!(!dir.path().join("evil.sav").exists() && !dir.path().join("apps/evil.sav").exists());
        // Only this app's backups, by name.
        assert!(restore(&store, "../5.zip", &roots, &[], &STAMP, &knowing(&places)).is_err());
        assert!(remove(&store, "state.json").is_err());
    }

    #[test]
    fn a_backup_never_chooses_where_its_files_go() {
        // A backup someone else put in the player's account: it names places that run things or hold the player's
        // own files, and none is where this computer keeps the app's saves.
        let (dir, roots, store) = world();
        write(&roots.app.join("saves/slot.sav"), "mine");
        write(&dir.path().join("home/Documents/Taxes/2025.txt"), "the player's own");
        let known = vec!["app/saves".to_string()];
        let names = vec!["Game".to_string()];
        let trust = Trust { known: &known, confirmed: &[], names: &names };
        let planted: [(&str, Kind, &str, &str); 5] = [
            ("home/.zshenv", Kind::File, ".zshenv", "home/.zshenv"),
            ("home/.config/fish", Kind::Folder, "config.fish", "home/.config/fish/config.fish"),
            ("config/environment.d", Kind::Folder, "x.conf", "config/environment.d/x.conf"),
            ("documents/Notes", Kind::Folder, "run.txt", "home/Documents/Notes/run.txt"),
            ("documents/Taxes", Kind::Folder, "note.txt", "home/Documents/Taxes/note.txt"),
        ];
        for (template, kind, file, lands) in planted {
            let entry = format!("0/{file}");
            let name = forged(&store, &manifest_of(&[(template, kind)], &[(0, file, "run me")]), &[(entry.as_str(), "run me")]);
            assert!(restore_plan(&store, &name, &roots, &[], &trust).is_err(), "{template}");
            // Not even when the window says the player agreed.
            let said = vec![template.to_string()];
            assert!(restore(&store, &name, &roots, &[], &STAMP, &Trust { confirmed: &said, ..knowing(&known) }).is_err(), "{template}");
            assert!(!dir.path().join(lands).exists(), "{template}");
        }
        assert_eq!(fs::read_to_string(dir.path().join("home/Documents/Taxes/2025.txt")).unwrap(), "the player's own");
        // One good place doesn't carry a bad one in with it.
        let name = forged(&store, &manifest_of(&[("app/saves", Kind::Folder), ("documents/Notes", Kind::Folder)], &[(0, "slot.sav", "theirs"), (1, "a.txt", "x")]), &[("0/slot.sav", "theirs"), ("1/a.txt", "x")]);
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &trust).unwrap_err(), WRONG_PLACE);
        assert_eq!(fs::read_to_string(roots.app.join("saves/slot.sav")).unwrap(), "mine");
    }

    #[test]
    fn a_place_this_computer_doesnt_know_waits_for_the_player() {
        // A new computer: it has no saves and knows no place for the app. The backup's folder is named like the
        // app, so it can be offered, and nothing is written until the player agrees to it.
        let (dir, roots, store) = world();
        let names = vec!["Game".to_string()];
        let name = forged(&store, &manifest_of(&[("localData/Game/saves", Kind::Folder), ("app/data/*.sav", Kind::Pattern)], &[(0, "slot.sav", "progress"), (1, "a.sav", "more")]), &[("0/slot.sav", "progress"), ("1/a.sav", "more")]);
        let trust = Trust { known: &[], confirmed: &[], names: &names };
        let plan = restore_plan(&store, &name, &roots, &[], &trust).unwrap();
        assert_eq!(plan.iter().map(|p| p.template.as_str()).collect::<Vec<_>>(), ["localData/Game/saves", "app/data/*.sav"]);
        assert_eq!(plan[0].path, dir.path().join("local").join("Game").join("saves").to_string_lossy());
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &trust).unwrap_err(), NOT_AGREED);
        assert!(!dir.path().join("local/Game").exists());
        // Agreeing to one of two isn't agreeing to both.
        let one = vec!["localData/Game/saves".to_string()];
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &Trust { confirmed: &one, ..trust }).unwrap_err(), NOT_AGREED);
        let both = vec!["localData/Game/saves".to_string(), "app/data/*.sav".to_string()];
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &Trust { confirmed: &both, names: &names, known: &[] }).unwrap(), 2);
        assert_eq!(fs::read_to_string(dir.path().join("local/Game/saves/slot.sav")).unwrap(), "progress");
        // Known now, there's nothing left to ask.
        assert!(restore_plan(&store, &name, &roots, &[], &Trust { known: &both, ..Default::default() }).unwrap().is_empty());
        // A folder that holds a known place's saves covers it; the app's whole folder is only ever the player's own.
        let whole = vec!["app".to_string(), "localData/Game".to_string()];
        assert!(restore_plan(&store, &name, &roots, &[], &knowing(&whole)).unwrap().is_empty());
        let other = vec!["app/data/*.dat".to_string(), "localData/Game/saves".to_string()];
        assert_eq!(restore_plan(&store, &name, &roots, &[], &Trust { known: &other, names: &names, confirmed: &[] }).unwrap().len(), 1);
        // A folder not named like the app, outside its own, is never offered.
        let name = forged(&store, &manifest_of(&[("localData/Other/saves", Kind::Folder)], &[(0, "slot.sav", "x")]), &[("0/slot.sav", "x")]);
        assert_eq!(restore_plan(&store, &name, &roots, &[], &trust).unwrap_err(), WRONG_PLACE);
    }

    #[test]
    fn names_windows_reads_as_another_folder_are_refused() {
        for bad in [
            "data/Microsoft./Windows/Start Menu/Programs/Startup.",
            "data/Game/Startup ",
            "data/Game/ saves",
            "data/MICROS~1/Windows/STARTM~1/Programs/Startu~1",
            "home/.zshenv",
            "home/.zprofile",
            "home/.bash_login",
            "home/.xprofile",
            "home/.config/fish",
            "home/.config/environment.d",
            "config/environment.d",
            "home/.local/share/applications",
            "data/applications",
            "documents/WindowsPowerShell",
            "documents/PowerShell/Modules",
            "home/Documents/WindowsPowerShell",
        ] {
            assert!(!valid_template(bad), "{bad}");
        }
        for good in ["data/Game.v2/saves", "home/.game", "app/save~", "documents/My Games/Game"] {
            assert!(valid_template(good), "{good}");
        }
        // A file named to become a program once Windows drops the dot isn't a save.
        let (_dir, roots, store) = world();
        let known = vec!["app/saves".to_string()];
        let name = forged(&store, &manifest_of(&[("app/saves", Kind::Folder)], &[(0, "run.exe.", "x")]), &[("0/run.exe.", "x")]);
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &knowing(&known)).unwrap_err(), DAMAGED);
    }

    #[test]
    fn scripts_and_shortcuts_are_left_alone() {
        let (dir, roots, store) = world();
        let folder = dir.path().join("local/Game");
        write(&folder.join("slot.sav"), "one");
        write(&folder.join("start.bat"), "the player's");
        write(&roots.app.join("saves/slot.sav"), "one");
        write(&roots.app.join("saves/start.sh"), "the app's");
        let places = vec!["localData/Game".to_string(), "app/saves".to_string()];
        let made = backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        // Never backed up, wherever they are.
        assert_eq!(made.files, 2);
        // A backup that holds one anyway doesn't put it back, not even over the app's own, and one that's there isn't
        // taken away.
        let entries = [("0/slot.sav", "two"), ("0/evil.cmd", "x"), ("0/link.desktop", "x"), ("1/slot.sav", "two"), ("1/start.sh", "theirs")];
        let name = forged(
            &store,
            &manifest_of(&[("localData/Game", Kind::Folder), ("app/saves", Kind::Folder)], &[(0, "slot.sav", "two"), (0, "evil.cmd", "x"), (0, "link.desktop", "x"), (1, "slot.sav", "two"), (1, "start.sh", "theirs")]),
            &entries,
        );
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP, &knowing(&places)).unwrap(), 2);
        assert_eq!(fs::read_to_string(folder.join("slot.sav")).unwrap(), "two");
        assert!(!folder.join("evil.cmd").exists() && !folder.join("link.desktop").exists());
        assert_eq!(fs::read_to_string(folder.join("start.bat")).unwrap(), "the player's");
        assert_eq!(fs::read_to_string(roots.app.join("saves/start.sh")).unwrap(), "the app's");
    }

    #[test]
    fn a_backup_bigger_than_saves_are_isnt_unpacked() {
        let (_dir, roots, store) = world();
        let known = vec!["app/saves".to_string()];
        let mut many = manifest_of(&[("app/saves", Kind::Folder)], &[]);
        many.files = (0..=MAX_FILES).map(|n| FileInfo { place: 0, path: format!("{n}.sav"), size: 1, sha256: "0".repeat(64) }).collect();
        many.hash = set_hash(&many.files);
        let mut heavy = manifest_of(&[("app/saves", Kind::Folder)], &[]);
        heavy.files = (0..5).map(|n| FileInfo { place: 0, path: format!("{n}.sav"), size: MAX_FILE, sha256: "0".repeat(64) }).collect();
        heavy.hash = set_hash(&heavy.files);
        let mut spread = manifest_of(&[("app/saves", Kind::Folder)], &[(0, "slot.sav", "x")]);
        spread.places = (0..=MAX_PLACES).map(|n| PlaceInfo { template: format!("app/saves{n}"), kind: Kind::Folder }).collect();
        for (n, manifest) in [many, heavy, spread].iter().enumerate() {
            let name = forged(&store, manifest, &[("0/slot.sav", "x")]);
            assert!(restore(&store, &name, &roots, &[], &STAMP, &knowing(&known)).is_err(), "case {n}");
            assert!(!store.join(".restore").join("0").exists(), "case {n}");
            fs::copy(store.join(&name), store.join(".cloud.part")).unwrap();
            assert_eq!(adopt(&store, &store.join(".cloud.part"), &manifest.hash).unwrap_err(), BAD_COPY, "case {n}");
        }
    }

    #[test]
    fn a_place_too_big_to_be_saves_isnt_backed_up() {
        let (_dir, roots, store) = world();
        for n in 0..MAX_FILES + 1 {
            write(&roots.app.join(format!("data/{n}.dat")), "");
        }
        assert_eq!(backup(&store, &["app/data".into()], &roots, &[], &STAMP).unwrap_err(), TOO_BIG);
        // The Saves tab says so until a backup of these places works, and the player's word stops it.
        let places = vec!["app/data".to_string()];
        let too_big = looks(&store, &state(&store), &places).unwrap();
        assert_eq!(too_big.reasons, ["tooBig"]);
        assert!(looks(&store, &state(&store), &["app/saves".into()]).is_none());
        let mut said = state(&store);
        said.fine = Some(places_key(&places));
        assert!(looks(&store, &said, &places).is_none());
        for n in 0..MAX_FILES + 1 {
            fs::remove_file(roots.app.join(format!("data/{n}.dat"))).unwrap();
        }
        write(&roots.app.join("data/slot.sav"), "one");
        backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        assert_eq!(state(&store).too_big, None);
        assert!(looks(&store, &state(&store), &places).is_none());
    }

    #[test]
    fn leaves_out_files_no_game_saves_as() {
        let (_dir, roots, store) = world();
        let saves = roots.app.join("saves");
        for name in ["slot.sav", "thumb.png", "options.ini", "data.bin", "music.ogg", "voice.wav", "font.ttf", "wall.dds", "shader.spv", "model.obj", "manual.pdf", "intro.bik"] {
            write(&saves.join(name), name);
        }
        let made = backup(&store, &["app/saves".into()], &roots, &[], &STAMP).unwrap().unwrap();
        let manifest = read_manifest(&store.join(&made.name)).unwrap();
        assert_eq!(manifest.files.iter().map(|f| f.path.as_str()).collect::<Vec<_>>(), ["data.bin", "options.ini", "slot.sav", "thumb.png"]);
    }

    fn info(path: &str, size: u64) -> FileInfo {
        FileInfo { place: 0, path: path.into(), size, sha256: String::new() }
    }

    #[test]
    fn tells_saves_from_more_than_saves() {
        const MB: u64 = 1024 * 1024;
        // Saves: small, few, or a memory card image at the most a whole one is.
        assert_eq!(look(&[info("slot1.sav", 2048), info("thumb.png", 30_000)]), None);
        assert_eq!(look(&[info("card.raw", 16 * MB)]), None);
        assert_eq!(look(&(0..MANY).map(|n| info(&format!("{n}.sav"), 10)).collect::<Vec<_>>()), None);
        // Bigger than saves, with what filled it biggest first.
        let big = look(&[info("slot.sav", 1000), info("a/shots.png", 12 * MB), info("b/more.png", 5 * MB), info("cache.bin", MB)]).unwrap();
        assert_eq!(big.reasons, ["big", "media"]);
        assert_eq!(big.files, 4);
        assert_eq!(big.kinds, [Share { ext: "png".into(), share: 0.94 }, Share { ext: "bin".into(), share: 0.06 }, Share { ext: "sav".into(), share: 0.0 }]);
        // Too many files.
        let many = look(&(0..MANY + 1).map(|n| info(&format!("{n}.dat"), 10)).collect::<Vec<_>>()).unwrap();
        assert_eq!(many.reasons, ["many"]);
        // Mostly pictures, once there's a megabyte of them; extensions that can't be told apart are left out.
        assert_eq!(look(&[info("slot.sav", 1000), info("shot.png", MB)]).unwrap().reasons, ["media"]);
        assert_eq!(look(&[info("slot.sav", 1000), info("shot.png", MB / 2)]), None);
        assert_eq!(look(&[info("weird.ext with space", MB), info("shot.jpg", 2 * MB)]).unwrap().kinds, [Share { ext: "jpg".into(), share: 0.67 }]);
        // Remembered by its signs and places.
        assert_eq!(big.report_key("app/saves"), "big,media:app/saves");
    }

    #[test]
    fn says_how_the_newest_backup_of_these_places_looks() {
        let (_dir, roots, store) = world();
        let places = vec!["app/saves".to_string()];
        let saves = roots.app.join("saves");
        write(&saves.join("slot.sav"), "one");
        backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        assert!(looks(&store, &state(&store), &places).is_none());
        for n in 0..=MANY {
            write(&saves.join(format!("shots/{n}.sav")), "x");
        }
        backup(&store, &places, &roots, &[], &STAMP).unwrap().unwrap();
        assert_eq!(looks(&store, &state(&store), &places).unwrap().reasons, ["many"]);
        // The player chose another folder: its backups aren't made yet.
        assert!(looks(&store, &state(&store), &["app/other".into()]).is_none());
    }

    #[test]
    fn which_places_win() {
        let catalog = vec!["app/saves".to_string()];
        let mut state = State::default();
        assert_eq!(places(&state, &[]), (vec![], "none"));
        state.found = vec!["app/found".into()];
        assert_eq!(places(&state, &[]), (vec!["app/found".to_string()], "found"));
        assert_eq!(places(&state, &catalog), (catalog.clone(), "catalog"));
        state.chosen = vec!["abs:/x".into()];
        assert_eq!(places(&state, &catalog), (vec!["abs:/x".to_string()], "you"));
        assert_eq!(chosen_abs(&state), ["abs:/x"]);
    }

    #[test]
    fn remembers_an_apps_places() {
        let (_dir, _, store) = world();
        assert_eq!(state(&store), State::default());
        let kept = State { chosen: vec!["app/saves".into()], found: vec!["data/Game".into()], reported: vec!["data/Game".into()], ..Default::default() };
        set_state(&store, &kept).unwrap();
        assert_eq!(state(&store), kept);
        // Beside the backups without being one.
        assert!(list(&store).is_empty());
    }

    #[test]
    fn reports_only_found_folders_once_and_never_the_players_own() {
        let (_dir, roots, _) = world();
        write(&roots.app.join("saves/slot1.sav"), "one");
        write(&roots.app.join("eeprom.bin"), "two");
        write(&roots.data.clone().unwrap().join("Game/profile.dat"), "three");
        let found: Vec<String> = ["app/saves", "app/eeprom.bin", "data/Game", "config/Gone", "abs:/somewhere/else"].map(String::from).into();
        let mut state = State { found, ..Default::default() };
        // Folders that are there: not the lone file, the folder that's gone, or one outside every root.
        assert_eq!(to_report(&state, &roots), ["app/saves", "data/Game"]);
        state.reported = vec!["app/saves".into()];
        assert_eq!(to_report(&state, &roots), ["data/Game"]);
        // The player chose a folder: what was found wasn't right.
        state.chosen = vec!["app/other".into()];
        assert!(to_report(&state, &roots).is_empty());
    }

    #[test]
    fn sees_where_an_app_writes_its_saves() {
        let (dir, roots, _) = world();
        // The game's own files, there before and untouched.
        write(&roots.app.join("game.exe"), "program");
        write(&roots.app.join("assets/level1.dat"), "level");
        write(&roots.app.join("saves/old.sav"), "old");
        write(&dir.path().join("local/Another Game/save.sav"), "someone else's");
        let watch = watch_start(&roots, &["Game".into(), "Zelda 64: Recompiled".into()]);
        // What a session leaves behind.
        write(&roots.app.join("saves/new.sav"), "new");
        write(&roots.app.join("settings.ini"), "volume=3");
        write(&roots.app.join("game.log"), "log");
        write(&roots.app.join("cache/shader.bin"), "cache");
        write(&roots.app.join("runtime/save.dat"), "written beside programs");
        write(&roots.app.join("runtime/tool.exe"), "program");
        write(&dir.path().join("config/Zelda64Recompiled/saves/file1.bin"), "save");
        write(&dir.path().join("local/Another Game/save.sav"), "someone else played");
        write(&dir.path().join("home/notes.txt"), "the player's own");
        let mut found = watch_end(watch, &roots);
        found.sort();
        assert_eq!(found, ["app/runtime/save.dat", "app/saves", "app/settings.ini", "config/Zelda64Recompiled"]);
    }

    #[test]
    fn an_untouched_app_has_no_found_places() {
        let (_dir, roots, _) = world();
        write(&roots.app.join("saves/old.sav"), "old");
        let watch = watch_start(&roots, &["Game".into()]);
        assert!(watch_end(watch, &roots).is_empty());
    }

    #[test]
    fn merges_found_places() {
        let known = vec!["app/saves".to_string()];
        assert_eq!(merge(&known, &["app/saves/slot1".into(), "app/settings.ini".into(), "app/saves".into()]), ["app/saves", "app/settings.ini"]);
        let many: Vec<String> = (0..20).map(|n| format!("app/f{n}")).collect();
        assert_eq!(merge(&[], &many).len(), MAX_FOUND);
    }

    #[test]
    fn names_a_store_folder_for_any_id() {
        assert_eq!(store_name("k57abc"), "k57abc");
        let custom = store_name("github:owner/repo");
        assert!(custom.starts_with("github_owner_repo-") && custom.len() == "github_owner_repo-".len() + 8);
        assert_ne!(store_name("github:owner/repo"), store_name("github:owner_repo"));
        assert!(!store_name("../..").contains('.'));
    }

    #[test]
    fn a_windows_apps_folders_are_in_its_wine_prefix() {
        let user = Path::new("/prefix/drive_c/users/steamuser");
        let roots = Roots::windows_user(PathBuf::from("/apps/Game"), user);
        assert_eq!(resolve("localData/Game/saves", &roots, &[]).unwrap().base, user.join("AppData/Local/Game/saves"));
        assert_eq!(resolve("data/Game", &roots, &[]).unwrap().base, user.join("AppData/Roaming/Game"));
        assert_eq!(resolve("documents/My Games/Game", &roots, &[]).unwrap().base, user.join("Documents/My Games/Game"));
    }
}
