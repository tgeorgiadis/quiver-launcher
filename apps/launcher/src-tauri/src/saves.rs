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

/// Never a save: programs, the game's own files, archives, logs and videos.
const NOT_SAVES: [&str; 47] = [
    "iso", "cue", "chd", "cso", "gcm", "rvz", "wbfs", "wad", "nsp", "xci", "xex", "xbe", "z64", "n64", "v64", "sfc", "smc", "gba", "gbc", "nds", "3ds", "elf", "rpx", "exe", "dll", "so",
    "dylib", "pdb", "msi", "apk", "zip", "7z", "rar", "gz", "xz", "appimage", "otr", "o2r", "pak", "log", "tmp", "dmp", "mp4", "mkv", "webm", "lock", "pid",
];
/// Folders that never hold saves.
const NOT_SAVE_DIRS: [&str; 18] = [
    "mods", "shadercache", "shader_cache", "shaders", "cache", "caches", "logs", "log", "crashdumps", "crashes", "dumps", "screenshots", "texture_packs", "textures", "temp", "tmp", ".git",
    "__pycache__",
];
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

fn valid_part(part: &str) -> bool {
    !part.is_empty() && part != "." && part != ".." && !part.chars().any(|c| c.is_control() || "<>:\"|?\\".contains(c))
}

/// Whether a template is one of the roots and a plain path under it: no `..`, no drive, one `*` at most and only in
/// the last part. `app` alone is the app's whole folder, which only the player can choose.
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
    path.len() <= 200 && parts.iter().all(|p| valid_part(p)) && parts[..parts.len() - 1].iter().all(|p| !p.contains('*')) && last.matches('*').count() <= 1 && last != "*"
}

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
        return if valid_template(&template) { Ok(template) } else { Ok(format!("abs:{}", path.display())) };
    }
    let mut best: Option<(usize, String)> = None;
    for root in ["localData", "data", "config", "documents", "home"] {
        let Some(base) = roots.of(root) else { continue };
        let Some(parts) = rel(&base) else { continue };
        if parts.is_empty() {
            return Err("Choose the folder this app saves in, not a folder every app shares.".into());
        }
        let depth = base.components().count();
        let template = format!("{root}/{}", parts.join("/"));
        if valid_template(&template) && best.as_ref().is_none_or(|(d, _)| depth > *d) {
            best = Some((depth, template));
        }
    }
    Ok(best.map(|(_, t)| t).unwrap_or_else(|| format!("abs:{}", path.display())))
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

fn read_manifest(zip_file: &Path) -> Option<Manifest> {
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
            Some(Backup { name, at: m.at, size: e.metadata().ok()?.len(), files: m.files.len(), version: m.version, reason: m.reason, device: m.device, hash: m.hash })
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
    let (places, found, files) = current(templates, roots, abs, kinds)?;
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
    Ok(Some(Backup { name, at, size, files: manifest.files.len(), version: manifest.version, reason: manifest.reason, device: manifest.device, hash }))
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
pub fn restore(store: &Path, name: &str, roots: &Roots, abs: &[String], stamp: &Stamp) -> Result<usize, String> {
    let zip_file = backup_file(store, name)?;
    let manifest = read_manifest(&zip_file).ok_or(DAMAGED)?;
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
    let mut archive = zip::ZipArchive::new(fs::File::open(zip_file).map_err(|_| DAMAGED)?).map_err(|_| DAMAGED)?;
    let mut seen = HashSet::new();
    let mut out = vec![];
    for (n, info) in manifest.files.iter().enumerate() {
        let parts: Vec<&str> = info.path.split('/').collect();
        let name = parts[parts.len() - 1];
        let place = places.get(info.place).ok_or(DAMAGED)?;
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
    if set_hash(&manifest.files) != manifest.hash {
        return Err(DAMAGED.into());
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

    #[test]
    fn templates_are_plain_paths_under_a_root() {
        for good in ["app", "app/saves", "localData/Game/saves", "app/save/*.sav", "home/.game", "documents/My Games/Game"] {
            assert!(valid_template(good), "{good}");
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
        let other = dir.path().join("other/drive");
        assert_eq!(templatize(&other, &roots).unwrap(), format!("abs:{}", other.display()));
        assert!(templatize(Path::new("saves"), &roots).is_err());
    }

    #[test]
    fn reads_the_catalogs_places_for_this_system() {
        let location = |root: &str, path: &str, os: Option<&[&str]>| Location { root: root.into(), path: path.into(), os: os.map(|o| o.iter().map(|s| s.to_string()).collect()) };
        let all = [location("localData", "Game", Some(&["windows"])), location("config", "Game", Some(&["linux", "macos"])), location("app", "save.bin", None), location("app", "../x", None)];
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
        restore(&store, &all[1].name, &roots, &[], &STAMP).unwrap();
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
        assert_eq!(restore(&store, &old.name, &roots, &[], &STAMP).unwrap(), 4);
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
        restore(&store, &all[0].name, &roots, &[], &STAMP).unwrap();
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
        for (n, (manifest, entries)) in cases.iter().enumerate() {
            let name = forged(&store, manifest, entries);
            assert!(restore(&store, &name, &roots, &[], &STAMP).is_err(), "case {n}");
            assert_eq!(fs::read_to_string(roots.app.join("saves/slot.sav")).unwrap(), "mine", "case {n}");
        }
        let mut wrong = manifest_of(&[("app/saves", Kind::Folder)], &[(0, "slot.sav", "x")]);
        wrong.hash = "0".repeat(64);
        let name = forged(&store, &wrong, &[("0/slot.sav", "x")]);
        assert_eq!(restore(&store, &name, &roots, &[], &STAMP).unwrap_err(), DAMAGED);
        assert!(!dir.path().join("evil.sav").exists() && !dir.path().join("apps/evil.sav").exists());
        // Only this app's backups, by name.
        assert!(restore(&store, "../5.zip", &roots, &[], &STAMP).is_err());
        assert!(remove(&store, "state.json").is_err());
    }

    #[test]
    fn a_place_too_big_to_be_saves_isnt_backed_up() {
        let (_dir, roots, store) = world();
        for n in 0..MAX_FILES + 1 {
            write(&roots.app.join(format!("data/{n}.dat")), "");
        }
        assert_eq!(backup(&store, &["app/data".into()], &roots, &[], &STAMP).unwrap_err(), TOO_BIG);
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
        let kept = State { chosen: vec!["app/saves".into()], found: vec!["data/Game".into()] };
        set_state(&store, &kept).unwrap();
        assert_eq!(state(&store), kept);
        // Beside the backups without being one.
        assert!(list(&store).is_empty());
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
