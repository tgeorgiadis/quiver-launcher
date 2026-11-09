import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { adoptedInstall, appRef, appsFolders, groupFolder, joinPath, rootOf, unsortedApps, folderClash, keptWhereTheyAre, ownedBy, bringsAlong, catalogMatch, declinedUpdate, filesAddable, folderGone, forHereKey, hasUpdate, importedPlaylists, importedTags, markedInApps, installFiles, keptInstall, locatable, looseInstalls, mayUpdate, movableApps, movedInside, noProgram, playedRef, relocatedInstall, skipped, uniqueFiles, updateFor, type ImportedApp, type LibraryItem } from "./store";
import type { OldApp, OldFilter } from "./native";

const entry = {
  id: "port",
  slug: "test-port",
  launcher: { folderName: "TestPort", preferredExecutables: { linux: ["port.sh"] } },
  verified: { version: "v1.2.0", releasedAt: 20 },
} as unknown as Entry;

describe("locating an install", () => {
  const found = { dir: "/new/TestPort", version: "v2.0.0", folder: "TestPort", executables: ["port.sh"] };
  const gone = { error: "This app's folder isn't there any more." };

  it("is offered with no install here, or one that lost its folder", () => {
    expect(locatable("port", undefined, undefined)).toBe(true);
    expect(locatable("port", found, undefined)).toBe(false);
    expect(locatable("port", found, { error: "The download failed." })).toBe(false);
    expect(folderGone(gone)).toBe(true);
    expect(locatable("port", found, gone)).toBe(true);
  });

  it("knows when no program was found, which lets the player choose one", () => {
    expect(noProgram({ error: "Couldn't find a program to start in this app's folder." })).toBe(true);
    expect(noProgram(gone)).toBe(false);
    expect(noProgram(undefined)).toBe(false);
  });

  it("isn't offered during a download, or for the player's own files", () => {
    expect(locatable("port", undefined, { id: "port", phase: "downloading", received: 0 } as never)).toBe(false);
    expect(locatable("port", { ...found, local: true }, gone)).toBe(false);
  });

  it("keeps what the player chose, and nothing about the copy that's gone", () => {
    const launch = { command: "--fullscreen" };
    const now = { version: "v1.0.0", folder: "Old", executables: [], wine: true as const, checksum: "sha256:a", unverified: true as const, installer: "msi" as const, program: "/old/port", playTime: 60, updates: "pinned" as const, launch };
    expect(relocatedInstall(now, found)).toEqual({ ...found, playTime: 60, updates: "pinned", launch });
    expect(relocatedInstall(undefined, found)).toEqual(found);
  });
});

describe("adoptedInstall", () => {
  it("keeps a Quiver Launcher 3 install where it is", () => {
    expect(adoptedInstall({ name: "Test Port", provider: "github", dir: "/old/Apps/TestPort", version: "v1.2.0" }, entry, "linux")).toEqual({
      dir: "/old/Apps/TestPort",
      version: "v1.2.0",
      folder: "TestPort",
      executables: ["port.sh"],
    });
  });

  it("brings one 3.x didn't know the version of up to date", () => {
    const install = adoptedInstall({ name: "Test Port", provider: "github", dir: "/old/Apps/TestPort", version: "0.0.0" }, entry, "linux");
    expect(install?.version).toBe("0.0.0");
    expect(mayUpdate(entry, install)).toBe(true);
  });

  it("is offered only newer releases, by version number, as it has no release date", () => {
    const at = (version: string) => adoptedInstall({ name: "Test Port", provider: "github", dir: "/old/Apps/TestPort", version }, entry, "linux");
    expect(mayUpdate(entry, at("v1.1.9"))).toBe(true);
    expect(mayUpdate(entry, at("v1.10.0"))).toBe(false);
    // Unverified in 3.x, newer than the verified one: never an update back to it.
    expect(mayUpdate({ ...entry, verified: { version: "v0.1.6", releasedAt: 20 } } as unknown as Entry, at("v0.1.7"))).toBe(false);
  });

  it("starts the program a Windows installer put on the computer", () => {
    const old = { name: "Test Port", provider: "github", dir: "C:\\Old\\Apps\\TestPort", version: "v1.0", program: "C:\\Program Files\\Port\\port.exe" };
    expect(adoptedInstall(old, entry, "windows")).toMatchObject({ installer: "msi", program: old.program, executables: [] });
  });

  it("has nothing to keep for an app 3.x hadn't installed", () => {
    expect(adoptedInstall({ name: "Test Port", provider: "github" }, entry, "linux")).toBeUndefined();
  });
});

