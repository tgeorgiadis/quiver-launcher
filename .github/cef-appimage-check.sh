#!/usr/bin/env bash
# `.github/cef-appimage-check.sh <AppImage>`: fails unless the CEF AppImage holds what Chromium loads. The bundler
# skips what it can't find without failing: Chromium's own library, NSS's modules (without which it aborts on
# SteamOS) and libxkbcommon-x11 (which winit loads).
set -euo pipefail
image=$(realpath "$1")
cd "$(mktemp -d)"
"$image" --appimage-extract > /dev/null
# uruntime's AppImages unpack to AppDir, others to squashfs-root.
root=AppDir; [ -d $root ] || root=squashfs-root
missing=0
for f in bin/libcef.so lib/libnss3.so lib/libsoftokn3.so lib/libfreeblpriv3.so lib/libnssckbi.so lib/libxkbcommon-x11.so.0; do
  test -e "$root/$f" || { echo "::error::$f isn't in the AppImage"; missing=1; }
done
rm -rf "$PWD"
exit $missing
