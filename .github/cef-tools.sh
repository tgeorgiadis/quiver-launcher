#!/usr/bin/env bash
# What a CI job needs to build the launcher for Linux on Chromium (Tauri's CEF runtime, apps/launcher/CEF-SPIKE.md)
# with apps/launcher/scripts/tauri-cef.sh, on Ubuntu 22.04. Run it after the System libraries step; the job caches
# ~/.cache/tauri-cef-cli and ~/.cache/cef, keyed on REV and Cargo.cef.lock.
set -euo pipefail

# The CEF build of the Tauri CLI, from Readest's fork of feat/cef: its AppImage bundling deploys the NSS and audio
# libraries Chromium loads at run time.
REV=6beeb209b5514f597a26f48b7a5b9fe4c59b5f12
cli=$HOME/.cache/tauri-cef-cli

# patchelf for the AppImage bundling; NSS, ALSA and libxkbcommon-x11 for the bundler to copy in.
"$(dirname "$0")/apt-install.sh" patchelf libnss3 libasound2 libxkbcommon-x11-0

# Ubuntu keeps NSS's modules in nss/, where the bundler doesn't look. Without them in the AppImage, Chromium loads the
# player's own, which on SteamOS need a newer glibc than the bundle's, and aborts.
# Some are links into nss/ there already (libfreebl3, libfreeblpriv3 on GitHub's runner), which the bundler follows
# into the AppImage's lib/nss/, where libsoftokn3 doesn't look for them: a real copy goes in their place.
for f in /usr/lib/x86_64-linux-gnu/nss/*.so; do
  t=/usr/lib/x86_64-linux-gnu/${f##*/}
  [ -L "$t" ] && sudo rm "$t"
  [ -e "$t" ] || sudo cp "$f" "$t"
done

[ -x "$cli/bin/cargo-tauri" ] || cargo install --git https://github.com/readest/tauri --rev "$REV" tauri-cli --locked --root "$cli"
echo "TAURI_CEF_CLI=$cli/bin/cargo-tauri" >> "$GITHUB_ENV"
# cef-dll-sys and the CLI take the CEF distribution from here, downloading it only when it's missing.
echo "CEF_PATH=$HOME/.cache/cef" >> "$GITHUB_ENV"
