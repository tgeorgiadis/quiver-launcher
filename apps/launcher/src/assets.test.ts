import { expect, test } from "vitest";
import type { Asset } from "@quiverlauncher/api";
import { assetFilterFor, bestAssets, hasBuildFor, installable, installFormat, pickedBefore, runsHere } from "./assets";

const asset = (filename: string, os: Asset["os"], architecture: Asset["architecture"] = "x64"): Asset => ({
  id: filename, url: `https://example.com/${filename}`, filename, os, architecture, format: "",
});

test("picks this platform's archive over installers, checksums and other systems", () => {
  const files = [
    asset("Game-linux.AppImage", "linux"),
    asset("Game-win64.exe", "windows"),
    asset("Game-win64.zip", "windows"),
    asset("Game-win64.zip.sha256", "windows"),
    asset("Game-win-arm64.zip", "windows", "arm64"),
  ];
  expect(bestAssets(files, "windows", "x64").map((a) => a.filename)).toEqual(["Game-win64.zip"]);
  expect(bestAssets(files, "windows", "arm64").map((a) => a.filename)).toEqual(["Game-win-arm64.zip"]);
});

test("installs tarballs however they're packed, and RAR archives, after zip and 7z", () => {
  const packed = ["Game-linux.tar.xz", "Game-linux.tar.bz2", "Game-linux.tar.zst", "Game-linux.tbz", "Game-linux.tar", "Game-linux.rar"];
  for (const name of packed) expect(bestAssets([asset(name, "linux")], "linux", "x64").map((a) => a.filename)).toEqual([name]);
  expect(bestAssets([asset("Game-linux.tar.xz", "linux"), asset("Game-linux.zip", "linux")], "linux", "x64").map((a) => a.filename)).toEqual(["Game-linux.zip"]);
  expect(assetFilterFor("GameB-Linux.tar.xz", ["GameA-Linux.tar.xz", "GameB-Linux.tar.xz"])).toBe("gameb");
});

test("applies the catalog's file filter and leaves real ties to the player", () => {
  const files = [asset("Game-vulkan.zip", "windows"), asset("Game-dx12.zip", "windows")];
  expect(bestAssets(files, "windows", "x64")).toHaveLength(2);
  expect(bestAssets(files, "windows", "x64", "DX12").map((a) => a.filename)).toEqual(["Game-dx12.zip"]);
});

test("falls back to unplaced files, and to Windows builds on Linux", () => {
  const files = [asset("Game.zip", "unknown", "unknown"), asset("Game-mac.zip", "macos")];
  expect(bestAssets(files, "windows", "x64").map((a) => a.filename)).toEqual(["Game.zip"]);
  expect(bestAssets([asset("Game-win64.zip", "windows")], "linux", "x64")).toHaveLength(1);
});

test("takes files a host names without an extension, after ones that have one", () => {
  // GitLab names a release's files as their links are titled.
  const gitlab = asset("Game-v0.9.2-Windows-RelWithDebInfo", "windows", "unknown");
  expect(bestAssets([gitlab, asset("Game-v0.9.2-macOS-Release", "macos", "unknown")], "windows", "x64")).toEqual([gitlab]);
  expect(bestAssets([gitlab, asset("Game-v0.9.2-win.zip", "windows", "unknown")], "windows", "x64").map((a) => a.filename)).toEqual(["Game-v0.9.2-win.zip"]);
  expect(bestAssets([asset("game.x86_64", "linux")], "linux", "x64")).toHaveLength(1);
  // A file with an extension Quiver can't install is still left out.
  expect(bestAssets([asset("Game-1.0.dmg", "macos"), asset("Game-1.0.deb", "linux")], "macos", "x64")).toEqual([]);
});

test("every file this computer could install, for the player to pick when none is marked for it", () => {
  const files = [asset("Game-mac.zip", "macos", "arm64"), asset("Game.zip.sha256", "macos"), asset("Game-1.0.msi", "linux"), asset("Game.apk", "android")];
  expect(bestAssets(files, "windows", "x64")).toEqual([]);
  expect(installable(files, "windows").map((a) => a.filename)).toEqual(["Game-mac.zip", "Game-1.0.msi"]);
  expect(installable(files, "linux").map((a) => a.filename)).toEqual(["Game-mac.zip"]);
});

