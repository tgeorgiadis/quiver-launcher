import { describe, expect, it } from "vitest";
import type { Asset } from "@quiverlauncher/api";
import type { TvApp, TvState } from "../bridge";
import { normalize } from "../data/sidecar";
import { discoverApps, downloadRows, matchesSearch, rememberedAsset } from "./lists";

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

  it("narrows the library's games to the chosen console (the catalog's are fetched for it)", () => {
    expect(discoverApps(library, [], "inLibrary", "ps1").map((a) => a.id)).toEqual(["ps"]);
  });
});

describe("downloadRows", () => {
  const data = normalize({ apps: { old: { installedAt: 50 }, fresh: { installedAt: 200 }, fresher: { installedAt: 300 } } });

  it("is Update all, then downloading, waiting, failed, and finished this session newest first", () => {
    const rows = downloadRows(
      [
        app("old", "installed"),
        app("fresh", "installed"),
        app("fresher", "installed"),
        app("upd", "updateAvailable"),
        app("dl", "downloading", { installedVersion: undefined }),
        app("q", "queued", { installedVersion: undefined }),
        app("bad", "error", { installedVersion: undefined }),
      ],
      data,
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
    expect(downloadRows([app("old", "installed")], data, 100)).toEqual([]);
  });
});

describe("rememberedAsset", () => {
  const asset = (filename: string) => ({ id: filename, filename }) as Asset;
  const files = [asset("Game-v2.0-win64-vulkan.zip"), asset("Game-v2.0-win64-opengl.zip")];

  it("finds the file picked before in a new release", () => {
    expect(rememberedAsset(files, "vulkan")?.filename).toBe("Game-v2.0-win64-vulkan.zip");
  });

  it("asks again when nothing, or more than one file, matches", () => {
    expect(rememberedAsset(files, "dx12")).toBeNull();
    expect(rememberedAsset(files, "win64")).toBeNull();
    expect(rememberedAsset(files, undefined)).toBeNull();
  });
});
