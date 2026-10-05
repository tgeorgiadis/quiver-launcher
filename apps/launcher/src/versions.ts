/**
 * Change version's list: every release on GitHub or GitLab and every one
 * quiverlauncher.com knows, each verified, unverified or blocked.
 */
import type { Release, ReleaseScan, UnverifiedRelease } from "@quiverlauncher/api";
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
};
export const bare = (v: string) => v.trim().replace(/^v/i, "");
/** "v1.2", however the release names it. */
export const versionLabel = (version = "") => (/^v/i.test(version) ? version : `v${version}`);
const NOT_SEEN = "Quiver hasn't seen this release yet.";
const fromSite = (u: UnverifiedRelease): Release => ({
  id: u.releaseId,
  version: u.version,
  releasedAt: u.releasedAt,
  prerelease: u.prerelease,
  assets: u.assets,
  ...(u.installationOverride ? { installationOverride: u.installationOverride } : {}),
});

/**
 * Every release on GitHub or GitLab and every one the site knows, each
 * marked verified, unverified or blocked. A site that doesn't list
 * unverified releases yet (`unverified` null) leaves every unpublished one
 * unverified, as GitHub or GitLab has it.
 */
export function versionList(published: Release[], unverified: UnverifiedRelease[] | null, upstream: Release[]): Version[] {
  const verified = new Map(published.map((r) => [bare(r.version), r]));
  const known = new Map((unverified ?? []).map((u) => [bare(u.version), u]));
  const list: Version[] = upstream.map((r) => {
    const key = bare(r.version);
    const mine = verified.get(key);
    const theirs = known.get(key);
    verified.delete(key);
    known.delete(key);
    if (mine) return { release: mine, state: "verified", reasons: [], known: true };
    if (theirs) return { release: fromSite(theirs), state: theirs.state, reasons: theirs.reasons, checkEndsAt: theirs.checkEndsAt, scan: theirs.scan, known: true };
    return { release: r, state: "unverified", reasons: unverified ? [NOT_SEEN] : [], known: false };
  });
  list.push(...[...verified.values()].map((release): Version => ({ release, state: "verified", reasons: [], known: true })));
  list.push(
    ...[...known.values()].map(
      (u): Version => ({ release: fromSite(u), state: u.state, reasons: u.reasons, checkEndsAt: u.checkEndsAt, scan: u.scan, known: true }),
    ),
  );
  return list.sort((a, b) => b.release.releasedAt - a.release.releasedAt);
}

