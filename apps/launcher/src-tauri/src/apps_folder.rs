//! Where apps are installed: `apps` in the data folder unless the player
//! picked another folder, which `apps-folder.json` remembers. Changing it
//! moves the installed apps there first. An app's own folder in it can be
//! renamed too.
use serde::{Deserialize, Serialize};
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

const FILE: &str = "apps-folder.json";

#[derive(Serialize, Deserialize)]
struct Saved {
    path: PathBuf,
}

/// The apps folder: the one the player picked, else `apps` in the data folder.
pub fn current(data: &Path) -> PathBuf {
    fs::read(data.join(FILE))
        .ok()
        .and_then(|b| serde_json::from_slice::<Saved>(&b).ok())
        .map(|s| s.path)
        .filter(|p| p.is_absolute())
        .unwrap_or_else(|| data.join("apps"))
}

fn save(data: &Path, path: &Path) -> io::Result<()> {
    fs::create_dir_all(data)?;
    let temp = data.join(format!("{FILE}.tmp"));
    fs::write(&temp, serde_json::to_vec_pretty(&Saved { path: path.to_path_buf() })?)?;
    fs::rename(temp, data.join(FILE))
}

/// An installed app to move: its folder in the apps folder (`folder`), or,
/// for one kept somewhere else (`dir`), as Quiver Launcher 3's are, that folder.
#[derive(Deserialize, Clone, Debug)]
pub struct App {
    pub id: String,
    pub folder: String,
    #[serde(default)]
    pub dir: Option<String>,
}

/// How far a move has got: `done` of `total` apps moved, and, while `folder`
/// copies to another drive, `copied` of its `size` bytes.
#[derive(Serialize, Clone, Debug, PartialEq)]
pub struct Moving {
    pub done: usize,
    pub total: usize,
    pub folder: String,
    pub copied: u64,
    pub size: u64,
}

/// Where an app is once it has moved: `dir`, its folder in the new place.
#[derive(Serialize, Clone, Debug)]
pub struct Moved {
    pub id: String,
    pub dir: String,
}

/// A finished move.
#[derive(Serialize, Debug, Default)]
#[serde(rename_all = "camelCase")]
pub struct Done {
    /// The apps now in the apps folder under their own folder's name, which no longer need a `dir`.
    pub inside: Vec<String>,
    /// Old copies that moved safely but couldn't all be deleted (a file was in use): the player can delete them.
    pub left: Vec<String>,
    /// The apps that couldn't move, each still whole where it was, and why.
    pub failed: Vec<Failed>,
}

/// An app a move left where it was: its folder's name, and why it couldn't move.
#[derive(Serialize, Debug)]
pub struct Failed {
    pub folder: String,
    pub why: String,
}

/// What went wrong moving an app, in the player's words where Quiver knows them.
fn explain(e: &io::Error) -> String {
    match e.raw_os_error() {
        // ERROR_VIRUS_INFECTED, ERROR_VIRUS_DELETED: Microsoft Defender (or another antivirus) won't let a file be read.
        Some(225 | 226) if cfg!(windows) => {
            "Windows Security blocked one of its files as unwanted software. If you trust this app, allow the file in Windows Security's Protection history, then move it again".into()
        }
        // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
        Some(32 | 33) if cfg!(windows) => "one of its files is in use. Close it, and anything with its files open, then try again".into(),
        _ if e.kind() == io::ErrorKind::StorageFull => "the drive ran out of room".into(),
        _ if e.kind() == io::ErrorKind::PermissionDenied => format!("Quiver isn't allowed to read or write one of its files ({e})"),
        _ => e.to_string(),
    }
}

fn same(a: &Path, b: &Path) -> bool {
    a.canonicalize().ok().zip(b.canonicalize().ok()).is_some_and(|(a, b)| a == b) || a == b
}

/// The apps kept somewhere else that a move to `to` brings in: those still
/// there, and not already in `to`.
fn elsewhere<'a>(apps: &'a [App], to: &'a Path) -> impl Iterator<Item = (&'a App, PathBuf)> + 'a {
    apps.iter()
        .filter_map(|a| Some((a, PathBuf::from(a.dir.as_ref()?))))
        .filter(|(_, dir)| dir.is_dir() && !dir.parent().is_some_and(|p| same(p, to)))
}

/// Why apps can't move from `from` to `to`, if they can't.
pub fn refuse(from: &Path, to: &Path, apps: &[App]) -> Option<String> {
    if !to.is_absolute() {
        return Some("Choose a folder on this computer.".into());
    }
    let staying = same(from, to);
    if staying && elsewhere(apps, to).next().is_none() {
        return Some("Apps are already installed there.".into());
    }
    if !staying && resolved(to).starts_with(resolved(from)) {
        return Some("Choose a folder outside the apps folder.".into());
    }
    if let Some((app, _)) = elsewhere(apps, to).find(|(_, dir)| resolved(to).starts_with(resolved(dir))) {
        return Some(format!("Choose a folder outside {}'s folder.", app.folder));
    }
    let inside = apps.iter().filter(|a| a.dir.is_none()).map(|a| &a.folder);
    if let Some(taken) = inside.filter(|_| !staying).find(|f| from.join(f).exists() && to.join(f).exists()) {
        return Some(format!("That folder already has a folder named {taken}. Choose another folder, or move it out of the way first."));
    }
    if !writable(to) {
        return Some("Quiver can't save files in that folder. Choose another one.".into());
    }
    let copying: Vec<PathBuf> = sources(from, to, apps).into_iter().map(|(_, dir)| dir).filter(|dir| !same_drive(dir, to)).collect();
    short_of_space(&copying, to, free_space(to))
}

