/**
 * Change version's list: every release on GitHub or GitLab and every one
 * quiverlauncher.com knows, each verified, unverified or blocked.
 */
import { t } from "@lingui/core/macro";
import type { Asset, Feedback, HistoryRelease, Release, ReleaseScan, UnverifiedRelease, Withdrawn } from "@quiverlauncher/api";
import type { ReleaseState } from "@quiverlauncher/ui";

/**
 * A release in Change version: verified (published on the site), or not.
 * An unverified one the site knows installs its files against the
 * checksums the site pinned; one it hasn't seen yet comes straight from
 * GitHub or GitLab. A blocked one installs its pinned files only when the
 * player insists.
 */
export type Version = {
  release: Release;
  state: ReleaseState;
  /** Why it isn't verified, or why it's blocked, worded for players. */
  reasons: string[];
  checkEndsAt?: number;
  scan?: ReleaseScan;
  /** The site knows it, so its files are the ones Quiver saw. */
  known: boolean;
  /** Taken back since it came out (withdrawnAsBlocked): blocked, with why. */
  withdrawn?: Withdrawn;
};
export const bare = (v: string) => v.trim().replace(/^v/i, "");
/** "v1.2", however the release names it. */
export const versionLabel = (version = "") => (/^v/i.test(version) ? version : `v${version}`);

/**
 * Whether `a` came out after `b`: by when each came out, else by version number, as for an install from Quiver
 * Launcher 3, which has no release date.
 */
export const cameAfter = (a: { version: string; releasedAt?: number }, b: { version: string; releasedAt?: number }) =>
  a.releasedAt && b.releasedAt ? a.releasedAt > b.releasedAt : bare(a.version).localeCompare(bare(b.version), undefined, { numeric: true }) > 0;

/**
 * The button that takes an app off a withdrawn release, worded for where it goes: back to an older release is a
 * downgrade, said so, so a player knows they're going back.
 */
export function wayOffLabel(install: { version: string; releasedAt?: number; updates?: string }, to: { version: string; releasedAt?: number }) {
  const back = cameAfter(install, to);
  const version = versionLabel(to.version);
  if (install.updates === "pinned") return back ? t`Unpin and downgrade to ${version}` : t`Unpin and install ${version}`;
  return back ? t`Downgrade to ${version}` : t`Install ${version}`;
}
/** A release from the site's list of unverified ones, or its history, as one to install. */
export const fromSite = (u: UnverifiedRelease | HistoryRelease): Release => ({
  id: u.releaseId,
  version: u.version,
  releasedAt: u.releasedAt,
  prerelease: u.prerelease,
  assets: u.assets,
  ...(u.installationOverride ? { installationOverride: u.installationOverride } : {}),
});

/**
 * The site's history of the repository without its verified releases, which
 * the catalog's own list gives with the ids a review names.
 */
export const unverifiedIn = (history: HistoryRelease[]) =>
  history.filter((h): h is HistoryRelease & UnverifiedRelease => h.state !== "verified");

/**
 * Every release on GitHub or GitLab and every one the site knows, each
 * marked verified, unverified or blocked. A site that doesn't list
 * unverified releases yet (`unverified` null) leaves every unpublished one
 * unverified, as GitHub or GitLab has it.
 */
export function versionList(published: Release[], unverified: UnverifiedRelease[] | null, upstream: Release[]): Version[] {
  const verified = new Map(published.map((r) => [bare(r.version), r]));
  const known = new Map((unverified ?? []).map((u) => [bare(u.version), u]));
  const notSeen = t`Quiver hasn't seen this release yet.`;
  const list: Version[] = upstream.map((r) => {
    const key = bare(r.version);
    const mine = verified.get(key);
    const theirs = known.get(key);
    verified.delete(key);
    known.delete(key);
    if (mine) return { release: mine, state: "verified", reasons: [], known: true };
    if (theirs) return { release: fromSite(theirs), state: theirs.state, reasons: theirs.reasons, checkEndsAt: theirs.checkEndsAt, scan: theirs.scan, known: true };
    return { release: r, state: "unverified", reasons: unverified ? [notSeen] : [], known: false };
  });
  list.push(...[...verified.values()].map((release): Version => ({ release, state: "verified", reasons: [], known: true })));
  list.push(
    ...[...known.values()].map(
      (u): Version => ({ release: fromSite(u), state: u.state, reasons: u.reasons, checkEndsAt: u.checkEndsAt, scan: u.scan, known: true }),
    ),
  );
  return list.sort((a, b) => b.release.releasedAt - a.release.releasedAt);
}

