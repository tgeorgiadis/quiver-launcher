import type { Asset, Os } from "@quiverlauncher/api";

/** Checksums, signatures, symbols and source archives: never the app itself. */
const AUXILIARY =
  /\.(json|sha\d*|md5|sig|asc|minisig|pdb|txt)$|(^|[._-])(checksums?|sha\d*sums?|source|src|debug|symbols)([._-]|$)/i;
/** What the launcher can install, best first. */
const FORMATS = [
  /\.zip$/i,
  /\.7z$/i,
  /\.(tar\.gz|tgz)$/i,
  /\.(tar\.xz|txz)$/i,
  /\.(tar\.zst|tzst)$/i,
  /\.(tar\.bz2|tbz2?)$/i,
  /\.tar$/i,
  /\.rar$/i,
  /\.appimage$/i,
  /\.flatpak$/i,
  /\.exe$/i,
  /\.apk$/i,
  // A Windows installer last: its setup asks the player where to put it, and usually to allow it.
  /\.msi$/i,
  // No file extension: a bare program, or a file a host names without one ("Game-v1.2-Windows"),
  // which the install names from its contents. Dots in a version or ".x86_64" aren't extensions.
  /^(?!.*\.[a-z][a-z0-9]{0,4}$)/i,
];

/** A Windows installer package, whose own setup installs it. */
export const isMsi = (name: string) => /\.msi$/i.test(name);

/** An Android app, which Android installs. */
const isApk = (name: string) => /\.apk$/i.test(name);

/** The kind of file an app was installed from, for usage data, as Quiver Launcher 3 sends it. */
export function installFormat(name: string) {
  if (/\.flatpak$/i.test(name) || (/\.zip$/i.test(name) && /flatpak/i.test(name))) return "flatpak";
  if (isMsi(name)) return "msi";
  if (/\.appimage$/i.test(name)) return "appimage";
  if (isApk(name)) return "apk";
  return FORMATS.slice(0, 8).some((f) => f.test(name)) ? "archive" : "executable";
}

const formatRank = (name: string) => FORMATS.findIndex((f) => f.test(name));

/**
 * The release files that suit this computer, narrowed to the best match.
 * One file means install it; several mean the player picks; none means the
 * release has nothing for this platform. `kind` keeps an update to the kind
 * installed: a Windows installer ("msi"), or files Quiver unpacks ("files").
 */
export function bestAssets(assets: Asset[], os: Os, arch: string, filter?: string, kind?: "msi" | "files"): Asset[] {
  const wanted = filter?.trim().toLowerCase();
  const archRank = (a: Asset) =>
    a.architecture === arch ? 0 : a.architecture === "universal" || a.architecture === "unknown" ? 1 : arch === "x64" && a.architecture === "x86" ? 2 : -1;
  const usable = installable(assets, os).filter((a) => !kind || isMsi(a.filename) === (kind === "msi"));
  // Files the site couldn't place on a platform are tried only when none are for this one.
  // Linux runs Windows builds through Wine when there's no Linux build.
  const fallbacks: Os[] = os === "linux" ? ["unknown", "windows"] : ["unknown"];
  const forOs = [os, ...fallbacks].map((o) => usable.filter((a) => a.os === o)).find((list) => list.length) ?? [];
  const ranked = forOs
    .filter((a) => !wanted || a.filename.toLowerCase().includes(wanted))
    .filter((a) => archRank(a) >= 0)
    .map((a) => ({ a, rank: archRank(a) * FORMATS.length + formatRank(a.filename) }))
    .sort((x, y) => x.rank - y.rank);
  return ranked.filter((r) => r.rank === ranked[0].rank).map((r) => r.a);
}

/**
 * Every release file this computer could install, whatever system the site
 * says it's for: the player's pick when none is marked for this computer.
 */
export function installable(assets: Asset[], os: Os): Asset[] {
  return assets.filter(
    (a) =>
      !AUXILIARY.test(a.filename) &&
      formatRank(a.filename) >= 0 &&
      // Windows Installer only runs on Windows, not through Wine.
      (!isMsi(a.filename) || os === "windows") &&
      // Android only installs APKs, and only Android does.
      isApk(a.filename) === (os === "android"),
  );
}

/**
 * The file among these that the player picked before, by the filter `assetFilterFor` made of their pick: none
 * unless exactly one fits, when they pick again.
 */
export function pickedBefore(assets: Asset[], pick?: string): Asset | undefined {
  if (!pick) return undefined;
  const fits = assets.filter((a) => a.filename.toLowerCase().includes(pick));
  return fits.length === 1 ? fits[0] : undefined;
}

/**
 * A filter that keeps picking the file the player chose among several, in
 * later releases too: the longest part of its name, without version numbers,
 * that no other file's name has. Its whole name when no part is its own.
 */
export function assetFilterFor(chosen: string, files: string[]): string {
  const name = chosen.toLowerCase();
  const others = files.map((f) => f.toLowerCase()).filter((f) => f !== name);
  // Platform and format words name the computer, not the app: a filter with one would miss on another computer.
  const PLATFORM = /(?:^|[\s._-])(?:windows|win(?:32|64)?|linux|macos|mac|osx|x64|x86(?:[_-]64)?|amd64|arm64|aarch64|universal|portable)(?=$|[\s._-])/g;
  const trim = (part: string) => part.replace(/^[\s._-]+|[\s._-]+$/g, "");
  const parts = name
    .replace(/\.(?:zip|7z|tar(?:\.(?:gz|xz|zst|bz2))?|tgz|txz|tzst|tbz2?|rar|appimage|flatpak|exe|apk|msi)$/, "")
    .replace(PLATFORM, " ")
    .split(/v?\d+(?:[._-]\d+)+/)
    .map(trim);
  const words = parts.flatMap((p) => p.split(/[\s._-]+/));
  return (
    [...parts, ...words]
      .filter((p) => p.length >= 2)
      .sort((a, b) => b.length - a.length)
      .find((p) => !others.some((o) => o.includes(p))) ?? name
  );
}
