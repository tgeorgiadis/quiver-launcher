/**
 * How the library shows its apps on this computer: a grid, or a sidebar of
 * names beside the open app's page; which picture each card has,
 * how big the cards are (a slider), and whether names show under them. Kept in
 * settings.json with the other preferences for this device.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import type { Entry } from "@quiverlauncher/api";
import { CARD_DETAILS, coverOf, type CardDetail } from "@quiverlauncher/ui";

/** Sidebar: the library as a list down the left, the app picked in it open beside it (wide windows only). */
export type Layout = "grid" | "sidebar";
/** Cover: the wide library art, as the catalog shows it. Box art: the portrait capsule. Icon: the app's icon on a plain tile. */
export type CardImage = "cover" | "box" | "icon";
/** The size the cards' finer points go by (app.css's size-*): the slider's scale, in three steps. */
export type CardSize = "small" | "medium" | "large";
/** Which name is the large one on a card: the game's, or the app's (the port's). */
export type NameOrder = "game" | "app";
export type LibraryView = {
  layout: Layout;
  image: CardImage;
  /** How big the cards are, from the slider: their width over a Medium card's. */
  cardScale: number;
  /** The step of it the cards' finer points go by (sizeOf), and what older launchers read. */
  size: CardSize;
  names: boolean;
  nameOrder: NameOrder;
  /** The sidebar shows the other name, the app's or the game's, under each one. */
  bothNames: boolean;
  /** The grid shows the app played last at the top. */
  continuePlaying: boolean;
  /** The grid's cards show their copy's mark by the name (Get's symbol not installed, a cloud for an update). */
  marks: boolean;
  /** The grid's cards for apps not installed here have their picture greyed. */
  greyUninstalled: boolean;
  /** The grid has a card for each game, its ports picked from it (group.ts), not one for each app. */
  byGame: boolean;
  /** What the grid's cards show with their names, as Browse's can (browse-cards.ts): the line under the name, at first. */
  details: readonly CardDetail[];
};

/** The details a library card can show: Browse's, but New and Easy setup, which are about getting an app. */
export const LIBRARY_DETAILS = CARD_DETAILS.filter((d) => d !== "new" && d !== "setup");
/**
 * The same choice of details as one list each time it's read, so the cards (memo) aren't all drawn again with each
 * change to the launcher for a list that's equal but new.
 */
const detailLists = new Map<string, readonly CardDetail[]>();
function detailsOf(saved: unknown): readonly CardDetail[] {
  if (!Array.isArray(saved)) return DEFAULT_VIEW.details;
  const details = LIBRARY_DETAILS.filter((d) => saved.includes(d));
  const key = details.join(" ");
  if (!detailLists.has(key)) detailLists.set(key, details);
  return detailLists.get(key)!;
}
const sameDetails = (a: readonly CardDetail[], b: readonly CardDetail[]) => a.length === b.length && a.every((d) => b.includes(d));

/**
 * A shelf of box art, medium, with names: unlike Browse's wide catalog cards.
 * A card for each app, as a new player added them; one for each game is the View menu's.
 */
export const DEFAULT_VIEW: LibraryView = { layout: "grid", image: "box", cardScale: 1, size: "medium", names: true, nameOrder: "game", bothNames: true, continuePlaying: true, marks: true, greyUninstalled: true, byGame: false, details: ["subtitle"] };
/** TV mode's look (tvModeOn), over the player's own view, which stays saved for when it's left. */
export const TV_VIEW: Pick<LibraryView, "layout" | "image" | "cardScale" | "size"> = { layout: "grid", image: "box", cardScale: 1.3, size: "large" };

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

/** The card size slider's ends and step: from about half a Medium card's width to over half as wide again. */
export const CARD_SCALE = { min: 0.6, max: 1.6, step: 0.05 } as const;
/** Where each of the sizes before the slider sits on it. */
const SIZE_SCALES: Record<CardSize, number> = { small: 0.8, medium: 1, large: 1.3 };
/** A scale on the slider, its ends at most and on a step. */
export function cardScaleOf(value: unknown, fallback = 1): number {
  if (typeof value !== "number" || !Number.isFinite(value)) return fallback;
  const clamped = Math.min(CARD_SCALE.max, Math.max(CARD_SCALE.min, value));
  return Number((Math.round(clamped / CARD_SCALE.step) * CARD_SCALE.step).toFixed(2));
}
/** The step a scale is in, for what goes by Small, Medium and Large: smaller titles, fewer facts on a tile. */
export const sizeOf = (scale: number): CardSize => (scale < 0.9 ? "small" : scale >= 1.15 ? "large" : "medium");

