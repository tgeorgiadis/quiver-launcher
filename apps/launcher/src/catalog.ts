/** Catalog helpers shared by the launcher's pages. The catalog itself is @quiverlauncher/api's REST client. */
import type { Entry } from "@quiverlauncher/api";

type Way = Pick<Entry, "name" | "recommended" | "reportIssues" | "reportBroken" | "lastReleaseAt">;

/**
 * Best ways to play a game first, as on the website's game page: the share of
 * players saying it runs well (issues count half), smoothed so one report
 * doesn't outrank many. Ties go to more feedback, a newer release, the name.
 */
export function byPlayerFeedback(a: Way, b: Way) {
  const total = (w: Way) => w.recommended + w.reportIssues + w.reportBroken;
  const score = (w: Way) => (w.recommended + w.reportIssues / 2 + 1) / (total(w) + 2);
  return (
    score(b) - score(a) ||
    total(b) - total(a) ||
    (b.lastReleaseAt ?? 0) - (a.lastReleaseAt ?? 0) ||
    a.name.localeCompare(b.name)
  );
}
