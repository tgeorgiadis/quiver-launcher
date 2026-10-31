import { expect, test } from "vitest";
import { tvModeOn } from "./store";

test("TV mode comes on by itself in Steam's Game Mode, unless turned off, and anywhere when turned on", () => {
  expect(tvModeOn({}, { gameMode: true })).toBe(true);
  expect(tvModeOn({}, { gameMode: false })).toBe(false);
  // An older build's config has no gameMode.
  expect(tvModeOn({}, {})).toBe(false);
  expect(tvModeOn({ tvMode: "off" }, { gameMode: true })).toBe(false);
  expect(tvModeOn({ tvMode: "on" }, { gameMode: false })).toBe(true);
});
