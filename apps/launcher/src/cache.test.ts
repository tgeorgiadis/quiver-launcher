import { describe, expect, test } from "vitest";
import type { Asset, Release } from "@quiverlauncher/api";
import { DEFAULT_LIMIT_GB, cacheNames, cacheOf, cachedReleases, orphans, sourceArchive, cacheTargets, cacheUse, fits, installedTargets, isSelected, protectedFiles, sameVersion, withSelected } from "./cache";

const GB = 1024 ** 3;
const sum = (c: string) => `sha256:${c.repeat(64)}`;
const asset = (filename: string, os: Asset["os"], checksum?: string, size = 100): Asset => ({ id: filename, url: `https://example.com/${filename}`, filename, os, architecture: "x64", format: "zip", size, ...(checksum && { checksum }) });
const release = (version: string, assets: Asset[]): Release => ({ id: version, version, releasedAt: 0, prerelease: false, assets });
const entry = { id: "app", slug: "app", projectName: "The App", launcher: {} as never };

describe("the settings", () => {
  test("off, in the data folder, with a 50 GB limit, until the player says otherwise", () => {
    expect(cacheOf(undefined, "/data/cache")).toEqual({ mode: "none", on: false, dir: "/data/cache", limit: DEFAULT_LIMIT_GB * GB, limitGb: 50, allPlatforms: false, sources: false, apps: [] });
    expect(cacheOf({ mode: "hoarder", dir: " /big/drive ", limitGb: null, allPlatforms: true }, "/data/cache")).toMatchObject({ mode: "hoarder", on: true, dir: "/big/drive", limit: undefined, allPlatforms: true });
    expect(cacheOf({ mode: "sparkles" as never, limitGb: 5 }, "/c")).toMatchObject({ mode: "none", limit: 5 * GB });
  });

  test("selected titles are kept by id, with the slug to find them by", () => {
    let saved = withSelected(undefined, { id: "a", slug: "app-a" }, true);
    saved = withSelected(saved, { id: "b", slug: "app-b" }, true);
    saved = withSelected(saved, { id: "a", slug: "app-a" }, false);
    expect(saved.apps).toEqual([{ id: "b", slug: "app-b" }]);
    expect(isSelected(cacheOf(saved, "/c"), "b")).toBe(true);
    expect(isSelected(cacheOf(saved, "/c"), "a")).toBe(false);
  });
});

describe("what an install asks of the cache", () => {
  const protect = ["sha256:x"];
  const app = { id: "a", version: "1.0", os: "windows" as const };
  test("nothing while it's off", () => {
    expect(cacheUse(cacheOf(undefined, "/c"), app, protect)).toBeUndefined();
  });
  test("Installed only keeps each app's latest file alone", () => {
    expect(cacheUse(cacheOf({ mode: "installed", limitGb: 1 }, "/c"), app, protect)).toEqual({ dir: "/c", keep: true, app: "a", version: "1.0", os: "windows", onlyThis: true, limit: GB, protect });
  });
  test("every latest and Hoarder keep installs too, without a limit when there's none", () => {
    expect(cacheUse(cacheOf({ mode: "latest", limitGb: null }, "/c"), app, protect)).toEqual({ dir: "/c", keep: true, app: "a", version: "1.0", os: "windows", protect });
  });
  test("Selected titles keeps only those apps' files, but takes any from the cache", () => {
    const cache = cacheOf({ mode: "selected", apps: [{ id: "b", slug: "b" }] }, "/c");
    expect(cacheUse(cache, app, protect)?.keep).toBe(false);
    expect(cacheUse(cache, { ...app, id: "b" }, protect)?.keep).toBe(true);
  });
  test("the installed versions' files are never rotated out", () => {
    expect(protectedFiles({ a: { checksum: "sha256:1" }, b: {}, c: { checksum: "sha256:1" }, d: { checksum: "sha256:2" } })).toEqual(["sha256:1", "sha256:2"]);
  });
});

