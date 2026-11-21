/**
 * How the library can be sorted: recently played, added, updated or installed, most
 * played, by name or by the game an app plays, by when that game first came out, or
 * installed apps first or last. Names sort without a leading "The", "A" or "An", as
 * in Quiver Launcher 3.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";

export type LibrarySort =
  | "played"
  | "added"
  | "updated"
  | "installed-at"
  | "playtime"
  | "name"
  | "game"
  | "released"
  | "released-new"
  | "installed"
  | "not-installed";

// Put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const LIBRARY_SORTS: [LibrarySort, MessageDescriptor][] = [
  ["played", msg`Recently played`],
  ["added", msg`Recently added`],
  ["updated", msg`Recently updated`],
  ["installed-at", msg({ message: "Recently installed", comment: "Library sort: the app installed last first" })],
  ["playtime", msg({ message: "Most played", comment: "Library sort: the app played longest, all told, first" })],
  ["name", msg({ message: "Name", comment: "Library sort: by the app's name" })],
  ["game", msg({ message: "Game", comment: "Library sort: by the game an app plays" })],
  ["released", msg({ message: "Release date, oldest first", comment: "Library sort: by the year the game an app plays first came out (not the app's own release)" })],
  ["released-new", msg({ message: "Release date, newest first", comment: "Library sort: by the year the game an app plays first came out (not the app's own release)" })],
  ["installed", msg`Installed first`],
  ["not-installed", msg`Not installed first`],
];

export const isLibrarySort = (value: unknown): value is LibrarySort => LIBRARY_SORTS.some(([sort]) => sort === value);

/** A title as it sorts: "The Legend of Zelda" goes under L. */
export const sortTitle = (title: string) => title.trim().replace(/^(the|an?)\s+(?=\S)/i, "");

const byTitle = (a: string, b: string) => sortTitle(a).localeCompare(sortTitle(b), i18n.locale, { sensitivity: "base", numeric: true });

/** Sorts by the year the game an app plays first came out: they need game-years.ts to have looked it up. */
export const byRelease = (sort: LibrarySort) => sort === "released" || sort === "released-new";

/**
 * What sorting knows about an app on this computer: `year` the one its game first came out in (game-years.ts), and
 * `playTime` in seconds.
 */
export type SortFacts = { lastPlayed?: number; addedAt?: number; installedAt?: number; playTime?: number; year?: number | null; installed: boolean };

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
    case "installed-at":
      return (fb.installedAt ?? 0) - (fa.installedAt ?? 0) || name;
    case "playtime":
      return (fb.playTime ?? 0) - (fa.playTime ?? 0) || name;
    case "released":
    case "released-new": {
      // A game with no year known goes last, either way.
      const [ya, yb] = [fa.year ?? undefined, fb.year ?? undefined];
      if (ya === undefined || yb === undefined) return Number(ya === undefined) - Number(yb === undefined) || name;
      return (sort === "released" ? ya - yb : yb - ya) || byTitle(gameOf(a), gameOf(b)) || name;
    }
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
