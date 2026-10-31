import { describe, expect, it } from "vitest";
import { fitCards } from "./discover";

describe("fitCards", () => {
  it("fits as many whole cards as there's room for at about the width asked, grown to fill the row", () => {
    // 1340 across, 22 between: six of 176 fit (1166), and grow to 205 each.
    const fit = fitCards(1340, 22, 176);
    expect(fit.count).toBe(6);
    expect(fit.width * 6 + 22 * 5).toBeCloseTo(1340);
  });

  it("keeps at least one card, however narrow the row", () => {
    expect(fitCards(120, 22, 176)).toEqual({ count: 1, width: 120 });
  });
});
