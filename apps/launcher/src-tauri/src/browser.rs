//! GitHub and Discord sign-in in the system browser. The website's auth only
//! returns to exact http(s) origins, so the launcher listens once on a fixed
//! loopback address (listed in the site's ALLOWED_AUTH_ORIGINS) for the code,
//! then sends the browser on to the website's "You're signed in" page.
use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{Duration, Instant};

pub const RETURN_TO: &str = "http://127.0.0.1:53682/";

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub enum Callback {
    Code(String),
    Error(String),
}

/// Where the browser ends up: the website's page saying the player can close
/// the tab, or that sign-in didn't finish (`?error=`). `QUIVER_SITE` points it
/// elsewhere (end-to-end tests).
fn finished_page(found: &Callback) -> String {
    let site = std::env::var("QUIVER_SITE").unwrap_or_else(|_| "https://quiverlauncher.com".into());
    let site = site.trim_end_matches('/');
    match found {
        Callback::Code(_) => format!("{site}/signed-in/"),
        Callback::Error(e) => {
            // Only plain error codes ("access_denied") go into the header.
            let code = if !e.is_empty() && e.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_') { e } else { "oauth_error" };
            format!("{site}/signed-in/?error={code}")
        }
    }
}

/// The sign-in being waited for: another starting, or `cancel`, ends the one before.
static ATTEMPT: AtomicU64 = AtomicU64::new(0);

/// Stops waiting for the sign-in, which comes back as `Error("cancelled")`.
pub fn cancel() {
    ATTEMPT.fetch_add(1, Ordering::Relaxed);
}