describe("catalogMatch", () => {
  // One repository's two games: the catalog's list keeps the last for the repository.
  const ids = new Map([
    ["github:mstan/rubysapphire", { id: "sapphire" }],
    ["id:ruby", { id: "ruby" }],
    ["id:sapphire", { id: "sapphire" }],
  ]);
  const old = { name: "Pokemon Ruby", provider: "github", repository: "MStan/RubySapphire" };

  it("tells apart the apps of one repository by the entry 3.x knew", () => {
    expect(catalogMatch({ ...old, catalogId: "ruby" }, ids)?.id).toBe("ruby");
    expect(catalogMatch({ ...old, catalogId: "sapphire" }, ids)?.id).toBe("sapphire");
  });

  it("goes by the repository when 3.x's entry isn't known or isn't in the catalog now", () => {
    expect(catalogMatch(old, ids)?.id).toBe("sapphire");
    expect(catalogMatch({ ...old, catalogId: "gone" }, ids)?.id).toBe("sapphire");
    expect(catalogMatch({ name: "Other", provider: "github", repository: "someone/other" }, ids)).toBeUndefined();
  });
});

describe("keptInstall", () => {
  const adopted = { dir: "/old/Apps/Ruby", version: "v1.0", folder: "Ruby", executables: [] };

  it("takes 3.x's install for an app with none", () => {
    expect(keptInstall(undefined, adopted)).toBe(adopted);
  });

  it("leaves one downloaded here since, or updated from 3.x's where it is", () => {
    const downloaded = { version: "v2.0", folder: "ruby", executables: [] };
    expect(keptInstall(downloaded, adopted)).toBe(downloaded);
    const updated = { ...adopted, version: "v2.0" };
    expect(keptInstall(updated, adopted)).toBe(updated);
  });

  it("corrects another of 3.x's folders, keeping what the player did here", () => {
    const wrong = { dir: "/old/Apps/Sapphire", version: "v1.1", folder: "Ruby", executables: [], playTime: 60 };
    expect(keptInstall(wrong, adopted)).toEqual({ ...adopted, playTime: 60 });
  });
});

describe("looseInstalls", () => {
  const item = (id: string, more: Partial<LibraryItem> = {}) => ({ id, slug: id, addedAt: 1, ...more }) as LibraryItem;
  const ids = new Map([["id:port", { id: "port" }]]);
  const old = { name: "Test Port", provider: "github", repository: "quiver/test-port", catalogId: "port", dir: "/old/Apps/TestPort", version: "v1.0" };
  const catalog = { port: entry };

  it("finds 3.x's install of an app in the library that has none here", () => {
    expect(looseInstalls([old], ids, [item("port")], {}, catalog, "linux")).toEqual({ port: { dir: old.dir, version: "v1.0", folder: "TestPort", executables: ["port.sh"] } });
  });

  it("finds one of an app that came over as a custom app", () => {
    const custom = "github:quiver/test-port";
    expect(Object.keys(looseInstalls([old], ids, [item(custom)], {}, { [custom]: entry }, "linux"))).toEqual([custom]);
  });

  it("leaves apps installed here, out of the library or removed from it, and those 3.x hadn't installed", () => {
    const here = { port: { version: "v2.0", folder: "port", executables: [] } };
    expect(looseInstalls([old], ids, [item("port")], here, catalog, "linux")).toEqual({});
    expect(looseInstalls([old], ids, [], {}, catalog, "linux")).toEqual({});
    expect(looseInstalls([old], ids, [item("port", { removed: true })], {}, catalog, "linux")).toEqual({});
    expect(looseInstalls([{ ...old, dir: undefined, version: undefined }], ids, [item("port")], {}, catalog, "linux")).toEqual({});
  });
});

