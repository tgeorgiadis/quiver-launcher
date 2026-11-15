import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { runsWellShare, waysToPlay } from "./ways-to-play";

const app = (id: string, [recommended, reportIssues, reportBroken]: number[], supportedOS: Entry["supportedOS"] = ["windows"]) =>
  ({ id, name: id, recommended, reportIssues, reportBroken, supportedOS }) as Entry;

describe("ways to play", () => {
  it("gives a share of runs well only with enough reports", () => {
    expect(runsWellShare(app("a", [13, 6, 2]))).toBe(62);
    expect(runsWellShare(app("a", [5, 0, 0]))).toBe(100);
    expect(runsWellShare(app("a", [3, 0, 0]))).toBeUndefined();
    expect(runsWellShare(app("a", [0, 0, 0]))).toBeUndefined();
  });

  it("puts the best first, and what this computer can't run last", () => {
    const ghost = app("ghostship", [13, 6, 2]);
    const coop = app("coop", [8, 0, 0]);
    const recomp = app("recomp", [5, 0, 0]);
    const android = app("android", [3, 0, 0], ["android"]);
    const fresh = app("fresh", [0, 0, 0]);
    const ways = waysToPlay([ghost, fresh, android, recomp, coop], "windows");
    expect(ways.here.map((e) => e.id)).toEqual(["coop", "recomp", "ghostship", "fresh"]);
    expect(ways.elsewhere.map((e) => e.id)).toEqual(["android"]);
    expect(ways.top).toBe("coop");
  });

  it("ranks none top on too few reports, or a tie", () => {
    expect(waysToPlay([app("a", [4, 0, 0]), app("b", [0, 0, 0])], "windows").top).toBeUndefined();
    expect(waysToPlay([app("a", [6, 0, 0]), app("b", [6, 0, 0])], "windows").top).toBeUndefined();
    expect(waysToPlay([app("a", [6, 0, 0]), app("b", [5, 1, 0])], "windows").top).toBe("a");
  });

  it("counts Windows apps as runnable on Linux, as installing does", () => {
    const ways = waysToPlay([app("w", [1, 0, 0]), app("m", [1, 0, 0], ["macos"])], "linux");
    expect(ways.here.map((e) => e.id)).toEqual(["w"]);
    expect(ways.elsewhere.map((e) => e.id)).toEqual(["m"]);
  });
});