/**
 * The saved choices over the defaults; anything unknown (an older or newer
 * launcher's) is the default. The List layout is gone: a library saved as one
 * is a grid, with the names a list always showed.
 */
export function viewOf(saved?: Partial<LibraryView>): LibraryView {
  const list = (saved?.layout as string | undefined) === "list";
  const pick = <T,>(options: [T, MessageDescriptor][], value: unknown, fallback: T) => (options.some(([o]) => o === value) ? (value as T) : fallback);
  const scale = cardScaleOf(saved?.cardScale, SIZE_SCALES[pick(CARD_SIZES, saved?.size, DEFAULT_VIEW.size)]);
  return {
    layout: pick(LAYOUTS, saved?.layout, DEFAULT_VIEW.layout),
    image: pick(CARD_IMAGES, saved?.image, DEFAULT_VIEW.image),
    // A library saved before the slider keeps the size it had.
    cardScale: scale,
    size: sizeOf(scale),
    names: !list && typeof saved?.names === "boolean" ? saved.names : DEFAULT_VIEW.names,
    nameOrder: pick(NAME_ORDERS, saved?.nameOrder, DEFAULT_VIEW.nameOrder),
    bothNames: typeof saved?.bothNames === "boolean" ? saved.bothNames : DEFAULT_VIEW.bothNames,
    continuePlaying: typeof saved?.continuePlaying === "boolean" ? saved.continuePlaying : DEFAULT_VIEW.continuePlaying,
    marks: typeof saved?.marks === "boolean" ? saved.marks : DEFAULT_VIEW.marks,
    greyUninstalled: typeof saved?.greyUninstalled === "boolean" ? saved.greyUninstalled : DEFAULT_VIEW.greyUninstalled,
    byGame: typeof saved?.byGame === "boolean" ? saved.byGame : DEFAULT_VIEW.byGame,
    details: detailsOf(saved?.details),
  };
}

/** Only what differs from the defaults is saved; the details in their own order. */
export function savedView(view: LibraryView): Partial<LibraryView> | undefined {
  // The step goes with the scale, so an older launcher shows about the same size.
  const { details, ...rest } = { ...view, cardScale: cardScaleOf(view.cardScale), size: sizeOf(cardScaleOf(view.cardScale)) };
  const changed: Partial<LibraryView> = Object.fromEntries(Object.entries(rest).filter(([k, v]) => DEFAULT_VIEW[k as keyof LibraryView] !== v));
  if (!sameDetails(details, DEFAULT_VIEW.details)) changed.details = LIBRARY_DETAILS.filter((d) => details.includes(d));
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

/**
 * What the library's filters ask of its apps; settings.json keeps them (`libraryFilters`). `managed`: Quiver's, which it
 * downloads and updates (from the catalog or a repository), or the player's, added from this computer, as 3.x's manually
 * managed apps were.
 */
export type LibraryFilters = {
  /** Apps for any of these consoles: ids, or "maker:Nintendo". */
  consoles?: string[];
  /** Apps of any of these kinds. */
  projectTypes?: string[];
  ai?: "no-generated" | "no-ai";
  tag?: string;
  installed?: "yes" | "no";
  managed?: "quiver" | "manual";
};

/**
 * The library's filters as they were saved, each only when it's one the library has. `tab` is the saved tab: Installed
 * and Not installed were tabs as well as a filter, and a library left on one opens with the filter on instead. Before
 * consoles and kinds could be picked several at a time, each was kept as one (`console`, `projectType`).
 */
export function keptFilters(saved: Record<string, string | string[]> = {}, tab?: string): LibraryFilters {
  const one = (v: unknown) => (typeof v === "string" && v ? v : undefined);
  const many = (now: unknown, was: unknown) => {
    const list = (Array.isArray(now) ? now : [was]).filter((v): v is string => typeof v === "string" && Boolean(v));
    return list.length ? [...new Set(list)] : undefined;
  };
  const { ai, tag, managed } = saved;
  const installed = tab === "installed" ? "yes" : tab === "not-installed" ? "no" : saved.installed;
  return Object.fromEntries(
    Object.entries({
      consoles: many(saved.consoles, saved.console),
      projectTypes: many(saved.projectTypes, saved.projectType),
      tag: one(tag),
      ai: ai === "no-generated" || ai === "no-ai" ? ai : undefined,
      installed: installed === "yes" || installed === "no" ? installed : undefined,
      managed: managed === "quiver" || managed === "manual" ? managed : undefined,
    }).filter(([, value]) => value),
  );
}

/** A tab the library opens at next time (`libraryTab`): not Updates or Hidden, which come and go, nor a playlist since removed. */
export const keepsTab = (tab: string | undefined, playlists: { key: string; removed?: boolean }[]) => playlists.some((p) => p.key === tab && !p.removed);
