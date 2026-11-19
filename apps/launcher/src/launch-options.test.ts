import { describe, expect, it } from "vitest";
import { outsideFolder, windowsProgram } from "./launch-options";

describe("windowsProgram", () => {
  it("follows the release the player installed when they chose no program", () => {
    expect(windowsProgram(undefined, true)).toBe(true);
    expect(windowsProgram(undefined, false)).toBe(false);
    expect(windowsProgram(undefined, undefined)).toBe(false);
  });

  it("follows the program the player chose, whatever the release", () => {
    expect(windowsProgram("bin/Game.EXE", undefined)).toBe(true);
    expect(windowsProgram("start.bat", false)).toBe(true);
    expect(windowsProgram("Game.AppImage", undefined)).toBe(false);
    expect(windowsProgram("game.x86_64", true)).toBe(false);
  });
});

describe("outsideFolder", () => {
  it("is a program chosen by its full path, on any system", () => {
    expect(outsideFolder("E:\\Games\\Game.exe")).toBe(true);
    expect(outsideFolder("d:/Games/Game.exe")).toBe(true);
    expect(outsideFolder("/run/media/deck/SD/game.sh")).toBe(true);
    expect(outsideFolder("\\\\nas\\games\\Game.exe")).toBe(true);
  });

  it("isn't one in the app's folder, nor an added app's, whose program is always a full path", () => {
    expect(outsideFolder("bin/Game.exe")).toBe(false);
    expect(outsideFolder("Game.exe")).toBe(false);
    expect(outsideFolder(undefined)).toBe(false);
    expect(outsideFolder("/home/tom/game.sh", "/home/tom/game.sh")).toBe(false);
  });
});
