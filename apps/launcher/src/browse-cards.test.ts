import { expect, test } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { DEFAULT_BROWSE_CARDS, browseCardsOf, sampleEntry, savedBrowseCards } from "./browse-cards";

test("nothing saved is box art with every detail but New", () => {
  expect(browseCardsOf()).toEqual(DEFAULT_BROWSE_CARDS);
  expect(browseCardsOf({})).toEqual({ look: "box", details: ["icon", "subtitle", "kinds", "rating", "updated", "platforms", "ai", "setup"], wrap: false });
});

test("the saved choices are kept in order, and anything unknown is left out", () => {
  expect(browseCardsOf({ look: "wide", details: ["new", "rating", "sparkles"] })).toEqual({ look: "wide", details: ["rating", "new"], wrap: false });
  expect(browseCardsOf({ look: "list", details: [] })).toEqual({ look: "box", details: [], wrap: false });
  expect(browseCardsOf({ details: "rating" }).details).toEqual(DEFAULT_BROWSE_CARDS.details);
});

test("long names are cut short unless the player picked wrapping them", () => {
  expect(browseCardsOf({ wrap: true }).wrap).toBe(true);
  expect(browseCardsOf({ wrap: "yes" }).wrap).toBe(false);
  expect(savedBrowseCards({ ...DEFAULT_BROWSE_CARDS, wrap: true })).toEqual({ wrap: true });
  expect(savedBrowseCards({ ...DEFAULT_BROWSE_CARDS, wrap: false })).toBeUndefined();
});

test("a sample card shows the first app with the most details to turn on and off", () => {
  const app = (id: string, more: Partial<Entry> = {}) => ({ id, games: [], tags: [], recommended: 0, reportIssues: 0, reportBroken: 0, supportedOS: [], ...more }) as unknown as Entry;
  const bare = app("bare");
  const rated = app("rated", { recommended: 3 });
  const full = app("full", { recommended: 3, aiLevel: "assisted", tags: ["recomp"] });
  const alike = app("alike", { recommended: 3, aiLevel: "generated", tags: ["recomp"] });
  expect(sampleEntry([])).toBeUndefined();
  expect(sampleEntry([bare, rated, full, alike])).toBe(full);
  // No label isn't one to show.
  expect(sampleEntry([app("none", { aiLevel: "none" }), rated])).toBe(rated);
  // Easy setup is one, so its chip can be seen to turn on and off.
  const easy = app("easy", { recommended: 3, aiLevel: "assisted", tags: ["recomp"], launcher: { setupFiles: [{ id: "rom" }] } as Entry["launcher"] });
  expect(sampleEntry([full, easy])).toBe(easy);
});

test("only what differs from the defaults is saved", () => {
  expect(savedBrowseCards(DEFAULT_BROWSE_CARDS)).toBeUndefined();
  expect(savedBrowseCards({ look: "box", details: [...DEFAULT_BROWSE_CARDS.details].reverse(), wrap: false })).toBeUndefined();
  expect(savedBrowseCards({ look: "wide", details: DEFAULT_BROWSE_CARDS.details, wrap: false })).toEqual({ look: "wide" });
  expect(savedBrowseCards({ look: "box", details: ["new", "subtitle"], wrap: false })).toEqual({ details: ["subtitle", "new"] });
  expect(savedBrowseCards({ look: "box", details: [], wrap: false })).toEqual({ details: [] });
});
