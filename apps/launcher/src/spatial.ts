/**
 * Arrow keys and controllers move focus to the nearest control in that
 * direction, across every screen, instead of per-screen focus code. Keys and
 * controller buttons map to actions through the player's bindings (Settings),
 * as in Quiver Launcher 3: Confirm presses, Back closes (Escape), Options
 * opens the focused app, and the four directions move.
 */
import { listen } from "@tauri-apps/api/event";

type Direction = "up" | "down" | "left" | "right";
export const ACTIONS = ["confirm", "back", "options", "up", "down", "left", "right"] as const;
export type Action = (typeof ACTIONS)[number];
export type Bindings = Record<Action, string[]>;
export const ACTION_NAMES: Record<Action, string> = {
  confirm: "Confirm / Select",
  back: "Cancel / Back",
  options: "Options",
  up: "Navigate up",
  down: "Navigate down",
  left: "Navigate left",
  right: "Navigate right",
};
export const DEFAULT_KEYS: Bindings = {
  confirm: ["Enter"],
  back: ["Escape"],
  options: ["O"],
  up: ["ArrowUp"],
  down: ["ArrowDown"],
  left: ["ArrowLeft"],
  right: ["ArrowRight"],
};
/** Controls as the Rust side names them (gilrs): South is A on Xbox pads, East is B. */
export const DEFAULT_PAD: Bindings = {
  confirm: ["South"],
  back: ["East"],
  options: ["North"],
  up: ["DPadUp", "LeftStickUp"],
  down: ["DPadDown", "LeftStickDown"],
  left: ["DPadLeft", "LeftStickLeft"],
  right: ["DPadRight", "LeftStickRight"],
};

const PAD_NAMES: Record<string, string> = {
  South: "A", East: "B", West: "X", North: "Y", LeftTrigger: "LB", RightTrigger: "RB", LeftTrigger2: "LT", RightTrigger2: "RT",
  Select: "Back", Start: "Start", Mode: "Guide", LeftThumb: "L3", RightThumb: "R3",
  DPadUp: "D-pad ↑", DPadDown: "D-pad ↓", DPadLeft: "D-pad ←", DPadRight: "D-pad →",
  LeftStickUp: "LS ↑", LeftStickDown: "LS ↓", LeftStickLeft: "LS ←", LeftStickRight: "LS →",
  RightStickUp: "RS ↑", RightStickDown: "RS ↓", RightStickLeft: "RS ←", RightStickRight: "RS →",
};
const KEY_NAMES: Record<string, string> = { ArrowUp: "↑", ArrowDown: "↓", ArrowLeft: "←", ArrowRight: "→", Escape: "Esc", " ": "Space" };
export const label = (kind: "keys" | "pad", binding: string) =>
  kind === "pad" ? (PAD_NAMES[binding] ?? binding) : binding.split("+").map((k) => KEY_NAMES[k] ?? k).join("+");

/** "Ctrl+Shift+K": the key with its modifiers; letters upper case. */
export function combo(e: KeyboardEvent) {
  const key = e.key.length === 1 ? e.key.toUpperCase() : e.key;
  return [e.ctrlKey && "Ctrl", e.altKey && "Alt", e.shiftKey && e.key.length > 1 && "Shift", e.metaKey && "Meta", key].filter(Boolean).join("+");
}

/** Gives a binding to one action only, taking it from any other. */
export function assign(bindings: Bindings, action: Action, binding: string): Bindings {
  const next = Object.fromEntries(ACTIONS.map((a) => [a, bindings[a].filter((b) => b !== binding)])) as Bindings;
  next[action] = [binding];
  return next;
}

let keys = DEFAULT_KEYS;
let pad = DEFAULT_PAD;
let padOn = true;
export function setBindings(next: { keys?: Bindings; pad?: Bindings; padOff?: boolean }) {
  keys = next.keys ?? DEFAULT_KEYS;
  pad = next.pad ?? DEFAULT_PAD;
  padOn = !next.padOff;
}

