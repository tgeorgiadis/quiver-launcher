# Usage data

Like quiverlauncher.com, the launcher sends usage data to Google Analytics, and errors to Sentry, to help improve it
unless you turn it off. In the EU, the UK and Switzerland it asks first, and sends nothing until you allow it. You can
change your mind on first start, in Settings ("Send usage data"), or through your Quiver account. The launcher follows
your account's setting while you're signed in, but what it sends isn't linked to your account: only to a random
identifier for this computer, which is replaced if you turn usage data off.

The launcher sends named events only (`apps/launcher/src/telemetry.ts`):

- **Starting up:** the launcher starting, first-run setup finishing, usage data being allowed (and where), and each kind
  of controller connected (Xbox, PlayStation and so on, not its name).
- **How it's set up** (`settings_loaded`, each launch): theme, language, TV mode, interface scale, the screen's resolution and scale, the
  library's layout and sort, the kind of Windows runner, and whether close to tray, full screen, a background picture or
  music, preview updates or a GitHub token are in use. Only that they are: never the files or the
  token.
- **What's in the library:** counts of its apps by kind, installed, with updates waiting, updating by themselves,
  pinned and hidden (`library_loaded`, each launch), and once a day each catalog app installed, with its version, how
  it updates and whether an update is waiting (`app_on_computer`, at most 40).
- **Apps:** catalog apps installed, updated, launched, closed (with the seconds played), uninstalled, added and removed,
  by slug, and when launched or closed, the version. Apps players add are named only by kind. Installs cancelled and
  at what stage; failed installs and launches, why, and what kind of failure; an app's folder renamed and the apps
  folder moved; an app's menu opened and its videos played.
- **Mods:** the mods screen opened, and mods installed, updated, uninstalled or failing to install: the catalog app's
  slug, the mod's id at its source, and the source.
- **Everything else:** screens opened (each once per launch), playlists made, shared and followed, searches that found
  nothing (their length, not their words), announcements dismissed and their links opened, the catalog falling back to
  its backup or not answering at all, the launcher's own updates starting and failing, and sign-in and sign-out.

Every event carries the launcher's version, how this copy was installed (installer, portable, Flatpak and so on), the
OS, its version (on Linux, with the distribution's name) and architecture, and whether it's a Steam Deck or in Steam's
Game Mode.

Google Analytics allows 25 properties on an event, and one over that loses those that count it as activity. Every event
carries 10, so one may add at most 15 (`usage-summary.test.ts` checks the largest).

Error reports cover crashes in the window, and failures installing, launching, uninstalling or reinstalling an app or
updating the launcher, with the app's slug and what was being done. Failures that aren't the launcher's to fix aren't
reported, such as no connection, a service that's down, a full disk, or a message already written for the player.
Errors from before the launcher knows your choice wait in memory until it does. If it can't start far enough to find
out (or opens on the library recovery screen), they're sent only if usage data was allowed the last time it ran and you
haven't turned it off on this computer since, so a change made on your account meanwhile counts from its next full start.

Events go to quiverlauncher.com (the website's `worker/launcherUsage.ts`), which keeps the secret Google Analytics
needs and passes them on with your country, so Google doesn't get your IP address. Error reports go directly to
Sentry's EU region, which is set not to store IP addresses, or any location more exact than your country. Those are
settings in the Sentry project (Security & Privacy), not in this code: its Advanced Data Scrubbing rules remove
`user.geo.city` and `user.geo.subdivision`, which Sentry works out from your IP address even when it doesn't store it.

Nothing is captured automatically (no clicks, text, page views or replays), and paths, home and apps folders,
and the computer's user name are removed from data sent.

| Variable            | When       | What                                                                                |
| ------------------- | ---------- | ----------------------------------------------------------------------------------- |
| `VITE_USAGE_DATA`   | build time | `1` sends usage data. Without it nothing is sent or shown.                          |
| `VITE_USAGE_DEV`    | build time | `1` lets `pnpm dev` send too; dev builds send nothing otherwise.                    |
| `VITE_SENTRY_DSN`   | build time | Sentry's launcher project. Without it no error reports are sent.                    |
| `SENTRY_AUTH_TOKEN` | build time | Uploads the source maps for error reports. Without it they aren't made.             |
| `QUIVER_USAGE_HOST` | run time   | Where events go; `https://quiverlauncher.com` unless set. The e2e tests use their mock, and anywhere else sends no error reports. |
