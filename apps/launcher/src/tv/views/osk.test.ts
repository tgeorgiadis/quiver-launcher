import { describe, expect, it } from "vitest";
import { LAYOUT, moveKey } from "./Osk";

describe("moveKey", () => {
  it("goes round a row", () => {
    expect(moveKey(LAYOUT, { r: 1, c: 0 }, "left")).toEqual({ r: 1, c: 9 });
    expect(moveKey(LAYOUT, { r: 1, c: 9 }, "right")).toEqual({ r: 1, c: 0 });
  });

  it("goes up and down to the key at the same spot, across wide keys", () => {
    // "c" is fourth on the bottom letter row; below it is the space bar (which starts at the third tenth).
    expect(moveKey(LAYOUT, { r: 3, c: 2 }, "down")).toEqual({ r: 4, c: 2 });
    // From the delete key (tenths 6–7) up to "m" (the seventh).
    expect(moveKey(LAYOUT, { r: 4, c: 3 }, "up")).toEqual({ r: 3, c: 6 });
    expect(moveKey(LAYOUT, { r: 0, c: 4 }, "up")).toEqual({ r: 0, c: 4 });
    expect(moveKey(LAYOUT, { r: 4, c: 4 }, "down")).toEqual({ r: 4, c: 4 });
  });
});
