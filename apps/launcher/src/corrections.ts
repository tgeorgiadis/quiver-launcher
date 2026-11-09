/**
 * A correction to an app's page, as the website's corrections.submit takes it, and the one being written, kept for
 * each account and app until it's sent or cancelled, or 30 days pass.
 */
import { OS_NAMES } from "@quiverlauncher/ui";

/** What a correction is about, as the website's correctionTopics.ts has them; the launcher's own part first. */
export const CORRECTION_TOPICS = ["install", "launch", "gameFiles", "description", "downloads", "setup", "games", "links", "other"] as const;
export type CorrectionTopic = (typeof CORRECTION_TOPICS)[number];
/** About the launcher's part, where how it ran belongs in Player feedback instead. */
export const LAUNCHER_TOPICS: readonly CorrectionTopic[] = ["install", "launch", "gameFiles"];
/** The longest correction the site takes. */
export const CORRECTION_MAX = 1000;

export type CorrectionDraft = { topic: CorrectionTopic | ""; body: string };
const KEEP_FOR = 30 * 24 * 60 * 60 * 1000;
const draftKey = (userId: string, entryId: string) => `quiver-correction-draft:${userId}:${entryId}`;

/** An unsent correction, kept for each account and app until it's sent or cancelled, or 30 days pass. */
export function readCorrectionDraft(userId: string, entryId: string): CorrectionDraft | null {
  try {
    const raw = localStorage.getItem(draftKey(userId, entryId));
    if (!raw) return null;
    const d = JSON.parse(raw) as Partial<CorrectionDraft> & { savedAt?: number };
    if (
      typeof d.savedAt !== "number" ||
      Date.now() - d.savedAt > KEEP_FOR ||
      typeof d.body !== "string" ||
      !(d.topic === "" || CORRECTION_TOPICS.includes(d.topic as CorrectionTopic))
    ) {
      localStorage.removeItem(draftKey(userId, entryId));
      return null;
    }
    return { topic: d.topic as CorrectionDraft["topic"], body: d.body };
  } catch {
    return null;
  }
}

export function saveCorrectionDraft(userId: string, entryId: string, draft: CorrectionDraft) {
  try {
    if (!draft.topic && !draft.body.trim()) return localStorage.removeItem(draftKey(userId, entryId));
    localStorage.setItem(draftKey(userId, entryId), JSON.stringify({ ...draft, savedAt: Date.now() }));
  } catch {
    // Storage is full or unavailable: the form still works, it just isn't kept.
  }
}

export function clearCorrectionDraft(userId: string, entryId: string) {
  try {
    localStorage.removeItem(draftKey(userId, entryId));
  } catch {
    // Nothing was kept.
  }
}

/** This launcher, as maintainers see it beside the correction: "4.0.0", "Steam Deck", the release installed. */
export function correctionClient(config: { version: string; os: string; steamDeck?: boolean }, installed?: string) {
  const os = config.steamDeck ? "Steam Deck" : (OS_NAMES as Record<string, string>)[config.os] ?? config.os;
  return { version: config.version.slice(0, 40), os: os.slice(0, 40), ...(installed ? { installed: installed.slice(0, 40) } : {}) };
}

