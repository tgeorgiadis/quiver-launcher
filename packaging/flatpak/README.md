# Flatpak packaging (experimental)

Based on [PR #39](https://github.com/tgeorgiadis/quiver-launcher/pull/39) by
jeffsmith82. The pack-flatpak workflow produces an x86_64 Flatpak bundle
as a CI artifact and attaches it to tagged GitHub Releases. It is not published
to Flathub. CI tests native game install/update/uninstall, then installs the
launcher bundle and checks both CLI startup and desktop startup under Xvfb.

## Build

Run these commands on Linux, from the repository root, with .NET 10 and Flatpak.
Use the current official builder: older distribution packages cannot compose
AppStream metadata with the 25.08 SDK.

~~~bash
flatpak remote-add --user --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user flathub org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08 org.flatpak.Builder
dotnet publish QuiverLauncher.Desktop/QuiverLauncher.Desktop.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishTrimmed=false -o publish/linux-x64
flatpak run org.flatpak.Builder --user --install --force-clean --repo=packaging/flatpak/repo \
  packaging/flatpak/build packaging/flatpak/io.github.tgeorgiadis.QuiverLauncher.yml
flatpak run io.github.tgeorgiadis.QuiverLauncher
flatpak build-bundle --runtime-repo=https://flathub.org/repo/flathub.flatpakrepo \
  packaging/flatpak/repo QuiverLauncher.flatpak \
  io.github.tgeorgiadis.QuiverLauncher master
~~~

On a headless machine or WSL without a session bus, prefix the builder command
with `dbus-run-session --` (as CI does), so it can invoke host Flatpak commands.

Use a clean publish directory, without personal library files. CI removes these
before packaging. The manifest consumes prebuilt .NET output; it builds pinned
libflatpak, OSTree and FUSE sources inside the SDK. Only the native libraries
are retained, for reading downloaded bundles. Flatpak installs and launches
are performed by the host's CLI, never by a nested package manager.

## Storage, launching and updates

Library data uses $XDG_DATA_HOME/QuiverLauncher, normally
~/.var/app/io.github.tgeorgiadis.QuiverLauncher/data/QuiverLauncher.
Downloads use its cache rather than sandbox-private /tmp, so the host
installer can read them. Existing AppImage libraries are not imported automatically.
Flatpak ownership records use the host data directory so portable and Flatpak
copies cannot silently claim the same installed app.

Games, Wine/Proton probes, Flatpak commands, file openers and Steam's keyboard
opener use flatpak-spawn --host. Its local sandbox environment is kept separate
from the game's environment. Sandbox-specific audio, D-Bus and XDG paths are
removed from the host environment; the X11 auth cookie is copied to persistent
storage. Debug logs redact environment arguments outside the existing allowlist.

Desktop and Steam game shortcuts target host commands directly. Steam-running
checks query the host and fail closed if the check fails. Flatpak Steam's game
files are read-only; its userdata directory is writable for shortcut creation.

The wrapper disables automatic Velopack self-updates. This experimental bundle
must be updated by installing a newer bundle. Automatic store updates require a
future Flatpak repository/Flathub release.

## Permissions

| Grant | Purpose |
|---|---|
| Network | Catalogs, release downloads and mods |
| IPC and X11 | Avalonia's X11 backend, including XWayland sessions |
| PulseAudio | Launcher audio |
| All devices | Launcher graphics and controller access |
| Host filesystem | Existing libraries and user-selected install folders |
| Flatpak Steam directory, read-only | Installed Proton discovery |
| Flatpak Steam userdata, read/write | Non-Steam shortcut configuration |
| org.freedesktop.Flatpak session bus | Host command execution |

Host execution grants broad access; this package does not isolate downloaded
games from the host. There is no system-helper permission. These permissions
still need review and justification before a Flathub submission. That submission
also needs a reproducible source/dependency build (or pinned downloadable release
inputs), a reviewed application ID and completed store metadata.

## Validation

Run the normal test suite plus the opt-in native bundle test in
[docs/flatpak.md](../../docs/flatpak.md). Before release, test the installed
package on a Linux desktop and Steam Deck:

1. Download and launch a native game, AppImage and Wine/Proton game; check audio,
   controller input, files with spaces in their names and external-drive installs.
2. Install, update and uninstall a Flatpak game; check that saves survive.
3. Launch generated desktop and Steam shortcuts after closing Quiver.
4. Add games with native and Flatpak Steam, both open and closed. Confirm the
   queued worker waits for Steam to exit and that restarting Steam retains entries.
5. Verify Open Folder, links, the Steam keyboard and persistent library storage.
6. Verify launcher updates are handled through Flatpak, with AppImage/MSI paths unchanged.

### Game process groups

Native games and Wine/Proton commands run in a dedicated host session using
setsid. A host-visible PID receipt lets CLI and foreground tracking wait for
remaining processes in that session after the initial command exits. The host
provides setsid and ps (normally installed by the distribution). Processes that
deliberately detach into a new session cannot be followed by process-group
tracking. Flatpak games use the host flatpak run command's own lifetime.
