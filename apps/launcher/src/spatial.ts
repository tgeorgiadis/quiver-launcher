/**
 * Arrow keys and controllers move focus to the nearest control in that
 * direction, across every screen, instead of per-screen focus code. Keys and
 * controller buttons map to actions through the player's bindings (Settings),
 * as in Quiver Launcher 3: Confirm presses (and plays a library card), Back closes (Escape) or goes back a page, Options
 * opens the focused library card's or sidebar row's menu (or gets the app a card or page is of), Back to
 * top goes to the top of a long page, the four directions move, and Scroll
 * up and down scroll what the focus is in (smoothly while the right stick is
 * held; a page at a time from the keys). A mouse's side buttons bind with the keys.
 */
import { listen } from "@tauri-apps/api/event";
import { t } from "@lingui/core/macro";
import { native, type PadLabels } from "./native";
import { closePicker, openPicker, pickerPanel } from "./select-picker";
import { boxZoom, windowBox, zoom as interfaceZoom } from "./zoom";

type Direction = "up" | "down" | "left" | "right";
export const ACTIONS = ["confirm", "back", "forward", "options", "top", "up", "down", "left", "right", "scrollUp", "scrollDown", "prevTab", "nextTab", "prevLetter", "nextLetter"] as const;
export type Action = (typeof ACTIONS)[number];
export type Bindings = Record<Action, string[]>;
/** Each action's name, in the language shown when it's read, not when the module loads (before one is picked). */
export const ACTION_NAMES: Record<Action, string> = {
  get confirm() { return t`Confirm / Select`; },
  get back() { return t`Cancel / Back`; },
  get forward() { return t({ message: "Forward", comment: "Action that opens again the page Back last left" }); },
  get options() { return t({ message: "Options", comment: "Controller action that opens the focused item's menu" }); },
  get top() { return t`Back to top`; },
  get up() { return t`Navigate up`; },
  get down() { return t`Navigate down`; },
  get left() { return t`Navigate left`; },
  get right() { return t`Navigate right`; },
  get scrollUp() { return t`Scroll up`; },
  get scrollDown() { return t`Scroll down`; },
  get prevTab() { return t({ message: "Previous tab", comment: "Controller action: the tab before the open one, as on an app's page" }); },
  get nextTab() { return t({ message: "Next tab", comment: "Controller action: the tab after the open one, as on an app's page" }); },
  get prevLetter() { return t({ message: "Previous letter or page", comment: "Controller action: in the library, the apps under the letter before, or a screen up when it isn't by name" }); },
  get nextLetter() { return t({ message: "Next letter or page", comment: "Controller action: in the library, the apps under the next letter, or a screen down when it isn't by name" }); },
};
export const DEFAULT_KEYS: Bindings = {
  confirm: ["Enter"],
  back: ["Escape", "Alt+ArrowLeft", "MouseBack"],
  forward: ["Alt+ArrowRight", "MouseForward"],
  options: ["O"],
  top: ["Home"],
  up: ["ArrowUp"],
  down: ["ArrowDown"],
  left: ["ArrowLeft"],
  right: ["ArrowRight"],
  scrollUp: ["PageUp"],
  scrollDown: ["PageDown"],
  prevTab: ["Ctrl+PageUp"],
  nextTab: ["Ctrl+PageDown"],
  prevLetter: ["Ctrl+ArrowUp"],
  nextLetter: ["Ctrl+ArrowDown"],
};
/** Controls as the Rust side names them (SDL's, by the names gilrs gave them): South is A on Xbox pads, East is B. */
export const DEFAULT_PAD: Bindings = {
  confirm: ["South"],
  back: ["East"],
  // None: a controller's buttons are all taken. The player can give it one in Settings.
  forward: [],
  options: ["North"],
  top: ["RightThumb"],
  up: ["DPadUp", "LeftStickUp"],
  down: ["DPadDown", "LeftStickDown"],
  left: ["DPadLeft", "LeftStickLeft"],
  right: ["DPadRight", "LeftStickRight"],
  scrollUp: ["RightStickUp"],
  scrollDown: ["RightStickDown"],
  prevTab: ["LeftTrigger"],
  nextTab: ["RightTrigger"],
  prevLetter: ["LeftTrigger2"],
  nextLetter: ["RightTrigger2"],
};

const PAD_NAMES: Record<string, string> = {
  South: "A", East: "B", West: "X", North: "Y", LeftTrigger: "LB", RightTrigger: "RB", LeftTrigger2: "LT", RightTrigger2: "RT",
  Select: "Back", Start: "Start", Mode: "Guide", LeftThumb: "L3", RightThumb: "R3",
  DPadUp: "D-pad ↑", DPadDown: "D-pad ↓", DPadLeft: "D-pad ←", DPadRight: "D-pad →",
  LeftStickUp: "LS ↑", LeftStickDown: "LS ↓", LeftStickLeft: "LS ←", LeftStickRight: "LS →",
  RightStickUp: "RS ↑", RightStickDown: "RS ↓", RightStickLeft: "RS ←", RightStickRight: "RS →",
  Misc1: "Share", RightPaddle1: "P1", RightPaddle2: "P2", LeftPaddle1: "P3", LeftPaddle2: "P4", Touchpad: "Touchpad",
};
const KEY_NAMES: Record<string, string> = {
  ArrowUp: "↑", ArrowDown: "↓", ArrowLeft: "←", ArrowRight: "→", Escape: "Esc", " ": "Space", PageUp: "Page Up", PageDown: "Page Down", MouseBack: "Mouse back", MouseForward: "Mouse forward",
};
/** A mouse's side buttons, by MouseEvent.button. */
const MOUSE_BUTTONS: Record<number, string> = { 3: "MouseBack", 4: "MouseForward" };
/** The face buttons by place, as the controller used last labels them (PadLabels); PlayStation's as their symbols. */
const FACES: Record<string, "south" | "east" | "west" | "north"> = { South: "south", East: "east", West: "west", North: "north" };
const SYMBOLS: Record<string, string> = { Cross: "✕", Circle: "○", Square: "□", Triangle: "△" };
/** What other controls are called on each kind of controller, where it isn't as on an Xbox pad. */
const KIND_NAMES: Record<PadLabels["kind"], Record<string, string>> = {
  xbox: { Select: "View", Start: "Menu" },
  playstation: { LeftTrigger: "L1", RightTrigger: "R1", LeftTrigger2: "L2", RightTrigger2: "R2", Select: "Share", Start: "Options", Mode: "PS", Misc1: "Mic" },
  nintendo: { LeftTrigger: "L", RightTrigger: "R", LeftTrigger2: "ZL", RightTrigger2: "ZR", Select: "−", Start: "+", Mode: "Home", Misc1: "Capture" },
  steam: { LeftTrigger: "L1", RightTrigger: "R1", LeftTrigger2: "L2", RightTrigger2: "R2", Select: "View", Start: "Menu", Mode: "Steam", RightPaddle1: "R4", RightPaddle2: "R5", LeftPaddle1: "L4", LeftPaddle2: "L5" },
  other: {},
};
/** A binding as the player would call it: a controller's as `pad` (the one used last) labels it, if it's known. */
export function label(kind: "keys" | "pad", binding: string, pad?: PadLabels | null) {
  if (kind === "keys") return binding.split("+").map((k) => KEY_NAMES[k] ?? k).join("+");
  const face = FACES[binding];
  if (pad && face) return SYMBOLS[pad[face]] ?? pad[face];
  return (pad && KIND_NAMES[pad.kind][binding]) ?? PAD_NAMES[binding] ?? binding;
}

/** "Ctrl+Shift+K": the key with its modifiers; letters upper case. */
export function combo(e: KeyboardEvent) {
  const key = e.key.length === 1 ? e.key.toUpperCase() : e.key;
  return [e.ctrlKey && "Ctrl", e.altKey && "Alt", e.shiftKey && e.key.length > 1 && "Shift", e.metaKey && "Meta", key].filter(Boolean).join("+");
}

/**
 * Confirm and Back on each other's buttons (as emulators' "swap A and B"):
 * whatever they're bound to, so it undoes itself. Swapped while Confirm has
 * the east button and Back the south one (B and A on an Xbox pad).
 */
export const swapConfirmBack = (pad: Bindings): Bindings => ({ ...pad, confirm: pad.back, back: pad.confirm });
export const confirmBackSwapped = (pad: Bindings) => pad.confirm.includes("East") && pad.back.includes("South");

