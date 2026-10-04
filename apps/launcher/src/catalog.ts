/**
 * The catalog through the site's own Convex queries (catalog.ts on
 * quiverlauncher.com), the same ones the website uses. They sort, hide
 * AI-made apps, search games and have READMEs, which the REST API doesn't.
 * Only the release status feed is an internal function there, so that one
 * stays on REST.
 */
import { ConvexHttpClient } from "convex/browser";
import { makeFunctionReference, type DefaultFunctionArgs } from "convex/server";
import { ApiError, type AppQuery, type Client, type Detail, type Entry, type Facets, type Feedback, type GameDetail, type SharedList, type GameMatch, type Page, type Readme, type Release } from "@quiver/api";

type ConvexPage<T> = { page: T[]; continueCursor: string; isDone: boolean };
type PageArgs = { paginationOpts: { numItems: number; cursor: string | null } };

const query = <Args extends DefaultFunctionArgs, Result>(name: string) => makeFunctionReference<"query", Args, Result>(name);
const refs = {
  list: query<PageArgs & Record<string, unknown>, ConvexPage<Entry>>("catalog:list"),
  detail: query<{ slug: string }, Detail | null>("catalog:detail"),
  facets: query<Record<string, never>, Facets>("catalog:facets"),
  readme: query<{ slug: string }, (Readme & { fetchedAt?: number }) | null>("catalog:readme"),
  releases: query<PageArgs & { slug: string }, ConvexPage<Release>>("catalog:releases"),
  matchingGames: query<{ search: string }, GameMatch[]>("catalog:matchingGames"),
  game: query<{ slug: string }, GameDetail | null>("catalog:game"),
  reviews: query<PageArgs & { slug: string }, ConvexPage<Feedback>>("reviews:list"),
  sharedList: query<{ slug: string }, SharedList | null>("sharedLists:get"),
};

const toPage = <T,>(r: ConvexPage<T>): Page<T> => ({ items: r.page, nextCursor: r.isDone ? null : r.continueCursor, isDone: r.isDone });

/** `fetch` is for tests. */
export function createConvexClient(url: string, rest: Client, fetch?: typeof globalThis.fetch): Client {
  const convex = new ConvexHttpClient(url, { logger: false, ...(fetch && { fetch }) });
  return {
    apps(q: AppQuery = {}) {
      const search = q.search?.trim() || undefined;
      const maker = q.console?.startsWith("maker:") ? q.console.slice(6) : undefined;
      // Optional arguments are left out, never null: the site's validators refuse null.
      return convex
        .query(refs.list, {
          paginationOpts: { numItems: q.limit ?? 48, cursor: q.cursor ?? null },
          ...(search && { search }),
          ...(q.os && { os: q.os }),
          ...(q.projectType && { projectType: q.projectType }),
          ...(maker ? { maker } : q.console && { console: q.console }),
          // A search orders by relevance.
          ...(q.sort && !search && { sort: q.sort }),
          ...(q.ai && { ai: q.ai }),
        })
        .then(toPage);
    },
    async app(slug) {
      const detail = await convex.query(refs.detail, { slug });
      if (!detail) throw new ApiError(404, "That app isn't in the catalog.");
      return detail;
    },
    facets: () => convex.query(refs.facets, {}),
    async readme(slug) {
      const readme = await convex.query(refs.readme, { slug });
      return readme && { markdown: readme.markdown, rawBase: readme.rawBase, htmlBase: readme.htmlBase };
    },
    releaseStatus: (cursor) => rest.releaseStatus(cursor),
    releases: (slug, limit = 5, cursor = null) =>
      convex.query(refs.releases, { slug, paginationOpts: { numItems: limit, cursor } }).then(toPage),
    reviews: (slug, cursor = null, limit = 12) =>
      convex.query(refs.reviews, { slug, paginationOpts: { numItems: limit, cursor } }).then(toPage),
    matchingGames(search) {
      const text = search.trim().slice(0, 200);
      return text.length < 2 ? Promise.resolve([]) : convex.query(refs.matchingGames, { search: text });
    },
    game: (slug) => convex.query(refs.game, { slug }),
    sharedList: (slug) => convex.query(refs.sharedList, { slug }),
  };
}

type Way = Pick<Entry, "name" | "recommended" | "reportIssues" | "reportBroken" | "lastReleaseAt">;

/**
 * Best ways to play a game first, as on the website's game page: the share of
 * players saying it runs well (issues count half), smoothed so one report
 * doesn't outrank many. Ties go to more feedback, a newer release, the name.
 */
export function byPlayerFeedback(a: Way, b: Way) {
  const total = (w: Way) => w.recommended + w.reportIssues + w.reportBroken;
  const score = (w: Way) => (w.recommended + w.reportIssues / 2 + 1) / (total(w) + 2);
  return (
    score(b) - score(a) ||
    total(b) - total(a) ||
    (b.lastReleaseAt ?? 0) - (a.lastReleaseAt ?? 0) ||
    a.name.localeCompare(b.name)
  );
}
