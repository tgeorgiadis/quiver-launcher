import { describe, expect, it } from "vitest";
import { tickRange, tickState, tickToggled, withIds, withOwnTag } from "./ticking";

describe("selecting many library apps", () => {
  it("says whether all, some or none of the selected apps have something", () => {
    const on = new Set(["a", "b"]);
    expect(tickState(["a", "b"], (id) => on.has(id))).toBe("all");
    expect(tickState(["a", "c"], (id) => on.has(id))).toBe("some");
    expect(tickState(["c", "d"], (id) => on.has(id))).toBe("none");
  });

  it("selects a range in the order the library shows, either way, keeping what was selected", () => {
    const order = ["a", "b", "c", "d", "e"];
    expect([...tickRange(order, "b", "d", new Set(["e"]))].sort()).toEqual(["b", "c", "d", "e"]);
    expect([...tickRange(order, "d", "b", new Set())].sort()).toEqual(["b", "c", "d"]);
  });

  it("selects just the one clicked when the last one clicked isn't showing", () => {
    expect([...tickRange(["a", "b", "c"], "z", "b", new Set())]).toEqual(["b"]);
    expect([...tickRange(["a", "b", "c"], undefined, "c", new Set(["a"]))].sort()).toEqual(["a", "c"]);
  });

  it("selects a game's ports together, or unselects them together once all are", () => {
    expect([...tickToggled(new Set(["a"]), ["a", "b"])].sort()).toEqual(["a", "b"]);
    expect([...tickToggled(new Set(["a", "b", "c"]), ["a", "b"])]).toEqual(["c"]);
  });

  it("puts apps on a list once, or takes them off", () => {
    expect(withIds(["a"], ["a", "b"], true)).toEqual(["a", "b"]);
    expect(withIds(undefined, ["a"], true)).toEqual(["a"]);
    expect(withIds(["a", "b", "c"], ["a", "c"], false)).toEqual(["b"]);
  });

  it("adds a tag an app has in another case only once, and no more than 20", () => {
    expect(withOwnTag(["Favs"], "favs", true)).toEqual(["Favs"]);
    expect(withOwnTag(["a"], "b", true)).toEqual(["a", "b"]);
    expect(withOwnTag(Array.from({ length: 20 }, (_, i) => `t${i}`), "new", true)).toHaveLength(20);
    expect(withOwnTag(["Favs", "b"], "FAVS", false)).toEqual(["b"]);
  });
});
