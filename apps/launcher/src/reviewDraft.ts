/**
 * Unsent player feedback, kept for each account and app so it survives
 * switching tabs, leaving the app page or restarting the launcher. It's
 * removed once the feedback is shared or the form is cancelled, and
 * forgotten after 30 days.
 */
import { OS_NAMES } from "@quiverlauncher/ui";
import type { Feedback } from "@quiverlauncher/api";

export type ReviewDraft = { result: Feedback["result"]; platform: string; releaseId: string; body: string };
/** The longest note the site accepts (reviews:save on the website). */
export const NOTE_LIMIT = 500;
const KEEP_FOR = 30 * 24 * 60 * 60 * 1000;
const RESULTS: readonly string[] = ["runs", "issues", "broken"];
const key = (userId: string, entryId: string) => `quiver-review-draft:${userId}:${entryId}`;

export function readDraft(userId: string, entryId: string): ReviewDraft | null {
  try {
    const raw = localStorage.getItem(key(userId, entryId));
    if (!raw) return null;
    const d = JSON.parse(raw) as Partial<ReviewDraft> & { savedAt?: number };
    if (
      typeof d.savedAt !== "number" ||
      Date.now() - d.savedAt > KEEP_FOR ||
      !RESULTS.includes(d.result as string) ||
      typeof d.platform !== "string" ||
      (d.platform !== "" && !(d.platform in OS_NAMES)) ||
      typeof d.releaseId !== "string" ||
      typeof d.body !== "string"
    ) {
      localStorage.removeItem(key(userId, entryId));
      return null;
    }
    return { result: d.result as Feedback["result"], platform: d.platform, releaseId: d.releaseId, body: d.body };
  } catch {
    return null;
  }
}

export function saveDraft(userId: string, entryId: string, draft: ReviewDraft) {
  try {
    localStorage.setItem(key(userId, entryId), JSON.stringify({ ...draft, savedAt: Date.now() }));
  } catch {
    // Storage is full or unavailable: the form still works, it just isn't kept.
  }
}

export function clearDraft(userId: string, entryId: string) {
  try {
    localStorage.removeItem(key(userId, entryId));
  } catch {
    // Nothing was kept.
  }
}