/// Opens `url` with `open` and waits for the sign-in to come back. One started
/// before is let go of: its browser page, left behind or never seen, would
/// otherwise hold the address until it timed out.
pub fn sign_in(url: &str, open: impl FnOnce(&str) -> Result<(), String>) -> Result<Callback, String> {
    let attempt = ATTEMPT.fetch_add(1, Ordering::Relaxed) + 1;
    // The one before lets go of the address within one of its 100 ms naps.
    let mut tries = 0;
    let listener = loop {
        match TcpListener::bind("127.0.0.1:53682") {
            Ok(listener) => break listener,
            Err(_) if tries < 20 => tries += 1,
            Err(_) => return Err("Another sign-in is already open. Finish or close it, then try again.".into()),
        }
        std::thread::sleep(Duration::from_millis(100));
    };
    open(url)?;
    listener.set_nonblocking(true).map_err(|e| e.to_string())?;
    let deadline = Instant::now() + Duration::from_secs(600);
    while Instant::now() < deadline {
        if ATTEMPT.load(Ordering::Relaxed) != attempt {
            return Ok(Callback::Error("cancelled".into()));
        }
        let Ok((stream, _)) = listener.accept() else {
            std::thread::sleep(Duration::from_millis(100));
            continue;
        };
        let _ = stream.set_nonblocking(false);
        let _ = stream.set_read_timeout(Some(Duration::from_secs(5)));
        let mut line = String::new();
        let _ = BufReader::new(&stream).read_line(&mut line);
        // "GET /?convexAuthCode=... HTTP/1.1"
        let target = line.split(' ').nth(1).unwrap_or("/");
        let query = url_query(target);
        let found = query.iter().find_map(|(k, v)| match k.as_str() {
            "convexAuthCode" => Some(Callback::Code(v.clone())),
            "convexAuthError" => Some(Callback::Error(v.clone())),
            _ => None,
        });
        let mut stream = stream;
        let Some(found) = found else {
            // The browser asking for a favicon, or a stray visit.
            let _ = stream.write_all(b"HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            continue;
        };
        let _ = write!(
            stream,
            "HTTP/1.1 302 Found\r\nLocation: {}\r\nCache-Control: no-store\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            finished_page(&found)
        );
        return Ok(found);
    }
    Err("Sign-in timed out. Try again.".into())
}

fn url_query(target: &str) -> Vec<(String, String)> {
    let query = target.split_once('?').map(|(_, q)| q).unwrap_or("");
    query
        .split('&')
        .filter_map(|pair| pair.split_once('='))
        .map(|(k, v)| (decode(k), decode(v)))
        .collect()
}

fn decode(s: &str) -> String {
    let mut out = Vec::with_capacity(s.len());
    let mut bytes = s.bytes();
    while let Some(b) = bytes.next() {
        let hex = |b: Option<u8>| b.and_then(|b| (b as char).to_digit(16));
        out.push(match b {
            b'+' => b' ',
            b'%' => match (hex(bytes.next()), hex(bytes.next())) {
                (Some(h), Some(l)) => (h * 16 + l) as u8,
                _ => b'?',
            },
            b => b,
        });
    }
    String::from_utf8_lossy(&out).into()
}

/// The system browser, from Quiver's window. On a Wayland desktop the browser
/// gets the desktop's leave to come to the front, as if the player had
/// clicked it: otherwise KDE leaves a browser that's open already (or
/// minimised) where it is, and the page goes unseen behind the launcher.
/// Steam's Game Mode shows only what Steam starts, so there it opens in
/// Steam's own browser.
pub fn open_from(window: &tauri::WebviewWindow, url: &str) -> Result<(), String> {
    #[cfg(target_os = "linux")]
    if std::env::var_os("QUIVER_BROWSER").is_none() {
        if crate::keyboard::game_mode(|name| std::env::var(name).ok()) {
            // Straight to Steam, as its keyboard is asked for: through xdg-open, the page took seconds to come up,
            // and a player who pressed again opened it twice.
            let steam = format!("steam://openurl/{url}");
            return if crate::keyboard::to_pipe(&steam) { Ok(()) } else { open(&steam, None) };
        }
        let (sent, token) = std::sync::mpsc::channel();
        let on = window.clone();
        let asked = window.run_on_main_thread(move || {
            let _ = sent.send(on.gtk_window().ok().and_then(|window| activation::token(&window)));
        });
        let token = asked.ok().and_then(|_| token.recv_timeout(Duration::from_secs(2)).ok()).flatten();
        return open(url, token);
    }
    #[cfg(not(target_os = "linux"))]
    let _ = window;
    open(url, None)
}

/// The system browser, or `QUIVER_BROWSER` (end-to-end tests).
fn open(url: &str, activation: Option<String>) -> Result<(), String> {
    match std::env::var_os("QUIVER_BROWSER") {
        Some(program) => std::process::Command::new(program).arg(url).spawn().map(|_| ()).map_err(|e| e.to_string()),
        #[cfg(target_os = "android")]
        None => {
            let _ = activation;
            crate::android::open_url(url)
        }
        #[cfg(not(target_os = "android"))]
        None => crate::host::open(url, activation).map_err(|_| "Couldn't open your browser.".into()),
    }
}

/// Wayland's leave for another program to come to the front (xdg-activation),
/// asked of the desktop for Quiver's window, which has the focus as the player
/// picks what opens. GTK 3 asks for it only under GNOME.
#[cfg(target_os = "linux")]
mod activation {
    use gtk::glib::translate::ToGlibPtr;
    use gtk::prelude::*;
    use wayland_client::backend::{Backend, ObjectId};
    use wayland_client::globals::{registry_queue_init, GlobalListContents};
    use wayland_client::protocol::{wl_registry::WlRegistry, wl_surface::WlSurface};
    use wayland_client::{Connection, Dispatch, Proxy, QueueHandle};
    use wayland_protocols::xdg::activation::v1::client::xdg_activation_token_v1::{self, XdgActivationTokenV1};
    use wayland_protocols::xdg::activation::v1::client::xdg_activation_v1::XdgActivationV1;

    /// None under X11, or where the desktop gives none. On GTK's thread: it uses GTK's connection to the desktop.
    pub fn token(window: &gtk::ApplicationWindow) -> Option<String> {
        let display = WidgetExt::display(window);
        if display.type_().name() != "GdkWaylandDisplay" {
            return None;
        }
        let surface = window.window()?;
        // SAFETY: a Wayland display and one of its windows, both GDK's and alive while this runs.
        let (wl_display, wl_surface) = unsafe {
            let display: *mut gtk::gdk::ffi::GdkDisplay = display.to_glib_none().0;
            let surface: *mut gtk::gdk::ffi::GdkWindow = surface.to_glib_none().0;
            (gdk_wayland_sys::gdk_wayland_display_get_wl_display(display.cast()), gdk_wayland_sys::gdk_wayland_window_get_wl_surface(surface.cast()))
        };
        if wl_display.is_null() || wl_surface.is_null() {
            return None;
        }
        // SAFETY: GDK's wl_display, which outlives this; the connection only adds a queue of its own to it.
        let connection = Connection::from_backend(unsafe { Backend::from_foreign_display(wl_display.cast()) });
        let (globals, mut queue) = registry_queue_init::<Token>(&connection).ok()?;
        let handle = queue.handle();
        let activation: XdgActivationV1 = globals.bind(&handle, 1..=1, ()).ok()?;
        // SAFETY: the wl_surface of GDK's window, a live wl_surface proxy on this connection.
        let surface = WlSurface::from_id(&connection, unsafe { ObjectId::from_ptr(WlSurface::interface(), wl_surface.cast()) }.ok()?).ok()?;
        let asked = activation.get_activation_token(&handle, ());
        asked.set_surface(&surface);
        asked.commit();
        let mut token = Token(None);
        for _ in 0..3 {
            if token.0.is_some() || queue.roundtrip(&mut token).is_err() {
                break;
            }
        }
        asked.destroy();
        activation.destroy();
        token.0.filter(|t| !t.is_empty())
    }

    struct Token(Option<String>);

    impl Dispatch<XdgActivationTokenV1, ()> for Token {
        fn event(token: &mut Self, _: &XdgActivationTokenV1, event: xdg_activation_token_v1::Event, _: &(), _: &Connection, _: &QueueHandle<Self>) {
            if let xdg_activation_token_v1::Event::Done { token: given } = event {
                token.0 = Some(given);
            }
        }
    }

    impl Dispatch<XdgActivationV1, ()> for Token {
        fn event(_: &mut Self, _: &XdgActivationV1, _: <XdgActivationV1 as Proxy>::Event, _: &(), _: &Connection, _: &QueueHandle<Self>) {}
    }

    impl Dispatch<WlRegistry, GlobalListContents> for Token {
        fn event(_: &mut Self, _: &WlRegistry, _: <WlRegistry as Proxy>::Event, _: &GlobalListContents, _: &Connection, _: &QueueHandle<Self>) {}
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn reads_the_code_from_the_callback() {
        let q = super::url_query("/?convexAuthCode=a%2Bb%3D&x=1");
        assert_eq!(q[0], ("convexAuthCode".into(), "a+b=".into()));
    }

    #[test]
    fn sends_the_browser_to_the_website_without_the_code() {
        use super::{finished_page, Callback};
        assert_eq!(finished_page(&Callback::Code("secret".into())), "https://quiverlauncher.com/signed-in/");
        assert_eq!(finished_page(&Callback::Error("access_denied".into())), "https://quiverlauncher.com/signed-in/?error=access_denied");
        // Nothing from the query string can add a header.
        assert_eq!(finished_page(&Callback::Error("x\r\nSet-Cookie: a=b".into())), "https://quiverlauncher.com/signed-in/?error=oauth_error");
    }
}
