# Product

<!-- impeccable:product-schema 1 -->

The same record lives at the root of both repositories: Quiver Launcher
(`tgeorgiadis/quiver-launcher`) and the website, quiverlauncher.com
(`QuiverLauncher/QuiverLauncherWebsite`). Change both together.

## Platform

web

Quiver Launcher is a Tauri app whose interface is web code (React,
`apps/launcher`), shipped for Windows, macOS, Linux, Steam Deck and Android.
quiverlauncher.com is a Vite and React site. Both share `packages/ui` (tokens,
the catalog card, artwork, controls) and `packages/i18n`.

## Users

**Players come first.** People who play games but don't follow the scene, and
who want console ports and fan projects without hunting through GitHub release
pages. They browse the catalog, press Get, play, and expect their apps to stay
up to date. They use Quiver at a desk with a mouse, on the couch with a
controller (TV mode, and the Steam Deck's Game Mode), and on an Android phone.

Secondary audiences, served without getting in the players' way:

- **Developers** who make the apps: they list and manage them on the website
  (releases, setup files, save locations, media) and read players' feedback.
- **Quiver staff**, who review submissions, corrections, feedback and media
  in the website's staff pages.

## Product Purpose

One place to find, install, update and play the games and apps people release
on GitHub, GitLab and other forges: modern ports (source ports, recomps,
recreations) and fan projects. Browse the catalog on quiverlauncher.com or in
the launcher, press **Get**, and Quiver downloads the app, checks it, installs
it and keeps it up to date.

Success: a player gets from "I heard about this port" to playing it without
reading a release page, choosing between files, or knowing which forge it
lives on, and their library follows them to every device they play on.

## Positioning

- **One catalog, any forge.** Apps from GitHub, GitLab, Gitea/Forgejo
  (including Codeberg), Bitbucket, self-hosted GitLab, GitHub Enterprise and
  JSON release feeds sit in one catalog, searchable by console, platform and
  kind, with players' feedback on how each app runs.
- **Everywhere you play.** The same launcher and library on Windows, macOS,
  Linux, Steam Deck and Android, with a controller-first TV mode for the couch,
  and the library and playlists synced across devices when signed in.

## Operating Context

- **The launcher at a desk:** mouse and keyboard, windowed or full screen,
  library and Discover side by side with the website's catalog.
- **The couch:** TV mode is a full-screen launcher driven by a controller. It
  turns on by itself in the Steam Deck's Game Mode. Focus, navigation and
  readability at a distance matter there.
- **The phone:** Android 7 or newer, touch, narrow screens.
- **The website:** the public catalog, app pages, playlists, developer
  profiles, the download page, and signed-in areas for accounts, developers
  and Quiver staff.
- **Languages:** English, Português (Brasil), Español (Latinoamérica),
  Deutsch, Français, Русский, Italiano, 한국어, 简体中文 and 繁體中文. Text
  length and scripts vary; strings go through Lingui.

## Capabilities and Constraints

- **Checked installs.** Each catalog app's release files have their checksums
  recorded when they come out; a download that doesn't match is refused.
  Updates are offered once a release has been out for 48 hours, unless the
  player chooses to update early or always. This proves the file is the one
  the developer published, not that it is safe, and copy must not claim more.
- **Easy setup.** For some apps Quiver finds the player's own copy of the game
  and puts it where the app needs it. Quiver never hosts game files.
- **Library.** Sort and filter by console, playlists to share and follow, any
  repository, installed program or folder can be added.
- **Mods** from Thunderstore and GameBanana, for apps that support them.
- **Play your way:** add an app to Steam or the desktop; run Windows builds on
  Linux with Wine or Proton.
- **Sign in** with GitHub or Discord (optional) to sync.
- **Coming from Quiver Launcher 3:** apps, tags, hidden apps, pictures and tag
  filters carry over on first start.
- **Wording:** `docs/standards/copy.md` in the website repo is binding: plain,
  short, on the player's side; one word for one thing ("app", "Get",
  "feedback", "developer", "Quiver staff"); sentence case; no "please",
  "sorry" or exclamation marks.
- **The website and the launcher match.** The same thing looks and reads the
  same in both; a change to one is made in the other in the same piece of
  work (`docs/standards/design.md` in the website repo).
- **Licensing:** Quiver Launcher 4 is under the Business Source License,
  switching to MIT a year after each version.

## Brand Commitments

- Names: **Quiver** and **Quiver Launcher**, written exactly so; the domain
  is quiverlauncher.com. Owner names stay as their owners write them (GitHub,
  Steam Deck, Nintendo 64).
- The Quiver logo and app icons are the project's marks (`TRADEMARKS.md`);
  forks may not use them or a look that could be mistaken for Quiver's.
- The legal line, always this sentence: "Quiver never hosts game files. You
  bring your own copy of the game."
- Quiver is "Quiver", not "we", except on the privacy page and in emails.
- Other launchers are never named or compared with in pull requests,
  commits or issues.

## Evidence on Hand

- The live catalog (Convex), with real apps, releases, artwork and players'
  feedback; counts come from it, never from copy.
- The launcher screenshot in `README.md`, the logo and icon files listed in
  `TRADEMARKS.md`, and the website's `public/` images (og-image, icons).
- Community: the Discord server and the Patreon supporters, shown on the
  website's `/supporters` page.
- Not on hand: testimonials, press quotes, user counts or download numbers
  for marketing. Don't invent them.

## Product Principles

1. **Player first, in their words.** Every screen should make sense to someone
   who plays games but doesn't know what a decomp, a ROM dump or a release
   asset is.
2. **Get should just work.** The path from finding an app to playing it is
   one press; choices about files, forges and versions are Quiver's job.
3. **Truthful about safety.** Say exactly what the checks prove and nothing
   more; never host or point to game files.
4. **One Quiver everywhere.** Desk, couch and phone, launcher and website:
   the same thing looks, reads and behaves the same.
5. **Respect the developers.** Their apps, names, credits and releases are
   shown as they publish them.

## Accessibility & Inclusion

- Text meets WCAG AA contrast (4.5:1; 3:1 at 18px and up) in both the dark
  default and the light theme.
- Colour is never the only signal: a status also has a word or an icon.
- Everything works from the keyboard, and in TV mode from a controller, with
  a visible focus.
- Motion respects `prefers-reduced-motion`.
- Every visible string is translated; layouts hold up with longer languages
  and with Korean and Chinese scripts.
