import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { browseShown, hiddenMatches, toggleHidden, type HiddenView } from "./browse-hidden";

describe("hiding an app from Browse", () => {
  it("hides it, and shows it again the second time", () => {
    const [a, b] = [{ id: "a", slug: "app-a" }, { id: "b", slug: "app-b" }];
    expect(toggleHidden(undefined, a)).toEqual([a]);
    expect(toggleHidden([a, b], a)).toEqual([b]);
  });
});

describe("Only hidden apps' search and filters", () => {
  const entry = { projectName: "Test Remake", projectType: "port", consoles: ["n64"], aiLevel: "generated" } as Entry;
  it("find an app by the start of each word of its name", () => {
    expect(hiddenMatches(entry, "re te", {})).toBe(true);
    expect(hiddenMatches(entry, "make", {})).toBe(false);
  });
  it("go by its type, console and AI use", () => {
    expect(hiddenMatches(entry, "", { projectTypes: ["port"], console: "n64" })).toBe(true);
    expect(hiddenMatches(entry, "", { projectTypes: ["tool"] })).toBe(false);
    expect(hiddenMatches(entry, "", { console: "snes" })).toBe(false);
    expect(hiddenMatches(entry, "", { ai: "no-generated" })).toBe(false);
    expect(hiddenMatches({ ...entry, aiLevel: "assisted" }, "", { ai: "no-generated" })).toBe(true);
    expect(hiddenMatches({ ...entry, aiLevel: "assisted" }, "", { ai: "no-ai" })).toBe(false);
  });
});

const apps = ["a", "owned", "hid", "both"].map((id) => ({ id }));
const shown = (view: HiddenView, showOwned = false) =>
  browseShown(apps, { owned: new Set(["owned", "both"]), showOwned, hidden: new Set(["hid", "both"]), view }).map((e) => e.id);

describe("the apps Browse shows", () => {
  it("leave out hidden apps, and those in the library, by default", () => {
    expect(shown("hide")).toEqual(["a"]);
  });

  it("show hidden apps with the rest when asked", () => {
    expect(shown("show")).toEqual(["a", "hid"]);
    expect(shown("show", true)).toEqual(["a", "owned", "hid", "both"]);
  });

  it("show hidden apps alone, those in the library too", () => {
    expect(shown("only", true)).toEqual(["hid", "both"]);
    expect(shown("only")).toEqual(["hid", "both"]);
  });

  it("keep the library's apps out unless they're asked for, as they are without hidden apps", () => {
    expect(shown("hide", true)).toEqual(["a", "owned"]);
  });
});
