/**
 * Browse's front page, above the whole catalog while the player isn't searching or filtering: a few featured apps,
 * then shelves of what players say runs well, what's just been updated, what's new, and the consoles with the most
 * apps. Each shelf is one request to the catalog, for this computer's apps, and See all shows the same in the grid.
 */
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { AppQuery, Entry, Sort } from "@quiverlauncher/api";
import { EntryCard, boxOf, cardSources } from "@quiverlauncher/ui";
import { ALL_APPS, CONSOLE_SHELVES, FeaturedCarousel, MORE_LIKE_THIS, SEE_ALL, SHELF_SIZE, SHELF_TITLES, Shelf, featuredDay, featuredMix, likeThis, runsWell } from "@quiverlauncher/ui/discover";
import { availableOn, catalogOs, useLauncher } from "./store";
import { CATALOG_BOX } from "./library-view";

export type ShelfKey = "rating" | "updated" | "added" | `console:${string}`;
type SeeAll = { sort: Sort } | { console: string };

/**
 * The catalog's shelves, for this computer: each one request (`console:n64` a console's best rated), undefined until
 * it comes and empty if it couldn't. Browse's front page and TV mode show them.
 */
export function useShelves(keys: readonly ShelfKey[]) {
  const { client, config, remember } = useLauncher();
  const [shelves, setShelves] = useState<Partial<Record<ShelfKey, Entry[]>>>({});
  useEffect(() => {
    let live = true;
    for (const key of keys) {
      const query: AppQuery = key.startsWith("console:") ? { console: key.slice(8), sort: "rating" } : { sort: key as Sort };
      // What this computer runs: on Linux, Windows' apps too (catalogOs).
      client.apps({ ...query, os: catalogOs(config.os), limit: SHELF_SIZE }).then(
        (page) => {
          if (!live) return;
          remember(page.items);
          setShelves((s) => ({ ...s, [key]: page.items.filter((e) => availableOn(e, config.os)) }));
        },
        // A shelf that couldn't load leaves the page without it; the grid under says when the catalog can't be reached.
        () => live && setShelves((s) => ({ ...s, [key]: [] })),
      );
    }
    return () => {
      live = false;
    };
  }, [client, config.os, keys.join(" ")]); // eslint-disable-line react-hooks/exhaustive-deps
  return shelves;
}

export function Discover({
  onOpen,
  onSeeAll,
  action,
  showOwned,
}: {
  onOpen: (entry: Entry) => void;
  /** Shows a shelf's whole list in the grid under it: its order, or its console. */
  onSeeAll: (to: SeeAll) => void;
  /** A card's Get, Play or Update. */
  action: (entry: Entry) => ReactNode;
  /** Browse shows apps already in the library too (as its grid does, else they stay out). */
  showOwned: boolean;
}) {
  const { consoles, consoleNames, library, jobs } = useLauncher();
  // As in the grid: the apps in the library when the page came stay out; one added since stays, with its badge.
  const [owned] = useState(() => new Set(library.map((i) => i.id)));
  const showing = (entries: Entry[]) => (showOwned ? entries : entries.filter((e) => !owned.has(e.id)));
  const systems = CONSOLE_SHELVES.filter((id) => consoles.some((c) => c.id === id));
  const shelves = useShelves(["rating", "updated", "added", ...systems.map((id): ShelfKey => `console:${id}`)]);
  const inLibrary = new Set(library.map((i) => i.id));
  // The day's mix of the best rated, just updated and newest, once all three have come, so the slides don't change as
  // each does; the day is the page's, so they don't change at midnight under the player either.
  const [day] = useState(featuredDay);
  const { rating, updated, added } = shelves;
  const featured = useMemo(
    () => (rating && updated && added ? featuredMix({ rating: showing(rating), updated: showing(updated), added: showing(added) }, day) : null),
    [rating, updated, added, day, showOwned], // eslint-disable-line react-hooks/exhaustive-deps
  );
  const consoleName = (tag: string) => (Object.hasOwn(consoleNames, tag) ? consoleNames[tag] : undefined);
  const card = (entry: Entry) => (
    <EntryCard
      key={entry.id}
      entry={entry}
      coverSources={boxOf(entry) ? cardSources(boxOf(entry)!, CATALOG_BOX) : undefined}
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
    <div className="discover">
      {!featured ? (
        <div className="featured-placeholder" aria-hidden="true" />
      ) : (
        <FeaturedCarousel
          slides={featured}
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
      {systems.map((id) => shelf(`console:${id}`, consoleNames[id] ?? id, { console: id }))}
      <h2 className="discover-all">{i18n._(ALL_APPS)}</h2>
    </div>
  );
}

/**
 * At the foot of an app's page: the best rated apps for other games on its console, for this computer, as a shelf.
 * None for an app with no console (the player's own, a tool) or when there's nothing else to show.
 */
export function MoreLikeThis({ entry, onOpen, action }: { entry: Entry; onOpen: (entry: Entry) => void; action: (entry: Entry) => ReactNode }) {
  const { client, config, consoleNames, remember, library, jobs } = useLauncher();
  const system = entry.consoles?.[0];
  const [items, setItems] = useState<{ for: string; entries: Entry[] } | null>(null);
  useEffect(() => {
    if (!system) return;
    let live = true;
    client.apps({ console: system, sort: "rating", os: catalogOs(config.os), limit: SHELF_SIZE }).then(
      (page) => {
        if (!live) return;
        remember(page.items);
        setItems({ for: entry.id, entries: likeThis(entry, page.items.filter((e) => availableOn(e, config.os))) });
      },
      () => live && setItems({ for: entry.id, entries: [] }),
    );
    return () => {
      live = false;
    };
  }, [client, config.os, entry.id, system]); // eslint-disable-line react-hooks/exhaustive-deps
  const entries = items?.for === entry.id ? items.entries : null;
  if (!system || (entries && !entries.length)) return null;
  const inLibrary = new Set(library.map((i) => i.id));
  return (
    <div className="app-more">
      <Shelf title={i18n._(MORE_LIKE_THIS)} loading={!entries}>
        {entries?.map((e) => (
          <EntryCard
            key={e.id}
            entry={e}
            coverSources={boxOf(e) ? cardSources(boxOf(e)!, CATALOG_BOX) : undefined}
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
