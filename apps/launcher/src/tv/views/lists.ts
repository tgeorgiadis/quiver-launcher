/** What Discover and Downloads list, and the remembered release file. Pure. */
import { inLibraryView, isInstalled, type TvApp } from "../bridge";
import { isBusy } from "./tileActions";

/** Search: every word of the query in the game's names or tags, any case. */
export function matchesSearch(app: Pick<TvApp, "title" | "gameTitle" | "tags">, query: string): boolean {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  if (!words.length) return true;
  const text = [app.title, app.gameTitle, ...app.tags].join(" ").toLowerCase();
  return words.every((w) => text.includes(w));
}

/** Discover's library filter: what's not in the library yet, what's in it but not installed, or both. */
export type LibraryFilter = "notInLibrary" | "inLibrary" | "all";

/**
 * Discover: games to get. Games in the library's tabs (installed, or downloading
 * for the first time) aren't here, except those got during this visit (`kept`):
 * they stay where they were, so getting one doesn't move the others.
 * `library` is the library as TV mode shows it; `catalog` the catalog's pages
 * (asked for the chosen consoles where the catalog could narrow to them), seen as apps.
 * `consoles`: the console chosen, or those a maker or kind narrows to; null is any.
 */
export function discoverApps(
  library: readonly TvApp[],
  catalog: readonly TvApp[],
  filter: LibraryFilter,
  consoles: ReadonlySet<string> | null,
  kept: ReadonlySet<string> = new Set(),
): TvApp[] {
  const ids = new Set(library.map((a) => a.id));
  const onConsole = (a: TvApp) => !consoles || (a.basedOn !== undefined && consoles.has(a.basedOn)) || a.consoles.some((c) => consoles.has(c));
  const mine = library.filter((a) => onConsole(a) && (kept.has(a.id) || !inLibraryView(a)));
  const theirs = catalog.filter((a) => onConsole(a) && (!ids.has(a.id) || kept.has(a.id)));
  if (filter === "inLibrary") return mine;
  if (filter === "notInLibrary") return theirs;
  return [...mine.filter((a) => !kept.has(a.id)), ...theirs];
}

export type DownloadRow = { kind: "updateAll"; apps: TvApp[] } | { kind: "app"; app: TvApp; status: "active" | "queued" | "failed" | "done" };

/**
 * Downloads: Update all (when there are updates), then what's downloading, what's waiting, what failed,
 * and what finished since `since` (this session), newest first.
 */
export function downloadRows(apps: readonly TvApp[], since: number): DownloadRow[] {
  const updates = apps.filter((a) => a.state === "updateAvailable");
  const active = apps.filter((a) => a.state === "downloading" || a.state === "updating");
  const queued = apps.filter((a) => a.state === "queued");
  const failed = apps.filter((a) => a.state === "error");
  const done = apps
    .filter((a) => isInstalled(a) && !isBusy(a) && a.state !== "error" && (a.installedAt ?? 0) >= since)
    .sort((a, b) => (b.installedAt ?? 0) - (a.installedAt ?? 0));
  return [
    ...(updates.length ? [{ kind: "updateAll" as const, apps: updates }] : []),
    ...active.map((app) => ({ kind: "app" as const, app, status: "active" as const })),
    ...queued.map((app) => ({ kind: "app" as const, app, status: "queued" as const })),
    ...failed.map((app) => ({ kind: "app" as const, app, status: "failed" as const })),
    ...done.map((app) => ({ kind: "app" as const, app, status: "done" as const })),
  ];
}

