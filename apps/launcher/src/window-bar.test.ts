import { describe, expect, it, vi } from "vitest";
import { windowButtons } from "./window-bar";

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

  it("leave a Mac its own", () => {
    expect(windowButtons("macos", false, false)).toBe("none");
    expect(windowButtons("macos", false, true)).toBe("none");
  });
});
