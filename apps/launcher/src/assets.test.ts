import { expect, test } from "vitest";
import type { Asset } from "@quiver/api";
import { bestAssets } from "./assets";

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

test("falls back to files with no known platform", () => {
  const files = [asset("Game.zip", "unknown", "unknown"), asset("Game-mac.zip", "macos")];
  expect(bestAssets(files, "windows", "x64").map((a) => a.filename)).toEqual(["Game.zip"]);
});
