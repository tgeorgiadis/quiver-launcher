/**
 * What Browse showed, kept while the launcher runs: going back to Browse (from the library, or Back from a page) shows
 * it again at once, as it was, rather than asking the catalog again and drawing it a piece at a time. Each answer is
 * kept for FRESH_FOR; an older one is asked for again as Browse opens. Filled ahead from the library too
 * (prefetchBrowse), so the first visit to Browse is quick as well.
 *
 * The front page's shelves are kept on disk too (SHELVES_KEY), for a week (SHOWN_FOR): Browse's first visit after the
 * launcher starts shows the last ones at once, and asks for them again behind them (useShelves).
 */
import type { Entry, GameMatch, Page } from "@quiverlauncher/api";

/** How long an answer is shown again without asking the catalog. */
export const FRESH_FOR = 5 * 60_000;
/** How long a shelf kept on disk is still shown, while asked for again: past that it's too old to show. */
export const SHOWN_FOR = 7 * 24 * 60 * 60_000;
/** Where the shelves are kept on disk. */
export const SHELVES_KEY = "quiver.browseShelves";

type Kept<T> = { at: number; value: T };
const fresh = <T>(kept: Kept<T> | undefined) => (kept && Date.now() - kept.at < FRESH_FOR ? kept.value : undefined);

const shelves = new Map<string, Kept<Entry[]>>(fromDisk());

function fromDisk(): [string, Kept<Entry[]>][] {
  try {
    const kept: unknown = JSON.parse(localStorage.getItem(SHELVES_KEY) ?? "{}");
    if (!kept || typeof kept !== "object") return [];
    return Object.entries(kept as Record<string, Kept<Entry[]>>).filter(([, k]) => typeof k?.at === "number" && Array.isArray(k.value) && Date.now() - k.at < SHOWN_FOR);
  } catch {
    return [];
  }
}

// Written once for the shelves that came together, not once each.
let writing = false;
function toDisk() {
  if (writing) return;
  writing = true;
  setTimeout(() => {
    writing = false;
    try {
      localStorage.setItem(SHELVES_KEY, JSON.stringify(Object.fromEntries(shelves)));
    } catch {
      // Full, or not allowed: they're kept for this run only.
    }
  });
}
const grids = new Map<string, Kept<{ pages: Page<Entry>[]; owned: string[] }>>();
const games = new Map<string, Kept<GameMatch[]>>();

/** A shelf's apps for this computer, by its request (discover.tsx's ShelfKey) and the platform asked for. */
export const keptShelf = (key: string) => fresh(shelves.get(key));
/** A shelf to show while it's asked for again (one from before the launcher started, or older than FRESH_FOR). */
export const shownShelf = (key: string) => {
  const kept = shelves.get(key);
  return kept && Date.now() - kept.at < SHOWN_FOR ? kept.value : undefined;
};
export function keepShelf(key: string, entries: Entry[]) {
  shelves.set(key, { at: Date.now(), value: entries });
  toDisk();
}

/**
 * The grid's pages for one search and set of filters (`key`), and the apps left out as in the library. More pages
 * from Show more keep the first one's time: they all go together.
 */
export const keptGrid = (key: string) => fresh(grids.get(key));
export function keepGrid(key: string, pages: Page<Entry>[], owned: Iterable<string>) {
  const before = grids.get(key);
  grids.set(key, { at: pages.length > 1 && before ? before.at : Date.now(), value: { pages, owned: [...owned] } });
}

/** The games whose titles match a search. */
export const keptGames = (search: string) => fresh(games.get(search));
export const keepGames = (search: string, found: GameMatch[]) => void games.set(search, { at: Date.now(), value: found });

/** An app Browse showed, by its slug: its art, for its page asked for ahead (app-memory.ts). */
export function keptEntry(slug: string): Entry | undefined {
  for (const { value } of shelves.values()) {
    const found = value.find((e) => e.slug === slug);
    if (found) return found;
  }
  for (const { value } of grids.values())
    for (const page of value.pages) {
      const found = page.items.find((e) => e.slug === slug);
      if (found) return found;
    }
  return undefined;
}

/** Forgets everything (a test's next case). */
export function forgetBrowse() {
  shelves.clear();
  try {
    localStorage.removeItem(SHELVES_KEY);
  } catch {
    // Nothing kept.
  }
  grids.clear();
  games.clear();
}
