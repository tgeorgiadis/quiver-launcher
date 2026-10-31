/**
 * TV mode: the launcher across a TV or a Steam Deck's screen, for a controller from the couch. Across the top, the bar
 * with two tabs: the library, as a grid, and the catalog's shelves to browse, LB and RB between them. For finding a
 * game in a large library, with a controller: what to show (a tab or a playlist), a console, an order, and LT and RT
 * to jump letter to letter in a list by name (a screen at a time in another), with the letters down the grid's right.
 * Its search is there only where something can type into it: Steam's keyboard (a Steam
 * Deck), or a keyboard, whose typing starts a search wherever the focus is. Its cards say what this computer has of each
 * app, where Browse's say what players said. A opens
 * what the focus is on and Y presses its button, as anywhere; an app's page opens over it as usual, and Back comes
 * home. It comes on by itself in Steam's Game Mode (unless turned off in Settings), or from the top bar's TV button.
 *
 * Moving should cost little: a card re-renders only when the focus comes to it or leaves (its button shows while it has
 * the focus), so a step doesn't re-render the screen's other cards.
 */
import { memo, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ChevronDown, Download, LogOut, Search, Settings as SettingsIcon, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";
import { EntryCard, boxOf, cardSources } from "@quiverlauncher/ui";
import { SHELF_TITLES, Shelf, runsWell } from "@quiverlauncher/ui/discover";
import { hasUpdate, useLauncher } from "./store";
import { useShelves } from "./discover";
import { withOverrides } from "./library";
import { GameTile, LibraryTile } from "./library-tile";
import { CATALOG_BOX } from "./library-view";
import { isStack, stacks } from "./group";
import { viewOf } from "./view";
import { TabPrompt } from "./pad-hints";
import { LIBRARY_SORTS, isLibrarySort, type LibrarySort } from "./sort";
import { takesText, typeInto } from "./spatial";
import { jumpLetters, letterOf, libraryConsoles, pickLibrary, type TvApp } from "./tv-library";
import { LetterIndex } from "./letter-index";

type TvTab = "library" | "browse";

/** A keyboard's been typed on since the launcher started: TV mode's search is offered from then on. */
let typedOn = false;

export function TvHome({
  onOpen,
  onLeave,
  onDownloads,
  onSettings,
  action,
}: {
  onOpen: (entry: Entry) => void;
  /** Back to the usual launcher, which TV mode then stays off for. */
  onLeave: () => void;
  onDownloads: () => void;
  onSettings: () => void;
  /** An app's Get, Play or Update. */
  action: (entry: Entry) => ReactNode;
}) {
  const { config, library, catalog, installs, consoleNames, consoles, collections, jobs, settings } = useLauncher();
  const shelves = useShelves(["rating", "updated", "added"]);
  // The library, as the player named and pictured it. Hidden apps stay hidden, and those the catalog no longer has
  // stay off. Made again only as the library or what's on this computer changes, so its cards keep theirs.
  const apps = useMemo<TvApp[]>(() => {
    const hidden = new Set(settings.hidden ?? []);
    return library
      .filter((i) => catalog[i.id] && !hidden.has(i.id))
      .map((i) => ({
        entry: withOverrides(catalog[i.id], i.overrides),
        addedAt: i.addedAt,
        installed: Boolean(installs[i.id]),
        lastPlayed: installs[i.id]?.lastPlayed,
        installedAt: installs[i.id]?.installedAt,
        update: !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id]),
        tags: i.overrides?.tags ?? [],
      }));
  }, [library, catalog, installs, jobs, settings.hidden]);
  const inLibrary = useMemo(() => new Set(library.map((i) => i.id)), [library]);
  const rated = useMemo(() => shelves.rating?.filter(runsWell), [shelves.rating]);
  // The library's own playlists (a followed one's apps come from the site, which TV mode doesn't ask for).
  const playlists = useMemo(() => collections.filter((c) => !c.follows), [collections]);
  const makers = useMemo(() => Object.fromEntries(consoles.map((c) => [c.id, c.brand])), [consoles]);

  const [search, setSearch] = useState("");
  const [show, setShow] = useState("all");
  const [system, setSystem] = useState("");
  const [sort, setSort] = useState<LibrarySort>("played");
  // A card for each game, as the library's grid has them (group.ts): by the games' names where it would be by the apps'.
  const { byGame } = viewOf(settings.library);
  const order: LibrarySort = byGame && sort === "name" ? "game" : sort;
  const mine = useMemo(() => pickLibrary(apps, { search, show, console: system, sort: order }, playlists, makers), [apps, search, show, system, order, playlists, makers]);
  const cards = useMemo(() => {
    const facts = new Map(mine.map((a) => [a.entry, a]));
    const entries = mine.map((a) => a.entry);
    return byGame ? stacks(entries, (e) => facts.get(e) ?? { installed: false }) : entries;
  }, [mine, byGame]);
  const systems = useMemo(() => libraryConsoles(apps), [apps]);
  // By name or game: the letters its apps file under, for the index and the triggers.
  const letters = useMemo(() => jumpLetters(mine.map((a) => a.entry), order).map((l) => l.letter), [mine, order]);
  const byLetter = order === "name" || order === "game";
  const letterFor = (e: Entry) => (byLetter ? letterOf(order === "game" ? (e.games[0]?.title ?? e.projectName) : e.projectName) : undefined);
  const inLibraryNow = useCallback((id: string) => inLibrary.has(id), [inLibrary]);
  const grid = useRef<HTMLDivElement>(null);
  const updateCount = apps.filter((a) => a.update).length;
  // The search: a button where something can type into it, a field once it's opened or has words.
  const [keyboard, setKeyboard] = useState(typedOn);
  const [searching, setSearching] = useState(false);
  const field = useRef<HTMLInputElement>(null);
  const searchable = Boolean(config.steamKeyboard) || keyboard;
  const openSearch = () => {
    setSearching(true);
    requestAnimationFrame(() => field.current && typeInto(field.current));
  };

  // The library, unless it has nothing (as the catalog its cards need may still be loading).
  const [tab, setTab] = useState<TvTab>(() => (library.length ? "library" : "browse"));
  // The cards' Get or Play and opening, the same functions every render, so a card's props change only with its app.
  const latest = useRef({ action, onOpen });
  latest.current = { action, onOpen };
  const cardAction = useCallback((entry: Entry) => latest.current.action(entry), []);
  const open = useCallback((entry: Entry) => latest.current.onOpen(entry), []);
  // Browse's: the catalog's cards, as Browse has them outside TV mode.
  const card = (entry: Entry) => (
    <TvCard key={entry.id} entry={entry} badge={inLibrary.has(entry.id)} busy={Boolean(jobs[entry.id])} consoleNames={consoleNames} onOpen={open} action={cardAction} />
  );
  // The focus on the first card when TV mode comes, and when the tab changes from anywhere but the tabs themselves
  // (LB and RB from a card), so the d-pad is ready at once.
  const panel = useRef<HTMLDivElement>(null);
  const tabs = useRef<HTMLDivElement>(null);
  const [focused, setFocused] = useState<TvTab | null>(null);
  useEffect(() => {
    if (focused === tab) return;
    const first = panel.current?.querySelector<HTMLElement>(".card-open");
    if (!first) return;
    if (!tabs.current?.contains(document.activeElement)) first.focus({ preventScroll: true });
    panel.current?.scrollTo({ top: 0 });
    setFocused(tab);
  });
  // A keyboard's letters, typed with the focus anywhere on the library but a field, start a search with them.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!e.isTrusted || e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey || e.key.length !== 1 || e.key === " ") return;
      typedOn = true;
      setKeyboard(true);
      const here = panel.current;
      if (tab !== "library" || !here || !here.getClientRects().length || takesText(e.target) || document.querySelector('[role="dialog"]')) return;
      e.preventDefault();
      setSearch((s) => s + e.key);
      setSearching(true);
      requestAnimationFrame(() => field.current?.focus());
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [tab]);
  const count = mine.length;
  return (
    <div className="tv">
      <header className="tv-bar">
        <span className="tv-brand">
          <img src="/quiver-icon-96.png" alt="" width="30" height="30" />
          Quiver
        </span>
        <div className="tv-tabs" role="tablist" aria-label={t`TV mode`} ref={tabs}>
          <TabPrompt step="prevTab" />
          {(
            [
              ["library", t`Library`],
              ["browse", t({ message: "Browse", comment: "The tab with the catalog of apps" })],
            ] as const
          ).map(([key, label]) => (
            <button key={key} type="button" role="tab" aria-selected={tab === key} onClick={() => setTab(key)}>
              {label}
            </button>
          ))}
          <TabPrompt step="nextTab" />
        </div>
        <Clock />
        <button type="button" className="tv-icon" aria-label={t`Downloads`} title={t`Downloads`} onClick={onDownloads}>
          <Download size={20} />
        </button>
        <button type="button" className="tv-icon" aria-label={t`Settings`} title={t`Settings`} onClick={onSettings}>
          <SettingsIcon size={20} />
        </button>
        <button type="button" className="tv-leave" onClick={onLeave}>
          <LogOut size={17} aria-hidden="true" />{" "}
          {/* An icon alone on a phone (app.css), its name kept for a screen reader. */}
          <span className="tv-leave-label">
            <Trans comment="Button: back from TV mode to the usual launcher">Leave TV mode</Trans>
          </span>
        </button>
      </header>
      <div className="tv-rows" role="tabpanel" ref={panel}>
        {tab === "library" ? (
          <>
            {/* Finding a game in a large library with a controller: as the library's own page has them. */}
            <div className="tv-tools">
              <span className="quiet-select tv-select">
                <select aria-label={t({ message: "Show", comment: "Picks which library tab or playlist TV mode's library shows" })} value={show} onChange={(e) => setShow(e.target.value)}>
                  <option value="all">{t({ message: "All", comment: "Library tab: every app in the library" })}</option>
                  <option value="installed">{t({ message: "Installed", context: "tab", comment: "Library tab" })}</option>
                  <option value="not-installed">{t({ message: "Not installed", context: "tab", comment: "Library tab" })}</option>
                  {(updateCount > 0 || show === "updates") && <option value="updates">{t({ message: `Updates (${updateCount})`, comment: "Library tab: apps with an update" })}</option>}
                  {playlists.map((c) => (
                    <option key={c.key} value={c.key}>
                      {c.name}
                    </option>
                  ))}
                </select>
                <ChevronDown size={16} aria-hidden="true" />
              </span>
              {systems.length > 1 && (
                <span className="quiet-select tv-select">
                  <select aria-label={t`Console`} value={system} onChange={(e) => setSystem(e.target.value)}>
                    <option value="">{t`Any console`}</option>
                    {systems.map(({ id, apps: n }) => (
                      <option key={id} value={id}>
                        {`${consoleNames[id] ?? id.toUpperCase()} (${n})`}
                      </option>
                    ))}
                  </select>
                  <ChevronDown size={16} aria-hidden="true" />
                </span>
              )}
              <span className="quiet-select tv-select">
                <select aria-label={t({ message: "Sort", comment: "Label of the library's sort order" })} value={sort} onChange={(e) => isLibrarySort(e.target.value) && setSort(e.target.value)}>
                  {LIBRARY_SORTS.map(([value, label]) => (
                    <option key={value} value={value}>
                      {i18n._(label)}
                    </option>
                  ))}
                </select>
                <ChevronDown size={16} aria-hidden="true" />
              </span>
              {searching || search ? (
                <label className="search tv-search">
                  <Search size={18} />
                  <input
                    ref={field}
                    aria-label={t`Search your library`}
                    placeholder={t`Search your library`}
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    // Empty and left (not for Steam's keyboard, which takes the window's focus but leaves the field the page's): a button again.
                    onBlur={() => !search && document.activeElement !== field.current && setSearching(false)}
                  />
                  {search && (
                    <button type="button" className="search-clear" aria-label={t`Clear search`} onClick={() => (setSearch(""), setSearching(false))}>
                      <X size={15} />
                    </button>
                  )}
                </label>
              ) : (
                searchable && (
                  <button type="button" className="tv-search-button" onClick={openSearch}>
                    <Search size={18} aria-hidden="true" /> <Trans comment="Placeholder of the library's search field">Search</Trans>
                  </button>
                )
              )}
              <span className="tv-count">{plural(count, { one: "# app", other: "# apps" })}</span>
            </div>
            {count > 0 ? (
              <div className="letters-wrap">
                {/* The library's own cards, box art with the names under them, as the library's grid has them. */}
                <div className="tv-grid layout-grid image-box size-medium" ref={grid} data-jump={byLetter ? "letters" : "pages"}>
                  {cards.map((entry) =>
                    isStack(entry) ? (
                      <GameTile
                        key={`game:${entry.game.id}`}
                        stack={entry}
                        letter={byLetter ? letterOf(entry.game.title) : undefined}
                        image="box"
                        size="medium"
                        names
                        marks
                        inLibrary={inLibraryNow}
                        action={cardAction}
                        onOpen={open}
                        tv
                      />
                    ) : (
                      <LibraryTile
                        key={entry.id}
                        entry={entry}
                        letter={letterFor(entry)}
                        image="box"
                        size="medium"
                        names
                        nameOrder="app"
                        marks
                        inLibrary
                        action={cardAction}
                        onOpen={open}
                        tv
                      />
                    ),
                  )}
                </div>
                {byLetter && letters.length > 1 && <LetterIndex grid={grid} letters={letters} />}
              </div>
            ) : apps.length ? (
              <p className="tv-empty">
                <Trans>No apps here match. Try another search, or show all of them.</Trans>
              </p>
            ) : (
              <p className="tv-empty">
                <Trans>Your library is empty. Apps you get from Browse show here.</Trans>
              </p>
            )}
          </>
        ) : (
          <>
            {(rated === undefined || rated.length > 0) && (
              <Shelf title={i18n._(SHELF_TITLES.rating)} loading={rated === undefined}>
                {rated?.map((entry) => card(entry))}
              </Shelf>
            )}
            {(["updated", "added"] as const).map((key) => {
              const entries = shelves[key];
              if (entries && !entries.length) return null;
              return (
                <Shelf key={key} title={i18n._(SHELF_TITLES[key])} loading={!entries}>
                  {entries?.map((entry) => card(entry))}
                </Shelf>
              );
            })}
          </>
        )}
      </div>
    </div>
  );
}

