# Quiver Launcher 3.5.0

## New App Catalog, powered by QuiverLauncher.com

- The App Catalog now comes from [quiverlauncher.com](https://quiverlauncher.com) and is laid out like the website. Search, sort and filter by project type, platform, console and AI use.
- Cards show how each app runs from player feedback (**Runs well**, **Has issues**, **Doesn't run**), the game it's based on, its platforms and when it was last updated.
- Searching shows the games that match. Each game has a page with every way to play it, best first.
- Each app has a page with its README, its releases and player feedback. To leave feedback, use the button that opens the website.
- Add an app straight from its card with **+ Add**, or press **Y** on a controller. Apps already in your library say **Open in Library**.
- Apps already in your library are hidden so new ones are easier to find. Untick **Hide apps in my library** to see them.
- The catalog fits any window size, and on Android it looks like the website on a phone.
- App lists are gone. If you keep your own list, add it in **Settings → Advanced → My app list** (a JSON file or a URL).

## Safer installs and updates

- Updates go to the release quiverlauncher.com has verified, not just the newest one.
- Downloads are checked against the file the site recorded, and refused if it changed.
- Installing a release that isn't verified asks first. A blocked release asks twice. Automatic updates never install either.
- Library cards show when a newer release is out but not verified yet. **Versions → Install vX (not verified)** installs it if you want it.
- **Versions** has **Change Version**, **Reinstall** to fix a broken install, and **Back to verified updates** when you've picked a version or skipped an update.
- Catalog apps get their releases from quiverlauncher.com, so adding, installing and updating no longer hit GitHub's request limit.

## Library

- Apps in your library pick up the catalog's current name, icon and tags. Your own names, covers and tags stay.
- **View README** and **Show Changelog** open the app's page on its Overview and Releases tabs.
- New name style, **Project + name below**, to match the App Catalog cards.
- Removing an app that isn't installed no longer asks first.

## Kiosk mode

- New in **Settings → General**: Quiver starts fullscreen with only browsing and launching, for arcade cabinets and shared PCs.
- Press **Ctrl+Alt+K** to unlock it, and again to lock it. You can set a PIN for unlocking.

## Anonymous usage data

- Quiver asks once whether it may send anonymous usage data: which features get used, which apps are installed and launched, and errors. It's off unless you say yes.
- It never includes your name, files or folders. Change it any time in **Settings → General → Usage data**.

## More platforms

- macOS downloads for Apple Silicon and Intel Macs. The app isn't notarized yet, so macOS asks you to confirm the first time you open it. If it says the app can't be opened, go to **System Settings → Privacy & Security** and choose **Open Anyway**.
- A Linux x64 Flatpak (experimental). It doesn't update itself, so install a newer bundle to update it.
- Apps that release a `.msi` on Windows install through the setup wizard, then you pick the program it installed. Updates for these apps always ask first.

## Other improvements and fixes

- A **QuiverLauncher.com** button at the bottom of the sidebar.
- Quiver uses less memory, especially with a big library.
- If a Quiver update can't be installed, Quiver says why instead of offering the same update again.
- Quiver installed at the top of a drive (like G:) no longer loses its library when it updates.
- If quiverlauncher.com can't be reached securely, the App Catalog tries another address.
- The Mods screen loads mods the first time it opens.
- The window no longer jumps while you move or resize it.
- Release tags like `Version1.0.4` are no longer mixed up with other versions.
- Fixes for text contrast in the light theme.

Thanks to sdelavega, jeffsmith82 and MarllonMenezes for their fixes.