/** Gives a binding to one action only, taking it from any other. */
export function assign(bindings: Bindings, action: Action, binding: string): Bindings {
  const next = Object.fromEntries(ACTIONS.map((a) => [a, bindings[a].filter((b) => b !== binding)])) as Bindings;
  next[action] = [binding];
  return next;
}

/**
 * Saved bindings with the defaults for any action added since they were saved
 * (Back to top), leaving out a default the player already uses for another.
 */
export function withDefaults(saved: Partial<Bindings> | undefined, defaults: Bindings): Bindings {
  if (!saved) return defaults;
  const used = new Set(Object.values(saved).flat());
  return Object.fromEntries(ACTIONS.map((a) => [a, saved[a] ?? defaults[a].filter((b) => !used.has(b))])) as Bindings;
}

let keys = DEFAULT_KEYS;
let pad = DEFAULT_PAD;
let padOn = true;
export function setBindings(next: { keys?: Bindings; pad?: Bindings; padOff?: boolean }) {
  keys = withDefaults(next.keys, DEFAULT_KEYS);
  pad = withDefaults(next.pad, DEFAULT_PAD);
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

// Not one that's disabled, which can't take the focus: the d-pad stopped at a platform's box that must stay ticked.
const FOCUSABLE = ":is(button, input, select, textarea):not(:disabled), a[href], [tabindex]:not([tabindex='-1'])";

/** Showing, so it can have the focus. A library card's buttons are hidden until the card has focus. */
const shown = (el: Element) => el.isConnected && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";

/**
 * What Confirm presses on a library card (TV mode's library's too), in place of
 * opening it, as in Quiver Launcher 3: its Play or Get. Enter, as a click,
 * opens its page.
 */
function playOf(el: EventTarget | null) {
  if (!(el instanceof HTMLElement)) return null;
  // Not Add, on a card for an app installed here but not in the library, which Options presses (quickButton).
  if (el.matches(".library-tile .card-open")) return el.closest("article")?.querySelector<HTMLElement>(".tile-play:not(.tile-add)") ?? null;
  return null;
}

/** What Confirm presses on a card in place of opening it, for the hints to mark (playOf): its Play or Get. */
export const confirmButton = (el: EventTarget | null) => playOf(el);

/**
 * What Options presses, from wherever the focus is, without moving to it: the
 * main button under a card (Get, Play), or on a library card for an app not in
 * the library (Add), or an app's page's own, from anywhere on the page. Confirm
 * on a card opens its page, as a store's does, so a stray press never starts a
 * download. Not from a text field or a form over the page.
 */
export function quickButton(el: EventTarget | null) {
  // Not a card with a menu (the library's, TV mode's library's): Options opens that, and Confirm presses its Play.
  if (!(el instanceof HTMLElement) || takesText(el) || el.closest('[role="dialog"]') || el.closest("[data-menu]")) return null;
  if (el.matches("article .card-open")) return el.closest("article")?.querySelector<HTMLElement>(".card-action .primary:not(:disabled), .tile-add") ?? null;
  return el.closest(".app-page")?.querySelector<HTMLElement>(".app-actions .primary:not(:disabled)") ?? null;
}

/** The only button under a card, which Options on the card presses: so the arrows go card to card past it. */
function pressedByCard(el: HTMLElement) {
  const under = el.closest(".card-action");
  return !!under && el.matches(".primary") && [...under.querySelectorAll(FOCUSABLE)].filter(shown).length === 1;
}

/**
 * Controls in a dropdown's open list (select-picker.ts), else in the topmost
 * dialog when one is open, else on the page. Not the
 * sidebar's edge (unless it has the focus): a stop on the way from the sidebar
 * to the page, it's for a mouse or Tab, and the View menu has its widths. Nor
 * a card's only button, which the card's Confirm presses.
 */
function candidates() {
  const dialogs = document.querySelectorAll('[role="dialog"]');
  const scope = pickerPanel() ?? dialogs[dialogs.length - 1] ?? document;
  // Not a mouse's helper either, kept out of the keys' order (tabindex -1): a shelf's arrows, the letters beside a
  // library, a download's name; the d-pad has its own way to each.
  return [...scope.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
    (el) =>
      !hiddenOnCard(el) &&
      shown(el) &&
      (el === document.activeElement || (el.getAttribute("tabindex") !== "-1" && !el.matches(".sidebar-edge") && !pressedByCard(el))),
  );
}

/**
 * A library card's buttons, hidden until it has the focus or the pointer (app.css), unless it's downloading: left out
 * without measuring them, which for a large library was most of the time a step took to find where to go.
 */
function hiddenOnCard(el: Element) {
  const card = el.closest(".tile-overlay")?.parentElement;
  return !!card && !card.matches(":hover, [data-focus], [data-busy]");
}

/**
 * Whether the mouse (or a touch) was used last, rather than a controller or
 * the keys. The browser shows the focus ring by what was used last, and focus
 * moved by script (all of a controller's) goes by that: after a click, or
 * coming back to the window, a controller's focus had no ring. So focus moved
 * here asks for its ring.
 */
let pointing = false;
/** The focus on `el`, by a controller or the keys: with its ring. */
const show = (el: HTMLElement, preventScroll = false) => el.focus({ preventScroll, focusVisible: true } as FocusOptions);

/** The bar across the top, above the page: the launcher's, or TV mode's own while TV mode shows (tv.tsx). */
const topBar = () => [...document.querySelectorAll<HTMLElement>(".topbar, .tv-bar")].find((bar) => bar.getClientRects().length > 0) ?? null;
/** What scrolls the page: main, under the top bar (app.css), the window itself not scrolling. */
export const pageArea = () => document.querySelector<HTMLElement>(".shell > main") ?? document.documentElement;

/** The control in that direction from `current`, of `among`. */
function ahead(direction: Direction, current: HTMLElement, among: HTMLElement[]) {
  const from = current.getBoundingClientRect();
  let best: HTMLElement | undefined;
  let bestScore = Infinity;
  for (const el of among) {
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
  return best;
}

/**
 * What Up and Down keep to, leaving only once nothing more in it is that way,
 * so the page beside one isn't left for it (Play's Down is the page's tabs, not
 * the library's sidebar): the top bar, the library's sidebar, Settings' list,
 * and a panel open over the page (View options), which the page's Up and Down
 * don't go into either. Sideways, only the top bar keeps to itself (the
 * account's Left is Browse, not the page under the bar). A panel, once in it,
 * is never left by a direction at all (move).
 */
const AREAS = ".topbar, .library-sidebar, .settings-nav, .menu.floating";

export function move(direction: Direction) {
  const all = candidates();
  const current = document.activeElement as HTMLElement | null;
  if (!current || !all.includes(current)) return all[0] && show(all[0]);
  // From the button of a panel it has open (library.tsx's Popover, put at the
  // page's end, outside the sidebar the button is in): into the panel, when
  // it's that way. Anywhere else, the panel closes as the focus leaves it.
  const panel = current.getAttribute("aria-expanded") === "true" ? document.querySelector<HTMLElement>(".menu.floating") : null;
  const into = panel && ahead(direction, current, all.filter((el) => panel.contains(el)));
  if (into) return show(into);
  // In such a panel (a dropdown's list too): within it whichever way, past its last item or first going nowhere, so
  // a step too far doesn't close it. Back is the way out, as from a dialog.
  const floating = current.closest(".menu.floating");
  if (floating) {
    const inside = all.filter((el) => floating.contains(el));
    // Sideways, what's on its line first, however far along: a game's port's download's × past its bar, not the Play
    // of the port above, nearer the line's start.
    const line = current.getBoundingClientRect();
    const level = direction === "left" || direction === "right" ? inside.filter((el) => {
      const r = el.getBoundingClientRect();
      return r.bottom > line.top && r.top < line.bottom;
    }) : [];
    const to = (level.length ? ahead(direction, current, level) : null) ?? ahead(direction, current, inside);
    if (to) {
      show(to, true);
      reveal(to);
    }
    return;
  }
  // From the page, the top bar is only for Up, once nothing on the page is above:
  // it's just above the page, so it can be nearer than the
  // control above, still scrolled away.
  const bar = topBar();
  const inBar = !!bar && bar.contains(current);
  const allowed = !bar || inBar ? all : all.filter((el) => !bar.contains(el));
  const area = current.closest(AREAS);
  const vertical = direction === "up" || direction === "down";
  // Sideways, what's level with it, or no more than a line or so above or below (a link's Evidence under its line): not
  // something far up or down the page (from a row of Downloads' left, Back at its top; from Back, More like this's last
  // card, out of sight at its foot). Out of the sidebar into the page beside it, whatever's in sight.
  const from = current.getBoundingClientRect();
  const near = (el: HTMLElement) => {
    const r = el.getBoundingClientRect();
    if (r.bottom > from.top && r.top < from.bottom) return true;
    if (r.bottom <= 0 || r.top >= windowBox().height) return false;
    return el.closest(AREAS) !== area || Math.max(r.top - from.bottom, from.top - r.bottom) <= Math.max(from.height, 48);
  };
  const among = vertical ? allowed.filter((el) => el.closest(AREAS) === area) : inBar ? all.filter((el) => bar.contains(el)) : allowed.filter(near);
  let best = ahead(direction, current, among);
  // Past more than a step of what's between (a README's text, a release's notes), a step's
  // scroll instead, until the next control is near: so the d-pad reads through it. Not past a dropdown's
  // list's last option, which would scroll the page from under it.
  if (vertical && !inBar && !pickerPanel() && step(current, direction === "down" ? 1 : -1, best)) return;
  if (!best && vertical) best = ahead(direction, current, all);
  const intoBar = !inBar && best !== undefined && !!bar?.contains(best);
  if (best) {
    // Not with the focus's own scroll, which jumps it into view: eased there instead.
    show(best, true);
    reveal(best);
  }
  // Up into the top bar takes the page to its top, where its title and art are.
  if (intoBar) easeTo(pageArea(), 0);
}

/**
 * Scrolls eased by script, frame by frame, toward where they're headed, which
 * another step moves on: so holding a direction on the d-pad glides down a long
 * grid at its steps' pace, where the focus's own scroll (or the browser's smooth
 * one, starting over with each step) jumped a row at a time.
 */
const easing = new Map<Element, { to: number; frame: number }>();
/**
 * The same sideways, along a row that scrolls that way (a shelf of Browse's): jumped at once, a row moved a whole card
 * in one frame, so every card on it seemed to swap its picture for the next one's.
 */
const easingSideways = new Map<Element, { to: number; frame: number }>();

function easeTo(el: Element, to: number, axis: "scrollTop" | "scrollLeft" = "scrollTop") {
  const [eases, max] = axis === "scrollTop" ? [easing, el.scrollHeight - el.clientHeight] : [easingSideways, el.scrollWidth - el.clientWidth];
  const top = Math.max(0, Math.min(max, Math.round(to)));
  const running = eases.get(el);
  if (running) return void (running.to = top);
  if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return void (el[axis] = top);
  const state = { to: top, frame: 0 };
  eases.set(el, state);
  let last = performance.now();
  const frame = (now: number) => {
    const gap = state.to - el[axis];
    // Most of the way in a tenth of a second, the rest following on.
    const by = gap * (1 - Math.exp(-Math.min(50, now - last) / 60));
    last = now;
    const before = el[axis];
    if (Math.abs(gap) >= 1.5) el[axis] = before + (Math.abs(by) < 1 ? Math.sign(gap) : by);
    // There, or held where it is (its end, nearer than it was).
    if (Math.abs(gap) < 1.5 || el[axis] === before) {
      el[axis] = state.to;
      eases.delete(el);
      return;
    }
    state.frame = requestAnimationFrame(frame);
  };
  state.frame = requestAnimationFrame(frame);
}

/** Where an area is headed: an eased scroll's end, or where it is. */
const headed = (el: Element) => easing.get(el)?.to ?? el.scrollTop;

/** An eased scroll stopped where it is (all of them, without `el`): the stick, a wheel or a finger moves it now. */
function stopEasing(el?: Element) {
  for (const eases of [easing, easingSideways])
    for (const [area, state] of eases)
      if (!el || area === el) {
        cancelAnimationFrame(state.frame);
        eases.delete(area);
      }
}

/**
 * A control eased into view in what scrolls under it, and the page if that's a list on it, inside each one's scroll
 * padding (main's keeps it off the window's edges). By its place on the screen, which the interface size zooms, not
 * the scroll, which it doesn't: jumping there with scrollIntoView and back to ease from cost most of a step.
 */
function reveal(el: HTMLElement) {
  const area = scrollArea(el);
  if (area === undefined) return;
  const zoom = boxZoom();
  const r = el.getBoundingClientRect();
  for (const scroller of new Set([scrolling(area), pageArea()])) {
    const style = getComputedStyle(scroller);
    const top = scroller.getBoundingClientRect().top + (scroller.clientTop + (parseFloat(style.scrollPaddingTop) || 0)) * zoom;
    const bottom = top + (scroller.clientHeight - (parseFloat(style.scrollPaddingTop) || 0) - (parseFloat(style.scrollPaddingBottom) || 0)) * zoom;
    // Taller than what shows: its top.
    const by = r.top < top || r.height > bottom - top ? r.top - top : r.bottom > bottom ? r.bottom - bottom : 0;
    // From where it is, which its place on the screen is of, though an eased scroll be on its way elsewhere.
    if (by) easeTo(scroller, scroller.scrollTop + by / zoom);
  }
  // Sideways, in a row that scrolls that way (a shelf, the playlists, a narrow window's top bar): eased the nearest way
  // into view inside its scroll padding, as up and down are. Not scrollIntoView, which jumped there, and scrolled the
  // page up or down too, under the ease above.
  const row = sidewaysRow(el);
  if (row) {
    const style = getComputedStyle(row);
    const left = row.getBoundingClientRect().left + (row.clientLeft + (parseFloat(style.scrollPaddingLeft) || 0)) * zoom;
    const right = left + (row.clientWidth - (parseFloat(style.scrollPaddingLeft) || 0) - (parseFloat(style.scrollPaddingRight) || 0)) * zoom;
    const by = r.left < left || r.width > right - left ? r.left - left : r.right > right ? r.right - right : 0;
    if (by) easeTo(row, row.scrollLeft + by / zoom, "scrollLeft");
  }
}

/** What scrolls sideways around a control, if anything does. */
function sidewaysRow(el: Element) {
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
    if (/auto|scroll/.test(getComputedStyle(p).overflowX) && p.scrollWidth > p.clientWidth + 1) return p;
  }
  return null;
}

/**
 * What scrolls under `el`: a list or dialog it's in that scrolls, else the
 * page (null). Undefined when nothing does: a dialog that doesn't scroll
 * keeps the page under it still.
 */
function scrollArea(el: Element | null): Element | null | undefined {
  const page = pageArea();
  for (let p = el?.parentElement; p && p !== document.body && p !== page; p = p.parentElement) {
    if (/auto|scroll/.test(getComputedStyle(p).overflowY) && p.scrollHeight > p.clientHeight + 1) return p;
  }
  return document.querySelector('[role="dialog"]') ? undefined : null;
}

/** The controller's hints along the bottom (pad-hints.tsx), when they show: over the page, as the top bar is. */
const hintsBar = () => document.querySelector<HTMLElement>(".pad-hints");

/**
 * The part of an area that shows: for the page, below the top bar and above
 * the hints, for what's under them (`r`, when it's given: the hints are only
 * beside the library's sidebar, which runs on under them).
 */
function viewOf(area: Element | null, r?: DOMRect) {
  const zoom = boxZoom();
  if (area) {
    const top = area.getBoundingClientRect().top + area.clientTop * zoom;
    return { top, bottom: top + area.clientHeight * zoom };
  }
  const hints = hintsBar()?.getBoundingClientRect();
  const over = hints && (!r || (r.right > hints.left && r.left < hints.right));
  return { top: topBar()?.getBoundingClientRect().bottom ?? 0, bottom: over ? hints.top : document.documentElement.clientHeight * zoom };
}

const scrolling = (area: Element | null) => area ?? pageArea();
const canScroll = (area: Element | null, way: number) => {
  const el = scrolling(area);
  return way > 0 ? el.scrollTop + el.clientHeight < el.scrollHeight - 1 : el.scrollTop > 0;
};
const scrollBy = (area: Element | null, top: number, behavior: ScrollBehavior) => scrolling(area).scrollBy({ top, behavior });

/**
 * After an area scrolls by `delta` (or has), the focus on a control that
 * shows, if it's going out of sight: the nearest in its column. So Confirm
 * never presses something out of sight.
 */
function keepInView(area: Element | null, delta = 0) {
  const current = document.activeElement as HTMLElement | null;
  if (!current || current === document.body) return;
  const shows = (r: DOMRect) => {
    const box = viewOf(area, r);
    return r.bottom - delta > box.top && r.top - delta < box.bottom;
  };
  const from = current.getBoundingClientRect();
  if (shows(from)) return;
  const bar = topBar();
  // Nearest in its column, and all of it showing if one is: not one about to go too, a frame or two of the stick on.
  const whole = (r: DOMRect) => {
    const box = viewOf(area, r);
    return r.top - delta >= box.top && r.bottom - delta <= box.bottom;
  };
  let to: HTMLElement | undefined;
  let rank = Infinity;
  for (const el of candidates()) {
    if (el === current || bar?.contains(el) || takesText(el)) continue;
    const r = el.getBoundingClientRect();
    if (!shows(r) || r.right <= from.left || r.left >= from.right) continue;
    const score = Math.abs(r.top - from.top) + (whole(r) ? 0 : 1e6);
    if (score >= rank) continue;
    // Last, for the few left: what it's in, which takes asking each of its ancestors' styles (each frame of the stick).
    if (scrollArea(el) !== area) continue;
    [to, rank] = [el, score];
  }
  if (to) show(to, true);
}

/** A d-pad step's scroll, when `target` (the next control that way, if any) is more than a step past what shows. */
function step(current: HTMLElement, way: 1 | -1, target: HTMLElement | undefined) {
  const area = scrollArea(current);
  if (area === undefined || !canScroll(area, way)) return false;
  const box = viewOf(area);
  const by = Math.round((box.bottom - box.top) * 0.4);
  const r = target?.getBoundingClientRect();
  const there = r && viewOf(area, r);
  const past = !r || !there ? Infinity : way > 0 ? r.top - there.bottom : there.top - r.bottom;
  if (past <= by) return false;
  // Before the scroll moves what it measures.
  keepInView(area, way * by);
  easeTo(scrolling(area), headed(scrolling(area)) + (way * by) / boxZoom());
  return true;
}

/** Page Up and Page Down: most of a screen of what the focus is in. */
function page(way: 1 | -1) {
  const area = scrollArea(document.activeElement);
  if (area === undefined) return;
  const box = viewOf(area);
  const by = way * Math.round((box.bottom - box.top) * 0.85);
  keepInView(area, by);
  easeTo(scrolling(area), headed(scrolling(area)) + by / boxZoom());
}

/**
 * What the buttons would do now, for the hints along the bottom
 * (pad-hints.tsx), as act() would: Confirm and Options on the focused
 * control, Back where there's something to go back from, Back to top on a
 * scrolled page, and Scroll on one that scrolls.
 */
export function buttonsNow(): [Action, string][] {
  const focused = document.activeElement instanceof HTMLElement && document.activeElement !== document.body ? document.activeElement : null;
  const now: [Action, string][] = [];
  if (focused) {
    const play = playOf(focused);
    const does = !showing(focused)
      ? t({ message: "Show", comment: "Controller hint: what Confirm does, scrolling the focused control into view" })
      : takesText(focused)
        ? t({ message: "Type", comment: "Controller hint: what Confirm does on a text field" })
        : play
          ? play.innerText.trim() || t({ message: "Play", comment: "Controller hint: what Confirm does on a library card" })
          : focused.matches("article .card-open")
            ? t({ message: "Open", comment: "Controller hint: what Confirm does on an app card" })
            : t({ message: "Select", comment: "Controller hint: what Confirm does on any other control" });
    now.push(["confirm", does]);
    const get = quickButton(focused);
    if (focused.closest("[data-menu]")) now.push(["options", t({ message: "Menu", comment: "Controller hint: what Options does, opening a menu" })]);
    else if (get) now.push(["options", get.innerText.trim() || t({ message: "Get", comment: "Controller hint: what Options does on a catalog card or an app's page, installing the app" })]);
    // A game's card (library-tile.tsx): the list of its ports, which Confirm opens too where none is installed to play.
    else if (focused.closest(".game-tile")) now.push(["options", t`Ways to play`]);
    else if (focused.closest("article")?.querySelector(".card-open")) now.push(["options", t({ message: "Details", comment: "Controller hint: what Options does on an app card, opening its page" })]);
  }
  if (backGoes()) now.push(["back",t({ message: "Back", comment: "Controller hint: what Back does, closing or going back" })]);
  // Tabs showing (an app's page's, TV mode's, the top bar's): LB and RB change them, said here as by the tabs themselves.
  const tabs = !inMenu() && Boolean(shoulderTabs());
  if (tabs) now.push(["nextTab", t({ message: "Tabs", comment: "Controller hint: what the shoulder buttons do where there are tabs, change tab" })]);
  // A grid of cards: the triggers jump a letter at a time when it's by name, else a screen.
  const grid = jumpGrid();
  if (grid) now.push(["nextLetter", grid.dataset.jump === "letters" ? t({ message: "Letters", comment: "Controller hint: what the triggers do in a library listed by name, jump letter to letter" }) : t({ message: "Pages", comment: "Controller hint: what the triggers do in a library in another order, jump a screen at a time" })]);
  const area = scrollArea(focused);
  if (area !== undefined && scrolling(area).scrollTop > 0) now.push(["top", t({ message: "Top", comment: "Controller hint: what Back to top does" })]);
  if (area !== undefined && (canScroll(area, 1) || canScroll(area, -1))) now.push(["scrollDown", t({ message: "Scroll", comment: "Controller hint: what the scroll stick does" })]);
  return now;
}

/** Whether a control shows (the top bar's always do). Reading on through text, the focus can be left out of sight. */
function showing(el: Element) {
  const area = scrollArea(el);
  if (area === undefined || topBar()?.contains(el)) return true;
  const r = el.getBoundingClientRect();
  const box = viewOf(area, r);
  return r.bottom > box.top && r.top < box.bottom;
}

let gliding = 0;
/**
 * The right stick: a smooth scroll of what the focus is in for as long as it's
 * held, faster the longer it is (about 10 to 33 pixels a frame), with the focus
 * kept on what shows. 0 stops it.
 */
function glide(way: -1 | 0 | 1) {
  cancelAnimationFrame(gliding);
  gliding = 0;
  const area = way ? scrollArea(document.activeElement) : undefined;
  if (area === undefined) return;
  stopEasing(scrolling(area));
  const since = performance.now();
  let last = since;
  const frame = (now: number) => {
    const speed = Math.min(2000, 600 + (now - since) * 1.4);
    scrollBy(area, (way * speed * (now - last)) / 1000, "instant");
    last = now;
    keepInView(area);
    gliding = canScroll(area, way) ? requestAnimationFrame(frame) : 0;
  };
  gliding = requestAnimationFrame(frame);
}

/**
 * Where the focus was, so it's put back when what had it goes (a dialog
 * closing, a page opening, a button swapped for another), rather than left
 * nowhere, which sends the next press to the first control of all.
 */
let last: { el: HTMLElement; dialog: Element | null; page: Element | null; x: number; y: number } | null = null;
/** What had the focus before each dialog took it. */
const openers = new WeakMap<Element, HTMLElement>();
/** The page showing: the top one in App.tsx's stack, or none (the library or the catalog). */
const pageShowing = () => [...document.querySelectorAll<HTMLElement>(".page-layer")].find((l) => !l.hidden) ?? null;
const usable = (el: Element) => shown(el) && !(el as HTMLButtonElement).disabled;

/**
 * The card the focus is in, marked [data-focus] for app.css in place of :focus-within, which WebKit clears and sets
 * on every ancestor of what has the focus as it moves: a rule with it ahead of a card's parts had it restyle the
 * whole page with each step, most of a tenth of a second a step on a Steam Deck in a large library.
 */
let focusCard: Element | null = null;
function markCard(el: EventTarget | null) {
  const card = el instanceof Element ? el.closest(".entry-card") : null;
  if (card === focusCard) return;
  focusCard?.removeAttribute("data-focus");
  card?.setAttribute("data-focus", "");
  focusCard = card;
}

/**
 * In the Sidebar layout, which side the focus is on, the list or the app's page, while a controller or the keys move
 * it: app.css greys the other back, so where the next press goes is plain. Set on the split only when it changes, and
 * taken off once the mouse or a finger is used.
 */
let focusSplit: Element | null = null;
function markSide(el: EventTarget | null) {
  const split = el instanceof Element && !pointing ? el.closest(".library-split") : null;
  const side = split && el instanceof Element ? (el.closest(".library-sidebar") ? "list" : el.closest(".library-pane") ? "page" : null) : null;
  if (focusSplit && (focusSplit !== split || !side)) focusSplit.removeAttribute("data-focus-side");
  focusSplit = side ? split : null;
  if (side && split!.getAttribute("data-focus-side") !== side) split!.setAttribute("data-focus-side", side);
}

/**
 * Where a controller's (or the keys') focus is, as a console shows it: one rounded box over what has the focus (a
 * card's art, a sidebar row, a tab), with a faint fill and a slow pulse (app.css). It glides to what the focus moves
 * to in a tenth of a second, easing in without overshooting, and its glide is aimed again each frame the page scrolls
 * under it, so it lands on what it's for rather than trailing where that was. It's cut off where its control is, by
 * what scrolls around it: a card scrolled half under the top bar has its box go under it too, rather than over the
 * bar. It stands in for the focus ring, and goes
 * once the mouse or a finger is used. One box moved about, not a style on each control: a fixed layer's place changes
 * without restyling the page under it.
 */
let halo: HTMLElement | null = null;
let haloFor: Element | null = null;
/** What it was put on last, to glide only as it comes to something new, not as the page scrolls under it. */
let haloWas: Element | null = null;
/** Where it was put last, and the glide from there that may be under way. */
type HaloBox = { x: number; y: number; w: number; h: number };
let haloBox: HaloBox | null = null;
let haloGlide: { from: HaloBox; run: Animation } | null = null;
const haloFrameOf = (b: HaloBox) => ({ transform: `translate(${b.x}px, ${b.y}px)`, width: `${b.w}px`, height: `${b.h}px` });
/**
 * What clips the control it's over: what scrolls around it or hides what overflows it, found once for each control.
 * Only while it has more in it than shows (and always the page, under the top bar): a row of tabs that all fit, which
 * would scroll were the window narrower, hides nothing, and cut the box off at the first and last tab's ends.
 */
let haloClips: Element[] = [];
function clipsOf(el: Element) {
  const clips: Element[] = [];
  const page = pageArea();
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
    const style = getComputedStyle(p);
    if (style.overflowX === "visible" && style.overflowY === "visible") continue;
    if (p === page || p.scrollWidth > p.clientWidth + 1 || p.scrollHeight > p.clientHeight + 1) clips.push(p);
  }
  return clips;
}
let haloFrame = 0;
/** How far out from the box its glow reaches (app.css's .pad-halo::before: 2 pixels' spread and 22 of blur). */
const HALO_GLOW = 24;
/**
 * A card's own button is the card, or the card's art where its names are under it (a box art card, a library card
 * with names): the art is what a console frames. A field or dropdown is its whole box, with its icon and chevron (a
 * search's label, a quiet select, a filter pill), and a tickbox its whole line, with its words. Its other buttons, and
 * the rest, are themselves.
 */
