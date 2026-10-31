/**
 * How the library can be sorted: recently played, added or updated, by name or by
 * the game an app plays, or installed apps first or last. Names sort without
 * a leading "The", "A" or "An", as in Quiver Launcher 3.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";

export type LibrarySort = "played" | "added" | "updated" | "name" | "game" | "installed" | "not-installed";

// Put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const LIBRARY_SORTS: [LibrarySort, MessageDescriptor][] = [
  ["played", msg`Recently played`],
  ["added", msg`Recently added`],
  ["updated", msg`Recently updated`],
  ["name", msg({ message: "Name", comment: "Library sort: by the app's name" })],
  ["game", msg({ message: "Game", comment: "Library sort: by the game an app plays" })],
  ["installed", msg`Installed first`],
  ["not-installed", msg`Not installed first`],
];

export const isLibrarySort = (value: unknown): value is LibrarySort => LIBRARY_SORTS.some(([sort]) => sort === value);

/** A title as it sorts: "The Legend of Zelda" goes under L. */
export const sortTitle = (title: string) => title.trim().replace(/^(the|an?)\s+(?=\S)/i, "");

const byTitle = (a: string, b: string) => sortTitle(a).localeCompare(sortTitle(b), i18n.locale, { sensitivity: "base", numeric: true });

/** What sorting knows about an app on this computer. */
export type SortFacts = { lastPlayed?: number; addedAt?: number; installed: boolean };

/** The game an app plays, or its own name for an app without one. */
const gameOf = (entry: Entry) => entry.games[0]?.title ?? entry.projectName;

/** When an app last had a release: its newest verified one, else its newest at all; apps the player added have none. */
const updatedAt = (entry: Entry) => entry.verified?.releasedAt ?? entry.lastReleaseAt ?? 0;

/** Orders two apps; ties go by name. */
export function compareApps(sort: LibrarySort, a: Entry, b: Entry, facts: (entry: Entry) => SortFacts): number {
  const [fa, fb] = [facts(a), facts(b)];
  const name = byTitle(a.projectName, b.projectName);
  switch (sort) {
    case "played":
      return (fb.lastPlayed ?? 0) - (fa.lastPlayed ?? 0) || name;
    case "added":
      return (fb.addedAt ?? 0) - (fa.addedAt ?? 0) || name;
    case "updated":
      return updatedAt(b) - updatedAt(a) || name;
    case "game":
      return byTitle(gameOf(a), gameOf(b)) || name;
    case "installed":
      return Number(fb.installed) - Number(fa.installed) || name;
    case "not-installed":
      return Number(fa.installed) - Number(fb.installed) || name;
    case "name":
      return name;
  }
}
