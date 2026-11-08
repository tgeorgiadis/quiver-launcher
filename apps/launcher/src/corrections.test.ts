import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { clearCorrectionDraft, correctionClient, readCorrectionDraft, saveCorrectionDraft } from "./corrections";

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

it("keeps an unsent correction for each account and app until it's cleared", () => {
  saveCorrectionDraft("u1", "e1", { topic: "launch", body: "Starts the editor." });
  expect(readCorrectionDraft("u1", "e1")).toEqual({ topic: "launch", body: "Starts the editor." });
  expect(readCorrectionDraft("u2", "e1")).toBeNull();
  expect(readCorrectionDraft("u1", "e2")).toBeNull();
  clearCorrectionDraft("u1", "e1");
  expect(readCorrectionDraft("u1", "e1")).toBeNull();
});

it("keeps nothing for an empty form, and forgets a draft after 30 days or with a topic it doesn't know", () => {
  saveCorrectionDraft("u1", "e1", { topic: "", body: "  " });
  expect(localStorage.getItem("quiver-correction-draft:u1:e1")).toBeNull();
  vi.useFakeTimers();
  saveCorrectionDraft("u1", "e1", { topic: "", body: "Old." });
  vi.advanceTimersByTime(31 * 24 * 60 * 60 * 1000);
  expect(readCorrectionDraft("u1", "e1")).toBeNull();
  localStorage.setItem("quiver-correction-draft:u1:e2", JSON.stringify({ topic: "nope", body: "x", savedAt: Date.now() }));
  expect(readCorrectionDraft("u1", "e2")).toBeNull();
});

it("names the launcher, its system and the release installed for maintainers", () => {
  expect(correctionClient({ version: "4.0.0-beta.3", os: "windows" }, "v1.3.0")).toEqual({ version: "4.0.0-beta.3", os: "Windows", installed: "v1.3.0" });
  expect(correctionClient({ version: "4.0.0", os: "linux", steamDeck: true })).toEqual({ version: "4.0.0", os: "Steam Deck" });
  expect(correctionClient({ version: "x".repeat(60), os: "plan9" }).version).toHaveLength(40);
});