const haloTarget = (el: Element) => {
  if (el.matches(".card-open")) {
    const card = el.closest(".entry-card");
    const art = card?.matches(".box, .layout-grid:not(.no-names, .image-icon) .library-tile") ? card.querySelector(".card-cover") : null;
    return art ?? card ?? el;
  }
  // A featured app's slide: the whole banner, as it's framed and rounded.
  if (el.matches(".featured-open")) return el.closest(".featured-slides") ?? el;
  return (el.matches("input, select, textarea") ? el.closest("label.search, .quiet-select, .pill, label.toggle") : null) ?? el;
};
function placeHalo() {
  haloFrame = 0;
  if (!halo) return;
  const el = haloFor;
  const rect = el?.isConnected && !pointing && el !== document.body ? el.getBoundingClientRect() : null;
  const on = Boolean(rect && rect.width && rect.height);
  document.documentElement.toggleAttribute("data-halo", on);
  halo.hidden = !on;
  if (!on) [haloWas, haloBox] = [null, null];
  if (!on || !rect || !el) return;
  // The interface size zooms the page, the box with it; its place is in unzoomed pixels (zoom.ts).
  const zoom = boxZoom();
  // A few pixels out from what it's over; inside the window's own buttons, which run to the window's edge. Kept on
  // the screen wherever it is: a box hanging past the window's edge was cut off.
  const inside = Boolean(el.closest(".window-controls"));
  const pad = inside ? -3 : 4;
  const [right, bottom] = [window.innerWidth / interfaceZoom() - 1, window.innerHeight / interfaceZoom() - 1];
  const x = Math.max(1, rect.left / zoom - pad);
  const y = Math.max(1, rect.top / zoom - pad);
  let box = { x, y, w: Math.max(0, Math.min(right, rect.right / zoom + pad) - x), h: Math.max(0, Math.min(bottom, rect.bottom / zoom + pad) - y) };
  // Cut off where its control is: inside each of the clipping ancestors' padding (less a scrollbar), with room for its
  // glow wherever there's room for it.
  if (el !== haloWas) haloClips = clipsOf(el);
  let [top, left, low, far] = [-Infinity, -Infinity, Infinity, Infinity];
  for (const clip of haloClips) {
    const r = clip.getBoundingClientRect();
    top = Math.max(top, r.top / zoom + clip.clientTop);
    left = Math.max(left, r.left / zoom + clip.clientLeft);
    low = Math.min(low, r.top / zoom + clip.clientTop + clip.clientHeight);
    far = Math.min(far, r.left / zoom + clip.clientLeft + clip.clientWidth);
  }
  // Its control showing whole, close to what clips it (a pill in a row that scrolls): the box drawn in to fit there,
  // rather than its rounded frame cut square across.
  if (rect.top / zoom >= top && rect.left / zoom >= left && rect.bottom / zoom <= low && rect.right / zoom <= far) {
    const [fitX, fitY] = [Math.max(box.x, left), Math.max(box.y, top)];
    box = { x: fitX, y: fitY, w: Math.min(box.x + box.w, far) - fitX, h: Math.min(box.y + box.h, low) - fitY };
  }
  Object.assign(halo.style, haloFrameOf(box));
  const glow = 48;
  const inset = [box.y - top, box.x + box.w - far, box.y + box.h - low, box.x - left].map((d, i) => Math.max(i === 0 || i === 3 ? -d : d, -glow));
  if (inset[0] + inset[2] >= box.h || inset[1] + inset[3] >= box.w) {
    // Scrolled out of sight: no box, and none to glide from once it's back.
    halo.hidden = true;
    document.documentElement.toggleAttribute("data-halo", false);
    [haloWas, haloBox] = [el, null];
    return;
  }
  // Its corners round as what it's over does, no rounder than half its height (a pill's 999px).
  const radius = Math.min(Math.max((parseFloat(getComputedStyle(el).borderTopLeftRadius) || 0) + pad, inside ? 6 : 10), Math.min(box.w, box.h) / 2);
  halo.style.setProperty("--halo-radius", `${radius}px`);
  // The least room round it for its glow (app.css's, 24 pixels out from its edge), less than none where it's cut.
  const [above, after, below, before] = inset.map((d) => -d);
  const room = Math.min(above, after, below, before);
  if (room >= HALO_GLOW) {
    halo.style.clipPath = "";
    halo.style.removeProperty("--halo-glow");
  } else if (room >= 0) {
    // Whole, with less room than its glow takes: the glow made to fade out inside it, and cut round, as the box's own
    // corners are. Cut square, a round control's glow showed the corners of what it's in.
    const corners = [Math.min(above, before), Math.min(above, after), Math.min(below, after), Math.min(below, before)].map((d) => `${radius + d}px`);
    halo.style.clipPath = `inset(${inset.map((d) => `${d}px`).join(" ")} round ${corners.join(" ")})`;
    halo.style.setProperty("--halo-glow", `${Math.max(0, room - 2)}px`);
  } else {
    // Partly out of sight, scrolled under the top bar or past a row's end: cut off straight, as its control is.
    halo.style.clipPath = `inset(${inset.map((d) => `${d}px`).join(" ")})`;
    halo.style.removeProperty("--halo-glow");
  }
  const still = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
  if (el !== haloWas) {
    // Come to something new: a glide there from where it was, easing in, no overshoot.
    haloGlide?.run.cancel();
    haloGlide = haloBox && !still && halo.animate ? { from: haloBox, run: halo.animate([haloFrameOf(haloBox), haloFrameOf(box)], { duration: 110, easing: "cubic-bezier(0.2, 0, 0, 1)" }) } : null;
  } else if (haloGlide?.run.playState === "running") {
    // The page moved under it as it glides: aimed again at where its control is now.
    (haloGlide.run.effect as KeyframeEffect | null)?.setKeyframes([haloFrameOf(haloGlide.from), haloFrameOf(box)]);
  }
  [haloWas, haloBox] = [el, box];
}
const queueHalo = () => void (haloFrame ||= requestAnimationFrame(placeHalo));
/** What has the focus moved with no scroll or transition to say so (a panel placed again under its button): the box with it. */
export const haloMoved = queueHalo;
/** What it's over changing size or place as it animates (a search widening as it takes the focus): followed each frame till it's done. */
let moving = 0;
const follow = () => void (placeHalo(), moving > 0 && requestAnimationFrame(follow));
const moves = (e: Event) => e.target instanceof Element && e.target !== halo && !halo?.contains(e.target) && Boolean(haloFor && (e.target === haloFor || e.target.contains(haloFor)));
function startHalo() {
  halo = document.createElement("div");
  halo.className = "pad-halo";
  halo.hidden = true;
  halo.setAttribute("aria-hidden", "true");
  document.body.appendChild(halo);
  document.addEventListener("focusin", (e) => ((haloFor = e.target instanceof Element ? haloTarget(e.target) : null), queueHalo()), true);
  document.addEventListener("focusout", (e) => e.relatedTarget === null && ((haloFor = null), queueHalo()), true);
  // Along with the page as it scrolls (a held d-pad's eased scroll too) and the window as it changes size.
  window.addEventListener("scroll", queueHalo, { capture: true, passive: true });
  window.addEventListener("resize", queueHalo);
  document.addEventListener("transitionrun", (e) => moves(e) && moving++ === 0 && requestAnimationFrame(follow), true);
  const settled = (e: Event) => moves(e) && ((moving = Math.max(0, moving - 1)), queueHalo());
  document.addEventListener("transitionend", settled, true);
  document.addEventListener("transitioncancel", settled, true);
}

