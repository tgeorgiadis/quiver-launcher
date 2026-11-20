/**
 * The year each game first came out, by its slug, for sorting the library by release date (sort.ts). The catalog's
 * apps don't carry it, only its game pages (client.game), so it's asked for each game once, a few at a time, and only
 * while a release date sort is on; kept on this device after. A game the catalog has no year for is null.
 */
import { useEffect, useSyncExternalStore } from "react";
import type { Client } from "@quiverlauncher/api";

const KEY = "quiver.gameYears";
/** How many games are asked about at once. */
const AT_ONCE = 4;

function load(): Record<string, number | null> {
  try {
    const saved = JSON.parse(localStorage.getItem(KEY) ?? "{}");
    return saved && typeof saved === "object" ? saved : {};
  } catch {
    return {};
  }
}

let years = load();
const asking = new Set<string>();
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => (listeners.add(listener), () => void listeners.delete(listener));
const snapshot = () => years;

function learned(found: Record<string, number | null>) {
  years = { ...years, ...found };
  try {
    localStorage.setItem(KEY, JSON.stringify(years));
  } catch {
    // Kept for this run only.
  }
  for (const listener of listeners) listener();
}

/** The years known so far; with `wanted`, the games in `slugs` not known yet are asked for. */
export function useGameYears(client: Client, slugs: readonly string[], wanted: boolean): Record<string, number | null> {
  const known = useSyncExternalStore(subscribe, snapshot);
  const missing = wanted ? [...new Set(slugs)].filter((slug) => slug && !(slug in years) && !asking.has(slug)) : [];
  const key = missing.join(" ");
  useEffect(() => {
    if (!missing.length) return;
    missing.forEach((slug) => asking.add(slug));
    let next = 0;
    const work = async () => {
      while (next < missing.length) {
        const slug = missing[next++];
        // A game that couldn't be asked about (offline) stays unknown, to be asked again another time.
        const detail = await client.game(slug).catch(() => undefined);
        asking.delete(slug);
        if (detail !== undefined) learned({ [slug]: detail?.game.year ?? null });
      }
    };
    void Promise.all(Array.from({ length: Math.min(AT_ONCE, missing.length) }, work));
  }, [client, key]); // eslint-disable-line react-hooks/exhaustive-deps
  return known;
}
