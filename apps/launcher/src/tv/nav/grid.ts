/**
 * Focus in a grid of tiles (spec §8.2). Pure: the view owns the index and scrolling.
 * Left and Right go in reading order (changed 2026-10-10 at the player's request):
 * Left on a row's first tile goes to the last tile of the row above, Right on a
 * row's last tile to the first of the next, and they go round: Right on the very
 * last tile goes to the first, Left on the first to the last.
 */
import type { InputAction } from "../input/actions";

export type NavResult = { moved: true; index: number } | { moved: false; edge: InputAction };

export class GridNavigator {
  count = 0;
  /** From the viewport's width and the tile's. */
  columns = 1;
  index = 0;

  reset(count: number, columns: number, keepIndex = 0) {
    this.count = count;
    this.columns = Math.max(1, columns);
    this.index = Math.min(Math.max(0, keepIndex), Math.max(0, count - 1));
    return this;
  }

  move(action: InputAction): NavResult {
    const { count, columns } = this;
    const i = this.index;
    let next: number | null = null;
    if (count > 0) {
      if (action === "left" && count > 1) next = i > 0 ? i - 1 : count - 1;
      else if (action === "right" && count > 1) next = i + 1 < count ? i + 1 : 0;
      else if (action === "up" && i - columns >= 0) next = i - columns;
      else if (action === "down" && i + columns < count) next = i + columns;
      // Down into a shorter last row lands on its last tile.
      else if (action === "down" && Math.floor(i / columns) < Math.floor((count - 1) / columns)) next = count - 1;
    }
    if (next === null) return { moved: false, edge: action };
    this.index = next;
    return { moved: true, index: next };
  }
}
