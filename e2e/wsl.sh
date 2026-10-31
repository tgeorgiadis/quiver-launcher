#!/usr/bin/env bash
# The Linux end-to-end tests on Windows, in WSL's Ubuntu 24.04 with WebKitGTK as on a Steam Deck. CI doesn't run
# them, so this is where they run. From the checkout, in PowerShell or a terminal:
#
#   wsl -d Ubuntu-24.04 -- e2e/wsl.sh setup                     once: Rust, Node, pnpm, cmake and tauri-driver, in ~
#   wsl -d Ubuntu-24.04 -- e2e/wsl.sh build                     copies the checkout to ~/quiver and builds the app
#   wsl -d Ubuntu-24.04 -- e2e/wsl.sh test journeys.test.js     copies it again and runs those files (none: all but live and phone)
#
# test only copies: after a change to the app itself, build again. First, once, in Ubuntu 24.04's own shell, the
# libraries CI installs:
#   sudo apt-get install -y libdbus-1-dev libwebkit2gtk-4.1-dev librsvg2-dev libayatana-appindicator3-dev libudev-dev webkit2gtk-driver xvfb build-essential curl git rsync
set -euo pipefail

checkout=$(cd "$(dirname "$0")/.." && pwd)
copy=${QUIVER_WSL_COPY:-$HOME/quiver}
# Linux's own tools ahead of Windows' (WSL adds Windows' PATH, with a pnpm of its own).
export PATH=$HOME/.cargo/bin:$HOME/.local/node/bin:$HOME/.local/cmake/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# A build through /mnt/c crawls: the tests run from a copy in Linux's own file system, which keeps its own packages and build.
copy_over() {
  mkdir -p "$copy"
  rsync -a --delete --exclude node_modules --exclude target --exclude .git --exclude e2e/phone-screenshots --exclude apps/launcher/dist "$checkout/" "$copy/"
}

case "${1:-}" in
  setup)
    command -v cargo >/dev/null || curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y --profile minimal
    if [ ! -x "$HOME/.local/node/bin/node" ]; then
      file=$(curl -fsSL https://nodejs.org/dist/latest-v22.x/ | grep -o 'node-v22[0-9.]*-linux-x64.tar.xz' | head -1)
      mkdir -p "$HOME/.local/node"
      curl -fsSL "https://nodejs.org/dist/latest-v22.x/$file" | tar -xJ -C "$HOME/.local/node" --strip-components=1
    fi
    corepack enable
    # The controllers' SDL builds with cmake, which GitHub's runners come with.
    if [ ! -x "$HOME/.local/cmake/bin/cmake" ]; then
      mkdir -p "$HOME/.local/cmake"
      curl -fsSL https://github.com/Kitware/CMake/releases/download/v3.31.6/cmake-3.31.6-linux-x86_64.tar.gz | tar -xz -C "$HOME/.local/cmake" --strip-components=1
    fi
    [ -x "$HOME/.cargo/bin/tauri-driver" ] || cargo install tauri-driver --locked
    ;;
  build)
    copy_over
    cd "$copy"
    CI=true pnpm install --frozen-lockfile
    pnpm --filter @quiver/e2e build
    ;;
  test)
    shift
    copy_over
    cd "$copy/e2e"
    [ $# -gt 0 ] || set -- $(ls *.test.js | grep -vE '^(live|phone)\.test\.js$')
    # Each app on a virtual display of its own (session.js), not WSLg's. WSLg mounts /tmp/.X11-unix read-only, where
    # Xvfb makes its socket: the run gets a writable one, in a mount namespace of its own. And a D-Bus session of its
    # own, as on CI.
    unset DISPLAY WAYLAND_DISPLAY
    exec unshare --user --map-root-user --mount sh -c 'mount -t tmpfs tmpfs /tmp/.X11-unix && chmod 1777 /tmp/.X11-unix && exec dbus-run-session -- node --test --test-concurrency=1 --test-force-exit "$@"' sh "$@"
    ;;
  *)
    sed -n '2,11p' "$0"
    exit 1
    ;;
esac
