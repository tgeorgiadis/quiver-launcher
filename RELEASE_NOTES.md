# Quiver Launcher 3.3.3-rc.2

- Fix Windows Play after 3.3.3-rc.1. The launch logger read `ProcessStartInfo.Environment` even when `UseShellExecute` was true, so every native `.exe` failed with “UseShellExecute property set to false in order to use environment variables.” Shell-execute launches no longer snapshot that dictionary. Linux sanitize and logging are unchanged.
- Fix Linux AppImage Play for ports such as Yu-Gi-Oh / psxrecomp. The setup host started, but pressing Play did nothing because the child inherited Quiver’s AppImage identity (`APPDIR`, `APPIMAGE`, `ARGV0`, `OWD`), an AppImage-prefixed `PATH`, and a stale `PWD`. Native games now launch with a host-like environment. Open Folder uses the same sanitizer.
- Play writes `quiver-launch.log` in the app install folder (and appends `launch-debug.log` next to user data) so a failed Linux launch can be diagnosed without a debugger.