describe("importedTags", () => {
  const listed = { ...entry, tags: ["recreation", "Mod Support"], consoles: ["n64"] } as unknown as Entry;
  const old = (tags: string[], catalogTags?: string[]) => ({ name: "Test Port", provider: "github", tags, catalogTags });

  it("brings over the player's tags, not the catalog's", () => {
    expect(importedTags(old(["n64", "recreation", "favourite", "co-op"], ["co-op"]), listed)).toEqual(["favourite"]);
  });

  it("knows the catalog's from the entry when 3.x didn't keep them", () => {
    expect(importedTags(old(["mod support", "N64", "favourite"]), listed)).toEqual(["favourite"]);
  });

  it("keeps them as the tag field does: once each, at most 20, each up to 50 letters", () => {
    expect(importedTags(old(["Mine", "mine", " spaced "]), listed)).toEqual(["Mine", "spaced"]);
    expect(importedTags(old(Array.from({ length: 25 }, (_, i) => `tag ${i}`)), listed)).toHaveLength(20);
    expect(importedTags(old(["x".repeat(60)]), listed)).toEqual(["x".repeat(50)]);
  });

  it("has none for an app without tags", () => {
    expect(importedTags({ name: "Test Port", provider: "github" }, listed)).toEqual([]);
  });
});

describe("importedPlaylists", () => {
  const filter = (name: string, tags: string[], more: Partial<OldFilter> = {}): OldFilter => ({ name, tags, all: false, exclude: [], excludeAll: false, ...more });
  // As 3.x tagged them, and as they're tagged here: the "n64" tag is a console here, so it isn't one of their tags.
  const apps: ImportedApp[] = [
    { id: "a", old: ["recomp", "n64"], tags: ["recomp"] },
    { id: "b", old: ["n64", "done"], tags: ["done"] },
    { id: "c", old: ["port", "done", "broken"], tags: ["port", "done", "broken"] },
  ];

  it("keeps a filter for any of its tags following them", () => {
    expect(importedPlaylists([filter("Finished", ["Done"])], apps)).toEqual([{ name: "Finished", tags: ["Done"] }]);
    // Showing nothing yet, it still finds them as they're tagged.
    expect(importedPlaylists([filter("Later", ["wishlist"])], apps)).toEqual([{ name: "Later", tags: ["wishlist"] }]);
  });

  it("picks the apps it showed that its tags don't find here", () => {
    expect(importedPlaylists([filter("N64", ["n64", "recomp"])], apps)).toEqual([{ name: "N64", tags: ["n64", "recomp"], apps: ["b"] }]);
  });

  it("picks the apps any other filter showed", () => {
    expect(importedPlaylists([filter("Both", ["n64", "done"], { all: true })], apps)).toEqual([{ name: "Both", tags: [], apps: ["b"] }]);
    expect(importedPlaylists([filter("Working", ["done"], { exclude: ["broken"] })], apps)).toEqual([{ name: "Working", tags: [], apps: ["b"] }]);
    expect(importedPlaylists([filter("Not ports", [], { exclude: ["port", "broken"], excludeAll: true })], apps)).toEqual([{ name: "Not ports", tags: [], apps: ["a", "b"] }]);
    // An app tagged here with a tag it had in 3.x no longer: the filter's tags would find more than it showed.
    const retagged = [...apps, { id: "d", old: [], tags: ["done"] }];
    expect(importedPlaylists([filter("Finished", ["done"])], retagged)).toEqual([{ name: "Finished", tags: [], apps: ["b", "c"] }]);
  });

  it("leaves out one that showed nothing it can keep", () => {
    expect(importedPlaylists([filter("Nothing", ["n64", "port"], { all: true })], apps)).toEqual([]);
  });

  it("filters by platform for 3.x's platform tags", () => {
    const onPlatforms: ImportedApp[] = [
      { id: "a", old: ["android", "recomp"], tags: ["recomp"], platforms: ["windows", "android"] },
      { id: "b", old: ["Linux"], tags: [], platforms: ["linux"] },
      { id: "c", old: ["recomp"], tags: ["recomp"], platforms: ["windows"] },
    ];
    expect(importedPlaylists([filter("Phone", ["android"])], onPlatforms)).toEqual([{ name: "Phone", tags: [], platforms: ["android"] }]);
    expect(importedPlaylists([filter("Not Windows", ["android", "linux", "mac"])], onPlatforms)).toEqual([
      { name: "Not Windows", tags: [], platforms: ["android", "linux", "macos"] },
    ]);
    // All of a platform's tag and another: both filters.
    expect(importedPlaylists([filter("Phone recomps", ["android", "recomp"], { all: true })], onPlatforms)).toEqual([
      { name: "Phone recomps", tags: ["recomp"], platforms: ["android"] },
    ]);
    // Any of them is either, which a platform and a tag can't say: by its tags, as here, or picked.
    expect(importedPlaylists([filter("Either", ["android", "recomp"])], onPlatforms)).toEqual([{ name: "Either", tags: ["android", "recomp"] }]);
    expect(importedPlaylists([filter("Either", ["linux", "recomp"])], onPlatforms)).toEqual([{ name: "Either", tags: ["linux", "recomp"], apps: ["b"] }]);
  });

  it("filters by the tag when the platform would find apps the tag didn't", () => {
    const tagged: ImportedApp[] = [
      { id: "a", old: ["windows"], tags: ["windows"], platforms: ["windows"] },
      { id: "b", old: [], tags: [], platforms: ["windows"] },
    ];
    expect(importedPlaylists([filter("PC", ["windows"])], tagged)).toEqual([{ name: "PC", tags: ["windows"] }]);
  });

  it("names it as 3.x did, as long as a playlist's name can be", () => {
    expect(importedPlaylists([filter("x".repeat(70), ["done"])], apps)[0].name).toBe("x".repeat(60));
  });
});

