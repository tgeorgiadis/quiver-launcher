//! The player's own copy of a game, for an app that plays it (a port of a console game): a ROM, a disc image or a
//! folder of the game's files. The catalog says what the copy is and how the app gets it (`setupFiles`, from
//! quiverlauncher.com): put somewhere in the app's folder or the system's folders for apps' data, passed after a
//! command-line flag or on its own, or named in an environment variable (launch.rs adds those at every start). Here it's
//! found where players keep copies, checked (by the dumps the catalog lists, else by its kind), and put in place.
//! Quiver never downloads or ships game files: it reads the player's, and writes them nowhere but where the app
//! they're for reads them.
use serde::{Deserialize, Serialize};
use sha1::Sha1;
use sha2::{Digest, Sha256};
use std::collections::HashSet;
use std::fs;
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};

/// One thing the player provides, as the catalog describes it (`SetupFile` in packages/api).
#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase")]
pub struct SetupFile {
    pub id: String,
    pub label: String,
    /// "file" (unset) or "folder".
    #[serde(default)]
    pub kind: Option<String>,
    /// Where the copy goes, under `root`.
    #[serde(default)]
    pub destination: Option<String>,
    /// "app" (unset), "data", "config" or "localData".
    #[serde(default)]
    pub root: Option<String>,
    #[serde(default)]
    pub flag: Option<String>,
    /// Its path is passed as an argument of its own, with no flag.
    #[serde(default)]
    pub positional: bool,
    #[serde(default)]
    pub env: Option<String>,
    /// For a folder: names it must hold.
    #[serde(default)]
    pub contains: Vec<String>,
    #[serde(default)]
    pub extensions: Vec<String>,
    #[serde(default)]
    pub accepted: Vec<Dump>,
}

/// A dump the app takes: its hash, as No-Intro, Redump or the app's own list gives it.
#[derive(Deserialize, Clone, Debug)]
pub struct Dump {
    /// "sha1" or "sha256".
    pub algo: String,
    /// Lowercase hex.
    pub value: String,
    #[serde(default)]
    pub label: Option<String>,
}

impl SetupFile {
    fn folder(&self) -> bool {
        self.kind.as_deref() == Some("folder")
    }
    /// The extensions it can be, lowercase. An N64 ROM's three byte orders are all one ROM.
    fn wanted(&self) -> Vec<String> {
        let mut out: Vec<String> = self.extensions.iter().map(|e| e.trim_start_matches('.').to_ascii_lowercase()).collect();
        if out.iter().any(|e| N64.contains(&e.as_str())) {
            out.extend(N64.iter().map(|e| e.to_string()));
        }
        let mut seen = HashSet::new();
        out.retain(|e| seen.insert(e.clone()));
        out
    }
    fn n64(&self) -> bool {
        self.wanted().iter().any(|e| N64.contains(&e.as_str()))
    }
}

const N64: [&str; 3] = ["z64", "n64", "v64"];
/// Archives a copy can be packed in: unpacked first, unless the app reads them.
const ARCHIVES: [&str; 3] = ["zip", "7z", "rar"];
/// Compressed disc images, which apps that read plain ones can't.
const COMPRESSED: [&str; 9] = ["rvz", "wia", "gcz", "wbfs", "chd", "cso", "zso", "ciso", "nkit"];
/// Never anyone's game: pictures, documents, videos, shortcuts.
const NOT_A_GAME: [&str; 16] = ["jpg", "jpeg", "png", "gif", "bmp", "webp", "txt", "pdf", "doc", "docx", "html", "url", "lnk", "mp4", "mkv", "mov"];

/// How an N64 ROM's bytes are ordered. No-Intro hashes them big-endian (.z64), as the cartridge holds them; a .v64
/// has each pair swapped, an .n64 each four reversed.
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Order {
    Big,
    Swapped,
    Little,
}
impl Order {
    pub fn of(head: &[u8]) -> Option<Order> {
        match head.get(..4)? {
            [0x80, 0x37, 0x12, 0x40] => Some(Order::Big),
            [0x37, 0x80, 0x40, 0x12] => Some(Order::Swapped),
            [0x40, 0x12, 0x37, 0x80] => Some(Order::Little),
            _ => None,
        }
    }
    /// Puts a piece (a multiple of four bytes long) in big-endian order.
    pub fn to_big(self, piece: &mut [u8]) {
        match self {
            Order::Big => {}
            Order::Swapped => piece.chunks_exact_mut(2).for_each(|c| c.swap(0, 1)),
            Order::Little => piece.chunks_exact_mut(4).for_each(|c| c.reverse()),
        }
    }
}

/// Read a mebibyte at a time: a multiple of four, as N64 byte orders swap in fours.
const PIECE: usize = 1 << 20;
/// How often a long read says how far it's got.
const EVERY: u64 = 16 << 20;

/// Fills `buf` from `reader` as far as it goes: short only at the end.
fn fill(reader: &mut impl Read, buf: &mut [u8]) -> io::Result<usize> {
    let mut n = 0;
    while n < buf.len() {
        match reader.read(&mut buf[n..]) {
            Ok(0) => break,
            Ok(k) => n += k,
            Err(e) if e.kind() == io::ErrorKind::Interrupted => {}
            Err(e) => return Err(e),
        }
    }
    Ok(n)
}

