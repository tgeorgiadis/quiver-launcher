# Linux on Chromium (Tauri's CEF runtime): spike report

Branch `chore/cef-linux-spike`, cut from `v4` (840d6d7). Measured on the Steam Deck (OLED, 90 Hz, SteamOS 3.8.1)
on the night of 2026-10-30. Not for release: see "Before it could ship".

## What was built

`apps/launcher/scripts/tauri-cef.sh build --bundles appimage` builds the launcher for Linux on Tauri's CEF runtime
(Chromium 151), the way Readest moved its Linux builds (tauri-apps/tauri#14963):

- Tauri from its `feat/cef` branch at `tauri-cef-v3.0.0-alpha.27`, through Readest's fork (`readest/tauri`), which
  adds the fix for keyboard focus after Alt-Tab. The dialog and fs plugins come from Readest's `feat/cef` plugins.
- Linux only. The CEF build has its own lockfile (`Cargo.cef.lock`) and a cargo `--config` (`.cargo/cef.toml`).
  Windows, macOS, Android and the WebKitGTK Linux build still build with plain `tauri` and crates.io Tauri 2, and
  resolve the optional `tauri-runtime-cef` dependency to an empty stub (`cef-stub/`).
- The AppImage is built in the `quiver-build` distrobox with the CLI from Readest's fork (its bundler deploys what
  Chromium loads at run time). Two build-host steps: install `patchelf`, and copy Ubuntu's
  `/usr/lib/x86_64-linux-gnu/nss/*.so` up one folder. Without that, Chromium loads SteamOS's own NSS modules,
  which need a newer glibc than the bundle's, and aborts at start.

The same source also builds the WebKitGTK AppImage. Both builds below come from it, so they differ only in the
engine.

## The numbers

Each figure is `requestAnimationFrame` frames per second inside the page, logged once a second by
`src/fps-probe.ts` (on only when `QUIVER_FPS_LOG` is set), while input was held. That is the frame rate the page
manages, the same way in both engines and both modes. The display runs at 90 Hz. gamescope's own counter read
90 throughout for both engines (it counts its output, not the app's frames), so it isn't used.

Runs: Game Mode, Quiver started through Steam on the player's own library (55 apps), 3 WebKitGTK and 4 CEF runs.
Desktop Mode (Plasma on Wayland; CEF goes through XWayland), 3 runs each. Ranges are across runs.

### Game Mode

| | WebKitGTK | CEF |
|---|---|---|
| D-pad held along Browse's first shelf: fps (avg / worst second) | **19–21 / 11–12** | **58–65 / 38–45** |
| …frames over 25 ms, per second | 13–14 | 3–5 |
| …longest frame | 150–205 ms | 45–100 ms |
| Library, right stick scrolling: fps avg / worst | 65–67 / 32–38 | 73–82 / 37–64 |
| Library, d-pad held down/up: fps avg / worst | 48–59 / 22–35 | 82–83 / 77–80 |
| Start-up, Steam's launch to the first picture | 4.9–5.5 s | 3.1–3.2 s (5.6 s on a new build's first start) |
| Memory (PSS, all processes), settled | 490–560 MiB | 700 MiB |
| Memory after the run | 690–770 MiB | 915–950 MiB (peaks 1.0–1.3 GiB while Browse loads art) |
| CPU while holding along Browse (100% = one core) | 134–136% | 105–108% |
| AppImage | 82.6 MiB (release), 83.6 MiB (this branch) | 183.7 MiB |

### Desktop Mode

| | WebKitGTK | CEF |
|---|---|---|
| Browse d-pad: fps avg / worst second | **19–20 / 13–15** | **56–58 / 38–41** |
| …frames over 25 ms, per second | 15.5–16 | 4.2–4.4 |
| Library, right stick: fps avg / worst | 48–50 / 22–33 | 79–82 / 55–66 |
| Library, d-pad: fps avg / worst | 44–48 / 29–33 | 79–84 / 70–77 |
| Start-up, launch to the first picture | 1.8–1.9 s | 2.4–2.6 s |
| Memory settled / after (one run) | ~420 / ~615 MiB | 680 / 900 MiB |
| CPU along Browse | 125% | 100% |

