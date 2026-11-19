//! The computer's processor, graphics card and memory, which a player can send with their feedback on an app
//! (feedback.tsx) so its developer knows what it ran on. Read only when the feedback form opens; never sent with usage
//! data. What can't be read is left out, and the page may fill in the graphics card from WebGL (specs.ts).

use serde::Serialize;

#[derive(Serialize, Default, Debug, PartialEq)]
#[serde(rename_all = "camelCase")]
pub struct Specs {
    pub cpu: Option<String>,
    pub gpu: Option<String>,
    pub ram_gb: Option<f64>,
}

pub fn read() -> Specs {
    Specs { cpu: cpu().map(|c| tidy(&c)).filter(|c| !c.is_empty()), gpu: gpu(), ram_gb: ram_gb() }
}

/// Names as Windows and Linux write them, without the (R), (TM) and padding: "Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz"
/// is "Intel Core i5-8400 CPU @ 2.80GHz".
fn tidy(name: &str) -> String {
    let mut name = name.to_string();
    for mark in ["(R)", "(r)", "(TM)", "(tm)", "®", "™"] {
        name = name.replace(mark, "");
    }
    name.split_whitespace().collect::<Vec<_>>().join(" ")
}

/// Bytes of memory as a number of GB, to one decimal place.
#[cfg_attr(not(any(target_os = "linux", target_os = "android", target_os = "macos", windows)), allow(dead_code))]
fn gb(bytes: u64) -> f64 {
    (bytes as f64 / (1u64 << 30) as f64 * 10.0).round() / 10.0
}

/// The graphics card to name when a computer has more than one: a separate card over the one built into the
/// processor, and never a stand-in Windows uses for a remote screen or when no driver is installed.
#[cfg_attr(not(any(target_os = "linux", windows)), allow(dead_code))]
fn pick_gpu(names: Vec<String>) -> Option<String> {
    let score = |name: &str| {
        let lower = name.to_lowercase();
        if lower.contains("microsoft") || lower.contains("basic display") || lower.contains("remote display") || lower.contains("virtual") {
            return None;
        }
        let separate = lower.contains("nvidia") || lower.contains("geforce") || lower.contains("radeon rx") || lower.contains("radeon pro") || lower.contains("intel arc");
        Some(if separate { 2 } else { 1 })
    };
    names.into_iter().map(|n| tidy(&n)).filter(|n| !n.is_empty()).filter_map(|n| score(&n).map(|s| (s, n))).max_by_key(|(s, _)| *s).map(|(_, n)| n)
}

// ── Windows ─────────────────────────────────────────────────────────────────

#[cfg(windows)]
fn cpu() -> Option<String> {
    use winreg::{enums::HKEY_LOCAL_MACHINE, RegKey};
    let key = RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey(r"HARDWARE\DESCRIPTION\System\CentralProcessor\0").ok()?;
    key.get_value::<String, _>("ProcessorNameString").ok()
}

/// Each display adapter's name, from the display class's driver keys (0000, 0001…), which any user can read.
#[cfg(windows)]
fn gpu() -> Option<String> {
    use winreg::{enums::HKEY_LOCAL_MACHINE, RegKey};
    let class = RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey(r"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}").ok()?;
    let names = class
        .enum_keys()
        .filter_map(Result::ok)
        .filter(|k| k.len() == 4 && k.chars().all(|c| c.is_ascii_digit()))
        .filter_map(|k| class.open_subkey(&k).ok()?.get_value::<String, _>("DriverDesc").ok())
        .collect();
    pick_gpu(names)
}

/// The memory installed, as the computer's firmware counts it: 16 GB, not the 15.9 Windows has left to use.
#[cfg(windows)]
fn ram_gb() -> Option<f64> {
    let mut kb = 0u64;
    // SAFETY: the call writes one u64 to the pointer it's given.
    let ok = unsafe { windows_sys::Win32::System::SystemInformation::GetPhysicallyInstalledSystemMemory(&mut kb) };
    (ok != 0 && kb > 0).then(|| gb(kb * 1024))
}

// ── Linux and Android ───────────────────────────────────────────────────────

#[cfg(target_os = "linux")]
fn cpu() -> Option<String> {
    cpu_from_cpuinfo(&std::fs::read_to_string("/proc/cpuinfo").ok()?)
}

/// Phones rarely name their chip in /proc/cpuinfo; Android says it in a property.
#[cfg(target_os = "android")]
fn cpu() -> Option<String> {
    getprop("ro.soc.model").or_else(|| cpu_from_cpuinfo(&std::fs::read_to_string("/proc/cpuinfo").ok()?))
}

#[cfg(any(target_os = "linux", target_os = "android"))]
fn ram_gb() -> Option<f64> {
    mem_total(&std::fs::read_to_string("/proc/meminfo").ok()?).map(gb)
}

