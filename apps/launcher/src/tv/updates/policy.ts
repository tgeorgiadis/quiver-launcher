/**
 * What pressing Play does when an update is waiting. Pure.
 * - No update (one the player skipped isn't one: the launcher's declined): play.
 * - The app's own setting in desktop mode is "Install automatically": update, then play.
 * - Otherwise TV mode's setting: Off plays, Auto updates then plays, Ask asks.
 * Pinned apps never have an update to offer (upstream's hasUpdate). X on a tile
 * updates whatever this says: a manual update ignores both settings.
 */
import type { TvApp } from "../bridge";
import type { UpdateBehavior } from "../settings";

export type PlayDecision = "launch" | "ask" | "update";

export function decide(app: Pick<TvApp, "state" | "updates">, behavior: UpdateBehavior): PlayDecision {
  if (app.state !== "updateAvailable") return "launch";
  if (app.updates === "auto") return "update";
  if (behavior === "off") return "launch";
  if (behavior === "auto") return "update";
  return "ask";
}

/**
 * The first lines of release notes worth showing in the prompt, as plain text: no headings' #, bullets or blank lines,
 * no pictures, HTML tags or bare links (GitHub's notes are full of them), and a link as its words.
 */
export function noteLines(notes: string | undefined, max = 6): string[] {
  return (notes ?? "")
    .replace(/<!--[\s\S]*?-->/g, "")
    .replace(/<[^>\n]*(?:\n[^>\n]*){0,3}>/g, "")
    .split(/\r?\n/)
    .map((l) =>
      l
        .replace(/!\[[^\]]*\]\([^)]*\)/g, "")
        .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
        .replace(/^\s*(#{1,6}\s+|[-*+]\s+|\d+\.\s+|>\s*)/, "")
        .replace(/\*\*|__|`/g, "")
        .replace(/https?:\/\/\S+/g, "")
        .trim(),
    )
    .filter((l) => /[\p{L}\p{N}]/u.test(l))
    .slice(0, max);
}
