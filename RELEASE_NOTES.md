# Quiver Launcher 3.3.0

- **Android** release is now available (signed APK on GitHub Releases)
- App Catalog overhaul, including **grid view** and speed-ups
- Library cards: option to **truncate name, project, and versions**, with sliding text
- Refined **Details in card** option in library card view
- Multiple games can share the same repository with new release asset filter option ([#16](https://github.com/tgeorgiadis/quiver-launcher/issues/16))
- Thunderstore mods with dependencies now prompt to inform the user of additional mod installs ([#13](https://github.com/tgeorgiadis/quiver-launcher/issues/13))
- Desktop and Steam shortcuts no longer point at a dead AppImage mount path ([#12](https://github.com/tgeorgiadis/quiver-launcher/issues/12))
- Updating an AppImage install removes the previous AppImage instead of leaving both ([#15](https://github.com/tgeorgiadis/quiver-launcher/issues/15))
- Fix rar files not being extracted properly ([#17](https://github.com/tgeorgiadis/quiver-launcher/issues/14))
- Huge changes under the hood:
  - Migrated to **.NET 10**
  - Migrated to **Avalonia 12**
