import { afterEach, expect, test, vi } from "vitest";
import { ApiError, createClient, createGithub, DEFAULT_API, FALLBACK_API } from "@quiverlauncher/api";
import { byPlayerFeedback } from "./catalog";

/** The site's REST API, answering each path from `answers` (404 otherwise) and recording what was asked. */
function fakeApi(answers: Record<string, unknown>, base = "https://api.test") {
  const asked: string[] = [];
  const fetch = (async (url: string) => {
    expect(url.startsWith(base)).toBe(true);
    const path = url.slice(base.length);
    asked.push(path);
    return path in answers
      ? new Response(JSON.stringify(answers[path]), { status: 200 })
      : new Response(JSON.stringify({ error: { code: "NOT_FOUND", message: "Route not found" } }), { status: 404 });
  }) as typeof globalThis.fetch;
  return { client: createClient(base, { fetch }), asked };
}
const empty = { items: [], nextCursor: null, isDone: true };

test("the listing sends only what's set, makers as maker, and sorts only without a search", async () => {
  const { client, asked } = fakeApi({
    "/apps?limit=5&maker=Nintendo&sort=rating&ai=no-ai": { items: [{ id: "a" }], nextCursor: "next", isDone: false },
    "/apps?limit=48&search=mario&console=n64&cursor=next": empty,
  });
  expect(await client.apps({ sort: "rating", ai: "no-ai", console: "maker:Nintendo", limit: 5 })).toEqual({ items: [{ id: "a" }], nextCursor: "next", isDone: false });
  await client.apps({ search: "  mario ", console: "n64", cursor: "next" });
  expect(asked).toHaveLength(2);
});

test("several kinds of project, from saved filters, go as projectTypes; one goes as projectType", async () => {
  const { client, asked } = fakeApi({ "/apps?limit=48&projectTypes=port%2Cgame": empty, "/apps?limit=48&projectType=tool": empty, "/apps?limit=48": empty });
  await client.apps({ projectTypes: ["port", "game"] });
  await client.apps({ projectTypes: ["tool"] });
  await client.apps({ projectTypes: [] });
  expect(asked).toEqual(["/apps?limit=48&projectTypes=port%2Cgame", "/apps?limit=48&projectType=tool", "/apps?limit=48"]);
});

test("an app, its README and releases; a missing app is a 404 and a missing README null", async () => {
  const detail = { entry: { id: "a" }, project: { name: "A", description: "", provider: "github", repository: "o/a" }, withdrawn: [] };
  const { client } = fakeApi({
    "/apps/a": detail,
    "/apps/a/readme": { markdown: "# A", rawBase: "r/", htmlBase: "h/" },
    "/apps/a/releases?limit=100": { items: [{ id: "r1" }], nextCursor: null, isDone: true },
  });
  expect(await client.app("a")).toEqual(detail);
  expect(await client.readme("a")).toEqual({ markdown: "# A", rawBase: "r/", htmlBase: "h/" });
  expect(await client.releases("a", 100)).toEqual({ items: [{ id: "r1" }], nextCursor: null, isDone: true });
  await expect(client.app("gone")).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 404);
  expect(await client.readme("gone")).toBeNull();
});

test("what players said, 12 at a time", async () => {
  const { client, asked } = fakeApi({
    "/apps/a/reviews?limit=12": { items: [{ id: "r", result: "runs" }], nextCursor: "c2", isDone: false },
    "/apps/a/reviews?limit=12&cursor=c2": empty,
  });
  expect(await client.reviews("a")).toEqual({ items: [{ id: "r", result: "runs" }], nextCursor: "c2", isDone: false });
  await client.reviews("a", "c2");
  expect(asked).toEqual(["/apps/a/reviews?limit=12", "/apps/a/reviews?limit=12&cursor=c2"]);
});

