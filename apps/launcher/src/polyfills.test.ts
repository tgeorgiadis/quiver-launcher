import { afterEach, expect, it, vi } from "vitest";

const original = Object.hasOwn;

afterEach(() => {
  Object.hasOwn = original;
  vi.resetModules();
});

it("gives an older WebView Object.hasOwn", async () => {
  // @ts-expect-error As before Chrome 93.
  delete Object.hasOwn;
  await import("./polyfills");
  expect(Object.hasOwn({ n64: "Nintendo 64" }, "n64")).toBe(true);
  expect(Object.hasOwn({ n64: "Nintendo 64" }, "toString")).toBe(false);
  expect(Object.hasOwn(Object.create(null), "n64")).toBe(false);
});

it("leaves a WebView's own Object.hasOwn alone", async () => {
  await import("./polyfills");
  expect(Object.hasOwn).toBe(original);
});