/// A read that stopped partway, said for a player: a disc, SD card or drive going bad shows here.
fn unreadable(at: u64, e: io::Error) -> String {
    format!("Couldn't read the whole copy: it stopped {} MB in ({e}). It may be on a drive that's failing, or still being copied.", at >> 20)
}

/// The copy's hashes: as it is, and (for an N64 ROM not in big-endian order) as No-Intro lists it.
struct Hashes {
    size: u64,
    sha1: String,
    sha256: String,
    big: Option<(String, String)>,
}
impl Hashes {
    fn matches(&self, dump: &Dump) -> bool {
        let value = dump.value.to_ascii_lowercase();
        let (own, big) = match dump.algo.as_str() {
            "sha1" => (&self.sha1, self.big.as_ref().map(|b| &b.0)),
            "sha256" => (&self.sha256, self.big.as_ref().map(|b| &b.1)),
            _ => return false,
        };
        *own == value || big.is_some_and(|b| *b == value)
    }
}

fn hash(path: &Path, n64: bool, progress: &dyn Fn(u64, u64)) -> Result<Hashes, String> {
    let mut f = fs::File::open(path).map_err(|e| format!("Couldn't open the copy: {e}"))?;
    let total = f.metadata().map(|m| m.len()).unwrap_or(0);
    let mut buf = vec![0u8; PIECE];
    let (mut s1, mut s256) = (Sha1::new(), Sha256::new());
    let (mut b1, mut b256) = (Sha1::new(), Sha256::new());
    let mut order = None;
    let mut read = 0u64;
    let mut said = 0u64;
    loop {
        let n = fill(&mut f, &mut buf).map_err(|e| unreadable(read, e))?;
        if n == 0 {
            break;
        }
        let piece = &mut buf[..n];
        s1.update(&*piece);
        s256.update(&*piece);
        if n64 {
            if read == 0 {
                order = Order::of(piece).filter(|o| *o != Order::Big);
            }
            if let Some(o) = order {
                o.to_big(piece);
                b1.update(&*piece);
                b256.update(&*piece);
            }
        }
        read += n as u64;
        if read - said >= EVERY {
            said = read;
            progress(read, total);
        }
    }
    progress(read, total);
    Ok(Hashes {
        size: read,
        sha1: hex::encode(s1.finalize()),
        sha256: hex::encode(s256.finalize()),
        big: order.map(|_| (hex::encode(b1.finalize()), hex::encode(b256.finalize()))),
    })
}

/// What checking a copy found.
#[derive(Serialize, Debug, PartialEq, Default)]
#[serde(rename_all = "camelCase")]
pub struct Checked {
    /// It's one the app takes.
    pub ok: bool,
    /// It matched one of the dumps the catalog lists (not only its kind of file).
    pub verified: bool,
    /// That dump's name ("US 1.0"), when it has one.
    pub dump: Option<String>,
    /// Why not, for the player.
    pub problem: Option<String>,
    pub size: u64,
    /// Its SHA-1, when it was read through: shown when it isn't a dump the app takes.
    pub sha1: Option<String>,
}
fn problem(text: impl Into<String>) -> Checked {
    Checked { problem: Some(text.into()), ..Default::default() }
}

/// The file names in a folder, lowercase.
fn names_in(dir: &Path) -> HashSet<String> {
    fs::read_dir(dir).into_iter().flatten().flatten().map(|e| e.file_name().to_string_lossy().to_lowercase()).collect()
}
/// Whether `dir` holds `path` (a name, or names with / between), ignoring case.
fn holds(dir: &Path, path: &str) -> bool {
    let mut at = dir.to_path_buf();
    for part in path.split('/').filter(|p| !p.is_empty()) {
        let want = part.to_lowercase();
        let Some(found) = fs::read_dir(&at).into_iter().flatten().flatten().find(|e| e.file_name().to_string_lossy().to_lowercase() == want) else {
            return false;
        };
        at = found.path();
    }
    true
}

/// A file the Flatpak's file dialog handed over through its document portal: the launcher can read it, but a game
/// started outside the sandbox can't see that path.
fn portal_path(path: &Path) -> bool {
    let p = path.to_string_lossy();
    p.starts_with("/run/user/") && p.contains("/doc/")
}