/// The folders a move to `to` takes: the apps folder's apps still there (unless it's staying where it is), then
/// those kept elsewhere, still there and not in `to` already.
fn sources<'a>(from: &Path, to: &'a Path, apps: &'a [App]) -> Vec<(&'a App, PathBuf)> {
    let inside = apps.iter().filter(|a| a.dir.is_none() && from.join(&a.folder).exists() && !same(from, to)).map(|a| (a, from.join(&a.folder)));
    inside.chain(elsewhere(apps, to)).collect()
}

/// Room kept free on the drive after a move, for the system and the apps' own saves.
const SPARE: u64 = 512 << 20;

/// Why `copying` (the folders that have to be copied, being on another drive) won't fit in `to` with `free` bytes
/// free, if they won't. Nothing to say when the free space isn't known: a copy that runs out stops and leaves the
/// original whole.
fn short_of_space(copying: &[PathBuf], to: &Path, free: Option<u64>) -> Option<String> {
    let free = free?;
    let needed = copying.iter().filter_map(|dir| size_of(dir).ok()).sum::<u64>();
    if needed == 0 || needed + SPARE <= free {
        return None;
    }
    let drive = to.ancestors().last().unwrap_or(to).display().to_string();
    Some(format!(
        "There isn't enough space on {drive} for your apps: they need {}, and it has {} free. Free up some space, or choose another drive.",
        gigabytes(needed + SPARE),
        gigabytes(free)
    ))
}

fn gigabytes(bytes: u64) -> String {
    format!("{:.1} GB", bytes as f64 / (1u64 << 30) as f64)
}

/// Whether Quiver can make files in `to`, or in the nearest folder above it that exists, where it would make `to`.
/// A folder another account owns, a read-only drive, or one a sandbox (Flatpak) doesn't let it into fails here,
/// before anything moves, rather than at the first app.
fn writable(to: &Path) -> bool {
    let Some(at) = to.ancestors().find(|p| p.is_dir()) else { return false };
    let probe = at.join(format!(".quiver-write-check-{}", std::process::id()));
    let ok = fs::write(&probe, b"").is_ok();
    let _ = fs::remove_file(&probe);
    ok
}

/// Whether two paths are on the same drive, so moving between them is a rename, not a copy. When it can't be told,
/// they're taken to be on different drives, so the space for a copy is checked.
fn same_drive(a: &Path, b: &Path) -> bool {
    let existing = |p: &Path| p.ancestors().find(|p| p.exists()).map(Path::to_path_buf);
    let (Some(a), Some(b)) = (existing(a), existing(b)) else { return false };
    #[cfg(unix)]
    {
        use std::os::unix::fs::MetadataExt;
        matches!((fs::metadata(&a), fs::metadata(&b)), (Ok(x), Ok(y)) if x.dev() == y.dev())
    }
    #[cfg(windows)]
    {
        let root = |p: &Path| p.canonicalize().ok().and_then(|p| p.components().next().map(|c| c.as_os_str().to_ascii_lowercase()));
        matches!((root(&a), root(&b)), (Some(x), Some(y)) if x == y)
    }
    #[cfg(not(any(unix, windows)))]
    {
        false
    }
}

/// How an apps folder is now: whether it's there (a drive that isn't plugged in, or a folder renamed, isn't), and the
/// bytes free on its drive when it is.
#[derive(Serialize, Debug, PartialEq)]
pub struct FolderState {
    pub exists: bool,
    pub free: Option<u64>,
}

/// How each apps folder is now, in the order given.
pub fn states(paths: &[PathBuf]) -> Vec<FolderState> {
    paths
        .iter()
        .map(|p| {
            let exists = p.is_dir();
            FolderState { exists, free: if exists { free_space(p) } else { None } }
        })
        .collect()
}

/// The bytes free for this user on the drive `path` is on (or will be, for a folder not made yet), when the system
/// says.
pub fn free_space(path: &Path) -> Option<u64> {
    let at = path.ancestors().find(|p| p.exists())?;
    free_at(at)
}

