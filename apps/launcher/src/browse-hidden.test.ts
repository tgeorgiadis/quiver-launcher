import { describe, expect, it } from "vitest";
import { browseShown, toggleHidden, type HiddenView } from "./browse-hidden";

describe("hiding an app from Browse", () => {
  it("hides it, and shows it again the second time", () => {
    expect(toggleHidden(undefined, "a")).toEqual(["a"]);
    expect(toggleHidden(["a", "b"], "a")).toEqual(["b"]);
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
