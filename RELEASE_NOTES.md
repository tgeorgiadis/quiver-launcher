# Quiver Launcher 3.0

## Upgrading from an older Quiver?

If you are on **2.4.2 or older**, the old in-app updater cannot take you to 3.0. Download a fresh copy from this release and move your library over.

**See the [migration guide](https://github.com/tgeorgiadis/quiver-launcher/blob/v3.0.0/MIGRATING.md)** for what to copy and where it goes on Windows, Linux, and macOS.

## What's new

### Apps from GitLab
You can add apps hosted on GitLab, not just GitHub. When you add or edit an app, choose **GitLab** as the repository source.

### New name: Quiver Launcher
The app is now called **Quiver Launcher** (previously just Quiver). Same project, clearer name.

### Better updates, and signed Windows builds
The old in-app updater has been replaced with a more standard update system that is a better foundation going forward.

On Windows, builds are now digitally signed. That should mean fewer false virus warnings than before.

**Please note for Windows users:** you may still see a SmartScreen warning ("Windows protected your PC") when you first download or run Quiver Launcher. That is normal for a newly signed app. It should become less common over time as the signing certificate builds reputation. If you see it, use **More info → Run anyway** after you have confirmed you downloaded Quiver Launcher from this official GitHub release.

## Quick install notes

- **Windows:** extract the zip anywhere and run it.
- **Linux:** put the AppImage in its own folder, mark it executable, then run it.
- **macOS:** stay tuned. Still a work in progress while I get the certificate / app signing set up.
