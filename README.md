# Quiver Launcher 4

Quiver Launcher rebuilt with [Tauri 2](https://tauri.app), React and a small Rust core. Browse the
[quiverlauncher.com](https://quiverlauncher.com) catalog, press **Get**, and the app is added to your
library, downloaded, checked against the checksum Quiver pinned for it, and installed.

> Work in progress. Quiver Launcher 3.x lives on `main`.

## Layout

| Path            | What it is                                                                 |
| --------------- | -------------------------------------------------------------------------- |
| `apps/launcher` | The Tauri app: React UI in `src`, Rust core in `src-tauri`                  |
| `packages/api`  | Typed client for the catalog API (`https://api.quiverlauncher.com/api/v1`) |
| `packages/ui`   | Catalog components and styles shared with quiverlauncher.com               |
| `e2e`           | User journeys run against the real app with WebDriver and a mock catalog   |

## Develop

Needs Node 22, pnpm, Rust, and on Linux the [Tauri system libraries](https://tauri.app/start/prerequisites/).

```sh
pnpm install
pnpm dev          # run the app
pnpm typecheck && pnpm test
cargo test --manifest-path apps/launcher/src-tauri/Cargo.toml
```

End-to-end journeys (Linux and Windows; needs `cargo install tauri-driver`, and on Linux `webkit2gtk-driver` and `xvfb`):

```sh
pnpm --filter @quiver/e2e build
xvfb-run -a pnpm e2e
```

`QUIVER_API` points the app at another catalog and `QUIVER_DATA` at another data folder. A
`portable.txt` beside the executable keeps everything in a `data` folder next to it.

## Shared packages

quiverlauncher.com includes this repository as a git submodule and builds `packages/api` and `packages/ui` from
it, so the site shows apps with the same components as the launcher and checks its API against the same types.
Nothing is published to npm. A change here reaches the site when its submodule is moved to a newer commit.

## Usage data

Like quiverlauncher.com, the launcher sends anonymous usage data to PostHog unless it's turned off:
in Settings ("Send anonymous usage data"), from the notice shown on the first start, or, signed in,
on the Quiver account (the launcher follows the account's setting, as the website does). It sends
named events only (`apps/launcher/src/telemetry.ts`): the screens opened, apps installed, updated,
launched and uninstalled (catalog apps by slug; apps players add only by kind), installs and launches
that failed and why, playlists made, shared and followed, searches that found nothing (their length,
not their words), sign-in and sign-out, and errors. Every event carries the launcher's version, OS
and architecture. Nothing is captured by itself (no clicks, text, page views or replays), and paths,
the home and apps folders and the computer's user name are taken out of everything sent. Signed in,
events join the account's person, as on the website; signed out, they're anonymous.

| Variable                     | When       | What                                                                                |
| ---------------------------- | ---------- | ----------------------------------------------------------------------------------- |
| `VITE_POSTHOG_PROJECT_TOKEN` | build time | The PostHog project token (the website's). Without it nothing is sent or shown.     |
| `VITE_POSTHOG_DEV`           | build time | `1` lets `pnpm dev` send too; dev builds send nothing otherwise.                    |
| `QUIVER_POSTHOG_HOST`        | run time   | Where events go; `https://us.i.posthog.com` unless set. The e2e tests use their mock. |

Release builds read the token from the environment, such as `VITE_POSTHOG_PROJECT_TOKEN=phc_… pnpm build`;
CI passes the repository variable `POSTHOG_PROJECT_TOKEN`. See `apps/launcher/.env.example`.

## License

MIT
