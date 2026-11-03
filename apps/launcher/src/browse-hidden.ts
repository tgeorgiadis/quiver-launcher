/**
 * Apps the player hid from Browse (settings.browseHidden): out of its grid,
 * its front page and More like this, until they're shown again. Apart from
 * the library's own hidden apps, which are in it and only tucked away.
 * Each by its slug too, to ask the site for them alone in Only hidden apps.
 */
import type { AppQuery, Entry } from "@quiverlauncher/api";

export type HiddenApp = { id: string; slug: string };

/** `hidden` with the app hidden, or shown again if it was. */
export const toggleHidden = (hidden: readonly HiddenApp[] | undefined, { id, slug }: HiddenApp): HiddenApp[] =>
  hidden?.some((h) => h.id === id) ? hidden.filter((h) => h.id !== id) : [...(hidden ?? []), { id, slug }];

const words = (text: string) => text.toLowerCase().split(/[^\p{L}\p{N}]+/u).filter(Boolean);

/** Only hidden apps' own search and filters, there being no catalog page to ask: as the site's, every typed word starts a word of the name. */
export function hiddenMatches(entry: Entry, search: string, { projectTypes, console, ai }: Pick<AppQuery, "projectTypes" | "console" | "ai">) {
  const name = words(entry.projectName);
  return (
    words(search).every((typed) => name.some((word) => word.startsWith(typed))) &&
    (!projectTypes?.length || projectTypes.includes(entry.projectType)) &&
    (!console || Boolean(entry.consoles?.includes(console))) &&
    (ai === "no-ai" ? !entry.aiLevel || entry.aiLevel === "none" : ai !== "no-generated" || entry.aiLevel !== "generated")
  );
}

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
