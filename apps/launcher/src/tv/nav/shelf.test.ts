import { describe, expect, it } from "vitest";
import { ShelfNavigator } from "./shelf";

describe("ShelfNavigator", () => {
  it("moves along a row without wrapping", () => {
    const nav = new ShelfNavigator().reset([3, 5]);
    expect(nav.move("left")).toEqual({ moved: false, edge: "left" });
    nav.move("right");
    nav.move("right");
    expect(nav.move("right")).toEqual({ moved: false, edge: "right" });
    expect(nav.col).toBe(2);
  });

  it("comes back to the column each row was left at", () => {
    const nav = new ShelfNavigator().reset([6, 6, 6]);
    nav.move("right");
    nav.move("right"); // row 0, col 2
    nav.move("down"); // row 1 was never visited: col 0
    expect([nav.row, nav.col]).toEqual([1, 0]);
    nav.move("right");
    nav.move("right");
    nav.move("right"); // row 1, col 3
    nav.move("up");
    expect([nav.row, nav.col]).toEqual([0, 2]);
    nav.move("down");
    expect([nav.row, nav.col]).toEqual([1, 3]);
  });

  it("clamps a remembered column to a row that got shorter", () => {
    const nav = new ShelfNavigator().reset([8, 4]);
    nav.move("down");
    for (let i = 0; i < 3; i++) nav.move("right"); // row 1, col 3
    nav.move("up");
    nav.reset([8, 2]);
    nav.move("down");
    expect([nav.row, nav.col]).toEqual([1, 1]);
  });

  it("skips empty rows and stops at the top and bottom", () => {
    const nav = new ShelfNavigator().reset([2, 0, 3]);
    expect(nav.move("up")).toEqual({ moved: false, edge: "up" });
    expect(nav.move("down")).toEqual({ moved: true, pos: { row: 2, col: 0 } });
    expect(nav.move("down")).toEqual({ moved: false, edge: "down" });
  });

  it("keeps its place when the shelves change, moving off a row that emptied", () => {
    const nav = new ShelfNavigator().reset([4, 4]);
    nav.move("down");
    nav.move("right");
    nav.reset([4, 1]);
    expect([nav.row, nav.col]).toEqual([1, 0]);
    nav.reset([4, 0]);
    expect([nav.row, nav.col]).toEqual([0, 0]);
    expect(new ShelfNavigator().reset([]).move("down")).toEqual({ moved: false, edge: "down" });
  });
});
