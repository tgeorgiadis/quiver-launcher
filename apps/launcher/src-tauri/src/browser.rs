//! GitHub and Discord sign-in in the system browser. The website's auth only
//! returns to exact http(s) origins, so the launcher listens once on a fixed
//! loopback address (listed in the site's ALLOWED_AUTH_ORIGINS) for the code,
//! then sends the browser on to the website's "You're signed in" page.
use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;
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

/// Opens `url` in the browser and waits for the sign-in to come back.
pub fn sign_in(url: &str) -> Result<Callback, String> {
    let listener = TcpListener::bind("127.0.0.1:53682")
        .map_err(|_| "Another sign-in is already open. Finish or close it, then try again.".to_string())?;
    open(url)?;
    listener.set_nonblocking(true).map_err(|e| e.to_string())?;
    let deadline = Instant::now() + Duration::from_secs(600);
    while Instant::now() < deadline {
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

/// The system browser, or `QUIVER_BROWSER` (end-to-end tests).
fn open(url: &str) -> Result<(), String> {
    match std::env::var_os("QUIVER_BROWSER") {
        Some(program) => std::process::Command::new(program).arg(url).spawn().map(|_| ()).map_err(|e| e.to_string()),
        None => open::that_detached(url).map_err(|_| "Couldn't open your browser.".into()),
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