function noteFocus(e: FocusEvent) {
  const el = e.target;
  if (!(el instanceof HTMLElement)) return;
  const dialog = el.closest('[role="dialog"]');
  if (dialog && last && !dialog.contains(last.el) && !openers.has(dialog)) openers.set(dialog, last.el);
  const r = el.getBoundingClientRect();
  const { scrollLeft, scrollTop } = pageArea();
  const zoom = boxZoom();
  last = { el, dialog, page: pageShowing(), x: (r.left + r.width / 2) / zoom + scrollLeft, y: (r.top + r.height / 2) / zoom + scrollTop };
}

function refocus() {
  const now = document.activeElement;
  if (now && now !== document.body && usable(now)) return;
  // Still there: it was let go of (a click on nothing), not taken away.
  if (!last || usable(last.el)) return;
  const all = candidates();
  // Not a text field, unless it's what opened a dialog: on a phone that would bring up its keyboard.
  const others = all.filter((el) => !takesText(el));
  const bar = topBar();
  const opener = last.dialog && !last.dialog.isConnected ? openers.get(last.dialog) : undefined;
  let to = opener && all.includes(opener) ? opener : undefined;
  // Another page: its first control, or the first where it says the focus starts (data-autofocus: an app's page in
  // TV mode, at its Play).
  const page = pageShowing();
  const start = page?.querySelector("[data-autofocus]");
  if (!to && page !== last.page) to = (start && others.find((el) => start.contains(el))) || others.find((el) => (page ? page.contains(el) : !bar?.contains(el)));
  // Else the control now nearest where it was.
  if (!to) {
    const { scrollLeft, scrollTop } = pageArea();
    const zoom = boxZoom();
    let nearest = Infinity;
    for (const el of others) {
      if (bar?.contains(el) && !bar.contains(last.el)) continue;
      const r = el.getBoundingClientRect();
      const d = Math.hypot((r.left + r.width / 2) / zoom + scrollLeft - last.x, (r.top + r.height / 2) / zoom + scrollTop - last.y);
      if (d < nearest) [to, nearest] = [el, d];
    }
  }
  // With its ring unless the mouse was what closed or opened it.
  to?.focus({ preventScroll: true, focusVisible: !pointing } as FocusOptions);
}

