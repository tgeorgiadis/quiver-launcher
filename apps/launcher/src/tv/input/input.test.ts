import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DEFAULT_KEYS, DEFAULT_PAD, assign, swapConfirmBack } from "../../spatial";
import { controlFor, keyAction, padAction, type InputAction } from "./actions";
import { InputGate } from "./gate";
import { RepeatFilter, type InputEvent } from "./repeat";
import { InputRouter, LAYER, type InputHandler } from "./router";

const key = (k: string, mods: Partial<KeyboardEvent> = {}, keys = DEFAULT_KEYS) =>
  keyAction({ key: k, shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, ...mods } as KeyboardEvent, keys);
const pad = (k: string, bindings = DEFAULT_PAD) => padAction(k, bindings);

describe("maps", () => {
  it("maps the keyboard through the launcher's keys, with TV mode's own on top", () => {
    expect(["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Enter", "Escape", "Backspace"].map((k) => key(k))).toEqual([
      "up", "down", "left", "right", "accept", "back", "back",
    ]);
    expect(["g", "o", "x", "Y", "q", "E", "PageUp", "PageDown", "/", "F10"].map((k) => key(k))).toEqual([
      "secondary", "details", "secondary", "details", "tabPrev", "tabNext", "pagePrev", "pageNext", "search", "menu",
    ]);
    expect(key("PageUp", { ctrlKey: true })).toBe("tabPrev");
    expect(key("ArrowDown", { ctrlKey: true })).toBe("pageNext");
    expect(key("Tab")).toBe("tabNext");
    expect(key("Tab", { shiftKey: true })).toBe("tabPrev");
    expect(key("x", { ctrlKey: true })).toBeNull();
    expect(key("z")).toBeNull();
  });

  it("maps controllers through the player's bindings: bumpers to tabs, triggers along, never the Guide button", () => {
    expect(["South", "East", "West", "North"].map((k) => pad(k))).toEqual(["accept", "back", "secondary", "details"]);
    expect(["LeftTrigger", "RightTrigger", "LeftTrigger2", "RightTrigger2"].map((k) => pad(k))).toEqual(["tabPrev", "tabNext", "pagePrev", "pageNext"]);
    expect(["Select", "Start", "DPadUp", "LeftStickRight"].map((k) => pad(k))).toEqual(["search", "menu", "up", "right"]);
    expect(pad("Mode")).toBeNull();
    expect(pad("RightStickUp")).toBeNull();
    expect(pad("RightThumb")).toBeNull();
  });

  it("follows rebinding: swapped A and B, and a button taken for something TV mode doesn't do", () => {
    const swapped = swapConfirmBack(DEFAULT_PAD);
    expect([pad("South", swapped), pad("East", swapped)]).toEqual(["back", "accept"]);
    expect(controlFor("accept", swapped, "pad")).toBe("East");
    const viewForward = assign(DEFAULT_PAD, "forward", "Select");
    expect(pad("Select", viewForward)).toBeNull();
    expect(controlFor("search", viewForward, "pad")).toBeUndefined();
    const startQuick = assign(DEFAULT_PAD, "quick", "Start");
    expect(pad("Start", startQuick)).toBe("secondary");
    expect(pad("West", startQuick)).toBeNull();
  });

  it("shows a keyboard's shortest key for each action", () => {
    expect(["accept", "secondary", "details", "tabPrev", "pageNext", "search"].map((a) => controlFor(a as InputAction, DEFAULT_KEYS, "keys"))).toEqual([
      "Enter", "G", "O", "Q", "PageDown", "/",
    ]);
  });
});

describe("RepeatFilter", () => {
  let events: InputEvent[];
  let filter: RepeatFilter;
  const repeats = () => events.filter((e) => e.phase === "repeat").length;
  beforeEach(() => {
    vi.useFakeTimers();
    events = [];
    filter = new RepeatFilter((e) => events.push(e));
  });
  afterEach(() => vi.useRealTimers());

  it("repeats a held direction after 380 ms, every 90 ms, then every 45 ms after 1.5 s", () => {
    filter.input("pad0:DPadRight", "right", true);
    expect(events).toEqual([{ action: "right", phase: "press" }]);
    vi.advanceTimersByTime(379);
    expect(repeats()).toBe(0);
    vi.advanceTimersByTime(1);
    expect(repeats()).toBe(1);
    vi.advanceTimersByTime(90);
    expect(repeats()).toBe(2);
    // 380, 470, … 1460: thirteen by 1.5 s.
    vi.advanceTimersByTime(1500 - 470);
    expect(repeats()).toBe(13);
    // Then 1550, and every 45 ms: 1595, 1640.
    vi.advanceTimersByTime(140);
    expect(repeats()).toBe(16);
  });

  it("stops repeating and releases when let go", () => {
    filter.input("key:ArrowDown", "down", true);
    vi.advanceTimersByTime(500);
    filter.input("key:ArrowDown", "down", false);
    const count = events.length;
    vi.advanceTimersByTime(1000);
    expect(events.length).toBe(count);
    expect(events.at(-1)).toEqual({ action: "down", phase: "release" });
  });

  it("never repeats anything but directions", () => {
    filter.input("pad0:South", "accept", true);
    vi.advanceTimersByTime(3000);
    expect(events).toEqual([{ action: "accept", phase: "press" }]);
  });

  it("presses once for two controls holding the same action, and releases when both let go", () => {
    filter.input("pad0:DPadUp", "up", true);
    filter.input("pad0:LeftStickUp", "up", true);
    filter.input("pad0:DPadUp", "up", false);
    expect(events).toEqual([{ action: "up", phase: "press" }]);
    filter.input("pad0:LeftStickUp", "up", false);
    expect(events.at(-1)).toEqual({ action: "up", phase: "release" });
  });

  it("forgets what's held on reset, without releasing, and ignores the late release", () => {
    filter.input("pad0:DPadLeft", "left", true);
    filter.reset();
    vi.advanceTimersByTime(2000);
    filter.input("pad0:DPadLeft", "left", false);
    expect(events).toEqual([{ action: "left", phase: "press" }]);
    expect(filter.isHeld("left")).toBe(false);
  });
});

