/**
 * The player's own copy of an app's game (own_copy.rs): which of the catalog's setup files apply here, which still
 * need the player's copy, and what Play tells the game about the ones it gets by flag or environment variable.
 */
import type { Entry, SetupFile } from "@quiverlauncher/api";
import type { LaunchCopy } from "./native";
import type { Install } from "./store";

/** Whether the launcher gets the copy to the app itself: puts it somewhere, or tells the app where it is. */
export const delivered = (file: SetupFile) => file.destination !== undefined || file.flag !== undefined || file.env !== undefined;

/**
 * The catalog's setup files for this app on this computer. None on Android and iOS, which keep an app's files to
 * itself; on a Mac an app is started through `open`, which passes no environment, so a copy it gets that way is left
 * to the app's own setup.
 */
export function setupFiles(entry: Pick<Entry, "launcher"> | undefined, os: string): SetupFile[] {
  if (os === "android" || os === "ios") return [];
  const files = entry?.launcher?.setupFiles ?? [];
  return files.filter((f) => f && f.id && f.label && Array.isArray(f.accepted) && !(os === "macos" && f.env && !f.destination && !f.flag));
}

/** The setup files Play waits for: needed, got to the app by the launcher, and without the player's copy yet. */
export function copiesNeeded(entry: Pick<Entry, "launcher"> | undefined, install: Pick<Install, "copies"> | undefined, os: string): SetupFile[] {
  return setupFiles(entry, os).filter((f) => f.required !== false && delivered(f) && !install?.copies?.[f.id]);
}

/** What Play passes the game: each copy it's told about on the command line or in its environment, where it is now. */
export function launchCopies(entry: Pick<Entry, "launcher"> | undefined, install: Pick<Install, "copies"> | undefined): LaunchCopy[] {
  const out: LaunchCopy[] = [];
  for (const file of entry?.launcher?.setupFiles ?? []) {
    const copy = install?.copies?.[file.id];
    if (!copy || (!file.flag && !file.env)) continue;
    out.push({ ...(file.flag ? { flag: file.flag } : {}), ...(file.env ? { env: file.env } : {}), path: copy.placed ?? copy.path });
  }
  return out;
}

/** A size for the player: "32 MB", "7.3 GB". */
export function sizeText(bytes: number): string {
  if (bytes >= 1 << 30) return `${(bytes / (1 << 30)).toFixed(1)} GB`;
  if (bytes >= 1 << 20) return `${Math.round(bytes / (1 << 20))} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