/** What scrolls under the focus: a scrolled list or dialog it's in, else the page. */
function scroller(from: Element | null): Element {
  const dialogs = document.querySelectorAll('[role="dialog"]');
  const dialog = dialogs[dialogs.length - 1];
  for (let el = from; el && el !== document.body; el = el.parentElement) {
    if (el.scrollTop > 0 && /auto|scroll/.test(getComputedStyle(el).overflowY)) return el;
    if (el === dialog) return el;
  }
  return dialog ?? pageArea();
}

/**
 * Back to the top of a long page (or of the dialog or list the focus is in).
 * The focus goes up too, to the top control in its column, so the next move
 * starts there instead of scrolling back down; from outside the page (its
 * Back to top button), to the top control of all. Nothing focused, it stays so.
 */
export function toTop(from: Element | null) {
  const at = from && from !== document.body ? from : null;
  const area = scroller(at);
  if (at) {
    const page = area === pageArea();
    const within = page ? document.querySelector("main") : area;
    const all = candidates().filter((el) => within?.contains(el));
    const was = within?.contains(at) ? at.getBoundingClientRect() : null;
    const column = was ? all.filter((el) => { const r = el.getBoundingClientRect(); return r.left < was.right && r.right > was.left; }) : [];
    // Scrolling moves every control in the area by as much, so the top one now is the top one then.
    const first = (column.length ? column : all).reduce<HTMLElement | undefined>(
      (best, el) => (!best || el.getBoundingClientRect().top < best.getBoundingClientRect().top - 1 ? el : best),
      undefined,
    );
    if (first) show(first, true);
  }
  easeTo(area, 0);
}