/**
 * A withdrawn version is blocked, whatever else the site says of it: never an
 * update, installed only when the player insists, and why is its withdrawal.
 */
export function withdrawnAsBlocked<T extends { state: ReleaseState; reasons: string[]; withdrawn?: Withdrawn }>(items: T[], versionOf: (item: T) => string, withdrawn: Withdrawn[] = []): T[] {
  const pulled = new Map(withdrawn.map((w) => [bare(w.version), w]));
  return items.map((item) => {
    const w = pulled.get(bare(versionOf(item)));
    return w ? { ...item, state: "blocked", reasons: [w.reason], withdrawn: w } : item;
  });
}

/**
 * What an app with no verified release offers in its place, after a warning,
 * as Quiver Launcher 3 does: its newest release that isn't blocked and still
 * has files. Undefined when there's none.
 */
export const newestUnblocked = <T extends { state: ReleaseState; assets: Asset[] }>(releases: T[]) =>
  releases.find((r) => r.state !== "blocked" && r.assets.length > 0);

/**
 * Why a file of a verified release asks the player before it installs: it
 * `changed` upstream since its release was verified, and isn't verified yet
 * itself (`unverifiedChecksum`); or Quiver checked the release's other files
 * but has no checksum for this one (`unchecked`). Undefined for a verified
 * file, and for a release with no checksums at all (GitLab), which installs
 * as it always has.
 */
export function unverifiedFile(asset: Asset, release: Pick<Release, "assets">): "changed" | "unchecked" | undefined {
  if (asset.checksum) return undefined;
  if (asset.unverifiedChecksum) return "changed";
  return release.assets.some((a) => a.checksum || a.unverifiedChecksum) ? "unchecked" : undefined;
}

/** Two `sha256:<hex>` checksums of the same file, however their hex is cased. */
export const sameChecksum = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/**
 * Whether the file installed before Quiver verified it (its checksum) is one
 * its release is verified with now: `verified` when it is, or when there's
 * nothing to tell by (no checksum kept, or none listed, as on GitLab);
 * `waiting` when it's a file of the release still in its own wait, or the
 * release has a file Quiver has no checksum for, which it may be; `differs`
 * when it's none of them.
 */
export function installedFile(checksum: string | undefined, release: Pick<Release, "assets">): "verified" | "waiting" | "differs" {
  const { assets } = release;
  const ours = (sum?: string) => Boolean(sum && checksum && sameChecksum(sum, checksum));
  if (!checksum || !assets.some((a) => a.checksum || a.unverifiedChecksum) || assets.some((a) => ours(a.checksum))) return "verified";
  return assets.some((a) => ours(a.unverifiedChecksum) || (!a.checksum && !a.unverifiedChecksum)) ? "waiting" : "differs";
}

/**
 * What a broken download offers instead: the newest other release of the
 * site's verified ones, not the one that failed or the one installed, that
 * has a file for this computer (`fits`).
 */
export const insteadOf = (releases: Release[], failed: string, installed: string | undefined, fits: (release: Release) => boolean) =>
  releases.find((r) => bare(r.version) !== bare(failed) && (installed === undefined || bare(r.version) !== bare(installed)) && fits(r));

/** How many players said a version runs well, has issues, or is broken. */
export type Said = { runs: number; issues: number; broken: number };

/** Feedback counted by the version it tested; feedback naming none isn't counted. */
export function saidOf(feedback: Pick<Feedback, "version" | "result">[]): Map<string, Said> {
  const counts = new Map<string, Said>();
  for (const f of feedback) {
    if (!f.version) continue;
    const key = bare(f.version);
    const c = counts.get(key) ?? { runs: 0, issues: 0, broken: 0 };
    c[f.result] += 1;
    counts.set(key, c);
  }
  return counts;
}
