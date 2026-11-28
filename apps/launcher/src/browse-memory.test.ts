// @vitest-environment jsdom
import { afterEach, expect, test, vi } from "vitest";
import type { Entry, Page } from "@quiverlauncher/api";
import { FRESH_FOR, SHELVES_KEY, SHOWN_FOR, forgetBrowse, keepGrid, keepShelf, keptGrid, keptShelf, shownShelf } from "./browse-memory";

const page = (id: string) => ({ items: [{ id } as Entry], nextCursor: null }) as unknown as Page<Entry>;

afterEach(() => {
  vi.useRealTimers();
  forgetBrowse();
});

test("a shelf comes back as it was until it's older than FRESH_FOR, then Browse asks again", () => {
  vi.useFakeTimers();
  const entries = [{ id: "a" } as Entry];
  keepShelf("windows|rating", entries);
  expect(keptShelf("windows|rating")).toBe(entries);
  expect(keptShelf("linux|rating")).toBeUndefined();
  vi.advanceTimersByTime(FRESH_FOR);
  expect(keptShelf("windows|rating")).toBeUndefined();
});

test("the grid's later pages keep the first one's time, and a new first page starts it again", () => {
  vi.useFakeTimers();
  keepGrid("k", [page("1")], ["x"]);
  vi.advanceTimersByTime(FRESH_FOR - 1000);
  keepGrid("k", [page("1"), page("2")], ["x"]);
  expect(keptGrid("k")?.pages).toHaveLength(2);
  expect(keptGrid("k")?.owned).toEqual(["x"]);
  vi.advanceTimersByTime(1000);
  expect(keptGrid("k")).toBeUndefined();
  // Refresh: its first page again.
  keepGrid("k", [page("1")], []);
  expect(keptGrid("k")?.pages).toHaveLength(1);
});

test("an older shelf is still shown while Browse asks for it again, until it's older than SHOWN_FOR", () => {
  vi.useFakeTimers();
  const entries = [{ id: "a" } as Entry];
  keepShelf("windows|rating", entries);
  vi.advanceTimersByTime(FRESH_FOR);
  expect(keptShelf("windows|rating")).toBeUndefined();
  expect(shownShelf("windows|rating")).toBe(entries);
  vi.advanceTimersByTime(SHOWN_FOR);
  expect(shownShelf("windows|rating")).toBeUndefined();
});

test("the shelves are kept on disk, and the next start shows them", async () => {
  vi.useFakeTimers();
  keepShelf("windows|rating", [{ id: "a" } as Entry]);
  keepShelf("windows|updated", [{ id: "b" } as Entry]);
  vi.runAllTimers();
  expect(Object.keys(JSON.parse(localStorage.getItem(SHELVES_KEY) ?? "{}"))).toEqual(["windows|rating", "windows|updated"]);
  // The launcher started again, a while later: the module reads them back, with when they came.
  vi.advanceTimersByTime(FRESH_FOR);
  vi.resetModules();
  const again = await import("./browse-memory");
  expect(again.shownShelf("windows|rating")).toEqual([{ id: "a" }]);
  // Shown, but no longer fresh: Browse asks for them again behind them.
  expect(again.keptShelf("windows|rating")).toBeUndefined();
});
