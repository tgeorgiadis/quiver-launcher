import { expect, test } from "vitest";
import { inferPlatform } from "@quiverlauncher/api";

const arch = (name: string) => inferPlatform(name).architecture;

test("Win64 and similar names say which architecture a release file is for", () => {
  expect(inferPlatform("SoH-Ackbar-Win64.zip")).toEqual({ os: "windows", architecture: "x64" });
  expect(inferPlatform("GameWin64.zip")).toEqual({ os: "windows", architecture: "x64" });
  expect(arch("game-windows64.zip")).toBe("x64");
  expect(arch("game-win_64.zip")).toBe("x64");
  expect(arch("game-linux64.tar.gz")).toBe("x64");
  expect(arch("game-1.0-x86-64.AppImage")).toBe("x64");
  expect(arch("Game (64-bit).zip")).toBe("x64");
  expect(arch("game-64bit.zip")).toBe("x64");
  expect(inferPlatform("Game-Win32.zip")).toEqual({ os: "windows", architecture: "x86" });
  expect(arch("game-linux32.tar.gz")).toBe("x86");
  expect(arch("game-ia32.zip")).toBe("x86");
  expect(arch("game-i586.zip")).toBe("x86");
  expect(arch("Game (32-bit).zip")).toBe("x86");
});

test("names that only look like an architecture don't count", () => {
  // "darwin64" is a Mac build, not Windows.
  expect(inferPlatform("game-darwin64.tar.gz").os).toBe("macos");
  expect(arch("DK64Recompiled-Flatpak.zip")).toBe("unknown");
  expect(arch("Mupen64-1.0.zip")).toBe("unknown");
  expect(arch("Harmony-Windows.zip")).toBe("unknown");
  expect(arch("Farmer-Linux.tar.gz")).toBe("unknown");
  expect(arch("game-linux-armv7.tar.gz")).toBe("arm");
  expect(arch("game-linux-armhf.tar.gz")).toBe("arm");
  expect(arch("game-linux-arm.tar.gz")).toBe("arm");
});
