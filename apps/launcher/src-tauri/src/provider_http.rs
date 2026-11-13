//! A narrow HTTP proxy for repository APIs whose hosts don't send CORS
//! headers, as many self-hosted forges don't. It only makes GET requests,
//! only over https, only to public hosts, and only forwards the headers a
//! provider needs. Redirects are followed by hand so the token never goes to
//! a different host.
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};
use std::time::Duration;

const MAX_BODY: usize = 10 * 1024 * 1024;
const TIMEOUT: Duration = Duration::from_secs(20);
const MAX_REDIRECTS: usize = 5;

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProviderRequest {
    pub url: String,
    #[serde(default)]
    pub headers: HashMap<String, String>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProviderResponse {
    pub status: u16,
    pub headers: HashMap<String, String>,
    pub body: String,
}

/// A provider's API answer, as text. The page can't read a host that doesn't
/// send CORS headers; this asks from Rust instead.
#[tauri::command]
pub async fn provider_http(request: ProviderRequest) -> Result<ProviderResponse, String> {
    let mut current = validate_url(
        reqwest::Url::parse(&request.url)
            .map_err(|_| "That repository address isn't valid.".to_string())?,
    )
    .await?;
    let mut headers = allowed_headers(&request.headers);
    let client = reqwest::Client::builder()
        .user_agent("QuiverLauncher/4")
        .redirect(reqwest::redirect::Policy::none())
        .timeout(TIMEOUT)
        .build()
        .map_err(|e| e.to_string())?;
    for redirects in 0..=MAX_REDIRECTS {
        let response = client
            .get(current.clone())
            .headers(headers.clone())
            .send()
            .await
            .map_err(|_| {
                "Couldn't reach the repository host. Check your connection and try again."
                    .to_string()
            })?;
        if response.status().is_redirection() {
            if redirects == MAX_REDIRECTS {
                return Err("The repository host redirected too many times.".into());
            }
            let location = response
                .headers()
                .get(reqwest::header::LOCATION)
                .and_then(|value| value.to_str().ok())
                .ok_or("The repository host redirected without an address.")?;
            let next = current
                .join(location)
                .map_err(|_| "The repository host redirected to an invalid address.")?;
            let next = validate_url(next).await?;
            // A token belongs to its host: never carry it to another one.
            if next.host_str() != current.host_str() {
                headers.remove(reqwest::header::AUTHORIZATION);
                headers.remove("private-token");
            }
            current = next;
            continue;
        }
        let status = response.status().as_u16();
        let headers = response_headers(response.headers());
        if response
            .content_length()
            .is_some_and(|length| length > MAX_BODY as u64)
        {
            return Err("The repository host's answer is too large.".into());
        }
        let bytes = response
            .bytes()
            .await
            .map_err(|_| "The repository host didn't finish its answer.".to_string())?;
        if bytes.len() > MAX_BODY {
            return Err("The repository host's answer is too large.".into());
        }
        let body = String::from_utf8(bytes.to_vec()).map_err(|_| {
            "The repository host answered with something other than text.".to_string()
        })?;
        return Ok(ProviderResponse {
            status,
            headers,
            body,
        });
    }
    Err("The repository host redirected too many times.".into())
}

/// Whether a URL is one the proxy may ask: https, a public host, and no local network.
async fn validate_url(url: reqwest::Url) -> Result<reqwest::Url, String> {
    if url.scheme() != "https" {
        return Err("Repository hosts must use https.".into());
    }
    let host = url
        .host_str()
        .ok_or("That repository address has no host.")?;
    let port = url.port_or_known_default().unwrap_or(443);
    if host.eq_ignore_ascii_case("localhost") {
        return Err("A repository host can't be this computer.".into());
    }
    if let Ok(ip) = host.parse::<IpAddr>() {
        if blocked_ip(ip) {
            return Err("A repository host can't be a private address.".into());
        }
        return Ok(url);
    }
    let addrs = tokio::net::lookup_host((host, port))
        .await
        .map_err(|_| "Couldn't look up the repository host.".to_string())?;
    for addr in addrs {
        if blocked_ip(addr.ip()) {
            return Err("A repository host can't be a private address.".into());
        }
    }
    Ok(url)
}

/// Loopback, private, link-local, multicast and documentation addresses, which
/// a repository host has no business being.
fn blocked_ip(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(ip) => {
            ip.is_loopback()
                || ip.is_private()
                || ip.is_link_local()
                || ip.is_broadcast()
                || is_documentation_v4(ip)
                || ip.is_unspecified()
                || ip.is_multicast()
                // Carrier-grade NAT, and the benchmark range.
                || (ip.octets()[0] == 100 && (64..=127).contains(&ip.octets()[1]))
                || (ip.octets()[0] == 198 && (18..=19).contains(&ip.octets()[1]))
        }
        IpAddr::V6(ip) => {
            ip.is_loopback()
                || ip.is_unspecified()
                || ip.is_multicast()
                || ip.is_unique_local()
                || ip.is_unicast_link_local()
                || is_documentation_v6(ip)
                || ip
                    .to_ipv4_mapped()
                    .is_some_and(|v4| blocked_ip(IpAddr::V4(v4)))
        }
    }
}