/// Whether `path` is a copy the app takes: the right kind of file or folder, and one of the dumps it lists, when
/// the catalog lists any. Reads it through once (`progress`: read, of how much).
pub fn check(file: &SetupFile, path: &Path, progress: &dyn Fn(u64, u64)) -> Checked {
    if !path.exists() {
        return problem("That copy isn't there any more.");
    }
    if (file.flag.is_some() || file.positional || file.env.is_some()) && file.destination.is_none() && portal_path(path) {
        return problem("The game can't see files picked from there. Pick it from your home folder, a game drive or an SD card.");
    }
    if file.folder() {
        if !path.is_dir() {
            return problem("It needs the game's folder, not a file. Pick the folder your copy is in.");
        }
        if let Some(missing) = file.contains.iter().find(|name| !holds(path, name)) {
            return problem(format!("That folder doesn't have {missing} in it. Pick the folder with your copy of the game."));
        }
        if file.contains.is_empty() && names_in(path).is_empty() {
            return problem("That folder is empty. Pick the folder with your copy of the game.");
        }
        return Checked { ok: true, ..Default::default() };
    }
    if path.is_dir() {
        return problem("It needs the file itself, not a folder.");
    }
    let ext = path.extension().map(|e| e.to_string_lossy().to_ascii_lowercase()).unwrap_or_default();
    let wanted = file.wanted();
    let takes = |e: &str| wanted.iter().any(|w| w == e);
    if ARCHIVES.contains(&ext.as_str()) && !takes(&ext) {
        return problem(format!("That's a compressed file (.{ext}). Unpack it first, then pick the game file inside it."));
    }
    if COMPRESSED.contains(&ext.as_str()) && !takes(&ext) {
        return problem(format!("That's a compressed .{ext} disc image, and this game reads plain ones. Convert it to .iso first (Dolphin can: right-click the game, Convert File)."));
    }
    if NOT_A_GAME.contains(&ext.as_str()) {
        return problem("That's a picture, document or video, not your copy of the game.");
    }
    if !wanted.is_empty() && !takes(&ext) {
        let list = wanted.iter().map(|e| format!(".{e}")).collect::<Vec<_>>();
        let list = match list.split_last() {
            Some((last, rest)) if !rest.is_empty() => format!("{} or {last}", rest.join(", ")),
            _ => list.join(""),
        };
        return problem(format!("It needs a {list} file."));
    }
    let size = fs::metadata(path).map(|m| m.len()).unwrap_or(0);
    if size == 0 {
        return problem("That file is empty.");
    }
    if file.accepted.is_empty() {
        return Checked { ok: true, size, ..Default::default() };
    }
    let hashes = match hash(path, file.n64(), progress) {
        Ok(h) => h,
        Err(e) => return problem(e),
    };
    match file.accepted.iter().find(|d| hashes.matches(d)) {
        Some(dump) => Checked { ok: true, verified: true, dump: dump.label.clone(), size: hashes.size, sha1: Some(hashes.sha1), problem: None },
        None => Checked {
            problem: Some("This isn't a copy this game takes. It may be another region or version, or a changed or damaged copy.".into()),
            size: hashes.size,
            sha1: Some(hashes.big.map(|b| b.0).unwrap_or(hashes.sha1)),
            ..Default::default()
        },
    }
}

/// A destination as the catalog writes it: a relative path, / between its parts, never climbing out.
fn relative(destination: &str) -> Result<PathBuf, String> {
    let parts: Vec<&str> = destination.split('/').collect();
    let bad = destination.is_empty()
        || destination.contains('\\')
        || destination.starts_with('/')
        || destination.contains(':')
        || parts.len() > 4
        || parts.iter().any(|p| p.is_empty() || *p == "." || *p == ".." || !plain(p));
    if bad {
        return Err(format!("The catalog's place for this copy ({destination}) isn't one Quiver can use."));
    }
    Ok(parts.iter().collect())
}

/// Whether a part of a destination is a plain file name on every system: none of the characters a command line or
/// Windows reads as more than a name, and no space or dot at its end (which Windows drops).
fn plain(part: &str) -> bool {
    !part.chars().any(|c| c.is_control() || "<>\"|?*%&^!`$;".contains(c)) && !part.ends_with([' ', '.']) && !part.starts_with(' ')
}

/// The home folder games see: in the Flatpak, the host's (games start outside the sandbox), not the sandbox's.
fn host_home() -> Option<PathBuf> {
    std::env::var_os("HOME").map(PathBuf::from).filter(|h| h.is_absolute()).or_else(dirs::home_dir)
}
/// The system's folders for apps' data, as the game reads them. `XDG_*` is the sandbox's own in the Flatpak, so
/// there it's the usual place under the host's home.
fn linux_dir(xdg: &str, under_home: &str) -> Option<PathBuf> {
    if crate::sandbox::id().is_none() {
        if let Some(dir) = std::env::var_os(xdg).map(PathBuf::from).filter(|d| d.is_absolute()) {
            return Some(dir);
        }
    }
    host_home().map(|h| h.join(under_home))
}

/// Where a destination's `root` is. `wine`: the app is a Windows build run through Wine or Proton here, whose
/// %APPDATA% is inside its Wine prefix, which Quiver doesn't reach into.
pub fn root_dir(root: Option<&str>, app: &Path, wine: bool) -> Result<PathBuf, String> {
    let root = root.unwrap_or("app");
    if root == "app" {
        return Ok(app.to_path_buf());
    }
    if wine {
        return Err("This copy goes in a folder Windows keeps, and this app runs through Wine or Proton here. Pick it in the game's own setup instead.".into());
    }
    let dir = match (crate::OS, root) {
        ("windows", "data" | "config") => dirs::config_dir(),
        ("windows", "localData") => dirs::data_local_dir(),
        ("macos", "data" | "config" | "localData") => dirs::data_dir(),
        ("linux", "data" | "localData") => linux_dir("XDG_DATA_HOME", ".local/share"),
        ("linux", "config") => linux_dir("XDG_CONFIG_HOME", ".config"),
        _ => None,
    };
    dir.ok_or_else(|| format!("Quiver doesn't know where {root} is on this computer."))
}

