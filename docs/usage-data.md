# Usage data

Like quiverlauncher.com, the launcher sends usage data to Google Analytics, and errors to Sentry, to help improve it
unless you turn it off. In the EU, the UK and Switzerland it asks first, and sends nothing until you allow it. You can
change your mind on first start, in Settings ("Send usage data"), or through your Quiver account. The launcher follows
your account's setting while you're signed in, but what it sends isn't linked to your account: only to a random
identifier for this computer, which is replaced if you turn usage data off.

The launcher sends named events only (`apps/launcher/src/telemetry.ts`): the launcher starting and first-run setup
finishing, screens opened (each once per launch), catalog apps installed, updated, launched and uninstalled (by slug;
apps players add only by kind), failed installs and launches and why, apps added, an app's folder renamed and the apps
folder moved, playlists made, shared and followed, searches that found nothing (their length, not their words), an
app's menu opened and its videos played, announcements dismissed and their links opened, the catalog falling back to
its backup, and sign-in and sign-out. Every event carries the launcher's version, OS and architecture.

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
