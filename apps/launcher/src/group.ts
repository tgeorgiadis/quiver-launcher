/**
 * The library in sections: by the original console, the company that made
 * it, the game (the sidebar's), or the app's developer. Each app is in one
 * section, by its first console or game; one with none is under Other, last.
 * And the grid's cards: a game's ports as one.
 */
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";

export type GroupBy = "console" | "maker" | "game" | "developer";

// Put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const GROUP_BYS: [GroupBy | "", MessageDescriptor][] = [
  ["", msg`No groups`],
  ["console", msg`Group by console`],
  ["maker", msg`Group by console maker`],
  ["game", msg`Group by game`],
  ["developer", msg`Group by developer`],
];

export const isGroupBy = (value: unknown): value is GroupBy => GROUP_BYS.some(([key]) => key && key === value);

/** The grid's sections: not by game, where a game's ports are one card (or, apart, a heading over each app). */
export const GRID_GROUP_BYS = GROUP_BYS.filter(([key]) => key !== "game");
export const gridGroupBy = (by?: GroupBy) => (by === "game" ? undefined : by);

/** What the catalog knows of the consoles: their names and who made each. */
export type ConsoleFacts = { names: Record<string, string>; makers: Record<string, string> };

/** The section an app goes in; `other` for one with none. */
function sectionOf(entry: Entry, by: GroupBy, { names, makers }: ConsoleFacts, other: string) {
  const id = entry.consoles?.[0];
  if (by === "console") return id ? (names[id] ?? id.toUpperCase()) : other;
  if (by === "maker") {
    const maker = id && makers[id];
    return !maker || maker === "OtherPlatforms" ? other : maker;
  }
  if (by === "game") return entry.games[0]?.title ?? other;
  return entry.developer?.name ?? other;
}

/** The apps in their sections, in order by name, Other last; each section keeps the apps' order. */
export function sections(entries: Entry[], by: GroupBy, facts: ConsoleFacts): [string, Entry[]][] {
  const other = t({ message: "Other", comment: "Library section for apps with no console, maker, game or developer" });
  const groups = new Map<string, Entry[]>();
  for (const e of entries) {
    const name = sectionOf(e, by, facts, other);
    groups.set(name, [...(groups.get(name) ?? []), e]);
  }
  return [...groups].sort(([a], [b]) => (a === other ? 1 : b === other ? -1 : a.localeCompare(b, i18n.locale)));
}

/** A game in the library, by its ports there: the one to play first, then the rest. */
export type Stack = { game: Entry["games"][number]; ports: Entry[] };
export const isStack = (card: Entry | Stack): card is Stack => "ports" in card;

/** What a game's card knows of its ports on this computer. */
export type PortFacts = { lastPlayed?: number; installedAt?: number; installed: boolean };

/**
 * The apps as the grid's cards: a game's ports as one card, where the first of them is, and apps of no game as they
 * are. Each app goes under its first game, so none shows twice. In a game, the port played last comes first, or one
 * installed since, not played yet, as the one the player just chose; then those installed, then the best recommended;
 * else in the order given.
 */
export function stacks(entries: Entry[], facts: (entry: Entry) => PortFacts): (Entry | Stack)[] {
  const cards: (Entry | Stack)[] = [];
  const games = new Map<string, Stack>();
  for (const e of entries) {
    const game = e.games[0];
    const stack = game && games.get(game.id);
    if (stack) stack.ports.push(e);
    else if (game) {
      const made = { game, ports: [e] };
      games.set(game.id, made);
      cards.push(made);
    } else cards.push(e);
  }
  const latest = (f: PortFacts) => (f.installed ? Math.max(f.lastPlayed ?? 0, f.installedAt ?? 0) : 0);
  for (const { ports } of games.values())
    ports.sort((a, b) => {
      const [fa, fb] = [facts(a), facts(b)];
      return latest(fb) - latest(fa) || Number(fb.installed) - Number(fa.installed) || b.recommended - a.recommended;
    });
  return cards;
}
