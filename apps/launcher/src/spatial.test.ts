import { expect, test, vi } from "vitest";
import { DEFAULT_KEYS, DEFAULT_PAD, confirmBackSwapped, goBack, swapConfirmBack, withDefaults } from "./spatial";

vi.mock("@tauri-apps/api/event", () => ({ listen: vi.fn() }));
vi.mock("./native", () => ({ native: {} }));

test("bindings saved before Back to top get its default, unless it's already used for something else", () => {
  const { top, ...older } = DEFAULT_PAD;
  expect(top).toEqual(["RightThumb"]);
  expect(withDefaults(older as never, DEFAULT_PAD)).toEqual(DEFAULT_PAD);
  const { top: _, ...keys } = { ...DEFAULT_KEYS, options: ["Home"] };
  expect(withDefaults(keys as never, DEFAULT_KEYS)).toEqual({ ...DEFAULT_KEYS, options: ["Home"], top: [] });
  expect(withDefaults(undefined, DEFAULT_KEYS)).toBe(DEFAULT_KEYS);
});

test("swapping A and B swaps what Confirm and Back are bound to, and swapping again undoes it", () => {
  expect(confirmBackSwapped(DEFAULT_PAD)).toBe(false);
  const swapped = swapConfirmBack(DEFAULT_PAD);
  expect(swapped).toEqual({ ...DEFAULT_PAD, confirm: ["East"], back: ["South"] });
  expect(confirmBackSwapped(swapped)).toBe(true);
  expect(swapConfirmBack(swapped)).toEqual(DEFAULT_PAD);
});

test("Android's Back presses Escape while there's something to close or go back from, and says when there's nothing", () => {
  let open: string | null = null;
  const pressed: string[] = [];
  // Sent from the focus, bubbling, as the key sends it: the window's capture listeners come first.
  const focused = { dispatchEvent: (e: { init: { key: string; bubbles: boolean } }) => e.init.bubbles && pressed.push(e.init.key) };
  vi.stubGlobal("document", { querySelector: () => open, activeElement: focused });
  vi.stubGlobal("KeyboardEvent", class {
    constructor(_type: string, readonly init: { key: string; bubbles: boolean }) {}
  });
  try {
    expect(goBack()).toBe(false);
    expect(pressed).toEqual([]);
    open = "a page, or a dialog over it";
    expect(goBack()).toBe(true);
    expect(pressed).toEqual(["Escape"]);
  } finally {
    vi.unstubAllGlobals();
  }
});
