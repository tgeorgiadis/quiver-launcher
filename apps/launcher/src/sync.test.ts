import { expect, test } from "vitest";
import type { LibraryItem } from "./store";
import { applyServer, changesFrom, joinAccount, type ServerItem } from "./sync";

const item = (id: string, extra: Partial<LibraryItem> = {}): LibraryItem => ({ id, slug: id, addedAt: 1, ...extra });
const server = (entryId: string, extra: Partial<ServerItem> = {}): ServerItem => ({
  entryId, slug: entryId, removed: false, updatedAt: 2, ...extra,
});

test("signing in adds this device's apps to the account and drops removals meant for another one", () => {
  const joined = joinAccount(
    [item("a", { overrides: { name: "Mine" } }), item("b", { account: "other", removed: true, pending: { removed: true } })],
    "me",
  );
  expect(joined.map((i) => i.id)).toEqual(["a"]);
  expect(changesFrom(joined, "me")).toEqual([{ entryId: "a", name: "Mine" }]);
});

test("the account's values win except unsaved changes, and removals spare installed apps", () => {
  const local = [
    item("renamed", { account: "me", overrides: { name: "Here" }, pending: { name: true } }),
    item("installed", { account: "me" }),
    item("gone", { account: "me" }),
  ];
  const result = applyServer(
    local,
    [
      server("renamed", { name: "There", artwork: "https://x/art.png" }),
      server("installed", { removed: true }),
      server("gone", { removed: true }),
      server("new"),
    ],
    "me",
    (id) => id === "installed",
  );
  const byId = Object.fromEntries(result.map((i) => [i.id, i]));
  expect(byId.renamed.overrides).toMatchObject({ name: "Here", cover: "https://x/art.png" });
  expect(byId.installed.removedElsewhere).toBe(true);
  expect(byId.gone).toBeUndefined();
  expect(byId.new.account).toBe("me");
});