/// The cards the kernel shows under /sys/class/drm, named from the PCI ID list the system keeps (the host's, inside a
/// Flatpak). Without that list there's no name, and the page asks WebGL instead.
#[cfg(target_os = "linux")]
fn gpu() -> Option<String> {
    let ids = ["/run/host/usr/share/hwdata/pci.ids", "/usr/share/hwdata/pci.ids", "/run/host/usr/share/misc/pci.ids", "/usr/share/misc/pci.ids"]
        .iter()
        .find_map(|p| std::fs::read_to_string(p).ok())?;
    let mut names = vec![];
    for entry in std::fs::read_dir("/sys/class/drm").ok()?.filter_map(Result::ok) {
        let name = entry.file_name().to_string_lossy().to_string();
        // card0, card1…, not their outputs (card0-HDMI-A-1).
        if !name.starts_with("card") || name.contains('-') {
            continue;
        }
        let read = |file: &str| std::fs::read_to_string(entry.path().join("device").join(file)).ok().map(|s| s.trim().trim_start_matches("0x").to_lowercase());
        if let (Some(vendor), Some(device)) = (read("vendor"), read("device")) {
            if let Some(found) = pci_name(&ids, &vendor, &device) {
                names.push(found);
            }
        }
    }
    pick_gpu(names)
}

#[cfg(target_os = "android")]
fn gpu() -> Option<String> {
    None
}

#[cfg(target_os = "android")]
fn getprop(name: &str) -> Option<String> {
    let output = std::process::Command::new("getprop").arg(name).output().ok()?;
    Some(String::from_utf8(output.stdout).ok()?.trim().to_string()).filter(|t| !t.is_empty())
}

// ── macOS ───────────────────────────────────────────────────────────────────

#[cfg(target_os = "macos")]
fn sysctl(name: &str) -> Option<String> {
    let output = std::process::Command::new("sysctl").args(["-n", name]).output().ok()?;
    Some(String::from_utf8(output.stdout).ok()?.trim().to_string()).filter(|t| !t.is_empty())
}

#[cfg(target_os = "macos")]
fn cpu() -> Option<String> {
    sysctl("machdep.cpu.brand_string")
}

/// On Apple's own chips the graphics are part of the chip, so they share its name ("Apple M2"). An Intel Mac's card
/// is left to WebGL.
#[cfg(target_os = "macos")]
fn gpu() -> Option<String> {
    cpu().filter(|c| c.starts_with("Apple"))
}

#[cfg(target_os = "macos")]
fn ram_gb() -> Option<f64> {
    sysctl("hw.memsize")?.parse().ok().map(gb)
}

#[cfg(not(any(windows, target_os = "linux", target_os = "android", target_os = "macos")))]
fn cpu() -> Option<String> {
    None
}
#[cfg(not(any(windows, target_os = "linux", target_os = "android", target_os = "macos")))]
fn gpu() -> Option<String> {
    None
}
#[cfg(not(any(windows, target_os = "linux", target_os = "android", target_os = "macos")))]
fn ram_gb() -> Option<f64> {
    None
}

// ── Reading the files ───────────────────────────────────────────────────────

/// The processor's name in /proc/cpuinfo: "model name" on x86, "Model" or "Hardware" on ARM boards.
#[cfg_attr(not(any(target_os = "linux", target_os = "android")), allow(dead_code))]
fn cpu_from_cpuinfo(text: &str) -> Option<String> {
    ["model name", "Model", "Hardware"].iter().find_map(|key| {
        text.lines().find_map(|line| {
            let (name, value) = line.split_once(':')?;
            (name.trim() == *key).then(|| value.trim().to_string()).filter(|v| !v.is_empty())
        })
    })
}

/// MemTotal in /proc/meminfo, in bytes.
#[cfg_attr(not(any(target_os = "linux", target_os = "android")), allow(dead_code))]
fn mem_total(text: &str) -> Option<u64> {
    let line = text.lines().find(|l| l.starts_with("MemTotal:"))?;
    let kb: u64 = line.split_whitespace().nth(1)?.parse().ok()?;
    Some(kb * 1024)
}