/// Where the copy goes for this app, if it goes anywhere (it may only be passed by flag or environment variable).
pub fn target(file: &SetupFile, app: &Path, wine: bool) -> Result<Option<PathBuf>, String> {
    let Some(destination) = file.destination.as_deref() else { return Ok(None) };
    let base = root_dir(file.root.as_deref(), app, wine)?;
    let path = relative(destination)?;
    // A folder in the system's folders is inside a folder of the app's there, never loose among other programs' own. A
    // single file may be (a ROM some apps read from there), and one already there is kept beside it.
    if file.root.as_deref().is_some_and(|r| r != "app") && file.folder() && path.components().count() < 2 {
        return Err(format!("The catalog's place for this copy ({destination}) isn't one Quiver can use."));
    }
    Ok(Some(base.join(path)))
}

/// Whether `b` is surely `a`, put in place before (in big-endian order, with `order`), without reading either
/// through: the same size, beginning and end.
fn same_copy(a: &Path, b: &Path, order: Option<Order>) -> bool {
    let (Ok(ma), Ok(mb)) = (fs::metadata(a), fs::metadata(b)) else { return false };
    if ma.len() != mb.len() || !ma.is_file() || !mb.is_file() {
        return false;
    }
    if a.canonicalize().ok().is_some_and(|ca| b.canonicalize().ok().is_some_and(|cb| ca == cb)) {
        return true;
    }
    // Both edges start at a multiple of four, so a byte order turns them as it turns the whole file.
    let edge = |p: &Path| -> Option<Vec<u8>> {
        let mut f = fs::File::open(p).ok()?;
        let mut head = vec![0u8; 1 << 16];
        let n = fill(&mut f, &mut head).ok()?;
        head.truncate(n);
        if ma.len() > (1 << 17) {
            use io::Seek;
            f.seek(io::SeekFrom::End(-(1 << 16))).ok()?;
            let mut tail = vec![0u8; 1 << 16];
            let k = fill(&mut f, &mut tail).ok()?;
            head.extend_from_slice(&tail[..k]);
        }
        Some(head)
    };
    let Some(mut x) = edge(a) else { return false };
    if let Some(o) = order {
        if ma.len() % 4 != 0 {
            return false;
        }
        let n = x.len() - x.len() % 4;
        o.to_big(&mut x[..n]);
    }
    edge(b).is_some_and(|y| x == y)
}

/// `path`, or `path (before Quiver)`, ` 2`… when that's taken: where a file already at the destination is kept.
fn aside(path: &Path) -> PathBuf {
    let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
    let mut n = 1;
    loop {
        let candidate = path.with_file_name(if n == 1 { format!("{name} (before Quiver)") } else { format!("{name} (before Quiver {n})") });
        if candidate.symlink_metadata().is_err() {
            return candidate;
        }
        n += 1;
    }
}

/// A folder linked into place: a junction on Windows (no administrator rights or Developer Mode needed), a symbolic
/// link elsewhere.
fn link_dir(from: &Path, to: &Path) -> Result<(), String> {
    #[cfg(windows)]
    {
        // Made by the system call, never through a command line: neither path is ever read as a command.
        junction::create(from, to).map_err(|e| format!("Couldn't link the game's folder into place: {e}"))
    }
    #[cfg(unix)]
    {
        std::os::unix::fs::symlink(from, to).map_err(|e| format!("Couldn't link the game's folder into place: {e}"))
    }
}

/// Copies `from` to `to`, an N64 ROM in big-endian order when `order` says it isn't, saying how far it's got.
fn write_copy(from: &Path, to: &Path, order: Option<Order>, progress: &dyn Fn(u64, u64)) -> Result<(), String> {
    let mut f = fs::File::open(from).map_err(|e| format!("Couldn't open the copy: {e}"))?;
    let total = f.metadata().map(|m| m.len()).unwrap_or(0);
    let part = to.with_file_name(format!("{}.quiver-part", to.file_name().unwrap_or_default().to_string_lossy()));
    let mut out = fs::File::create(&part).map_err(|e| format!("Couldn't write the copy into place: {e}"))?;
    let mut buf = vec![0u8; PIECE];
    let (mut done, mut said) = (0u64, 0u64);
    let result = (|| {
        loop {
            let n = fill(&mut f, &mut buf).map_err(|e| unreadable(done, e))?;
            if n == 0 {
                break;
            }
            if let Some(o) = order {
                o.to_big(&mut buf[..n]);
            }
            out.write_all(&buf[..n]).map_err(|e| format!("Couldn't write the copy into place: {e}"))?;
            done += n as u64;
            if done - said >= EVERY {
                said = done;
                progress(done, total);
            }
        }
        out.sync_all().map_err(|e| format!("Couldn't write the copy into place: {e}"))?;
        fs::rename(&part, to).map_err(|e| format!("Couldn't write the copy into place: {e}"))
    })();
    if result.is_err() {
        let _ = fs::remove_file(&part);
    }
    progress(done, total);
    result
}

