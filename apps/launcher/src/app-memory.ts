/**
 * What an app's page asks the catalog (its page on the site, its README, its newest releases), kept while the
 * launcher runs: the page opens whole, drawn in place at once, rather than a part at a time as each answer comes and
 * pushes the rest down. Asked ahead while the pointer or the focus is on a card (prefetchApp), and all at once as the
 * page opens, not the README after the page's answer. Each answer is kept for FRESH_FOR (browse-memory.ts), as Browse's
 * are; a failed one isn't kept, so the page asks again.
 */
import type { Client } from "@quiverlauncher/api";
import { warmImage } from "@quiverlauncher/ui";
import { FRESH_FOR } from "./browse-memory";

type Kept = { at: number; promise: Promise<unknown>; value?: unknown; came: boolean };
const kept = new Map<string, Kept>();
const fresh = (k: Kept | undefined) => (k && Date.now() - k.at < FRESH_FOR ? k : undefined);

/** The questions an app's page asks, by its slug. */
export const appKey = {
  detail: (slug: string) => `detail|${slug}`,
  readme: (slug: string) => `readme|${slug}`,
  /** The newest few, as the page lists them first (useReleases). */
  releases: (slug: string) => `releases|${slug}`,
  /** Who made it: their profile on the site (detail.tsx's DeveloperLink), by their key. */
  developer: (key: string) => `developer|${key}`,
  /** "More like this" under the page (discover.tsx's MoreLikeThis): its console's best rated, for this computer. */
  likeThis: (console: string, os: string | undefined) => `like-this|${console}|${os}`,
  /** A game's page, and its apps for "ways to play" (game.tsx, ways-to-play.tsx). */
  game: (slug: string) => `game|${slug}`,
  /** Every release's first page, the Releases tab's, whose length its count shows (detail.tsx's useReleaseHistory). */
  history: (slug: string) => `history|${slug}`,
  /** The newest player feedback, as the Feedback tab lists it first (feedback.tsx). */
  reviews: (slug: string) => `reviews|${slug}`,
};
/** How much player feedback the Feedback tab lists first. */
export const FIRST_REVIEWS = 10;
/** How many releases the page lists first. */
export const FIRST_RELEASES = 5;

/** `key`'s answer, when it came and is fresh: what the page draws as it opens. */
export function keptAnswer<T>(key: string): T | undefined {
  const k = fresh(kept.get(key));
  return k?.came ? (k.value as T) : undefined;
}

/** `key`'s answer: the one kept or on its way, else asked now with `ask`. */
export function answer<T>(key: string, ask: () => Promise<T>): Promise<T> {
  const k = fresh(kept.get(key));
  if (k) return k.promise as Promise<T>;
  const asked: Kept = { at: Date.now(), promise: Promise.resolve(), came: false };
  asked.promise = ask().then(
    (value) => {
      asked.value = value;
      asked.came = true;
      return value;
    },
    (e: unknown) => {
      if (kept.get(key) === asked) kept.delete(key);
      throw e;
    },
  );
  kept.set(key, asked);
  return asked.promise as Promise<T>;
}

/** Asks for what an app's page shows first, all at once, so it's there (or on its way) when the page opens. */
export function prefetchApp(client: Client, slug: string): void {
  const quiet = () => {};
  // Who made it once the page says (its link and badges beside "Made by").
  answer(appKey.detail(slug), () => client.app(slug)).then((detail) => {
    const key = detail?.entry.developer?.key;
    if (key) answer(appKey.developer(key), () => client.developer(key)).catch(quiet);
  }, quiet);
  answer(appKey.readme(slug), () => client.readme(slug)).catch(quiet);
  answer(appKey.releases(slug), () => client.releases(slug, FIRST_RELEASES, null)).catch(quiet);
}

/**
 * A game's page asked ahead, as the pointer or the focus rests on a link to it (the app page's "Based on"): its apps,
 * and its backdrop and box art once they say what they are, so it opens whole.
 */
export function prefetchGame(client: Client, slug: string): void {
  answer(appKey.game(slug), () => client.game(slug)).then((d) => {
    const art = d?.game.libraryArt;
    const backdrop = art?.hero || art?.header;
    const box = art?.capsule ?? d?.game.artwork;
    if (backdrop) warmImage({ src: backdrop });
    if (box) warmImage({ src: box });
  }, () => {});
}

/** The Feedback tab asked ahead, as the pointer or the focus comes to it. */
export function prefetchReviews(client: Client, slug: string): void {
  answer(appKey.reviews(slug), () => client.reviews(slug, null, FIRST_REVIEWS)).catch(() => {});
}

/** Forgets everything (a test's next case). */
export function forgetApps() {
  kept.clear();
}
