# Quiver Launcher 3.3.3

- Filter catalog apps by platform (Windows, Linux, Mac, Android) using binaries on the latest release. First visit defaults to the OS you are running Quiver on; combo filters such as Windows + Linux (Proton/Wine) are remembered. Unlabeled `.zip`, `.7z`, and `.rar` assets count as Windows.
- Fix Linux AppImage Play for ports such as Yu-Gi-Oh / psxrecomp. The app's launcher started, but pressing Play did nothing because the child inherited Quiver’s AppImage identity (`APPDIR`, `APPIMAGE`, `ARGV0`, `OWD`), an AppImage-prefixed `PATH`, and a stale `PWD`. Native games now launch with a host-like environment. Open Folder uses the same sanitizer.
- Fix Windows Play after the launch logger. Native `.exe` starts no longer fail with “UseShellExecute property set to false in order to use environment variables.”
- Play writes `quiver-launch.log` in the app install folder (and appends `launch-debug.log` next to user data) so a failed launch can be diagnosed without a debugger.
- Retry opening a newly downloaded archive when Windows Defender or Search still has it locked, extract 7z/RAR with a streaming reader so solid archives install, and keep the progress bar moving during extraction.
