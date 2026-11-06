import { describe, expect, it } from "vitest";
import type { TvApp } from "../bridge";
import { dominantColors, withArtOverrides } from "./art";

const app = (id: string) => ({ id, art: { capsule: `cat/${id}.jpg`, hero: `hero/${id}.jpg`, logo: `logo/${id}.png`, icon: `icon/${id}.png` } }) as TvApp;

describe("withArtOverrides", () => {
  it("puts the player's own pictures over the catalog's, picture by picture", () => {
    const [a] = withArtOverrides([app("a")], { a: { art: { grid: "C:/mine.png" } } });
    expect(a.art).toEqual({ capsule: "C:/mine.png", hero: "hero/a.jpg", logo: "logo/a.png", icon: "icon/a.png" });
  });

  it("leaves apps without their own pictures as the same objects", () => {
    const apps = [app("a"), app("b")];
    const out = withArtOverrides(apps, { b: { playCount: 3, art: {} } });
    expect(out[0]).toBe(apps[0]);
    expect(out[1]).toBe(apps[1]);
  });
});

describe("dominantColors", () => {
  const pixels = (...colors: [number, number, number, number, number][]) =>
    colors.flatMap(([r, g, b, a, count]) => Array.from({ length: count }, () => [r, g, b, a]).flat());

  it("finds the main colour and the next one", () => {
    const colors = dominantColors(pixels([200, 30, 30, 255, 50], [30, 60, 200, 255, 20]));
    expect(colors).toEqual(["rgb(150 23 23)", "rgb(14 27 90)"]);
  });

  it("uses a darker shade of the main colour when there's no real second one", () => {
    expect(dominantColors(pixels([200, 30, 30, 255, 50], [30, 60, 200, 255, 2]))).toEqual(["rgb(150 23 23)", "rgb(60 9 9)"]);
  });

  it("ignores transparent, grey, black and white pixels, and gives up on an icon with no colour", () => {
    expect(dominantColors(pixels([0, 0, 0, 255, 40], [255, 255, 255, 255, 40], [120, 120, 120, 255, 40], [200, 30, 30, 0, 40]))).toBeNull();
  });
});
