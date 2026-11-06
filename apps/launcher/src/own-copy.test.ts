import { describe, expect, it } from "vitest";
import type { SetupFile } from "@quiverlauncher/api";
import { copiesNeeded, delivered, launchCopies, nameWords, rankCopies, setupFiles, sizeText } from "./own-copy";

describe("rankCopies", () => {
  const base = (path: string) => path.split("\\").pop();
  const found = [
    "Diddy Kong Racing (USA) (En,Fr) (Rev 1).z64",
    "Legend of Zelda, The - Ocarina of Time (USA).z64",
    "mimi-6126231.z64",
    "007 - The World Is Not Enough (USA).z64",
    "GoldenEye 007 (USA).v64",
    "Banjo-Kazooie (USA) (Rev 1).z64",
    "goldeneye.z64",
  ].map((n) => ({ path: `C:\\Users\\p\\Downloads\\${n}`, size: 1 }));

  it("puts the files named like the game first, best first, and folds the rest away", () => {
    const { likely, others } = rankCopies(found, ["GoldenEye 007", "GoldenEye 007 ROM"]);
    expect(likely.map((f) => base(f.path))).toEqual(["GoldenEye 007 (USA).v64", "goldeneye.z64"]);
    expect(others).toHaveLength(5);
  });

  it("matches a title however the file words it", () => {
    const { likely } = rankCopies(found, ["The Legend of Zelda: Ocarina of Time"]);
    expect(likely.map((f) => base(f.path))).toEqual(["Legend of Zelda, The - Ocarina of Time (USA).z64"]);
  });

  it("keeps everything when there's no title to go by", () => {
    expect(rankCopies(found, ["ROM", ""])).toEqual({ likely: [], others: found });
  });

  it("reads the words that tell games apart", () => {
    expect(nameWords("Pokémon Emerald (USA, Europe) (Rev 1).gba")).toEqual(["pokemon", "emerald", "1"]);
  });
});

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
