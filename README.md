# Quiver Launcher

[![Download](https://img.shields.io/github/v/release/tgeorgiadis/quiver-launcher?include_prereleases&label=Download)](https://github.com/tgeorgiadis/quiver-launcher/releases)
[![Windows](https://img.shields.io/badge/Windows-0078D4)](https://github.com/tgeorgiadis/quiver-launcher/releases)
[![macOS](https://img.shields.io/badge/macOS-555555)](https://github.com/tgeorgiadis/quiver-launcher/releases)
[![Linux](https://img.shields.io/badge/Linux-333333)](https://github.com/tgeorgiadis/quiver-launcher/releases)
[![Steam Deck](https://img.shields.io/badge/Steam_Deck-1A9FFF)](https://github.com/tgeorgiadis/quiver-launcher/releases)
[![Android](https://img.shields.io/badge/Android-3A7D44)](https://github.com/tgeorgiadis/quiver-launcher/releases)

[![Join the community on Discord](https://img.shields.io/badge/Join_the_community-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/5XRThpWHGk)
[![Support Quiver on Patreon](https://img.shields.io/badge/Support_on_Patreon-B8394A?style=for-the-badge&logo=patreon&logoColor=white)](https://www.patreon.com/c/quiverlauncher)

**One place to find, install, update and play the games and apps people release on GitHub and GitLab**,
such as PC ports, recompilations and fan projects. Browse the [Quiver catalog](https://quiverlauncher.com), press
**Get**, and Quiver downloads it, checks it and installs it. Then it keeps it up to date.

<img width="2559" height="1366" alt="quiver-launcher-screenshot" src="https://github.com/user-attachments/assets/4cae3f54-0e41-43a3-9925-1adbbf81071e" />

## What it does

- **A catalog to browse.** Search the [quiverlauncher.com](https://quiverlauncher.com) catalog by console, platform
  and type, and see what other players said about how each app runs.
- **Verified downloads.** Quiver records the checksum of each catalog app's release files when they come out. Every
  download is compared with it, and a file that changed since is refused. Automatic updates only install releases
  Quiver has verified. This checks that you get the file the developer published; it can't promise that the file
  itself is safe.
- **Your library, your way.** Sort and filter by console, make playlists, and share them or follow other people's.
  Add any GitHub or GitLab repository, a program already on your computer, or a folder you fill yourself.
- **Made for the couch.** TV mode turns Quiver into a full-screen launcher for a controller. It comes on by itself in
  the Steam Deck's Game Mode.
- **Mods.** Browse, install and update mods from Thunderstore and GameBanana for the apps that support them.
- **Play it your way.** Add any app to Steam or the desktop, and run Windows builds on Linux with Wine or Proton.
- **Sign in to sync** (optional). Sign in with GitHub or Discord to keep your library and playlists the same on every
  device.
- **In your language.** English, Português (Brasil), Español (Latinoamérica), Deutsch, Français, Русский, Italiano
  and 한국어.

## Download

Get the latest version from [Releases](https://github.com/tgeorgiadis/quiver-launcher/releases). Quiver updates
itself after that.

**Official downloads** come only from this repository's Releases page and from Quiver's own updates. A copy from
anywhere else isn't made by us.

| Your device | Download | Then |
| --- | --- | --- |
| **Windows** | `QuiverLauncher-win-Portable.zip` | Extract it anywhere and run `QuiverLauncher.exe`. It updates itself from there. |
| **macOS** | `…_universal.dmg` | Open it and drag Quiver Launcher into Applications. Works on Intel and Apple silicon. |
| **Linux** | `…_x86_64.flatpak` or `…_amd64.AppImage` | Flatpak: open it with your software center. AppImage: mark it executable, then run it. |
| **Steam Deck** | `…_x86_64.flatpak` or `…_amd64.AppImage` | In Desktop Mode, install the Flatpak with Discover, or mark the AppImage executable. Then add Quiver to Steam to start it from Game Mode. |
| **Android** | `…_arm64.apk` | Open it on your phone and allow installing from this source when asked. Android 7 or newer. |

## Coming from Quiver Launcher 3

Your apps come with you, and nothing needs downloading again. On its first start, Quiver Launcher 4 asks where your
Quiver Launcher 3 folder is (or finds it by itself) and adds your apps to your library. Anything already installed
stays where it is and plays from there. You can do this later too, in **Settings**.

Quiver Launcher 3 is still available on the [`v3` branch](https://github.com/tgeorgiadis/quiver-launcher/tree/v3)
and its [releases](https://github.com/tgeorgiadis/quiver-launcher/releases?q=v3).

## Privacy

Quiver sends optional usage data, such as which screens are opened and which apps are installed, to help improve
it. When you're signed in, events are linked to your Quiver account; earlier events from the same device may also
become linked after sign-in. It doesn't send what you type, your files, paths or your computer's name. You can turn
usage data off on first start, in **Settings**, or through your Quiver account.
See [what's sent, exactly](docs/usage-data.md).

## Help

- Ask on [Discord](https://discord.gg/5XRThpWHGk)
- [Report a bug or ask for a feature](https://github.com/tgeorgiadis/quiver-launcher/issues)

## License

Quiver Launcher 4 is **source-available** under the Business Source License 1.1. You can read, build, modify and test
the source code, contribute changes, and use official releases or copies you build yourself from our published source.
You can also share source code and modified builds for development and testing.

During the first year after a version is first published, production use is allowed only as described in the
[Additional Use Grant](LICENSE). Unofficial builds distributed by others do not have production-use permission. One
year after each version is first publicly available, that version becomes
[MIT licensed](https://opensource.org/license/mit).

The Quiver name, logo and icons are covered by a separate [trade mark policy](TRADEMARKS.md). Quiver Launcher 3 and
earlier remain MIT licensed. Quiver Launcher started as a fork of
[GithubLauncher](https://github.com/SirDiabo/GithubLauncher) by SirDiabo.

## Contributing

Want to build Quiver from source or help out? [CONTRIBUTING.md](CONTRIBUTING.md) explains how to set it up, run the
tests and send a pull request.
