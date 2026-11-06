/** Home's shelves (spec §8): recently played, updates waiting, recently installed. Empty shelves aren't shown. */
import { isInstalled, type TvApp } from "../bridge";

export type Shelf = {
  id: "recent" | "updates" | "installed";
  title: string;
  apps: TvApp[];
  /** Starts with an "Update all" card (X). */
  updateAll?: boolean;
};

export const SHELF_LENGTH = 15;

/** When it was installed: when Big Picture saw it finish, else when it was added to the library. */
export const installedAt = (app: TvApp) => app.installedAt ?? app.addedAt ?? 0;

export function homeShelves(apps: readonly TvApp[]): Shelf[] {
  const installed = apps.filter(isInstalled);
  const recent = installed
    .filter((a) => a.lastPlayed)
    .sort((a, b) => b.lastPlayed! - a.lastPlayed!)
    .slice(0, SHELF_LENGTH);
  const updates = installed.filter((a) => a.state === "updateAvailable").sort((a, b) => a.title.localeCompare(b.title));
  const newest = [...installed].sort((a, b) => installedAt(b) - installedAt(a)).slice(0, SHELF_LENGTH);
  const shelves: Shelf[] = [
    { id: "recent", title: "Recently played", apps: recent },
    { id: "updates", title: "Updates available", apps: updates, updateAll: true },
    { id: "installed", title: "Recently installed", apps: newest },
  ];
  return shelves.filter((s) => s.apps.length);
}

/** Tiles in a shelf's row, counting the Update all card. */
export const rowLength = (shelf: Shelf) => shelf.apps.length + (shelf.updateAll ? 1 : 0);

/** The app at a column; null for the Update all card. */
export const appAt = (shelf: Shelf, col: number): TvApp | null => (shelf.updateAll ? (col === 0 ? null : shelf.apps[col - 1]) : shelf.apps[col]) ?? null;
