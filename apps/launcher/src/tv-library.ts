/**
 * TV mode's library (tv.tsx): which apps show for its search and its Show and Console picks, and in what order, as
 * the library's own page has them (library.tsx): the same tabs, playlists, search and orders.
 */
import type { Entry } from "@quiverlauncher/api";
import { compareApps, sortTitle, type LibrarySort } from "./sort";
import { inCollection, type Collection } from "./sync";
import { isOwn } from "./custom";

/** What the player picked: words to find, a tab or playlist's key (all, installed, not-installed, updates), a console's id or none, an order. */
export type TvPick = { search: string; show: string; console: string; sort: LibrarySort };

/** An app in the library, with what this computer knows of it. */
export type TvApp = { entry: Entry; addedAt: number; installed: boolean; lastPlayed?: number; installedAt?: number; update: boolean; tags: readonly string[] };

export function pickLibrary(apps: readonly TvApp[], pick: TvPick, playlists: readonly Collection[] = [], makers: Record<string, string> = {}) {
  const words = pick.search.toLowerCase().split(/\s+/).filter(Boolean);
  const playlist = playlists.find((c) => c.key === pick.show);
  const facts = new Map(apps.map((a) => [a.entry.id, a]));
  return apps
    .filter((a) => {
      const { entry } = a;
      const text = [entry.projectName, ...entry.games.map((g) => g.title), ...entry.tags, ...a.tags].join(" ").toLowerCase();
      if (!words.every((w) => text.includes(w))) return false;
      if (pick.console && !(entry.consoles ?? []).includes(pick.console)) return false;
      if (pick.show === "installed" || pick.show === "not-installed") return a.installed === (pick.show === "installed");
      if (pick.show === "updates") return a.update;
      if (playlist)
        return inCollection(
          playlist,
          {
            id: entry.id,
            tags: [...entry.tags, ...a.tags],
            consoles: entry.consoles ?? [],
            installed: a.installed,
            // Apps the player added match only the filters there's data for.
            projectType: isOwn(entry.id) ? undefined : entry.projectType,
            aiLevel: entry.aiLevel,
            names: [entry.projectName, ...entry.games.map((g) => g.title)],
          },
          makers,
        );
      return true;
    })
    .sort((x, y) => compareApps(pick.sort, x.entry, y.entry, (e) => facts.get(e.id) ?? { installed: false }));
}

/**
 * The letter a title files under in a list by name, as sorting reads it ("The Legend of Zelda" under L, "Élan" under
 * E), and # for one starting with a digit or a sign.
 */
export function letterOf(title: string) {
  const first = [...sortTitle(title).normalize("NFD")][0] ?? "";
  return /\p{L}/u.test(first) ? first.toLocaleUpperCase() : "#";
}

/**
 * For Jump to, in a list by name or by game: each letter its apps file under, in the list's order, and where the first
 * of them is. None in another order, where letters don't follow on.
 */
export function jumpLetters(entries: readonly Entry[], sort: LibrarySort) {
  if (sort !== "name" && sort !== "game") return [];
  const at = new Map<string, number>();
  entries.forEach((e, i) => {
    const letter = letterOf(sort === "game" ? (e.games[0]?.title ?? e.projectName) : e.projectName);
    if (!at.has(letter)) at.set(letter, i);
  });
  return [...at].map(([letter, index]) => ({ letter, index }));
}

/** The consoles the library's apps are for, the most apps first, for TV mode's Console pick. */
export function libraryConsoles(apps: readonly TvApp[]) {
  const count = new Map<string, number>();
  for (const a of apps) for (const id of new Set(a.entry.consoles ?? [])) count.set(id, (count.get(id) ?? 0) + 1);
  return [...count].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([id, apps]) => ({ id, apps }));
}
