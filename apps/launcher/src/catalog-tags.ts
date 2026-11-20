/**
 * The catalog's tags the player would rather not see: hidden everywhere, on one app only, or as the catalog adds
 * them. A hidden tag is off the app wherever its tags are read (its card, the library's tag filter, search and the
 * playlists that follow a tag); the catalog still has it, and showing it again brings it back. A tag that names a
 * console is never hidden: an app's console is read from it.
 */

/** Kept in this computer's settings. Tags are matched whatever their case. */
export type HiddenTags = {
  /** Hidden on every app. */
  everywhere?: string[];
  /** Tags the catalog adds from now on are hidden too: all but those in `shown`. */
  hideNew?: boolean;
  /** With `hideNew`, the tags that show: those there when it was turned on, and any the player showed since. */
  shown?: string[];
  /** Hidden on one app, by its id. */
  apps?: Record<string, string[]>;
};

const has = (list: readonly string[] | undefined, tag: string) => Boolean(list?.some((t) => t.toLowerCase() === tag.toLowerCase()));
const without = (list: readonly string[] | undefined, tag: string) => (list ?? []).filter((t) => t.toLowerCase() !== tag.toLowerCase());
const withOne = (list: readonly string[] | undefined, tag: string) => (has(list, tag) ? [...(list ?? [])] : [...(list ?? []), tag]);

/** Whether a tag is hidden on every app. */
export const hiddenEverywhere = (h: HiddenTags | undefined, tag: string) => Boolean(h && (has(h.everywhere, tag) || (h.hideNew && !has(h.shown, tag))));

/** Whether a tag is hidden on this app alone. */
export const hiddenOnApp = (h: HiddenTags | undefined, id: string, tag: string) => has(h?.apps?.[id], tag);

/** An app's catalog tags less the hidden ones; the same list when none is. */
export function shownTags(h: HiddenTags | undefined, id: string, tags: readonly string[], isConsole: (tag: string) => boolean = () => false): readonly string[] {
  if (!h || !(h.everywhere?.length || h.hideNew || h.apps?.[id]?.length)) return tags;
  const shown = tags.filter((tag) => isConsole(tag) || !(hiddenEverywhere(h, tag) || hiddenOnApp(h, id, tag)));
  return shown.length === tags.length ? tags : shown;
}

/**
 * The catalog's tags on an app that can be unticked on it: all it has (`allTags`, when some are hidden) but a
 * console's, and those hidden on every app, which Settings shows again.
 */
export function hideableTags(entry: { tags: readonly string[]; allTags?: readonly string[] } | undefined, h: HiddenTags | undefined, consoleNames: Record<string, string> = {}): string[] {
  return (entry?.allTags ?? entry?.tags ?? []).filter((tag) => !Object.hasOwn(consoleNames, tag) && !hiddenEverywhere(h, tag));
}

/** Hides a tag on every app, or shows it again. */
export function hideEverywhere(h: HiddenTags = {}, tag: string, hide: boolean): HiddenTags {
  const everywhere = hide ? withOne(h.everywhere, tag) : without(h.everywhere, tag);
  return { ...h, everywhere, shown: h.hideNew ? (hide ? without(h.shown, tag) : withOne(h.shown, tag)) : undefined };
}

/** Hides every one of these tags on every app, or shows them all again. */
export const hideAllEverywhere = (h: HiddenTags = {}, tags: readonly string[], hide: boolean): HiddenTags => tags.reduce((next, tag) => hideEverywhere(next, tag, hide), h);

/**
 * Tags the catalog adds later are hidden too, or not. Turned on, the tags known now and not hidden keep showing;
 * turned off, only the ones the player hid stay hidden.
 */
export function hideNewTags(h: HiddenTags = {}, on: boolean, known: readonly string[]): HiddenTags {
  if (!on) return { ...h, hideNew: undefined, shown: undefined };
  return { ...h, hideNew: true, shown: known.filter((tag, i) => !has(h.everywhere, tag) && known.findIndex((k) => k.toLowerCase() === tag.toLowerCase()) === i) };
}

/** Hides a tag on one app, or shows it there again. */
export function hideOnApp(h: HiddenTags = {}, id: string, tag: string, hide: boolean): HiddenTags {
  const list = hide ? withOne(h.apps?.[id], tag) : without(h.apps?.[id], tag);
  const { [id]: _, ...others } = h.apps ?? {};
  return { ...h, apps: list.length ? { ...others, [id]: list } : others };
}

/** The catalog's tags on these apps a player can hide, each once as first written, with how many apps have it; by name. */
export function catalogTagCounts(apps: readonly { tags: readonly string[] }[], isConsole: (tag: string) => boolean = () => false, also: readonly string[] = []): { tag: string; apps: number }[] {
  const counts = new Map<string, { tag: string; apps: number }>();
  for (const app of apps)
    for (const tag of new Set(app.tags)) {
      if (isConsole(tag)) continue;
      const had = counts.get(tag.toLowerCase());
      counts.set(tag.toLowerCase(), { tag: had?.tag ?? tag, apps: (had?.apps ?? 0) + 1 });
    }
  for (const tag of also) if (!isConsole(tag) && !counts.has(tag.toLowerCase())) counts.set(tag.toLowerCase(), { tag, apps: 0 });
  return [...counts.values()].sort((a, b) => a.tag.localeCompare(b.tag, undefined, { sensitivity: "base" }));
}

/** Which of the catalog's tags the manager lists: all of them, only those showing, or only those hidden on every app. */
export type TagFilter = "all" | "shown" | "hidden";
/** By name, or the ones on the most apps first. */
export type TagSort = "name" | "apps";

/**
 * The catalog's tags the manager lists: those whose name (as shown, `label`) has what's searched for, in what the filter
 * keeps, in the order picked. By name, they keep the order they came in.
 */
export function listedTags<T extends { tag: string; apps: number }>(
  tags: readonly T[],
  h: HiddenTags | undefined,
  { search = "", filter = "all", sort = "name", label = (tag: string) => tag }: { search?: string; filter?: TagFilter; sort?: TagSort; label?: (tag: string) => string } = {},
): T[] {
  const words = search.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const listed = tags.filter(({ tag }) => {
    if (filter !== "all" && hiddenEverywhere(h, tag) !== (filter === "hidden")) return false;
    const name = `${label(tag)} ${tag}`.toLowerCase();
    return words.every((w) => name.includes(w));
  });
  return sort === "apps" ? listed.sort((a, b) => b.apps - a.apps) : listed;
}
