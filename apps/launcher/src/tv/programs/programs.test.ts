import { describe, expect, it } from "vitest";
import { programHints } from "./programs";

describe("programHints", () => {
  it("tries the Windows name without .exe when the catalog names no Linux program", () => {
    expect(programHints("linux", [], { windows: ["starfox_pc.exe"] })).toEqual(["starfox_pc"]);
  });

  it("puts the catalog's own names first", () => {
    expect(programHints("linux", ["DKR-R-*-Linux-x86_64.AppImage"], { windows: ["DKR-R.exe"] })).toEqual(["DKR-R-*-Linux-x86_64.AppImage", "DKR-R"]);
  });

  it("uses the catalog's names for this computer when the install has none", () => {
    expect(programHints("linux", [], { linux: ["starship.appimage"], windows: ["Starship.exe"] })).toEqual(["starship.appimage", "Starship"]);
  });

  it("changes nothing on Windows", () => {
    expect(programHints("windows", ["game.exe"], { windows: ["game.exe"] })).toEqual(["game.exe"]);
  });
});
