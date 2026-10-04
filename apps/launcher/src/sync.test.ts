import { expect, test } from "vitest";
import type { LibraryItem } from "./store";
import { applyServer, applyServerCollections, changesFrom, inCollection, joinAccount, type ServerItem } from "./sync";

const item = (id: string, extra: Partial<LibraryItem> = {}): LibraryItem => ({ id, slug: id, addedAt: 1, ...extra });
const server = (key: string, extra: Partial<ServerItem> = {}): ServerItem => ({
  key, entryId: key, slug: key, removed: false, updatedAt: 2, ...extra,
});

test("signing in adds the guest apps to the account and leaves another account's apps out", () => {
  const joined = joinAccount([item("a", { overrides: { name: "Mine" } }), item("b", { account: "other" })], "me");
  expect(joined.map((i) => i.id)).toEqual(["a"]);
  expect(changesFrom(joined, "me")).toEqual([{ key: "a", add: true, entryId: "a", name: "Mine" }]);
});

test("the account's values win except unsaved changes, and removals leave the library", () => {
  const local = [
    item("renamed", { account: "me", overrides: { name: "Here" }, pending: { name: true } }),
    item("gone", { account: "me" }),
  ];
  const result = applyServer(
    local,
    [server("renamed", { name: "There", artUrl: "https://x/art.png" }), server("gone", { removed: true }), server("new")],
    "me",
  );
  const byId = Object.fromEntries(result.map((i) => [i.id, i]));
  expect(byId.renamed.overrides).toMatchObject({ name: "Here", cover: "https://x/art.png" });
  expect(byId.gone).toBeUndefined();
  expect(byId.new.account).toBe("me");
  expect(changesFrom(result, "me")).toEqual([{ key: "renamed", name: "Here" }]);
});

test("collections: this device's join the account, the account's win unless changed here", () => {
  const shelf = { name: "Fav", tags: ["fav"], consoles: [], order: 0 };
  const result = applyServerCollections(
    [{ key: "guest", ...shelf }, { key: "edited", ...shelf, name: "Mine", account: "me", pending: true }, { key: "theirs", ...shelf, account: "other" }],
    [{ key: "edited", ...shelf, removed: false, updatedAt: 1 }, { key: "gone", ...shelf, removed: true, updatedAt: 1 }, { key: "new", ...shelf, removed: false, updatedAt: 1 }],
    "me",
  );
  expect(result.map((c) => [c.key, c.name, c.pending ?? false])).toEqual([["guest", "Fav", true], ["edited", "Mine", true], ["new", "Fav", false]]);
});

test("a collection matches every part it sets", () => {
  const app = { tags: ["Fav", "port"], consoles: ["n64"], installed: true };
  expect(inCollection({ tags: ["fav"], consoles: [] }, app)).toBe(true);
  expect(inCollection({ tags: [], consoles: ["n64", "snes"], installed: "yes" }, app)).toBe(true);
  expect(inCollection({ tags: ["fav"], consoles: ["snes"] }, app)).toBe(false);
  expect(inCollection({ tags: [], consoles: [], installed: "no" }, app)).toBe(false);
});

test("a shelf holds its picked apps, and with filters also what matches them, as the catalog filters", () => {
  const app = { id: "a", tags: [], consoles: ["n64"], installed: false, projectType: "port", aiLevel: "generated" };
  const makers = { n64: "Nintendo" };
  // Only picked apps, without filters.
  expect(inCollection({ tags: [], consoles: [], apps: ["a"] }, app)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], apps: ["b"] }, app)).toBe(false);
  // A maker's consoles, project types and AI use.
  expect(inCollection({ tags: [], consoles: ["maker:Nintendo"] }, app, makers)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], projectTypes: ["tool"] }, app)).toBe(false);
  expect(inCollection({ tags: [], consoles: [], ai: "no-generated" }, app)).toBe(false);
  expect(inCollection({ tags: [], consoles: [], ai: "no-ai" }, { ...app, aiLevel: "none" })).toBe(true);
});
