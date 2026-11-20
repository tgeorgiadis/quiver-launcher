import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@quiverlauncher/api";
import { onSavesChanged, reportSaveLocations, reportSaveWarning, savesChanged, savesOf, savesOn } from "./saves";

it("names an installed app for the saves commands", () => {
  const entry = {
    name: "Zelda 64",
    projectName: "Zelda 64: Recompiled",
    slug: "zelda-64-recompiled",
    launcher: { folderName: "Zelda64Recompiled", filesToAdd: [], saves: { locations: [{ root: "localData" as const, path: "Zelda64Recompiled/saves", os: ["windows" as const] }] } },
  };
  expect(savesOf("e1", { folder: "Zelda64Recompiled", version: "1.2.0", wine: true, program: "C:\\Games\\Zelda\\Zelda64Recompiled.exe" }, entry)).toEqual({
    id: "e1",
    folder: "Zelda64Recompiled",
    wine: true,
    locations: [{ root: "localData", path: "Zelda64Recompiled/saves", os: ["windows"] }],
    version: "1.2.0",
    names: ["Zelda 64: Recompiled", "Zelda 64", "zelda-64-recompiled", "Zelda64Recompiled"],
  });
});

it("names an app the player added, which has no catalog entry", () => {
  expect(savesOf("github:owner/repo", { folder: "repo", dir: "/games/repo", version: "" })).toEqual({ id: "github:owner/repo", folder: "repo", dir: "/games/repo", wine: false, locations: [], names: ["repo"] });
});

it("is for desktops", () => {
  expect(["windows", "linux", "macos", "android", "ios"].map(savesOn)).toEqual([true, true, true, false, false]);
});

it("tells an open Saves tab about a new backup until it stops listening", () => {
  const heard = vi.fn();
  const stop = onSavesChanged(heard);
  savesChanged("e1");
  stop();
  savesChanged("e2");
  expect(heard.mock.calls).toEqual([["e1"]]);
});

describe("telling the catalog where an app saved", () => {
  const of = savesOf("e1", { folder: "Game", version: "1.0" });
  const entry = { slug: "game", catalogId: "qcat_1" };
  const places = ["data/Game/saves"];
  const via = (send: () => Promise<void>, found = places) => ({
    toReport: vi.fn(async () => ({ os: "linux" as const, places: found })),
    reported: vi.fn(async () => {}),
    send: vi.fn(send),
  });

  it("sends the folders with the system, then remembers them as sent", async () => {
    const calls = via(async () => {});
    await reportSaveLocations(of, entry, calls);
    expect(calls.send).toHaveBeenCalledWith(entry, { os: "linux", places });
    expect(calls.reported).toHaveBeenCalledWith(of, places);
  });

  it("sends nothing when there's nothing new", async () => {
    const calls = via(async () => {}, []);
    await reportSaveLocations(of, entry, calls);
    expect(calls.send).not.toHaveBeenCalled();
    expect(calls.reported).not.toHaveBeenCalled();
  });

  it("doesn't send again what the site refused, and tries again later what never got there", async () => {
    const refused = via(async () => Promise.reject(new ApiError(404, "No such app")));
    await reportSaveLocations(of, entry, refused);
    expect(refused.reported).toHaveBeenCalledWith(of, places);
    for (const error of [new ApiError(503, "Down"), new ApiError(429, "Slow down"), new TypeError("Failed to fetch")]) {
      const lost = via(async () => Promise.reject(error));
      await reportSaveLocations(of, entry, lost);
      expect(lost.reported).not.toHaveBeenCalled();
    }
  });
});

describe("telling the catalog a backup looks like more than saves", () => {
  const of = savesOf("e1", { folder: "Game", version: "1.0" });
  const entry = { slug: "game", catalogId: "cat-1" };
  const warning = { os: "windows" as const, source: "catalog" as const, reasons: ["big" as const], bytes: 120 * 1024 * 1024, files: 40, kinds: [{ ext: "png", share: 0.9 }], fine: false, key: "big:app/saves" };
  const via = (send: () => Promise<void>, found: typeof warning | null = warning) => ({ warning: vi.fn(async () => found), warned: vi.fn(async () => {}), send: vi.fn(send) });

  it("sends the signs, size and kinds of file, never the key, and marks it sent", async () => {
    const calls = via(async () => {});
    await reportSaveWarning(of, entry, calls);
    const { key, ...report } = warning;
    expect(calls.send).toHaveBeenCalledWith(entry, report);
    expect(calls.warned).toHaveBeenCalledWith(of, key);
  });

  it("sends nothing when there's nothing new, and tries again later what never got there", async () => {
    const none = via(async () => {}, null);
    await reportSaveWarning(of, entry, none);
    expect(none.send).not.toHaveBeenCalled();
    const refused = via(async () => Promise.reject(new ApiError(404, "No such app")));
    await reportSaveWarning(of, entry, refused);
    expect(refused.warned).toHaveBeenCalled();
    const lost = via(async () => Promise.reject(new ApiError(503, "Down")));
    await reportSaveWarning(of, entry, lost);
    expect(lost.warned).not.toHaveBeenCalled();
  });
});