describe("bringsAlong", () => {
  const app = (more: Partial<OldApp> = {}): OldApp => ({ name: "Test Port", provider: "github", repository: "quiver/test-port", ...more });

  it("counts the apps with the player's own tags, the tag filters and the hidden apps", () => {
    const apps = [
      app({ catalogId: "a", tags: ["n64", "favourite"], catalogTags: ["n64"] }),
      app({ catalogId: "b", tags: ["N64"], catalogTags: ["n64"], hidden: true }),
      // Not in the catalog: its tags are all the player's.
      app({ tags: ["mine"] }),
      // From a 3.x that didn't keep the catalog's tags: there's no telling.
      app({ catalogId: "c", tags: ["n64", "favourite"] }),
      // 3.x doesn't know where it comes from, so it doesn't come over.
      app({ repository: undefined, tags: ["mine"], hidden: true }),
    ];
    const filters = [{ name: "N64", tags: ["n64"], all: false, exclude: [], excludeAll: false }];
    expect(bringsAlong({ apps, filters })).toEqual({ tagged: 2, filters: 1, hidden: 1 });
    expect(bringsAlong({ apps: [app()] })).toEqual({ tagged: 0, filters: 0, hidden: 0 });
  });
});

describe("the files in an app's folder", () => {
  const install = { version: "1", folder: "x", executables: [] };

  it("follows the catalog until the player or Settings chooses", () => {
    expect(installFiles(undefined, ["portable.txt"], false)).toBeUndefined();
    expect(installFiles(install, ["portable.txt"], false)).toBeUndefined();
  });

  it("adds Settings' default to a new install, not one already here, whose saves it would seem to lose", () => {
    expect(installFiles(undefined, ["config.ini"], true)).toEqual(["config.ini", "portable.txt"]);
    expect(installFiles(undefined, ["Portable.txt"], true)).toEqual(["Portable.txt"]);
    expect(installFiles(install, ["config.ini"], true)).toBeUndefined();
  });

  it("keeps to what the player chose through updates, over the catalog and Settings", () => {
    expect(installFiles({ ...install, files: [] }, ["portable.txt"], true)).toEqual([]);
    expect(installFiles({ ...install, files: ["a.txt"] }, ["portable.txt"], false)).toEqual(["a.txt"]);
  });

  it("keeps them when the app is pointed at a new folder", () => {
    expect(relocatedInstall({ ...install, files: ["a.txt"] }, install).files).toEqual(["a.txt"]);
  });

  it("takes names without blanks or repeats", () => {
    expect(uniqueFiles([" a.txt ", "", "A.TXT", "b.txt"])).toEqual(["a.txt", "b.txt"]);
  });

  it("isn't for a program added from where it is, or one a Windows installer put elsewhere", () => {
    expect(filesAddable(undefined)).toBe(false);
    expect(filesAddable(install)).toBe(true);
    expect(filesAddable({ ...install, program: "/games/a.exe" })).toBe(false);
    expect(filesAddable({ ...install, installer: "msi" })).toBe(false);
  });
});

