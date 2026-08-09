# Migrating to Quiver Launcher 3.0

Quiver Launcher 3.0 uses [Velopack](https://docs.velopack.io/) for packaging and self-updates. If you are on **2.4.2 or older**, the in-app updater cannot update you to 3.0. You will have to download the 3.0 version from [GitHub Quiver Launcher releases](https://github.com/tgeorgiadis/quiver/releases). Apologies for the inconvenience, but I hope it will be worth it in the long run.

## Why this change?

Previously Quiver updated itself by downloading a zip and then running a small helper script to overwrite the install in place (eg on Windows this was a `.cmd` file). Velopack replaces that custom flow with:

- **Standard self-updates** across Windows, Linux, and macOS.
- **Fewer false threat detections.** Security tools often flagged the old helper script ("download something, then rewrite program files"). Velopack applies updates without that script.
- **Signed Windows builds.** Windows packages are digitally signed. Hopefully as reputation builds, SmartScreen warnings should become less common, though you may still see some early on.

## How to migrate

1. Download Quiver Launcher 3.0 (or whatever the latest version is) for your OS from [Releases](https://github.com/tgeorgiadis/quiver/releases) into a **new** folder.
2. Quit the old Quiver / Quiver Launcher if it is running.
3. Copy `apps.json`, `settings.json`, and `Apps/` from your current folder into the new one (see your OS below). `Cache/` is optional to copy over.
4. Run the new Quiver Launcher and confirm your library looks right, then delete the old install.

### Windows

**Current (2.4.x)**

```
Quiver/
├── Quiver.exe
├── apps.json          ← copy
├── settings.json      ← copy
├── Apps/              ← copy
└── Cache/             ← optional
```

**New (3.0)** — paste into the outer folder (not into `current/`):

```
QuiverLauncher/
├── QuiverLauncher.exe
├── current/           # app binaries — do not put library here
├── apps.json          ← paste
├── settings.json      ← paste
├── Apps/              ← paste
└── Cache/             ← optional
```

### Linux

**Current (2.4.x)**

```
MyFolder/
├── Quiver…            # old AppImage or binary
├── apps.json          ← copy
├── settings.json      ← copy
├── Apps/              ← copy
└── Cache/             ← optional
```

**New (3.0)** — put the AppImage in its own folder (it creates files beside itself), mark it executable, then paste beside it (or into `~/.local/share/QuiverLauncher/` if that folder is not writable):

```
MyFolder/
├── QuiverLauncher-x.y.z-linux-x64.AppImage
├── apps.json          ← paste
├── settings.json      ← paste
├── Apps/              ← paste
└── Cache/             ← optional
```

### macOS

**Current (2.4.x)**

```
MyFolder/
├── Quiver.app
├── apps.json          ← copy
├── settings.json      ← copy
├── Apps/              ← copy
└── Cache/             ← optional
```

**New (3.0)** — paste beside `QuiverLauncher.app` (or into `~/Library/Application Support/QuiverLauncher/` if the parent folder is not writable):

```
MyFolder/
├── QuiverLauncher.app
├── apps.json          ← paste
├── settings.json      ← paste
├── Apps/              ← paste
└── Cache/             ← optional
```

## After migrating

You'll be able to update from within Quiver Launcher again