test("game search needs two characters; a site without it finds none", async () => {
  const { client, asked } = fakeApi({ "/games?search=ma": { items: [{ slug: "g", title: "G", apps: 2 }] } });
  expect(await client.matchingGames(" m ")).toEqual([]);
  expect(asked).toEqual([]);
  expect(await client.matchingGames(" ma ")).toEqual([{ slug: "g", title: "G", apps: 2 }]);
  expect(await client.game("nope")).toBeNull();
  // An older site ignores search and lists games, which aren't matches.
  const older = fakeApi({ "/games?search=ma": { items: [{ id: "1", slug: "g", title: "G" }], nextCursor: "c", isDone: false } });
  expect(await older.client.matchingGames("ma")).toEqual([]);
});

test("unverified releases and every release; a site without them is a 404", async () => {
  const unverified = [{ releaseId: "r2", version: "1.1", releasedAt: 2, prerelease: false, state: "unverified", reasons: [], assets: [] }];
  const { client } = fakeApi({
    "/apps/a/unverified-releases": { items: unverified },
    "/apps/a/release-history?limit=10": { items: unverified, nextCursor: "c1", isDone: false },
  });
  expect(await client.unverifiedReleases("a")).toEqual(unverified);
  expect(await client.releaseHistory("a", 10)).toEqual({ items: unverified, nextCursor: "c1", isDone: false });
  await expect(client.unverifiedReleases("b")).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 404);
  await expect(client.releaseHistory("b")).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 404);
});

test("a broken download is reported by the app's catalog id, else its slug; a file the site doesn't offer is a 404", async () => {
  const sent: { url: string; method?: string; type: string | null; body: unknown }[] = [];
  const fetch = (async (url: string, init?: RequestInit) => {
    sent.push({ url, method: init?.method, type: new Headers(init?.headers).get("content-type"), body: JSON.parse(String(init?.body)) });
    return url.includes("/gone/")
      ? new Response(JSON.stringify({ error: { code: "NOT_FOUND", message: "No such file in a release of this app" } }), { status: 404 })
      : new Response(JSON.stringify({ status: "noted" }), { status: 202 });
  }) as typeof globalThis.fetch;
  const client = createClient("https://api.test", { fetch });
  const mismatch = { version: "v1.2", file: "Game-Windows.zip", problem: "mismatch" } as const;
  await client.downloadProblem({ slug: "game", catalogId: "qcat_1" }, mismatch);
  await client.downloadProblem({ slug: "game" }, { ...mismatch, problem: "missing" });
  expect(sent).toEqual([
    { url: "https://api.test/catalog-entries/qcat_1/download-problem", method: "POST", type: "application/json", body: mismatch },
    { url: "https://api.test/apps/game/download-problem", method: "POST", type: "application/json", body: { ...mismatch, problem: "missing" } },
  ]);
  await expect(client.downloadProblem({ slug: "gone" }, mismatch)).rejects.toSatisfy(
    (e) => e instanceof ApiError && e.status === 404 && e.message === "No such file in a release of this app",
  );
});

test("an error from the site is thrown with its message", async () => {
  const fetch = (async () => new Response(JSON.stringify({ error: { message: "Unable to load catalog" } }), { status: 500 })) as typeof globalThis.fetch;
  await expect(createClient("https://api.test", { fetch }).facets()).rejects.toThrow("Unable to load catalog");
});

test("when api.quiverlauncher.com can't be reached, the deployment's own host answers from then on", async () => {
  const asked: string[] = [];
  const fell: unknown[] = [];
  let down = true;
  const fetch = (async (url: string) => {
    asked.push(url);
    if (url.startsWith(DEFAULT_API) && down) throw new TypeError("Failed to fetch");
    return new Response(JSON.stringify({ total: 1, consoles: [] }), { status: 200 });
  }) as typeof globalThis.fetch;
  const client = createClient(DEFAULT_API, { fetch, onFallback: (e) => fell.push(e) });
  expect(await client.facets()).toEqual({ total: 1, consoles: [] });
  down = false;
  await client.facets();
  expect(asked).toEqual([`${DEFAULT_API}/facets`, `${FALLBACK_API}/facets`, `${FALLBACK_API}/facets`]);
  expect(fell).toHaveLength(1);
});

