# Contributing

Thanks for wanting to help with Quiver Launcher! Everyone is welcome to contribute: bug reports, fixes, features,
translations and docs.

- **Found a bug or have an idea?** [Open an issue](https://github.com/tgeorgiadis/quiver-launcher/issues), or ask on
  [Discord](https://discord.gg/5XRThpWHGk) first if you're not sure.
- **Want to change something?** Open a pull request. For a big change, open an issue first so we can agree on the
  approach before you spend time on it.

Quiver Launcher 4 is source-available under the Business Source License 1.1, and each version becomes MIT licensed one
year after it's published. You're free to read, build, change and test the code to work on a contribution. See
[LICENSE](LICENSE) for the exact terms and [TRADEMARKS.md](TRADEMARKS.md) for the Quiver name and logo.

## Contributor License Agreement

As with Apache, Google and many other projects, contributions to Quiver Launcher are made under a Contributor License
Agreement (CLA). You sign it once, on your first pull request, and it covers everything you contribute after that.

The CLA lets Thomas Georgiadis publish your work as part of Quiver Launcher, first under the Business Source License
and later under MIT, as each version's license says. **You keep the copyright to your work**, and you're free to use it
anywhere else.

### Signing it

When you open your first pull request, the CLA Assistant bot asks you to accept this agreement. Follow its
instructions to sign. You only need to sign once, unless the agreement changes. A pull request can't be merged until
it's signed.

Translations, artwork and other work only come into Quiver Launcher through pull requests, so the agreement is always
signed first. If you share something in an issue or on Discord, we'll ask you to open a pull request for it.

### The agreement

By signing, you agree to the following with Thomas Georgiadis ("the maintainer") for every contribution you submit to
Quiver Launcher, now or later.

A **contribution** is any code, artwork, documentation, translation or other work you submit for inclusion in Quiver
Launcher, by pull request, issue or otherwise. General discussion, and anything you clearly mark as "not a
contribution", isn't one.

1. **You keep your copyright.** This agreement doesn't transfer ownership of your contributions. You can use and
   license them however you like elsewhere.
2. **Copyright license.** You give the maintainer a license to your contributions that's permanent, worldwide,
   non-exclusive, free of charge and can't be withdrawn. It lets the maintainer reproduce, change, make works based on,
   publicly display, publicly perform, sublicense and distribute your contributions and works based on them, as part
   of Quiver Launcher under the Business Source License, the MIT License, or other terms the maintainer chooses.
   Everyone else gets rights to your contributions through the license Quiver Launcher is published under.
3. **Patent license.** You give the maintainer and everyone who receives Quiver Launcher a patent license that's
   permanent, worldwide, non-exclusive and free of charge. It lets them make, have made, use, sell, offer for sale,
   import and otherwise transfer your contributions, alone or as part of Quiver Launcher. It covers only patent claims
   you own or control that your contribution necessarily infringes, alone or together with Quiver Launcher. If anyone
   who received Quiver Launcher sues claiming that Quiver Launcher or a contribution infringes a patent, the patent
   licenses you gave that person or company under this agreement end on the day they file.
4. **You have the right to contribute it.** Each contribution is your own original work, or you have permission to
   submit it under this agreement. If your employer or anyone else owns rights to your work, you've got their
   permission first. If someone else's work is in a contribution, such as code or assets under their own license, say
   so in your pull request, with where it came from and its license. Don't submit anything you don't have permission to
   contribute.
5. **No warranty or support.** Your contributions are provided "as is", as far as the law allows. You don't have to
   support them unless you choose to.
6. **Keeping it accurate.** If you learn that anything here stops being true for one of your contributions, you'll
   tell the maintainer.
7. **Credit.** Contributors are thanked in the release notes, or elsewhere in the project, where that's practical.

**Contributing for a company?** If your employer owns the work, someone who can sign for them may need to agree to a
corporate version of this agreement. Open an issue and we'll sort it out before your contribution is merged.

## Pull requests

- Work on a branch and open a pull request against `main`. Every change is reviewed before it's merged.
- Keep each pull request to one change, and describe what a player would see before and after it.
- Run `pnpm typecheck` and `pnpm test` before you push.
- On Windows, [`e2e/wsl.sh`](e2e/wsl.sh) runs the end-to-end tests as CI's Linux check does, in WSL's Ubuntu 24.04, so
  a failure there can be tried in minutes rather than a CI run. Its first lines say how to set it up and use it.

## How the code is laid out

| Path            | What it is                                                                 |
| --------------- | -------------------------------------------------------------------------- |
| `apps/launcher` | The Tauri app: React UI in `src`, Rust core in `src-tauri`                  |
| `packages/api`  | Typed client for the catalog API (`https://api.quiverlauncher.com/api/v1`) |
| `packages/ui`   | Catalog components and styles shared with quiverlauncher.com               |
| `e2e`           | User journeys run against the real app with WebDriver and a mock catalog   |

`announcement.json` is the banner Quiver Launcher 3 shows. 3.x reads it from `main` here, so it stays at the root even
though Quiver Launcher 4 takes its announcements from quiverlauncher.com.

## Building and testing Quiver

Needs Node 22, pnpm, Rust, [CMake](https://cmake.org/download/) on the path (the build compiles SDL, which
reads controllers), and on Linux the [Tauri system libraries](https://tauri.app/start/prerequisites/) and
`libudev-dev`.

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

On Linux with no display at all, each copy of the app gets a virtual display of its own, so
`pnpm --filter @quiver/e2e e2e:ci` can run two test files at once. CI doesn't run these (on its runners
they failed for timing, not the app): on Windows, `e2e/wsl.sh` runs them in WSL. The tests against the
real catalog (`pnpm --filter @quiver/e2e live`) run once a day in CI.

How every screen looks on Android phones, without one or a build of the app (Chromium, as Playwright installs it):

```sh
pnpm --filter @quiver/e2e exec playwright install chromium   # once
pnpm --filter @quiver/e2e phone
```

It fails when a screen scrolls sideways, cuts text off at its edge, squeezes text to a few letters a line, or lets
the player pinch to zoom, and saves each screen in `e2e/phone-screenshots/` (CI keeps them with each run).
`QUIVER_PHONE_API=https://api.quiverlauncher.com/api/v1` shows the real catalog instead of the mock one.

`QUIVER_API` points the app at another catalog and `QUIVER_DATA` at another data folder. A
`portable.txt` beside the executable keeps everything in a `data` folder next to it.

## Shared packages

quiverlauncher.com includes this repository as a git submodule and builds `packages/api` and `packages/ui` from
it, so the site shows apps with the same components as the launcher and checks its API against the same types.
Nothing is published to npm. A change here reaches the site when its submodule is moved to a newer commit.


The build-time and run-time variables for usage data are in [docs/usage-data.md](docs/usage-data.md).
