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

/** The key in the player's copy folders for folders that hold copies for every console. */
export const EVERY_CONSOLE = "*";

/**
 * The folders the player keeps this game's copies in, to look in first: those for its consoles, then those for every
 * console, each once.
 */
export function copyFoldersFor(folders: Record<string, string[]> | undefined, consoles: string[] | undefined): string[] {
  const keys = [...(consoles ?? []), EVERY_CONSOLE];
  return [...new Set(keys.flatMap((k) => folders?.[k] ?? []))];
}

/** The player's copy folders with `folder` added to a console's (or every console's), or taken away from it. */
export function withCopyFolder(folders: Record<string, string[]> | undefined, console: string, folder: string, add: boolean): Record<string, string[]> {
  const out = { ...folders };
  const list = (out[console] ?? []).filter((f) => f !== folder);
  if (add) list.push(folder);
  if (list.length) out[console] = list;
  else delete out[console];
  return out;
}

/** Words in a game's or file's name that say nothing about which game it is. */
const NOISE = new Set(["the", "of", "a", "an", "and", "rom", "iso", "disc", "disk", "image", "file", "files", "game", "copy", "usa", "us", "eu", "europe", "japan", "jp", "ntsc", "pal", "rev", "version", "v1", "en", "fr", "de"]);

/** A name's words that tell games apart: lowercase, without its extension, punctuation or the words above. */
export function nameWords(name: string): string[] {
  const base = name.replace(/\.[a-z0-9]{1,5}$/i, "");
  return base
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .split(/[^a-z0-9]+/)
    .filter((w) => w && !NOISE.has(w));
}

/**
 * The copies found, split into the ones whose file names look like this game (most of its title in them, by the
 * length of its words, best first) and the rest, as found. `titles` are the game's names: the original games' titles and the file's label.
 */
export function rankCopies<T extends { path: string }>(found: T[], titles: string[]): { likely: T[]; others: T[] } {
  const wanted = titles.map(nameWords).filter((w) => w.length > 0);
  if (!wanted.length) return { likely: [], others: found };
  const score = (path: string) => {
    const words = new Set(nameWords(path.split(/[\\/]/).pop() ?? path));
    // Longer words weigh more: "goldeneye" says which game far more than a "007" another game shares.
    const weight = (list: string[]) => list.reduce((sum, w) => sum + w.length, 0);
    return Math.max(...wanted.map((title) => weight(title.filter((w) => words.has(w))) / weight(title)));
  };
  const scored = found.map((f, i) => ({ f, i, s: score(f.path) }));
  const likely = scored.filter((c) => c.s >= 0.5).sort((a, b) => b.s - a.s || a.i - b.i);
  return { likely: likely.map((c) => c.f), others: scored.filter((c) => c.s < 0.5).map((c) => c.f) };
}

/** A size for the player: "32 MB", "7.3 GB". */
export function sizeText(bytes: number): string {
  if (bytes >= 1 << 30) return `${(bytes / (1 << 30)).toFixed(1)} GB`;
  if (bytes >= 1 << 20) return `${Math.round(bytes / (1 << 20))} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
