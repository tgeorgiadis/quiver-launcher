# Usage data

Like quiverlauncher.com, the launcher sends usage data to Google Analytics, and errors to Sentry, to help improve it
unless you turn it off. In the EU, the UK and Switzerland it asks first, and sends nothing until you allow it. You can
change your mind on first start, in Settings ("Send usage data"), or through your Quiver account. The launcher follows
your account's setting while you're signed in, but what it sends isn't linked to your account: only to a random
identifier for this computer, which is replaced if you turn usage data off.

The launcher sends named events only (`apps/launcher/src/telemetry.ts`): screens opened (each once per launch), catalog apps installed,
updated, launched and uninstalled (by slug; apps players add only by kind), failed installs and launches and why,
playlists made, shared and followed, searches that found nothing (their length, not their words), sign-in and
sign-out, and errors. Every event carries the launcher's version, OS and architecture.

Events go to quiverlauncher.com (the website's `worker/launcherUsage.ts`), which keeps the secret Google Analytics
needs and passes them on with your country, so Google doesn't get your IP address. Error reports go directly to
Sentry's EU region, which is set not to store IP addresses.

Nothing is captured automatically (no clicks, text, page views or replays), and paths, home and apps folders,
and the computer's user name are removed from data sent.

| Variable            | When       | What                                                                                |
| ------------------- | ---------- | ----------------------------------------------------------------------------------- |
| `VITE_USAGE_DATA`   | build time | `1` sends usage data. Without it nothing is sent or shown.                          |
| `VITE_USAGE_DEV`    | build time | `1` lets `pnpm dev` send too; dev builds send nothing otherwise.                    |
| `VITE_SENTRY_DSN`   | build time | Sentry's launcher project. Without it no error reports are sent.                    |
| `SENTRY_AUTH_TOKEN` | build time | Uploads the source maps for error reports. Without it they aren't made.             |
| `QUIVER_USAGE_HOST` | run time   | Where events go; `https://quiverlauncher.com` unless set. The e2e tests use their mock, and anywhere else sends no error reports. |