/// Puts a checked copy where the app reads it, if the catalog says where: a file as a hard link (no space taken),
/// or a copy when it's on another drive; an N64 ROM going in as .z64 written big-endian; a folder linked. Anything
/// already there that isn't this copy is kept beside it, never deleted. Returns where it went.
pub fn place(file: &SetupFile, copy: &Path, app: &Path, wine: bool, progress: &dyn Fn(u64, u64)) -> Result<Option<PathBuf>, String> {
    let Some(to) = target(file, app, wine)? else { return Ok(None) };
    if let Some(parent) = to.parent() {
        fs::create_dir_all(parent).map_err(|e| format!("Couldn't make the folder it goes in: {e}"))?;
    }
    let wants_z64 = to.extension().is_some_and(|e| e.eq_ignore_ascii_case("z64"));
    let order = if wants_z64 && !file.folder() {
        let mut head = [0u8; 4];
        fs::File::open(copy).and_then(|mut f| f.read_exact(&mut head)).ok().and_then(|_| Order::of(&head)).filter(|o| *o != Order::Big)
    } else {
        None
    };
    if file.folder() {
        if to.canonicalize().ok().is_some_and(|t| copy.canonicalize().ok().is_some_and(|c| c == t)) {
            return Ok(Some(to));
        }
        // A folder in the place it's linked to, or around it, can't be linked there: moving what's there aside
        // would take the copy with it, or the link would lead to itself.
        let (mine, place) = (crate::apps_folder::resolved(copy), crate::apps_folder::resolved(&to));
        if mine.starts_with(&place) || place.starts_with(&mine) {
            return Err(format!("That folder is where the app keeps its own files ({}). Choose the folder your copy of the game is in.", to.display()));
        }
    } else if same_copy(copy, &to, order) {
        return Ok(Some(to));
    }
    if to.symlink_metadata().is_ok() {
        // A link Quiver made for an earlier copy goes; anything else is kept.
        if is_link(&to) {
            remove_link(&to)?;
        } else if file.root.as_deref().is_some_and(|r| r != "app") && to.is_dir() {
            // Outside the app's folder no folder is moved: it may be another program's. A file (a copy put there
            // before) is kept beside the new one, as in the app's folder.
            return Err(format!("There's already something at {}. Move it somewhere else, then try again.", to.display()));
        } else {
            fs::rename(&to, aside(&to)).map_err(|e| format!("Couldn't move aside what's already at {}: {e}", to.display()))?;
        }
    }
    if file.folder() {
        link_dir(copy, &to)?;
        return Ok(Some(to));
    }
    if order.is_none() && fs::hard_link(copy, &to).is_ok() {
        return Ok(Some(to));
    }
    write_copy(copy, &to, order, progress)?;
    Ok(Some(to))
}

#[cfg(windows)]
fn is_junction(path: &Path) -> bool {
    use std::os::windows::fs::MetadataExt;
    // FILE_ATTRIBUTE_DIRECTORY and FILE_ATTRIBUTE_REPARSE_POINT: Rust calls a junction a symbolic link, not a folder.
    fs::symlink_metadata(path).map(|m| m.file_attributes() & 0x410 == 0x410).unwrap_or(false)
}
#[cfg(not(windows))]
fn is_junction(_: &Path) -> bool {
    false
}
/// Takes away a link, never what it points at.
/// Whether something at `path` is a link (a symbolic link, or a junction on Windows), not a file or folder itself.
pub fn is_link(path: &Path) -> bool {
    fs::symlink_metadata(path).map(|m| m.file_type().is_symlink()).unwrap_or(false) || is_junction(path)
}

fn remove_link(path: &Path) -> Result<(), String> {
    let meta = fs::symlink_metadata(path).map_err(|e| e.to_string())?;
    // A junction (or a folder link) on Windows is removed as a folder; that removes the link only.
    let result = if meta.is_dir() || is_junction(path) { fs::remove_dir(path) } else { fs::remove_file(path) };
    result.map_err(|e| format!("Couldn't replace the link at {}: {e}", path.display()))
}

/// Whether the copy is still where the app reads it (or, passed by flag or variable, where the player keeps it).
pub fn present(file: &SetupFile, copy: &Path, app: &Path, wine: bool) -> bool {
    match target(file, app, wine) {
        Ok(Some(to)) => to.exists(),
        Ok(None) => copy.exists(),
        Err(_) => false,
    }
}

/// A copy found where players keep them, before it's checked.
#[derive(Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Found {
    pub path: String,
    pub size: u64,
    /// Found in a folder the player keeps this console's copies in (their settings).
    #[serde(skip_serializing_if = "std::ops::Not::not")]
    pub mine: bool,
}

/// How deep under a place copies are looked for: an SD card's Emulation/roms/n64 is three down.
const DEPTH: usize = 4;
/// How many files and folders are looked at in all, so a huge drive can't keep it looking.
const BUDGET: usize = 50_000;
const MOST: usize = 20;
/// In the player's own folders for a console: deeper, longer and more, as everything there is likely a copy. All of
/// a big ROM set, not its first few hundred files in the order the drive lists them: the launcher ranks them by name,
/// and a set's _All folder with a thousand ROMs has the one wanted anywhere in it.
const OWN_DEPTH: usize = 6;
const OWN_BUDGET: usize = 100_000;
const OWN_MOST: usize = 5_000;
/// Folders that hold no game copies, only a lot of other files.
const PASS_OVER: [&str; 14] = [
    "node_modules", "steamapps", "windows", "program files", "program files (x86)", "programdata", "$recycle.bin",
    "system volume information", "appdata", "compatdata", "pfx", "library", "proc", "quiver launcher",
];

