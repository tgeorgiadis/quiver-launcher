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

## License

MIT
