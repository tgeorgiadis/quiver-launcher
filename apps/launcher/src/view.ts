/**
 * How the library shows its apps on this computer: a grid, or a sidebar of
 * names beside the open app's page; which picture each card has,
 * how big the cards are, and whether names show under them. Kept in
 * settings.json with the other preferences for this device.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import type { Entry } from "@quiverlauncher/api";
import { coverOf } from "@quiverlauncher/ui";

/** Sidebar: the library as a list down the left, the app picked in it open beside it (wide windows only). */
export type Layout = "grid" | "sidebar";
/** Cover: the wide library art, as the catalog shows it. Box art: the portrait capsule. Icon: the app's icon on a plain tile. */
export type CardImage = "cover" | "box" | "icon";
export type CardSize = "small" | "medium" | "large";
/** Which name is the large one on a card: the game's, or the app's (the port's). */
export type NameOrder = "game" | "app";
export type LibraryView = {
  layout: Layout;
  image: CardImage;
  size: CardSize;
  names: boolean;
  nameOrder: NameOrder;
  /** The sidebar shows the other name, the app's or the game's, under each one. */
  bothNames: boolean;
  /** The grid shows the app played last at the top. */
  continuePlaying: boolean;
  /** The grid's cards show their copy's mark by the name (Get's symbol not installed, a cloud for an update). */
  marks: boolean;
  /** The grid has a card for each game, its ports picked from it (group.ts), not one for each app. */
  byGame: boolean;
};

/**
 * A shelf of box art, medium, with names: unlike Browse's wide catalog cards.
 * A card for each app, as a new player added them; one for each game is the View menu's.
 */
export const DEFAULT_VIEW: LibraryView = { layout: "grid", image: "box", size: "medium", names: true, nameOrder: "game", bothNames: true, continuePlaying: true, marks: true, byGame: false };

// The choices' names put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const LAYOUTS: [Layout, MessageDescriptor][] = [
  ["grid", msg({ message: "Grid", comment: "Library layout" })],
  ["sidebar", msg({ message: "Sidebar", comment: "Library layout: a list of names down the left" })],
];
/** The narrowest window, in CSS pixels at the interface size, the sidebar fits beside an app's page in. */
export const SIDEBAR_MIN_WIDTH = 900;
export const CARD_IMAGES: [CardImage, MessageDescriptor][] = [
  ["cover", msg({ message: "Cover", comment: "Library card image: the wide library art" })],
  ["box", msg({ message: "Box art", comment: "Library card image: the portrait capsule" })],
  ["icon", msg({ message: "Icon", comment: "Library card image" })],
];
export const NAME_ORDERS: [NameOrder, MessageDescriptor][] = [
  ["game", msg({ message: "Game", comment: "Which name is the large one on a library card: the game's" })],
  ["app", msg({ message: "App", comment: "Which name is the large one on a library card: the app's (the port's)" })],
];
export const CARD_SIZES: [CardSize, MessageDescriptor][] = [
  ["small", msg({ message: "Small", comment: "Library card size" })],
  ["medium", msg({ message: "Medium", comment: "Library card size" })],
  ["large", msg({ message: "Large", comment: "Library card size" })],
];

/**
 * The saved choices over the defaults; anything unknown (an older or newer
 * launcher's) is the default. The List layout is gone: a library saved as one
 * is a grid, with the names a list always showed.
 */
export function viewOf(saved?: Partial<LibraryView>): LibraryView {
  const list = (saved?.layout as string | undefined) === "list";
  const pick = <T,>(options: [T, MessageDescriptor][], value: unknown, fallback: T) => (options.some(([o]) => o === value) ? (value as T) : fallback);
  return {
    layout: pick(LAYOUTS, saved?.layout, DEFAULT_VIEW.layout),
    image: pick(CARD_IMAGES, saved?.image, DEFAULT_VIEW.image),
    size: pick(CARD_SIZES, saved?.size, DEFAULT_VIEW.size),
    names: !list && typeof saved?.names === "boolean" ? saved.names : DEFAULT_VIEW.names,
    nameOrder: pick(NAME_ORDERS, saved?.nameOrder, DEFAULT_VIEW.nameOrder),
    bothNames: typeof saved?.bothNames === "boolean" ? saved.bothNames : DEFAULT_VIEW.bothNames,
    continuePlaying: typeof saved?.continuePlaying === "boolean" ? saved.continuePlaying : DEFAULT_VIEW.continuePlaying,
    marks: typeof saved?.marks === "boolean" ? saved.marks : DEFAULT_VIEW.marks,
    byGame: typeof saved?.byGame === "boolean" ? saved.byGame : DEFAULT_VIEW.byGame,
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
