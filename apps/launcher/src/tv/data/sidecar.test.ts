import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DEFAULT_SETTINGS, SidecarStore, normalize, type TvSidecar, type SidecarIO } from "./sidecar";

function memoryIO(initial: unknown = null) {
  const writes: TvSidecar[] = [];
  const io: SidecarIO = {
    read: async () => initial,
    write: async (value) => void writes.push(JSON.parse(JSON.stringify(value))),
  };
  return { io, writes };
}

describe("normalize", () => {
  it("starts from defaults with version 1 when there's no file", () => {
    expect(normalize(null)).toEqual({ version: 1, settings: DEFAULT_SETTINGS, apps: {} });
  });

  it("keeps fields it doesn't know, at the top, in settings and in apps", () => {
    const raw = { version: 1, extra: { a: 1 }, settings: { updateBehavior: "auto", futureThing: true }, apps: { x: { playCount: 2, later: "yes" } } };
    const data = normalize(raw) as TvSidecar & { extra: unknown; settings: { futureThing?: boolean } };
    expect(data.extra).toEqual({ a: 1 });
    expect(data.settings.futureThing).toBe(true);
    expect(data.settings.updateBehavior).toBe("auto");
    expect(data.apps.x).toEqual({ playCount: 2, later: "yes" });
  });

  it("keeps a newer file's version number", () => {
    expect(normalize({ version: 3 }).version).toBe(3);
  });

  it("uses the default for a value of the wrong kind and clamps the UI scale", () => {
    const data = normalize({ settings: { updateBehavior: "sometimes", showHomeTab: "yes", uiScale: 9, monitor: "" }, apps: { a: 5, b: {} } });
    expect(data.settings.updateBehavior).toBe("ask");
    expect(data.settings.showHomeTab).toBe(true);
    expect(data.settings.uiScale).toBe(2);
    expect(data.settings.monitor).toBeNull();
    expect(Object.keys(data.apps)).toEqual(["b"]);
  });

  it("leaves adding programs off unless it was turned on", () => {
    expect(normalize({ settings: {} }).settings.allowPrograms).toBe(false);
    expect(normalize({ settings: { allowPrograms: "yes" } }).settings.allowPrograms).toBe(false);
    expect(normalize({ settings: { allowPrograms: true } }).settings.allowPrograms).toBe(true);
  });
});

describe("SidecarStore", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("round-trips through its file", async () => {
    const { io, writes } = memoryIO();
    const store = await SidecarStore.load(io);
    store.setSettings({ updateBehavior: "off", reduceMotion: true });
    store.updateApp("github:a/b", { skippedVersion: "v1.2.0" });
    await store.flush();
    const again = await SidecarStore.load(memoryIO(writes.at(-1)).io);
    expect(again.settings.updateBehavior).toBe("off");
    expect(again.settings.reduceMotion).toBe(true);
    expect(again.app("github:a/b").skippedVersion).toBe("v1.2.0");
  });

  it("writes once, 500 ms after the last of several changes", async () => {
    const { io, writes } = memoryIO();
    const store = await SidecarStore.load(io);
    store.updateApp("a", (s) => ({ ...s, playCount: (s.playCount ?? 0) + 1 }));
    await vi.advanceTimersByTimeAsync(300);
    store.updateApp("a", (s) => ({ ...s, playCount: (s.playCount ?? 0) + 1 }));
    await vi.advanceTimersByTimeAsync(499);
    expect(writes).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(1);
    expect(writes).toHaveLength(1);
    expect(writes[0].apps.a.playCount).toBe(2);
  });

  it("makes a new snapshot on every change and tells its listeners", async () => {
    const store = await SidecarStore.load(memoryIO().io);
    const listener = vi.fn();
    store.subscribe(listener);
    const before = store.snapshot;
    store.setSettings({ showHomeTab: false });
    expect(store.snapshot).not.toBe(before);
    expect(before.settings.showHomeTab).toBe(true);
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("writes in order even when an earlier write is slow", async () => {
    const order: number[] = [];
    let release!: () => void;
    const slow = new Promise<void>((r) => (release = r));
    const io: SidecarIO = {
      read: async () => null,
      write: async (value) => {
        if (value.settings.uiScale === 1.5) await slow;
        order.push(value.settings.uiScale);
      },
    };
    const store = await SidecarStore.load(io);
    store.setSettings({ uiScale: 1.5 });
    const first = store.flush();
    store.setSettings({ uiScale: 1.25 });
    const second = store.flush();
    release();
    await Promise.all([first, second]);
    expect(order).toEqual([1.5, 1.25]);
  });

  it("starts from defaults when the file can't be read, and reports a failed write without throwing", async () => {
    const onError = vi.fn();
    const io: SidecarIO = { read: () => Promise.reject(new Error("gone")), write: () => Promise.reject(new Error("disk full")) };
    const store = await SidecarStore.load(io, 500, onError);
    expect(store.settings).toEqual(DEFAULT_SETTINGS);
    store.setSettings({ reduceMotion: true });
    await store.flush();
    expect(onError).toHaveBeenCalledOnce();
  });
});
