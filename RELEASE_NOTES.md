# Quiver Launcher 3.1.0

## Upgrading from an older Quiver?

If you are on **2.4.2 or older**, the old in-app updater cannot take you to 3.x. Download a fresh copy from this release and move your library over.

**See the [migration guide](https://github.com/tgeorgiadis/quiver-launcher/blob/main/MIGRATING.md)** for what to copy and where it goes on Windows, Linux, and macOS.

If you are already on **3.0**, the in-app updater can take you to 3.1.0.

## What's new

### New settings

These are in Settings and were not in 3.0.0:

- **Prompt when catalog updates are available** — popup when catalog sources have changes to review (off by default; you can still open App Catalog anytime)
- **Prompt when library apps have updates** — popup for pending app updates (off by default; Quiver Launcher self-update prompts still appear)
- **Show catalog update badges on library cards** — small badge when catalog metadata for an app has changes to review
- **Library name style** — name only, name + project below, name (project) in the title, or project only. Custom display names always win when set
- **Library card tags** — featured / common tags, all tags, or hidden
- **Tag lines on library cards** — how many wrapped lines of tags each card can show

### Mods: multiple download files

Mod pages that offer more than one file (especially GameBanana) no longer treat the page as a single install.

- Choose which files to install; each file is tracked on its own
- Add or remove extra files later without uninstalling the others
- Version checks are per file, so an update on one download is not missed or applied to the wrong file

### App Catalog review

- **Search this list** — match name, project, repository, folder, display name, or tags (including tags that are not shown as chips)
- **Tag chips** — click to include or exclude. Catalog lists can declare `preferredTagFilters` (shown first, by frequency) and `hiddenTagFilters` (never shown as chips)
- Jump from a library catalog-update badge to that list’s review
- Catalog `folderName` changes do not retarget an existing install. Your current folder and files stay put; new Adds use the catalog folder name

### Also in this release

- Custom display names on apps; alphabetical library sort uses the name you see
- Quieter update prompts so you can review catalog and app updates when you want
- Gamepad and keyboard: highlight the catalog search box, then press Select to type
- Catalog list cards show the pending count only on **Review (n)**
