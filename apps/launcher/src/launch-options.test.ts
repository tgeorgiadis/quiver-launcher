import { describe, expect, it } from "vitest";
import { windowsProgram } from "./launch-options";

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
