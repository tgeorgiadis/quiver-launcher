import { describe, expect, test, vi } from "vitest";
import type { Asset, Entry, Release } from "@quiverlauncher/api";
import { cacheOf } from "./cache";
import { runCacheFetch, type FetchDeps } from "./cache-fetch";
import type { InstallRequest } from "./native";

const sum = (c: string) => `sha256:${c.repeat(64)}`;
const file = (name: string, checksum: string, size = 10): Asset => ({ id: name, url: `https://example.com/${name}`, filename: name, os: "windows", architecture: "x64", format: "zip", size, checksum });
const rel = (version: string, assets: Asset[]): Release => ({ id: version, version, releasedAt: 0, prerelease: false, assets });
const app = (id: string) => ({ id, slug: id, projectName: id.toUpperCase(), launcher: {} }) as unknown as Entry;

/** A catalog of apps, two to a page, and their releases; a cache that has `held`. */
function deps(apps: Entry[], releases: Record<string, Release[]>, held: string[] = [], bytes = 0) {
  const fetched: InstallRequest[] = [];
  const d: FetchDeps = {
    client: {
      apps: vi.fn(async ({ cursor }: { cursor?: string | null }) => {
        const at = Number(cursor ?? 0);
        const items = apps.slice(at, at + 2);
        return { items, nextCursor: String(at + 2), isDone: at + 2 >= apps.length };
      }),
      releases: vi.fn(async (slug: string, _limit?: number, cursor?: string | null) => {
        const all = releases[slug] ?? [];
        // One to a page, so Hoarder has to go through them.
        const at = Number(cursor ?? 0);
        return { items: all.slice(at, at + 1), nextCursor: String(at + 1), isDone: at + 1 >= all.length };
      }),
      app: vi.fn(async (slug: string) => ({ entry: apps.find((a) => a.slug === slug)! })),
    } as unknown as FetchDeps["client"],
    native: {
      cacheHas: async (_dir, checksums) => checksums.map((c) => held.includes(c)),
      cacheStatus: async () => ({ files: held.length, bytes, entries: [] }),
      cacheFetch: async (r) => void fetched.push(r),
    },
    entryOf: async ({ id }) => apps.find((a) => a.id === id),
    here: { os: "windows", arch: "x64" },
    protect: () => ["sha256:installed"],
  };
  return { d, fetched };
}

