/**
 * Browse's front page, above the whole catalog while the player isn't searching or filtering: a few featured apps,
 * then shelves of what players say runs well, what's just been updated, what's new, each console maker's and the
 * handhelds' (console-shelves.ts). Each shelf is one request to the catalog, for this computer's apps, and See all
 * shows the same in the grid, its consoles on the console filter.
 */
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { AppQuery, Client as ApiClient, Entry, Os, Sort } from "@quiverlauncher/api";
import { EntryCard, heroSources, logoSources, warmImage, type ImageSource } from "@quiverlauncher/ui";
import { ALL_APPS, FeaturedCarousel, MORE_LIKE_THIS, SEE_ALL, SHELF_SIZE, SHELF_TITLES, Shelf, featuredDay, featuredMix, likeThis, runsWell } from "@quiverlauncher/ui/discover";
import { availableOn, catalogOs, useLauncher } from "./store";
import { browseShown } from "./browse-hidden";
import { TickableCard } from "./browse-ticking";
import { useBrowseCards } from "./library-view";
import { consoleShelves, type ConsoleShelf } from "./console-shelves";
import { keepShelf, keptShelf, shownShelf } from "./browse-memory";
import { answer, appKey, keptAnswer } from "./app-memory";

export type ShelfKey = "rating" | "updated" | "added" | `consoles:${string}`;
/** A shelf's See all: its order, or its consoles as Browse's console filter takes them. */
type SeeAll = { sort: Sort } | { consoles: string[] };

/** A console shelf's request: its console filters, "maker:Nintendo" or ids, after the colon. */
const consolesKey = (shelf: ConsoleShelf): ShelfKey => `consoles:${shelf.consoles.join(",")}`;

/**
 * The catalog's shelves, for this computer: each one request (`consoles:maker:Nintendo` those consoles' best rated),
 * undefined until it comes and empty if it couldn't. Browse's front page and TV mode show them.
 */
export function useShelves(keys: readonly ShelfKey[]) {
  const { client, config, remember } = useLauncher();
  // Those Browse showed a moment ago, prefetchBrowse brought ahead, or the last ones before the launcher started, at
  // once (browse-memory.ts).
  const [shelves, setShelves] = useState<Partial<Record<ShelfKey, Entry[]>>>(() =>
    Object.fromEntries(keys.flatMap((key) => (shownShelf(shelfMemoryKey(key, config.os)) ? [[key, shownShelf(shelfMemoryKey(key, config.os))]] : []))),
  );
  useEffect(() => {
    let live = true;
    for (const key of keys) {
      const kept = keptShelf(shelfMemoryKey(key, config.os));
      if (kept) {
        setShelves((s) => (s[key] === kept ? s : { ...s, [key]: kept }));
        continue;
      }
      // An older one shows meanwhile, and is swapped for the new one only if it changed, so the cards stay put.
      const shown = shownShelf(shelfMemoryKey(key, config.os));
      if (shown) setShelves((s) => (s[key] ? s : { ...s, [key]: shown }));
      fetchShelf(client, key, config.os).then(
        (entries) => {
          if (!live) return;
          remember(entries);
          setShelves((s) => (s[key] && JSON.stringify(s[key]) === JSON.stringify(entries) ? s : { ...s, [key]: entries }));
        },
        // A shelf that couldn't load leaves the page without it, unless an older one shows; the grid under says when
        // the catalog can't be reached.
        () => live && setShelves((s) => (s[key] ? s : { ...s, [key]: [] })),
      );
    }
    return () => {
      live = false;
    };
  }, [client, config.os, keys.join(" ")]); // eslint-disable-line react-hooks/exhaustive-deps
  return shelves;
}

const shelfMemoryKey = (key: ShelfKey, os: Os) => `${os}|${key}`;

/** A hero as wide as the launcher shows one: the window less its side bar. */
export const launcherHero = (url: string) => heroSources(url, "calc(100vw - 120px)");

/** A shelf's apps for this computer, kept for the next time Browse shows it. */
async function fetchShelf(client: ApiClient, key: ShelfKey, os: Os): Promise<Entry[]> {
  const query: AppQuery = key.startsWith("consoles:") ? { consoles: key.slice(9).split(","), sort: "rating" } : { sort: key as Sort };
  // What this computer runs: on Linux, Windows' apps too (catalogOs).
  const page = await client.apps({ ...query, os: catalogOs(os), limit: SHELF_SIZE });
  const entries = page.items.filter((e) => availableOn(e, os));
  keepShelf(shelfMemoryKey(key, os), entries);
  return entries;
}

/** Browse's front page's shelves: the three orders, then each maker's consoles. */
export const frontShelves = (consoles: Parameters<typeof consoleShelves>[0]): ShelfKey[] => ["rating", "updated", "added", ...consoleShelves(consoles).map(consolesKey)];