/// The IPv4 ranges reserved for documentation: 192.0.2.0/24, 198.51.100.0/24 and 203.0.113.0/24.
fn is_documentation_v4(ip: Ipv4Addr) -> bool {
    let [a, b, c, _] = ip.octets();
    (a == 192 && b == 0 && c == 2)
        || (a == 198 && b == 51 && c == 100)
        || (a == 203 && b == 0 && c == 113)
}

/// The IPv6 range reserved for documentation: 2001:db8::/32.
fn is_documentation_v6(ip: Ipv6Addr) -> bool {
    ip.segments()[0] == 0x2001 && ip.segments()[1] == 0x0db8
}

/// Only the headers a provider sends: its token, what it accepts, and its name.
fn allowed_headers(given: &HashMap<String, String>) -> reqwest::header::HeaderMap {
    let mut headers = reqwest::header::HeaderMap::new();
    for (name, value) in given {
        let lower = name.to_ascii_lowercase();
        if !matches!(
            lower.as_str(),
            "accept" | "content-type" | "authorization" | "private-token" | "user-agent"
        ) {
            continue;
        }
        if let (Ok(name), Ok(value)) = (
            reqwest::header::HeaderName::from_bytes(name.as_bytes()),
            reqwest::header::HeaderValue::from_str(value),
        ) {
            headers.insert(name, value);
        }
    }
    headers
}

/// The answer's headers the launcher reads: its type, caching, and rate limits.
fn response_headers(headers: &reqwest::header::HeaderMap) -> HashMap<String, String> {
    let mut out = HashMap::new();
    for (name, value) in headers {
        let lower = name.as_str().to_ascii_lowercase();
        if matches!(
            lower.as_str(),
            "content-type" | "etag" | "last-modified" | "link" | "retry-after"
        ) || lower.starts_with("x-ratelimit-")
            || lower.starts_with("ratelimit-")
        {
            if let Ok(value) = value.to_str() {
                out.insert(lower, value.to_string());
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::{Ipv4Addr, Ipv6Addr};

    #[test]
    fn private_addresses_are_blocked() {
        for ip in [
            Ipv4Addr::LOCALHOST,
            Ipv4Addr::new(10, 0, 0, 1),
            Ipv4Addr::new(172, 16, 0, 1),
            Ipv4Addr::new(192, 168, 1, 1),
            Ipv4Addr::new(169, 254, 169, 254),
            Ipv4Addr::new(100, 64, 0, 1),
            Ipv4Addr::UNSPECIFIED,
        ] {
            assert!(blocked_ip(IpAddr::V4(ip)), "{ip} should be blocked");
        }
        for ip in [
            Ipv6Addr::LOCALHOST,
            Ipv6Addr::UNSPECIFIED,
            Ipv6Addr::new(0xfc00, 0, 0, 0, 0, 0, 0, 1),
        ] {
            assert!(blocked_ip(IpAddr::V6(ip)), "{ip} should be blocked");
        }
        assert!(!blocked_ip(IpAddr::V4(Ipv4Addr::new(140, 82, 112, 4))));
    }

    #[test]
    fn only_provider_headers_go_through() {
        let headers = allowed_headers(&HashMap::from([
            ("Accept".into(), "application/json".into()),
            ("Authorization".into(), "token abc".into()),
            ("Cookie".into(), "secret".into()),
            ("Host".into(), "evil.test".into()),
        ]));
        assert_eq!(headers.len(), 2);
        assert_eq!(headers.get("accept").unwrap(), "application/json");
        assert_eq!(headers.get("authorization").unwrap(), "token abc");
        assert!(headers.get("cookie").is_none());
        assert!(headers.get("host").is_none());
    }

    #[test]
    fn only_readable_answer_headers_come_back() {
        let mut headers = reqwest::header::HeaderMap::new();
        headers.insert("x-ratelimit-remaining", "42".parse().unwrap());
        headers.insert("content-type", "application/json".parse().unwrap());
        headers.insert("set-cookie", "secret".parse().unwrap());
        let out = response_headers(&headers);
        assert_eq!(
            out.get("x-ratelimit-remaining").map(String::as_str),
            Some("42")
        );
        assert_eq!(
            out.get("content-type").map(String::as_str),
            Some("application/json")
        );
        assert!(!out.contains_key("set-cookie"));
    }
}
