import { describe, expect, it } from "vitest";
import type { CatalogEntry, Install, LibraryItem } from "../store";
import { InstallQueue, isInstalled, libraryApps, toTvApp, type BridgeInput } from "./bridge";

const entry = (id: string, over: Partial<CatalogEntry> = {}): CatalogEntry =>
  ({
    id,
    slug: id,
    name: `Game ${id}`,
    projectName: `Project ${id}`,
    description: "",
    games: [],
    consoles: ["n64"],
    basedOn: { console: "n64" },
    libraryArt: { capsule: `https://art/${id}/capsule.png`, hero: `https://art/${id}/hero.png` },
    artwork: `https://art/${id}/icon.png`,
    tags: ["recomp"],
    launcher: { folderName: "", filesToAdd: [] },
    projectType: "port",
    supportedOS: ["windows"],
    verified: { version: "v2.0.0", releasedAt: 200, prerelease: false, pinned: true },
    ...over,
  }) as CatalogEntry;

const install = (over: Partial<Install> = {}): Install => ({ version: "v2.0.0", releasedAt: 200, folder: "f", executables: [], ...over });
const item = (id: string, over: Partial<LibraryItem> = {}): LibraryItem => ({ id, slug: id, addedAt: 1, ...over });

const input = (over: Partial<BridgeInput> = {}): BridgeInput => ({ library: [], installs: {}, jobs: {}, catalog: {}, ...over });

describe("toTvApp", () => {
  it("derives each state from installs, jobs and the queue", () => {
    // The release for this computer, looked up (upstream says no update until it is).
    const e = entry("a", { forHere: { of: "2.0.0:own:files", release: { version: "v2.0.0", releasedAt: 200 } } });
    const state = (over: Partial<BridgeInput>) => toTvApp(e, input(over)).state;
    expect(state({})).toBe("notInstalled");
    expect(state({ queued: new Set(["a"]) })).toBe("queued");
    expect(state({ jobs: { a: { id: "a", phase: "downloading", received: 0, total: null } } })).toBe("downloading");
    expect(state({ installs: { a: install() }, jobs: { a: { id: "a", phase: "installing", received: 5, total: 10 } } })).toBe("updating");
    expect(state({ jobs: { a: { error: "No download for your computer." } } })).toBe("error");
    expect(state({ installs: { a: install() } })).toBe("installed");
    expect(state({ installs: { a: install({ version: "v1.0.0", releasedAt: 100 }) } })).toBe("updateAvailable");
  });

  it("never shows an update for a pinned app or one the player brought themselves", () => {
    const old = { version: "v1.0.0", releasedAt: 100 };
    expect(toTvApp(entry("a"), input({ installs: { a: install({ ...old, updates: "pinned" }) } })).state).toBe("installed");
    expect(toTvApp(entry("a"), input({ installs: { a: install({ ...old, local: true }) } })).state).toBe("installed");
  });

  it("reports progress only when the size is known", () => {
    const known = toTvApp(entry("a"), input({ jobs: { a: { id: "a", phase: "downloading", received: 25, total: 100 } } }));
    expect(known.progress).toBe(0.25);
    expect(known.phase).toBe("downloading");
    expect(toTvApp(entry("a"), input({ jobs: { a: { id: "a", phase: "downloading", received: 25, total: null } } })).progress).toBeNull();
  });

  it("uses the player's own name and cover over the catalog's, as the library does", () => {
    const app = toTvApp(entry("a"), input(), item("a", { overrides: { name: "Majora", cover: "C:/covers/mm.png", tags: ["fav"] } }));
    expect(app.title).toBe("Majora");
    expect(app.gameTitle).toBe("Game a");
    expect(app.art.hero).toBe("C:/covers/mm.png");
    expect(app.art.capsule).toBe("https://art/a/capsule.png");
    expect(app.art.icon).toBe("https://art/a/icon.png");
    expect(app.tags).toEqual(["recomp", "fav"]);
    expect(app.inLibrary).toBe(true);
  });

  it("carries the catalog's consoles, versions and the last time it was played", () => {
    const app = toTvApp(entry("a", { consoles: ["ps1", "pc"], basedOn: { console: "ps1" } }), input({ installs: { a: install({ version: "v1.9", lastPlayed: 42 }) } }));
    expect(app.consoles).toEqual(["ps1", "pc"]);
    expect(app.basedOn).toBe("ps1");
    expect(app.installedVersion).toBe("v1.9");
    expect(app.latestVersion).toBe("v2.0.0");
    expect(app.lastPlayed).toBe(42);
  });
});