test("the proxy's 499 falls back too, but a real answer, another address or a failing fallback doesn't", async () => {
  const proxy = (async (url: string) =>
    new Response("{}", { status: url.startsWith(DEFAULT_API) ? 499 : 200 })) as typeof globalThis.fetch;
  expect(await createClient(DEFAULT_API, { fetch: proxy }).facets()).toEqual({});

  const asked: string[] = [];
  const missing = (async (url: string) => (asked.push(url), new Response("{}", { status: 404 }))) as typeof globalThis.fetch;
  await expect(createClient(DEFAULT_API, { fetch: missing }).facets()).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 404);
  expect(asked).toEqual([`${DEFAULT_API}/facets`]);

  const offline = (async () => {
    throw new TypeError("Failed to fetch");
  }) as typeof globalThis.fetch;
  await expect(createClient("https://api.test", { fetch: offline }).facets()).rejects.toThrow("Failed to fetch");
  const both = createClient(DEFAULT_API, { fetch: offline });
  await expect(both.facets()).rejects.toThrow("Failed to fetch");
});

test("a server error falls back to the deployment's own host, and keeps the site's message when that fails too", async () => {
  const asked: string[] = [];
  const error = (async (url: string) => (
    asked.push(url),
    url.startsWith(DEFAULT_API)
      ? new Response(JSON.stringify({ error: { message: "Unable to load catalog" } }), { status: 500 })
      : new Response(JSON.stringify({ total: 1 }), { status: 200 })
  )) as typeof globalThis.fetch;
  expect(await createClient(DEFAULT_API, { fetch: error }).facets()).toEqual({ total: 1 });
  expect(asked).toEqual([`${DEFAULT_API}/facets`, `${FALLBACK_API}/facets`]);

  const down = (async () => new Response(JSON.stringify({ error: { message: "Unable to load catalog" } }), { status: 503 })) as typeof globalThis.fetch;
  await expect(createClient(DEFAULT_API, { fetch: down }).facets()).rejects.toSatisfy(
    (e) => e instanceof ApiError && e.status === 503 && e.message === "Unable to load catalog",
  );
});

test("a call the site never answers gives up, and the deployment's own host is asked instead", async () => {
  /** Answers only from the fallback host; the default one hangs until it's given up on. */
  const stalled = ((url: string, init?: RequestInit) =>
    url.startsWith(FALLBACK_API)
      ? Promise.resolve(new Response(JSON.stringify({ total: 2 }), { status: 200 }))
      : new Promise((_, reject) => init?.signal?.addEventListener("abort", () => reject(init.signal!.reason)))) as typeof globalThis.fetch;
  expect(await createClient(DEFAULT_API, { fetch: stalled, timeoutMs: 5 }).facets()).toEqual({ total: 2 });
  await expect(createClient("https://api.test", { fetch: stalled, timeoutMs: 5 }).facets()).rejects.toThrow("took too long to answer");
});

const way = (name: string, recommended: number, reportIssues = 0, reportBroken = 0, lastReleaseAt?: number) => ({
  name, recommended, reportIssues, reportBroken, lastReleaseAt,
});

test("ways to play a game are best first, as on the website", () => {
  const order = (...ways: ReturnType<typeof way>[]) => ways.sort(byPlayerFeedback).map((w) => w.name);
  // One "runs well" beats none; ten of ten beat one of one; one "doesn't run" goes below no feedback.
  expect(order(way("none", 0), way("one", 1))).toEqual(["one", "none"]);
  expect(order(way("one", 1), way("ten", 10))).toEqual(["ten", "one"]);
  expect(order(way("broken", 0, 0, 1), way("none", 0))).toEqual(["none", "broken"]);
  // Equal scores: more feedback, then the newer release, then the name.
  expect(order(way("few", 1, 0, 1), way("many", 2, 0, 2))).toEqual(["many", "few"]);
  expect(order(way("old", 0, 0, 0, 1), way("new", 0, 0, 0, 2))).toEqual(["new", "old"]);
  expect(order(way("b", 0), way("a", 0))).toEqual(["a", "b"]);
});

afterEach(() => vi.unstubAllGlobals());