const NOT_TEXT = new Set(["checkbox", "radio", "range", "button", "submit", "reset", "color", "file", "image"]);
/** A field that takes typing. */
export const takesText = (el: EventTarget | null): el is HTMLInputElement | HTMLTextAreaElement =>
  (el instanceof HTMLInputElement && !NOT_TEXT.has(el.type)) || el instanceof HTMLTextAreaElement;

/** The caret at a text field's start, for Left, or its end, for Right, with nothing selected: further is out of it. */
function atEnd(field: HTMLInputElement | HTMLTextAreaElement, action: Action) {
  // Null where the field has no caret to ask about (an email or number field): it keeps its keys.
  const { selectionStart: start, selectionEnd: end, value } = field;
  if (start === null || start !== end) return false;
  return (action === "left" && start === 0) || (action === "right" && start === value.length);
}

let editing: Element | null = null;
/** How much of the window Steam's keyboard covers, from its bottom: 360 of a Steam Deck's 800 pixels. */
const STEAM_KEYBOARD = "45vh";
/**
 * A field scrolled above Steam's keyboard, in what scrolls under it: a dialog
 * that scrolls, or the page. Not scrollIntoView, which scrolled the page under
 * a dialog too. A dialog that doesn't scroll has moved up whole (app.css).
 */
function aboveKeyboard(field: HTMLElement) {
  const area = scrollArea(field);
  if (area === undefined) return;
  // On the screen, which the interface size zooms; the scroll, which it doesn't (reveal).
  const bottom = area ? area.getBoundingClientRect().bottom : windowBox().height * 0.55;
  const over = field.getBoundingClientRect().bottom + 12 - bottom;
  if (over > 0) easeTo(scrolling(area), headed(scrolling(area)) + over / boxZoom());
}

/**
 * Edits a text field without a keyboard: as in Quiver Launcher 3, moving onto
 * it only highlights it, and Confirm (or a tap) starts editing, which brings
 * up Steam's on-screen keyboard where there is one. Leaving the field hides it.
 * Asked for on every Confirm, so a keyboard closed in Steam comes back. While
 * it's up, the field is kept above it (app.css, --steam-keyboard).
 */
