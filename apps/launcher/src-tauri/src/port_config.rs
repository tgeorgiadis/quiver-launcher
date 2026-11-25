//! The settings a port's own setup file (port_setup.rs) lets the player change, shown on the app page's Settings
//! tab: rows of choices or a slider, each written to one of the port's settings files (INI, TOML or JSON) or passed
//! on the command line at Play. A settings file is edited in place, a line at a time: its comments, its order and
//! keys Quiver doesn't know stay as they are. Before the port starts, the file's own `new`, `ensure` and `always`
//! values are written too, as the port asks.
//!
//! What's written is only what the file says, in the places it names inside the app's folder or the player's own
//! folders; a place Quiver can't name (a Windows folder inside a Wine prefix, a player's copy) leaves that file out.
use crate::port_setup::{self, Manifest};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
use std::path::{Component, Path, PathBuf};

/// Where the player's command-line picks are kept, in the launcher's data folder: app folder → setting → pick.
const PICKS_FILE: &str = "port-settings.json";
/// Bigger than any settings file a port writes.
const LIMIT: u64 = 4 << 20;

/// What the player picked for a command-line setting: a choice by its place in the list, or a slider's number.
#[derive(Serialize, Deserialize, Clone, Copy, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub enum Pick {
    Choice(usize),
    Value(f64),
}

/// One app's command-line picks, by setting id.
pub type Picks = BTreeMap<String, Pick>;

// ---- What the setup file says ----

#[derive(Clone, Copy, Debug, PartialEq)]
enum Format {
    Ini,
    Toml,
    Json,
}

#[derive(Debug)]
struct Config {
    id: String,
    format: Format,
    /// Places it may be, each with the path that must exist for it to be used; the first that applies.
    places: Vec<(String, Option<String>)>,
    ensure: BTreeMap<String, String>,
    always: BTreeMap<String, String>,
    new: BTreeMap<String, String>,
    /// The key that holds the path to the player's copy of the game, written at every start.
    game_path: Option<String>,
}

#[derive(Debug)]
struct Choice {
    label: String,
    set: BTreeMap<String, String>,
    args: Vec<String>,
}

#[derive(Debug)]
struct Slider {
    key: Option<String>,
    min: f64,
    max: f64,
    step: f64,
    format: String,
    args: Vec<String>,
}

#[derive(Debug)]
enum Kind {
    Choices(Vec<Choice>),
    Slider(Slider),
}

#[derive(Debug)]
struct Setting {
    id: String,
    label: String,
    help: Option<String>,
    /// A config id, or "args" for the command line.
    input: String,
    group: Option<String>,
    advanced: bool,
    defaults: BTreeMap<String, String>,
    when: BTreeMap<String, String>,
    kind: Kind,
}

impl Setting {
    fn command_line(&self) -> bool {
        self.input == "args"
    }
}

/// A key → value list. Values are text, written as given; a number or true/false written bare is taken as its text.
fn values(v: Option<&Value>) -> BTreeMap<String, String> {
    let Some(Value::Object(map)) = v else { return BTreeMap::new() };
    map.iter()
        .filter_map(|(k, v)| {
            let text = match v {
                Value::String(s) => s.clone(),
                Value::Number(n) => n.to_string(),
                Value::Bool(b) => b.to_string(),
                _ => return None,
            };
            Some((k.clone(), text))
        })
        .collect()
}

fn texts(v: Option<&Value>) -> Vec<String> {
    v.and_then(Value::as_array).map(|a| a.iter().filter_map(|x| x.as_str().map(str::to_string)).collect()).unwrap_or_default()
}

fn text(v: &Value, key: &str) -> Option<String> {
    v.get(key).and_then(Value::as_str).map(str::trim).filter(|s| !s.is_empty()).map(str::to_string)
}

fn configs(m: &Manifest) -> Vec<Config> {
    let Some(list) = m.config.as_array() else { return Vec::new() };
    list.iter()
        .filter_map(|c| {
            let format = match c.get("format")?.as_str()? {
                "ini" => Format::Ini,
                "toml" => Format::Toml,
                "json" => Format::Json,
                _ => return None,
            };
            let places = match c.get("file")? {
                Value::String(p) => vec![(p.clone(), None)],
                Value::Array(list) => list.iter().filter_map(|p| Some((p.get("path")?.as_str()?.to_string(), p.get("if").and_then(Value::as_str).map(str::to_string)))).collect(),
                _ => return None,
            };
            if places.is_empty() {
                return None;
            }
            Some(Config {
                id: text(c, "id").unwrap_or_else(|| "main".into()),
                format,
                places,
                ensure: values(c.get("ensure")),
                always: values(c.get("always")),
                new: values(c.get("new")),
                game_path: text(c, "game_path"),
            })
        })
        .collect()
}

