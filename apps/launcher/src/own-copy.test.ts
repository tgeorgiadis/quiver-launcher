import { describe, expect, it } from "vitest";
import type { SetupFile } from "@quiverlauncher/api";
import { copiesNeeded, delivered, launchCopies, setupFiles, sizeText } from "./own-copy";

const rom: SetupFile = { id: "rom", label: "Game ROM", destination: "baserom.z64", extensions: [".z64"], accepted: [{ algo: "sha1", value: "a".repeat(40) }] };
const iso: SetupFile = { id: "iso", label: "Disc image", flag: "--iso", extensions: [".iso"], accepted: [] };
const env: SetupFile = { id: "env", label: "Data", env: "GAME_DATA", kind: "folder", contains: ["data.pak"], accepted: [] };
const asks: SetupFile = { id: "asks", label: "Asks itself", extensions: [".wad"], accepted: [] };
const entry = (...files: SetupFile[]) => ({ launcher: { setupFiles: files } }) as never;

describe("own copy", () => {
  it("knows which copies the launcher gets to the game", () => {
    expect([rom, iso, env].every(delivered)).toBe(true);
    expect(delivered(asks)).toBe(false);
  });

  it("leaves phones out, and a Mac's environment-only copies", () => {
    expect(setupFiles(entry(rom), "android")).toEqual([]);
    expect(setupFiles(entry(rom), "ios")).toEqual([]);
    expect(setupFiles(entry(rom, env), "macos").map((f) => f.id)).toEqual(["rom"]);
    expect(setupFiles(entry(rom, env), "linux").map((f) => f.id)).toEqual(["rom", "env"]);
  });

  it("skips files the catalog got wrong", () => {
    expect(setupFiles(entry({ id: "x" } as SetupFile, rom), "windows").map((f) => f.id)).toEqual(["rom"]);
  });

  it("waits only for needed copies it can deliver and hasn't got", () => {
    const e = entry(rom, iso, asks, { ...env, required: false });
    expect(copiesNeeded(e, {}, "windows").map((f) => f.id)).toEqual(["rom", "iso"]);
    expect(copiesNeeded(e, { copies: { rom: { path: "C:/r.z64", at: 1 } } }, "windows").map((f) => f.id)).toEqual(["iso"]);
  });

  it("passes flag and environment copies where they are now", () => {
    const install = { copies: { rom: { path: "/r.v64", placed: "/a/baserom.z64", at: 1 }, iso: { path: "/g.iso", at: 1 }, env: { path: "/d", placed: "/a/data", at: 1 } } };
    expect(launchCopies(entry(rom, iso, env), install)).toEqual([
      { flag: "--iso", path: "/g.iso" },
      { env: "GAME_DATA", path: "/a/data" },
    ]);
  });

  it("says sizes plainly", () => {
    expect(sizeText(500)).toBe("1 KB");
    expect(sizeText(32 * 1024 * 1024)).toBe("32 MB");
    expect(sizeText(7.3 * 1024 ** 3)).toBe("7.3 GB");
  });
});
