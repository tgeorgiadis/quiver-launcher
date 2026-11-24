# Working on Quiver Launcher

Rules for anyone changing this repository, people and coding agents alike. `CLAUDE.md` points here, so every agent
reads the same file. [CONTRIBUTING.md](CONTRIBUTING.md) has the layout of the code, how to build it, and the
Contributor License Agreement.

## Before you write code

- One issue, one branch, one pull request. Don't fold a second fix into it.
- For a bug, find the cause first and say it in the pull request. A fix for the symptom alone isn't one.
- Every string a player can read follows the wording standard, and every screen the design standard. Both live in the
  website's repository (`docs/standards/copy.md` and `docs/standards/design.md` in `QuiverLauncher/quiverlauncher.com`).
  The short version: "app" not "project", "Get" on the button, "sign in" not "log in", sentence case, no "please",
  errors as "Couldn't <do the thing>." then one next step.
- Every string goes through Lingui (`t`, `<Trans>`, `plural`), as a whole sentence. After adding or changing one, run
  `pnpm --filter @quiverlauncher/i18n extract`. CI fails when the catalogs don't list every string.
- `packages/api` and `packages/ui` are shared with quiverlauncher.com, which builds them from this repository as a
  submodule. A change there changes the website too: check it there, and move the website's submodule pointer in a
  pull request on its repository once this one is merged.

## Checks before you push

```sh
pnpm typecheck && pnpm test
pnpm --filter @quiverlauncher/i18n check
cargo test --manifest-path apps/launcher/src-tauri/Cargo.toml
```

A change to a screen also runs `pnpm --filter @quiver/e2e phone` (every screen at phone sizes) and the tests in
`e2e/web/` that cover it. A new behaviour gets a test beside the code it changes. Never skip, disable or loosen a test
to get a green run.

CI doesn't compile for Android or macOS. Code that calls a desktop-only window or system API needs its
`#[cfg(...)]`, or the release build breaks after the merge.

## Try it in the real app

Tests passing isn't enough for a change a player can see. Before asking for review:

- Run the app (`pnpm dev`, or a build) and do what the player would do, on the system the change is for. A Windows
  behaviour is tried on Windows.
- Say in the pull request what you tried and what you saw, with a screenshot for anything visual, in dark and light
  where colours changed.
- If something couldn't be tried, say what and why. Don't hand the reviewer a list of manual checks instead.

## Pull requests and commits

- The title says what a player sees change, in words they'd use. Name the issue it closes (`Fixes #123`).
- The description has: what was wrong or missing, the cause, what changes, and what you tried in the real app.
- Never name another launcher, or compare Quiver with one, in a commit, pull request, issue, code comment or release
  note. Describe what Quiver does.
- Don't call the old wording or the old behaviour bad. Say what it does now.
- Comments say why the code is the way it is, for the next person and agent. They aren't a history of the change.
- No secrets, tokens, or a player's paths, user names or account details anywhere in the repository or a pull request.

## What reaches players first

- When the launcher needs something new from quiverlauncher.com (an endpoint, a field), the website's change is
  merged and deployed first. Launchers already installed keep working either way: the launcher ignores fields it
  doesn't know, and the website keeps answering older launchers.
- When the website would show something older launchers can't handle, the launcher's change is released first.
- Say in both pull requests which goes first.

## Usage data and error reports

Anything the launcher sends is listed in [docs/usage-data.md](docs/usage-data.md). A new event or a new thing in an
error report is added there in the same pull request, goes through `scrub` (`apps/launcher/src/telemetry.ts`), and
never carries a path, a file name, a user name or an account.

## Releases

A release is a pull request named "Release 4.0.0-beta.N" that bumps the version in `apps/launcher/package.json`,
`src-tauri/Cargo.toml`, `src-tauri/tauri.conf.json`, `Cargo.lock` and `Cargo.cef.lock` (only the `quiver-launcher`
entry in the lock files). After it merges, the Release workflow builds a draft release from `main`.

The notes are written by hand and pasted into the draft, in this order:

1. The download links, in full, with the paragraphs about the Windows zip and the Flatpak, as in earlier betas.
2. A line (`---`), then `# Quiver Launcher 4.0.0-beta.N` and one sentence about the release.
3. A section for each new feature, each point starting with a few words in bold.
4. Fixes, Known limits, Testing notes, and Thanks with a link to each outside contributor.
5. A line (`---`), then GitHub's generated "What's Changed" list, kept as it is.

Publishing a release is the maintainer's step.
