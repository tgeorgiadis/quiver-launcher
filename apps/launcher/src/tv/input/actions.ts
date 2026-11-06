/**
 * What a press means in Big Picture (spec §5.1), from the keyboard or a
 * controller. Big Picture's map is fixed; upstream's rebindable map is for
 * desktop mode.
 */

export type InputAction =
  | "up"
  | "down"
  | "left"
  | "right"
  /** A / Cross (south) */
  | "accept"
  /** B / Circle (east) */
  | "back"
  /** X / Square (west) */
  | "secondary"
  /** Y / Triangle (north) */
  | "details"
  /** LB / L1 */
  | "tabPrev"
  /** RB / R1 */
  | "tabNext"
  /** LT / L2 */
  | "pagePrev"
  /** RT / R2 */
  | "pageNext"
  /** View / Create */
  | "search"
  /** Menu / Options */
  | "menu";

export type ControllerFamily = "xbox" | "playstation" | "generic";

export const DIRECTIONS: ReadonlySet<InputAction> = new Set(["up", "down", "left", "right"]);
export const isDirection = (a: InputAction) => DIRECTIONS.has(a);

/** Keyboard (spec §5.1 and rule 8): every screen works without a controller. */
export function keyAction(e: Pick<KeyboardEvent, "key" | "shiftKey" | "ctrlKey" | "altKey" | "metaKey">): InputAction | null {
  if (e.ctrlKey || e.altKey || e.metaKey) return null;
  switch (e.key) {
    case "ArrowUp":
      return "up";
    case "ArrowDown":
      return "down";
    case "ArrowLeft":
      return "left";
    case "ArrowRight":
      return "right";
    case "Enter":
      return "accept";
    case "Escape":
    case "Backspace":
      return "back";
    case "Tab":
      return e.shiftKey ? "tabPrev" : "tabNext";
    case "PageUp":
      return "pagePrev";
    case "PageDown":
      return "pageNext";
    case "/":
      return "search";
    case "F10":
      return "menu";
  }
  switch (e.key.toLowerCase()) {
    case "x":
      return "secondary";
    case "y":
      return "details";
    case "q":
      return "tabPrev";
    case "e":
      return "tabNext";
  }
  return null;
}

/**
 * Controller controls as the Rust side names them (gilrs). gilrs calls the
 * bumpers LeftTrigger/RightTrigger and the triggers LeftTrigger2/RightTrigger2.
 * Mode (Guide / PS) is never mapped: Game Bar and Steam use it.
 */
const PAD: Record<string, InputAction> = {
  South: "accept",
  East: "back",
  West: "secondary",
  North: "details",
  LeftTrigger: "tabPrev",
  RightTrigger: "tabNext",
  LeftTrigger2: "pagePrev",
  RightTrigger2: "pageNext",
  Select: "search",
  Start: "menu",
  DPadUp: "up",
  DPadDown: "down",
  DPadLeft: "left",
  DPadRight: "right",
  LeftStickUp: "up",
  LeftStickDown: "down",
  LeftStickLeft: "left",
  LeftStickRight: "right",
};

export const padAction = (key: string): InputAction | null => PAD[key] ?? null;
