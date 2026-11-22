import { expect, test } from "vitest";
import { approach } from "./glide";

test("a glide closes most of the way in a fifth of a second, however it's cut into frames", () => {
  let at = 0;
  for (let i = 0; i < 12; i++) at = approach(at, 400, 16.7);
  expect(at).toBeGreaterThan(360);
  expect(at).toBeLessThan(400);
  expect(approach(0, 400, 200.4)).toBeCloseTo(at, 5);
});

test("a new place to go carries on from where it is", () => {
  const at = approach(0, 400, 50);
  expect(approach(at, 800, 50)).toBeGreaterThan(at);
});
