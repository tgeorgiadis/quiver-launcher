import { describe, expect, it } from "vitest";
import { GridNavigator } from "./grid";

describe("GridNavigator", () => {
  // 10 tiles in 4 columns:
  //  0 1 2 3
  //  4 5 6 7
  //  8 9
  const grid = (index: number) => new GridNavigator().reset(10, 4, index);

  it("moves within the grid", () => {
    expect(grid(5).move("left")).toEqual({ moved: true, index: 4 });
    expect(grid(5).move("right")).toEqual({ moved: true, index: 6 });
    expect(grid(5).move("up")).toEqual({ moved: true, index: 1 });
    expect(grid(5).move("down")).toEqual({ moved: true, index: 9 });
  });

  it("goes left and right in reading order, across rows", () => {
    expect(grid(4).move("left")).toEqual({ moved: true, index: 3 });
    expect(grid(8).move("left")).toEqual({ moved: true, index: 7 });
    expect(grid(3).move("right")).toEqual({ moved: true, index: 4 });
    expect(grid(7).move("right")).toEqual({ moved: true, index: 8 });
  });

  it("goes round: right from the last tile to the first, left from the first to the last", () => {
    expect(grid(9).move("right")).toEqual({ moved: true, index: 0 });
    expect(grid(0).move("left")).toEqual({ moved: true, index: 9 });
  });

  it("reports an edge above and below the grid, and anywhere in a grid of one", () => {
    expect(new GridNavigator().reset(1, 4).move("right")).toEqual({ moved: false, edge: "right" });
    expect(new GridNavigator().reset(1, 4).move("left")).toEqual({ moved: false, edge: "left" });
    expect(grid(2).move("up")).toEqual({ moved: false, edge: "up" });
    expect(grid(9).move("down")).toEqual({ moved: false, edge: "down" });
  });

  it("goes down into a shorter last row onto its last tile", () => {
    expect(grid(7).move("down")).toEqual({ moved: true, index: 9 });
    expect(grid(6).move("down")).toEqual({ moved: true, index: 9 });
  });

  it("has nowhere to go when empty", () => {
    expect(new GridNavigator().reset(0, 4).move("down")).toEqual({ moved: false, edge: "down" });
  });

  it("keeps the index when the columns change, clamped to the tiles there are", () => {
    const nav = grid(9);
    nav.reset(10, 6, nav.index);
    expect(nav.index).toBe(9);
    expect(nav.move("up")).toEqual({ moved: true, index: 3 });
    expect(new GridNavigator().reset(3, 4, 9).index).toBe(2);
    expect(new GridNavigator().reset(3, 0).columns).toBe(1);
  });
});