fn settings(m: &Manifest) -> Vec<Setting> {
    let Some(list) = m.settings.as_array() else { return Vec::new() };
    // Whole groups hidden behind Show advanced options.
    let advanced_groups: Vec<String> = m.groups.as_array().map(|g| g.iter().filter(|g| g.get("advanced").and_then(Value::as_bool) == Some(true)).filter_map(|g| text(g, "id")).collect()).unwrap_or_default();
    let mut seen = std::collections::HashSet::new();
    list.iter()
        .filter_map(|s| {
            let id = text(s, "id")?;
            let label = text(s, "label")?;
            let kind = if let Some(sl) = s.get("slider") {
                let min = sl.get("min")?.as_f64()?;
                let max = sl.get("max")?.as_f64()?;
                if !(min.is_finite() && max.is_finite() && max > min) {
                    return None;
                }
                let step = sl.get("step").and_then(Value::as_f64).filter(|s| s.is_finite() && *s > 0.0).unwrap_or((max - min) / 100.0);
                Kind::Slider(Slider {
                    key: text(sl, "key"),
                    min,
                    max,
                    step,
                    format: text(sl, "format").unwrap_or_else(|| "%g".into()),
                    args: { let own = texts(sl.get("args")); if own.is_empty() { texts(s.get("args")) } else { own } },
                })
            } else {
                // Key bindings ("bindings") aren't shown yet: the port's own menu has them.
                let choices: Vec<Choice> = s.get("choices")?.as_array()?.iter().filter_map(|c| Some(Choice { label: text(c, "label")?, set: values(c.get("set")), args: texts(c.get("args")) })).collect();
                if choices.len() < 2 {
                    return None;
                }
                Kind::Choices(choices)
            };
            let group = text(s, "group");
            let setting = Setting {
                advanced: s.get("advanced").and_then(Value::as_bool) == Some(true) || group.as_ref().is_some_and(|g| advanced_groups.contains(g)),
                help: text(s, "help"),
                input: text(s, "in").unwrap_or_else(|| "main".into()),
                group,
                defaults: values(s.get("defaults")),
                when: values(s.get("when")),
                id,
                label,
                kind,
            };
            // A slider on the command line needs {value} somewhere; one in a file needs its key.
            let usable = match &setting.kind {
                Kind::Slider(sl) if setting.command_line() => sl.args.iter().any(|a| a.contains("{value}")),
                Kind::Slider(sl) => sl.key.is_some(),
                Kind::Choices(_) => true,
            };
            (usable && seen.insert(setting.id.clone())).then_some(setting)
        })
        .collect()
}

// ---- Places ----

/// A place the file names, as a path on this computer: `{install}` and `{data}` in the app's folder, the player's
/// own folders where Quiver knows them. None for one it can't name, or a path that climbs out with "..".
pub fn host_path(text: &str, app_dir: &Path, wine: bool) -> Option<PathBuf> {
    let mut out = String::new();
    let mut rest = text;
    while let Some(open) = rest.find('{') {
        out.push_str(&rest[..open]);
        let close = rest[open..].find('}')? + open;
        let name = &rest[open + 1..close];
        let path = match name {
            "install" => app_dir.to_path_buf(),
            "data" => app_dir.join(port_setup::DATA),
            _ => port_setup::place(name, wine)?,
        };
        out.push_str(&path.to_string_lossy());
        rest = &rest[close + 1..];
    }
    out.push_str(rest);
    let path = PathBuf::from(out);
    (path.is_absolute() && !path.components().any(|c| c == Component::ParentDir)).then_some(path)
}

/// The settings file a config entry points at on this computer: the first of its places whose condition holds.
fn locate(c: &Config, app_dir: &Path, wine: bool) -> Option<PathBuf> {
    c.places.iter().find_map(|(path, cond)| {
        if let Some(cond) = cond {
            if !host_path(cond, app_dir, wine)?.exists() {
                return None;
            }
        }
        host_path(path, app_dir, wine)
    })
}

/// A value to write, with the places in it filled in for the game (a Windows path through Wine).
fn value_for_game(value: &str, app_dir: &Path, wine: bool) -> String {
    if value.contains("{install}") || value.contains("{data}") || value.contains("{home}") || value.contains("{local}") || value.contains("{config}") || value.contains("{documents}") {
        port_setup::fill(value, app_dir, wine).unwrap_or_else(|| value.to_string())
    } else {
        value.to_string()
    }
}

// ---- Settings files ----

/// A settings file, open for reading and editing. INI and TOML are kept as lines; JSON as its object.
struct Doc {
    path: PathBuf,
    format: Format,
    lines: Vec<String>,
    json: serde_json::Map<String, Value>,
    crlf: bool,
    existed: bool,
    changed: bool,
}

impl Doc {
    /// Errs on a file that's there but can't be read or understood: it's left alone.
    fn open(path: PathBuf, format: Format) -> Result<Doc, String> {
        let text = match std::fs::metadata(&path) {
            Ok(meta) if meta.len() > LIMIT => return Err("too big".into()),
            Ok(_) => Some(std::fs::read_to_string(&path).map_err(|e| e.to_string())?),
            Err(_) => None,
        };
        let existed = text.is_some();
        let text = text.unwrap_or_default();
        let text = text.trim_start_matches('\u{feff}');
        let json = if format == Format::Json && !text.trim().is_empty() {
            match serde_json::from_str::<Value>(text).map_err(|e| e.to_string())? {
                Value::Object(map) => map,
                _ => return Err("not a JSON object".into()),
            }
        } else {
            Default::default()
        };
        Ok(Doc { crlf: text.contains("\r\n"), lines: text.lines().map(str::to_string).collect(), json, path, format, existed, changed: false })
    }

