# Quiver Launcher 3.2.0

## Upgrading from an older Quiver?

If you are on **2.4.2 or older**, the old in-app updater cannot take you to 3.x. Download a fresh copy from this release and move your library over.

**See the [migration guide](https://github.com/tgeorgiadis/quiver-launcher/blob/main/MIGRATING.md)** for what to copy and where it goes on Windows, Linux, and macOS.

If you are already on **3.0 or 3.1**, the in-app updater can take you to 3.2.0.

## What's new

### Manually managed apps

You can add apps with no GitHub or GitLab repository. Quiver Launcher creates the folder and you drop the files in.

Later you can attach a repository on **Edit App Entry** to start downloads and updates. Existing files stay; Auto Update stays off until you pick a version. Clear the repository to go back to managing files yourself.

Catalog review also covers cases where a list starts or stops using a repository for an app you already have, or points it at a different one. **Merge** or **Replace** then updates the repository and clears version pins and Auto Update.

### App Catalog review

- Extra local tags and version pins no longer keep a row **Changed**
- **Merge** applies catalog metadata (name, icon, files, mods, and catalog tags) and keeps extra local tags and your version pin
- **Replace** applies the same catalog fields but drops extra local tags. It still keeps your version pin unless the catalog is changing whether the app is repository-managed or which repository it uses
- Tag chips in review: orange = library only, green = catalog only, gray = both
- Add is blocked when the catalog folder already belongs to another library app. **Add all** skips those and can show a message

### Library search

Search the current library list by name, tags, repository, or folder. If nothing matches, you get **No apps match this search** and a way to clear, including the × on the search box.

### Library card tags

The old **Library card tags** setting (featured / common / all / hidden) is gone. Featured did not work the way it was meant to: since a common tag like `nintendo` is used, it would show first even though most of the library already has it, so it was not a useful order.

Cards now show each app’s tags, clipped by **Tag lines on library cards**. Set that to **0 lines** to hide tags on cards; filters still work.

### Also in this release

- Smarter latest-release picking: prefers a stable release when Auto Update is on, unless you pin a version or the repo only publishes prereleases. This is what makes **Syphon Filter** downloads work now