let capture: { kind: "keys" | "pad"; resolve: (binding: string | null) => void } | null = null;
/** Resolves to the next key or controller press, or null when it's cancelled (Escape). */
export function captureNext(kind: "keys" | "pad") {
  capture?.resolve(null);
  return new Promise<string | null>((resolve) => (capture = { kind, resolve }));
}
const finish = (binding: string | null) => {
  capture?.resolve(binding);
  capture = null;
};

const FOCUSABLE = "button:not(:disabled), input, select, textarea, a[href], [tabindex]:not([tabindex='-1'])";

/** Controls in the topmost dialog when one is open, else on the page. */
function candidates() {
  const dialogs = document.querySelectorAll('[role="dialog"]');
  const scope = dialogs[dialogs.length - 1] ?? document;
  return [...scope.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((el) => el.getClientRects().length > 0);
}

export function move(direction: Direction) {
  const all = candidates();
  const current = document.activeElement as HTMLElement | null;
  if (!current || !all.includes(current)) return all[0]?.focus();
  const from = current.getBoundingClientRect();
  let best: HTMLElement | undefined;
  let bestScore = Infinity;
  for (const el of all) {
    if (el === current) continue;
    const r = el.getBoundingClientRect();
    // How far past the current control's edge it starts; a little overlap is fine.
    const along = { up: from.top - r.bottom, down: r.top - from.bottom, left: from.left - r.right, right: r.left - from.right }[direction];
    if (along < -10) continue;
    // Sideways gap between the two boxes: 0 when they line up.
    const across =
      direction === "up" || direction === "down"
        ? Math.max(0, r.left - from.right, from.left - r.right)
        : Math.max(0, r.top - from.bottom, from.top - r.bottom);
    // Prefer what's straight ahead over what's merely close.
    const score = Math.max(0, along) + across * 2;
    // Within a few pixels (a hovered card is lifted) the earlier one wins.
    if (score < bestScore - 8) [best, bestScore] = [el, score];
  }
  best?.focus();
  best?.scrollIntoView({ block: "nearest", behavior: "smooth" });
}

function act(action: Action) {
  const focused = document.activeElement as HTMLElement | null;
  if (action === "confirm") focused?.click();
  else if (action === "back") window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
  else if (action === "options") focused?.closest("article")?.querySelector<HTMLElement>(".card-open")?.click();
  else move(action);
}

const MODIFIERS = new Set(["Control", "Shift", "Alt", "Meta"]);

export function startSpatialNavigation() {
  window.addEventListener(
    "keydown",
    (e) => {
      if (capture?.kind === "pad" && e.key === "Escape") {
        e.stopImmediatePropagation();
        return finish(null);
      }
      if (capture?.kind === "keys") {
        if (MODIFIERS.has(e.key)) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        return finish(e.key === "Escape" ? null : combo(e));
      }
      if (!e.isTrusted) return; // Our own Escape for Back.
      const pressed = combo(e);
      const action = ACTIONS.find((a) => keys[a].includes(pressed));
      if (!action) return;
      const el = e.target as HTMLElement;
      const typing = (el instanceof HTMLInputElement && el.type !== "checkbox") || el instanceof HTMLTextAreaElement;
      // Text fields keep their keys; up and down still leave them.
      if (typing && action !== "up" && action !== "down" && !(action === "back" && e.key === "Escape")) return;
      // Enter on a control and Escape already do this themselves.
      if ((action === "confirm" && e.key === "Enter") || (action === "back" && e.key === "Escape")) return;
      e.preventDefault();
      act(action);
    },
    true,
  );

  // Controllers come from the Rust side ("pad" events): act on press, repeat directions while held.
  const repeats = new Map<string, ReturnType<typeof setTimeout>>();
  void listen<{ key: string; down: boolean }>("pad", ({ payload: { key, down } }) => {
    clearTimeout(repeats.get(key));
    repeats.delete(key);
    if (!down) return;
    if (capture?.kind === "pad") return finish(key);
    if (!padOn || !document.hasFocus()) return; // A game is running in front.
    const action = ACTIONS.find((a) => pad[a].includes(key));
    if (!action) return;
    act(action);
    if (action === "confirm" || action === "back" || action === "options") return;
    const again = (delay: number) => repeats.set(key, setTimeout(() => (act(action), again(120)), delay));
    again(300);
  });
}
