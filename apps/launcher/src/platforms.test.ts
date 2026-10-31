import { expect, test } from "vitest";
import { defaultPlatforms, forPlatforms, platformsOf, savedPlatforms } from "./platforms";

test("a device shows its own platform's apps at first, and Linux Windows' too", () => {
  expect(defaultPlatforms("windows")).toEqual(["windows"]);
  expect(defaultPlatforms("macos")).toEqual(["macos"]);
  expect(defaultPlatforms("android")).toEqual(["android"]);
  expect(defaultPlatforms("linux")).toEqual(["windows", "linux"]);
  // A device the library can't narrow to: every platform's.
  expect(defaultPlatforms("ios")).toEqual(["windows", "macos", "linux", "android"]);
});

test("the saved choice is kept in order; nothing usable saved is the default", () => {
  expect(platformsOf(["linux", "windows"], "android")).toEqual(["windows", "linux"]);
  expect(platformsOf(["linux", "ios", "beos"], "windows")).toEqual(["linux"]);
  expect(platformsOf(undefined, "android")).toEqual(["android"]);
  expect(platformsOf([], "linux")).toEqual(["windows", "linux"]);
  expect(platformsOf("windows", "macos")).toEqual(["macos"]);
});

test("only a choice other than the device's own is saved", () => {
  expect(savedPlatforms(["android"], "android")).toBeUndefined();
  expect(savedPlatforms(["linux", "windows"], "linux")).toBeUndefined();
  expect(savedPlatforms([], "windows")).toBeUndefined();
  expect(savedPlatforms(["linux"], "linux")).toEqual(["linux"]);
  expect(savedPlatforms(["android", "windows"], "android")).toEqual(["windows", "android"]);
});

test("an app shows for any of its platforms picked, or when its platforms aren't known", () => {
  expect(forPlatforms({ supportedOS: ["windows"] }, ["android"])).toBe(false);
  expect(forPlatforms({ supportedOS: ["windows", "android"] }, ["android"])).toBe(true);
  expect(forPlatforms({ supportedOS: ["windows"] }, ["windows", "linux"])).toBe(true);
  expect(forPlatforms({ supportedOS: ["ios"] }, ["android"])).toBe(false);
  expect(forPlatforms({ supportedOS: ["unknown"] }, ["android"])).toBe(true);
  expect(forPlatforms({ supportedOS: ["windows", "unknown"] }, ["macos"])).toBe(true);
  expect(forPlatforms({ supportedOS: [] }, ["macos"])).toBe(true);
});