    /// `Section/Key`: Key under [Section] (an INI section or TOML table). A plain key is looked for anywhere.
    fn split<'a>(&self, key: &'a str) -> (Option<&'a str>, &'a str) {
        match key.split_once('/') {
            Some((section, k)) if !section.trim().is_empty() && !k.trim().is_empty() => (Some(section.trim()), k.trim()),
            _ => (None, key.trim()),
        }
    }

    fn header(line: &str) -> Option<&str> {
        let t = line.trim();
        if t.starts_with("[[") || !t.starts_with('[') {
            return None;
        }
        t.find(']').map(|end| t[1..end].trim())
    }

    fn is_table_line(line: &str) -> bool {
        line.trim_start().starts_with('[')
    }

    /// The lines a key under `section` may be on (after its header), or every line for none. None when the section
    /// isn't there.
    fn range(&self, section: Option<&str>) -> Option<(usize, usize)> {
        let Some(section) = section else { return Some((0, self.lines.len())) };
        let start = self.lines.iter().position(|l| Self::header(l).is_some_and(|h| if self.format == Format::Ini { h.eq_ignore_ascii_case(section) } else { h == section }))? + 1;
        let end = self.lines[start..].iter().position(|l| Self::is_table_line(l)).map_or(self.lines.len(), |i| start + i);
        Some((start, end))
    }

    /// Where on `line` the value of `key` is, when the line sets it.
    fn value_at(&self, line: &str, key: &str) -> Option<(usize, usize)> {
        let lead = line.len() - line.trim_start().len();
        let rest = &line[lead..];
        let named = rest.get(..key.len())?;
        let matches = if self.format == Format::Ini { named.eq_ignore_ascii_case(key) } else { named == key };
        if !matches {
            return None;
        }
        let after = &rest[key.len()..];
        let eq = after.len() - after.trim_start().len();
        if !after[eq..].starts_with('=') {
            return None;
        }
        let start = lead + key.len() + eq + 1;
        let start = start + (line[start..].len() - line[start..].trim_start().len());
        let tail = &line[start..];
        // Up to a comment: in INI one after a space (a value may hold # or ;), in TOML a # outside quotes.
        let mut end = tail.len();
        let mut quote: Option<char> = None;
        let mut prev = ' ';
        for (i, ch) in tail.char_indices() {
            match self.format {
                Format::Ini if (ch == ';' || ch == '#') && prev.is_whitespace() && i > 0 => {
                    end = i;
                    break;
                }
                Format::Toml => match (quote, ch) {
                    (None, '"' | '\'') => quote = Some(ch),
                    (Some(q), c) if c == q && prev != '\\' => quote = None,
                    (None, '#') => {
                        end = i;
                        break;
                    }
                    _ => {}
                },
                _ => {}
            }
            prev = ch;
        }
        let value = tail[..end].trim_end();
        Some((start, start + value.len()))
    }

    /// A key's value as the file writes it (a string with its quotes).
    fn get(&self, key: &str) -> Option<String> {
        if self.format == Format::Json {
            let mut parts = key.split('/');
            let mut at = self.json.get(parts.next()?)?;
            for p in parts {
                at = at.get(p)?;
            }
            return Some(match at {
                Value::String(s) => format!("\"{s}\""),
                other => other.to_string(),
            });
        }
        let (section, k) = self.split(key);
        let (a, b) = self.range(section)?;
        self.lines[a..b].iter().find_map(|l| self.value_at(l, k).map(|(s, e)| l[s..e].to_string()))
    }

    /// How the file writes `=`, for a line it doesn't have yet.
    fn eq(&self) -> &'static str {
        let tight = self.lines.iter().find_map(|l| {
            let t = l.trim();
            if t.is_empty() || t.starts_with(['#', ';', '[']) {
                return None;
            }
            let i = t.find('=')?;
            Some(!t[..i].ends_with(' ') && !t[i + 1..].starts_with(' '))
        });
        if tight == Some(true) {
            "="
        } else {
            " = "
        }
    }

    fn set(&mut self, key: &str, value: &str) {
        if self.get(key).is_some_and(|now| now == value) {
            return;
        }
        self.changed = true;
        if self.format == Format::Json {
            let new = serde_json::from_str::<Value>(value).unwrap_or_else(|_| Value::String(value.to_string()));
            let parts: Vec<&str> = key.split('/').collect();
            let (last, path) = parts.split_last().expect("split gives one part at least");
            let mut at = &mut self.json;
            for p in path {
                let next = at.entry(p.to_string()).or_insert_with(|| Value::Object(Default::default()));
                if !next.is_object() {
                    *next = Value::Object(Default::default());
                }
                at = next.as_object_mut().expect("made an object above");
            }
            at.insert(last.to_string(), new);
            return;
        }
        let (section, k) = self.split(key);
        let eq = self.eq();
        let Some((a, b)) = self.range(section) else {
            // A section the file doesn't have: at the end, after a blank line.
            if self.lines.last().is_some_and(|l| !l.trim().is_empty()) {
                self.lines.push(String::new());
            }
            self.lines.push(format!("[{}]", section.unwrap_or_default()));
            self.lines.push(format!("{k}{eq}{value}"));
            return;
        };
        if let Some(i) = (a..b).find(|i| self.value_at(&self.lines[*i], k).is_some()) {
            let (s, e) = self.value_at(&self.lines[i], k).expect("found above");
            self.lines[i].replace_range(s..e, value);
            return;
        }
        // Written out but commented out (`;vsync=1`, `# vsync = true`): that line, switched on.
        if let Some(i) = (a..b).find(|i| {
            let t = self.lines[*i].trim_start();
            let uncommented = t.trim_start_matches(['#', ';']).trim_start();
            uncommented.len() < t.len() && self.value_at(uncommented, k).is_some()
        }) {
            let lead: String = self.lines[i].chars().take_while(|c| c.is_whitespace()).collect();
            let uncommented = self.lines[i].trim_start().trim_start_matches(['#', ';']).trim_start().to_string();
            let (s, e) = self.value_at(&uncommented, k).expect("found above");
            let mut line = uncommented;
            line.replace_range(s..e, value);
            self.lines[i] = lead + &line;
            return;
        }
        // A new line: at the end of its section; a plain TOML key before the first table, or it would be the table's.
        let end = if section.is_none() && self.format == Format::Toml { self.lines.iter().position(|l| Self::is_table_line(l)).unwrap_or(self.lines.len()) } else { b };
        let at = (a..end).rev().find(|i| !self.lines[*i].trim().is_empty()).map_or(a, |i| i + 1);
        self.lines.insert(at, format!("{k}{eq}{value}"));
    }

    fn save(&mut self) -> Result<(), String> {
        if !self.changed {
            return Ok(());
        }
        if let Some(dir) = self.path.parent() {
            std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
        }
        let text = if self.format == Format::Json {
            serde_json::to_string_pretty(&self.json).map_err(|e| e.to_string())? + "\n"
        } else {
            let nl = if self.crlf { "\r\n" } else { "\n" };
            self.lines.iter().map(|l| format!("{l}{nl}")).collect()
        };
        // Whole or not at all: a game reading it half-written would lose its settings.
        let name = self.path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
        let tmp = self.path.with_file_name(format!(".{name}.quiver-tmp"));
        std::fs::write(&tmp, text).map_err(|e| e.to_string())?;
        std::fs::rename(&tmp, &self.path).map_err(|e| {
            let _ = std::fs::remove_file(&tmp);
            e.to_string()
        })?;
        self.existed = true;
        self.changed = false;
        Ok(())
    }
}

