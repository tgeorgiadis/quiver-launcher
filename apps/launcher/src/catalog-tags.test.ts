import { describe, expect, test } from "vitest";
import { catalogTagCounts, hiddenEverywhere, hideAllEverywhere, hideEverywhere, hideNewTags, hideOnApp, hideableTags, listedTags, shownTags } from "./catalog-tags";

const isConsole = (tag: string) => tag === "n64";

test("an app's tags are the same list until one of them is hidden", () => {
  const tags = ["recomp", "nintendo", "n64"];
  expect(shownTags(undefined, "a", tags)).toBe(tags);
  expect(shownTags(hideOnApp({}, "b", "recomp", true), "a", tags)).toBe(tags);
  expect(shownTags(hideEverywhere({}, "zelda", true), "a", tags)).toBe(tags);
});

test("a tag hidden everywhere is off every app, whatever its case, and shown again comes back", () => {
  const hidden = hideEverywhere({}, "Recomp", true);
  expect(shownTags(hidden, "a", ["recomp", "nintendo"])).toEqual(["nintendo"]);
  expect(shownTags(hidden, "b", ["RECOMP"])).toEqual([]);
  expect(shownTags(hideEverywhere(hidden, "recomp", false), "a", ["recomp", "nintendo"])).toEqual(["recomp", "nintendo"]);
});

test("a tag unticked on one app is off that app alone", () => {
  const hidden = hideOnApp({}, "a", "nintendo", true);
  expect(shownTags(hidden, "a", ["recomp", "nintendo"])).toEqual(["recomp"]);
  expect(shownTags(hidden, "b", ["recomp", "nintendo"])).toEqual(["recomp", "nintendo"]);
  expect(hideOnApp(hidden, "a", "Nintendo", false).apps).toEqual({});
});

test("a console's tag is never hidden", () => {
  const hidden = hideNewTags(hideEverywhere({}, "n64", true), true, []);
  expect(shownTags(hidden, "a", ["recomp", "n64"], isConsole)).toEqual(["n64"]);
});

test("hiding new tags keeps the ones known then, and hides any the catalog adds after", () => {
  const hidden = hideNewTags(hideEverywhere({}, "recomp", true), true, ["recomp", "nintendo", "Nintendo"]);
  expect(hidden.shown).toEqual(["nintendo"]);
  expect(shownTags(hidden, "a", ["recomp", "nintendo", "widescreen"])).toEqual(["nintendo"]);
  // One the player shows stays shown; one they hide doesn't.
  const shown = hideEverywhere(hidden, "widescreen", false);
  expect(shownTags(shown, "a", ["recomp", "nintendo", "widescreen"])).toEqual(["nintendo", "widescreen"]);
  expect(hiddenEverywhere(hideEverywhere(shown, "nintendo", true), "nintendo")).toBe(true);
  // Turned off, only what the player hid stays hidden.
  expect(shownTags(hideNewTags(hidden, false, []), "a", ["recomp", "nintendo", "widescreen"])).toEqual(["nintendo", "widescreen"]);
});

test("hide all and show all", () => {
  const all = hideAllEverywhere({}, ["recomp", "nintendo"], true);
  expect(shownTags(all, "a", ["recomp", "nintendo"])).toEqual([]);
  expect(shownTags(hideAllEverywhere(all, ["recomp", "nintendo"], false), "a", ["recomp", "nintendo"])).toEqual(["recomp", "nintendo"]);
  // With new ones hidden too, show all shows them.
  const strict = hideNewTags({}, true, []);
  expect(shownTags(hideAllEverywhere(strict, ["recomp"], false), "a", ["recomp", "new"])).toEqual(["recomp"]);
});

test("the tags to untick on an app: all the catalog gave it but a console's and those hidden everywhere", () => {
  const hidden = hideOnApp(hideEverywhere({}, "recomp", true), "a", "nintendo", true);
  expect(hideableTags({ tags: ["n64"], allTags: ["recomp", "nintendo", "zelda", "n64"] }, hidden, { n64: "Nintendo 64" })).toEqual(["nintendo", "zelda"]);
  expect(hideableTags(undefined, hidden)).toEqual([]);
});

test("the catalog's tags with how many apps have each, hidden ones no app has among them", () => {
  expect(catalogTagCounts([{ tags: ["recomp", "n64"] }, { tags: ["Recomp", "zelda"] }], isConsole, ["old", "zelda"])).toEqual([
    { tag: "old", apps: 0 },
    { tag: "recomp", apps: 2 },
    { tag: "zelda", apps: 1 },
  ]);
});

describe("listedTags", () => {
  const tags = [
    { tag: "007", apps: 2 },
    { tag: "ace combat", apps: 3 },
    { tag: "decomp", apps: 9 },
    { tag: "Zelda", apps: 1 },
  ];
  const label = (tag: string) => (tag === "decomp" ? "Source port" : tag);
  const names = (list: { tag: string }[]) => list.map((x) => x.tag);

  test("all of them, in the order they came, when nothing is searched or filtered", () => {
    expect(names(listedTags(tags, undefined))).toEqual(["007", "ace combat", "decomp", "Zelda"]);
  });

  test("searches the name shown and the tag itself, every word, whatever the case", () => {
    expect(names(listedTags(tags, undefined, { search: "SOURCE", label }))).toEqual(["decomp"]);
    expect(names(listedTags(tags, undefined, { search: "decomp", label }))).toEqual(["decomp"]);
    expect(names(listedTags(tags, undefined, { search: " combat  ace " }))).toEqual(["ace combat"]);
    expect(names(listedTags(tags, undefined, { search: "nothing" }))).toEqual([]);
  });

  test("keeps the shown or the hidden ones, the catalog's later ones too when those are hidden", () => {
    const h = hideEverywhere({}, "zelda", true);
    expect(names(listedTags(tags, h, { filter: "hidden" }))).toEqual(["Zelda"]);
    expect(names(listedTags(tags, h, { filter: "shown" }))).toEqual(["007", "ace combat", "decomp"]);
    const later = hideNewTags({}, true, ["007"]);
    expect(names(listedTags(tags, later, { filter: "shown" }))).toEqual(["007"]);
  });

  test("the ones on the most apps first, without changing the list it was given", () => {
    expect(names(listedTags(tags, undefined, { sort: "apps" }))).toEqual(["decomp", "ace combat", "007", "Zelda"]);
    expect(names(tags)).toEqual(["007", "ace combat", "decomp", "Zelda"]);
  });
});
