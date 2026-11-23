import { expect, test } from "vitest";
import type { LibraryItem } from "./store";
import { applyServer, applyServerCollections, changesFrom, inCollection, joinAccount, movedKey, pickedOutside, reordered, withoutApp, type Collection, type ServerItem } from "./sync";

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
  const playlist = { name: "Fav", tags: ["fav"], consoles: [], order: 0 };
  const result = applyServerCollections(
    [{ key: "guest", ...playlist }, { key: "edited", ...playlist, name: "Mine", account: "me", pending: true }, { key: "theirs", ...playlist, account: "other" }],
    [{ key: "edited", ...playlist, removed: false, updatedAt: 1 }, { key: "gone", ...playlist, removed: true, updatedAt: 1 }, { key: "new", ...playlist, removed: false, updatedAt: 1 }],
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

test("a playlist holds its picked apps, and with filters also what matches them, as the catalog filters", () => {
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

test("apps on this computer only stay on a playlist when the account's copy of it arrives", () => {
  const local = [{ key: "s", name: "Mine", tags: [], consoles: [], apps: ["entry_a", "local:game"], order: 0, account: "me" }];
  const [playlist] = applyServerCollections(local, [{ key: "s", name: "Mine", tags: [], consoles: [], apps: ["entry_a", "entry_b"], order: 0, removed: false, updatedAt: 2 }], "me");
  expect(playlist.apps).toEqual(["entry_a", "entry_b", "local:game"]);
});

test("a playlist's words match apps whose name or games have them all", () => {
  const app = { tags: [], consoles: [], installed: true, names: ["2 Ship 2 Harkinian", "The Legend of Zelda: Majora's Mask"] };
  expect(inCollection({ tags: [], consoles: [], search: "zelda" }, app)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], search: "Zelda mask" }, app)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], search: "mario" }, app)).toBe(false);
  // Blank words aren't a filter: only picked apps.
  expect(inCollection({ tags: [], consoles: [], search: "  " }, app)).toBe(false);
});

test("a playlist's words and icon stay on this device when the account's copy of it arrives", () => {
  const local = [{ key: "z", name: "Zelda", tags: [], consoles: [], search: "zelda", icon: "entry_a", order: 0, account: "me" }];
  const [playlist] = applyServerCollections(local, [{ key: "z", name: "Zelda games", tags: [], consoles: [], apps: [], order: 0, removed: false, updatedAt: 2 }], "me");
  expect(playlist).toMatchObject({ name: "Zelda games", search: "zelda", icon: "entry_a" });
});

test("a playlist's Managed by holds the apps this computer says the player, or Quiver, looks after", () => {
  const app = { id: "a", tags: [], consoles: [], installed: false };
  const manual = { ...app, manual: true };
  expect(inCollection({ tags: [], consoles: [], managed: "manual" }, manual)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], managed: "manual" }, app)).toBe(false);
  expect(inCollection({ tags: [], consoles: [], managed: "quiver" }, app)).toBe(true);
  expect(inCollection({ tags: [], consoles: [], managed: "quiver" }, manual)).toBe(false);
  // With another filter, an app has to match both.
  expect(inCollection({ tags: ["fav"], consoles: [], managed: "manual" }, manual)).toBe(false);
  // Hand-picked apps stay on it whoever manages them.
  expect(inCollection({ tags: [], consoles: [], managed: "manual", apps: ["a"] }, app)).toBe(true);
});

test("a playlist's Managed by syncs like its other filters: the account's, unless changed here", () => {
  const local = [{ key: "m", name: "Mine", tags: [], consoles: [], managed: "manual" as const, order: 0, account: "me" }];
  const server = (managed?: "quiver" | "manual") => [{ key: "m", name: "Mine", tags: [], consoles: [], managed, order: 0, removed: false, updatedAt: 2 }];
  expect(applyServerCollections(local, server("quiver"), "me")[0].managed).toBe("quiver");
  expect(applyServerCollections(local, server(), "me")[0].managed).toBeUndefined();
  expect(applyServerCollections([{ ...local[0], pending: true }], server("quiver"), "me")[0].managed).toBe("manual");
});

test("a playlist's platforms sync like its other filters: the account's, unless changed here", () => {
  const local = [{ key: "p", name: "Phone", tags: [], consoles: [], platforms: ["android" as const], order: 0, account: "me" }];
  const server = (platforms?: ("android" | "linux")[]) => [{ key: "p", name: "Phone", tags: [], consoles: [], platforms, order: 0, removed: false, updatedAt: 2 }];
  expect(applyServerCollections(local, server(["linux"]), "me")[0].platforms).toEqual(["linux"]);
  expect(applyServerCollections(local, server(), "me")[0].platforms).toBeUndefined();
  expect(applyServerCollections([{ ...local[0], pending: true }], server(["linux"]), "me")[0].platforms).toEqual(["android"]);
});

