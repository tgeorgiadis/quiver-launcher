import { afterEach, expect, test, vi } from "vitest";
import { ApiError, createClient, createGithub, createGitlab, DEFAULT_API, FALLBACK_API } from "@quiverlauncher/api";
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

test("several consoles go as consoles and makers, several platforms as platforms; one of each as before", async () => {
  const urls = [
    "/apps?limit=48&consoles=n64%2Cps1&makers=Sega",
    "/apps?limit=48&makers=Sega%2CSony",
    "/apps?limit=48&maker=Sega",
    "/apps?limit=48&console=n64",
    "/apps?limit=48&platforms=windows%2Cmacos",
    "/apps?limit=48&os=linux",
  ];
  const { client, asked } = fakeApi(Object.fromEntries(urls.map((u) => [u, empty])));
  await client.apps({ consoles: ["n64", "maker:Sega", "ps1"] });
  await client.apps({ consoles: ["maker:Sega", "maker:Sony"] });
  await client.apps({ consoles: ["maker:Sega"] });
  await client.apps({ consoles: ["n64"] });
  await client.apps({ platforms: ["windows", "macos"] });
  await client.apps({ platforms: ["linux"] });
  expect(asked).toEqual(urls);
});

test("an app, its README and releases; a missing app is a 404 and a missing README null", async () => {
  const detail = { entry: { id: "a", games: [] }, project: { name: "A", description: "", provider: "github", repository: "o/a" }, withdrawn: [] };
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

test("an app with no slug, or an answer with no app in it, is a 404: /apps/ answers with the catalog's first page", async () => {
  const { client, asked } = fakeApi({ "/apps/": { items: [{ id: "a" }], nextCursor: null, isDone: true }, "/apps/odd": { items: [] } });
  const notFound = (e: unknown) => e instanceof ApiError && e.status === 404;
  await expect(client.app("")).rejects.toSatisfy(notFound);
  await expect(client.app("odd")).rejects.toSatisfy(notFound);
  expect(asked).toEqual(["/apps/odd"]);
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

test("calls already on their way when it falls back tell about it once", async () => {
  const fell: unknown[] = [];
  const fetch = (async (url: string) => {
    if (url.startsWith(DEFAULT_API)) throw new TypeError("Failed to fetch");
    return new Response(JSON.stringify({ total: 1, consoles: [] }), { status: 200 });
  }) as typeof globalThis.fetch;
  const client = createClient(DEFAULT_API, { fetch, onFallback: (e) => fell.push(e) });
  await Promise.all([client.facets(), client.facets(), client.facets()]);
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

test("GitLab's request limit says when it resets, rather than only its status", async () => {
  const reset = Date.UTC(2026, 9, 21, 9, 30) / 1000;
  const limited = new Response("", {
    status: 429,
    headers: { "ratelimit-remaining": "0", "ratelimit-reset": String(reset) },
  });
  vi.stubGlobal("fetch", async () => limited);
  const at = new Date(reset * 1000).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  await expect(createGitlab("https://gl.test").releases("o/r")).rejects.toSatisfy(
    (e) =>
      e instanceof ApiError &&
      e.status === 429 &&
      e.message === `GitLab's limit of requests from this network is used up until ${at}. Try again then, or add a GitLab token in Settings to raise the limit.`,
  );
  await expect(createGitlab("https://gl.test", "glpat-x").releases("o/r")).rejects.toThrow(`Your GitLab token's limit of requests is used up until ${at}. Try again then.`);
  vi.stubGlobal("fetch", async () => new Response("", { status: 429, headers: { "retry-after": "90" } }));
  await expect(createGitlab("https://gl.test").releases("o/r")).rejects.toThrow("GitLab is limiting requests from this network. Try again in 2 min.");
  vi.stubGlobal("fetch", async () => new Response("", { status: 500 }));
  await expect(createGitlab("https://gl.test").releases("o/r")).rejects.toThrow("GitLab answered 500. Try again later.");
});

test("a saved GitLab token goes with every request to GitLab, and the limit is read for it", async () => {
  const asked: { url: string; token: string | null }[] = [];
  const rate = (limit: number, remaining: number) => ({
    "ratelimit-limit": String(limit),
    "ratelimit-remaining": String(remaining),
    "ratelimit-reset": "1800000000",
  });
  vi.stubGlobal("fetch", async (url: string, init?: RequestInit) => {
    const token = new Headers(init?.headers).get("private-token");
    asked.push({ url, token });
    if (url.endsWith("/user"))
      return token === "glpat-bad" ? new Response(JSON.stringify({ message: "401 Unauthorized" }), { status: 401 }) : new Response("{}", { headers: rate(2000, 1999) });
    if (url.endsWith("/projects?per_page=1")) return new Response("{}", { headers: rate(500, 499) });
    return Response.json([]);
  });
  const gitlab = createGitlab("https://gl.test", "  glpat-x ");
  await gitlab.releases("o/r");
  expect(await gitlab.rateLimit()).toEqual({ limit: 2000, remaining: 1999, resetAt: 1_800_000_000_000 });
  expect(asked.map((a) => a.token)).toEqual(["glpat-x", "glpat-x"]);
  // Without one, GitLab still says how many are left; with one GitLab refuses, it says so.
  expect(await createGitlab("https://gl.test").rateLimit()).toEqual({ limit: 500, remaining: 499, resetAt: 1_800_000_000_000 });
  expect(asked.at(-1)!.token).toBeNull();
  await expect(createGitlab("https://gl.test", "glpat-bad").rateLimit()).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 401);
  // An instance that doesn't say has no usage line.
  vi.stubGlobal("fetch", async () => Response.json({}));
  expect(await createGitlab("https://gl.test", "glpat-x").rateLimit()).toBeNull();
});

test("the Rust side reads GitLab's limit, since the page can't see its headers", async () => {
  const asked: { url: string; token: string | null }[] = [];
  const read = async (url: string, token: string | null) => {
    asked.push({ url, token });
    return token === "glpat-bad" ? { status: 401 } : token ? { status: 200, limit: 2000, remaining: 1999, reset: 1_800_000_000 } : { status: 200, limit: 500, remaining: 499, reset: 1_800_000_000 };
  };
  const browser = vi.fn();
  vi.stubGlobal("fetch", browser);
  const gitlab = createGitlab("https://gl.test", "glpat-x", read);
  expect(await gitlab.rateLimit()).toEqual({ limit: 2000, remaining: 1999, resetAt: 1_800_000_000_000 });
  expect(asked).toEqual([{ url: "https://gl.test/user", token: "glpat-x" }]);
  expect(browser).not.toHaveBeenCalled();
  // Without a token, a public project list says how many are left.
  expect(await createGitlab("https://gl.test", undefined, read).rateLimit()).toEqual({ limit: 500, remaining: 499, resetAt: 1_800_000_000_000 });
  expect(asked.at(-1)).toEqual({ url: "https://gl.test/projects?per_page=1", token: null });
  // A token GitLab refuses is an ApiError, so Settings doesn't save it.
  await expect(createGitlab("https://gl.test", "glpat-bad", read).rateLimit()).rejects.toSatisfy((e) => e instanceof ApiError && e.status === 401);
  // An instance that doesn't say has no usage line.
  expect(await createGitlab("https://gl.test", "glpat-x", async () => ({ status: 200 })).rateLimit()).toBeNull();
});

test("when GitLab refuses releases, the Rust side says whether it's the limit, since the page can't see why", async () => {
  const reset = Date.UTC(2026, 9, 21, 9, 30) / 1000;
  const at = new Date(reset * 1000).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
  const usedUp = async () => ({ status: 429, limit: 500, remaining: 0, reset });
  // The page sees only the status: GitLab doesn't expose its rate-limit headers.
  vi.stubGlobal("fetch", async () => new Response("", { status: 429 }));
  await expect(createGitlab("https://gl.test", undefined, usedUp).releases("o/r")).rejects.toThrow(`GitLab's limit of requests from this network is used up until ${at}.`);
  // A refusal without CORS headers is a failed fetch to the page.
  vi.stubGlobal("fetch", async () => {
    throw new TypeError("Failed to fetch");
  });
  await expect(createGitlab("https://gl.test", "glpat-x", usedUp).releases("o/r")).rejects.toThrow(`Your GitLab token's limit of requests is used up until ${at}.`);
  await expect(createGitlab("https://gl.test", undefined, async () => ({ status: 429, retryAfter: 90 })).releases("o/r")).rejects.toThrow("Try again in 2 min.");
  // Offline, or not the limit: the page's own error stands.
  await expect(createGitlab("https://gl.test", undefined, async () => ({ status: 200, limit: 500, remaining: 499, reset })).releases("o/r")).rejects.toThrow(TypeError);
  await expect(
    createGitlab("https://gl.test", undefined, async () => {
      throw "Couldn't reach GitLab.";
    }).releases("o/r"),
  ).rejects.toThrow(TypeError);
  // A private project's 403 isn't the limit.
  vi.stubGlobal("fetch", async () => new Response("", { status: 403 }));
  await expect(createGitlab("https://gl.test", undefined, async () => ({ status: 200, limit: 500, remaining: 499, reset })).releases("o/r")).rejects.toThrow("GitLab answered 403. Try again later.");
});

test("says once when neither address answers, and not when the other one does", async () => {
  const lost: unknown[] = [];
  const fell: unknown[] = [];
  const fetch = (async () => {
    throw new TypeError("Failed to fetch");
  }) as typeof globalThis.fetch;
  const client = createClient(DEFAULT_API, { fetch, onUnreachable: (e) => lost.push(e), onFallback: (e) => fell.push(e) });
  await expect(client.facets()).rejects.toThrow("Failed to fetch");
  await expect(client.facets()).rejects.toThrow("Failed to fetch");
  expect(lost).toHaveLength(1);
  expect(fell).toHaveLength(0);

  const answered: unknown[] = [];
  const other = (async (url: string) => {
    if (url.startsWith(DEFAULT_API)) throw new TypeError("Failed to fetch");
    return new Response("{}", { status: 200 });
  }) as typeof globalThis.fetch;
  await createClient(DEFAULT_API, { fetch: other, onUnreachable: (e) => answered.push(e) }).facets();
  expect(answered).toHaveLength(0);
});