test("a file picked among several is found again in later releases", () => {
  const files = ["GameA-v1.2.0-windows-x64.zip", "GameB-v1.2.0-windows-x64.zip", "Collection-Extras-1.2-windows.zip"];
  expect(assetFilterFor(files[1], files)).toBe("gameb");
  expect(assetFilterFor(files[2], files)).toBe("collection-extras");
  // It still matches the next release's file.
  expect("GameB-v1.3.0-windows-x64.zip".toLowerCase()).toContain(assetFilterFor(files[1], files));
  // Platform words name the computer, not the app, so the pick holds on another computer too.
  expect(assetFilterFor("GameB-Linux.zip", ["GameA-Linux.zip", "GameB-Linux.zip"])).toBe("gameb");
  // No part of its own: the whole name.
  expect(assetFilterFor("Game-1.0.zip", ["Game-1.0.zip", "GameDeluxe-1.0.zip"])).toBe("game-1.0.zip");
});

test("an update takes the file picked before, and asks again when it can't tell which that is", () => {
  const picked = ["Game-vulkan-1.2.zip", "Game-dx12-1.2.zip"];
  const pick = assetFilterFor(picked[1], picked);
  const next = [asset("Game-vulkan-1.3.zip", "windows"), asset("Game-dx12-1.3.zip", "windows")];
  expect(pickedBefore(next, pick)?.filename).toBe("Game-dx12-1.3.zip");
  // Renamed since, or now in more than one file: the player picks again.
  expect(pickedBefore([asset("Game-d3d-1.3.zip", "windows"), next[0]], pick)).toBeUndefined();
  expect(pickedBefore([...next, asset("Game-dx12-debug-1.3.zip", "windows")], pick)).toBeUndefined();
  expect(pickedBefore(next, undefined)).toBeUndefined();
});

test("a Flatpak bundle installs on Linux, after an AppImage", () => {
  expect(bestAssets([asset("Game-x86_64.flatpak", "linux"), asset("Game-win64.zip", "windows")], "linux", "x64").map((a) => a.filename)).toEqual(["Game-x86_64.flatpak"]);
  expect(bestAssets([asset("Game.flatpak", "linux"), asset("Game.AppImage", "linux")], "linux", "x64").map((a) => a.filename)).toEqual(["Game.AppImage"]);
  expect(assetFilterFor("GameB-1.0.flatpak", ["GameA-1.0.flatpak", "GameB-1.0.flatpak"])).toBe("gameb");
});

test("a Windows installer (MSI) installs on Windows when there's nothing to unpack, and never elsewhere", () => {
  const msi = asset("Game-1.0-x64.msi", "windows");
  const zip = asset("Game-1.0-win64.zip", "windows");
  expect(bestAssets([msi, zip], "windows", "x64").map((a) => a.filename)).toEqual(["Game-1.0-win64.zip"]);
  expect(bestAssets([msi, asset("Game-1.0-x64.exe", "windows")], "windows", "x64").map((a) => a.filename)).toEqual(["Game-1.0-x64.exe"]);
  expect(bestAssets([msi], "windows", "x64").map((a) => a.filename)).toEqual(["Game-1.0-x64.msi"]);
  // Linux runs Windows builds through Wine, but not Windows Installer.
  expect(bestAssets([msi], "linux", "x64")).toEqual([]);
  expect(bestAssets([msi, zip], "linux", "x64").map((a) => a.filename)).toEqual(["Game-1.0-win64.zip"]);
  expect(assetFilterFor("GameB-1.0.msi", ["GameA-1.0.msi", "GameB-1.0.msi"])).toBe("gameb");
});

test("an update installs the kind it installed before", () => {
  const files = [asset("Game-2.0-x64.msi", "windows"), asset("Game-2.0-win64.zip", "windows")];
  expect(bestAssets(files, "windows", "x64", undefined, "msi").map((a) => a.filename)).toEqual(["Game-2.0-x64.msi"]);
  expect(bestAssets(files, "windows", "x64", undefined, "files").map((a) => a.filename)).toEqual(["Game-2.0-win64.zip"]);
  expect(bestAssets([files[0]], "windows", "x64", undefined, "files")).toEqual([]);
  expect(bestAssets([files[1]], "windows", "x64", undefined, "msi")).toEqual([]);
});