describe("an update for this computer", () => {
  const linuxOnly = { version: "linux-v0.1.0-beta", releasedAt: 30 };
  const listed = { verified: linuxOnly } as unknown as Entry;
  const install = { version: "v0.3.1-beta", releasedAt: 10, folder: "Port", executables: [] };
  const found = (release?: { version: string; releasedAt: number }, on = install) => ({ ...listed, forHere: { of: forHereKey(linuxOnly, on), ...(release ? { release } : {}) } });
  const windows = { version: "v0.3.2-beta", releasedAt: 20 };

  it("isn't offered until the app's releases say there's one", () => {
    expect(mayUpdate(listed, install)).toBe(true);
    expect(hasUpdate(listed, install)).toBe(false);
  });

  it("is the newest release with a build for it, not the verified one", () => {
    expect(updateFor(found(windows), install)).toEqual(windows);
    expect(hasUpdate(found(windows), install)).toBe(true);
  });

  it("isn't offered when the verified release has the only newer files, for another system", () => {
    expect(hasUpdate(found(), install)).toBe(false);
    expect(hasUpdate(found(windows), { ...install, version: "v0.3.2-beta", releasedAt: 20 })).toBe(false);
  });

  it("is looked up again for a newer verified release, or another build", () => {
    const stale = found(windows);
    expect(hasUpdate({ ...stale, verified: { version: "v0.4.0", releasedAt: 40 } } as unknown as Entry, install)).toBe(false);
    expect(hasUpdate(stale, { ...install, wine: true })).toBe(false);
  });

  it("isn't offered once skipped, until a newer one comes", () => {
    const declined = { ...install, declined: "0.3.2-beta" };
    expect(hasUpdate(found(windows), declined)).toBe(false);
    expect(declinedUpdate(found(windows), declined)).toEqual(windows);
    const newer = { version: "v0.3.3-beta", releasedAt: 25 };
    expect(hasUpdate(found(newer), declined)).toBe(true);
    expect(declinedUpdate(found(newer), declined)).toBeUndefined();
  });

  it("is what Auto Update leaves alone after going back from it", () => {
    expect(skipped(found(windows), { ...install, skip: "v0.3.2-beta" })).toBe(true);
    expect(skipped(found(windows), { ...install, skip: "linux-v0.1.0-beta" })).toBe(false);
  });

  it("stays quiet under a pin, while the app's page can still say there's one", () => {
    const pinned = { ...install, updates: "pinned" as const };
    expect(hasUpdate(found(windows), pinned)).toBe(false);
    expect(updateFor(found(windows), pinned)).toEqual(windows);
  });
});

describe("naming an app being played in usage data", () => {
  it("gives a catalog app's version", () => {
    expect(playedRef(appRef("port", "test-port"), "v1.2.0")).toEqual({ slug: "test-port", source: "catalog", version: "v1.2.0" });
  });

  it("keeps an app the player added to its kind", () => {
    expect(playedRef(appRef("github:someone/private-port", "private-port"), "my-tag")).toEqual({ slug: null, source: "custom", version: null });
    expect(playedRef(appRef("local:abc", "my-game"), "1")).toEqual({ slug: null, source: "local", version: null });
  });
});