/// How far one search goes: how deep, and how many it finds.
#[derive(Clone, Copy)]
struct Reach {
    deepest: usize,
    most: usize,
}

/// Copies that could be the one wanted, by name, without reading them: first in `own`, the folders the player keeps
/// this game's console's copies in, then in the usual `places`. `skip`: folders not to look in (Quiver's apps folder,
/// where copies already put in place are).
pub fn find(file: &SetupFile, own: &[PathBuf], places: &[PathBuf], skip: &[PathBuf]) -> Vec<Found> {
    let wanted = file.wanted();
    if !file.folder() && wanted.is_empty() {
        return vec![];
    }
    if file.folder() && file.contains.is_empty() {
        return vec![];
    }
    let mut found = Vec::new();
    let mut seen = HashSet::new();
    let mut budget = OWN_BUDGET;
    let reach = Reach { deepest: OWN_DEPTH, most: OWN_MOST };
    for place in own {
        look(file, &wanted, place, 0, reach, &mut budget, &mut seen, skip, &mut found);
    }
    for f in &mut found {
        f.mine = true;
    }
    let mut budget = BUDGET;
    let reach = Reach { deepest: DEPTH, most: found.len() + MOST };
    for place in places {
        look(file, &wanted, place, 0, reach, &mut budget, &mut seen, skip, &mut found);
        if found.len() >= reach.most || budget == 0 {
            break;
        }
    }
    found
}

