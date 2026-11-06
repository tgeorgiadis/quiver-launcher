import { expect, test } from "vitest";
import { isOversized } from "./fitted";

test("only pictures far bigger than a 4K screen are made smaller", () => {
  expect(isOversized(18106, 4509)).toBe(true);
  expect(isOversized(3840, 1240)).toBe(false);
  expect(isOversized(3840, 2160)).toBe(false);
});
