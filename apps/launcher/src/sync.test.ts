import { expect, test } from "vitest";
import type { LibraryItem } from "./store";
import { applyServer, changesFrom, joinAccount, type ServerItem } from "./sync";

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