/**
 * Browse's front page, brought ahead while the player is in the library: its shelves into browse-memory.ts, and the
 * pictures its first screen shows (the featured slides' art and the shelves' cards) loaded, so Browse comes whole.
 */
export async function prefetchBrowse({
  client,
  os,
  consoles,
  remember,
  cardSource,
}: {
  client: ApiClient;
  os: Os;
  consoles: Parameters<typeof consoleShelves>[0];
  remember: (entries: Entry[]) => void;
  /** The picture a Browse card of an app loads (useBrowseCards), to load it ahead. */
  cardSource: (entry: Entry) => ImageSource | undefined;
}) {
  const keys = frontShelves(consoles);
  const lists = await Promise.all(keys.map((key) => keptShelf(shelfMemoryKey(key, os)) ?? fetchShelf(client, key, os).catch(() => [] as Entry[])));
  const got = Object.fromEntries(keys.map((key, i) => [key, lists[i]])) as Partial<Record<ShelfKey, Entry[]>>;
  lists.forEach((entries) => remember(entries));
  // The featured slides first, the first one's above all; then each shelf's first cards, as far as a wide window shows.
  const { rating = [], updated = [], added = [] } = got;
  for (const { entry } of featuredMix({ rating, updated, added }, featuredDay())) {
    if (entry.libraryArt?.hero) warmImage(launcherHero(entry.libraryArt.hero)[0]);
    if (entry.libraryArt?.logo) warmImage(logoSources(entry.libraryArt.logo)[0]);
  }
  for (const entries of lists)
    for (const entry of entries.slice(0, 8)) {
      const source = cardSource(entry);
      if (source) warmImage(source);
    }
}

export function Discover({
  onOpen,
  onSeeAll,
  action,
  showOwned,
  hidden,
}: {
  onOpen: (entry: Entry) => void;
  /** Shows a shelf's whole list in the grid under it: its order, or its console. */
  onSeeAll: (to: SeeAll) => void;
  /** A card's Get, Play or Update. */
  action: (entry: Entry) => ReactNode;
  /** Browse shows apps already in the library too (as its grid does, else they stay out). */
  showOwned: boolean;
  /** The apps hidden from Browse (browse-hidden.ts), out of it as the grid has them. */
  hidden: ReadonlySet<string>;
}) {
  const { consoles, consoleNames, library, jobs, withShownTags } = useLauncher();
  // As in the grid: the apps in the library when the page came stay out; one added since stays, with its badge.
  const [owned] = useState(() => new Set(library.map((i) => i.id)));
  const showing = (entries: Entry[]) => browseShown(entries, { owned, showOwned, hidden, view: "hide" });
  const systems = consoleShelves(consoles);
  const shelves = useShelves(frontShelves(consoles));
  const inLibrary = new Set(library.map((i) => i.id));
  // The room a shelf out of sight keeps until it comes near and is drawn (app.css): the first shelf's, which is in sight,
  // as tall as the rest, and taller or shorter with the window as its cards are. A guess would move the page by the
  // difference as each one came.
  const page = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const el = page.current;
    const first = el?.querySelector<HTMLElement>(":scope > .shelf");
    if (!el || !first || typeof ResizeObserver === "undefined") return;
    const size = () => {
      // Nothing while Browse is hidden, where it measures nothing.
      if (first.offsetHeight) el.style.setProperty("--shelf-size", `${first.offsetHeight}px`);
    };
    size();
    const sized = new ResizeObserver(size);
    sized.observe(first);
    return () => sized.disconnect();
  }, [shelves]);
  // The day's mix of the best rated, just updated and newest, once all three have come, so the slides don't change as
  // each does; the day is the page's, so they don't change at midnight under the player either.
  const [day] = useState(featuredDay);
  const { rating, updated, added } = shelves;
  const featured = useMemo(
    () => (rating && updated && added ? featuredMix({ rating: showing(rating), updated: showing(updated), added: showing(added) }, day) : null),
    [rating, updated, added, day, showOwned, [...hidden].join()], // eslint-disable-line react-hooks/exhaustive-deps
  );
  const shelfTitle = (shelf: ConsoleShelf) => {
    if (shelf.id === "portable") return t({ message: "Portable systems", comment: "A shelf on Browse's front page: apps for handhelds (PSP, Vita, DS, 3DS, Game Boy)" });
    const brand = shelf.id.slice(6);
    return brand === "OtherPlatforms" ? t({ message: "Other platforms", comment: "A shelf on Browse's front page: apps for consoles no other shelf's maker made" }) : brand;
  };
  const consoleName = (tag: string) => (Object.hasOwn(consoleNames, tag) ? consoleNames[tag] : undefined);
  const looks = useBrowseCards();
  const card = (entry: Entry) => (
    <TickableCard
      key={entry.id}
      entry={withShownTags(entry)}
      {...looks.card(entry)}
      className={jobs[entry.id] ? "busy" : undefined}
      consoleNames={consoleNames}
      onOpen={() => onOpen(entry)}
      badge={inLibrary.has(entry.id) ? t`In library` : undefined}
      action={action(entry)}
    />
  );
  const shelf = (key: ShelfKey, title: string, to: SeeAll) => {
    const loaded = shelves[key];
    const entries = loaded && showing(key === "rating" ? loaded.filter(runsWell) : loaded);
    // One that came back empty (no apps for this computer, or no answer) isn't shown.
    if (entries && !entries.length) return null;
    return (
      <Shelf
        key={key}
        title={title}
        loading={!entries}
        action={
          <button type="button" className="text-button shelf-all" aria-label={t`See all: ${title}`} onClick={() => onSeeAll(to)}>
            {i18n._(SEE_ALL)}
          </button>
        }
      >
        {entries?.map(card)}
      </Shelf>
    );
  };
  return (
    <div ref={page} className={looks.look === "wide" ? "discover wide-cards" : "discover"}>
      {!featured ? (
        <div className="featured-placeholder" aria-hidden="true" />
      ) : (
        <FeaturedCarousel
          slides={featured}
          sources={launcherHero}
          consoleName={consoleName}
          open={(entry, content, props) => (
            <button type="button" {...props} onClick={() => onOpen(entry as Entry)}>
              {content}
            </button>
          )}
        />
      )}
      {shelf("rating", i18n._(SHELF_TITLES.rating), { sort: "rating" })}
      {shelf("updated", i18n._(SHELF_TITLES.updated), { sort: "updated" })}
      {shelf("added", i18n._(SHELF_TITLES.added), { sort: "added" })}
      {systems.map((s) => shelf(consolesKey(s), shelfTitle(s), { consoles: s.consoles }))}
      <h2 className="discover-all">{i18n._(ALL_APPS)}</h2>
    </div>
  );
}

