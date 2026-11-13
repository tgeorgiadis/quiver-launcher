import { describe, expect, it, vi } from "vitest";
import { moved, movesByPage, resized, resizeEdges, windowButtons } from "./window-bar";

vi.mock("./native", () => ({ native: {} }));
vi.mock("./store", () => ({}));

describe("the window's buttons in the top bar", () => {
  it("are there on Windows and Linux unless the system's title bar is on", () => {
    expect(windowButtons("windows", false, false)).toBe("window");
    expect(windowButtons("linux", false, false)).toBe("window");
    expect(windowButtons("windows", true, false)).toBe("none");
    expect(windowButtons("linux", true, false)).toBe("none");
  });

  it("give full screen, which has no title bar, a way out", () => {
    expect(windowButtons("windows", true, true)).toBe("fullscreen");
    expect(windowButtons("linux", false, true)).toBe("fullscreen");
  });

  it("are only Close in Steam's Game Mode, whatever the other settings", () => {
    expect(windowButtons("linux", false, false, true)).toBe("close");
    expect(windowButtons("linux", true, false, true)).toBe("close");
    expect(windowButtons("linux", false, true, true)).toBe("close");
  });

  it("leave a Mac its own", () => {
    expect(windowButtons("macos", false, false)).toBe("none");
    expect(windowButtons("macos", false, true)).toBe("none");
  });
});

describe("resizing the window from the page's edges", () => {
  const start = { x: 100, y: 50, width: 1000, height: 700, minWidth: 760, minHeight: 500 };

  it("moves only the side that's pulled", () => {
    expect(resized(start, "e", 40, 25)).toEqual({ x: 100, y: 50, width: 1040, height: 700 });
    expect(resized(start, "s", 40, 25)).toEqual({ x: 100, y: 50, width: 1000, height: 725 });
    expect(resized(start, "se", 40, 25)).toEqual({ x: 100, y: 50, width: 1040, height: 725 });
  });

  it("keeps the far side put when the left or top is pulled", () => {
    expect(resized(start, "w", 30, 0)).toEqual({ x: 130, y: 50, width: 970, height: 700 });
    expect(resized(start, "n", 0, -20)).toEqual({ x: 100, y: 30, width: 1000, height: 720 });
    expect(resized(start, "nw", -10, 10)).toEqual({ x: 90, y: 60, width: 1010, height: 690 });
  });

  it("stops at the smallest the window goes, without sliding it", () => {
    expect(resized(start, "e", -900, 0).width).toBe(760);
    expect(resized(start, "w", 900, 0)).toEqual({ x: 340, y: 50, width: 760, height: 700 });
    expect(resized(start, "n", 0, 900)).toEqual({ x: 100, y: 250, width: 1000, height: 500 });
  });

  it("rounds to whole pixels", () => {
    expect(resized(start, "se", 1.4, 2.6)).toEqual({ x: 100, y: 50, width: 1001, height: 703 });
  });

  it("are there only on Linux with Quiver's own title bar, un-maximized", () => {
    expect(resizeEdges("linux", "window", false)).toBe(true);
    expect(resizeEdges("linux", "window", true)).toBe(false);
    expect(resizeEdges("linux", "none", false)).toBe(false);
    expect(resizeEdges("linux", "fullscreen", false)).toBe(false);
    expect(resizeEdges("linux", "close", false)).toBe(false);
    expect(resizeEdges("windows", "window", false)).toBe(false);
  });
});

describe("moving the window from the page on an X11 desktop", () => {
  it("follows the pointer from where the window was", () => {
    expect(moved({ x: 300, y: 20 }, 45, -12)).toEqual({ x: 345, y: 8 });
    expect(moved({ x: 300, y: 20 }, -1.4, 2.6)).toEqual({ x: 299, y: 23 });
  });

  it("is for X11 only, and not for a maximized window", () => {
    expect(movesByPage(true, false)).toBe(true);
    expect(movesByPage(true, true)).toBe(false);
    expect(movesByPage(false, false)).toBe(false);
    expect(movesByPage(undefined, false)).toBe(false);
  });
});
