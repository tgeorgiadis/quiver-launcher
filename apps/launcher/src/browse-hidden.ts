/**
 * Apps the player hid from Browse (settings.browseHidden): out of its grid,
 * its front page and More like this, until they're shown again. Apart from
 * the library's own hidden apps, which are in it and only tucked away.
 */

/** `hidden` with the app hidden, or shown again if it was. */
export const toggleHidden = (hidden: readonly string[] | undefined, id: string): string[] =>
  hidden?.includes(id) ? hidden.filter((h) => h !== id) : [...(hidden ?? []), id];

/** How Browse treats hidden apps: out of it (the default), in it with the rest, or alone. */
export type HiddenView = "hide" | "show" | "only";

/**
 * The apps Browse shows of `entries`. `owned`: the apps in the library when
 * their page came, out of it unless `showOwned`. `hidden`: the apps the
 * player hid, as `view` says.
 */
export function browseShown<T extends { id: string }>(
  entries: T[],
  { owned, showOwned, hidden, view }: { owned: ReadonlySet<string>; showOwned: boolean; hidden: ReadonlySet<string>; view: HiddenView },
): T[] {
  // Only hidden apps: all of them, those in the library too, so none of them seems lost.
  if (view === "only") return entries.filter((e) => hidden.has(e.id));
  return entries.filter((e) => (showOwned || !owned.has(e.id)) && (view === "show" || !hidden.has(e.id)));
}
