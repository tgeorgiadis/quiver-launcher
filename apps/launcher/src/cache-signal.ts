/**
 * Asks the download cache for a run (cache-fetch.ts), from anywhere: Settings › Cache's and Downloads' Check now, an
 * app's menu, an install. Apart from cache-fetch.ts, which the store would otherwise import back.
 */
export type CacheAsk = { count: number; only?: string[] };

let ask: CacheAsk = { count: 0 };
const listeners = new Set<() => void>();

export const subscribeCacheAsk = (l: () => void) => (listeners.add(l), () => void listeners.delete(l));
export const cacheAsk = () => ask;

/** A run now: of these apps alone (Installed only, Selected titles), or of everything the cache keeps. */
export function requestCache(only?: string[]) {
  ask = { count: ask.count + 1, ...(only && { only }) };
  listeners.forEach((l) => l());
}
