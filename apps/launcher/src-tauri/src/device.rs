//! What usage data says about the system the launcher runs on (telemetry.ts): its version and whether it's a Steam
//! Deck. Nothing here can tell one computer from another of the same kind.

/// The system's version: "11.26200" on Windows, "steamos 3.7.13" on Linux, "15.5" on macOS, "14" on Android.
pub fn os_version() -> Option<String> {
    static VERSION: std::sync::OnceLock<Option<String>> = std::sync::OnceLock::new();
    VERSION.get_or_init(read_os_version).clone()
}

#[cfg(windows)]
fn read_os_version() -> Option<String> {
    let key = winreg::RegKey::predef(winreg::enums::HKEY_LOCAL_MACHINE).open_subkey(r"SOFTWARE\Microsoft\Windows NT\CurrentVersion").ok()?;
    windows_version(&key.get_value::<String, _>("CurrentBuildNumber").ok()?)
}

#[cfg(target_os = "linux")]
fn read_os_version() -> Option<String> {
    // Inside a Flatpak, /etc/os-release is the runtime's; the host's is here.
    let text = std::fs::read_to_string("/run/host/os-release").or_else(|_| std::fs::read_to_string("/etc/os-release")).ok()?;
    linux_version(&text)
}

#[cfg(target_os = "macos")]
fn read_os_version() -> Option<String> {
    answer("sw_vers", "-productVersion")
}

#[cfg(target_os = "android")]
fn read_os_version() -> Option<String> {
    answer("getprop", "ro.build.version.release")
}

#[cfg(not(any(windows, target_os = "linux", target_os = "macos", target_os = "android")))]
fn read_os_version() -> Option<String> {
    None
}

#[cfg(any(target_os = "macos", target_os = "android"))]
fn answer(program: &str, argument: &str) -> Option<String> {
    let output = std::process::Command::new(program).arg(argument).output().ok()?;
    let text = String::from_utf8(output.stdout).ok()?;
    Some(text.trim().to_string()).filter(|t| !t.is_empty())
}

/// Windows 11 still calls itself 10.0: only its build number tells them apart.
#[cfg_attr(not(windows), allow(dead_code))]
fn windows_version(build: &str) -> Option<String> {
    let number: u32 = build.trim().parse().ok()?;
    Some(format!("{}.{number}", if number >= 22000 { 11 } else { 10 }))
}

/// The distribution and its version, from os-release; a rolling one has only a name.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn linux_version(os_release: &str) -> Option<String> {
    let field = |name: &str| {
        os_release.lines().find_map(|line| {
            let value = line.strip_prefix(name)?.strip_prefix('=')?.trim().trim_matches(|c| c == '"' || c == '\'');
            Some(value.to_string()).filter(|v| !v.is_empty())
        })
    };
    let id = field("ID")?;
    Some(match field("VERSION_ID") {
        Some(version) => format!("{id} {version}"),
        None => id,
    })
}

/// A Steam Deck (or another of Valve's handhelds), in its Game Mode or desktop: Steam says so, or its board's maker does, as gamepad.rs goes by.
pub fn steam_deck(var: impl Fn(&str) -> Option<String>, vendor: Option<String>) -> bool {
    var("SteamDeck").is_some_and(|v| !v.trim().is_empty() && v.trim() != "0") || vendor.is_some_and(|v| v.trim() == "Valve")
}

/// Who made the computer's board, on Linux.
pub fn board_vendor() -> Option<String> {
    std::fs::read_to_string("/sys/class/dmi/id/board_vendor").ok()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn windows_11_is_told_from_10_by_its_build() {
        assert_eq!(windows_version("26200\n").as_deref(), Some("11.26200"));
        assert_eq!(windows_version("22000").as_deref(), Some("11.22000"));
        assert_eq!(windows_version("19045").as_deref(), Some("10.19045"));
        assert_eq!(windows_version("unknown"), None);
    }

    #[test]
    fn a_linux_version_is_its_distribution_and_number() {
        assert_eq!(linux_version("NAME=\"SteamOS\"\nID=steamos\nID_LIKE=arch\nVERSION_ID=3.7.13\n").as_deref(), Some("steamos 3.7.13"));
        assert_eq!(linux_version("ID=\"ubuntu\"\nVERSION_ID=\"24.04\"\n").as_deref(), Some("ubuntu 24.04"));
    }

    #[test]
    fn a_rolling_distribution_has_only_its_name() {
        assert_eq!(linux_version("NAME=Arch\nID=arch\nBUILD_ID=rolling\n").as_deref(), Some("arch"));
        assert_eq!(linux_version("NAME=Nothing\n"), None);
    }

    #[test]
    fn a_steam_deck_is_known_by_steam_or_its_board_s_maker() {
        let none = |_: &str| None;
        let steam = |name: &str| (name == "SteamDeck").then(|| "1".to_string());
        let not = |name: &str| (name == "SteamDeck").then(|| "0".to_string());
        assert!(steam_deck(steam, None));
        assert!(steam_deck(none, Some("Valve\n".into())));
        assert!(!steam_deck(not, Some("ASUSTeK COMPUTER INC.".into())));
        assert!(!steam_deck(none, None));
    }
}
