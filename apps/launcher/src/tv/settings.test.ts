import { describe, expect, it } from "vitest";
import { DEFAULT_SETTINGS, savedTvSettings, tvSettings } from "./settings";

describe("tvSettings", () => {
  it("starts from the defaults when nothing is saved", () => {
    expect(tvSettings(undefined)).toEqual(DEFAULT_SETTINGS);
    expect(tvSettings("nonsense")).toEqual(DEFAULT_SETTINGS);
  });

  it("uses the default for a value of the wrong kind", () => {
    const s = tvSettings({ updateBehavior: "sometimes", showHomeTab: "yes", monitor: "", afterGet: "library" });
    expect(s.updateBehavior).toBe("ask");
    expect(s.showHomeTab).toBe(true);
    expect(s.monitor).toBeNull();
    expect(s.afterGet).toBe("library");
  });

  it("saves only what differs from the defaults, and nothing once all are back", () => {
    expect(savedTvSettings(undefined, { reduceMotion: true })).toEqual({ reduceMotion: true });
    expect(savedTvSettings({ reduceMotion: true }, { allSort: "recent" })).toEqual({ reduceMotion: true, allSort: "recent" });
    expect(savedTvSettings({ reduceMotion: true }, { reduceMotion: false })).toBeUndefined();
  });
});