describe("InputGate", () => {
  let events: InputEvent[];
  let gate: InputGate;
  beforeEach(() => {
    vi.useFakeTimers();
    events = [];
    gate = new InputGate(new RepeatFilter((e) => events.push(e)));
  });
  afterEach(() => vi.useRealTimers());

  it("drops everything while the window is in the background or a game is in front", () => {
    gate.windowActive = false;
    gate.input("pad0:South", "accept", true);
    gate.windowActive = true;
    gate.gameInForeground = true;
    gate.input("pad0:South", "accept", true);
    expect(gate.isOpen).toBe(false);
    expect(events).toEqual([]);
  });

  it("stops a held direction when a game starts, and doesn't resume it after", () => {
    gate.input("pad0:DPadDown", "down", true);
    gate.gameInForeground = true;
    gate.gameInForeground = false;
    vi.advanceTimersByTime(2000);
    expect(events).toEqual([{ action: "down", phase: "press" }]);
  });

  it("ignores the release of a button pressed in the game", () => {
    gate.gameInForeground = true;
    gate.input("pad0:South", "accept", true);
    gate.gameInForeground = false;
    gate.input("pad0:South", "accept", false);
    expect(events).toEqual([]);
    gate.input("pad0:South", "accept", true);
    expect(events).toEqual([{ action: "accept", phase: "press" }]);
  });
});

describe("InputRouter", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const handler = (over: Partial<InputHandler> = {}) => {
    const got: string[] = [];
    const h: InputHandler = {
      onAction: (a, repeat) => void got.push(repeat ? `${a}*` : a),
      onHold: (a) => void got.push(`hold:${a}`),
      hints: () => [{ action: "accept", label: "Play" }],
      ...over,
    };
    return { h, got };
  };
  const press = (action: InputAction) => ({ action, phase: "press" as const });
  const release = (action: InputAction) => ({ action, phase: "release" as const });

  it("sends input only to the top handler, and back to the view when a dialog closes", () => {
    const router = new InputRouter();
    const view = handler();
    const dialog = handler({ hints: () => [{ action: "back", label: "Cancel" }] });
    router.push(view.h);
    const close = router.push(dialog.h);
    router.dispatch(press("accept"));
    expect(router.hints()).toEqual([{ action: "back", label: "Cancel" }]);
    close();
    router.dispatch(press("down"));
    router.dispatch({ action: "down", phase: "repeat" });
    expect(dialog.got).toEqual(["accept"]);
    expect(view.got).toEqual(["down", "down*"]);
    expect(router.hints()).toEqual([{ action: "accept", label: "Play" }]);
  });

  it("fires a held action once it's been held long enough, with its progress readable meanwhile", () => {
    const router = new InputRouter();
    const view = handler({ holdFor: (a) => (a === "secondary" ? 1000 : undefined) });
    router.push(view.h);
    router.dispatch(press("secondary"));
    expect(router.holding).toMatchObject({ action: "secondary", duration: 1000 });
    vi.advanceTimersByTime(999);
    expect(view.got).toEqual([]);
    vi.advanceTimersByTime(1);
    expect(view.got).toEqual(["hold:secondary"]);
    expect(router.holding).toBeNull();
  });

  it("does nothing when a hold is let go early", () => {
    const router = new InputRouter();
    const view = handler({ holdFor: () => 1000 });
    router.push(view.h);
    router.dispatch(press("accept"));
    vi.advanceTimersByTime(600);
    router.dispatch(release("accept"));
    vi.advanceTimersByTime(1000);
    expect(view.got).toEqual([]);
  });

  it("cancels a hold when a dialog opens over it", () => {
    const router = new InputRouter();
    const view = handler({ holdFor: () => 1000 });
    router.push(view.h);
    router.dispatch(press("secondary"));
    router.push(handler().h);
    vi.advanceTimersByTime(2000);
    expect(view.got).toEqual([]);
  });

  it("keeps a dialog above a view that mounts after it", () => {
    const router = new InputRouter();
    const dialog = handler();
    const view = handler();
    router.push(dialog.h, LAYER.dialog);
    router.push(view.h);
    router.dispatch(press("accept"));
    expect(dialog.got).toEqual(["accept"]);
    expect(view.got).toEqual([]);
  });

  it("tells subscribers when hints or holds change", () => {
    const router = new InputRouter();
    const listener = vi.fn();
    router.subscribe(listener);
    const before = router.snapshot();
    router.push(handler().h);
    router.refresh();
    expect(listener).toHaveBeenCalledTimes(2);
    expect(router.snapshot()).not.toBe(before);
  });
});
