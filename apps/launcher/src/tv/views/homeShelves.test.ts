import { describe, expect, it } from "vitest";
import type { TvApp, TvState } from "../bridge";
import { appAt, homeShelves, rowLength } from "./homeShelves";

const app = (id: string, state: TvState, over: Partial<TvApp> = {}) => ({ id, title: id, state, ...over }) as TvApp;

describe("homeShelves", () => {
  it("orders recently played by last played and leaves out never-played and uninstalled apps", () => {
    const shelves = homeShelves(
      [app("a", "installed", { lastPlayed: 10 }), app("b", "installed", { lastPlayed: 30 }), app("c", "installed"), app("d", "notInstalled", { lastPlayed: 99 })],
    );
    expect(shelves.find((s) => s.id === "recent")!.apps.map((a) => a.id)).toEqual(["b", "a"]);
  });

  it("lists updates with an Update all card first", () => {
    const shelves = homeShelves([app("z", "updateAvailable"), app("y", "updateAvailable"), app("x", "installed")]);
    const updates = shelves.find((s) => s.id === "updates")!;
    expect(updates.apps.map((a) => a.id)).toEqual(["y", "z"]);
    expect(rowLength(updates)).toBe(3);
    expect(appAt(updates, 0)).toBeNull();
    expect(appAt(updates, 1)!.id).toBe("y");
  });

  it("orders recently installed by when it was installed, else when it was added", () => {
    const shelves = homeShelves([app("a", "installed", { addedAt: 100 }), app("b", "installed", { addedAt: 900 }), app("c", "installed", { addedAt: 1, installedAt: 500 })]);
    expect(shelves.find((s) => s.id === "installed")!.apps.map((a) => a.id)).toEqual(["b", "c", "a"]);
  });

  it("shows no empty shelves", () => {
    expect(homeShelves([app("a", "installed")]).map((s) => s.id)).toEqual(["installed"]);
    expect(homeShelves([])).toEqual([]);
  });
});