test("GitHub's hourly limit says when it resets, rather than only its status", async () => {
  const reset = Date.UTC(2026, 9, 21, 9, 30) / 1000;
  const limited = new Response(JSON.stringify({ message: "API rate limit exceeded" }), {
    status: 403,
    headers: { "x-ratelimit-remaining": "0", "x-ratelimit-reset": String(reset) },
  });
  vi.stubGlobal("fetch", async () => limited);
  const at = new Date(reset * 1000).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  await expect(createGithub("https://gh.test").releases("o/r")).rejects.toSatisfy(
    (e) =>
      e instanceof ApiError &&
      e.status === 403 &&
      e.message === `GitHub's limit of requests from this network is used up until ${at}. Try again then, or add a GitHub token in Settings to raise the limit.`,
  );
  await expect(createGithub("https://gh.test", "ghp_x").releases("o/r")).rejects.toThrow(`Your GitHub token's limit of requests is used up until ${at}. Try again then.`);
  vi.stubGlobal("fetch", async () => new Response("", { status: 429, headers: { "retry-after": "90" } }));
  await expect(createGithub("https://gh.test").readme("o/r")).rejects.toThrow("GitHub is limiting requests from this network. Try again in 2 min.");
  vi.stubGlobal("fetch", async () => new Response("", { status: 500 }));
  await expect(createGithub("https://gh.test").releases("o/r")).rejects.toThrow("GitHub answered 500. Try again later.");
});

test("a run of pre-releases doesn't hide the stable release: GitHub's latest comes first", async () => {
  const release = (tag: string, prerelease: boolean) => ({ tag_name: tag, draft: false, prerelease, published_at: "2026-10-01T00:00:00Z", assets: [] });
  const betas = Array.from({ length: 10 }, (_, i) => release(`Version1.0.5beta${10 - i}`, true));
  const asked: string[] = [];
  vi.stubGlobal("fetch", async (url: string) => {
    asked.push(url.slice("https://gh.test/repos/o/r".length));
    return url.endsWith("/releases/latest") ? Response.json(release("Version1.0.4", false)) : Response.json(betas);
  });
  const releases = await createGithub("https://gh.test").releases("o/r");
  expect(releases.map((r) => r.version).slice(0, 2)).toEqual(["Version1.0.4", "Version1.0.5beta10"]);
  expect(asked).toEqual(["/releases?per_page=10", "/releases/latest"]);
  // A page with a stable release, or fewer than asked for, needs no more.
  asked.length = 0;
  await createGithub("https://gh.test").releases("o/r", 20);
  expect(asked).toEqual(["/releases?per_page=20"]);
});

test("a saved GitHub token goes with every request to GitHub, and the limit is read for it", async () => {
  const asked: { url: string; auth: string | null }[] = [];
  vi.stubGlobal("fetch", async (url: string, init?: RequestInit) => {
    const auth = new Headers(init?.headers).get("authorization");
    asked.push({ url, auth });
    if (url.endsWith("/rate_limit"))
      return auth === "Bearer bad"
        ? new Response(JSON.stringify({ message: "Bad credentials" }), { status: 401 })
        : Response.json({ resources: { core: { limit: auth ? 5000 : 60, remaining: 4999, reset: 1_800_000_000 } } });
    return url.endsWith("/readme") ? new Response("# Hi") : Response.json([]);
  });
  const github = createGithub("https://gh.test", "  ghp_x ");
  await github.releases("o/r");
  await github.readme("o/r");
  expect(await github.rateLimit()).toEqual({ limit: 5000, remaining: 4999, resetAt: 1_800_000_000_000 });
  expect(asked.map((a) => a.auth)).toEqual(["Bearer ghp_x", "Bearer ghp_x", "Bearer ghp_x"]);
  // Without one, nothing is sent; with one GitHub refuses, it says so.
  expect((await createGithub("https://gh.test").rateLimit()).limit).toBe(60);
  expect(asked.at(-1)!.auth).toBeNull();
  await expect(createGithub("https://gh.test", "bad").rateLimit()).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 401);
});
