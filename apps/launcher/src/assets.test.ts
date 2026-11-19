import { describe, expect, it, test } from "vitest";
import type { Asset, Os } from "@quiverlauncher/api";
import { installPreference, flatpakAsset, isFlatpakAsset, assetFilterFor, bestAssets, hasBuildFor, installable, installFormat, otherSystems, pickedBefore, releaseFor, runsHere } from "./assets";

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

test("preferring Flatpak puts a Flatpak bundle first, and falls back when there isn't one", () => {
  const files = [asset("Game.flatpak", "linux"), asset("Game.AppImage", "linux"), asset("Game-linux.zip", "linux")];
  expect(bestAssets(files, "linux", "x64", undefined, undefined, undefined, { preference: "flatpak" }).map((a) => a.filename)).toEqual(["Game.flatpak"]);
  // A zip whose name says it holds a Flatpak counts as one, ahead of an AppImage.
  expect(
    bestAssets([asset("Game-Flatpak.zip", "linux"), asset("Game.AppImage", "linux")], "linux", "x64", undefined, undefined, undefined, { preference: "flatpak" }).map((a) => a.filename),
  ).toEqual(["Game-Flatpak.zip"]);
  // Echoes Unbound-style: Flatpak with unknown arch still beats an arch-matched AppImage when preferred.
  expect(
    bestAssets(
      [asset("MetroidPrime2Port.flatpak", "linux", "unknown"), asset("MetroidPrime2-x86_64.AppImage", "linux", "x64")],
      "linux",
      "x64",
      undefined,
      undefined,
      undefined,
      { preference: "flatpak" },
    ).map((a) => a.filename),
  ).toEqual(["MetroidPrime2Port.flatpak"]);
  // No Flatpak: the usual order (archives before AppImage) is unchanged.
  expect(bestAssets([asset("Game.AppImage", "linux"), asset("Game-linux.zip", "linux")], "linux", "x64", undefined, undefined, undefined, { preference: "flatpak" }).map((a) => a.filename)).toEqual([
    "Game-linux.zip",
  ]);
  // A catalog filter still wins over preferring Flatpak.
  expect(bestAssets(files, "linux", "x64", "AppImage", undefined, undefined, { preference: "flatpak" }).map((a) => a.filename)).toEqual(["Game.AppImage"]);
  expect(isFlatpakAsset("Game.flatpak")).toBe(true);
  expect(isFlatpakAsset("Game-Flatpak.zip")).toBe(true);
  expect(isFlatpakAsset("Game.AppImage")).toBe(false);
  expect(isFlatpakAsset("readme-about-flatpak.txt")).toBe(false);
  expect(flatpakAsset(files, "linux", "x64")?.filename).toBe("Game.flatpak");
  expect(flatpakAsset([asset("Game.AppImage", "linux")], "linux", "x64")).toBeUndefined();
  // Wrong architecture: not offered.
  expect(flatpakAsset([asset("Game-aarch64.flatpak", "linux", "arm64"), asset("Game.AppImage", "linux")], "linux", "x64")).toBeUndefined();
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

test("a release whose only file is a Windows script (an installer that builds the app) installs on Windows only", () => {
  const script = asset("GT5-Installer.bat", "windows", "unknown");
  expect(bestAssets([script], "windows", "x64").map((a) => a.filename)).toEqual(["GT5-Installer.bat"]);
  expect(bestAssets([asset("Start.cmd", "windows", "unknown")], "windows", "x64").map((a) => a.filename)).toEqual(["Start.cmd"]);
  // A program or an archive comes first.
  expect(bestAssets([script, asset("Game-win64.zip", "windows")], "windows", "x64").map((a) => a.filename)).toEqual(["Game-win64.zip"]);
  expect(bestAssets([script, asset("Game.exe", "windows")], "windows", "x64").map((a) => a.filename)).toEqual(["Game.exe"]);
  // Wine has no PowerShell, which these scripts mostly use.
  expect(bestAssets([script], "linux", "x64")).toEqual([]);
  expect(runsHere(script, "linux", "x64")).toBe(false);
  expect(installFormat("GT5-Installer.bat")).toBe("executable");
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

test("new Linux install preferences select each format and keep automatic behavior", () => {
  const files = [asset("Game-Flatpak.zip", "linux"), asset("Game.tar.gz", "linux"), asset("Game.AppImage", "linux")];
  const select = (preference: "automatic" | "flatpak" | "appimage" | "archive") =>
    bestAssets(files, "linux", "x64", undefined, undefined, undefined, { preference });
  expect(select("automatic")).toEqual([files[0]]);
  expect(select("flatpak")).toEqual([files[0]]);
  expect(select("archive")).toEqual([files[1]]);
  expect(select("appimage")).toEqual([files[2]]);
});

test("preferences fall back without overriding architecture, catalog filters or platform choices", () => {
  const files = [asset("Game.zip", "linux"), asset("Game.AppImage", "linux", "arm64"), asset("Game.flatpak", "linux", "arm64"), asset("Game-win.zip", "windows")];
  for (const preference of ["appimage", "flatpak", "archive"] as const) {
    expect(bestAssets(files, "linux", "x64", undefined, undefined, undefined, { preference })).toEqual([files[0]]);
    expect(bestAssets(files, "linux", "x64", undefined, undefined, "windows", { preference })).toEqual([files[3]]);
  }
  const compatible = [files[0], asset("Game.AppImage", "linux")];
  expect(bestAssets(compatible, "linux", "x64", ".zip", undefined, undefined, { preference: "appimage" })).toEqual([files[0]]);
  const windows = [asset("Game.zip", "windows"), asset("Game.AppImage", "windows")];
  expect(bestAssets(windows, "windows", "x64", undefined, undefined, undefined, { preference: "appimage" })).toEqual([windows[0]]);
});

test("a remembered file wins before format and architecture ranking discard it", () => {
  const files = [asset("Game-vulkan.zip", "linux"), asset("Game-opengl.AppImage", "linux", "unknown")];
  expect(bestAssets(files, "linux", "x64", undefined, undefined, undefined, { preference: "archive", pick: "opengl" })).toEqual([files[1]]);
  const ambiguous = [asset("Game-opengl.zip", "linux"), files[1]];
  expect(bestAssets(ambiguous, "linux", "x64", undefined, undefined, undefined, { pick: "opengl" })).toEqual(ambiguous);
  expect(bestAssets(files, "linux", "x64", undefined, undefined, undefined, { pick: "renamed" })).toEqual(files);
});

test("updates retain their recorded format regardless of the global preference and never fall back", () => {
  const files = [asset("Game.zip", "linux"), asset("Game.AppImage", "linux"), asset("Game.flatpak", "linux")];
  for (const format of ["archive", "appimage", "flatpak"] as const) {
    for (const preference of ["automatic", "archive", "appimage", "flatpak"] as const) {
      expect(bestAssets(files, "linux", "x64", undefined, "files", undefined, { format, preference })).toEqual(files.filter((a) => installFormat(a.filename) === format));
    }
    expect(bestAssets(files.filter((a) => installFormat(a.filename) !== format), "linux", "x64", undefined, "files", undefined, { format })).toEqual([]);
  }
});

test("the legacy Flatpak checkbox is preserved until explicitly replaced, including Automatic", () => {
  expect(installPreference({})).toBe("automatic");
  expect(installPreference({ preferFlatpak: false })).toBe("automatic");
  expect(installPreference({ preferFlatpak: true })).toBe("flatpak");
  for (const preferredInstallFormat of ["automatic", "archive", "appimage", "flatpak"] as const)
    expect(installPreference({ preferredInstallFormat, preferFlatpak: true })).toBe(preferredInstallFormat);
});

describe("releaseFor", () => {
  const build = (version: string, ...systems: Os[]) => ({ version, assets: systems.map((os) => asset(os === "linux" ? `Game-${version}.AppImage` : `Game-${version}-${os}.zip`, os)) });
  // As Wario Land 4's: the newest release is Linux's alone.
  const releases = [build("linux-v0.1.0", "linux"), build("v0.3.2", "windows"), build("v0.3.1", "windows")];

  it("passes over another system's release", () => {
    expect(releaseFor(releases, "windows", "x64")?.version).toBe("v0.3.2");
    expect(releaseFor(releases, "windows", "x64", { platform: "windows", kind: "files" })?.version).toBe("v0.3.2");
  });

  it("finds nothing when no release has a build for it", () => {
    expect(releaseFor([build("linux-v0.1.0", "linux")], "windows", "x64", { platform: "windows" })).toBeUndefined();
  });

  it("keeps a Linux install to the build it has", () => {
    const windowsNewest = [build("v2", "windows"), build("v1", "linux")];
    expect(releaseFor(windowsNewest, "linux", "x64", { platform: "linux" })?.version).toBe("v1");
    expect(releaseFor(releases, "linux", "x64", { platform: "windows" })?.version).toBe("v0.3.2");
    expect(releaseFor([build("v1", "linux")], "linux", "x64", { platform: "windows" })).toBeUndefined();
  });

  it("lets Linux take Windows' build where no release has its own, or for a new install", () => {
    expect(releaseFor([build("v2", "windows")], "linux", "x64", { platform: "linux" })?.version).toBe("v2");
    expect(releaseFor([build("v2", "windows"), build("v1", "linux")], "linux", "x64")?.version).toBe("v2");
  });
});

test("otherSystems names the systems of a release with nothing for this computer", () => {
  const linux = asset("Game.AppImage", "linux");
  const windows = asset("Game-win.zip", "windows");
  expect(otherSystems([linux], "windows", "x64")).toEqual(["linux"]);
  expect(otherSystems([linux, asset("Game.apk", "android")], "windows", "x64")).toEqual(["linux", "android"]);
  // Nothing to say of one that runs here, through Wine, on another architecture, or with no files.
  expect(otherSystems([linux, windows], "windows", "x64")).toEqual([]);
  expect(otherSystems([windows], "linux", "x64")).toEqual([]);
  expect(otherSystems([windows], "windows", "arm64")).toEqual([]);
  expect(otherSystems([], "windows", "x64")).toEqual([]);
  // Nor do a checksum or a file with no platform count as a system.
  expect(otherSystems([linux, asset("Game.sha256", "windows"), asset("art.png", "unknown")], "windows", "x64")).toEqual(["linux"]);
});