function edit(field: HTMLInputElement | HTMLTextAreaElement) {
  field.focus();
  void native.steamKeyboard(true).then((shown) => {
    if (!shown) return;
    document.documentElement.style.setProperty("--steam-keyboard", STEAM_KEYBOARD);
    // Once the room is made: a dialog moves up into it.
    requestAnimationFrame(() => aboveKeyboard(field));
    if (editing === field) return;
    editing = field;
    const left = () => {
      // Steam's keyboard taking the window's focus (in Game Mode) blurs the field too, though it keeps the
      // page's focus: hiding the keyboard then closed it as it opened.
      if (document.activeElement === field && field.isConnected) return;
      field.removeEventListener("blur", left);
      document.removeEventListener("focusin", left, true);
      editing = null;
      void native.steamKeyboard(false);
      // Unless another field has it now.
      requestAnimationFrame(() => editing || document.documentElement.style.removeProperty("--steam-keyboard"));
    };
    field.addEventListener("blur", left);
    // A field that goes (its dialog closing) has no blur: the focus put back elsewhere says it was left.
    document.addEventListener("focusin", left, true);
  });
}

/** Back has something to close or go back from: a dialog, a menu or a dropdown's list, a floating panel, the sidebar's filters, or a page. */
export const backGoes = () => Boolean(pickerPanel() || document.querySelector('[role="dialog"], [role="menu"], [role="listbox"], .menu.floating, .sidebar-filters, .page-layer'));

/**
 * Back from outside the bindings (Android's button or gesture, main.tsx): closes or goes back as a controller's Back
 * does, without showing the focus box a touch doesn't need; false when there's nothing to.
 */
export function goBack() {
  if (!backGoes()) return false;
  if (pickerPanel()) closePicker();
  else pressEscape();
  return true;
}

/**
 * Escape as the key sends it, from the focus up: the window's capture listeners first (a narrow Settings page's, back
 * to its list), then its others. Sent to the window itself, Android's webview ran them as they were added instead, the
 * launcher's going back a page first, so Back from a Settings section skipped its list.
 */
/** Sent to the window by Forward (a mouse's forward button, Alt+→), for the page stack to open what Back left. */
export const FORWARD = "quiver:forward";

const pressEscape = () => (document.activeElement ?? document.body).dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));

/**
 * Moving by a controller or the keys: the page's hover looks are let be (app.css), so a card under a mouse left resting
 * isn't lit as if it were chosen too, each card scrolling under it in turn. The mouse moving gives them back.
 */
const navigating = (on: boolean) => document.documentElement.toggleAttribute("data-navigating", on);

function act(action: Action) {
  const focused = document.activeElement as HTMLElement | null;
  pointing = false;
  navigating(true);
  // Back from the mouse: the box shows where the focus is before this press moves it.
  if (focused && focused !== document.body) ((haloFor = haloTarget(focused)), queueHalo());
  if (action === "confirm") {
    // Out of sight (left there by reading on through text): brought back into view, not pressed unseen.
    if (focused && focused !== document.body && !showing(focused)) reveal(focused);
    else if (takesText(focused)) edit(focused);
    // A click never opens a dropdown's own list: its options in a menu instead.
    else if (focused instanceof HTMLSelectElement && !focused.multiple) openPicker(focused);
    else {
      // On a library card, Confirm plays it (or gets it), as in Quiver Launcher 3; its page is in its menu.
      (playOf(focused) ?? focused)?.click();
    }
  }
  // A dropdown's list closes by itself: Escape would reach the page or dialog under it too.
  else if (action === "back" && pickerPanel()) closePicker();
  else if (action === "back") pressEscape();
  // The pages are App.tsx's: it reopens the one Back last left.
  else if (action === "forward") window.dispatchEvent(new Event(FORWARD));
  // A library card's or sidebar row's menu, else a card's or an app page's Get (or Play), else the focused app's page.
  else if (action === "options") {
    const owner = focused?.closest("[data-menu]");
    if (owner) owner.dispatchEvent(new KeyboardEvent("keydown", { key: "ContextMenu", bubbles: true }));
    else (quickButton(focused) ?? focused?.closest("article")?.querySelector<HTMLElement>(".card-open"))?.click();
  }
  else if (action === "top") toTop(focused);
  else if (action === "prevTab" || action === "nextTab") nextTab(action === "prevTab" ? -1 : 1);
  else if (action === "prevLetter" || action === "nextLetter") jump(action === "prevLetter" ? -1 : 1);
  else if (action === "scrollUp" || action === "scrollDown") page(action === "scrollUp" ? -1 : 1);
  // A menu that moves through its items itself (an app's menu, card-menu.tsx), or Browse's featured apps through
  // theirs (left and right), gets the direction as an arrow key.
  else if (focused && ownArrow(focused, ARROWS[action as Direction])) {
    focused.dispatchEvent(new KeyboardEvent("keydown", { key: ARROWS[action as Direction], bubbles: true, cancelable: true }));
  }
  else move(action);
}

const ARROWS: Record<Direction, string> = { up: "ArrowUp", down: "ArrowDown", left: "ArrowLeft", right: "ArrowRight" };
/**
 * The control `el` is in takes this arrow key itself (data-arrows): every one, as an app's menu does, or with
 * data-arrows="x" left and right alone, as the featured apps across Browse's top do, up and down still leaving it.
 */
function ownArrow(el: EventTarget | null, key: string) {
  const owner = el instanceof Element ? el.closest("[data-arrows]") : null;
  return Boolean(owner) && (owner?.getAttribute("data-arrows") !== "x" || key === "ArrowLeft" || key === "ArrowRight");
}

/**
 * The shoulder buttons (Ctrl+Page Up and Down on the keys): the tab before or after the open one, in the tabs showing
 * (an app's page's, or a dialog's over it), as a console's store pages go. The focus goes along when it's on the tabs.
 */
function nextTab(step: -1 | 1) {
  if (inMenu()) return;
  const list = shoulderTabs();
  const tabs = list ? [...list.querySelectorAll<HTMLElement>(list.matches('[role="tablist"]') ? '[role="tab"]' : "button")].filter(shown) : [];
  const at = tabs.findIndex((tab) => tab.getAttribute("aria-selected") === "true" || tab.getAttribute("aria-current") === "page");
  const to = tabs[at + step];
  if (!to) return;
  const along = list!.contains(document.activeElement);
  to.click();
  if (along) show(to);
  // Kept in sight: the row scrolls sideways where the tabs don't all fit.
  to.scrollIntoView({ block: "nearest", inline: "nearest" });
}

/**
 * The tabs the shoulder buttons change: those showing in the dialog on top, else on the page (an app's page's, TV
 * mode's), else, with no page or dialog over them, the top bar's Library and Browse (data-tabs), as TV mode's are.
 */
function shoulderTabs() {
  const dialogs = document.querySelectorAll('[role="dialog"]');
  const scope = dialogs[dialogs.length - 1] ?? document;
  const list = [...scope.querySelectorAll<HTMLElement>('[role="tablist"]')].find(shown);
  if (list || dialogs.length || pageShowing()) return list;
  return [...document.querySelectorAll<HTMLElement>(".topbar [data-tabs]")].find(shown);
}

/** The focus is in an open menu (a card's, card-menu.tsx), which the shoulder buttons and triggers leave be. */
const inMenu = () => Boolean(document.activeElement?.closest('[role="menu"]'));

/** The grid of cards the triggers jump through: the focus's, else the one showing (none under a dialog or a menu). */
function jumpGrid() {
  if (document.querySelector('[role="dialog"]') || inMenu()) return undefined;
  const focused = document.activeElement;
  const grid = focused instanceof Element ? focused.closest<HTMLElement>("[data-jump]") : null;
  return grid && shown(grid) ? grid : [...document.querySelectorAll<HTMLElement>("[data-jump]")].find(shown);
}

/**
 * The triggers (Ctrl+Up and Down on the keys), in a grid of cards: sorted by name (data-jump="letters", each card
 * under its data-letter), the first app under the next letter that has any, or back to the first under this one, then
 * the one before; in another order, a screenful of rows on, in about the focus's column. The grid hears the letter
 * come to (a "letterjump" event), for its index to show it large.
 */
