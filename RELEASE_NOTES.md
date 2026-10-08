# Quiver Launcher 3.5.0-rc.4

This is a prerelease for testing before 3.5.0.

## New in rc.4

- Apps from the App Catalog now get their releases and downloads from quiverlauncher.com instead of asking GitHub. Adding, installing, checking for updates and the version list should no longer hit GitHub's request limit.
- GitHub is only asked about apps that aren't in the catalog, or if quiverlauncher.com can't be reached.

## New in rc.3

- Add an app to your library straight from its App Catalog card with **+ Add**, or press **Y** on a controller.
- Apps already in your library are hidden from the App Catalog so new ones are easier to find. Untick **Hide apps in my library** to see them.
- The App Catalog fits smaller windows: the search and filters move onto their own lines and the cards fill the width. On Android it looks like the website on a phone.
- **View README** and **Show Changelog** in the Library now open the app's page, on its Overview and Releases tabs.
- Quiver uses less memory, especially with a big library: pictures load at the size they're shown, and cards are only built when they're near the screen.
- Quiver now asks once whether it may send anonymous usage data (see below).
- Smaller fixes: the "not verified yet" line on library cards scrolls like the rest of the card, and the **NEW** badge is green so it stands apart from **+ Add**.

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

## Kiosk mode

- New in **Settings → General**: Quiver starts fullscreen with only browsing and launching, for arcade cabinets and shared PCs.
- Press **Ctrl+Alt+K** to unlock it, and again to lock it. You can set a PIN for unlocking.

## Anonymous usage data

- Quiver asks once whether it may send anonymous usage data: which features get used, which apps are installed and launched, and errors. It's off unless you say yes.
- It never includes your name, files or folders. Change it any time in **Settings → General → Usage data**.

## Windows MSI installs

- Apps that release a `.msi` install through the Windows setup wizard, then you pick the program it installed.
- Updates for these apps always ask first.

## Flatpak on Linux (experimental)

- A Linux x64 Flatpak download is included again. It's experimental, so expect rough edges and please report any problems.
- The Flatpak doesn't update itself. Install a newer bundle to update it.

## macOS

- macOS downloads are available for Apple Silicon and Intel Macs.
- The app isn't notarized yet, so macOS asks you to confirm the first time you open it. If it says the app can't be opened, go to **System Settings → Privacy & Security** and choose **Open Anyway**.
- A native menu bar, Apple Silicon support, and fixes for installing `.dmg` apps.

## Other fixes

- The window no longer jumps while you move or resize it.
- Before an update installs, Quiver checks again which release to install, so an out-of-date saved choice isn't used.
- Release tags like `Version1.0.4` are no longer mixed up with other versions.
- Fixes for renamed filters and text contrast in the light theme.

Thanks to sdelavega, jeffsmith82 and MarllonMenezes for their fixes.