/// A card's name from the PCI ID list: the marketing name in its brackets when it has one ("Navi 23 [Radeon RX
/// 6600/6600 XT/6600M]"), with its maker in front unless the name has it already.
#[cfg_attr(not(target_os = "linux"), allow(dead_code))]
fn pci_name(ids: &str, vendor: &str, device: &str) -> Option<String> {
    let mut lines = ids.lines();
    let vendor_name = lines.by_ref().find_map(|line| line.strip_prefix(vendor).filter(|rest| rest.starts_with("  ")).map(|rest| rest.trim().to_string()))?;
    let device_line = lines.take_while(|line| line.starts_with('\t') || line.starts_with('#') || line.is_empty()).find_map(|line| {
        let rest = line.strip_prefix('\t')?;
        if rest.starts_with('\t') {
            return None;
        }
        rest.strip_prefix(device).filter(|r| r.starts_with("  ")).map(|r| r.trim().to_string())
    })?;
    let name = match (device_line.find('['), device_line.rfind(']')) {
        (Some(start), Some(end)) if end > start => device_line[start + 1..end].to_string(),
        _ => device_line,
    };
    let maker = if vendor == "10de" {
        "NVIDIA"
    } else if vendor == "1002" {
        "AMD"
    } else if vendor == "8086" {
        "Intel"
    } else {
        vendor_name.split_whitespace().next().unwrap_or("")
    };
    Some(if name.to_lowercase().contains(&maker.to_lowercase()) { name } else { format!("{maker} {name}") })
}

#[cfg(test)]
mod tests {
    use super::*;

    const IDS: &str = "# comment\n\
1002  Advanced Micro Devices, Inc. [AMD/ATI]\n\
\t163f  VanGogh [AMD Custom GPU 0405]\n\
\t73ff  Navi 23 [Radeon RX 6600/6600 XT/6600M]\n\
\t\t1458 2331  Radeon RX 6600 EAGLE\n\
10de  NVIDIA Corporation\n\
\t2504  GA106 [GeForce RTX 3060 Lite Hash Rate]\n\
\t2505  Some Board\n\
8086  Intel Corporation\n\
\t46a6  Alder Lake-P GT2 [Iris Xe Graphics]\n";

    #[test]
    fn names_cards_from_the_pci_id_list() {
        assert_eq!(pci_name(IDS, "1002", "163f").as_deref(), Some("AMD Custom GPU 0405"));
        assert_eq!(pci_name(IDS, "1002", "73ff").as_deref(), Some("AMD Radeon RX 6600/6600 XT/6600M"));
        assert_eq!(pci_name(IDS, "10de", "2504").as_deref(), Some("NVIDIA GeForce RTX 3060 Lite Hash Rate"));
        assert_eq!(pci_name(IDS, "10de", "2505").as_deref(), Some("NVIDIA Some Board"));
        assert_eq!(pci_name(IDS, "8086", "46a6").as_deref(), Some("Intel Iris Xe Graphics"));
        // A device under another maker isn't taken for this one's.
        assert_eq!(pci_name(IDS, "8086", "2504"), None);
        assert_eq!(pci_name(IDS, "abcd", "0001"), None);
    }

    #[test]
    fn a_separate_card_wins_and_windows_stand_ins_never_count() {
        let names = |n: &[&str]| n.iter().map(|s| s.to_string()).collect();
        assert_eq!(pick_gpu(names(&["Intel(R) UHD Graphics 630", "NVIDIA GeForce RTX 3060"])).as_deref(), Some("NVIDIA GeForce RTX 3060"));
        assert_eq!(pick_gpu(names(&["AMD Radeon(TM) Graphics", "AMD Radeon RX 7600"])).as_deref(), Some("AMD Radeon RX 7600"));
        assert_eq!(pick_gpu(names(&["Microsoft Basic Display Adapter", "Intel(R) Iris(R) Xe Graphics"])).as_deref(), Some("Intel Iris Xe Graphics"));
        assert_eq!(pick_gpu(names(&["Microsoft Remote Display Adapter"])), None);
        assert_eq!(pick_gpu(vec![]), None);
    }

    #[test]
    fn reads_the_processor_and_memory_linux_shows() {
        assert_eq!(cpu_from_cpuinfo("processor\t: 0\nmodel name\t: AMD Custom APU 0405\nflags\t: fpu\n").as_deref(), Some("AMD Custom APU 0405"));
        assert_eq!(cpu_from_cpuinfo("processor : 0\nHardware : Qualcomm Snapdragon 8 Gen 2\n").as_deref(), Some("Qualcomm Snapdragon 8 Gen 2"));
        assert_eq!(cpu_from_cpuinfo("processor : 0\n"), None);
        assert_eq!(mem_total("MemTotal:       16303540 kB\nMemFree: 1 kB\n"), Some(16303540 * 1024));
        assert_eq!(gb(16 * (1 << 30)), 16.0);
        assert_eq!(gb(16303540 * 1024), 15.5);
    }

    /// What this computer says, to check by hand: `cargo test --lib specs::tests::this_computer -- --ignored --nocapture`.
    #[test]
    #[ignore]
    fn this_computer() {
        let specs = read();
        println!("{specs:?}");
        assert!(specs.cpu.is_some() && specs.ram_gb.is_some());
    }

    #[test]
    fn names_lose_their_trademark_signs() {
        assert_eq!(tidy("Intel(R) Core(TM) i5-8400 CPU @ 2.80GHz  "), "Intel Core i5-8400 CPU @ 2.80GHz");
    }
}
