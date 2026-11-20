import { describe, expect, it } from "vitest";
import type { TvApp, TvState } from "../bridge";
import { discoverApps, downloadRows, matchesSearch } from "./lists";

const app = (id: string, state: TvState, over: Partial<TvApp> = {}) =>
  ({ id, title: id, state, consoles: ["n64"], basedOn: "n64", installedVersion: state === "notInstalled" ? undefined : "v1", ...over }) as TvApp;

describe("matchesSearch", () => {
  const game = { title: "2 Ship 2 Harkinian", gameTitle: "The Legend of Zelda: Majora's Mask", tags: ["recomp", "n64"] };

  it("finds every word in the names or tags, any case", () => {
    expect(matchesSearch(game, "zelda")).toBe(true);
    expect(matchesSearch(game, "SHIP majora")).toBe(true);
    expect(matchesSearch(game, "recomp n64")).toBe(true);
    expect(matchesSearch(game, "zelda ocarina")).toBe(false);
    expect(matchesSearch(game, "  ")).toBe(true);
  });
});

describe("discoverApps", () => {
  const library = [app("mine", "notInstalled"), app("installed", "installed"), app("ps", "notInstalled", { consoles: ["ps1"], basedOn: "ps1" })];
  const catalog = [app("mine", "notInstalled"), app("new", "notInstalled"), app("installed", "installed")];

  it("shows what's not in the library yet, without what is", () => {
    expect(discoverApps(library, catalog, "notInLibrary", null).map((a) => a.id)).toEqual(["new"]);
  });

  it("shows library games that aren't installed", () => {
    expect(discoverApps(library, catalog, "inLibrary", null).map((a) => a.id)).toEqual(["mine", "ps"]);
  });

  it("shows both, library first, never an installed game", () => {
    expect(discoverApps(library, catalog, "all", null).map((a) => a.id)).toEqual(["mine", "ps", "new"]);
  });

  it("keeps a game got during this visit where it was, whatever its state now", () => {
    const got = [...library, app("new", "downloading")];
    const kept = new Set(["new"]);
    expect(discoverApps(got, catalog, "notInLibrary", null, kept).map((a) => a.id)).toEqual(["new"]);
    expect(discoverApps(got, catalog, "all", null, kept).map((a) => a.id)).toEqual(["mine", "ps", "new"]);
    expect(discoverApps(got, catalog, "notInLibrary", null).map((a) => a.id)).toEqual([]);
  });

  it("narrows the library's games and the catalog's to the chosen consoles", () => {
    expect(discoverApps(library, [], "inLibrary", new Set(["ps1"])).map((a) => a.id)).toEqual(["ps"]);
    const handhelds = [app("gba", "notInstalled", { consoles: ["gba"] }), app("n64", "notInstalled", { consoles: ["n64"] })];
    expect(discoverApps(library, handhelds, "notInLibrary", new Set(["gba", "psp"])).map((a) => a.id)).toEqual(["gba"]);
    // None matching: nothing.
    expect(discoverApps(library, handhelds, "all", new Set()).map((a) => a.id)).toEqual([]);
  });
});

describe("downloadRows", () => {
  it("is Update all, then downloading, waiting, failed, and finished this session newest first", () => {
    const rows = downloadRows(
      [
        app("old", "installed", { installedAt: 50 }),
        app("fresh", "installed", { installedAt: 200 }),
        app("fresher", "installed", { installedAt: 300 }),
        app("upd", "updateAvailable"),
        app("dl", "downloading", { installedVersion: undefined }),
        app("q", "queued", { installedVersion: undefined }),
        app("bad", "error", { installedVersion: undefined }),
      ],
      100,
    );
    expect(rows.map((r) => (r.kind === "updateAll" ? `all:${r.apps.length}` : `${r.status}:${r.app.id}`))).toEqual([
      "all:1",
      "active:dl",
      "queued:q",
      "failed:bad",
      "done:fresher",
      "done:fresh",
    ]);
  });

  it("is empty when nothing is going on", () => {
    expect(downloadRows([app("old", "installed", { installedAt: 50 })], 100)).toEqual([]);
  });
});
