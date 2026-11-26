import { describe, expect, it } from "vitest";
import { fold, matchesAll, searchWords } from "./settings-search";

describe("searching Settings", () => {
  it("looks for each word typed, in any case and without accents", () => {
    expect(searchWords("  Thème   SOMBRE ")).toEqual(["theme", "sombre"]);
    expect(fold("Größe der Oberfläche")).toBe("große der oberflache");
  });

  it("finds a setting with every word in it, in any order", () => {
    const text = fold("Swap A and B: Confirm on B, Back on A");
    expect(matchesAll(searchWords("confirm swap"), text)).toBe(true);
    expect(matchesAll(searchWords("swap volume"), text)).toBe(false);
  });

  it("finds words written together that the setting writes apart", () => {
    expect(matchesAll(searchWords("fullscreen"), fold("Full screen"))).toBe(true);
    expect(matchesAll(searchWords("full screen"), fold("Full screen"))).toBe(true);
  });
});