#[allow(clippy::too_many_arguments)]
fn look(file: &SetupFile, wanted: &[String], dir: &Path, depth: usize, reach: Reach, budget: &mut usize, seen: &mut HashSet<PathBuf>, skip: &[PathBuf], found: &mut Vec<Found>) {
    if *budget == 0 || found.len() >= reach.most || skip.iter().any(|s| dir.starts_with(s)) {
        return;
    }
    let Ok(real) = dir.canonicalize() else { return };
    if !seen.insert(real) {
        return;
    }
    let Ok(entries) = fs::read_dir(dir) else { return };
    let entries: Vec<_> = entries.flatten().collect();
    if file.folder() {
        let names: HashSet<String> = entries.iter().map(|e| e.file_name().to_string_lossy().to_lowercase()).collect();
        let first = |p: &str| p.split('/').next().unwrap_or("").to_lowercase();
        if file.contains.iter().all(|c| names.contains(&first(c))) && file.contains.iter().all(|c| holds(dir, c)) {
            found.push(Found { path: dir.to_string_lossy().into_owned(), size: 0, mine: false });
            return;
        }
    }
    let mut folders = Vec::new();
    for entry in entries {
        if *budget == 0 || found.len() >= reach.most {
            return;
        }
        *budget -= 1;
        let name = entry.file_name().to_string_lossy().into_owned();
        if name.starts_with('.') {
            continue;
        }
        let Ok(kind) = entry.file_type() else { continue };
        let path = entry.path();
        if kind.is_dir() || (kind.is_symlink() && path.is_dir()) {
            if !PASS_OVER.contains(&name.to_lowercase().as_str()) {
                folders.push(path);
            }
        } else if !file.folder() {
            let ext = path.extension().map(|e| e.to_string_lossy().to_ascii_lowercase()).unwrap_or_default();
            if wanted.contains(&ext) {
                let size = entry.metadata().map(|m| m.len()).unwrap_or(0);
                if size > 0 {
                    found.push(Found { path: path.to_string_lossy().into_owned(), size, mine: false });
                }
            }
        }
    }
    if depth + 1 < reach.deepest {
        for folder in folders {
            look(file, wanted, &folder, depth + 1, reach, budget, seen, skip, found);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("quiver-own-copy-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }
    /// A tiny N64 ROM in big-endian order: its header's first word, then bytes 0..252.
    fn rom() -> Vec<u8> {
        let mut b = vec![0x80, 0x37, 0x12, 0x40];
        b.extend((0..252u32).map(|i| i as u8));
        b
    }
    fn sha1_of(b: &[u8]) -> String {
        hex::encode(Sha1::digest(b))
    }
    fn rom_file(sha1: &str) -> SetupFile {
        SetupFile {
            id: "rom".into(),
            label: "ROM".into(),
            destination: Some("baserom.us.z64".into()),
            extensions: vec!["z64".into()],
            accepted: vec![Dump { algo: "sha1".into(), value: sha1.into(), label: Some("US".into()) }],
            ..Default::default()
        }
    }
    fn none(_: u64, _: u64) {}

    #[test]
    fn byte_orders_become_big_endian() {
        let big = rom();
        let mut swapped = big.clone();
        swapped.chunks_exact_mut(2).for_each(|c| c.swap(0, 1));
        let mut little = big.clone();
        little.chunks_exact_mut(4).for_each(|c| c.reverse());
        assert_eq!(Order::of(&big), Some(Order::Big));
        assert_eq!(Order::of(&swapped), Some(Order::Swapped));
        assert_eq!(Order::of(&little), Some(Order::Little));
        Order::Swapped.to_big(&mut swapped);
        Order::Little.to_big(&mut little);
        assert_eq!(swapped, big);
        assert_eq!(little, big);
        assert_eq!(Order::of(b"PK\x03\x04"), None);
    }

    #[test]
    fn a_rom_in_any_byte_order_matches_its_no_intro_hash() {
        let dir = temp("orders");
        let big = rom();
        let file = rom_file(&sha1_of(&big));
        let mut little = big.clone();
        little.chunks_exact_mut(4).for_each(|c| c.reverse());
        fs::write(dir.join("game.n64"), &little).unwrap();
        let checked = check(&file, &dir.join("game.n64"), &none);
        assert!(checked.ok && checked.verified, "{checked:?}");
        assert_eq!(checked.dump.as_deref(), Some("US"));
    }

    #[test]
    fn says_why_a_copy_isnt_one_it_takes() {
        let dir = temp("why");
        let file = rom_file(&"0".repeat(40));
        fs::write(dir.join("other.z64"), rom()).unwrap();
        fs::write(dir.join("game.zip"), b"PK").unwrap();
        fs::write(dir.join("game.rvz"), b"x").unwrap();
        fs::write(dir.join("game.iso"), b"x").unwrap();
        let other = check(&file, &dir.join("other.z64"), &none);
        assert!(!other.ok && other.problem.as_deref().unwrap().contains("another region or version"));
        assert_eq!(other.sha1.as_deref(), Some(sha1_of(&rom()).as_str()));
        assert!(check(&file, &dir.join("game.zip"), &none).problem.unwrap().contains("Unpack it first"));
        assert!(check(&file, &dir.join("game.rvz"), &none).problem.unwrap().contains("Convert it to .iso"));
        assert_eq!(check(&file, &dir.join("game.iso"), &none).problem.as_deref(), Some("It needs a .z64, .n64 or .v64 file."));
        assert!(check(&file, &dir, &none).problem.unwrap().contains("not a folder"));
    }

    #[test]
    fn without_dumps_it_checks_the_kind_of_copy() {
        let dir = temp("kind");
        let disc = SetupFile { id: "disc".into(), label: "Disc".into(), flag: Some("--iso".into()), extensions: vec!["iso".into()], ..Default::default() };
        fs::write(dir.join("SLUS-20651.ISO"), b"disc").unwrap();
        let checked = check(&disc, &dir.join("SLUS-20651.ISO"), &none);
        assert!(checked.ok && !checked.verified);
        let folder = SetupFile { id: "game".into(), label: "Game".into(), kind: Some("folder".into()), env: Some("GAME".into()), contains: vec!["Default.xex".into(), "media/a.bin".into()], ..Default::default() };
        fs::create_dir_all(dir.join("game/Media")).unwrap();
        fs::write(dir.join("game/default.XEX"), b"x").unwrap();
        assert!(check(&folder, &dir.join("game"), &none).problem.unwrap().contains("doesn't have media/a.bin"));
        fs::write(dir.join("game/Media/a.bin"), b"x").unwrap();
        assert!(check(&folder, &dir.join("game"), &none).ok);
    }

    #[test]
    fn places_a_rom_big_endian_and_keeps_what_was_there() {
        let dir = temp("place");
        let app = dir.join("app");
        fs::create_dir_all(&app).unwrap();
        let big = rom();
        let mut swapped = big.clone();
        swapped.chunks_exact_mut(2).for_each(|c| c.swap(0, 1));
        fs::write(dir.join("mine.v64"), &swapped).unwrap();
        fs::write(app.join("baserom.us.z64"), b"someone else's file").unwrap();
        let file = rom_file(&sha1_of(&big));
        let to = place(&file, &dir.join("mine.v64"), &app, false, &none).unwrap().unwrap();
        assert_eq!(to, app.join("baserom.us.z64"));
        assert_eq!(fs::read(&to).unwrap(), big);
        assert_eq!(fs::read(app.join("baserom.us.z64 (before Quiver)")).unwrap(), b"someone else's file");
        // Again: it's that copy already, so nothing moves.
        place(&file, &dir.join("mine.v64"), &app, false, &none).unwrap();
        assert!(!app.join("baserom.us.z64 (before Quiver 2)").exists());
        assert!(present(&file, &dir.join("mine.v64"), &app, false));
    }

    #[test]
    fn links_a_file_and_a_folder_into_place() {
        let dir = temp("link");
        let app = dir.join("app");
        fs::create_dir_all(&app).unwrap();
        fs::write(dir.join("disc.iso"), b"disc").unwrap();
        let disc = SetupFile { id: "disc".into(), label: "Disc".into(), destination: Some("data/game.iso".into()), extensions: vec!["iso".into()], ..Default::default() };
        let to = place(&disc, &dir.join("disc.iso"), &app, false, &none).unwrap().unwrap();
        assert_eq!(fs::read(to).unwrap(), b"disc");
        fs::create_dir_all(dir.join("extracted")).unwrap();
        fs::write(dir.join("extracted/default.xex"), b"x").unwrap();
        let folder = SetupFile { id: "game".into(), label: "Game".into(), kind: Some("folder".into()), destination: Some("game".into()), contains: vec!["default.xex".into()], ..Default::default() };
        place(&folder, &dir.join("extracted"), &app, false, &none).unwrap();
        assert!(app.join("game/default.xex").is_file());
        // A second copy replaces the link, never the folder it pointed at.
        fs::create_dir_all(dir.join("other")).unwrap();
        place(&folder, &dir.join("other"), &app, false, &none).unwrap();
        assert!(dir.join("extracted/default.xex").is_file());
        assert!(!app.join("game/default.xex").exists());
    }

    #[test]
    fn refuses_places_outside_its_roots() {
        let app = Path::new("/apps/game");
        for bad in ["../x.z64", "/etc/x", "a\\b.z64", "C:/x.z64", "a//b", "a/b/c/d/e.z64", "x\"&calc&\"", "a%PATH%", "a|b", "rom.z64 ", "rom."] {
            let file = SetupFile { destination: Some(bad.into()), ..Default::default() };
            assert!(target(&file, app, false).is_err(), "{bad}");
        }
        let data = SetupFile { destination: Some("Game/rom.z64".into()), root: Some("localData".into()), ..Default::default() };
        assert!(target(&data, app, true).unwrap_err().contains("Wine or Proton"));
        assert!(target(&data, app, false).unwrap().unwrap().ends_with("Game/rom.z64"));
        // In the system's folders, only inside a folder of the app's.
        let loose = |kind: &str| SetupFile { destination: Some("autostart".into()), root: Some("config".into()), kind: Some(kind.into()), ..Default::default() };
        assert!(target(&loose("folder"), app, false).is_err());
        // A file may be: one app reads its ROM from there.
        assert!(target(&loose("file"), app, false).unwrap().unwrap().ends_with("autostart"));
    }

    #[test]
    fn never_links_a_folder_to_itself_or_moves_the_copy_aside() {
        let dir = temp("inside");
        let app = dir.join("app");
        fs::create_dir_all(app.join("assets/game")).unwrap();
        fs::write(app.join("assets/game/default.xex"), b"x").unwrap();
        let folder = SetupFile { id: "game".into(), label: "Game".into(), kind: Some("folder".into()), destination: Some("assets".into()), ..Default::default() };
        // The copy inside the place it goes, and the app's own folder around it.
        assert!(place(&folder, &app.join("assets/game"), &app, false, &none).is_err());
        assert!(place(&folder, &app, &app, false, &none).is_err());
        assert!(app.join("assets/game/default.xex").is_file() && !app.join("assets (before Quiver)").exists());
    }

    #[test]
    fn looks_in_the_players_own_folders_first_and_deeper() {
        let dir = temp("own");
        let own = dir.join("Collection");
        let deep = own.join("Nintendo/N64/USA/Shooters/Rare");
        fs::create_dir_all(&deep).unwrap();
        fs::create_dir_all(dir.join("Downloads")).unwrap();
        fs::write(deep.join("GoldenEye 007 (USA).z64"), rom()).unwrap();
        fs::write(dir.join("Downloads/Banjo.z64"), rom()).unwrap();
        // The usual places include the player's folder too: what's in it is listed once, as theirs.
        let found = find(&rom_file("x"), std::slice::from_ref(&own), &[dir.join("Downloads"), dir.clone()], &[]);
        assert_eq!(found.len(), 2, "{found:?}");
        assert!(found[0].path.ends_with("GoldenEye 007 (USA).z64") && found[0].mine);
        assert!(found[1].path.ends_with("Banjo.z64") && !found[1].mine);
        // Five folders down is past where the usual places are looked in.
        assert!(find(&rom_file("x"), &[], &[own], &[]).is_empty());
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn finds_copies_by_kind_where_players_keep_them() {
        let dir = temp("find");
        fs::create_dir_all(dir.join("Emulation/roms/n64")).unwrap();
        fs::create_dir_all(dir.join(".hidden")).unwrap();
        fs::create_dir_all(dir.join("apps/game")).unwrap();
        fs::write(dir.join("Emulation/roms/n64/Zelda (USA).Z64"), rom()).unwrap();
        fs::write(dir.join(".hidden/x.z64"), rom()).unwrap();
        fs::write(dir.join("apps/game/baserom.z64"), rom()).unwrap();
        fs::write(dir.join("notes.txt"), b"x").unwrap();
        let found = find(&rom_file("x"), &[], std::slice::from_ref(&dir), &[dir.join("apps")]);
        assert_eq!(found.len(), 1);
        assert!(found[0].path.ends_with("Zelda (USA).Z64"));
        fs::create_dir_all(dir.join("ps3/BLUS30443/PS3_GAME")).unwrap();
        fs::write(dir.join("ps3/BLUS30443/PS3_GAME/PARAM.SFO"), b"x").unwrap();
        let folder = SetupFile { kind: Some("folder".into()), contains: vec!["PS3_GAME/PARAM.SFO".into()], ..Default::default() };
        let found = find(&folder, &[], std::slice::from_ref(&dir), &[]);
        assert_eq!(found.len(), 1);
        assert!(found[0].path.ends_with("BLUS30443"));
    }
}
