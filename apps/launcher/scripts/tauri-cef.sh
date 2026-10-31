#!/bin/sh
# scripts/tauri-cef.sh <dev|build> [tauri args]: the launcher on Chromium (Tauri's CEF runtime) instead of WebKitGTK,
# on Linux only. It makes the released AppImage and the Flatpak's program (release.yml); every other build is the
# plain `tauri` command, as is the WebKitGTK Linux build the e2e tests drive (tauri-driver can't drive Chromium).
#
# It runs the CEF build of the Tauri CLI (from Readest's fork of feat/cef, whose AppImage bundling deploys the NSS
# and audio libraries Chromium loads at run time): `cargo install --git https://github.com/readest/tauri
# --rev <REV in .github/cef-tools.sh> tauri-cli --locked`, or TAURI_CEF_CLI=<its cargo-tauri>. On top of
# the usual command line it turns on the app's `cef` feature, which the CLI takes to bundle the CEF distribution,
# and hands cargo src-tauri/.cargo/cef.toml, which takes Tauri from feat/cef.
#
# Cargo keeps one Cargo.lock and that graph is a different one, so for the length of the command Cargo.cef.lock
# stands in for Cargo.lock, and what it resolved goes back to Cargo.cef.lock afterwards. The usual one waits in
# Cargo.lock.wry meanwhile; if a run is killed before it's put back, `mv Cargo.lock.wry Cargo.lock` in src-tauri.
set -e
cd "$(dirname "$0")/.."
tauri=src-tauri
[ -e $tauri/Cargo.lock.wry ] && { echo "$tauri/Cargo.lock.wry exists: another CEF build is running, or one was killed before it put Cargo.lock back" >&2; exit 1; }
cp $tauri/Cargo.lock $tauri/Cargo.lock.wry
[ -e $tauri/Cargo.cef.lock ] && cp $tauri/Cargo.cef.lock $tauri/Cargo.lock
restore() { cp $tauri/Cargo.lock $tauri/Cargo.cef.lock; mv $tauri/Cargo.lock.wry $tauri/Cargo.lock; }
trap restore EXIT
trap 'exit 1' INT TERM HUP
command=$1; shift
# feat/cef's tauri crate is 2.11, behind the @tauri-apps/api the page uses (2.12), which the CLI otherwise refuses.
# tauri.cef.conf.json adds to the AppImage what Chromium loads at run time and the bundler misses: NSS's modules,
# which Ubuntu keeps in <triple>/nss/ (flatten them into <triple>/ first, `cp /usr/lib/x86_64-linux-gnu/nss/*.so
# /usr/lib/x86_64-linux-gnu/`; without them Chromium takes SteamOS's, which need a newer glibc than the bundle's,
# and aborts), and libxkbcommon-x11, which winit loads.
${TAURI_CEF_CLI:-cargo tauri} "$command" --features cef --ignore-version-mismatches --config $tauri/tauri.cef.conf.json "$@" \
  -- --config "$PWD/$tauri/.cargo/cef.toml"