describe("libraryApps", () => {
  it("leaves out removed apps and ones whose catalog entry hasn't loaded", () => {
    const apps = libraryApps(
      input({
        library: [item("a"), item("b", { removed: true }), item("c")],
        catalog: { a: entry("a"), b: entry("b") },
      }),
    );
    expect(apps.map((a) => a.id)).toEqual(["a"]);
  });

  it("keeps an installed app whose launch or update failed, but not one whose first download failed", () => {
    const apps = libraryApps(
      input({
        library: [item("a"), item("b")],
        catalog: { a: entry("a"), b: entry("b") },
        installs: { a: install() },
        jobs: { a: { error: "Couldn't find a program to start." }, b: { error: "Network" } },
      }),
    );
    expect(apps.map((a) => [a.id, a.state, isInstalled(a)])).toEqual([
      ["a", "error", true],
      ["b", "error", false],
    ]);
  });

  it("counts updating and update-available apps as installed", () => {
    const apps = libraryApps(
      input({
        library: [item("a"), item("b"), item("c"), item("d")],
        catalog: { a: entry("a"), b: entry("b"), c: entry("c"), d: entry("d") },
        installs: { a: install(), b: install({ version: "v1", releasedAt: 1 }), c: install() },
        jobs: { c: { id: "c", phase: "downloading", received: 0, total: null } },
      }),
    );
    expect(apps.filter(isInstalled).map((a) => a.id)).toEqual(["a", "b", "c"]);
  });

  it("shows what the launcher keeps: install and play times, the program picked, the player's consoles and pictures", () => {
    const [app] = libraryApps(
      input({
        library: [item("a", { overrides: { consoles: ["gc"], pictures: { box: "https://me/box", logo: "https://me/logo" } } })],
        catalog: { a: entry("a") },
        installs: { a: install({ installedAt: 5, playTime: 90, launch: { executable: "Game.exe" } }) },
      }),
    );
    expect(app).toMatchObject({ installedAt: 5, playTime: 90, program: "Game.exe", ownConsoles: ["gc"] });
    expect(app.art).toMatchObject({ capsule: "https://me/box", logo: "https://me/logo", hero: "https://art/a/hero.png", icon: "https://art/a/icon.png" });
  });
});

describe("InstallQueue", () => {
  const deferred = () => {
    let resolve!: () => void;
    let reject!: (e: unknown) => void;
    const promise = new Promise<void>((res, rej) => ((resolve = res), (reject = rej)));
    return { promise, resolve, reject };
  };

  it("installs one app at a time, in order", async () => {
    const q = new InstallQueue();
    const started: string[] = [];
    const a = deferred();
    const b = deferred();
    const doneA = q.enqueue("a", () => (started.push("a"), a.promise));
    const doneB = q.enqueue("b", () => (started.push("b"), b.promise));
    expect(started).toEqual(["a"]);
    expect(q.active).toBe("a");
    expect([...q.queued]).toEqual(["b"]);
    a.resolve();
    await doneA;
    expect(started).toEqual(["a", "b"]);
    expect(q.queued.size).toBe(0);
    b.resolve();
    await doneB;
    expect(q.active).toBeNull();
  });

  it("moves on when an install fails", async () => {
    const q = new InstallQueue();
    const a = deferred();
    const ranB = deferred();
    void q.enqueue("a", () => a.promise);
    const doneB = q.enqueue("b", async () => ranB.resolve());
    a.reject(new Error("network"));
    await doneB;
    await ranB.promise;
  });

  it("installs once when asked again, and the second ask waits for that install to finish", async () => {
    const q = new InstallQueue();
    const a = deferred();
    let runs = 0;
    let secondDone = false;
    void q.enqueue("a", () => (runs++, a.promise));
    const second = q.enqueue("a", async () => void runs++).then(() => void (secondDone = true));
    await new Promise((r) => setTimeout(r, 0));
    expect(secondDone).toBe(false);
    a.resolve();
    await second;
    expect(runs).toBe(1);
  });

  it("takes a waiting app out of the queue, but not the one installing", async () => {
    const q = new InstallQueue();
    const a = deferred();
    let ranB = false;
    void q.enqueue("a", () => a.promise);
    const doneB = q.enqueue("b", async () => void (ranB = true));
    expect(q.cancel("a")).toBe(false);
    expect(q.cancel("b")).toBe(true);
    await doneB;
    a.resolve();
    await new Promise((r) => setTimeout(r, 0));
    expect(ranB).toBe(false);
  });

  it("tells listeners when the queue changes, with a new set each time", () => {
    const q = new InstallQueue();
    let calls = 0;
    q.subscribe(() => calls++);
    const before = q.queued;
    void q.enqueue("a", () => new Promise(() => {}));
    void q.enqueue("b", () => new Promise(() => {}));
    expect(calls).toBeGreaterThan(0);
    expect(q.queued).not.toBe(before);
    expect([...q.queued]).toEqual(["b"]);
  });
});
