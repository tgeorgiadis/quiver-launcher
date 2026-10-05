import type { Asset, Os } from "@quiverlauncher/api";

/** Checksums, signatures, symbols and source archives: never the app itself. */
const AUXILIARY =
  /\.(json|sha\d*|md5|sig|asc|minisig|pdb|txt)$|(^|[._-])(checksums?|sha\d*sums?|source|src|debug|symbols)([._-]|$)/i;
/** What the launcher can install, best first. */
const FORMATS = [/\.zip$/i, /\.7z$/i, /\.(tar\.gz|tgz)$/i, /\.appimage$/i, /\.exe$/i, /^[^.]+$/];

const formatRank = (name: string) => FORMATS.findIndex((f) => f.test(name));

/**
 * The release files that suit this computer, narrowed to the best match.
 * One file means install it; several mean the player picks; none means the
 * release has nothing for this platform.
 */
export function bestAssets(assets: Asset[], os: Os, arch: string, filter?: string): Asset[] {
  const wanted = filter?.trim().toLowerCase();
  const archRank = (a: Asset) =>
    a.architecture === arch ? 0 : a.architecture === "universal" || a.architecture === "unknown" ? 1 : arch === "x64" && a.architecture === "x86" ? 2 : -1;
  const usable = assets.filter((a) => !AUXILIARY.test(a.filename) && formatRank(a.filename) >= 0);
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
    .replace(/\.(?:zip|7z|tar\.gz|tgz|appimage|exe)$/, "")
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
