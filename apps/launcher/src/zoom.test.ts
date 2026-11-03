import { describe, expect, it } from "vitest";
import { INTERFACE_SIZES, nearestInterfaceSize, pageZoom } from "./zoom";

describe("nearestInterfaceSize", () => {
  it("keeps a size that's on the list", () => {
    for (const size of INTERFACE_SIZES) expect(nearestInterfaceSize(size)).toBe(size);
  });

  it("moves Large and Extra large, from before there were percentages, to 125%", () => {
    expect(nearestInterfaceSize(1.15)).toBe(1.25);
    expect(nearestInterfaceSize(1.3)).toBe(1.25);
  });

  it("brings a size past either end back to it", () => {
    expect(nearestInterfaceSize(0.5)).toBe(0.9);
    expect(nearestInterfaceSize(3)).toBe(2);
  });
});

describe("pageZoom", () => {
  it("is the interface size alone outside TV mode, however large the screen", () => {
    expect(pageZoom(1.3, false, 3840, 2160)).toBe(1.3);
  });

  it("is a size larger at 1080p and on a Steam Deck in TV mode", () => {
    expect(pageZoom(1, true, 1920, 1080)).toBeCloseTo(1.15);
    expect(pageZoom(1, true, 1280, 800)).toBeCloseTo(1.15);
  });

  it("shows 4K as 1080p is in TV mode, with the interface size over it", () => {
    expect(pageZoom(1, true, 3840, 2160)).toBeCloseTo(2.3);
    expect(pageZoom(1.3, true, 3840, 2160)).toBeCloseTo(2.99);
  });

  it("stops where the page would run out of room for the top bar", () => {
    expect(pageZoom(2, true, 1280, 800)).toBeCloseTo(1280 / 1060);
    expect(pageZoom(2, true, 3840, 2160)).toBeCloseTo(3840 / 1060);
    expect(pageZoom(2, false, 1366, 768)).toBeCloseTo(1366 / 1060);
    expect(pageZoom(2, false, 3840, 2160)).toBe(2);
  });

  it("doesn't shrink a window already narrower than that", () => {
    expect(pageZoom(1.25, false, 720, 500)).toBe(1);
    expect(pageZoom(0.9, false, 720, 500)).toBe(0.9);
    expect(pageZoom(1, true, 720, 500)).toBe(1);
  });

  it("goes by the tighter side of a screen that isn't 16:9", () => {
    expect(pageZoom(1, true, 3440, 1440)).toBeCloseTo((1.15 * 1440) / 1080);
  });
});