describe("the files a mode keeps of an app", () => {
  const releases = [
    release("2.0", [asset("app-win.zip", "windows", sum("a")), asset("app-linux.tar.gz", "linux", sum("b")), asset("checksums.txt", "unknown", sum("c")), asset("app-mac.zip", "macos")]),
    release("1.0", [asset("app-win-1.zip", "windows", sum("d"))]),
  ];
  const here = { os: "windows" as const, arch: "x64" };
  const names = (mode: "latest" | "hoarder", allPlatforms = false) => cacheTargets(entry, releases, cacheOf({ mode, allPlatforms }, "/c"), here).map((t) => `${t.version} ${t.asset.filename}`);

  test("the newest release's file for this computer", () => {
    expect(names("latest")).toEqual(["2.0 app-win.zip"]);
  });
  test("Hoarder: every release's", () => {
    expect(names("hoarder")).toEqual(["2.0 app-win.zip", "1.0 app-win-1.zip"]);
  });
  test("every system's builds, but not checksum files, nor a file without a checksum", () => {
    expect(names("latest", true)).toEqual(["2.0 app-win.zip", "2.0 app-linux.tar.gz"]);
  });
  test("an app with no release, or none for this computer, has none", () => {
    expect(cacheTargets(entry, [], cacheOf({ mode: "latest" }, "/c"), here)).toEqual([]);
    expect(cacheTargets(entry, releases, cacheOf({ mode: "latest" }, "/c"), { os: "android", arch: "arm64" })).toEqual([]);
  });
});

test("a fetch only takes what fits, never pushing out what's kept", () => {
  expect(fits({ size: 40 }, 60, 100)).toBe(true);
  expect(fits({ size: 41 }, 60, 100)).toBe(false);
  expect(fits({ size: 10 ** 15 }, 0, undefined)).toBe(true);
});

describe("Installed only's files of an installed app", () => {
  const release = { id: "1", version: "1.0", releasedAt: 0, prerelease: false, assets: [
    { id: "w", url: "u", filename: "app-win.zip", os: "windows", architecture: "x64", format: "zip", checksum: `sha256:${"a".repeat(64)}` },
    { id: "w2", url: "u", filename: "app-win-portable.zip", os: "windows", architecture: "x64", format: "zip", checksum: `sha256:${"b".repeat(64)}` },
    { id: "l", url: "u", filename: "app-linux.tar.gz", os: "linux", architecture: "x64", format: "tar.gz", checksum: `sha256:${"c".repeat(64)}` },
  ] } as Release;
  const app = { id: "app", slug: "app", projectName: "App", launcher: {} as never };
  const here = { os: "windows" as const, arch: "x64" };
  const names = (installed: { checksum?: string }, allPlatforms = false) => installedTargets(app, release, installed, cacheOf({ mode: "installed", allPlatforms }, "/c"), here).map((t) => t.asset.filename);
  test("the very file installed, though the release has others for this computer", () => {
    expect(names({ checksum: `sha256:${"b".repeat(64)}` })).toEqual(["app-win-portable.zip"]);
  });
  test("every system's builds when those are kept", () => {
    expect(names({ checksum: `sha256:${"b".repeat(64)}` }, true)).toEqual(["app-win.zip", "app-win-portable.zip", "app-linux.tar.gz"]);
  });
  test("without its checksum, the file an install picks here; none when the file installed isn't the release's any more", () => {
    expect(names({})).toEqual(["app-win.zip"]);
    expect(names({ checksum: "sha256:gone" })).toEqual([]);
  });
  test("a version reads the same with its v or without", () => {
    expect(sameVersion("v1.2", "1.2")).toBe(true);
    expect(sameVersion("1.2", "1.20")).toBe(false);
  });
});

test("an app's files go in its slug's folder, the selected titles' and the library's named for it", () => {
  expect(cacheUse(cacheOf({ mode: "installed" }, "/c"), { id: "k17", version: "1", slug: "ship-of-harkinian" }, [])?.slug).toBe("ship-of-harkinian");
  expect(cacheNames({ a: { slug: "app-a" }, own: { slug: "" } }, [{ id: "b", slug: "app-b" }])).toEqual({ a: "app-a", b: "app-b" });
});

describe("orphans: what Clear orphans takes", () => {
  const f = (hash: string, ...apps: string[]) => ({ hash, apps: apps.map((app) => ({ app })) });
  const files = [f("a1", "a"), f("b1", "b"), f("shared", "a", "c"), f("c1", "c"), f("installed-c", "c")];
  const hashes = (list: { hash: string }[]) => list.map((x) => x.hash);
  test("Selected titles: the files of apps no longer selected, not one a selected app shares", () => {
    expect(hashes(orphans(files, cacheOf({ mode: "selected", apps: [{ id: "a", slug: "a" }] }, "/c"), [], []))).toEqual(["b1", "c1", "installed-c"]);
  });
  test("Installed only: the files of apps not installed now", () => {
    expect(hashes(orphans(files, cacheOf({ mode: "installed" }, "/c"), ["b"], []))).toEqual(["a1", "shared", "c1", "installed-c"]);
  });
  test("never a file of a version installed now", () => {
    expect(hashes(orphans(files, cacheOf({ mode: "selected", apps: [] }, "/c"), [], [`sha256:INSTALLED-C`.replace("INSTALLED-C", "installed-c")]))).not.toContain("installed-c");
  });
  test("nothing in the modes that keep the whole catalog, nor while off", () => {
    for (const mode of ["latest", "hoarder", "none"] as const) expect(orphans(files, cacheOf({ mode }, "/c"), [], [])).toEqual([]);
  });
});

