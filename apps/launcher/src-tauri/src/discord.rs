//! Discord's Rich Presence: what the player is playing from Quiver, or that they're in it, on their Discord
//! profile, with a button for their friends to get Quiver too. It talks to the Discord app running on this
//! computer over its local socket (a named pipe on Windows), as Discord's own SDK would. No Discord, or the
//! player turned it off in Settings, and nothing is sent; Discord started later is found again.
use serde::Deserialize;
use serde_json::{json, Value};
use std::io::{Read, Write};
use std::sync::mpsc::{channel, Receiver, RecvTimeoutError, Sender};
use std::sync::Mutex;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

/// The Quiver Launcher application in Discord's Developer Portal: its name is what Discord shows the player playing.
/// `QUIVER_DISCORD_CLIENT_ID` stands in for it, for trying another application.
const CLIENT_ID: &str = "1555927002362675301";
/// Where the button goes: the website's download page.
const DOWNLOAD: &str = "https://quiverlauncher.com/download/";
const LOGO: &str = "https://quiverlauncher.com/icon-512.png";
/// How often a connection is checked, and a Discord that wasn't running looked for again.
const CHECK: Duration = Duration::from_secs(15);

/// A game the player started from Quiver, as its card shows it.
#[derive(Deserialize, Clone, Debug, PartialEq)]
pub struct Game {
    pub name: String,
    /// Its picture: only a web address, which Discord fetches itself.
    #[serde(default)]
    pub image: Option<String>,
}

/// What the player sees in the launcher's language, from the page.
#[derive(Deserialize, Clone, Debug, Default, PartialEq)]
pub struct Texts {
    /// Under Quiver Launcher's name while no game is running.
    pub idle: String,
    /// The button that opens Quiver's website.
    pub button: String,
    /// Under a game's name: "via Quiver Launcher".
    pub via: String,
}

#[derive(Default)]
struct State {
    /// Off until the page says the player's setting.
    enabled: bool,
    texts: Texts,
    /// The games running, the latest last, and when each started (Unix seconds).
    playing: Vec<(String, Game, u64)>,
}

impl State {
    fn activity(&self, opened: u64) -> Option<Value> {
        if !self.enabled {
            return None;
        }
        let buttons = json!([{ "label": clip(&self.texts.button, 32), "url": DOWNLOAD }]);
        Some(match self.playing.last() {
            Some((_, game, started)) => {
                let name = clip(&game.name, 128);
                let image = game.image.as_deref().filter(|i| i.starts_with("https://") && i.len() <= 256);
                let mut assets = json!({ "large_image": image.unwrap_or(LOGO), "large_text": name });
                if image.is_some() {
                    assets["small_image"] = json!(LOGO);
                    assets["small_text"] = json!("Quiver Launcher");
                }
                json!({
                    "type": 0,
                    // The game as the activity's title, with "via Quiver Launcher" under it.
                    "name": name,
                    "details": name,
                    "state": clip(&self.texts.via, 128),
                    // Discord's status lines read "Playing <the game>", not "Playing Quiver Launcher".
                    "status_display_type": 2,
                    "timestamps": { "start": started },
                    "assets": assets,
                    "buttons": buttons,
                })
            }
            None => json!({
                "type": 0,
                "state": clip(&self.texts.idle, 128),
                "timestamps": { "start": opened },
                "assets": { "large_image": LOGO, "large_text": "Quiver Launcher" },
                "buttons": buttons,
            }),
        })
    }
}

/// Discord takes 2 to `max` characters in each field.
fn clip(text: &str, max: usize) -> String {
    let mut text: String = text.trim().chars().take(max).collect();
    while text.chars().count() < 2 {
        text.push('\u{00a0}');
    }
    text
}

