/**
 * The catalog listing through the site's Convex query, which sorts and hides
 * AI-made apps; the REST route ignores both for now. Falls back to REST.
 */
import { ConvexHttpClient } from "convex/browser";
import { makeFunctionReference } from "convex/server";
import type { AppQuery, Client, Entry, Page } from "@quiver/api";

const list = makeFunctionReference<"query">("catalog:list");

export function withConvexListing(rest: Client, url: string): Client {
  const convex = new ConvexHttpClient(url);
  const apps = (q: AppQuery = {}): Promise<Page<Entry>> => {
    const search = q.search?.trim() || undefined;
    const maker = q.console?.startsWith("maker:") ? q.console.slice(6) : undefined;
    return convex
      .query(list, {
        paginationOpts: { numItems: q.limit ?? 48, cursor: q.cursor ?? null },
        ...(search && { search }),
        ...(q.os && { os: q.os }),
        ...(q.projectType && { projectType: q.projectType }),
        ...(maker ? { maker } : q.console && { console: q.console }),
        ...(q.sort && !search && { sort: q.sort }),
        ...(q.ai && { ai: q.ai }),
      })
      .then((r: { page: Entry[]; continueCursor: string; isDone: boolean }) => ({
        items: r.page,
        nextCursor: r.isDone ? null : r.continueCursor,
        isDone: r.isDone,
      }))
      .catch(() => rest.apps(q));
  };
  return { ...rest, apps };
}