/**
 * A catalog card on TV mode's Browse, as Browse has them outside TV mode (the library's are the library's own,
 * library-tile.tsx). It shows its button (Get, Play or a download's progress) while it has the focus or is
 * downloading: one Action for the card the player is on, not one for each card on the screen, every one of which
 * redrew with each change to the launcher's state.
 */
const TvCard = memo(function TvCard({
  entry,
  badge,
  busy,
  consoleNames,
  onOpen,
  action,
}: {
  entry: Entry;
  /** "In library". */
  badge: boolean;
  busy: boolean;
  consoleNames: Record<string, string>;
  onOpen: (entry: Entry) => void;
  action: (entry: Entry) => ReactNode;
}) {
  const [here, setHere] = useState(false);
  return (
    <div
      onFocus={() => setHere(true)}
      // Leaving for its own button keeps it: it mustn't go as the focus goes to it.
      onBlur={(e) => !e.currentTarget.contains(e.relatedTarget as Node | null) && setHere(false)}
    >
      <EntryCard
        entry={entry}
        coverSources={boxOf(entry) ? cardSources(boxOf(entry)!, CATALOG_BOX) : undefined}
        className={busy ? "busy" : undefined}
        consoleNames={consoleNames}
        onOpen={() => onOpen(entry)}
        badge={badge ? t`In library` : undefined}
        action={here || busy ? action(entry) : undefined}
      />
    </div>
  );
});

/** The time, as a console's home screen shows it. */
function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(timer);
  }, []);
  return <span className="tv-clock">{now.toLocaleTimeString(i18n.locale, { hour: "numeric", minute: "2-digit" })}</span>;
}
