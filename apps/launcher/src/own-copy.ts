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

/**
 * A name's words that tell games apart: lowercase, without its extension, punctuation or the words above, and without
 * the tags in brackets that ROM sets add ("(USA)", "(Rev 1)", "(SGB Enhanced)"), which say which dump, not which game.
 */
export function nameWords(name: string): string[] {
  const base = name.replace(/\.[a-z0-9]{1,5}$/i, "").replace(/\([^)]*\)|\[[^\]]*\]/g, " ");
  return base
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .split(/[^a-z0-9]+/)
    .filter((w) => w && !NOISE.has(w));
}

/** Tags that mark a dump as not the game as sold: put after the one that is. */
const UNFINISHED = /\((?:[^)]*\b)?(?:beta|proto|prototype|demo|sample|kiosk|debug|hack|pirate|unl|aftermarket)\b[^)]*\)/i;

/** Longer words weigh more: "goldeneye" says which game far more than a "007" another game shares. */
const weight = (list: string[]) => list.reduce((sum, w) => sum + w.length, 0);

/**
 * How well a file's name fits a title, 0 to 1: how much of the title is in it, and how much of it is the title, so
 * "Duke Nukem - Zero Hour" doesn't pass for "F-Zero X" on one word, and "F-Zero X" beats a longer name that also has it.
 */
function fit(file: string[], title: string[]): { covers: number; score: number } {
  const words = new Set(file);
  const shared = title.filter((w) => words.has(w));
  if (!shared.length) return { covers: 0, score: 0 };
  const covers = weight(shared) / weight(title);
  const own = weight(shared) / Math.max(1, weight([...words]));
  return { covers, score: (2 * covers * own) / (covers + own) };
}

/**
 * The copies found, split into the ones whose file names look like this game, best first, and the rest, nearest
 * first. `titles` are the game's names (the original games' titles and the app's), and `label` the copy's own, which
 * comes first where it names a game of its own: an Oracle of Seasons ROM before the Oracle of Ages one the app also
 * plays. Dumps marked Beta, Proto, Demo and the like come after the one as sold.
 */
export function rankCopies<T extends { path: string }>(found: T[], titles: string[], label = ""): { likely: T[]; others: T[] } {
  const wanted = [...titles, label].map(nameWords).filter((w) => w.length > 0);
  if (!wanted.length) return { likely: [], others: found };
  const own = nameWords(label);
  const scored = found.map((f, i) => {
    const name = f.path.split(/[\\/]/).pop() ?? f.path;
    const words = nameWords(name);
    const fits = wanted.map((title) => fit(words, title));
    const best = fits.reduce((a, b) => (b.score > a.score ? b : a));
    // Most of a title in the name, and not just one word of it among many others.
    const likely = fits.some((x) => x.covers >= 0.6 && (x.covers === 1 || x.score >= 0.4));
    const byLabel = own.length ? fit(words, own).score : 0;
    return { f, i, likely, s: best.score * (UNFINISHED.test(name) ? 0.7 : 1), byLabel };
  });
  const order = (a: (typeof scored)[number], b: (typeof scored)[number]) => b.byLabel - a.byLabel || b.s - a.s || a.i - b.i;
  return {
    likely: scored.filter((c) => c.likely).sort(order).map((c) => c.f),
    others: scored.filter((c) => !c.likely).sort((a, b) => b.s - a.s || a.i - b.i).map((c) => c.f),
  };
}

/** A size for the player: "32 MB", "7.3 GB". */
export function sizeText(bytes: number): string {
  if (bytes >= 1 << 30) return `${(bytes / (1 << 30)).toFixed(1)} GB`;
  if (bytes >= 1 << 20) return `${Math.round(bytes / (1 << 20))} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}