fn now() -> u64 {
    SystemTime::now().duration_since(UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or_default()
}

pub struct Presence {
    state: Mutex<State>,
    opened: u64,
    send: Mutex<Sender<Option<Value>>>,
}

impl Presence {
    pub fn new() -> Self {
        let (send, receive) = channel();
        std::thread::spawn(move || run(receive));
        Presence { state: Mutex::default(), opened: now(), send: Mutex::new(send) }
    }

    fn changed(&self, state: &State) {
        let _ = self.send.lock().unwrap().send(state.activity(self.opened));
    }

    /// The player's setting, and the texts in their language.
    pub fn set(&self, enabled: bool, texts: Texts) {
        let mut state = self.state.lock().unwrap();
        if state.enabled == enabled && state.texts == texts {
            return;
        }
        state.enabled = enabled;
        state.texts = texts;
        self.changed(&state);
    }

    pub fn started(&self, id: &str, game: Game) {
        let mut state = self.state.lock().unwrap();
        state.playing.push((id.to_string(), game, now()));
        self.changed(&state);
    }

    /// One of the game's runs closed: the game shown is the one started before it, else none.
    pub fn closed(&self, id: &str) {
        let mut state = self.state.lock().unwrap();
        let Some(at) = state.playing.iter().rposition(|(i, ..)| i == id) else { return };
        state.playing.remove(at);
        self.changed(&state);
    }
}

/// Keeps Discord showing the latest activity: connects when there's one to show, and lets go when there's none,
/// which clears it.
fn run(receive: Receiver<Option<Value>>) {
    let (mut want, mut connection, mut shown) = (None::<Value>, None::<Connection>, true);
    loop {
        match receive.recv_timeout(CHECK) {
            Ok(activity) => (want, shown) = (activity, false),
            Err(RecvTimeoutError::Timeout) => {}
            Err(RecvTimeoutError::Disconnected) => return,
        }
        // Only the latest of changes made together.
        while let Ok(activity) = receive.try_recv() {
            (want, shown) = (activity, false);
        }
        if shown {
            // Discord closed, or restarted: found again at the next check.
            if connection.as_mut().is_some_and(|c| c.ping().is_err()) {
                (connection, shown) = (None, want.is_none());
            }
            if want.is_none() || connection.is_some() {
                continue;
            }
        }
        let Some(activity) = &want else {
            connection = None;
            shown = true;
            continue;
        };
        if connection.is_none() {
            connection = Connection::open();
        }
        if let Some(c) = &mut connection {
            match c.set_activity(activity) {
                Ok(()) => shown = true,
                Err(_) => connection = None,
            }
        }
    }
}

trait Stream: Read + Write + Send {}
impl<T: Read + Write + Send> Stream for T {}

const HANDSHAKE: u32 = 0;
const FRAME: u32 = 1;
const CLOSE: u32 = 2;
const PING: u32 = 3;

struct Connection {
    stream: Box<dyn Stream>,
    nonce: u64,
}

impl Connection {
    fn open() -> Option<Self> {
        let id = std::env::var("QUIVER_DISCORD_CLIENT_ID").unwrap_or_else(|_| CLIENT_ID.to_string());
        if id.is_empty() {
            return None;
        }
        let mut connection = Connection { stream: socket()?, nonce: 0 };
        connection.write(HANDSHAKE, &json!({ "v": 1, "client_id": id })).ok()?;
        match connection.read().ok()? {
            // A client id Discord doesn't know, say.
            (op, reply) if op == CLOSE || reply["evt"] == "ERROR" => {
                eprintln!("Discord refused Rich Presence: {reply}");
                None
            }
            _ => Some(connection),
        }
    }

    fn set_activity(&mut self, activity: &Value) -> std::io::Result<()> {
        self.nonce += 1;
        let request = json!({
            "cmd": "SET_ACTIVITY",
            "args": { "pid": std::process::id(), "activity": activity },
            "nonce": self.nonce.to_string(),
        });
        self.write(FRAME, &request)?;
        match self.read()? {
            (CLOSE, _) => Err(std::io::ErrorKind::ConnectionAborted.into()),
            // Discord said no to this activity, a mistake of Quiver's: the connection is still good.
            (_, reply) if reply["evt"] == "ERROR" => {
                eprintln!("Discord didn't take the activity: {}", reply["data"]);
                Ok(())
            }
            _ => Ok(()),
        }
    }

    fn ping(&mut self) -> std::io::Result<()> {
        self.write(PING, &json!({}))?;
        match self.read()? {
            (CLOSE, _) => Err(std::io::ErrorKind::ConnectionAborted.into()),
            _ => Ok(()),
        }
    }

    fn write(&mut self, op: u32, body: &Value) -> std::io::Result<()> {
        let body = body.to_string();
        let mut frame = Vec::with_capacity(8 + body.len());
        frame.extend_from_slice(&op.to_le_bytes());
        frame.extend_from_slice(&(body.len() as u32).to_le_bytes());
        frame.extend_from_slice(body.as_bytes());
        self.stream.write_all(&frame)?;
        self.stream.flush()
    }

    fn read(&mut self) -> std::io::Result<(u32, Value)> {
        let mut header = [0; 8];
        self.stream.read_exact(&mut header)?;
        let op = u32::from_le_bytes(header[..4].try_into().unwrap());
        let len = u32::from_le_bytes(header[4..].try_into().unwrap()) as usize;
        if len > 1 << 20 {
            return Err(std::io::ErrorKind::InvalidData.into());
        }
        let mut body = vec![0; len];
        self.stream.read_exact(&mut body)?;
        Ok((op, serde_json::from_slice(&body).unwrap_or(Value::Null)))
    }
}

/// Discord's socket: the first of `discord-ipc-0` to `-9` that answers.
#[cfg(windows)]
fn socket() -> Option<Box<dyn Stream>> {
    (0..10).find_map(|n| {
        let pipe = std::fs::OpenOptions::new().read(true).write(true).open(format!(r"\\.\pipe\discord-ipc-{n}")).ok()?;
        Some(Box::new(pipe) as Box<dyn Stream>)
    })
}

/// Discord's socket, in the runtime or temporary folder, or inside Discord's Flatpak or Snap.
#[cfg(any(target_os = "linux", target_os = "macos"))]
fn socket() -> Option<Box<dyn Stream>> {
    use std::os::unix::net::UnixStream;
    let base = ["XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP"]
        .iter()
        .find_map(|name| std::env::var_os(name).filter(|v| !v.is_empty()))
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|| "/tmp".into());
    let folders = ["", "app/com.discordapp.Discord", "app/com.discordapp.DiscordCanary", "snap.discord", ".flatpak/dev.vencord.Vesktop/xdg-run"];
    folders.iter().flat_map(|folder| (0..10).map(move |n| (folder, n))).find_map(|(folder, n)| {
        let stream = UnixStream::connect(base.join(folder).join(format!("discord-ipc-{n}"))).ok()?;
        // A Discord that stops answering doesn't hold up the next check.
        stream.set_read_timeout(Some(Duration::from_secs(5))).ok()?;
        stream.set_write_timeout(Some(Duration::from_secs(5))).ok()?;
        Some(Box::new(stream) as Box<dyn Stream>)
    })
}

