import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { clearDraft, readDraft, saveDraft } from "./reviewDraft";

beforeEach(() => {
  const store = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
  });
});
afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

const draft = { result: "issues" as const, platform: "linux", releaseId: "r1", body: "Audio crackles." };

it("keeps a draft for each account and app until it's cleared", () => {
  saveDraft("u1", "e1", draft);
  expect(readDraft("u1", "e1")).toEqual(draft);
  expect(readDraft("u2", "e1")).toBeNull();
  expect(readDraft("u1", "e2")).toBeNull();
  clearDraft("u1", "e1");
  expect(readDraft("u1", "e1")).toBeNull();
});

it("forgets a draft after 30 days", () => {
  vi.useFakeTimers();
  saveDraft("u1", "e1", draft);
  vi.advanceTimersByTime(31 * 24 * 60 * 60 * 1000);
  expect(readDraft("u1", "e1")).toBeNull();
});

it("ignores a draft it can't read", () => {
  localStorage.setItem("quiver-review-draft:u1:e1", "{not json");
  expect(readDraft("u1", "e1")).toBeNull();
  localStorage.setItem("quiver-review-draft:u1:e1", JSON.stringify({ ...draft, platform: "amiga", savedAt: Date.now() }));
  expect(readDraft("u1", "e1")).toBeNull();
});
