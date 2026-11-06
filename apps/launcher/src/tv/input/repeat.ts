/**
 * Turns presses and releases into actions (spec §5.4). Directions repeat while
 * held: after 380 ms, then every 90 ms, every 45 ms once held 1.5 s. Nothing
 * else repeats. A source is one physical control ("pad0:DPadUp", "key:ArrowUp");
 * two sources holding the same action press it once.
 */
import { isDirection, type InputAction } from "./actions";

export type InputPhase = "press" | "repeat" | "release";
export type InputEvent = { action: InputAction; phase: InputPhase };

export const REPEAT_DELAY = 380;
export const REPEAT_EVERY = 90;
export const REPEAT_FAST = 45;
export const FAST_AFTER = 1500;

export class RepeatFilter {
  /** Which sources hold each action. */
  private readonly holders = new Map<InputAction, Set<string>>();
  private readonly timers = new Map<InputAction, ReturnType<typeof setTimeout>>();

  constructor(
    private readonly emit: (event: InputEvent) => void,
    private readonly now: () => number = () => Date.now(),
  ) {}

  input(source: string, action: InputAction, down: boolean) {
    const holders = this.holders.get(action) ?? new Set<string>();
    if (down) {
      if (holders.has(source)) return;
      holders.add(source);
      this.holders.set(action, holders);
      if (holders.size > 1) return;
      this.emit({ action, phase: "press" });
      if (isDirection(action)) this.schedule(action, this.now(), REPEAT_DELAY);
    } else {
      if (!holders.delete(source) || holders.size) return;
      this.holders.delete(action);
      this.stop(action);
      this.emit({ action, phase: "release" });
    }
  }

  /** Forgets everything held, without releases: after a game, a still-held button must not carry on. */
  reset() {
    for (const action of this.timers.keys()) this.stop(action);
    this.holders.clear();
  }

  isHeld(action: InputAction) {
    return this.holders.has(action);
  }

  private schedule(action: InputAction, since: number, delay: number) {
    this.timers.set(
      action,
      setTimeout(() => {
        this.emit({ action, phase: "repeat" });
        this.schedule(action, since, this.now() - since >= FAST_AFTER ? REPEAT_FAST : REPEAT_EVERY);
      }, delay),
    );
  }

  private stop(action: InputAction) {
    clearTimeout(this.timers.get(action));
    this.timers.delete(action);
  }
}