/// The same setting value: quotes aside, numbers by value, words in any case.
fn same(a: &str, b: &str) -> bool {
    let bare = |x: &str| x.trim().trim_matches(['"', '\'']).trim().to_string();
    let (a, b) = (bare(a), bare(b));
    match (a.parse::<f64>(), b.parse::<f64>()) {
        (Ok(x), Ok(y)) => (x - y).abs() < 1e-9 * x.abs().max(y.abs()).max(1.0),
        _ => a.eq_ignore_ascii_case(&b),
    }
}

/// A number written as a printf-style format says: `%d`, `%.2f`, `%g`, `%e`, with any text around it (`%.1fx`).
pub fn printf(format: &str, v: f64) -> String {
    let mut out = String::new();
    let mut chars = format.chars().peekable();
    let mut used = false;
    while let Some(c) = chars.next() {
        if c != '%' {
            out.push(c);
            continue;
        }
        if chars.peek() == Some(&'%') {
            chars.next();
            out.push('%');
            continue;
        }
        // Flags and width aren't needed for a settings file; skipped.
        while chars.peek().is_some_and(|c| matches!(c, '-' | '+' | ' ' | '#' | '0'..='9')) {
            chars.next();
        }
        let mut precision = None;
        if chars.peek() == Some(&'.') {
            chars.next();
            let mut digits = String::new();
            while let Some(d) = chars.peek().filter(|d| d.is_ascii_digit()) {
                digits.push(*d);
                chars.next();
            }
            precision = Some(digits.parse::<usize>().unwrap_or(0));
        }
        let kind = chars.next().unwrap_or('g');
        if used {
            continue;
        }
        used = true;
        out.push_str(&match kind {
            'd' | 'i' | 'u' => format!("{}", v.round() as i64),
            'f' | 'F' => format!("{:.*}", precision.unwrap_or(6), v),
            'e' | 'E' => c_exp(v, precision.unwrap_or(6), kind == 'E'),
            _ => c_general(v, precision.unwrap_or(6)),
        });
    }
    if !used {
        out.push_str(&c_general(v, 6));
    }
    out
}

/// C's `%e`: one digit, the fraction, and a signed exponent of two digits at least.
fn c_exp(v: f64, precision: usize, upper: bool) -> String {
    let text = format!("{:.*e}", precision, v);
    let (mantissa, exp) = text.split_once('e').unwrap_or((&text, "0"));
    let exp: i32 = exp.parse().unwrap_or(0);
    let e = if upper { 'E' } else { 'e' };
    format!("{mantissa}{e}{}{:02}", if exp < 0 { '-' } else { '+' }, exp.abs())
}

/// C's `%g`: `significant` digits, fixed or with an exponent as C picks, without trailing zeros.
fn c_general(v: f64, significant: usize) -> String {
    let p = significant.max(1);
    if v == 0.0 || !v.is_finite() {
        return if v == 0.0 { "0".into() } else { v.to_string() };
    }
    // The exponent after rounding to p digits.
    let exp = {
        let e = format!("{:.*e}", p - 1, v);
        e.split_once('e').and_then(|(_, x)| x.parse::<i32>().ok()).unwrap_or(0)
    };
    let trim = |s: String| if s.contains('.') { s.trim_end_matches('0').trim_end_matches('.').to_string() } else { s };
    if exp < -4 || exp >= p as i32 {
        let text = c_exp(v, p - 1, false);
        let (m, e) = text.split_once('e').unwrap_or((&text, ""));
        format!("{}e{e}", trim(m.to_string()))
    } else {
        trim(format!("{:.*}", (p as i32 - 1 - exp).max(0) as usize, v))
    }
}

// ---- The Settings tab ----

#[derive(Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct SliderShown {
    pub min: f64,
    pub max: f64,
    pub step: f64,
    /// Where it is now; None when it isn't a number (or, on the command line, the player hasn't moved it).
    pub value: Option<f64>,
    /// The value as written, for one off the slider's steps or not a number.
    pub text: Option<String>,
}

#[derive(Serialize, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Shown {
    pub id: String,
    pub label: String,
    pub help: Option<String>,
    pub group: Option<String>,
    pub advanced: bool,
    /// False while the setting's `when` doesn't hold: shown greyed out.
    pub enabled: bool,
    /// What it's used with, by another setting's label and choice ("Window", "Windowed"), when the file says.
    pub needs: Option<(String, String)>,
    /// Passed on the command line at Play rather than written to a file.
    pub command_line: bool,
    pub choices: Option<Vec<String>>,
    /// Which choice is in effect; None when what the file has is none of them.
    pub chosen: Option<usize>,
    /// What the file has, when it's none of the choices (set in the game's own menu, say).
    pub other: Option<String>,
    pub slider: Option<SliderShown>,
}

#[derive(Serialize, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct View {
    pub settings: Vec<Shown>,
    /// Settings files that are there but couldn't be read: their settings aren't shown, and nothing is written to them.
    pub unreadable: Vec<String>,
}

/// The settings files on this computer, opened, by config id. One that can't be placed is left out; one that can't
/// be read is named in `unreadable`.
fn open_all(m: &Manifest, app_dir: &Path, wine: bool, unreadable: &mut Vec<String>) -> BTreeMap<String, (Config, Doc)> {
    let mut docs = BTreeMap::new();
    for c in configs(m) {
        if docs.contains_key(&c.id) {
            continue;
        }
        let Some(path) = locate(&c, app_dir, wine) else { continue };
        match Doc::open(path.clone(), c.format) {
            Ok(doc) => {
                docs.insert(c.id.clone(), (c, doc));
            }
            Err(_) => unreadable.push(path.to_string_lossy().into_owned()),
        }
    }
    docs
}