#[cfg(windows)]
fn free_at(path: &Path) -> Option<u64> {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Storage::FileSystem::GetDiskFreeSpaceExW;
    let wide: Vec<u16> = path.as_os_str().encode_wide().chain([0]).collect();
    let mut free = 0u64;
    // SAFETY: a nul-terminated path, and a u64 for the one answer asked for; the others are optional.
    let ok = unsafe { GetDiskFreeSpaceExW(wide.as_ptr(), &mut free, std::ptr::null_mut(), std::ptr::null_mut()) };
    (ok != 0).then_some(free)
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn free_at(path: &Path) -> Option<u64> {
    use std::os::unix::ffi::OsStrExt;
    let path = std::ffi::CString::new(path.as_os_str().as_bytes()).ok()?;
    // SAFETY: a nul-terminated path, and a statvfs for it to fill in.
    let mut stat: libc::statvfs = unsafe { std::mem::zeroed() };
    let ok = unsafe { libc::statvfs(path.as_ptr(), &mut stat) } == 0;
    ok.then(|| stat.f_bavail as u64 * stat.f_frsize as u64)
}

#[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
fn free_at(_: &Path) -> Option<u64> {
    None
}

/// The path as the file system names it, for a folder that doesn't exist yet
/// too: its nearest existing parent, resolved, with the rest added back. So
/// both sides of a comparison are written alike, as Windows' `\\?\` form or
/// its long names are once resolved.
fn resolved(path: &Path) -> PathBuf {
    let mut rest = vec![];
    let mut at = path;
    loop {
        if let Ok(found) = at.canonicalize() {
            return rest.iter().rev().fold(found, |path, part| path.join(part));
        }
        match (at.parent(), at.file_name()) {
            (Some(parent), Some(name)) => {
                rest.push(name.to_os_string());
                at = parent;
            }
            _ => return path.to_path_buf(),
        }
    }
}

/// A name for an app kept elsewhere in `to`: its own folder's name, unless
/// that's taken there or by another app moving, then with a number after it.
fn free_name(folder: &str, to: &Path, claimed: &[String]) -> String {
    let taken = |n: &str| to.join(n).exists() || claimed.iter().any(|c| c.eq_ignore_ascii_case(n));
    std::iter::once(folder.to_string()).chain((2..).map(|i| format!("{folder} {i}"))).find(|n| !taken(n)).unwrap()
}

/// Moves the installed apps into `to` and makes it the apps folder: those in
/// the apps folder (`from`), and those kept somewhere else, such as Quiver
/// Launcher 3's, which come in beside them. `moved` hears each app's new
/// folder as soon as it's there, so the launcher always knows where each one
/// is, whatever happens after.
///
/// No app's files are ever at risk: one moves in a single step on the same
/// drive, else it's copied, the copy checked against the original, and only
/// then the original deleted. Stops at the first app that can't move, which
/// stays as it was; those moved before it play from their new folder, and
/// the apps folder stays `from`.
pub fn move_apps(data: &Path, from: &Path, to: &Path, apps: &[App], progress: impl Fn(Moving), moved: impl Fn(Moved)) -> Result<Done, String> {
    if let Some(why) = refuse(from, to, apps) {
        return Err(why);
    }
    fs::create_dir_all(to).map_err(|e| format!("Couldn't use that folder: {e}"))?;
    let staying = same(from, to);
    if !writable(to) {
        return Err("Quiver can't save files in that folder. Choose another one.".into());
    }
    let mut claimed: Vec<String> = apps.iter().filter(|a| a.dir.is_none()).map(|a| a.folder.clone()).collect();
    let mut plan: Vec<(&App, PathBuf, String)> = vec![];
    for (app, dir) in sources(from, to, apps) {
        // The apps folder's own keep their names, which nothing in `to` has (`refuse`); one kept elsewhere gets a
        // free one.
        let name = if app.dir.is_none() { app.folder.clone() } else { free_name(&app.folder, to, &claimed) };
        claimed.push(name.clone());
        plan.push((app, dir, name));
    }
    let total = plan.len();
    let mut done = Done::default();
    for (i, (app, old, name)) in plan.iter().enumerate() {
        progress(Moving { done: i, total, folder: name.clone(), copied: 0, size: 0 });
        let new = to.join(name);
        match move_one(old, &new, |copied, size| progress(Moving { done: i, total, folder: name.clone(), copied, size })) {
            Ok(left) => {
                moved(Moved { id: app.id.clone(), dir: new.to_string_lossy().into() });
                if *name == app.folder {
                    done.inside.push(app.id.clone());
                }
                done.left.extend(left.map(|p| p.to_string_lossy().into_owned()));
            }
            // Each app moves on its own, so one that can't (a file blocked or in use) stays whole where it was and
            // the rest carry on. One of the apps folder's own goes on playing from the old folder, as one kept
            // elsewhere does, since the apps folder becomes the new one.
            Err(why) => {
                if app.dir.is_none() && !staying {
                    moved(Moved { id: app.id.clone(), dir: old.to_string_lossy().into() });
                }
                done.failed.push(Failed { folder: name.clone(), why });
            }
        }
    }
    if !staying {
        // Downloads under way are never moved; a stale staging folder is just left out.
        let _ = fs::remove_dir_all(from.join(".staging"));
        save(data, to).map_err(|e| e.to_string())?;
    }
    progress(Moving { done: total, total, folder: String::new(), copied: 0, size: 0 });
    Ok(done)
}

/// Where one app is once moved: its folder there, and any of the old copy left to delete.
#[derive(Serialize, Debug)]
pub struct AppMoved {
    pub dir: String,
    pub left: Option<String>,
}

/// Moves one app's folder `from` into the folder `to`, under `folder` (or "folder 2" when that's taken there), the
/// same safe way the apps folder moves: checked for room and a place it can save in first, then copied and checked
/// before the old copy goes. `progress` hears the bytes copied, of the app's size.
pub fn move_app(from: &Path, to: &Path, folder: &str, progress: impl Fn(u64, u64)) -> Result<AppMoved, String> {
    if !to.is_absolute() {
        return Err("Choose a folder on this computer.".into());
    }
    if !from.is_dir() {
        return Err("This app's folder isn't there any more.".into());
    }
    if from.parent().is_some_and(|p| same(p, to)) {
        return Err("It's already in that folder.".into());
    }
    if resolved(to).starts_with(resolved(from)) {
        return Err("Choose a folder outside the app's own folder.".into());
    }
    if !writable(to) {
        return Err("Quiver can't save files in that folder. Choose another one.".into());
    }
    if !same_drive(from, to) {
        if let Some(why) = short_of_space(&[from.to_path_buf()], to, free_space(to)) {
            return Err(why);
        }
    }
    fs::create_dir_all(to).map_err(|e| format!("Couldn't use that folder: {}", explain(&e)))?;
    let new = to.join(free_name(folder, to, &[]));
    let left = move_one(from, &new, progress).map_err(|why| format!("Couldn't move it: {why}. It's still where it was, with all its files."))?;
    Ok(AppMoved { dir: new.to_string_lossy().into(), left: left.map(|p| p.to_string_lossy().into_owned()) })
}

/// Moves a folder from `old` to `new`, which mustn't exist yet. On the same
/// drive that's one rename. Across drives it's copied into a folder beside
/// `new`, the copy checked file by file against the original, then named
/// `new`, and only then the original removed. Until that last step nothing
/// of the original is touched, so any failure leaves it whole (and removes
/// the copy). Resolves to where some of the original was left, when it
/// couldn't all be deleted.
fn move_one(old: &Path, new: &Path, progress: impl Fn(u64, u64)) -> Result<Option<PathBuf>, String> {
    if new.exists() {
        return Err("there's already a folder of its name there".into());
    }
    if fs::rename(old, new).is_ok() {
        return Ok(None);
    }
    move_by_copy(old, new, progress)
}

/// `move_one` where a rename can't do it: another drive, or a file in use.
fn move_by_copy(old: &Path, new: &Path, progress: impl Fn(u64, u64)) -> Result<Option<PathBuf>, String> {
    let name = new.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let copying = new.with_file_name(format!(".{name}.quiver-moving"));
    // A copy that a move before this one left unfinished.
    if copying.exists() {
        fs::remove_dir_all(&copying).map_err(|e| explain(&e))?;
    }
    let size = size_of(old).map_err(|e| explain(&e))?;
    // Told every 32 MB or so, and once it's all there: often enough for a bar to move smoothly.
    let (copied, told) = (std::cell::Cell::new(0u64), std::cell::Cell::new(0u64));
    let result = copy_counting(old, &copying, &|n| {
        copied.set(copied.get() + n);
        if copied.get() - told.get() >= 32 << 20 || copied.get() >= size {
            told.set(copied.get());
            progress(copied.get(), size);
        }
    })
    .map_err(|e| explain(&e))
    .and_then(|_| if same_files(old, &copying) { Ok(()) } else { Err("the copy didn't match the original".into()) })
    .and_then(|_| fs::rename(&copying, new).map_err(|e| explain(&e)));
    if let Err(e) = result {
        let _ = fs::remove_dir_all(&copying);
        return Err(e);
    }
    // A folder with a file open in it can't be renamed, so this tells whether it's in use before deleting anything.
    let old_name = old.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let retired = old.with_file_name(format!(".{old_name}.quiver-moved"));
    if let Err(e) = fs::rename(old, &retired) {
        let _ = fs::remove_dir_all(new);
        return Err(format!("its files are in use ({e}). Close it, and anything with its files open, then try again"));
    }
    Ok(fs::remove_dir_all(&retired).err().map(|_| retired))
}

/// The bytes in a folder's files. A link copied as a link (on Unix) adds none, as copying it counts none.
fn size_of(dir: &Path) -> io::Result<u64> {
    let mut size = 0;
    for entry in fs::read_dir(dir)? {
        let entry = entry?;
        let meta = followed(&entry.path(), entry.file_type()?)?;
        size += if meta.is_dir() { size_of(&entry.path())? } else if meta.file_type().is_symlink() { 0 } else { meta.len() };
    }
    Ok(size)
}

/// An entry's details as a copy of it ends up: a link's own on Unix, where the copy is a link too, else what it points at.
fn followed(path: &Path, kind: fs::FileType) -> io::Result<fs::Metadata> {
    if kind.is_symlink() && cfg!(not(unix)) {
        fs::metadata(path)
    } else {
        fs::symlink_metadata(path)
    }
}

/// Every file and folder in `dir`, by its path in it, with each file's size and link's target.
fn listing(dir: &Path, under: &Path, into: &mut Vec<(PathBuf, String)>) -> io::Result<()> {
    for entry in fs::read_dir(dir)? {
        let entry = entry?;
        let (path, rel) = (entry.path(), under.join(entry.file_name()));
        let meta = followed(&path, entry.file_type()?)?;
        if meta.is_symlink() {
            into.push((rel, format!("link {}", fs::read_link(&path)?.display())));
        } else if meta.is_dir() {
            into.push((rel.clone(), "folder".into()));
            listing(&path, &rel, into)?;
        } else {
            into.push((rel, format!("file {}", meta.len())));
        }
    }
    Ok(())
}

/// Whether two folders hold the same files and folders, each file the same size.
fn same_files(a: &Path, b: &Path) -> bool {
    let list = |d: &Path| {
        let mut l = vec![];
        listing(d, Path::new(""), &mut l).map(|_| {
            l.sort();
            l
        })
    };
    matches!((list(a), list(b)), (Ok(a), Ok(b)) if a == b)
}

/// Copies a folder, keeping symbolic links as links (on Unix) and each file's
/// permissions, with each file flushed to the drive; `copied` hears the bytes
/// as they go.
fn copy_counting(from: &Path, to: &Path, copied: &dyn Fn(u64)) -> io::Result<()> {
    use std::io::{Read, Write};
    fs::create_dir_all(to)?;
    for entry in fs::read_dir(from)? {
        let entry = entry?;
        let (src, dst) = (entry.path(), to.join(entry.file_name()));
        let kind = entry.file_type()?;
        if kind.is_dir() {
            copy_counting(&src, &dst, copied)?;
            continue;
        }
        #[cfg(unix)]
        if kind.is_symlink() {
            std::os::unix::fs::symlink(fs::read_link(&src)?, &dst)?;
            continue;
        }
        let (mut input, mut output) = (fs::File::open(&src)?, fs::File::create(&dst)?);
        let mut buffer = vec![0; 1 << 20];
        loop {
            let n = input.read(&mut buffer)?;
            if n == 0 {
                break;
            }
            output.write_all(&buffer[..n])?;
            copied(n as u64);
        }
        output.sync_all()?;
        fs::set_permissions(&dst, fs::metadata(&src)?.permissions())?;
    }
    Ok(())
}

/// Renames an app's folder in `apps` from `from` to `to`, and its Proton
/// prefix in `proton` with it, so it keeps its Wine settings and saves. Never
/// onto a folder that's already there; a change of case alone is the same
/// folder, so it goes ahead. Fails with nothing changed.
pub fn rename(apps: &Path, proton: &Path, from: &str, to: &str) -> Result<(), String> {
    if from == to {
        return Ok(());
    }
    let (old, new) = (apps.join(from), apps.join(to));
    if !old.is_dir() {
        return Err("This app's folder isn't there any more.".into());
    }
    let same = |a: &Path, b: &Path| a.canonicalize().ok().zip(b.canonicalize().ok()).is_some_and(|(a, b)| a == b);
    if new.exists() && !same(&old, &new) {
        return Err(format!("There's already a folder named {to} in your apps folder. Choose another name."));
    }
    let (old_prefix, new_prefix) = (proton.join(from), proton.join(to));
    let prefix = old_prefix.is_dir();
    if prefix && new_prefix.exists() && !same(&old_prefix, &new_prefix) {
        return Err(format!("There's already a Proton folder named {to}. Choose another name."));
    }
    fs::rename(&old, &new).map_err(|e| format!("Couldn't rename {from}: {e}. Close the app, and anything with its files open, then try again."))?;
    if prefix {
        if let Err(e) = fs::rename(&old_prefix, &new_prefix) {
            let _ = fs::rename(&new, &old);
            return Err(format!("Couldn't rename {from}'s Proton folder: {e}. Close the app, then try again."));
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn moves_installed_apps_and_remembers_the_folder() {
        let data = tempfile::tempdir().unwrap();
        let elsewhere = tempfile::tempdir().unwrap();
        let from = current(data.path());
        assert_eq!(from, data.path().join("apps"));
        fs::create_dir_all(from.join("Game/data")).unwrap();
        fs::write(from.join("Game/data/save.dat"), "saved").unwrap();
        fs::create_dir_all(from.join(".staging/x")).unwrap();
        let to = elsewhere.path().join("Games");
        let steps = std::cell::RefCell::new(vec![]);
        let moved = std::cell::RefCell::new(vec![]);
        let done = move_apps(data.path(), &from, &to, &[app("a", "Game"), app("b", "Gone")], |m| steps.borrow_mut().push((m.done, m.total)), |m| moved.borrow_mut().push((m.id, m.dir))).unwrap();
        assert_eq!(fs::read_to_string(to.join("Game/data/save.dat")).unwrap(), "saved");
        assert!(!from.join("Game").exists() && !from.join(".staging").exists());
        assert_eq!(current(data.path()), to);
        assert_eq!(steps.into_inner(), vec![(0, 1), (1, 1)]);
        assert_eq!(moved.into_inner(), vec![("a".to_string(), to.join("Game").to_string_lossy().into_owned())]);
        assert_eq!(done.inside, ["a"]);
    }

    fn app(id: &str, folder: &str) -> App {
        App { id: id.into(), folder: folder.into(), dir: None }
    }

    fn kept(id: &str, folder: &str, dir: &Path) -> App {
        App { id: id.into(), folder: folder.into(), dir: Some(dir.to_string_lossy().into()) }
    }

    #[test]
    fn brings_quiver_3_apps_in_beside_the_others_with_their_saves() {
        let data = tempfile::tempdir().unwrap();
        let v3 = tempfile::tempdir().unwrap();
        let from = current(data.path());
        fs::create_dir_all(from.join("sm64")).unwrap();
        // 3.x named its folders its own way; one's name is taken by an app already here.
        fs::create_dir_all(v3.path().join("Apps/Zelda64Recomp/saves")).unwrap();
        fs::write(v3.path().join("Apps/Zelda64Recomp/saves/file1.sav"), "link").unwrap();
        fs::create_dir_all(v3.path().join("Apps/SM64")).unwrap();
        fs::write(v3.path().join("Apps/SM64/game.exe"), "mario").unwrap();
        let to = data.path().join("Games");
        let apps = [app("a", "sm64"), kept("z", "zelda", &v3.path().join("Apps/Zelda64Recomp")), kept("m", "sm64", &v3.path().join("Apps/SM64")), kept("gone", "x", &v3.path().join("Apps/Gone"))];
        let steps = std::cell::RefCell::new(vec![]);
        let done = move_apps(data.path(), &from, &to, &apps, |m| steps.borrow_mut().push((m.done, m.total)), |_| {}).unwrap();
        assert_eq!(fs::read_to_string(to.join("zelda/saves/file1.sav")).unwrap(), "link");
        assert_eq!(fs::read_to_string(to.join("sm64 2/game.exe")).unwrap(), "mario");
        assert!(to.join("sm64").is_dir());
        assert!(!v3.path().join("Apps/Zelda64Recomp").exists() && !v3.path().join("Apps/SM64").exists());
        // The one under another name keeps its own folder (`dir`).
        assert_eq!(done.inside, ["a", "z"]);
        assert_eq!(steps.into_inner().last(), Some(&(3, 3)));
    }

    #[test]
    fn brings_quiver_3_apps_into_the_apps_folder_it_already_is() {
        let data = tempfile::tempdir().unwrap();
        let v3 = tempfile::tempdir().unwrap();
        let from = current(data.path());
        fs::create_dir_all(&from).unwrap();
        fs::create_dir_all(v3.path().join("Game")).unwrap();
        let apps = [kept("g", "game", &v3.path().join("Game"))];
        assert!(refuse(&from, &from, &[]).is_some());
        assert!(refuse(&from, &from, &apps).is_none());
        let done = move_apps(data.path(), &from, &from, &apps, |_| {}, |_| {}).unwrap();
        assert!(from.join("game").is_dir() && !v3.path().join("Game").exists());
        assert_eq!(done.inside, ["g"]);
        // Once there, there's nothing left to bring.
        assert!(refuse(&from, &from, &[kept("g", "game", &from.join("game"))]).is_some());
    }

    #[test]
    fn copies_across_drives_checks_the_copy_then_removes_the_original() {
        let root = tempfile::tempdir().unwrap();
        let old = root.path().join("Game");
        fs::create_dir_all(old.join("data/empty")).unwrap();
        fs::write(old.join("data/save.dat"), vec![7u8; 3 << 20]).unwrap();
        fs::write(old.join("game.exe"), "run").unwrap();
        #[cfg(unix)]
        std::os::unix::fs::symlink("data/save.dat", old.join("link")).unwrap();
        let copy = root.path().join("copy");
        let reports = std::cell::RefCell::new(vec![]);
        copy_counting(&old, &copy, &|n| reports.borrow_mut().push(n)).unwrap();
        assert!(same_files(&old, &copy));
        assert_eq!(reports.into_inner().iter().sum::<u64>(), (3 << 20) + 3);
        assert_eq!(size_of(&old).unwrap(), (3 << 20) + 3);
        #[cfg(unix)]
        assert_eq!(fs::read_link(copy.join("link")).unwrap(), Path::new("data/save.dat"));
        // A file short, missing or extra: not the same.
        fs::write(copy.join("game.exe"), "ru").unwrap();
        assert!(!same_files(&old, &copy));
        fs::write(copy.join("game.exe"), "run").unwrap();
        fs::remove_dir(copy.join("data/empty")).unwrap();
        assert!(!same_files(&old, &copy));
        fs::create_dir(copy.join("data/empty")).unwrap();
        fs::write(copy.join("extra"), "").unwrap();
        assert!(!same_files(&old, &copy));
    }

    #[test]
    fn an_app_that_cant_move_stays_whole_where_it_was_and_the_rest_carry_on() {
        let data = tempfile::tempdir().unwrap();
        let from = current(data.path());
        for name in ["First", "Second", "Third"] {
            fs::create_dir_all(from.join(name)).unwrap();
        }
        fs::write(from.join("Second/save.dat"), "saved").unwrap();
        let to = data.path().join("Games");
        fs::create_dir_all(&to).unwrap();
        let moved = std::cell::RefCell::new(vec![]);
        // Something takes Second's name in the new folder once the move has begun.
        let done = move_apps(data.path(), &from, &to, &[app("1", "First"), app("2", "Second"), app("3", "Third")], |m| {
            if m.folder == "Second" && m.copied == 0 {
                fs::create_dir_all(to.join("Second")).unwrap();
            }
        }, |m| moved.borrow_mut().push((m.id, m.dir)))
        .unwrap();
        assert_eq!(done.failed.len(), 1);
        assert_eq!(done.failed[0].folder, "Second");
        assert_eq!(fs::read_to_string(from.join("Second/save.dat")).unwrap(), "saved");
        assert!(to.join("First").is_dir() && to.join("Third").is_dir());
        assert_eq!(done.inside, ["1", "3"]);
        // The apps folder is the new one, and the app left behind plays from the old one.
        assert_eq!(current(data.path()), to);
        let second = from.join("Second").to_string_lossy().into_owned();
        assert!(moved.into_inner().contains(&("2".to_string(), second)));
    }

    #[test]
    fn says_in_plain_words_when_windows_security_blocks_a_file() {
        let blocked = explain(&io::Error::from_raw_os_error(225));
        if cfg!(windows) {
            assert!(blocked.contains("Windows Security"), "{blocked}");
        }
        assert!(explain(&io::Error::from(io::ErrorKind::StorageFull)).contains("ran out of room"));
    }

    #[test]
    fn refuses_the_same_folder_one_inside_it_or_a_name_already_taken() {
        let root = tempfile::tempdir().unwrap();
        let from = root.path().join("apps");
        fs::create_dir_all(from.join("Game")).unwrap();
        let folders = [app("g", "Game")];
        assert!(refuse(&from, &from, &folders).is_some());
        assert!(refuse(&from, &from.join("More"), &folders).is_some());
        // Named another way (here through a link) while the new folder doesn't exist yet.
        #[cfg(unix)]
        {
            std::os::unix::fs::symlink(root.path(), root.path().join("link")).unwrap();
            assert!(refuse(&from, &root.path().join("link/apps/More"), &folders).is_some());
        }
        assert!(refuse(&from, Path::new("relative"), &folders).is_some());
        let to = root.path().join("Games");
        assert!(refuse(&from, &to, &folders).is_none());
        fs::create_dir_all(to.join("Game")).unwrap();
        assert!(refuse(&from, &to, &folders).unwrap().contains("Game"));
        // Not into the folder of an app kept elsewhere, which would move into itself.
        fs::create_dir_all(root.path().join("v3/Apps/Zelda")).unwrap();
        let kept_apps = [kept("z", "zelda", &root.path().join("v3/Apps/Zelda"))];
        assert!(refuse(&from, &root.path().join("v3/Apps/Zelda/in"), &kept_apps).unwrap().contains("zelda"));
        assert!(refuse(&from, &root.path().join("v3"), &kept_apps).is_none());
    }

    #[test]
    fn renames_an_app_folder_and_its_proton_prefix() {
        let data = tempfile::tempdir().unwrap();
        let (apps, proton) = (data.path().join("apps"), data.path().join("proton"));
        fs::create_dir_all(apps.join("ssx3/data")).unwrap();
        fs::write(apps.join("ssx3/data/save.dat"), "saved").unwrap();
        fs::create_dir_all(proton.join("ssx3/pfx")).unwrap();
        rename(&apps, &proton, "ssx3", "SSX 3").unwrap();
        assert_eq!(fs::read_to_string(apps.join("SSX 3/data/save.dat")).unwrap(), "saved");
        assert!(proton.join("SSX 3/pfx").is_dir());
        assert!(!apps.join("ssx3").exists() && !proton.join("ssx3").exists());
        // No prefix (never run with Proton): only the folder.
        rename(&apps, &proton, "SSX 3", "Racing").unwrap();
        assert!(apps.join("Racing/data").is_dir() && proton.join("Racing/pfx").is_dir());
        fs::remove_dir_all(proton.join("Racing")).unwrap();
        rename(&apps, &proton, "Racing", "SSX").unwrap();
        assert!(apps.join("SSX/data").is_dir() && !proton.join("SSX").exists());
    }

    #[test]
    fn a_change_of_case_renames_the_same_folder() {
        let data = tempfile::tempdir().unwrap();
        let apps = data.path().join("apps");
        fs::create_dir_all(apps.join("ssx")).unwrap();
        rename(&apps, &data.path().join("proton"), "ssx", "SSX").unwrap();
        let names: Vec<_> = fs::read_dir(&apps).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(names, ["SSX"]);
    }

    #[test]
    fn refuses_a_name_taken_or_a_folder_gone_and_changes_nothing() {
        let data = tempfile::tempdir().unwrap();
        let (apps, proton) = (data.path().join("apps"), data.path().join("proton"));
        fs::create_dir_all(apps.join("Game")).unwrap();
        fs::create_dir_all(apps.join("Other")).unwrap();
        assert!(rename(&apps, &proton, "Game", "Other").unwrap_err().contains("Other"));
        assert!(rename(&apps, &proton, "Gone", "New").unwrap_err().contains("isn't there"));
        // Its prefix's new name is taken: the folder isn't renamed either.
        fs::create_dir_all(proton.join("Game")).unwrap();
        fs::create_dir_all(proton.join("New")).unwrap();
        assert!(rename(&apps, &proton, "Game", "New").unwrap_err().contains("Proton"));
        assert!(apps.join("Game").is_dir() && !apps.join("New").exists());
        rename(&apps, &proton, "Game", "Game").unwrap();
    }

    #[test]
    fn refuses_a_drive_without_room_for_what_has_to_be_copied() {
        let root = tempfile::tempdir().unwrap();
        let game = root.path().join("v3/Apps/Game");
        fs::create_dir_all(&game).unwrap();
        fs::write(game.join("data.bin"), vec![0u8; 1 << 20]).unwrap();
        let to = Path::new("D:\\Games");
        // 1 MB to copy, plus the room kept spare.
        assert!(short_of_space(&[game.clone()], to, Some(SPARE + (1 << 20))).is_none());
        let why = short_of_space(&[game.clone()], to, Some(SPARE)).unwrap();
        assert!(why.contains("0.5 GB free") && why.contains("need 0.5 GB"), "{why}");
        // Free space unknown, or nothing to copy (a move on the same drive is a rename): nothing to say.
        assert!(short_of_space(&[game], to, None).is_none());
        assert!(short_of_space(&[], to, Some(0)).is_none());
    }

    #[test]
    fn says_whether_each_apps_folder_is_there_and_its_room() {
        let root = tempfile::tempdir().unwrap();
        let gone = root.path().join("unplugged");
        let found = states(&[root.path().to_path_buf(), gone]);
        assert!(found[0].exists && found[0].free.is_some_and(|f| f > 0));
        assert_eq!(found[1], FolderState { exists: false, free: None });
    }

    #[test]
    fn switches_where_new_apps_go_moving_nothing() {
        let data = tempfile::tempdir().unwrap();
        let from = current(data.path());
        fs::create_dir_all(from.join("Game")).unwrap();
        let to = data.path().join("Games");
        // The launcher has recorded Game where it is first, so nothing is asked to move.
        let done = move_apps(data.path(), &from, &to, &[], |_| {}, |_| {}).unwrap();
        assert!(done.inside.is_empty() && done.failed.is_empty());
        assert_eq!(current(data.path()), to);
        assert!(from.join("Game").is_dir());
        // Not one inside the folder it was.
        assert!(move_apps(data.path(), &to, &to.join("More"), &[], |_| {}, |_| {}).is_err());
    }

    #[test]
    fn moves_one_app_beside_whatever_is_there_and_refuses_where_it_cant_go() {
        let root = tempfile::tempdir().unwrap();
        let from = root.path().join("apps/Game");
        fs::create_dir_all(from.join("data")).unwrap();
        fs::write(from.join("data/save.dat"), "saved").unwrap();
        let to = root.path().join("N64");
        fs::create_dir_all(to.join("Game")).unwrap();
        fs::write(to.join("Game/theirs.txt"), "the player's").unwrap();
        // Not into its own folder, nor where it already is.
        assert!(move_app(&from, &from.join("inside"), "Game", |_, _| {}).is_err());
        assert!(move_app(&from, &root.path().join("apps"), "Game", |_, _| {}).is_err());
        assert!(move_app(&from, Path::new("relative"), "Game", |_, _| {}).is_err());
        let moved = move_app(&from, &to, "Game", |_, _| {}).unwrap();
        assert_eq!(PathBuf::from(&moved.dir), to.join("Game 2"));
        assert!(moved.left.is_none() && !from.exists());
        assert_eq!(fs::read_to_string(to.join("Game 2/data/save.dat")).unwrap(), "saved");
        assert_eq!(fs::read_to_string(to.join("Game/theirs.txt")).unwrap(), "the player's");
        // Gone: nothing to move.
        assert!(move_app(&from, &to, "Game", |_, _| {}).is_err());
    }

    #[test]
    fn knows_the_free_space_and_drive_of_a_folder_not_made_yet() {
        let root = tempfile::tempdir().unwrap();
        let later = root.path().join("not/made/yet");
        assert!(free_space(&later).is_some_and(|f| f > 0));
        assert!(same_drive(root.path(), &later));
        assert!(writable(&later));
        // Nothing was left behind by the check.
        assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
    }

    #[cfg(unix)]
    #[test]
    fn refuses_a_folder_it_cant_save_in() {
        use std::os::unix::fs::PermissionsExt;
        let root = tempfile::tempdir().unwrap();
        let locked = root.path().join("locked");
        fs::create_dir(&locked).unwrap();
        fs::set_permissions(&locked, fs::Permissions::from_mode(0o555)).unwrap();
        // Root can write anywhere, so this only says something for an ordinary account.
        if fs::write(locked.join("x"), "").is_err() {
            assert!(!writable(&locked.join("Games")));
            let from = root.path().join("apps");
            assert!(refuse(&from, &locked.join("Games"), &[]).unwrap().contains("can't save"));
        }
        fs::set_permissions(&locked, fs::Permissions::from_mode(0o755)).unwrap();
    }

    #[test]
    fn a_move_by_copy_ends_with_one_whole_copy_in_the_new_place() {
        let root = tempfile::tempdir().unwrap();
        let (old, new) = (root.path().join("v3/Game"), root.path().join("Games/Game"));
        fs::create_dir_all(old.join("saves")).unwrap();
        fs::write(old.join("saves/1.sav"), vec![1u8; 40 << 20]).unwrap();
        fs::create_dir_all(new.parent().unwrap()).unwrap();
        // An unfinished copy from a move that stopped part way is started over.
        fs::create_dir_all(root.path().join("Games/.Game.quiver-moving/stale")).unwrap();
        let reports = std::cell::RefCell::new(vec![]);
        assert_eq!(move_by_copy(&old, &new, |c, s| reports.borrow_mut().push((c, s))).unwrap(), None);
        assert_eq!(fs::metadata(new.join("saves/1.sav")).unwrap().len(), 40 << 20);
        assert!(!new.join("stale").exists() && !old.exists());
        let names: Vec<_> = fs::read_dir(root.path().join("Games")).unwrap().map(|e| e.unwrap().file_name()).collect();
        assert_eq!(names, ["Game"]);
        assert!(!root.path().join("v3/.Game.quiver-moved").exists());
        // Reported now and then, not for every chunk, ending with all of it.
        let reports = reports.into_inner();
        assert!(reports.len() < 10, "{reports:?}");
        assert_eq!(reports.last(), Some(&(40 << 20, 40 << 20)));
    }

    #[test]
    fn a_move_by_copy_that_cant_finish_leaves_the_original_and_no_copy() {
        let root = tempfile::tempdir().unwrap();
        let (old, new) = (root.path().join("Game"), root.path().join("Games/Game"));
        fs::create_dir_all(&old).unwrap();
        fs::write(old.join("save.dat"), "saved").unwrap();
        // The new place can't be written to: there's a file where its folder should go.
        fs::write(root.path().join("Games"), "").unwrap();
        assert!(move_by_copy(&old, &new, |_, _| {}).is_err());
        assert_eq!(fs::read_to_string(old.join("save.dat")).unwrap(), "saved");
        assert!(!root.path().join(".Game.quiver-moved").exists());
    }

    /// Windows won't rename a folder with a file open in it: the copy goes, the original stays.
    #[cfg(windows)]
    #[test]
    fn a_move_by_copy_of_an_app_in_use_leaves_it_where_it_is() {
        let root = tempfile::tempdir().unwrap();
        let (old, new) = (root.path().join("Game"), root.path().join("Games/Game"));
        fs::create_dir_all(&old).unwrap();
        fs::write(old.join("save.dat"), "saved").unwrap();
        fs::create_dir_all(root.path().join("Games")).unwrap();
        // Open as a game has its files: others may read it, not move or delete it.
        use std::os::windows::fs::OpenOptionsExt;
        let open = fs::OpenOptions::new().read(true).share_mode(1).open(old.join("save.dat")).unwrap();
        let err = move_by_copy(&old, &new, |_, _| {}).unwrap_err();
        assert!(err.contains("in use"), "{err}");
        drop(open);
        assert_eq!(fs::read_to_string(old.join("save.dat")).unwrap(), "saved");
        assert!(!new.exists());
    }
}
