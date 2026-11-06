/**
 * Whether Big Picture listens (spec §5.5): only while its window is in front
 * and no game is. Every change forgets what's held, so a button held as a
 * game exits doesn't keep repeating, and a release whose press was dropped is
 * ignored.
 */
import type { InputAction } from "./actions";
import type { RepeatFilter } from "./repeat";

export class InputGate {
  private active = true;
  private inGame = false;

  constructor(private readonly filter: RepeatFilter) {}

  get windowActive() {
    return this.active;
  }
  set windowActive(value: boolean) {
    if (value === this.active) return;
    this.active = value;
    this.filter.reset();
  }

  get gameInForeground() {
    return this.inGame;
  }
  set gameInForeground(value: boolean) {
    if (value === this.inGame) return;
    this.inGame = value;
    this.filter.reset();
  }

  get isOpen() {
    return this.active && !this.inGame;
  }

  input(source: string, action: InputAction, down: boolean) {
    if (this.isOpen) this.filter.input(source, action, down);
  }
}
