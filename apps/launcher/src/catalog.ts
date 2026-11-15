/** Catalog helpers shared by the launcher's pages. The catalog itself is @quiverlauncher/api's REST client. */
import type { Entry } from "@quiverlauncher/api";

type Way = Pick<Entry, "name" | "recommended" | "reportIssues" | "reportBroken" | "lastReleaseAt">;

/** How many players said how an app runs. */
export const reportCount = (w: Pick<Way, "recommended" | "reportIssues" | "reportBroken">) => w.recommended + w.reportIssues + w.reportBroken;

/**
 * How well players say an app runs, from 0 to 1: the share saying it runs well (issues count half), smoothed so one
 * report doesn't outrank many.
 */
export const feedbackScore = (w: Pick<Way, "recommended" | "reportIssues" | "reportBroken">) => (w.recommended + w.reportIssues / 2 + 1) / (reportCount(w) + 2);

/**
 * Best ways to play a game first, as on the website's game page, by `feedbackScore`. Ties go to more feedback, a newer
 * release, the name.
 */
export function byPlayerFeedback(a: Way, b: Way) {
  return (
    feedbackScore(b) - feedbackScore(a) ||
    reportCount(b) - reportCount(a) ||
    (b.lastReleaseAt ?? 0) - (a.lastReleaseAt ?? 0) ||
    a.name.localeCompare(b.name)
  );
}
