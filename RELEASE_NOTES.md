# Quiver Launcher 3.3.3-rc.1

- Fix Linux AppImage Play for ports such as Yu-Gi-Oh / psxrecomp. The setup host started, but pressing Play did nothing because the child inherited Quiver’s AppImage identity (`APPDIR`, `APPIMAGE`, `ARGV0`, `OWD`), an AppImage-prefixed `PATH`, and a stale `PWD`. Native games now launch with a host-like environment. Open Folder uses the same sanitizer.
- Play writes `quiver-launch.log` in the app install folder (and appends `launch-debug.log` next to user data) so a failed Linux launch can be diagnosed without a debugger.
