import type { Asset, Os } from "@quiver/api";

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
  const ranked = assets
    .filter((a) => a.os === os && !AUXILIARY.test(a.filename) && formatRank(a.filename) >= 0)
    .filter((a) => !wanted || a.filename.toLowerCase().includes(wanted))
    .filter((a) => archRank(a) >= 0)
    .map((a) => ({ a, rank: archRank(a) * FORMATS.length + formatRank(a.filename) }))
    .sort((x, y) => x.rank - y.rank);
  return ranked.filter((r) => r.rank === ranked[0].rank).map((r) => r.a);
}
