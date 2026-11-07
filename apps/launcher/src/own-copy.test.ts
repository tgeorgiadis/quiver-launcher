import { describe, expect, it } from "vitest";
import type { SetupFile } from "@quiverlauncher/api";
import { copiesNeeded, copyFoldersFor, delivered, EVERY_CONSOLE, launchCopies, nameWords, rankCopies, setupFiles, sizeText, withCopyFolder } from "./own-copy";

describe("copy folders", () => {
  const folders = { n64: ["D:/roms/n64", "E:/n64"], ps1: ["D:/roms/ps1"], [EVERY_CONSOLE]: ["D:/roms", "E:/n64"] };

  it("looks in the game's consoles' folders, then every console's, each once", () => {
    expect(copyFoldersFor(folders, ["n64"])).toEqual(["D:/roms/n64", "E:/n64", "D:/roms"]);
    expect(copyFoldersFor(folders, ["snes"])).toEqual(["D:/roms", "E:/n64"]);
    expect(copyFoldersFor(undefined, ["n64"])).toEqual([]);
  });

  it("adds a folder once, and drops a console with none left", () => {
    expect(withCopyFolder(folders, "n64", "E:/n64", true).n64).toEqual(["D:/roms/n64", "E:/n64"]);
    expect(withCopyFolder(undefined, "gc", "F:/gc", true)).toEqual({ gc: ["F:/gc"] });
    expect("ps1" in withCopyFolder(folders, "ps1", "D:/roms/ps1", false)).toBe(false);
    expect(folders.ps1).toEqual(["D:/roms/ps1"]);
  });
});

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

  // From a real ROM set: names as No-Intro has them.
  const set = (names: string[]) => names.map((n) => ({ path: `S:\\Roms\\N64\\${n}`, size: 1 }));

  it("puts the dump as sold before a beta, and doesn't take one shared word for the game", () => {
    const n64 = set([
      "F-Zero X (USA) (Beta) (The Legend of Zelda - Ocarina of Time leftover data).z64",
      "Duke Nukem - Zero Hour (USA).z64",
      "F-Zero X (USA).z64",
      "Mortal Kombat Mythologies - Sub-Zero (USA).z64",
    ]);
    const { likely } = rankCopies(n64, ["F-Zero X"], "F-Zero X ROM");
    expect(likely.map((f) => base(f.path))).toEqual(["F-Zero X (USA).z64", "F-Zero X (USA) (Beta) (The Legend of Zelda - Ocarina of Time leftover data).z64"]);
  });

  it("doesn't take another game for this one on its longest word", () => {
    const n64 = set(["F1 Racing Championship (USA) (Beta).z64", "World Driver Championship (USA).z64", "GT 64 - Championship Edition (USA).z64"]);
    expect(rankCopies(n64, ["World Driver Championship"]).likely.map((f) => base(f.path))).toEqual(["World Driver Championship (USA).z64"]);
  });

  it("puts the game itself before games with its name and more", () => {
    const gb = set(["Donkey Kong Land (USA, Europe) (SGB Enhanced).gb", "Donkey Kong (World) (Rev 1) (SGB Enhanced).gb", "Donkey Kong Land 2 (USA, Europe) (SGB Enhanced).gb"]);
    expect(base(rankCopies(gb, ["Donkey Kong"], "Donkey Kong ROM").likely[0].path)).toBe("Donkey Kong (World) (Rev 1) (SGB Enhanced).gb");
  });

  it("doesn't take the first game for its sequel", () => {
    const snes = set([
      "Donkey Kong Country (USA) (Rev 2).sfc",
      "Donkey Kong Country 2 - Diddy's Kong Quest (USA) (En,Fr) (Rev 1).sfc",
      "Donkey Kong Country 3 - Dixie Kong's Double Trouble! (USA) (En,Fr).sfc",
    ]);
    const first = (titles: string[], label: string) => base(rankCopies(snes, titles, label).likely[0].path);
    expect(first(["Donkey Kong Country 2"], "Donkey Kong Country 2 (USA).sfc")).toBe("Donkey Kong Country 2 - Diddy's Kong Quest (USA) (En,Fr) (Rev 1).sfc");
    expect(first(["Donkey Kong Country"], "Donkey Kong Country ROM")).toBe("Donkey Kong Country (USA) (Rev 2).sfc");
  });

  it("goes by the copy's own label first, for an app that plays more than one game", () => {
    const gbc = set(["Legend of Zelda, The - Oracle of Ages (USA, Australia).gbc", "Legend of Zelda, The - Oracle of Seasons (USA, Australia).gbc"]);
    const titles = ["The Legend of Zelda: Oracle of Ages", "The Legend of Zelda: Oracle of Seasons"];
    expect(base(rankCopies(gbc, titles, "Oracle of Seasons (US) ROM").likely[0].path)).toBe("Legend of Zelda, The - Oracle of Seasons (USA, Australia).gbc");
    expect(base(rankCopies(gbc, titles, "Oracle of Ages (US) ROM").likely[0].path)).toBe("Legend of Zelda, The - Oracle of Ages (USA, Australia).gbc");
  });

  it("keeps everything when there's no title to go by", () => {
    expect(rankCopies(found, ["ROM", ""])).toEqual({ likely: [], others: found });
  });

  it("reads the words that tell games apart", () => {
    expect(nameWords("Pokémon Emerald (USA, Europe) (Rev 1).gba")).toEqual(["pokemon", "emerald"]);
    expect(nameWords("Super Mario World 2 - Yoshi's Island")).toEqual(["super", "mario", "world", "2", "yoshi", "s", "island"]);
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

  it("passes a positional copy on its own, and a flag wins over it", () => {
    const bare: SetupFile = { id: "bare", label: "USA ROM", positional: true, extensions: [".z64"], accepted: [] };
    expect(delivered(bare)).toBe(true);
    expect(delivered({ ...bare, positional: false })).toBe(false);
    expect(setupFiles(entry(bare), "macos").map((f) => f.id)).toEqual(["bare"]);
    expect(copiesNeeded(entry(bare), {}, "windows").map((f) => f.id)).toEqual(["bare"]);
    const install = { copies: { bare: { path: "C:/roms/wdc.z64", at: 1 }, iso: { path: "/g.iso", at: 1 } } };
    expect(launchCopies(entry(bare), install)).toEqual([{ positional: true, path: "C:/roms/wdc.z64" }]);
    expect(launchCopies(entry({ ...iso, positional: true }), install)).toEqual([{ flag: "--iso", path: "/g.iso" }]);
  });

  it("says sizes plainly", () => {
    expect(sizeText(500)).toBe("1 KB");
    expect(sizeText(32 * 1024 * 1024)).toBe("32 MB");
    expect(sizeText(7.3 * 1024 ** 3)).toBe("7.3 GB");
  });
});