/// Whether a setting writes `key` in its file.
fn writes(s: &Setting, key: &str) -> bool {
    match &s.kind {
        Kind::Choices(choices) => !s.command_line() && choices.iter().any(|c| c.set.contains_key(key)),
        Kind::Slider(sl) => !s.command_line() && sl.key.as_deref() == Some(key),
    }
}

/// A key's value as the game will read it: what its file has; for a file not there yet, what Play writes into it
/// first (`new`); else the game's own default, as the setting that writes the key says it (another setting's
/// `defaults` can't speak for it), or failing that `s`'s own.
fn current(file: Option<&(Config, Doc)>, s: &Setting, all: &[Setting], key: &str) -> Option<String> {
    let (config, doc) = match file {
        Some((c, d)) => (Some(c), Some(d)),
        None => (None, None),
    };
    if let Some(v) = doc.and_then(|d| d.get(key)) {
        return Some(v);
    }
    if let Some(v) = doc.filter(|d| !d.existed).and_then(|_| config?.new.get(key).cloned()) {
        return Some(v);
    }
    let input = if s.command_line() { "main" } else { s.input.as_str() };
    let owner = if writes(s, key) { Some(s) } else { all.iter().find(|o| o.input == input && writes(o, key)) };
    owner.and_then(|o| o.defaults.get(key)).or_else(|| s.defaults.get(key)).cloned()
}

/// The other setting and its choice that make `s` apply, for the player: the first of its `when` keys another
/// setting's choice writes.
fn needs(s: &Setting, all: &[Setting]) -> Option<(String, String)> {
    s.when.iter().find_map(|(k, v)| {
        all.iter().filter(|o| o.id != s.id).find_map(|o| match &o.kind {
            Kind::Choices(choices) => choices.iter().find(|c| c.set.get(k).is_some_and(|w| same(w, v))).map(|c| (o.label.clone(), c.label.clone())),
            Kind::Slider(_) => None,
        })
    })
}

fn shown(s: &Setting, all: &[Setting], docs: &BTreeMap<String, (Config, Doc)>, picks: &Picks) -> Option<Shown> {
    let doc = if s.command_line() {
        None
    } else {
        // A setting in a file Quiver can't reach isn't shown.
        Some(docs.get(&s.input)?)
    };
    let when_doc = doc.or_else(|| docs.get("main"));
    let enabled = s.when.iter().all(|(k, v)| current(when_doc, s, all, k).is_some_and(|now| same(&now, v)));
    let mut out = Shown {
        id: s.id.clone(),
        label: s.label.clone(),
        help: s.help.clone(),
        group: s.group.clone(),
        advanced: s.advanced,
        enabled,
        needs: needs(s, all),
        command_line: s.command_line(),
        choices: None,
        chosen: None,
        other: None,
        slider: None,
    };
    match &s.kind {
        Kind::Choices(choices) => {
            out.choices = Some(choices.iter().map(|c| c.label.clone()).collect());
            if s.command_line() {
                out.chosen = Some(match picks.get(&s.id) {
                    Some(Pick::Choice(i)) if *i < choices.len() => *i,
                    _ => 0,
                });
            } else {
                // The first choice whose keys all match; one that writes nothing stands for "anything else".
                out.chosen = choices
                    .iter()
                    .position(|c| !c.set.is_empty() && c.set.iter().all(|(k, v)| current(doc, s, all, k).is_some_and(|now| same(&now, v))))
                    .or_else(|| choices.iter().position(|c| c.set.is_empty()));
                if out.chosen.is_none() {
                    // What the file has instead, for the keys the choices write.
                    let keys: Vec<&String> = choices.iter().flat_map(|c| c.set.keys()).collect::<std::collections::BTreeSet<_>>().into_iter().collect();
                    let said: Vec<String> = keys.iter().filter_map(|k| current(doc, s, all, k)).map(|v| v.trim_matches('"').to_string()).collect();
                    out.other = (!said.is_empty()).then(|| said.join(", "));
                }
            }
        }
        Kind::Slider(sl) => {
            let (value, text) = if s.command_line() {
                match picks.get(&s.id) {
                    Some(Pick::Value(v)) => (Some(*v), None),
                    _ => (None, None),
                }
            } else {
                let written = sl.key.as_deref().and_then(|k| current(doc, s, all, k));
                let number = written.as_deref().and_then(|w| w.trim().trim_matches(['"', '\'']).parse::<f64>().ok()).filter(|v| v.is_finite());
                (number, written.map(|w| w.trim_matches('"').to_string()))
            };
            out.slider = Some(SliderShown { min: sl.min, max: sl.max, step: sl.step, value, text });
        }
    }
    Some(out)
}

/// What the Settings tab shows for the port: None when its file has no settings Quiver can show here.
pub fn view(m: &Manifest, app_dir: &Path, wine: bool, picks: &Picks) -> Option<View> {
    let mut unreadable = Vec::new();
    let docs = open_all(m, app_dir, wine, &mut unreadable);
    let all = settings(m);
    let settings: Vec<Shown> = all.iter().filter_map(|s| shown(s, &all, &docs, picks)).collect();
    (!settings.is_empty() || !unreadable.is_empty()).then_some(View { settings, unreadable })
}

/// The player changed a setting: written to its file at once, or kept in `picks` for the command line.
pub fn set(m: &Manifest, app_dir: &Path, wine: bool, picks: &mut Picks, id: &str, pick: Pick) -> Result<(), String> {
    let all = settings(m);
    let s = all.iter().find(|s| s.id == id).ok_or("This setting isn't in the port's setup file anymore.")?;
    let pick = match (&s.kind, pick) {
        (Kind::Choices(choices), Pick::Choice(i)) if i < choices.len() => Pick::Choice(i),
        (Kind::Slider(sl), Pick::Value(v)) if v.is_finite() => Pick::Value(v.clamp(sl.min, sl.max)),
        _ => return Err("That isn't one of this setting's values.".into()),
    };
    if s.command_line() {
        picks.insert(s.id.clone(), pick);
        return Ok(());
    }
    let mut unreadable = Vec::new();
    let mut docs = open_all(m, app_dir, wine, &mut unreadable);
    let (_, doc) = docs.get_mut(&s.input).ok_or("Quiver can't find this setting's file.")?;
    match (&s.kind, pick) {
        (Kind::Choices(choices), Pick::Choice(i)) => {
            for (k, v) in &choices[i].set {
                doc.set(k, &value_for_game(v, app_dir, wine));
            }
        }
        (Kind::Slider(sl), Pick::Value(v)) => doc.set(sl.key.as_deref().ok_or("This slider has no key.")?, &printf(&sl.format, v)),
        _ => unreachable!("checked above"),
    }
    doc.save().map_err(|e| format!("Couldn't save the setting: {e}"))
}

