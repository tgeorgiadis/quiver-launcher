# Quiver Launcher 3.3.1

- Fix Linux zip installs that only unpacked a nested `.tar.gz` and dropped the rest of the app (e.g. psxrecomp / recomp-ui releases) ([#19](https://github.com/tgeorgiadis/quiver-launcher/issues/19))
- Fix maximized window restoring as fullscreen after minimize - removed custom restore logic so windowed/maximized state is preserved when coming back from the taskbar
- Add a Ko-fi button next to GitHub and Discord in the sidebar footer