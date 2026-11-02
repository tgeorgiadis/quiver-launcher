import { expect, test } from "vitest";
import type { Asset } from "@quiverlauncher/api";
import { formatsOf, matchesFormats, type ReleaseFormat } from "./release-formats";

const asset = (filename: string, os: Asset["os"] = "linux"): Asset => ({
  id: filename,
  url: `https://example.com/${filename}`,
  filename,
  os,
  architecture: "x64",
  format: filename.split(".").pop() ?? "",
});

test("detects Flatpak, AppImage, archives and Windows executables from release assets", () => {
  expect(
    formatsOf([
      asset("Game.flatpak"),
      asset("Game.AppImage"),
      asset("Game-linux.zip"),
      asset("Game-win64.exe", "windows"),
      asset("Game-1.0.msi", "windows"),
      asset("Game.apk", "android"),
    ]),
  ).toEqual(["flatpak", "appimage", "archive", "exe"]);
});

test("a Flatpak zip counts as Flatpak, not as a plain archive", () => {
  expect(formatsOf([asset("Game-Flatpak.zip")])).toEqual(["flatpak"]);
  expect(formatsOf([asset("Game.tar.xz"), asset("Game.7z")])).toEqual(["archive"]);
});

test("leaves out checksums, signatures and source archives", () => {
  expect(formatsOf([asset("Game.zip.sha256"), asset("checksums.txt"), asset("Game-source.zip"), asset("Game-src.tar.gz")])).toEqual([]);
  expect(formatsOf([asset("Game.zip"), asset("Game.zip.sig")])).toEqual(["archive"]);
});

test("a project with several formats appears under each", () => {
  const have = formatsOf([asset("Game.AppImage"), asset("Game-win64.zip", "windows"), asset("Game-win64.exe", "windows")]);
  expect(have).toEqual(["appimage", "archive", "exe"]);
  expect(matchesFormats(have, ["flatpak"])).toBe(false);
  expect(matchesFormats(have, ["appimage"])).toBe(true);
  expect(matchesFormats(have, ["flatpak", "exe"])).toBe(true);
});

test("no format filter matches every app; unknown formats match none of a picked filter", () => {
  expect(matchesFormats(undefined, [])).toBe(true);
  expect(matchesFormats(["archive"], [])).toBe(true);
  expect(matchesFormats(undefined, ["flatpak"])).toBe(false);
  expect(matchesFormats([], ["flatpak"] as ReleaseFormat[])).toBe(false);
});

test("counts every platform's files, not only this computer's", () => {
  expect(formatsOf([asset("Game-mac.zip", "macos"), asset("Game-win64.exe", "windows")])).toEqual(["archive", "exe"]);
});
