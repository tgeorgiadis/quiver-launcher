import { expect, it, vi } from "vitest";
import { onSavesChanged, savesChanged, savesOf, savesOn } from "./saves";

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