describe("a run of the cache's fetches", () => {
  const catalog = [app("a"), app("b"), app("c")];
  const releases = {
    a: [rel("2", [file("a2.zip", sum("1"))]), rel("1", [file("a1.zip", sum("2"))])],
    b: [rel("5", [file("b5.zip", sum("3"))])],
    c: [rel("9", [file("c9.zip", sum("4"))])],
  };

  test("Every latest: each app's newest file the cache doesn't have, across the catalog's pages", async () => {
    const { d, fetched } = deps(catalog, releases, [sum("3")]);
    const done = await runCacheFetch(cacheOf({ mode: "latest" }, "/cache"), d, () => {}, () => false);
    expect(fetched.map((r) => r.filename)).toEqual(["a2.zip", "c9.zip"]);
    expect(done).toMatchObject({ running: false, apps: 3, fetched: 2, failed: 0, noRoom: 0 });
    // Into the cache alone, checked against its checksum, with the installed files kept safe.
    expect(fetched[0]).toMatchObject({ checksum: sum("1"), version: "2", cache: { dir: "/cache", keep: true, app: "a", slug: "a", version: "2", protect: ["sha256:installed"] } });
  });

  test("Hoarder: every release of every app", async () => {
    const { d, fetched } = deps(catalog, releases);
    await runCacheFetch(cacheOf({ mode: "hoarder" }, "/cache"), d, () => {}, () => false);
    expect(fetched.map((r) => r.filename)).toEqual(["a2.zip", "a1.zip", "b5.zip", "c9.zip"]);
  });

  test("Selected titles: only those apps, looked up even when they aren't in the library", async () => {
    const { d, fetched } = deps(catalog, releases);
    await runCacheFetch(cacheOf({ mode: "selected", apps: [{ id: "c", slug: "c" }, { id: "gone", slug: "gone" }] }, "/cache"), d, () => {}, () => false);
    expect(fetched.map((r) => r.filename)).toEqual(["c9.zip"]);
    expect(d.client.apps).not.toHaveBeenCalled();
  });

  test("leaves out what wouldn't fit in the limit, rather than pushing out what's kept", async () => {
    const big = { a: [rel("2", [file("a2.zip", sum("1"), 2 ** 30)])], b: releases.b, c: releases.c };
    const { d, fetched } = deps(catalog, big, [], 2 ** 30 - 15);
    const done = await runCacheFetch(cacheOf({ mode: "latest", limitGb: 1 }, "/cache"), d, () => {}, () => false);
    // 15 bytes of room: b's 10-byte file fits, then c's doesn't.
    expect(fetched.map((r) => r.filename)).toEqual(["b5.zip"]);
    expect(done.noRoom).toBe(2);
  });

  test("lists each file with its app, version and name, and why one failed: one an antivirus took away says so", async () => {
    const { d } = deps(catalog, releases);
    const real = d.native.cacheFetch;
    d.native.cacheFetch = async (r) => (r.filename === "b5.zip" ? Promise.reject("blocked") : real(r));
    const earlier = { key: "old", entry: app("z"), version: "1", filename: "z.zip", state: "kept" as const, at: 1 };
    const done = await runCacheFetch(cacheOf({ mode: "latest" }, "/cache"), d, () => {}, () => false, [earlier]);
    expect(done.files.map((f) => [f.entry.id, f.version, f.filename, f.state, f.why])).toEqual([
      ["c", "9", "c9.zip", "kept", undefined],
      ["b", "5", "b5.zip", "failed", "blocked"],
      ["a", "2", "a2.zip", "kept", undefined],
      // Kept from the run before.
      ["z", "1", "z.zip", "kept", undefined],
    ]);
  });

  test("Installed only: the installed version's file, by its checksum, not the newest release's; asked for, never by itself", async () => {
    const both = { a: [rel("2", [file("a2.zip", sum("1"))]), rel("1", [file("a1.zip", sum("2")), { ...file("a1-linux.tar.gz", sum("5")), os: "linux" as const }])] };
    const { d, fetched } = deps(catalog, both);
    d.installed = () => [{ id: "a", slug: "a", version: "v1", checksum: sum("2") }];
    await runCacheFetch(cacheOf({ mode: "installed" }, "/cache"), d, () => {}, () => false);
    expect(fetched.map((r) => r.filename)).toEqual(["a1.zip"]);
    // Kept as its latest installed version alone.
    expect(fetched[0].cache).toMatchObject({ app: "a", version: "1", onlyThis: true });
    expect(d.client.apps).not.toHaveBeenCalled();
    // Keeping every system's files: the release's other builds too.
    const all = deps(catalog, both);
    all.d.installed = d.installed;
    await runCacheFetch(cacheOf({ mode: "installed", allPlatforms: true }, "/cache"), all.d, () => {}, () => false);
    expect(all.fetched.map((r) => r.filename)).toEqual(["a1.zip", "a1-linux.tar.gz"]);
  });

  test("asked for some apps alone, a run goes through only those", async () => {
    const { d, fetched } = deps(catalog, releases);
    d.installed = () => ["a", "b", "c"].map((id) => ({ id, slug: id, version: releases[id as "a"][0].version, checksum: releases[id as "a"][0].assets[0].checksum }));
    await runCacheFetch(cacheOf({ mode: "installed" }, "/cache"), d, () => {}, () => false, [], ["b"]);
    expect(fetched.map((r) => r.filename)).toEqual(["b5.zip"]);
  });

  test("Keep source code: the newest release's source once, no checksum, beside its builds; not again once kept", async () => {
    const { d, fetched } = deps([app("a"), app("b")], { a: releases.a, b: releases.b });
    d.projectOf = async (slug) => ({ provider: "github", repository: `dev/${slug}` });
    // b's source is kept already.
    const status = d.native.cacheStatus;
    d.native.cacheStatus = async (dir) => ({ ...(await status(dir)), entries: [{ hash: "x", filename: "b-5-source.zip", path: "b/5/b-5-source.zip", source: true, size: 1, apps: [{ app: "b", version: "5" }], cachedAt: 0, usedAt: 0 }] });
    await runCacheFetch(cacheOf({ mode: "latest", sources: true }, "/cache"), d, () => {}, () => false);
    expect(fetched.map((r) => r.filename)).toEqual(["a-2-source.zip", "a2.zip", "b5.zip"]);
    expect(fetched[0]).toMatchObject({ url: "https://github.com/dev/a/archive/refs/tags/2.zip", version: "2", cache: { app: "a", slug: "a", source: true } });
    expect(fetched[0].checksum).toBeUndefined();
  });

  test("a file that fails counts, and the run goes on; being stopped ends it", async () => {
    const { d, fetched } = deps(catalog, releases);
    const real = d.native.cacheFetch;
    d.native.cacheFetch = async (r) => (r.filename === "a2.zip" ? Promise.reject("mismatch") : real(r));
    let stop = false;
    const seen: string[] = [];
    const done = await runCacheFetch(
      cacheOf({ mode: "latest" }, "/cache"),
      d,
      (s) => {
        if (s.current?.filename) seen.push(s.current.filename);
        if (s.current?.filename === "b5.zip") stop = true;
      },
      () => stop,
    );
    expect(fetched.map((r) => r.filename)).toEqual(["b5.zip"]);
    expect(done).toMatchObject({ failed: 1, fetched: 1, running: false });
    expect(seen).toEqual(["a2.zip", "b5.zip"]);
  });
});