test("usage data names the kind of file installed", () => {
  expect(["Game.msi", "Game.flatpak", "Game-Flatpak.zip", "Game.AppImage", "Game.tar.xz", "Game.zip", "Game.exe", "game"].map(installFormat)).toEqual([
    "msi", "flatpak", "flatpak", "appimage", "archive", "archive", "executable", "executable",
  ]);
});

test("Android installs its APKs, and only Android does", () => {
  const files = [
    asset("Game-arm64-v8a.apk", "android", "arm64"),
    asset("Game-armeabi-v7a.apk", "android", "arm"),
    asset("Game-win64.zip", "windows"),
    asset("Game.zip", "unknown", "unknown"),
  ];
  expect(bestAssets(files, "android", "arm64").map((a) => a.filename)).toEqual(["Game-arm64-v8a.apk"]);
  expect(bestAssets([asset("Game.apk", "android", "unknown")], "android", "arm64")).toHaveLength(1);
  expect(bestAssets([asset("Game.zip", "unknown", "unknown")], "android", "arm64")).toEqual([]);
  expect(bestAssets(files, "windows", "x64").map((a) => a.filename)).toEqual(["Game-win64.zip"]);
  expect(installFormat("Game.apk")).toBe("apk");
});

test("a file the player can install from the Releases tab is one this computer runs", () => {
  expect(runsHere(asset("Game-win64.zip", "windows"), "windows", "x64")).toBe(true);
  expect(runsHere(asset("Game-win32.zip", "windows", "x86"), "windows", "x64")).toBe(true);
  expect(runsHere(asset("Game.zip", "unknown", "unknown"), "windows", "x64")).toBe(true);
  // Linux runs Windows builds through Wine; Windows doesn't run Linux ones.
  expect(runsHere(asset("Game-win64.zip", "windows"), "linux", "x64")).toBe(true);
  expect(runsHere(asset("Game-linux-x64.tar.gz", "linux"), "windows", "x64")).toBe(false);
  expect(runsHere(asset("Game-win-arm64.zip", "windows", "arm64"), "windows", "x64")).toBe(false);
  expect(runsHere(asset("Game-win64.zip.sha256", "windows"), "windows", "x64")).toBe(false);
  expect(runsHere(asset("Game-1.0-x64.msi", "windows"), "linux", "x64")).toBe(false);
});

test("the Windows build the player chose on Linux comes first, and its updates keep to it", () => {
  const files = [asset("Game-linux.AppImage", "linux"), asset("Game-win64.zip", "windows"), asset("Game-1.0-x64.msi", "windows")];
  const names = (list: Asset[]) => list.map((a) => a.filename);
  expect(names(bestAssets(files, "linux", "x64"))).toEqual(["Game-linux.AppImage"]);
  expect(names(bestAssets(files, "linux", "x64", undefined, "files", "windows"))).toEqual(["Game-win64.zip"]);
  // Back to Linux's, and a release with no Windows build: Linux's.
  expect(names(bestAssets(files, "linux", "x64", undefined, "files", "linux"))).toEqual(["Game-linux.AppImage"]);
  expect(names(bestAssets([files[0]], "linux", "x64", undefined, "files", "windows"))).toEqual(["Game-linux.AppImage"]);
  // A system this computer can't run isn't one to prefer.
  expect(names(bestAssets(files, "windows", "x64", undefined, undefined, "linux"))).toEqual(["Game-win64.zip"]);
});

test("a release has the build the player chose to get when that's what installing it would take", () => {
  const linux = asset("Game-linux.AppImage", "linux");
  const windows = asset("Game-win64.zip", "windows");
  const unplaced = asset("Game.zip", "unknown");
  expect(hasBuildFor([linux, windows], "linux", "x64", "windows")).toBe(true);
  expect(hasBuildFor([linux, windows], "linux", "x64", "linux")).toBe(true);
  // Linux's alone: choosing Windows' would install Linux's.
  expect(hasBuildFor([linux], "linux", "x64", "windows")).toBe(false);
  expect(hasBuildFor([windows], "linux", "x64", "linux")).toBe(false);
  // A file the site couldn't place could be either, as Get takes it for either.
  expect(hasBuildFor([unplaced], "linux", "x64", "windows")).toBe(true);
  expect(hasBuildFor([linux, unplaced], "linux", "x64", "windows")).toBe(false);
  // Windows Installer doesn't run through Wine.
  expect(hasBuildFor([linux, asset("Game-1.0-x64.msi", "windows")], "linux", "x64", "windows")).toBe(false);
});
