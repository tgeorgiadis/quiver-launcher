import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { forgetSetupFiles, setupFileText } from "./port-setup";

describe("setupFileText", () => {
  beforeEach(() => {
    forgetSetupFiles();
    const store = new Map<string, string>();
    vi.stubGlobal("localStorage", {
      getItem: (k: string) => store.get(k) ?? null,
      setItem: (k: string, v: string) => void store.set(k, v),
      removeItem: (k: string) => void store.delete(k),
    });
  });
  afterEach(() => vi.unstubAllGlobals());

  it("asks once a session and keeps the answer for when the site can't be reached", async () => {
    const setupFile = vi.fn().mockResolvedValue({ text: '{"format":1}', changedAt: 1 });
    expect(await setupFileText({ setupFile }, "port")).toBe('{"format":1}');
    expect(await setupFileText({ setupFile }, "port")).toBe('{"format":1}');
    expect(setupFile).toHaveBeenCalledTimes(1);
    forgetSetupFiles();
    const offline = vi.fn().mockRejectedValue(new Error("offline"));
    expect(await setupFileText({ setupFile: offline }, "port")).toBe('{"format":1}');
  });

  it("has nothing for an app without one, and forgets one that was removed", async () => {
    await setupFileText({ setupFile: vi.fn().mockResolvedValue({ text: "x", changedAt: 1 }) }, "port");
    forgetSetupFiles();
    expect(await setupFileText({ setupFile: vi.fn().mockResolvedValue(null) }, "port")).toBeUndefined();
    forgetSetupFiles();
    expect(await setupFileText({ setupFile: vi.fn().mockRejectedValue(new Error("offline")) }, "port")).toBeUndefined();
    expect(await setupFileText({ setupFile: vi.fn() }, undefined)).toBeUndefined();
  });

  it("doesn't hold Play up when the site is slow", async () => {
    const slow = vi.fn(() => new Promise<never>(() => {}));
    expect(await setupFileText({ setupFile: slow }, "slow", 10)).toBeUndefined();
  });
});
