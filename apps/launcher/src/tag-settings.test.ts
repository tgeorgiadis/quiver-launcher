import { expect, test } from "vitest";
import { tagCounts } from "./tag-settings";
import type { LibraryItem } from "./store";

test("each of the player's tags once, as first written, with how many apps in the library have it, by name", () => {
  const item = (id: string, tags: string[], removed?: boolean): LibraryItem => ({ id, slug: id, addedAt: 0, overrides: { tags }, removed });
  expect(tagCounts([item("a", ["ps3", "Zelda"]), item("b", ["PS3"]), item("c", ["gone"], true), item("d", ["2d"])])).toEqual([
    { tag: "2d", apps: 1 },
    { tag: "ps3", apps: 2 },
    { tag: "Zelda", apps: 1 },
  ]);
});