function jump(step: -1 | 1) {
  const grid = jumpGrid();
  if (!grid) return;
  const cards = [...grid.querySelectorAll<HTMLElement>(".card-open")].filter(shown);
  if (!cards.length) return;
  const focused = document.activeElement;
  const at = cards.findIndex((c) => focused instanceof Node && (c === focused || Boolean(c.closest(".entry-card")?.contains(focused))));
  let to: HTMLElement | undefined;
  if (grid.dataset.jump === "letters") {
    const letter = (el: Element) => el.closest("[data-letter]")?.getAttribute("data-letter") ?? "";
    const firstOf = (i: number) => {
      while (i > 0 && letter(cards[i - 1]) === letter(cards[i])) i--;
      return i;
    };
    if (at < 0) to = cards[0];
    else if (step > 0) to = cards.slice(at + 1).find((c) => letter(c) !== letter(cards[at]));
    else {
      const first = firstOf(at);
      to = first < at ? cards[first] : first > 0 ? cards[firstOf(first - 1)] : undefined;
    }
    if (to) grid.dispatchEvent(new CustomEvent("letterjump", { detail: letter(to) }));
  } else {
    const from = (cards[at] ?? cards[0]).getBoundingClientRect();
    const area = scrollArea(grid);
    // A screen of rows, less one, so the row it was on still shows.
    const page = Math.max(from.height, scrolling(area ?? null).clientHeight * boxZoom() - from.height * 1.2);
    const target = from.top + step * page;
    let best = Infinity;
    for (const c of cards) {
      const r = c.getBoundingClientRect();
      if (step > 0 ? r.top <= from.top + 1 : r.top >= from.top - 1) continue;
      const score = Math.abs(r.top - target) * 4 + Math.abs(r.left - from.left);
      if (score < best) [to, best] = [c, score];
    }
  }
  if (to && to !== cards[at]) focusOn(to);
}

/** The focus on `el` as a controller's step puts it: with its ring, eased into view. */
export function focusOn(el: HTMLElement) {
  pointing = false;
  show(el, true);
  reveal(el);
}

/** The focus taken to `el` by the page (an answer picked elsewhere on it): eased into view, with its ring unless the mouse was used last. */
export function moveFocus(el: HTMLElement) {
  el.focus({ preventScroll: true, focusVisible: !pointing } as FocusOptions);
  reveal(el);
}

/** A text field to type in, as Confirm on it does: with Steam's keyboard where there's one. */
export const typeInto = (field: HTMLInputElement | HTMLTextAreaElement) => edit(field);

const MODIFIERS = new Set(["Control", "Shift", "Alt", "Meta"]);
/** A Steam Deck's own controls come as Steam's desktop layout's keys: its A Enter, B Escape, Y Space. */
let steamDesktop = false;

export function startSpatialNavigation() {
  startHalo();
  // The card the focus is in marked, first, so measuring where the focus is (noteFocus) restyles once; let go of
  // (not just the window left, which keeps it where it was), no card has it.
  document.addEventListener("focusin", (e) => (markCard(e.target), markSide(e.target)), true);
  document.addEventListener("focusout", (e) => e.relatedTarget === null && document.activeElement !== e.target && markCard(null), true);
  // The focus put back when what had it goes, once the page has changed.
  document.addEventListener("focusin", noteFocus, true);
  let pending = 0;
  new MutationObserver(() => {
    pending ||= window.setTimeout(() => ((pending = 0), refocus()), 50);
  }).observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["hidden", "disabled", "class", "style"] });

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
      if (e.key === "Escape" && pickerPanel()) {
        e.preventDefault();
        e.stopImmediatePropagation();
        return closePicker();
      }
      const pressed = combo(e);
      // A Steam Deck's Y comes as Space in Steam's desktop layout: Options, as Y is, but in a text field (its space bar).
      const action = steamDesktop && pressed === " " && !takesText(e.target) ? "options" : ACTIONS.find((a) => keys[a].includes(pressed));
      if (!action) return;
      // Its own arrow keys, as they are.
      if (e.key.startsWith("Arrow") && ownArrow(e.target, e.key)) return;
      // The sidebar's edge, reached with Tab, takes Left and Right to resize; Up and Down still leave it.
      if ((e.key === "ArrowLeft" || e.key === "ArrowRight") && e.target instanceof Element && e.target.matches(".sidebar-edge")) return;
      const typing = takesText(e.target);
      // Text fields keep their keys; up and down still leave them, and left and right do from the text's start
      // and end (a Steam Deck's d-pad comes as the arrow keys in its desktop).
      const out = typing && atEnd(e.target, action);
      if (typing && !out && action !== "up" && action !== "down" && !(action === "back" && e.key === "Escape")) return;
      // Enter on a control and Escape already do this themselves (but Enter on one out of sight brings it back first).
      // Not Enter on a tickbox or a choice, which only Space ticks: Confirm clicks it, as a controller's A does (a Steam
      // Deck's A is Enter in its desktop).
      const unseen = action === "confirm" && e.target instanceof Element && e.target !== document.body && !showing(e.target);
      const ticks = e.target instanceof HTMLInputElement && (e.target.type === "checkbox" || e.target.type === "radio");
      // Nor a Steam Deck's A (Enter in its desktop) on a library card, which plays it as A does in Game Mode, and as the
      // hints say: the card's own click opened its page, or a game's ways to play.
      const plays = steamDesktop && Boolean(playOf(e.target));
      if ((action === "confirm" && e.key === "Enter" && !unseen && !ticks && !plays) || (action === "back" && e.key === "Escape")) return;
      e.preventDefault();
      act(action);
    },
    true,
  );

  // A mouse's side buttons: as bound, and never the webview's own back and forward.
  for (const type of ["mousedown", "mouseup"] as const)
    window.addEventListener(
      type,
      (e) => {
        const button = MOUSE_BUTTONS[e.button];
        if (!button) return;
        e.preventDefault();
        if (type === "mousedown") return;
        if (capture?.kind === "keys") return finish(button);
        const action = ACTIONS.find((a) => keys[a].includes(button));
        if (action) act(action);
      },
      true,
    );

  // A wheel or a finger takes over from an eased scroll.
  for (const type of ["wheel", "touchstart"] as const) window.addEventListener(type, () => stopEasing(), { capture: true, passive: true });

  // A tap on a text field on a touch screen (a Steam Deck's) edits it.
  window.addEventListener("pointerdown", (e) => {
    if (e.pointerType === "touch" && takesText(e.target)) edit(e.target);
  });
  window.addEventListener("pointerdown", () => ((pointing = true), markSide(null), queueHalo()), true);
  // Only a real move, not one the browser makes up as the page scrolls under a resting mouse.
  window.addEventListener("pointermove", (e) => (e.movementX || e.movementY) && navigating(false), { passive: true });
  window.addEventListener("wheel", () => navigating(false), { passive: true, capture: true });
  // Back in front (from minimised, or another window) as a controller or the keys left it: the focus's ring
  // shows again, which the browser doesn't always put back. Focus put on what has it does nothing, so it's
  // let go and put back; not from a text field (Steam's keyboard would go) or a menu (it'd close), which
  // show their focus without the ring.
  window.addEventListener("focus", () => {
    const at = document.activeElement;
    if (pointing || !(at instanceof HTMLElement) || at === document.body || at.matches(":focus-visible")) return;
    if (takesText(at) || at.closest(".menu, [role='menu']")) return;
    at.blur();
    show(at, true);
  });

  // Whether a Steam Deck's controls come as Steam's desktop layout's keys (gamepad.rs): listening before asking, as pad-hints.tsx does.
  const off = native.onControllerLabels((labels) => (steamDesktop = Boolean(labels?.desktop)));
  void off.then(() => native.controllerLabels()).then((labels) => (steamDesktop = Boolean(labels?.desktop)));

  // Controllers come from the Rust side ("pad" events): act on press, repeat directions while held.
  const repeats = new Map<string, ReturnType<typeof setTimeout>>();
  void listen<{ key: string; down: boolean }>("pad", ({ payload: { key, down } }) => {
    clearTimeout(repeats.get(key));
    repeats.delete(key);
    const action = ACTIONS.find((a) => pad[a].includes(key));
    const scroll = action === "scrollUp" ? -1 : action === "scrollDown" ? 1 : 0;
    if (!down) return void (scroll && glide(0));
    if (capture?.kind === "pad") return finish(key);
    if (!padOn || !document.hasFocus()) return; // A game is running in front.
    if (!action) return;
    if (scroll) return glide(scroll);
    act(action);
    if (action === "confirm" || action === "back" || action === "options" || action === "top" || action === "prevTab" || action === "nextTab") return;
    const again = (delay: number) => repeats.set(key, setTimeout(() => (act(action), again(120)), delay));
    again(300);
  });
}
