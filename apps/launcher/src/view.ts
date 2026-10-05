/**
 * How the library shows its apps on this computer: a grid or a list, which
 * picture each card has, how big the cards are, and whether names show under
 * them. Kept in settings.json with the other preferences for this device.
 */
import type { Entry } from "@quiverlauncher/api";
import { coverOf } from "@quiverlauncher/ui";

export type Layout = "grid" | "list";
/** Cover: the wide library art, as the catalog shows it. Box art: the portrait capsule. Icon: the app's icon on a plain tile. */
export type CardImage = "cover" | "box" | "icon";
export type CardSize = "small" | "medium" | "large";
export type LibraryView = { layout: Layout; image: CardImage; size: CardSize; names: boolean };

/** A shelf of box art, medium, with names: unlike Browse's wide catalog cards. */
export const DEFAULT_VIEW: LibraryView = { layout: "grid", image: "box", size: "medium", names: true };

export const LAYOUTS: [Layout, string][] = [
  ["grid", "Grid"],
  ["list", "List"],
];
export const CARD_IMAGES: [CardImage, string][] = [
  ["cover", "Cover"],
  ["box", "Box art"],
  ["icon", "Icon"],
];
export const CARD_SIZES: [CardSize, string][] = [
  ["small", "Small"],
  ["medium", "Medium"],
  ["large", "Large"],
];

/** The saved choices over the defaults; anything unknown (an older or newer launcher's) is the default. */
export function viewOf(saved?: Partial<LibraryView>): LibraryView {
  const pick = <T,>(options: [T, string][], value: unknown, fallback: T) => (options.some(([o]) => o === value) ? (value as T) : fallback);
  return {
    layout: pick(LAYOUTS, saved?.layout, DEFAULT_VIEW.layout),
    image: pick(CARD_IMAGES, saved?.image, DEFAULT_VIEW.image),
    size: pick(CARD_SIZES, saved?.size, DEFAULT_VIEW.size),
    names: typeof saved?.names === "boolean" ? saved.names : DEFAULT_VIEW.names,
  };
}

/** Only what differs from the defaults is saved. */
export function savedView(view: LibraryView): Partial<LibraryView> | undefined {
  const changed = Object.fromEntries(Object.entries(view).filter(([k, v]) => DEFAULT_VIEW[k as keyof LibraryView] !== v));
  return Object.keys(changed).length ? changed : undefined;
}

/**
 * How a picture sits on a card: `fill` covers the frame, `icon` sits on a
 * plain tile, `letterbox` shows a picture of another shape whole, over a
 * blurred copy of itself.
 */
export type Fit = "fill" | "icon" | "letterbox";
export type Art = { src: string; fit: Fit };

/**
 * The pictures to try for a card, best first; the next is shown when one
 * doesn't load. The catalog already gives an app its game's art for any slot
 * the app has none of (box art and icon included), and apps the player added
 * have their matched game's, so no game is looked up here.
 */
export function artFor(entry: Pick<Entry, "libraryArt" | "artwork">, image: Exclude<CardImage, "cover">): Art[] {
  const { header, capsule } = entry.libraryArt ?? {};
  const icon = entry.artwork;
  const tries: [string | undefined, Fit][] =
    image === "box"
      ? // Box art, else the cover whole, else the icon.
        [[capsule, "fill"], [header, "letterbox"], [icon, "icon"]]
      : // The icon, else the cover whole.
        [[icon, "icon"], [coverOf({ libraryArt: entry.libraryArt }), "letterbox"]];
  const list = tries.flatMap(([src, fit]) => (src ? [{ src, fit }] : []));
  // The same picture once.
  return list.filter((a, i) => list.findIndex((b) => b.src === a.src) === i);
}