describe("moving the apps folder", () => {
  const base = { version: "1.0.0", executables: [] };
  const installs = {
    here: { ...base, folder: "sm64" },
    v3: { ...base, folder: "zelda", dir: "C:/QuiverLauncher3/Apps/Zelda64Recomp", inApps: true as const },
    // Put on a drive of its own on purpose: in 3.x, or with Locate existing install.
    placed: { ...base, folder: "mario", dir: "E:/Fast SSD/Mario" },
    own:{ ...base, folder: "mine", dir: "D:/Games/Mine", local: true as const, program: "D:/Games/Mine/mine.exe" },
    msi: { ...base, folder: "msi", dir: "C:/Program Files/Thing", installer: "msi" as const, program: "C:/Program Files/Thing/thing.exe" },
  };

  it("takes the apps folder's apps, and offers to bring in Quiver 3's, never one placed on purpose, the player's own or an installer's", () => {
    expect(movableApps(installs)).toEqual({
      inside: [{ id: "here", folder: "sm64" }],
      elsewhere: [{ id: "v3", folder: "zelda", dir: "C:/QuiverLauncher3/Apps/Zelda64Recomp" }],
      placed: 1,
    });
  });

  it("drops the old folder once an app is in the apps folder under its own name, keeping the rest", () => {
    expect(movedInside({ ...installs.v3, lastPlayed: 5 })).toEqual({ ...base, folder: "zelda", lastPlayed: 5 });
  });

  it("knows a 3.x app in 3.x's apps folder can come along, and one 3.x kept in a folder of its own can't", () => {
    const entry = { slug: "zelda", launcher: { folderName: "zelda" } } as unknown as Parameters<typeof adoptedInstall>[1];
    const old = { name: "Zelda", provider: "github", dir: "C:/Q3/Apps/Zelda", version: "1.0.0" };
    expect(adoptedInstall({ ...old, inApps: true }, entry, "windows")?.inApps).toBe(true);
    expect(adoptedInstall(old, entry, "windows")?.inApps).toBeUndefined();
    // Brought over again by a launcher that knows, the same copy learns it.
    expect(keptInstall({ ...base, folder: "zelda", dir: old.dir }, { ...base, folder: "zelda", dir: old.dir, inApps: true }).inApps).toBe(true);
  });

  it("marks 3.x's apps-folder installs that came over before it knew, and no other", () => {
    const { v3: _, ...rest } = installs;
    const before = { ...rest, v3: { ...base, folder: "zelda", dir: "C:/QuiverLauncher3/Apps/Zelda64Recomp" } };
    const old = [
      { name: "Zelda", provider: "github", dir: "C:/QuiverLauncher3/Apps/Zelda64Recomp", version: "1.0.0", inApps: true },
      { name: "Mario", provider: "github", dir: "E:/Fast SSD/Mario", version: "1.0.0" },
    ];
    const after = markedInApps(before, old);
    expect(after.v3.inApps).toBe(true);
    expect(after.placed).toBe(before.placed);
    expect(markedInApps(after, old)).toBe(after);
  });
});

describe("several apps folders", () => {
  const base = { version: "1.0.0", executables: [] };
  const installs = {
    here: { ...base, folder: "sm64" },
    zelda: { ...base, folder: "zelda", dir: "C:/QuiverLauncher3/Apps/Zelda64Recomp", inApps: true as const },
    // The same folder written the Windows way, as a move records it.
    mario: { ...base, folder: "mario", dir: "C:\\QuiverLauncher3\\Apps\\mario", inApps: true as const },
    placed: { ...base, folder: "kart", dir: "E:/Fast SSD/Kart" },
    msi: { ...base, folder: "msi", dir: "C:/Program Files/Thing", installer: "msi" as const, program: "C:/Program Files/Thing/thing.exe" },
  };

  it("lists the default first, then each folder apps are kept in, never one chosen for a single app", () => {
    const folders = appsFolders(installs, "D:\\Games");
    expect(folders.map((f) => [f.path, f.default, f.apps.map((a) => a.id)])).toEqual([
      ["D:\\Games", true, ["here"]],
      ["C:/QuiverLauncher3/Apps", false, ["zelda", "mario"]],
    ]);
  });

  it("keeps the default's apps where they are before another folder becomes the default", () => {
    const kept = keptWhereTheyAre(installs, "D:\\Games\\");
    expect(kept.here).toEqual({ ...installs.here, dir: "D:\\Games\\sm64", inApps: true });
    expect(kept.zelda).toBe(installs.zelda);
    expect(appsFolders(kept, "E:\\New").map((f) => f.path)).toEqual(["E:\\New", "C:/QuiverLauncher3/Apps", "D:\\Games"]);
  });

  it("gives the new default back the apps in it under their own name, and keeps one under another name where it is", () => {
    const within = {
      a: { ...base, folder: "zelda", dir: "E:\\New\\zelda", inApps: true as const },
      b: { ...base, folder: "kart", dir: "E:\\New\\kart 2", inApps: true as const },
    };
    const owned = ownedBy(within, "e:/new/");
    expect(owned.a).toEqual({ ...base, folder: "zelda" });
    expect(owned.b).toBe(within.b);
  });

  it("refuses a new default inside another apps folder, or around one", () => {
    const folders = appsFolders(installs, "D:\\Games");
    expect(folderClash("D:\\Games\\More", folders)).toContain("D:\\Games");
    expect(folderClash("C:/", folders)).toContain("QuiverLauncher3");
    expect(folderClash("E:/Games", folders)).toBeNull();
    // An apps folder already in the list can become the default.
    expect(folderClash("C:/QuiverLauncher3/Apps", folders)).toBeNull();
  });
});

