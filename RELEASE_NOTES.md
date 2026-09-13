# Quiver Launcher 3.4.0-rc1

This is a preview of Quiver Launcher 3.4.0. Enable preview updates in Settings to receive release candidates.

- Check installed app updates first. The top update button now shows progress and Cancel, updates badges as results arrive, and lets catalogs, artwork, mods, and automatic installations continue separately. Installed checks have a one-minute deadline, with incomplete checks clearly reported. Ordinary unpinned GitHub apps use one release request instead of two.
- Show available app updates after a manual check even when automatic library update prompts are disabled. Apps assigned to automatic updates stay out of the manual update summary.
- Browse new catalog apps immediately while compatibility checks run automatically. Apps no longer disappear just because the shared platform index has not caught up. Results update in place, and a compact status row replaces the large warning blocks. Bulk Add includes only verified compatible apps.
- Refresh the App Catalog cards with optional list icons, clearer review counts, separate library totals, and Review apps or Browse apps actions. Completed reviews show green All reviewed text, and refresh failures remain visible. Tighten card spacing and improve narrow-screen layouts.
- Update Quiver on Android from inside the launcher. A banner, badge, and Settings section show available versions; download the APK in Quiver and confirm installation with Android. Downloads can be cancelled, and completed downloads remain available to retry installation. Existing Android users need to install this version manually once to get the updater.
- Improve download selection. Ignore JSON metadata, provenance files, checksums, and debug symbols; recognise more platform labels so iOS, macOS, Linux AppImage, handheld, and Xbox packages are not mistaken for Windows downloads. A single matching build can install without an unnecessary file picker.
- Improve keyboard, mouse, touch, and gamepad navigation across the library, catalog, Settings, menus, and dialogs. Fix text-field paste and focus handling, retain located install folders across restarts, and improve Android update button alignment.
- Create desktop and Steam shortcuts that launch the selected app directly, using its saved executable, arguments, working folder, and configured Linux runner.
- Fix deferred cover downloads on Linux and reject ZIP entries that escape the extraction folder before handing archives to system tools.
- Rework the library, catalog, Settings, and mod screens into shared components, with better handling of navigation, background work, and shutdown. Preserve existing settings and library data.