In short, Chromium triples the frame rate on Browse's shelves (the worst case on WebKitGTK), lifts the library
from about 50 to about 80, and cuts the long frames by three to four times, all on less CPU. It costs about
200–250 MiB more memory and 100 MiB more download. Start-up is faster through Steam in Game Mode and about half a
second slower started straight from the desktop.

### The Flatpak

`flatpak/com.quiverlauncher.QuiverLauncher.cef.yml` packs the CEF build the way Readest's Flathub build does
(flathub/com.bilingify.readest): the CEF build's deb (`scripts/tauri-cef.sh build --bundles deb`) puts the program
and the CEF distribution in `usr/share/Quiver Launcher/`, which goes into the Flatpak's `/app/lib/quiver-launcher`
on the GNOME 51 runtime. Two changes from the WebKitGTK Flatpak: `--socket=x11` in place of `wayland` +
`fallback-x11` (CEF draws through X11, and fallback-x11 leaves X11 out whenever Wayland is there), and no
`chrome-sandbox`. Readest adds zypak for Chromium's sandbox; Quiver turns that sandbox off, and its helper processes
started fine without it. Built on the Deck with Flathub's `org.flatpak.Builder`.

It runs: Chromium 151 on the GPU (ANGLE over radeonsi, not SwiftShader), the library drawn, no errors in its log. The
NSS crash the AppImage needed a fix for can't happen here, since the runtime brings its own NSS and glibc together.

Measured against the CEF AppImage, 3 runs each, alternating, each in a headless gamescope (1280×800, 90 Hz) on a
copy of the player's library, driven over DevTools: Browse opened, → and ← held along the first shelf, 3 s each,
four times, with key repeats every 33 ms. That isn't the Game Mode setup above (no Steam, the desktop layout,
keys instead of the pad), so compare these with each other only.

| | CEF AppImage | CEF Flatpak |
|---|---|---|
| Download | 183.7 MiB | 103.4 MiB bundle (plus the GNOME 51 runtime, shared with other apps) |
| Start-up, launch to the first picture | 2.6–2.9 s | 1.6–1.7 s |
| Memory (PSS), settled / after the holds | 648–669 / 873–895 MiB | 619–638 / 781–825 MiB |
| Browse holds: fps avg / worst second | 67–69 / 34–41 | 68–74 / 32–45 |
| …frames over 25 ms, per second | 4.1–5.4 | 3.6–5.1 |
| CPU over the holds | about 108% | about 101% |

The Flatpak draws as fast as the AppImage, starts a second sooner (nothing to mount and unpack), and uses a little
less memory. The WebKitGTK Flatpak was a few MiB because the runtime carried WebKitGTK; CEF adds about 100 MiB.

Not yet tried in the Flatpak: controllers and Steam's keyboard in Game Mode (it needs adding to Steam), the portal
file picker, sign-in through the browser, and self-update to a next bundle.

## What broke, and what was fixed

