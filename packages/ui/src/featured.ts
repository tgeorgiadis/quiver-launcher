/**
 * Which apps the featured slides across the top of the catalog's front page show: a mix of the best rated, the just
 * updated and the newest, a different few each day. Plain data, without React, so the site's Worker can pick the same
 * first slide to preload its art (worker/index.ts) as the page then shows.
 */

/** Why an app is featured, said on its slide: one of the front page's lists it came from. */
export type FeaturedReason = "rating" | "updated" | "added";

/** What picking needs of an app. */
export type Pickable = {
  id: string;
  recommended: number;
  reportIssues: number;
  reportBroken: number;
  aiLevel?: string | null;
  libraryArt?: { hero?: string | null; logo?: string | null } | null;
};

/** How many slides there are. */
export const FEATURED_COUNT = 6;
/** Of each list, the first this many take turns on the slides, so a day's picks are still among its best. */
export const FEATURED_POOL = 10;
/** The lists the slides come from, in turn. The day says which leads. */
export const FEATURED_FROM: readonly FeaturedReason[] = ["rating", "updated", "added"];

/** Whether a featured slide can be made of an app: its wide art and its game's logo. */
export const featurable = (entry: Pick<Pickable, "libraryArt">) => Boolean(entry.libraryArt?.hero && entry.libraryArt?.logo);

/**
 * Whether most players who said how an app runs said it runs well, as `Score`'s verdict has it (a tie goes to the
 * more careful answer). The best rated shelf holds only these: the catalog's order alone can put an app one player
 * couldn't run near the top.
 */
export const runsWell = (e: Pick<Pickable, "recommended" | "reportIssues" | "reportBroken">) =>
  e.recommended > e.reportIssues && e.recommended > e.reportBroken;

/** The day, counted in UTC as the Worker counts it, that says the picks: the same everywhere until midnight UTC. */
export const featuredDay = (now = Date.now()) => Math.floor(now / 86_400_000);

/**
 * Whether an app can be featured from a list: it has the art for a slide, isn't mostly AI-generated (its shelves and
 * the full list still have it), and players don't say it runs badly. From the best rated, players must say it runs well.
 */
const eligible = (reason: FeaturedReason) => (e: Pickable) =>
  featurable(e) && e.aiLevel !== "generated" && (reason === "rating" ? runsWell(e) : e.recommended + e.reportIssues + e.reportBroken === 0 || runsWell(e));

/** A list's first `FEATURED_POOL` eligible apps, shuffled the same way all day. */
function pool<T extends Pickable>(reason: FeaturedReason, list: readonly T[], day: number) {
  const apps = list.filter(eligible(reason)).slice(0, FEATURED_POOL);
  // mulberry32, seeded by the day and the list, so each list shuffles its own way.
  let seed = (day * 3 + FEATURED_FROM.indexOf(reason)) >>> 0;
  const random = () => {
    seed = (seed + 0x6d2b79f5) >>> 0;
    let t = seed;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  for (let i = apps.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [apps[i], apps[j]] = [apps[j], apps[i]];
  }
  return apps;
}

/**
 * The day's featured apps, each with the list it's from: one from each list in turn, the day's lead list first. An
 * app on two lists is shown once; when a list runs out, the others fill its turns. A list that hasn't loaded is passed
 * as undefined and sits out, so callers wait for all of them to keep the slides from changing as they come.
 */
export function featuredMix<T extends Pickable>(lists: Partial<Record<FeaturedReason, readonly T[]>>, day = featuredDay(), count = FEATURED_COUNT) {
  const lead = day % FEATURED_FROM.length;
  const order = FEATURED_FROM.map((_, i) => FEATURED_FROM[(lead + i) % FEATURED_FROM.length]);
  const pools = new Map(order.map((reason) => [reason, pool(reason, lists[reason] ?? [], day)]));
  const picked: { entry: T; reason: FeaturedReason }[] = [];
  const used = new Set<string>();
  const next = (reason: FeaturedReason) => {
    const apps = pools.get(reason)!;
    while (apps.length) {
      const entry = apps.shift()!;
      if (!used.has(entry.id)) return entry;
    }
    return undefined;
  };
  for (let turn = 0; picked.length < count; turn++) {
    // This turn's list, else the next one in order that still has an app.
    let found: { entry: T; reason: FeaturedReason } | undefined;
    for (let i = 0; i < order.length && !found; i++) {
      const reason = order[(turn + i) % order.length];
      const entry = next(reason);
      if (entry) found = { entry, reason };
    }
    if (!found) break;
    used.add(found.entry.id);
    picked.push(found);
  }
  return picked;
}
