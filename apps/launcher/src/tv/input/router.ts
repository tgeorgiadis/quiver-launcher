/**
 * Sends input to whatever is on top (spec §5.6): the on-screen keyboard, then
 * dialogs, then the overlay, then the tab's view. The hint bar shows the top
 * handler's hints.
 *
 * Destructive actions are held to confirm (spec §5.2): when the top handler
 * says an action needs holding, a press starts a hold, and the action fires
 * once it's been held that long. Letting go sooner does nothing.
 */
import type { InputAction } from "./actions";
import type { InputEvent } from "./repeat";

export type ButtonHint = { action: InputAction; label: string; hold?: boolean };

export type InputHandler = {
  /** A press, or a held direction repeating. */
  onAction(action: InputAction, repeat: boolean): void;
  /** An action that was held long enough (see holdFor). */
  onHold?(action: InputAction): void;
  /** How long this action must be held, as things are now; undefined for a plain press. */
  holdFor?(action: InputAction): number | undefined;
  hints(): readonly ButtonHint[];
  /**
   * Typing on a physical keyboard, while this handler is on top (the on-screen keyboard):
   * a character, "\b" for Backspace, "\n" for Enter. Those keys then aren't actions.
   */
  onText?(text: string): void;
};

export type Hold = { action: InputAction; since: number; duration: number };

/** Higher layers are always above lower ones, whatever order they were pushed in. */
export const LAYER = { view: 0, overlay: 1, dialog: 2, keyboard: 3 } as const;
export type Layer = (typeof LAYER)[keyof typeof LAYER];

export class InputRouter {
  private stack: InputHandler[] = [];
  private readonly layers = new Map<InputHandler, Layer>();
  private hold: (Hold & { timer: ReturnType<typeof setTimeout> }) | null = null;
  private readonly listeners = new Set<() => void>();
  private version = 0;

  constructor(private readonly now: () => number = () => Date.now()) {}

  /** Puts a handler on top of its layer (a view remounting under an open dialog stays under it); returns what takes it off again. */
  push(handler: InputHandler, layer: Layer = LAYER.view): () => void {
    const before = this.top;
    this.layers.set(handler, layer);
    const at = this.stack.findIndex((h) => this.layers.get(h)! > layer);
    this.stack = at < 0 ? [...this.stack, handler] : [...this.stack.slice(0, at), handler, ...this.stack.slice(at)];
    if (this.top !== before) this.cancelHold();
    this.changed();
    return () => {
      if (!this.stack.includes(handler)) return;
      if (this.top === handler) this.cancelHold();
      this.stack = this.stack.filter((h) => h !== handler);
      this.layers.delete(handler);
      this.changed();
    };
  }

  get top(): InputHandler | undefined {
    return this.stack.at(-1);
  }

  /** The layer of whatever is on top; undefined when nothing is. */
  get topLayer(): Layer | undefined {
    const top = this.top;
    return top && this.layers.get(top);
  }

  hints(): readonly ButtonHint[] {
    return this.top?.hints() ?? [];
  }

  /** The hold in progress, for the glyph's fill. */
  get holding(): Hold | null {
    return this.hold && { action: this.hold.action, since: this.hold.since, duration: this.hold.duration };
  }

  dispatch(event: InputEvent) {
    const handler = this.top;
    if (!handler) return;
    if (event.phase === "release") {
      if (this.hold?.action === event.action) this.cancelHold();
      return;
    }
    const duration = event.phase === "press" ? handler.holdFor?.(event.action) : undefined;
    if (duration !== undefined) return this.startHold(handler, event.action, duration);
    // A repeat of a held action never presses it.
    if (event.phase === "repeat" && this.hold?.action === event.action) return;
    handler.onAction(event.action, event.phase === "repeat");
  }

  /** The top handler's hints changed (its focus moved, say). */
  refresh() {
    this.changed();
  }

  cancelHold() {
    if (!this.hold) return;
    clearTimeout(this.hold.timer);
    this.hold = null;
    this.changed();
  }

  /** For useSyncExternalStore: a number that changes whenever hints or the hold do. */
  snapshot = () => this.version;

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => void this.listeners.delete(listener);
  };

  private startHold(handler: InputHandler, action: InputAction, duration: number) {
    this.cancelHold();
    const timer = setTimeout(() => {
      this.hold = null;
      this.changed();
      if (this.top === handler) handler.onHold?.(action);
    }, duration);
    this.hold = { action, since: this.now(), duration, timer };
    this.changed();
  }

  private changed() {
    this.version++;
    for (const listener of this.listeners) listener();
  }
}
