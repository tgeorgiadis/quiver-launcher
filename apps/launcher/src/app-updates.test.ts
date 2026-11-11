import { expect, test } from "vitest";
import { appUpdatesMode, withAppUpdates } from "./app-updates";
import { stillWaiting } from "./store";

test("apps wait 48 hours for their updates until the player picks Newest right away", () => {
  expect(appUpdatesMode({})).toBe("wait");
  expect(appUpdatesMode({ appUpdates: "newest" })).toBe("newest");
});

test("picking how apps update answers the question, and going back to waiting leaves nothing set", () => {
  const newest = withAppUpdates({ theme: "dark" }, "newest");
  expect(newest).toEqual({ theme: "dark", appUpdates: "newest", appUpdatesAsked: true });
  expect(withAppUpdates(newest, "wait")).toEqual({ theme: "dark", appUpdatesAsked: true });
});

test("a release is still in its wait for 48 hours after it came out", () => {
  const now = Date.UTC(2026, 10, 11);
  expect(stillWaiting(now - 47 * 3_600_000, now)).toBe(true);
  expect(stillWaiting(now - 48 * 3_600_000, now)).toBe(false);
  expect(stillWaiting(undefined, now)).toBe(false);
});
