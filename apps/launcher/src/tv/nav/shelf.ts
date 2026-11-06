/**
 * Focus on Home's shelves (spec §8.2): a row and a column, and the column each
 * row was last left at, so going up and down comes back to the same tile.
 * Nothing wraps; Left at the start of a row is an edge. Pure.
 */
import type { InputAction } from "../input/actions";
import type { NavResult } from "./grid";

export type ShelfPos = { row: number; col: number };
export type ShelfResult = { moved: true; pos: ShelfPos } | Extract<NavResult, { moved: false }>;

export class ShelfNavigator {
  row = 0;
  col = 0;
  private lengths: number[] = [];
  private remembered: number[] = [];

  /** The rows' lengths (empty rows are skipped over); keeps the position, clamped. */
  reset(lengths: readonly number[], pos: ShelfPos = { row: this.row, col: this.col }, remembered: readonly number[] = this.remembered) {
    this.lengths = [...lengths];
    this.remembered = lengths.map((_, i) => remembered[i] ?? 0);
    this.row = this.clampRow(pos.row);
    this.col = this.clampCol(this.row, pos.col);
    this.remembered[this.row] = this.col;
    return this;
  }

  get memory(): readonly number[] {
    return this.remembered;
  }

  move(action: InputAction): ShelfResult {
    const length = this.lengths[this.row] ?? 0;
    if (!length) return { moved: false, edge: action };
    if (action === "left" && this.col > 0) return this.go(this.row, this.col - 1);
    if (action === "right" && this.col < length - 1) return this.go(this.row, this.col + 1);
    if (action === "up" || action === "down") {
      const step = action === "up" ? -1 : 1;
      for (let r = this.row + step; r >= 0 && r < this.lengths.length; r += step) {
        if (this.lengths[r]) return this.go(r, this.clampCol(r, this.remembered[r]));
      }
    }
    return { moved: false, edge: action };
  }

  private go(row: number, col: number): ShelfResult {
    this.row = row;
    this.col = col;
    this.remembered[row] = col;
    return { moved: true, pos: { row, col } };
  }

  private clampRow(row: number) {
    if (this.lengths[row]) return row;
    // The row went away (its last game was played elsewhere, or updated): the nearest one with tiles.
    for (let d = 1; d < this.lengths.length; d++) {
      if (this.lengths[row - d]) return row - d;
      if (this.lengths[row + d]) return row + d;
    }
    return 0;
  }

  private clampCol(row: number, col: number) {
    return Math.min(Math.max(0, col), Math.max(0, (this.lengths[row] ?? 0) - 1));
  }
}
