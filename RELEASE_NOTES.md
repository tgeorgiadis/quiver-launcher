# Quiver Launcher 3.5.0-rc.2

This is a prerelease for testing before 3.5.0.

## New App Catalog

- The App Catalog now comes from [quiverlauncher.com](https://quiverlauncher.com). Search, sort and filter it like the website, including by project type, platform, console and AI use.
- Searching shows the games that match. Each game has a page with every way to play it.
- Each app has a page with its README, its releases and player feedback. To leave feedback, use the button that opens the website.
- App lists are gone. If you keep your own list, add it in **Settings → Advanced → My app list** (a JSON file or a URL).

## Safer installs and updates

- Updates go to the release quiverlauncher.com has verified, not just the newest one.
- Downloads are checked against the file the site recorded, and refused if it changed.
- Installing a release that isn't verified asks first. A blocked release asks twice. Automatic updates never install either.
- Library cards show when a newer release is out but not verified yet.
- Apps in your library pick up the catalog's current name, icon and tags. Your own names, covers and tags stay.

## macOS

- macOS downloads are available for Apple Silicon and Intel Macs.
- The app isn't notarized yet, so macOS asks you to confirm the first time you open it. If it says the app can't be opened, go to **System Settings → Privacy & Security** and choose **Open Anyway**.
- A native menu bar, Apple Silicon support, and fixes for installing `.dmg` apps.

## Other fixes

- The window no longer jumps while you move or resize it.
- Release tags like `Version1.0.4` are no longer mixed up with other versions.
- Fixes for renamed filters and text contrast in the light theme.

Thanks to sdelavega, jeffsmith82 and MarllonMenezes for their fixes.
