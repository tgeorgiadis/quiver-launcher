import { expect, test } from "vitest";
import type { Asset } from "@quiver/api";
import { assetFilterFor, bestAssets } from "./assets";

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
