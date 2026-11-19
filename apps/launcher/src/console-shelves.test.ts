import { describe, expect, it } from "vitest";
import { consoleShelves } from "./console-shelves";

const c = (id: string, brand: string) => ({ id, brand });

describe("consoleShelves", () => {
  it("has each maker's shelf in order, then the handhelds'", () => {
    const consoles = [c("x360", "Xbox"), c("n64", "Nintendo"), c("gba", "Nintendo"), c("psp", "PlayStation"), c("ps1", "PlayStation"), c("saturn", "Sega"), c("pc", "OtherPlatforms")];
    expect(consoleShelves(consoles)).toEqual([
      { id: "maker:Nintendo", consoles: ["maker:Nintendo"] },
      { id: "maker:PlayStation", consoles: ["maker:PlayStation"] },
      { id: "maker:Xbox", consoles: ["maker:Xbox"] },
      { id: "maker:Sega", consoles: ["maker:Sega"] },
      { id: "maker:OtherPlatforms", consoles: ["maker:OtherPlatforms"] },
      { id: "portable", consoles: ["psp", "gba"] },
    ]);
  });

  it("leaves out a maker, or the handhelds, the catalog has none of", () => {
    expect(consoleShelves([c("n64", "Nintendo")])).toEqual([{ id: "maker:Nintendo", consoles: ["maker:Nintendo"] }]);
    expect(consoleShelves([])).toEqual([]);
  });
});
