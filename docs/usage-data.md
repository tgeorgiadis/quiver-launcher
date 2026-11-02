# Usage data

Like quiverlauncher.com, the launcher sends usage data to PostHog to help improve it unless you turn it off.
You can do that on first start, in Settings ("Send usage data"), or through your Quiver account. The launcher follows
your account's setting while you're signed in. Events are linked to your Quiver account when you're signed in;
earlier events from the same device may also become linked when you sign in. Signed-out events are not intentionally
associated with a Quiver account, but they can still carry a device-level analytics identifier.

The launcher sends named events only (`apps/launcher/src/telemetry.ts`): screens opened (each once per launch), catalog apps installed,
updated, launched and uninstalled (by slug; apps players add only by kind), failed installs and launches and why,
playlists made, shared and followed, searches that found nothing (their length, not their words), sign-in and
sign-out, and errors. Every event carries the launcher's version, OS and architecture.

Nothing is captured automatically (no clicks, text, page views or replays), and paths, home and apps folders,
and the computer's user name are removed from data sent. Signing out does not remove any earlier association
with an account from events already collected.

| Variable                     | When       | What                                                                                |
| ---------------------------- | ---------- | ----------------------------------------------------------------------------------- |
| `VITE_POSTHOG_PROJECT_TOKEN` | build time | The PostHog project token (the website's). Without it nothing is sent or shown.     |
| `VITE_POSTHOG_DEV`           | build time | `1` lets `pnpm dev` send too; dev builds send nothing otherwise.                    |
| `QUIVER_POSTHOG_HOST`        | run time   | Where events go; `https://us.i.posthog.com` unless set. The e2e tests use their mock. |
