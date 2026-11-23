import { beforeEach, describe, expect, test, vi } from "vitest";
import type { Asset, Entry, Release } from "@quiverlauncher/api";
import { cacheOf } from "./cache";
import { forgetSettled, runCacheFetch, type FetchDeps } from "./cache-fetch";
import type { CachedFile, InstallRequest } from "./native";

const sum = (c: string) => `sha256:${c.repeat(64)}`;
const file = (name: string, checksum: string, size = 10): Asset => ({ id: name, url: `https://example.com/${name}`, filename: name, os: "windows", architecture: "x64", format: "zip", size, checksum });
const rel = (version: string, assets: Asset[]): Release => ({ id: version, version, releasedAt: 0, prerelease: false, assets });
const app = (id: string) => ({ id, slug: id, projectName: id.toUpperCase(), launcher: {} }) as unknown as Entry;

/** A catalog of apps, two to a page, and their releases; a cache that has `held`. */
function deps(apps: Entry[], releases: Record<string, Release[]>, held: string[] = [], bytes = 0, entries: CachedFile[] = []) {
  const fetched: InstallRequest[] = [];
  const removed: string[][] = [];
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
      cacheStatus: async () => ({ files: held.length, bytes, entries }),
      cacheFetch: async (r) => void fetched.push(r),
      cacheRemove: async (_dir, hashes) => void removed.push(hashes ?? []),
    },
    entryOf: async ({ id }) => apps.find((a) => a.id === id),
    here: { os: "windows", arch: "x64" },
    protect: () => ["sha256:installed"],
  };
  return { d, fetched, removed };
}