/**
 * At the foot of an app's page: the best rated apps for other games on its console, for this computer, as a shelf.
 * None for an app with no console (the player's own, a tool) or when there's nothing else to show.
 */
/** The best rated for `system` on this computer, which "More like this" picks from: kept with the app's page (app-memory.ts). */
function likeThisAsk(client: ApiClient, system: string, os: Os) {
  const on = catalogOs(os);
  return answer(appKey.likeThis(system, on), () => client.apps({ console: system, sort: "rating", os: on, limit: SHELF_SIZE }));
}

/** "More like this" asked ahead with an app's page (App.tsx), as the pointer or the focus rests on its card. */
export function prefetchMoreLikeThis(client: ApiClient, entry: Pick<Entry, "consoles">, os: Os) {
  const system = entry.consoles?.[0];
  if (system) likeThisAsk(client, system, os).catch(() => {});
}

export function MoreLikeThis({ entry, onOpen, action, held }: { entry: Entry; onOpen: (entry: Entry) => void; action: (entry: Entry) => ReactNode; held?: boolean }) {
  const { client, config, consoleNames, remember, library, jobs, settings, withShownTags } = useLauncher();
  const looks = useBrowseCards();
  const system = entry.consoles?.[0];
  const pick = (items: Entry[]) => ({ for: entry.id, entries: likeThis(entry, items.filter((e) => availableOn(e, config.os))) });
  // One asked ahead shows with the page.
  const [items, setItems] = useState<{ for: string; entries: Entry[] } | null>(() => {
    const kept = system && keptAnswer<{ items: Entry[] }>(appKey.likeThis(system, catalogOs(config.os)));
    return kept ? pick(kept.items) : null;
  });
  useEffect(() => {
    if (!system) return;
    let live = true;
    likeThisAsk(client, system, config.os).then(
      (page) => {
        if (!live) return;
        remember(page.items);
        setItems(pick(page.items));
      },
      () => live && setItems({ for: entry.id, entries: [] }),
    );
    return () => {
      live = false;
    };
  }, [client, config.os, entry.id, system]); // eslint-disable-line react-hooks/exhaustive-deps
  // Less those hidden from Browse (browse-hidden.ts), as soon as they are.
  const hidden = new Set(settings.browseHidden?.map((h) => h.id));
  const entries = items?.for === entry.id ? items.entries.filter((e) => !hidden.has(e.id)) : null;
  // Asked for straight away, shown once what's over it came (`held`): the README coming in over it would push it down.
  if (held || !system || (entries && !entries.length)) return null;
  const inLibrary = new Set(library.map((i) => i.id));
  return (
    <div className={looks.look === "wide" ? "app-more wide-cards" : "app-more"}>
      <Shelf title={i18n._(MORE_LIKE_THIS)} loading={!entries}>
        {entries?.map((e) => (
          <EntryCard
            key={e.id}
            entry={withShownTags(e)}
            {...looks.card(e)}
            className={jobs[e.id] ? "busy" : undefined}
            consoleNames={consoleNames}
            onOpen={() => onOpen(e)}
            badge={inLibrary.has(e.id) ? t`In library` : undefined}
            action={action(e)}
          />
        ))}
      </Shelf>
    </div>
  );
}
