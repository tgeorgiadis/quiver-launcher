/**
 * What pressing Play does when an update is waiting (spec §9). Pure.
 * - No update, or this version skipped ("Skip this version"): play.
 * - The app's own setting in desktop mode is "Install automatically": update, then play.
 * - Otherwise Big Picture's setting: Off plays, Auto updates then plays, Ask asks.
 * Pinned apps never have an update to offer (upstream's hasUpdate). X on a tile
 * updates whatever this says: a manual update ignores both settings.
 */
import type { TvApp } from "../bridge";
import type { UpdateBehavior } from "../data/sidecar";

export type PlayDecision = "launch" | "ask" | "update";

/** "v1.2" and "1.2" are the same release. */
const bare = (version: string) => version.trim().replace(/^v/i, "");

export function decide(app: Pick<TvApp, "state" | "latestVersion" | "updates">, behavior: UpdateBehavior, skippedVersion?: string): PlayDecision {
  if (app.state !== "updateAvailable") return "launch";
  if (skippedVersion && app.latestVersion && bare(skippedVersion) === bare(app.latestVersion)) return "launch";
  if (app.updates === "auto") return "update";
  if (behavior === "off") return "launch";
  if (behavior === "auto") return "update";
  return "ask";
}

/** The first lines of release notes worth showing in the prompt: no headings' #, bullets or blank lines. */
export function noteLines(notes: string | undefined, max = 6): string[] {
  return (notes ?? "")
    .split(/\r?\n/)
    .map((l) => l.replace(/^\s*(#{1,6}\s+|[-*+]\s+|\d+\.\s+)/, "").replace(/\*\*|__|`/g, "").trim())
    .filter(Boolean)
    .slice(0, max);
}