describe("an app's versions from the cache, for installing offline", () => {
  const file = (hash: string, filename: string, version: string, os: string, at: number, more: { arch?: string; source?: boolean; app?: string } = {}) => ({
    hash: hash.repeat(64), filename, path: "", size: 10, source: more.source, cachedAt: at, usedAt: at,
    apps: [{ app: more.app ?? "duke", version, os, ...(more.arch && { arch: more.arch }) }],
  });
  const files = [
    file("a", "Duke-1.0-win.zip", "1.0", "windows", 1, { arch: "x64" }),
    file("b", "Duke-1.1-win.zip", "1.1", "windows", 5, { arch: "x64" }),
    file("c", "Duke-1.1-linux.tar.gz", "1.1", "linux", 4),
    file("d", "duke-1.1-source.zip", "1.1", "unknown", 6, { source: true }),
    file("e", "Other.zip", "9", "windows", 9, { app: "other" }),
  ];
  test("newest kept first, each file checked by its SHA-256, never its source code nor another app's", () => {
    const releases = cachedReleases(files, "duke");
    expect(releases.map((r) => [r.version, r.assets.map((a) => a.filename)])).toEqual([
      ["1.1", ["Duke-1.1-win.zip", "Duke-1.1-linux.tar.gz"]],
      ["1.0", ["Duke-1.0-win.zip"]],
    ]);
    expect(releases[0].assets[0]).toMatchObject({ checksum: `sha256:${"b".repeat(64)}`, os: "windows", architecture: "x64", url: "" });
    // One kept before architectures were: any, as the catalog's unknown is.
    expect(releases[0].assets[1].architecture).toBe("unknown");
    expect(cachedReleases(files, "nothing")).toEqual([]);
  });
});

describe("a release's source code, as its host archives its tag", () => {
  test("GitHub, GitLab, Gitea/Forgejo and Bitbucket, named <repository>-<tag>-source.zip", () => {
    expect(sourceArchive({ provider: "github", repository: "TwilitRealm/dusklight" }, "v2.0.3")).toEqual({
      url: "https://github.com/TwilitRealm/dusklight/archive/refs/tags/v2.0.3.zip",
      filename: "dusklight-v2.0.3-source.zip",
    });
    expect(sourceArchive({ provider: "gitlab", repository: "group/sub/game" }, "1.0")?.url).toBe("https://gitlab.com/group/sub/game/-/archive/1.0/game-1.0.zip");
    expect(sourceArchive({ provider: "gitlab", repository: "a/b", host: "https://git.example.org/" }, "1.0")?.url).toBe("https://git.example.org/a/b/-/archive/1.0/b-1.0.zip");
    expect(sourceArchive({ provider: "gitea", repository: "a/b", host: "codeberg.org" }, "v1")?.url).toBe("https://codeberg.org/a/b/archive/v1.zip");
    expect(sourceArchive({ provider: "bitbucket", repository: "a/b" }, "v1")?.url).toBe("https://bitbucket.org/a/b/get/v1.zip");
  });
  test("a tag is made safe in the address and the name", () => {
    expect(sourceArchive({ provider: "github", repository: "a/b" }, "release/1.0 beta")).toEqual({
      url: "https://github.com/a/b/archive/refs/tags/release%2F1.0%20beta.zip",
      filename: "b-release_1.0_beta-source.zip",
    });
  });
  test("none for a release feed, a Gitea with no host, or no repository", () => {
    expect(sourceArchive({ provider: "feed", repository: "https://example.com/feed.json" }, "1")).toBeUndefined();
    expect(sourceArchive({ provider: "gitea", repository: "a/b" }, "1")).toBeUndefined();
    expect(sourceArchive({ provider: "github" }, "1")).toBeUndefined();
    expect(sourceArchive({ provider: "github", repository: "a/b" }, " ")).toBeUndefined();
  });
});