/// No Discord app on phones.
#[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
fn socket() -> Option<Box<dyn Stream>> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    fn state() -> State {
        State { enabled: true, texts: Texts { idle: "Browsing games".into(), button: "Get Quiver Launcher".into(), via: "via Quiver Launcher".into() }, playing: vec![] }
    }

    #[test]
    fn in_the_launcher_shows_quiver_with_its_button() {
        let activity = state().activity(100).unwrap();
        assert_eq!(activity["state"], "Browsing games");
        assert_eq!(activity["timestamps"]["start"], 100);
        assert_eq!(activity["assets"]["large_image"], LOGO);
        assert_eq!(activity["buttons"], json!([{ "label": "Get Quiver Launcher", "url": DOWNLOAD }]));
    }

    #[test]
    fn a_game_shows_by_name_with_its_picture_and_quivers_logo() {
        let mut state = state();
        let game = Game { name: "Some Game".into(), image: Some("https://example.com/icon.png".into()) };
        state.playing.push(("a".into(), game, 200));
        let activity = state.activity(100).unwrap();
        assert_eq!(activity["details"], "Some Game");
        assert_eq!(activity["status_display_type"], 2);
        assert_eq!(activity["name"], "Some Game");
        assert_eq!(activity["state"], "via Quiver Launcher");
        assert_eq!(activity["timestamps"]["start"], 200);
        assert_eq!(activity["assets"]["large_image"], "https://example.com/icon.png");
        assert_eq!(activity["assets"]["small_image"], LOGO);
    }

    #[test]
    fn a_picture_discord_cant_fetch_is_quivers_logo() {
        let mut state = state();
        state.playing.push(("a".into(), Game { name: "Some Game".into(), image: Some("asset://localhost/art.png".into()) }, 200));
        let activity = state.activity(100).unwrap();
        assert_eq!(activity["assets"]["large_image"], LOGO);
        assert!(activity["assets"].get("small_image").is_none());
    }

    #[test]
    fn turned_off_shows_nothing() {
        assert_eq!(State { enabled: false, ..state() }.activity(100), None);
    }

    #[test]
    fn fields_are_cut_to_what_discord_takes() {
        assert_eq!(clip("X", 32), "X\u{00a0}");
        assert_eq!(clip(&"a".repeat(40), 32).chars().count(), 32);
    }

    /// With Discord running: `QUIVER_DISCORD_CLIENT_ID=… cargo test --lib discord -- --ignored --nocapture`, then look at your profile.
    #[test]
    #[ignore]
    fn shows_on_the_running_discord() {
        let mut connection = Connection::open().expect("Discord running, and QUIVER_DISCORD_CLIENT_ID an application's");
        let mut state = state();
        state.playing.push(("a".into(), Game { name: "Some Game".into(), image: None }, now()));
        connection.set_activity(&state.activity(now()).unwrap()).unwrap();
        std::thread::sleep(Duration::from_secs(20));
        connection.ping().unwrap();
    }

    #[test]
    fn closing_a_game_shows_the_one_started_before() {
        let presence = Presence::new();
        presence.set(true, state().texts);
        presence.started("a", Game { name: "First".into(), image: None });
        presence.started("b", Game { name: "Second".into(), image: None });
        presence.closed("b");
        let state = presence.state.lock().unwrap();
        assert_eq!(state.activity(0).unwrap()["details"], "First");
    }
}
