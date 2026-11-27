import { afterEach, expect, test, vi } from "vitest";
import { FRESH_FOR } from "./browse-memory";
import { answer, forgetApps, keptAnswer } from "./app-memory";

afterEach(() => {
  vi.useRealTimers();
  forgetApps();
});

test("an answer is asked once, drawn at once when it came, and asked again once it's older than FRESH_FOR", async () => {
  vi.useFakeTimers();
  const ask = vi.fn(() => Promise.resolve("readme"));
  const first = answer("k", ask);
  // On its way: the page waits for the same question rather than asking again.
  expect(keptAnswer("k")).toBeUndefined();
  expect(answer("k", ask)).toBe(first);
  await first;
  expect(keptAnswer("k")).toBe("readme");
  expect(ask).toHaveBeenCalledTimes(1);
  vi.advanceTimersByTime(FRESH_FOR);
  expect(keptAnswer("k")).toBeUndefined();
  await answer("k", ask);
  expect(ask).toHaveBeenCalledTimes(2);
});

test("a failed answer isn't kept, so the page asks again", async () => {
  const ask = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce(null);
  await expect(answer("k", ask)).rejects.toThrow("offline");
  expect(keptAnswer("k")).toBeUndefined();
  // None (null) is an answer too, and kept.
  await expect(answer("k", ask)).resolves.toBeNull();
  expect(keptAnswer("k")).toBeNull();
});