| | Result |
|---|---|
| Start-up | **Broke, fixed.** Chromium aborted (NSS's modules from SteamOS, glibc mismatch). Fixed by bundling them (`tauri.cef.conf.json`) and flattening the build host's `nss/`. |
| Controllers (`gamepad.rs`) | **Broke, fixed.** Readest's Alt-Tab fix moves X focus into Chromium's child window, so Tauri reports the window as unfocused all the time, and gamepad.rs stopped reading pads. Under CEF the page's own focus and blur now drive it (`page_focus`). Works in Game Mode and on the desktop, and lets go of the pads when another window is in front. |
| Layout | **Broke, fixed.** Chromium's classic scrollbar took 15 px, so the library showed 7 columns instead of 8. Now uses `OverlayScrollbar`, like WebKitGTK. |
| Extra processes | **Fixed.** Chromium picked up KDE's Plasma browser-integration extension and started its two helpers. Extensions are off now, and so is Safe Browsing (background requests to Google). |
| Steam's on-screen keyboard (`keyboard.rs`) | **Works.** A on the search field brought it up, and three presses on it typed "ggh" into Chromium's field. The field kept its focus while Steam's keyboard had the window's. |
| Keyboard after Alt-Tab | **Works** (Readest's fix). Alt-Tab away to Konsole and back, pointer parked outside: keys reached the page. |
| `startDragging` (`window-bar.tsx`) | **Works** on Plasma Wayland/XWayland. A drag of the empty top bar un-maximized and moved the window, a second drag moved it again, and the maximize button works. Readest's broken drag was on X11 desktops, which weren't tested. |
| Resizing an undecorated window | **Broken.** Pulling the window's corner does nothing: CEF's window has no resize borders (Readest's pitfall 4). Only matters with Quiver's own title bar, un-maximized. Readest drives resizing from pointer events. |
| File picker | **Works on the desktop.** The KDE portal dialog opened, Esc returned nothing, and CEF survived the mouse afterwards. **In Game Mode** there's no FileChooser portal, so rfd would fall back to zenity: the same desktop-sized dialog as GTK's. The launcher's own picker (picker.rs, file-picker.tsx, merged from `feature/first-start-setup`) takes over there, and works on CEF: the first start's setup chose an apps folder through it. Linux's WebKitGTK build now uses the portal too. |
| AppImage self-update (`update.rs`) | **Works.** A signed "4.0.0-alpha.6" served locally was downloaded, verified, swapped in, backed up, and relaunched. The new copy started and drew. |
| Remembered window place | **Changes.** CEF names the monitor `eDP-1`, GTK `ANX7530 U`, so a saved window position doesn't carry over between engines. |
| Web storage | **Starts empty.** CEF keeps its profile in `~/.cache/AppImage-Cache/com.quiverlauncher.app/cef`. Only review drafts and PostHog's anonymous id live in localStorage, so a switch loses little. |
| Not tested | The KWin script that steps out of full screen for Steam's keyboard on the desktop (`keyboard::kwin`). Bringing the browser forward for sign-in on Wayland (`browser.rs`). Both use `gtk_window()`, which has no GTK window behind it under CEF, so they likely need another way. |

## Before it could ship

- **The runtime.** `feat/cef` is an unsupported alpha, and the focus fix lives in Readest's fork. Pin both, and
  expect to follow them.
- **Window resizing** from pointer events (as Readest did), and the two `gtk_window()` paths above.
- **Flatpak.** Built and measured (above), and now the release's (`com.quiverlauncher.QuiverLauncher.yml`). Left:
  the checks it hasn't had (controllers, Steam's keyboard, the picker, sign-in and self-update inside the Flatpak).
- **The e2e suite.** tauri-driver (WebKitWebDriver) can't drive CEF. Either keep a WebKitGTK test build (Readest
  does), which then tests an engine nobody ships, or move the suite to the DevTools protocol: `QUIVER_CDP_PORT` is
  already in this branch, and WebdriverIO can attach over CDP.
- **CI.** Done: release.yml and ci.yml's appimage jobs build on CEF (`.github/cef-tools.sh`: the CLI, cached, and
  the `nss/` flatten), and `.github/cef-appimage-check.sh` fails a build whose AppImage lacks libcef, the NSS
  modules or libxkbcommon-x11, since the bundler skips what it can't find without failing.
- **Size and memory.** A 184 MiB AppImage, and about 200–250 MiB more RAM on a 16 GB Deck.
- **Clean-up.** `fps-probe.ts` stays opt-in or goes. WebKitGTK-only workarounds (`WEBKIT_SKIA_GPU_PAINTING_THREADS`,
  the card rules for WebKitGTK in `app.css` and `catalog.css`) could go from a Linux that no longer runs WebKitGTK.

## Recommendation

**Decided (2026-10-31), after the player used the CEF build on the Deck: Linux ships on CEF, the AppImage and the
Flatpak both.** What follows was the recommendation before that.

Go ahead with Chromium for Linux and the Steam Deck, starting with the AppImage. On the Deck it fixes the
complaint outright: Browse goes from about 20 fps with stalls up to 200 ms to about 60 fps, and the library to
about 80 of 90. Everything Quiver relies on works or has a small, known fix. Do it as a separate alpha AppImage for
the testers first, with the WebKitGTK build and the Flatpak unchanged, while window resizing, the Flatpak and the
e2e move to CDP are done. Then make it the only Linux build. The costs are a bigger download, more RAM, and
following an alpha Tauri runtime through Readest's fork.
