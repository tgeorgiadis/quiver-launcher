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
  it("is the interface size, however large the screen", () => {
    expect(pageZoom(1.3, false, 3840)).toBe(1.3);
  });

  it("is none in TV mode, which sizes itself", () => {
    expect(pageZoom(1.3, true, 3840)).toBe(1);
  });

  it("stops where the page would run out of room for the top bar", () => {
    expect(pageZoom(2, false, 1366)).toBeCloseTo(1366 / 1060);
    expect(pageZoom(2, false, 3840)).toBe(2);
  });

  it("doesn't shrink a window already narrower than that", () => {
    expect(pageZoom(1.25, false, 720)).toBe(1);
    expect(pageZoom(0.9, false, 720)).toBe(0.9);
  });
});
