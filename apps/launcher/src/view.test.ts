import { expect, test } from "vitest";
import { artFor, keepsTab, keptFilters, savedView, viewOf, DEFAULT_VIEW } from "./view";

test("a library view keeps only what differs from today's look, and ignores what it doesn't know", () => {
  expect(viewOf(undefined)).toEqual(DEFAULT_VIEW);
  expect(viewOf({ image: "icon", size: "huge" as never, names: false })).toEqual({ ...DEFAULT_VIEW, image: "icon", names: false });
  expect(savedView(DEFAULT_VIEW)).toBeUndefined();
  expect(savedView({ ...DEFAULT_VIEW, layout: "sidebar" })).toEqual({ layout: "sidebar" });
  expect(viewOf({ bothNames: false, nameOrder: "app" })).toEqual({ ...DEFAULT_VIEW, bothNames: false, nameOrder: "app" });
  expect(savedView({ ...DEFAULT_VIEW, continuePlaying: false })).toEqual({ continuePlaying: false });
  expect(viewOf({ byGame: true })).toEqual({ ...DEFAULT_VIEW, byGame: true });
  expect(savedView({ ...DEFAULT_VIEW, byGame: true })).toEqual({ byGame: true });
});

test("the details a library card shows are kept in order, the same list each time, without New or anything unknown", () => {
  expect(viewOf({}).details).toEqual(["subtitle"]);
  const view = viewOf({ details: ["updated", "new", "rating", "sparkles"] as never });
  expect(view.details).toEqual(["rating", "updated"]);
  expect(viewOf({ details: ["rating", "updated"] }).details).toBe(view.details);
  expect(viewOf({ details: "rating" as never }).details).toBe(DEFAULT_VIEW.details);
  expect(savedView({ ...DEFAULT_VIEW, details: ["subtitle"] })).toBeUndefined();
  expect(savedView({ ...DEFAULT_VIEW, details: ["updated", "kinds"] })).toEqual({ details: ["kinds", "updated"] });
  expect(savedView({ ...DEFAULT_VIEW, details: [] })).toEqual({ details: [] });
});

test("a library saved as a list, a layout there is no more, is a grid with names", () => {
  const view = viewOf({ layout: "list" as never, image: "icon", names: false });
  expect(view).toEqual({ ...DEFAULT_VIEW, image: "icon" });
  expect(savedView(view)).toEqual({ image: "icon" });
});

test("box art and icon cards fall back to the cover, then to nothing", () => {
  const all = { artwork: "icon", libraryArt: { capsule: "box", header: "cover" } };
  expect(artFor(all, "box")).toEqual([
    { src: "box", fit: "fill" },
    { src: "cover", fit: "letterbox" },
    { src: "icon", fit: "icon" },
  ]);
  expect(artFor(all, "icon")).toEqual([
    { src: "icon", fit: "icon" },
    { src: "cover", fit: "letterbox" },
  ]);
  expect(artFor({ libraryArt: { header: "cover" } }, "box")).toEqual([{ src: "cover", fit: "letterbox" }]);
  expect(artFor({}, "icon")).toEqual([]);
});

test("the library's filters and tab come back as the player left them, when the library still has them", () => {
  expect(keptFilters({ installed: "yes", console: "n64", ai: "no-ai" })).toEqual({ installed: "yes", console: "n64", ai: "no-ai" });
  expect(keptFilters({ installed: "maybe", ai: "some", tag: "" })).toEqual({});
  expect(keptFilters(undefined)).toEqual({});
  // Installed and Not installed were tabs too: left on one, the library opens with the filter on.
  expect(keptFilters({ console: "n64" }, "installed")).toEqual({ console: "n64", installed: "yes" });
  expect(keptFilters({ installed: "yes" }, "not-installed")).toEqual({ installed: "no" });
  expect(keptFilters({ installed: "yes" }, "racing")).toEqual({ installed: "yes" });
  const playlists = [{ key: "racing" }, { key: "gone", removed: true }];
  expect(keepsTab("installed", playlists)).toBe(false);
  expect(keepsTab("racing", playlists)).toBe(true);
  expect(keepsTab("gone", playlists)).toBe(false);
  expect(keepsTab("updates", playlists)).toBe(false);
  expect(keepsTab("hidden", playlists)).toBe(false);
  expect(keepsTab(undefined, playlists)).toBe(false);
});
