/**
 * Selecting many library apps at once (library-ticking.tsx): which are ticked, a range of them as the library shows
 * them, and how each change made to them all at once reads and writes what one app's menu changes for one.
 */

/** Of the apps ticked, how many have something: all, some (a half-ticked box) or none. */
export type TickState = "all" | "some" | "none";

export function tickState(ids: readonly string[], has: (id: string) => boolean): TickState {
  const count = ids.filter(has).length;
  return count === 0 ? "none" : count === ids.length ? "all" : "some";
}

/**
 * A click with Shift: every app from the one clicked last (`anchor`) to this one, in the order the library shows them
 * (`order`), ticked with the rest. With no anchor still showing, just this one.
 */
export function tickRange(order: readonly string[], anchor: string | undefined, to: string, ticked: ReadonlySet<string>): Set<string> {
  const end = order.indexOf(to);
  const start = anchor === undefined ? -1 : order.indexOf(anchor);
  const next = new Set(ticked);
  if (end < 0) return next;
  if (start < 0) return next.add(to);
  for (const id of order.slice(Math.min(start, end), Math.max(start, end) + 1)) next.add(id);
  return next;
}

/** One card, or a game's card for all its ports (`ids`): ticked when all of them are; a click ticks them all, or unticks them all. */
export function tickToggled(ticked: ReadonlySet<string>, ids: readonly string[]): Set<string> {
  const next = new Set(ticked);
  const on = ids.every((id) => ticked.has(id));
  for (const id of ids) {
    if (on) next.delete(id);
    else next.add(id);
  }
  return next;
}

/** A list of app ids (a playlist's, the hidden ones) with `ids` put on it (`on`), or taken off it; the same list when nothing changes. */
export function withIds(list: readonly string[] | undefined, ids: readonly string[], on: boolean): string[] {
  const now = list ?? [];
  if (on) {
    const adding = ids.filter((id) => !now.includes(id));
    return adding.length ? [...now, ...adding] : [...now];
  }
  return now.filter((id) => !ids.includes(id));
}

/** An app's own tags with `tag` put on it (as the menu adds one: not twice in another case, 20 at most), or taken off it. */
export function withOwnTag(tags: readonly string[], tag: string, on: boolean): string[] {
  const same = (x: string) => x.toLowerCase() === tag.toLowerCase();
  if (!on) return tags.filter((x) => !same(x));
  return tags.some(same) || tags.length >= 20 ? [...tags] : [...tags, tag];
}