/// The command-line settings' arguments at Play: each setting's pick, or its first choice when the player hasn't
/// picked one; a slider only once it's been moved.
pub fn picked_args(m: &Manifest, app_dir: &Path, windows: bool, picks: &Picks) -> Vec<String> {
    let mut out = Vec::new();
    for s in settings(m).iter().filter(|s| s.command_line()) {
        let words: Vec<String> = match (&s.kind, picks.get(&s.id)) {
            (Kind::Choices(choices), pick) => {
                let i = match pick {
                    Some(Pick::Choice(i)) if *i < choices.len() => *i,
                    _ => 0,
                };
                choices[i].args.clone()
            }
            (Kind::Slider(sl), Some(Pick::Value(v))) => {
                let v = printf(&sl.format, v.clamp(sl.min, sl.max));
                sl.args.iter().map(|a| a.replace("{value}", &v)).collect()
            }
            _ => Vec::new(),
        };
        out.extend(words.iter().filter(|a| !port_setup::carries_copy(a)).filter_map(|a| port_setup::fill(a, app_dir, windows)));
    }
    out
}

/// Before the port starts: its settings files get the values it asks for, `new` in a file not there yet, `ensure`
/// where a key is missing or empty, `always` every time, and `game_path` the player's copy of the game (`game`, where
/// it is on this computer) when they gave one. A file that can't be read is left alone.
pub fn at_start(m: &Manifest, app_dir: &Path, wine: bool, game: Option<&Path>) {
    let mut unreadable = Vec::new();
    for (_, (c, mut doc)) in open_all(m, app_dir, wine, &mut unreadable) {
        if !doc.existed {
            for (k, v) in &c.new {
                doc.set(k, &value_for_game(v, app_dir, wine));
            }
        }
        for (k, v) in &c.ensure {
            if doc.get(k).is_none_or(|now| now.trim().trim_matches(['"', '\'']).is_empty()) {
                doc.set(k, &value_for_game(v, app_dir, wine));
            }
        }
        for (k, v) in &c.always {
            doc.set(k, &value_for_game(v, app_dir, wine));
        }
        if let (Some(k), Some(game)) = (&c.game_path, game) {
            let path = port_setup::for_game(game, wine);
            // A string in TOML and JSON; as it is in INI.
            let value = if c.format == Format::Ini { path } else { serde_json::to_string(&path).unwrap_or_default() };
            doc.set(k, &value);
        }
        let _ = doc.save();
    }
}

// ---- The player's command-line picks ----

fn picks_path(data_dir: &Path) -> PathBuf {
    data_dir.join(PICKS_FILE)
}

fn all_picks(data_dir: &Path) -> BTreeMap<String, Picks> {
    std::fs::read_to_string(picks_path(data_dir)).ok().and_then(|t| serde_json::from_str(&t).ok()).unwrap_or_default()
}

fn key(app_dir: &Path) -> String {
    app_dir.to_string_lossy().into_owned()
}

/// The command-line picks for the app in `app_dir`.
pub fn picks(data_dir: &Path, app_dir: &Path) -> Picks {
    all_picks(data_dir).remove(&key(app_dir)).unwrap_or_default()
}

