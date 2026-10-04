import { expect, test } from "vitest";
import { ApiError, type Client } from "@quiver/api";
import { byPlayerFeedback, createConvexClient } from "./catalog";

/** Convex's HTTP endpoint, answering each query from `answers` and recording what was asked. */
function fakeConvex(answers: Record<string, unknown>) {
  const asked: { path: string; args: Record<string, unknown> }[] = [];
  const fetch = (async (url: string, init: RequestInit) => {
    expect(url).toBe("https://convex.test/api/query");
    const { path, args } = JSON.parse(String(init.body));
    asked.push({ path, args: args[0] });
    const body = path in answers ? { status: "success", value: answers[path] } : { status: "error", errorMessage: `Could not find public function for '${path}'` };
    return new Response(JSON.stringify(body), { status: path in answers ? 200 : 560 });
  }) as typeof globalThis.fetch;
  return { fetch, asked };
}
const rest = { releaseStatus: async () => ({ items: [{ id: "a", slug: "a", provider: "github" }], nextCursor: null, isDone: true }) } as unknown as Client;

test("the listing goes through catalog:list, leaving out what isn't set and sorting only without a search", async () => {
  const { fetch, asked } = fakeConvex({ "catalog:list": { page: [{ id: "a" }], continueCursor: "next", isDone: false } });
  const client = createConvexClient("https://convex.test", rest, fetch);
  expect(await client.apps({ sort: "rating", ai: "no-ai", console: "maker:Nintendo", limit: 5 })).toEqual({ items: [{ id: "a" }], nextCursor: "next", isDone: false });
  expect(asked[0]).toEqual({ path: "catalog:list", args: { paginationOpts: { numItems: 5, cursor: null }, sort: "rating", ai: "no-ai", maker: "Nintendo" } });
  await client.apps({ search: "  mario ", sort: "rating", console: "n64", cursor: "next" });
  expect(asked[1].args).toEqual({ paginationOpts: { numItems: 48, cursor: "next" }, search: "mario", console: "n64" });
});

test("an app, README and releases come from Convex; a missing app is a 404", async () => {
  const detail = { entry: { id: "a" }, project: { name: "A", description: "", provider: "github", repository: "o/a" }, withdrawn: [] };
  const { fetch, asked } = fakeConvex({
    "catalog:detail": detail,
    "catalog:readme": { markdown: "# A", rawBase: "r/", htmlBase: "h/", fetchedAt: 1 },
    "catalog:releases": { page: [{ id: "r1" }], continueCursor: "", isDone: true },
  });
  const client = createConvexClient("https://convex.test", rest, fetch);
  expect(await client.app("a")).toEqual(detail);
  expect(await client.readme("a")).toEqual({ markdown: "# A", rawBase: "r/", htmlBase: "h/" });
  expect(await client.releases("a", 100)).toEqual({ items: [{ id: "r1" }], nextCursor: null, isDone: true });
  expect(asked.at(-1)).toEqual({ path: "catalog:releases", args: { slug: "a", paginationOpts: { numItems: 100, cursor: null } } });

  const none = createConvexClient("https://convex.test", rest, fakeConvex({ "catalog:detail": null, "catalog:readme": null }).fetch);
  await expect(none.app("gone")).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 404);
  expect(await none.readme("gone")).toBeNull();
});

test("game search needs two characters, and the release status feed stays on REST", async () => {
  const { fetch, asked } = fakeConvex({ "catalog:matchingGames": [{ slug: "g", title: "G", apps: 2 }], "catalog:game": null });
  const client = createConvexClient("https://convex.test", rest, fetch);
  expect(await client.matchingGames(" m ")).toEqual([]);
  expect(asked).toEqual([]);
  expect(await client.matchingGames(" ma ")).toEqual([{ slug: "g", title: "G", apps: 2 }]);
  expect(asked[0]).toEqual({ path: "catalog:matchingGames", args: { search: "ma" } });
  expect(await client.game("nope")).toBeNull();
  expect((await client.releaseStatus()).items[0].slug).toBe("a");
  expect(asked.map((a) => a.path)).not.toContain("releaseStatus:page");
});

test("an error from the site is thrown, not swallowed", async () => {
  const client = createConvexClient("https://convex.test", rest, fakeConvex({}).fetch);
  await expect(client.facets()).rejects.toThrow(/catalog:facets/);
});

const way = (projectName: string, recommended: number, reportIssues = 0, reportBroken = 0, lastReleaseAt?: number) => ({
  projectName, recommended, reportIssues, reportBroken, lastReleaseAt,
});

test("ways to play a game are best first, as on the website", () => {
  const order = (...ways: ReturnType<typeof way>[]) => ways.sort(byPlayerFeedback).map((w) => w.projectName);
  // One "runs well" beats none; ten of ten beat one of one; one "doesn't run" goes below no feedback.
  expect(order(way("none", 0), way("one", 1))).toEqual(["one", "none"]);
  expect(order(way("one", 1), way("ten", 10))).toEqual(["ten", "one"]);
  expect(order(way("broken", 0, 0, 1), way("none", 0))).toEqual(["none", "broken"]);
  // Equal scores: more feedback, then the newer release, then the name.
  expect(order(way("few", 1, 0, 1), way("many", 2, 0, 2))).toEqual(["many", "few"]);
  expect(order(way("old", 0, 0, 0, 1), way("new", 0, 0, 0, 2))).toEqual(["new", "old"]);
  expect(order(way("b", 0), way("a", 0))).toEqual(["a", "b"]);
});
