import { describe, expect, it } from "vitest";
import { fileName, literal, programHints } from "./programs";

describe("programHints", () => {
  it("tries the Windows name without .exe when the catalog names no Linux program", () => {
    expect(programHints("linux", [], { windows: ["starfox_pc.exe"] })).toEqual(["starfox_pc"]);
  });

  it("puts the player's pick first, then the catalog's own names", () => {
    expect(programHints("linux", ["DKR-R-*-Linux-x86_64.AppImage"], { windows: ["DKR-R.exe"] }, "DKR [1].AppImage")).toEqual([
      "DKR [[]1[]].AppImage",
      "DKR-R-*-Linux-x86_64.AppImage",
      "DKR-R",
    ]);
  });

  it("uses the catalog's names for this computer when the install has none", () => {
    expect(programHints("linux", [], { linux: ["starship.appimage"], windows: ["Starship.exe"] })).toEqual(["starship.appimage", "Starship"]);
  });

  it("changes nothing on Windows", () => {
    expect(programHints("windows", ["game.exe"], { windows: ["game.exe"] })).toEqual(["game.exe"]);
  });
});

describe("literal and fileName", () => {
  it("escapes glob characters and takes the last part of a path", () => {
    expect(literal("a*b?c")).toBe("a[*]b[?]c");
    expect(fileName("bin/linux/game")).toBe("game");
  });
});