test("a playlist's platforms match apps with a release for any of them, as the library goes by them", () => {
  const app = (platforms?: ("windows" | "linux" | "android" | "unknown")[]) => ({ tags: [], consoles: [], installed: false, platforms });
  expect(inCollection({ tags: [], consoles: [], platforms: ["android"] }, app(["windows", "android"]))).toBe(true);
  expect(inCollection({ tags: [], consoles: [], platforms: ["android", "linux"] }, app(["linux"]))).toBe(true);
  expect(inCollection({ tags: [], consoles: [], platforms: ["android"] }, app(["windows"]))).toBe(false);
  // One whose platforms aren't known yet.
  expect(inCollection({ tags: [], consoles: [], platforms: ["android"] }, app(["unknown"]))).toBe(true);
  expect(inCollection({ tags: [], consoles: [], platforms: ["android"] }, app())).toBe(true);
  // With the other filters, all of them.
  expect(inCollection({ tags: ["recomp"], consoles: [], platforms: ["android"] }, app(["android"]))).toBe(false);
});

test("the consoles the player picked sync like tags: sent when changed here, the account's otherwise", () => {
  const local = [
    item("picked", { account: "me", overrides: { consoles: ["ps4"] }, pending: { consoles: true } }),
    item("reset", { account: "me", pending: { consoles: true } }),
    item("elsewhere", { account: "me", overrides: { consoles: ["ps4"] } }),
  ];
  const result = applyServer(
    local,
    [server("picked", { consoles: ["n64"] }), server("reset", { consoles: ["n64"] }), server("elsewhere", { consoles: ["pc"] })],
    "me",
  );
  const byId = Object.fromEntries(result.map((i) => [i.id, i]));
  expect(byId.picked.overrides?.consoles).toEqual(["ps4"]);
  expect(byId.reset.overrides?.consoles).toBeUndefined();
  expect(byId.elsewhere.overrides?.consoles).toEqual(["pc"]);
  expect(changesFrom(result, "me")).toEqual([
    { key: "picked", consoles: ["ps4"] },
    { key: "reset", consoles: null },
  ]);
  // Signing in sends the pick with the app.
  const joined = joinAccount([item("a", { overrides: { consoles: ["ps4"] } })], "me");
  expect(changesFrom(joined, "me")).toEqual([{ key: "a", add: true, entryId: "a", consoles: ["ps4"] }]);
});

test("a playlist shows a picked catalog app that isn't in the library, not one the player added and has since removed (issue 435)", () => {
  const catalog = { zelda: {}, "github:me/port": {}, "local:abc": {}, mine: {} };
  const playlist = { apps: ["mine", "zelda", "github:me/port", "local:abc", "unknown"] };
  // In the library still: shown as the library's, whatever it is.
  expect(pickedOutside(playlist, (id) => id === "mine" || id === "github:me/port", catalog)).toEqual(["zelda"]);
  // Removed from it: the catalog's app can be opened and added again, the player's own can't.
  expect(pickedOutside(playlist, () => false, catalog)).toEqual(["mine", "zelda"]);
  expect(pickedOutside(undefined, () => false, catalog)).toEqual([]);
});

test("removing an app the player added takes it off their playlists, to save to the account", () => {
  const list = (key: string, extra: Partial<Collection> = {}): Collection => ({ key, name: key, tags: [], consoles: [], order: 0, ...extra });
  const untouched = list("other", { apps: ["zelda"] });
  const followed = list("followed", { follows: "someones" });
  const [switchList, guest, other, theirs] = withoutApp(
    [list("switch", { apps: ["zelda", "github:me/port"], icon: "github:me/port", account: "me" }), list("guest", { apps: ["github:me/port"] }), untouched, followed],
    "github:me/port",
    "me",
  );
  expect(switchList).toEqual(list("switch", { apps: ["zelda"], account: "me", pending: true }));
  expect(guest).toEqual(list("guest", { apps: [], account: "me", pending: true }));
  expect(other).toBe(untouched);
  expect(theirs).toBe(followed);
  // Signed out: nothing to save.
  expect(withoutApp([list("guest", { apps: ["local:abc"] })], "local:abc")[0].pending).toBeUndefined();
});

test("moving a playlist puts it before another, or last", () => {
  const keys = ["a", "b", "c", "d"];
  expect(movedKey(keys, "c", "a")).toEqual(["c", "a", "b", "d"]);
  expect(movedKey(keys, "a", "c")).toEqual(["b", "a", "c", "d"]);
  expect(movedKey(keys, "b")).toEqual(["a", "c", "d", "b"]);
  expect(movedKey(keys, "b", "b")).toEqual(keys);
  expect(movedKey(keys, "b", "gone")).toEqual(keys);
});

test("reordering numbers the playlists and marks only the moved ones to save", () => {
  const list = (key: string, order: number, extra: Partial<Collection> = {}): Collection => ({ key, name: key, tags: [], consoles: [], order, ...extra });
  const a = list("a", 0, { account: "me" });
  const gone = list("gone", 1, { removed: true, account: "me", pending: true });
  const b = list("b", 1, { account: "me" });
  const c = list("c", 1);
  const [na, ngone, nb, nc] = reordered([a, gone, b, c], ["a", "c", "b"], "me");
  expect(na).toBe(a);
  expect(ngone).toBe(gone);
  expect(nb).toEqual({ ...b, order: 2, pending: true });
  // Ties (two at 1) are untied, and one never synced joins the account.
  expect(nc).toBe(c);
  expect(reordered([a, b, c], ["c", "a", "b"], "me").map((x) => [x.order, x.pending])).toEqual([[1, true], [2, true], [0, true]]);
  // Signed out: nothing to save.
  expect(reordered([list("x", 0), list("y", 1)], ["y", "x"]).map((x) => x.pending)).toEqual([undefined, undefined]);
});