pub fn save_picks(data_dir: &Path, app_dir: &Path, picks: Picks) -> Result<(), String> {
    let mut all = all_picks(data_dir);
    if picks.is_empty() {
        all.remove(&key(app_dir));
    } else {
        all.insert(key(app_dir), picks);
    }
    let text = serde_json::to_string_pretty(&all).map_err(|e| e.to_string())?;
    let path = picks_path(data_dir);
    let tmp = path.with_extension("json.tmp");
    std::fs::write(&tmp, text).map_err(|e| e.to_string())?;
    std::fs::rename(&tmp, &path).map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn manifest(json: &str) -> Manifest {
        serde_json::from_str(json).unwrap()
    }

    fn doc(format: Format, text: &str) -> Doc {
        let dir = tempfile::tempdir().unwrap();
        let path = dir.keep().join("f");
        std::fs::write(&path, text).unwrap();
        Doc::open(path, format).unwrap()
    }

    fn saved(d: &mut Doc) -> String {
        d.save().unwrap();
        std::fs::read_to_string(&d.path).unwrap()
    }

    #[test]
    fn ini_keys_are_read_and_written_in_place_with_comments_kept() {
        let mut d = doc(Format::Ini, "; The game's settings\n[Graphics]\nFullscreen = 1 ; on\nwidth=1280\n\n[Audio]\nvolume=80\n;muted=1\n");
        assert_eq!(d.get("Graphics/Fullscreen").as_deref(), Some("1"));
        assert_eq!(d.get("graphics/fullscreen").as_deref(), Some("1"), "INI names in any case");
        assert_eq!(d.get("volume").as_deref(), Some("80"), "a plain key anywhere");
        assert_eq!(d.get("Audio/width"), None);
        d.set("Graphics/Fullscreen", "0");
        d.set("Graphics/height", "720");
        d.set("Audio/muted", "0");
        d.set("Video/vsync", "1");
        assert_eq!(saved(&mut d), "; The game's settings\n[Graphics]\nFullscreen = 0 ; on\nwidth=1280\nheight = 720\n\n[Audio]\nvolume=80\nmuted=0\n\n[Video]\nvsync = 1\n");
    }

    #[test]
    fn toml_keeps_quotes_comments_and_puts_a_plain_key_before_the_first_table() {
        let mut d = doc(Format::Toml, "renderer = \"vulkan\" # or d3d12\n\n[window]\nfullscreen = false\ntitle = \"A # in a string\"\n");
        assert_eq!(d.get("renderer").as_deref(), Some("\"vulkan\""));
        assert_eq!(d.get("window/title").as_deref(), Some("\"A # in a string\""));
        d.set("renderer", "\"d3d12\"");
        d.set("scale", "2");
        d.set("window/fullscreen", "true");
        assert_eq!(saved(&mut d), "renderer = \"d3d12\" # or d3d12\nscale = 2\n\n[window]\nfullscreen = true\ntitle = \"A # in a string\"\n");
    }

    #[test]
    fn json_keys_go_through_nested_objects_and_windows_line_ends_stay() {
        let mut d = doc(Format::Json, "{\"CVars\": {\"res\": 2}, \"name\": \"x\"}");
        assert_eq!(d.get("CVars/res").as_deref(), Some("2"));
        assert_eq!(d.get("name").as_deref(), Some("\"x\""));
        d.set("CVars/res", "3");
        d.set("Window/mode", "\"borderless\"");
        let v: Value = serde_json::from_str(&saved(&mut d)).unwrap();
        assert_eq!(v["CVars"]["res"], 3);
        assert_eq!(v["Window"]["mode"], "borderless");
        let mut d = doc(Format::Ini, "a=1\r\nb=2\r\n");
        d.set("b", "3");
        assert_eq!(saved(&mut d), "a=1\r\nb=3\r\n");
        // Nothing changed, nothing written.
        let mut d = doc(Format::Ini, "a=1");
        d.set("a", "1");
        assert_eq!(saved(&mut d), "a=1");
    }

    #[test]
    fn numbers_are_written_as_printf_formats_say() {
        assert_eq!(printf("%.2f", 1.5), "1.50");
        assert_eq!(printf("%d", 2.6), "3");
        assert_eq!(printf("%g", 1.0), "1");
        assert_eq!(printf("%g", 0.25), "0.25");
        assert_eq!(printf("%g", 1234567.0), "1.23457e+06");
        assert_eq!(printf("%.1fx", 1.25), "1.2x");
        assert_eq!(printf("%e", 1500.0), "1.500000e+03");
        assert!(same("\"1.0\"", "1"));
        assert!(same("True", "true"));
        assert!(!same("1", "2"));
    }

    const PORT: &str = r#"{
        "format": 1,
        "start": { "program": ["game.exe"] },
        "config": [
            { "format": "ini", "file": [{ "path": "{install}/portable.ini", "if": "{install}/portable.txt" }, { "path": "{install}/game.ini" }],
              "new": { "Video/vsync": "1" }, "ensure": { "Video/scale": "1" }, "always": { "Launcher/skip": "true" } }
        ],
        "groups": [{ "id": "Debug", "advanced": true }],
        "settings": [
            { "id": "fullscreen", "label": "Fullscreen", "group": "Video",
              "choices": [{ "label": "Off", "set": { "Video/fullscreen": "0" } }, { "label": "On", "set": { "Video/fullscreen": "1" } }],
              "defaults": { "Video/fullscreen": "1" } },
            { "id": "size", "label": "Window size", "when": { "Video/fullscreen": "0" }, "defaults": { "Video/fullscreen": "0" },
              "choices": [{ "label": "720p", "set": { "Video/w": "1280", "Video/h": "720" } }, { "label": "1080p", "set": { "Video/w": "1920", "Video/h": "1080" } }] },
            { "id": "sens", "label": "Sensitivity", "slider": { "key": "Input/sens", "min": 0.1, "max": 5, "step": 0.05, "format": "%.2f" }, "defaults": { "Input/sens": "1.0" } },
            { "id": "log", "label": "Log", "in": "args", "group": "Debug",
              "choices": [{ "label": "Game's option", "args": [] }, { "label": "Verbose", "args": ["--verbose", "--log={data}/log.txt"] }] },
            { "id": "fov", "label": "Field of view", "in": "args", "slider": { "min": 60, "max": 120, "step": 5, "format": "%d", "args": ["--fov", "{value}"] } },
            { "id": "keys", "label": "Keys", "bindings": { "actions": [{ "label": "Jump", "key": "jump" }] } },
            { "id": "broken", "label": "Broken", "choices": "nope" },
            { "id": "elsewhere", "label": "Elsewhere", "in": "other", "choices": [{ "label": "A", "set": { "a": "1" } }, { "label": "B", "set": { "a": "2" } }] }
        ]
    }"#;

    #[test]
    fn the_tab_shows_what_the_file_and_defaults_say_and_leaves_out_what_it_cant_use() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        let m = manifest(PORT);
        let v = view(&m, app, false, &Picks::new()).unwrap();
        let ids: Vec<&str> = v.settings.iter().map(|s| s.id.as_str()).collect();
        assert_eq!(ids, ["fullscreen", "size", "sens", "log", "fov"], "bindings, broken and unplaced settings are left out");
        let by = |id: &str| v.settings.iter().find(|s| s.id == id).unwrap();
        assert_eq!(by("fullscreen").chosen, Some(1), "from its defaults while the file isn't there");
        assert!(!by("size").enabled, "only while fullscreen is off, as Fullscreen's own default says, not Window size's");
        assert_eq!(by("size").needs, Some(("Fullscreen".into(), "Off".into())));
        assert_eq!(by("size").chosen, None);
        assert_eq!(by("sens").slider.as_ref().unwrap().value, Some(1.0));
        assert!(by("log").advanced && by("log").command_line);
        assert_eq!(by("fov").slider.as_ref().unwrap().value, None);

        let mut picks = Picks::new();
        set(&m, app, false, &mut picks, "fullscreen", Pick::Choice(0)).unwrap();
        set(&m, app, false, &mut picks, "size", Pick::Choice(1)).unwrap();
        set(&m, app, false, &mut picks, "sens", Pick::Value(9.0)).unwrap();
        set(&m, app, false, &mut picks, "log", Pick::Choice(1)).unwrap();
        set(&m, app, false, &mut picks, "fov", Pick::Value(90.0)).unwrap();
        assert!(set(&m, app, false, &mut picks, "fullscreen", Pick::Choice(5)).is_err());
        assert!(set(&m, app, false, &mut picks, "keys", Pick::Choice(0)).is_err());
        assert_eq!(std::fs::read_to_string(app.join("game.ini")).unwrap(), "[Video]\nfullscreen = 0\nh = 1080\nw = 1920\n\n[Input]\nsens = 5.00\n");
        assert!(!app.join("portable.ini").exists());

        let v = view(&m, app, false, &picks).unwrap();
        let by = |id: &str| v.settings.iter().find(|s| s.id == id).unwrap();
        assert!(by("size").enabled);
        assert_eq!(by("size").chosen, Some(1));
        assert_eq!(by("sens").slider.as_ref().unwrap().value, Some(5.0));
        assert_eq!(by("log").chosen, Some(1));
        let data = app.join(port_setup::DATA).to_string_lossy().into_owned();
        assert_eq!(picked_args(&m, app, false, &picks), ["--verbose".to_string(), format!("--log={data}/log.txt"), "--fov".into(), "90".into()]);
        assert_eq!(picked_args(&m, app, false, &Picks::new()), Vec::<String>::new());

        // Set in the game's own menu: none of the choices.
        std::fs::write(app.join("game.ini"), "[Video]\nfullscreen = 2\n").unwrap();
        let v = view(&m, app, false, &picks).unwrap();
        let full = v.settings.iter().find(|s| s.id == "fullscreen").unwrap();
        assert_eq!((full.chosen, full.other.as_deref()), (None, Some("2")));
    }

    #[test]
    fn at_start_writes_new_ensure_and_always_and_the_portable_file_is_used_when_its_marker_is_there() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        let m = manifest(PORT);
        std::fs::write(app.join("portable.txt"), "").unwrap();
        at_start(&m, app, false, None);
        assert_eq!(std::fs::read_to_string(app.join("portable.ini")).unwrap(), "[Video]\nvsync = 1\nscale = 1\n\n[Launcher]\nskip = true\n");
        std::fs::write(app.join("portable.ini"), "[Video]\nvsync=0\nscale=\n").unwrap();
        at_start(&m, app, false, None);
        assert_eq!(std::fs::read_to_string(app.join("portable.ini")).unwrap(), "[Video]\nvsync=0\nscale=1\n\n[Launcher]\nskip=true\n");
        // A file that can't be read is left alone, and named.
        std::fs::write(app.join("portable.ini"), [0xff, 0xfe, 0x00]).unwrap();
        at_start(&m, app, false, None);
        assert_eq!(std::fs::read(app.join("portable.ini")).unwrap(), [0xff, 0xfe, 0x00]);
        assert_eq!(view(&m, app, false, &Picks::new()).unwrap().unreadable.len(), 1);
    }

    #[test]
    fn at_start_writes_the_players_copy_where_the_file_says_as_the_game_reads_it() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        let m = manifest(r#"{"format":1,"config":[{"format":"ini","file":"{install}/game.ini","game_path":"rom/path"},{"id":"t","format":"toml","file":"{install}/game.toml","game_path":"game_data_root"}]}"#);
        at_start(&m, app, false, None);
        assert!(!std::fs::read_to_string(app.join("game.ini")).unwrap_or_default().contains("path"));
        at_start(&m, app, false, Some(Path::new("/roms/game.iso")));
        assert_eq!(std::fs::read_to_string(app.join("game.ini")).unwrap(), "[rom]\npath = /roms/game.iso\n");
        assert_eq!(std::fs::read_to_string(app.join("game.toml")).unwrap(), "game_data_root = \"/roms/game.iso\"\n");
        at_start(&m, app, true, Some(Path::new("/roms/other.iso")));
        assert_eq!(std::fs::read_to_string(app.join("game.ini")).unwrap(), "[rom]\npath = Z:\\roms\\other.iso\n");
        assert_eq!(std::fs::read_to_string(app.join("game.toml")).unwrap(), "game_data_root = \"Z:\\\\roms\\\\other.iso\"\n");
    }

    #[test]
    fn places_stay_in_the_apps_folder_or_the_players_own_and_wine_has_only_the_apps() {
        let dir = tempfile::tempdir().unwrap();
        let app = dir.path();
        assert_eq!(host_path("{install}/a.ini", app, false), Some(app.join("a.ini")));
        assert_eq!(host_path("{data}/a.ini", app, false), Some(app.join(port_setup::DATA).join("a.ini")));
        assert_eq!(host_path("{install}/../a.ini", app, false), None);
        assert_eq!(host_path("{input:game}/a.ini", app, false), None);
        assert_eq!(host_path("relative.ini", app, false), None);
        assert_eq!(host_path("{local}/Game/a.ini", app, true), None);
        if let Some(local) = dirs::data_local_dir() {
            assert_eq!(host_path("{local}/Game/a.ini", app, false), Some(local.join("Game/a.ini")));
        }
    }

    #[test]
    fn picks_are_kept_per_app_folder() {
        let data = tempfile::tempdir().unwrap();
        let a = Path::new("/apps/A");
        let mut p = Picks::new();
        p.insert("log".into(), Pick::Choice(1));
        p.insert("fov".into(), Pick::Value(90.0));
        save_picks(data.path(), a, p.clone()).unwrap();
        assert_eq!(picks(data.path(), a), p);
        assert!(picks(data.path(), Path::new("/apps/B")).is_empty());
        save_picks(data.path(), a, Picks::new()).unwrap();
        assert!(picks(data.path(), a).is_empty());
    }
}
