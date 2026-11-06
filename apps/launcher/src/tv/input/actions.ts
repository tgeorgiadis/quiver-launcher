/**
 * What a press means in TV mode, from the keyboard or a controller. Presses go through the player's bindings
 * (Settings → Controls, spatial.ts) as in the rest of the launcher: Confirm, Back, Quick action, Options, the tabs and
 * the letters keep their buttons here, and the letters' (LT and RT by default) step between the library, Discover and
 * Downloads. TV mode's own controls (Search, Menu, Tab) come on top, where the player hasn't bound them to anything.
 */
import { combo, type Action, type Bindings } from "../../spatial";

export type InputAction =
  | "up"
  | "down"
  | "left"
  | "right"
  /** Confirm: A / Cross (south) */
  | "accept"
  /** Back: B / Circle (east) */
  | "back"
  /** Quick action: X / Square (west) */
  | "secondary"
  /** Options: Y / Triangle (north) */
  | "details"
  /** Previous tab: LB / L1 */
  | "tabPrev"
  /** Next tab: RB / R1 */
  | "tabNext"
  /** Previous letter: LT / L2 */
  | "pagePrev"
  /** Next letter: RT / R2 */
  | "pageNext"
  /** View / Create */
  | "search"
  /** Menu / Options */
  | "menu";

export type ControllerFamily = "xbox" | "playstation" | "nintendo" | "steam";

export const DIRECTIONS: ReadonlySet<InputAction> = new Set(["up", "down", "left", "right"]);
export const isDirection = (a: InputAction) => DIRECTIONS.has(a);

/** The launcher's actions TV mode answers to, and what each is here. Forward, Back to top and Scroll aren't. */
export const FROM_BINDINGS: Partial<Record<Action, InputAction>> = {
  confirm: "accept",
  back: "back",
  quick: "secondary",
  options: "details",
  prevTab: "tabPrev",
  nextTab: "tabNext",
  prevLetter: "pagePrev",
  nextLetter: "pageNext",
  up: "up",
  down: "down",
  left: "left",
  right: "right",
};
const ACTION_OF = Object.fromEntries(Object.entries(FROM_BINDINGS).map(([a, tv]) => [tv, a])) as Partial<Record<InputAction, Action>>;

/** TV mode's own controls on a controller: View for Search, Menu for the menu. Mode (Guide / PS) never: Game Bar and Steam use it. */
const PAD_EXTRAS: Record<string, InputAction> = { Select: "search", Start: "menu" };
/** And on a keyboard: every screen works without a controller. */
const KEY_EXTRAS: Record<string, InputAction> = {
  Q: "tabPrev",
  E: "tabNext",
  Tab: "tabNext",
  "Shift+Tab": "tabPrev",
  Backspace: "back",
  PageUp: "pagePrev",
  PageDown: "pageNext",
  "/": "search",
  F10: "menu",
  X: "secondary",
  Y: "details",
};

/** A binding's TV action; one TV mode doesn't do has its own control's, where only scrolling or nothing binds it. */
function bound(bindings: Bindings, binding: string, extras: Record<string, InputAction>): InputAction | null {
  const actions = (Object.keys(bindings) as Action[]).filter((a) => bindings[a].includes(binding));
  for (const a of actions) if (FROM_BINDINGS[a]) return FROM_BINDINGS[a];
  // Page Up scrolls on the desktop: here it steps along, as it always has.
  const scrolls = actions.every((a) => a === "scrollUp" || a === "scrollDown");
  return scrolls ? (extras[binding] ?? null) : null;
}

export const keyAction = (e: KeyboardEvent, keys: Bindings): InputAction | null => bound(keys, combo(e), KEY_EXTRAS);

/** Controller controls as the Rust side names them (gamepad.rs, gilrs's names): bumpers LeftTrigger, triggers LeftTrigger2. */
export const padAction = (key: string, pad: Bindings): InputAction | null => bound(pad, key, PAD_EXTRAS);

/**
 * The control a hint shows for an action: what the player bound it to, else TV mode's own. On a keyboard, a key
 * without Ctrl or Alt first, TV mode's own before a combination, being shorter to read.
 */
export function controlFor(action: InputAction, bindings: Bindings, kind: "keys" | "pad"): string | undefined {
  const extras = kind === "keys" ? KEY_EXTRAS : PAD_EXTRAS;
  const own = Object.keys(extras).filter((b) => extras[b] === action && bound(bindings, b, extras) === action);
  const a = ACTION_OF[action];
  const mine = a ? bindings[a] : [];
  if (kind === "pad") return mine[0] ?? own[0];
  const plain = mine.filter((b) => !/^(Ctrl|Alt|Meta)\+/.test(b));
  return plain[0] ?? own.find((b) => !b.includes("+")) ?? mine[0] ?? own[0];
}