// Each test is a launcher just started: nothing is known to be up to date yet.
beforeEach(() => forgetSettled());

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

  test("Every latest and Selected titles: a newer version takes the place of the app's older ones, even in a full cache", async () => {
    const kept = (hash: string, app: string, version: string, size: number): CachedFile => ({ hash, filename: `${app}${version}.zip`, path: "", size, apps: [{ app, version }], cachedAt: 0, usedAt: 0 });
    const hex = (c: string) => c.repeat(64);
    // Full: a's version 1 and b's version 5, the newest, and a file of a's that b shares.
    const entries = [kept(hex("2"), "a", "1", 10), kept(hex("3"), "b", "5", 10), { ...kept(hex("7"), "a", "0", 10), apps: [{ app: "a", version: "0" }, { app: "b", version: "4" }] }];
    const full = 30;
    for (const mode of ["latest", "selected"] as const) {
      const { d, fetched, removed } = deps(catalog, releases, [sum("3")], full, entries);
      const cache = cacheOf({ mode, limitGb: full / 1024 ** 3, apps: [{ id: "a", slug: "a" }] }, "/cache");
      const done = await runCacheFetch(cache, d, () => {}, () => false);
      expect(fetched[0].filename).toBe("a2.zip");
      // Room for it while the old one is still there, so nothing else is rotated out meanwhile.
      expect(fetched[0].cache?.limit).toBe(full + 10);
      // Then the old one goes; never the file b shares.
      expect(removed[0]).toEqual([hex("2")]);
      expect(done.fetched).toBeGreaterThan(0);
    }
  });

  test("Every latest never replaces the file of a version installed now", async () => {
    const entries: CachedFile[] = [{ hash: "2".repeat(64), filename: "a1.zip", path: "", size: 10, apps: [{ app: "a", version: "1" }], cachedAt: 0, usedAt: 0 }];
    const { d, fetched, removed } = deps([app("a")], releases, [], 10, entries);
    d.protect = () => [sum("2")];
    const done = await runCacheFetch(cacheOf({ mode: "latest", limitGb: 10 / 1024 ** 3 }, "/cache"), d, () => {}, () => false);
    expect(fetched).toEqual([]);
    expect(removed).toEqual([]);
    expect(done.noRoom).toBe(1);
  });

  test("Hoarder keeps every version", async () => {
    const entries: CachedFile[] = [{ hash: "2".repeat(64), filename: "a1.zip", path: "", size: 10, apps: [{ app: "a", version: "1" }], cachedAt: 0, usedAt: 0 }];
    const { d, removed } = deps([app("a")], releases, [sum("2")], 10, entries);
    await runCacheFetch(cacheOf({ mode: "hoarder" }, "/cache"), d, () => {}, () => false);
    expect(removed).toEqual([]);
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

  test("each app's README is kept as the site has it now, as the run goes through it", async () => {
    const { d } = deps(catalog, releases);
    const readmes: string[] = [];
    d.keepReadme = async (entry) => void readmes.push(entry.id);
    await runCacheFetch(cacheOf({ mode: "latest" }, "/cache"), d, () => {}, () => false);
    expect(readmes).toEqual(["a", "b", "c"]);
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

describe("apps with nothing newer than what's kept", () => {
  const listed = (id: string, version: string, rolling = false) => ({ ...app(id), verified: { version, releasedAt: 0, prerelease: false, pinned: true, ...(rolling && { rolling }) } }) as Entry;
  const kept = (hash: string, id: string, version: string): CachedFile => ({ hash, filename: `${id}${version}.zip`, path: "", size: 10, apps: [{ app: id, version }], cachedAt: 0, usedAt: 0 });
  const releases = {
    a: [rel("2", [file("a2.zip", sum("1"))])],
    b: [rel("v5", [file("b5.zip", sum("3"))])],
    c: [rel("9", [file("c9.zip", sum("4"))])],
  };
  const asked = (d: FetchDeps) => vi.mocked(d.client.releases).mock.calls.map(([slug]) => slug);

  test("Every latest: an app whose listed release is kept already isn't asked for its releases, from the first run", async () => {
    // a's newest is kept, b's is kept under the other spelling of its version, c's isn't.
    const { d, fetched } = deps([listed("a", "2"), listed("b", "5"), listed("c", "9")], releases, [sum("1"), sum("3")], 20, [kept("1".repeat(64), "a", "2"), kept("3".repeat(64), "b", "v5")]);
    const done = await runCacheFetch(cacheOf({ mode: "latest" }, "/cache"), d, () => {}, () => false);
    expect(asked(d)).toEqual(["c"]);
    expect(fetched.map((r) => r.filename)).toEqual(["c9.zip"]);
    expect(done).toMatchObject({ apps: 3, fetched: 1 });
  });

  test("Every latest: a newer release listed has the app's releases read again", async () => {
    const { d, fetched } = deps([listed("a", "3")], { a: [rel("3", [file("a3.zip", sum("5"))]), ...releases.a] }, [sum("1")], 10, [kept("1".repeat(64), "a", "2")]);
    await runCacheFetch(cacheOf({ mode: "latest" }, "/cache"), d, () => {}, () => false);
    expect(asked(d)).toEqual(["a"]);
    expect(fetched.map((r) => r.filename)).toEqual(["a3.zip"]);
  });

  test("a rolling release, whose files change under one version, is asked for every run", async () => {
    const { d } = deps([listed("a", "2", true)], releases, [sum("1")], 10, [kept("1".repeat(64), "a", "2")]);
    const cache = cacheOf({ mode: "latest" }, "/cache");
    await runCacheFetch(cache, d, () => {}, () => false);
    await runCacheFetch(cache, d, () => {}, () => false);
    expect(asked(d)).toEqual(["a", "a"]);
  });

  test("Hoarder and every system's builds: asked for once a launcher, then only when a newer release is listed", async () => {
    for (const settings of [{ mode: "hoarder" }, { mode: "latest", allPlatforms: true }] as const) {
      forgetSettled();
      const apps = [listed("a", "2"), app("b")];
      const { d } = deps(apps, releases, [sum("1"), sum("3")], 20, [kept("1".repeat(64), "a", "2")]);
      const cache = cacheOf(settings, "/cache");
      await runCacheFetch(cache, d, () => {}, () => false);
      // The file kept doesn't say every release, or every system's build, is: the first run reads them.
      expect(asked(d), settings.mode).toEqual(["a", "b"]);
      await runCacheFetch(cache, d, () => {}, () => false);
      expect(asked(d), settings.mode).toEqual(["a", "b"]);
      apps[0] = listed("a", "3");
      await runCacheFetch(cache, d, () => {}, () => false);
      expect(asked(d), settings.mode).toEqual(["a", "b", "a"]);
    }
  });

  test("an app whose file didn't download, or didn't fit, is asked for again next run", async () => {
    const { d } = deps([listed("c", "9")], releases);
    d.native.cacheFetch = async () => Promise.reject("failed");
    const cache = cacheOf({ mode: "hoarder" }, "/cache");
    await runCacheFetch(cache, d, () => {}, () => false);
    await runCacheFetch(cache, d, () => {}, () => false);
    expect(asked(d)).toEqual(["c", "c"]);
  });

  test("Selected titles go by what the launcher knew of an app, so they're always asked for", async () => {
    const { d } = deps([listed("a", "2")], releases, [sum("1")], 10, [kept("1".repeat(64), "a", "2")]);
    const cache = cacheOf({ mode: "selected", apps: [{ id: "a", slug: "a" }] }, "/cache");
    await runCacheFetch(cache, d, () => {}, () => false);
    await runCacheFetch(cache, d, () => {}, () => false);
    expect(asked(d)).toEqual(["a", "a"]);
  });
});
