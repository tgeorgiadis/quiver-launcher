/**
 * Arrow keys and controllers move focus to the nearest control in that
 * direction, across every screen, instead of per-screen focus code.
 * Controller: d-pad or left stick moves, A presses, B goes back (Escape).
 */
type Direction = "up" | "down" | "left" | "right";

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

const KEYS: Record<string, Direction> = { ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right" };

export function startSpatialNavigation() {
  window.addEventListener("keydown", (e) => {
    const direction = KEYS[e.key];
    const typing = e.target instanceof HTMLInputElement && e.target.type !== "checkbox";
    // Left and right stay inside a text field; up and down leave it.
    if (!direction || (typing && (direction === "left" || direction === "right"))) return;
    e.preventDefault();
    move(direction);
  });

  // Standard mapping: 0 = A, 1 = B, 12-15 = d-pad; axes 0/1 = left stick.
  const held = new Map<string, number>();
  const press = (key: string, down: boolean, act: () => void) => {
    const now = performance.now();
    const since = held.get(key);
    if (!down) return void held.delete(key);
    // Act on press, then repeat while held.
    if (since === undefined || now - since > 180) {
      held.set(key, since === undefined ? now + 220 : now);
      act();
    }
  };
  const poll = () => {
    for (const pad of navigator.getGamepads?.() ?? []) {
      if (!pad) continue;
      const b = (i: number) => Boolean(pad.buttons[i]?.pressed);
      const [x, y] = [pad.axes[0] ?? 0, pad.axes[1] ?? 0];
      press("up", b(12) || y < -0.5, () => move("up"));
      press("down", b(13) || y > 0.5, () => move("down"));
      press("left", b(14) || x < -0.5, () => move("left"));
      press("right", b(15) || x > 0.5, () => move("right"));
      press("a", b(0), () => (document.activeElement as HTMLElement | null)?.click());
      press("b", b(1), () => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" })));
    }
    requestAnimationFrame(poll);
  };
  window.addEventListener("gamepadconnected", () => requestAnimationFrame(poll), { once: true });
}