describe("grouping new apps by console", () => {
  const app = (projectType: string, consoles: string[] = [], basedOn?: string) => ({ id: "k1", projectType, consoles, ...(basedOn ? { basedOn: { console: basedOn } } : {}) }) as unknown as Entry;

  it("puts each kind of app in its folder, a port in its console's by its short name", () => {
    expect(groupFolder(app("emulator", ["n64", "gcn"]))).toBe("Emulators");
    expect(groupFolder(app("tool", ["n64"]))).toBe("Tools");
    expect(groupFolder(app("game", ["pc"]))).toBe("Games");
    expect(groupFolder(app("port", ["n64"]))).toBe("Ports/N64");
    expect(groupFolder(app("port", ["og xbox"]))).toBe("Ports/Xbox");
    // The version it plays decides, over the consoles its games came out on.
    expect(groupFolder(app("port", ["ps1", "n64"], "ps1"))).toBe("Ports/PS1");
    expect(groupFolder(app("port", ["ps1", "n64"]))).toBe("Ports/Multi-console");
    expect(groupFolder(app("port"))).toBe("Ports/Other");
    // One the list doesn't know yet goes by the catalog's name for it, made a folder name.
    expect(groupFolder(app("port", ["neo"]), { neo: "Neo Geo: AES" })).toMatch(/^Ports\/[^:/]+$/);
    expect(groupFolder({ ...app("port", ["n64"]), id: "local:abc" })).toBeUndefined();
  });

  it("counts an app in a console folder in its apps folder, not as a folder of its own", () => {
    const base = { version: "1.0.0", executables: [] };
    const installs = {
      plain: { ...base, folder: "sm64" },
      grouped: { ...base, folder: "Zelda", dir: joinPath(joinPath("D:/Games", "Ports/N64"), "Zelda"), inApps: true as const, group: "Ports/N64" },
      other: { ...base, folder: "Kart", dir: "E:/More/Emulators/Kart", inApps: true as const, group: "Emulators" },
    };
    expect(installs.grouped.dir).toBe("D:/Games/Ports/N64/Zelda");
    expect(joinPath("D:\\Games", "Ports/N64")).toBe("D:\\Games\\Ports\\N64");
    expect(rootOf(installs.other)).toBe("E:/More");
    expect(rootOf(installs.grouped)).toBe("D:/Games");
    expect(appsFolders(installs, "D:/Games").map((f) => [f.path, f.apps.map((a) => a.id)])).toEqual([
      ["D:/Games", ["plain", "grouped"]],
      ["E:/More", ["other"]],
    ]);
    // A move takes it with its console folder.
    expect(movableApps(installs).elsewhere.find((a) => a.id === "grouped")?.group).toBe("Ports/N64");
  });

  it("lists the installed apps at the top of the default apps folder for sorting, none kept anywhere else", () => {
    const base = { version: "1.0.0", executables: [] };
    const port = (id: string) => ({ ...app("port", ["n64"]), id });
    const installs = {
      plain: { ...base, folder: "sm64" },
      kept: { ...base, folder: "Kart", dir: "E:/More/Kart", inApps: true as const },
      sorted: { ...base, folder: "Zelda", dir: "D:/Games/N64/Zelda", inApps: true as const, group: "N64" },
      chosen: { ...base, folder: "Own", dir: "F:/Mine/Own" },
      msi: { ...base, folder: "Setup", installer: "msi" as const },
      gone: { ...base, folder: "Gone" },
      "local:x": { ...base, folder: "Local", local: true as const },
    };
    const catalog = { plain: port("plain"), kept: port("kept"), sorted: port("sorted"), chosen: port("chosen"), msi: port("msi") };
    expect(unsortedApps(installs, "D:/Games", catalog)).toEqual([
      { id: "plain", folder: "sm64", to: "D:/Games" },
    ]);
  });
});
