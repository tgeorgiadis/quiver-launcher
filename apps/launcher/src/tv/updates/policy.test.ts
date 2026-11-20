import { describe, expect, it } from "vitest";
import type { TvApp } from "../bridge";
import { decide, noteLines } from "./policy";

const app = (over: Partial<TvApp> = {}) => ({ state: "updateAvailable", latestVersion: "v1.2.0", updates: undefined, ...over }) as TvApp;

describe("decide", () => {
  it("plays straight away when there's no update", () => {
    for (const behavior of ["ask", "off", "auto"] as const) expect(decide(app({ state: "installed" }), behavior)).toBe("launch");
  });

  it("asks, plays or updates first by TV mode's setting", () => {
    expect(decide(app(), "ask")).toBe("ask");
    expect(decide(app(), "off")).toBe("launch");
    expect(decide(app(), "auto")).toBe("update");
  });

  it("updates first an app set in desktop mode to install updates automatically", () => {
    expect(decide(app({ updates: "auto" }), "ask")).toBe("update");
    expect(decide(app({ updates: "auto" }), "off")).toBe("update");
  });
});

describe("noteLines", () => {
  it("keeps the first lines of the notes, without Markdown marks or blank lines", () => {
    const notes = "## What's new\n\n- **Faster** loading\n* Fixed `audio`\n1. Widescreen\n\nmore\nand more\nand more";
    expect(noteLines(notes)).toEqual(["What's new", "Faster loading", "Fixed audio", "Widescreen", "more", "and more"]);
    expect(noteLines(undefined)).toEqual([]);
  });
});
