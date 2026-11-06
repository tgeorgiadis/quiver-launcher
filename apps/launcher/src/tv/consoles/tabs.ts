/**
 * The tab strip (spec §7.3): Home, All, one tab per console with at least one
 * installed game, then Discover and Downloads. A console with nothing
 * installed has no tab.
 */
import type { TvSettings } from "../data/sidecar";
import type { ConsoleCatalog, ConsoleInfo } from "./consoles";

export type TabKind = "home" | "all" | "console" | "discover" | "downloads";

export type Tab = {
  /** Stable across rebuilds: "home", "all", "console:n64", "discover", "downloads". */
  id: string;
  kind: TabKind;
  label: string;
  /** For a crowded strip. */
  short: string;
  console?: ConsoleInfo;
  /** Games in a console tab; active downloads on Downloads. */
  count?: number;
};

export const HOME: Tab = { id: "home", kind: "home", label: "Home", short: "Home" };
export const ALL: Tab = { id: "all", kind: "all", label: "All", short: "All" };
export const DISCOVER: Tab = { id: "discover", kind: "discover", label: "Discover", short: "Discover" };
export const downloadsTab = (active: number): Tab => ({ id: "downloads", kind: "downloads", label: "Downloads", short: "Downloads", count: active });
export const consoleTabId = (consoleId: string) => `console:${consoleId}`;

/** `installed` is each installed app's console id (from resolveConsole). */
export function buildTabs(
  installed: readonly string[],
  catalog: ConsoleCatalog,
  settings: Pick<TvSettings, "showHomeTab" | "consoleTabOrder">,
  downloadsActive: number,
): Tab[] {
  const counts = new Map<string, number>();
  for (const id of installed) counts.set(id, (counts.get(id) ?? 0) + 1);
  const groups = [...counts].map(([id, count]) => ({ console: catalog.get(id), count }));

  const order = {
    gameCount: (a: (typeof groups)[0], b: (typeof groups)[0]) => b.count - a.count || a.console.name.localeCompare(b.console.name),
    alphabetical: (a: (typeof groups)[0], b: (typeof groups)[0]) => a.console.name.localeCompare(b.console.name),
    manufacturerThenYear: (a: (typeof groups)[0], b: (typeof groups)[0]) => catalog.compare(a.console, b.console),
  }[settings.consoleTabOrder];
  // Apps & Tools and Other come after the consoles whatever the order.
  groups.sort((a, b) => Number(a.console.special) - Number(b.console.special) || order(a, b));

  return [
    ...(settings.showHomeTab ? [HOME] : []),
    ALL,
    ...groups.map(({ console, count }) => ({ id: consoleTabId(console.id), kind: "console" as const, label: console.name, short: console.short, console, count })),
    DISCOVER,
    downloadsTab(downloadsActive),
  ];
}

/** The tab to show after a rebuild: the same one by id, else All (its last game was uninstalled). */
export function keepTab(tabs: readonly Tab[], selectedId: string): Tab {
  return tabs.find((t) => t.id === selectedId) ?? tabs.find((t) => t.id === ALL.id)!;
}

/**
 * LT / RT: straight between the library tab last shown (Home, All or a console),
 * Discover and Downloads, round and round. The library tab is All if it has gone.
 */
export function cycleTab(tabs: readonly Tab[], currentId: string, libraryId: string, by: 1 | -1): string {
  const ring = [keepTab(tabs, libraryId).id, DISCOVER.id, "downloads"];
  const at = Math.max(0, ring.indexOf(currentId));
  return ring[(at + by + ring.length) % ring.length];
}
