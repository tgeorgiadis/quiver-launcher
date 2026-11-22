/**
 * Discover: catalog games and library games not yet installed, with the consoles
 * above them and Filter & sort before those: the catalog's order, the library (not in
 * it / in it / both), makers, and home consoles or handhelds, in a list X opens from
 * anywhere here. Up from the
 * grid's top row goes to the consoles; LB / RB step through them from anywhere, the
 * console's name flashing up as they do. A gets a game: adds it to the library and
 * downloads it in one press. Pages load as focus nears the end.
 */
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ChevronDown, Search } from "lucide-react";
import type { Entry, Sort } from "@quiverlauncher/api";
import { useLauncher } from "../../store";
import { useGameYears } from "../../game-years";
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import { CONSOLE_FORMS, consolesMatching, type ConsoleForm } from "../../console-forms";
import { useTv, useInputHandler } from "../context";
import { isInstalled, type TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";
import { GameHeader } from "./GameHeader";
import { Glyph } from "./Glyph";
import { GridTab, type Back } from "./GridTab";
import { usesX } from "./tileActions";
import { RowList, useRows, type Row } from "./Settings";
import { discoverApps, matchesSearch, sortApps, type LibraryFilter } from "./lists";

/** Kept by the shell: Discover comes back as it was left. */
export type DiscoverState = {
  filter: LibraryFilter;
  /** The order: the catalog's (not while searching, which is by how well games match), or by release date (here). */
  sort: DiscoverSort;
  /** The makers (the catalog's brands) and kinds the consoles are narrowed to; none is any. */
  makers: string[];
  forms: ConsoleForm[];
  consoleId: string | null;
  /** Catalog pages loaded, per question asked of the catalog. */
  pages: Map<string, { entries: Entry[]; cursor: string | null; done: boolean; loading: boolean; error: string | null }>;
};

export const newDiscoverState = (): DiscoverState => ({ filter: "notInLibrary", sort: "added", makers: [], forms: [], consoleId: null, pages: new Map() });

/** The catalog's orders, and by release date, which Discover does itself (the catalog can't). */
type DiscoverSort = Sort | "released" | "released-new";

/** The most consoles the catalog takes in one question (App.tsx's MAX_SYSTEMS). */
const MAX_CONSOLES = 8;

const LIBRARY_FILTERS: { id: LibraryFilter; label: MessageDescriptor }[] = [
  { id: "notInLibrary", label: msg({ message: "Not in library", comment: "Discover's filter: games not in the library yet" }) },
  { id: "inLibrary", label: msg({ message: "In library", comment: "Discover's filter: games in the library but not installed" }) },
  { id: "all", label: msg({ message: "All", comment: "Discover's filter: games in the library or not" }) },
];

const FORM_NAMES: Record<ConsoleForm, MessageDescriptor> = {
  home: msg({ message: "Home consoles", comment: "Discover's filter: consoles played on a TV, not handhelds" }),
  handheld: msg({ message: "Handhelds", comment: "Discover's filter: handheld consoles, such as the Game Boy or PSP" }),
};

/** The catalog's orders, as Browse has them; Discover starts with the newest. */
const CATALOG_SORTS: { id: DiscoverSort; label: MessageDescriptor }[] = [
  { id: "added", label: msg`Recently added` },
  { id: "updated", label: msg`Recently updated` },
  { id: "rating", label: msg`Top rated` },
  { id: "name", label: msg`Name A–Z` },
  { id: "released", label: msg({ message: "Release date, oldest first", comment: "Library sort: by the year the game an app plays first came out (not the app's own release)" }) },
  { id: "released-new", label: msg({ message: "Release date, newest first", comment: "Library sort: by the year the game an app plays first came out (not the app's own release)" }) },
];

const makerName = (brand: string) => (brand === "OtherPlatforms" ? t`Other platforms` : brand);

/** Discover's hint for going back down from the consoles to the games. */
const games = () => t({ message: "Games", comment: "Controller hint on Discover's filters: back down to the games" });
/** And for the shoulder buttons, which step through the consoles there. */
const consolesHint = () => t({ message: "Consoles", comment: "Controller hint on Discover: the shoulder buttons go to the next or previous console" });
/** Filter's name, on its button and as X's hint. */
const filterName = () => t({ message: "Filter/Sort", comment: "Discover's button and X's hint: the order of the games, and narrowing them to the library or not, makers, home consoles or handhelds" });

type Props = {
  state: DiscoverState;
  memory: Map<string, string>;
  focused: TvApp | null;
  onFocus: (app: TvApp | null) => void;
  onGlobal: (action: InputAction) => boolean;
  back?: Back;
  onDetails: (app: TvApp) => void;
  onDownloads: () => void;
  consoleName: (app: TvApp) => string;
  /** A game was got here (the shell may go to it in the library). */
  onGot: (app: TvApp) => void;
  /** The search (the catalog is searched for it, once typing pauses). */
  search: string;
  /** Shows a name large over the page for a moment (the shell's Flash, one for its tabs and these consoles). */
  flash: (label: string) => void;
};

/**
 * Filter's list: the library (one of three, left / right), then makers and kinds, each ticked or not with A, any
 * number of them; B closes it.
 */
function ConsoleFilter({ filter, setFilter, sort, setSort, makers, picked, forms, toggleMaker, toggleForm, clear, onClose, left }: {
  filter: LibraryFilter;
  setFilter: (filter: LibraryFilter) => void;
  sort: DiscoverSort;
  setSort: (sort: DiscoverSort) => void;
  makers: string[];
  picked: string[];
  forms: ConsoleForm[];
  toggleMaker: (brand: string) => void;
  toggleForm: (form: ConsoleForm) => void;
  clear: () => void;
  onClose: () => void;
  left: number;
}) {
  const tick = (on: boolean) => (on ? "✓" : "");
  const rows: Row[] = [
    {
      kind: "choice",
      label: t({ message: "Sort", comment: "Label of the library's sort order" }),
      options: CATALOG_SORTS.map((s) => ({ value: s.id, label: i18n._(s.label) })),
      value: sort,
      set: (value) => setSort(value as DiscoverSort),
    },
    {
      kind: "choice",
      label: t({ message: "Games", comment: "Discover's Filter: whether it shows games in the library, games not in it, or both" }),
      options: LIBRARY_FILTERS.map((f) => ({ value: f.id, label: i18n._(f.label) })),
      value: filter,
      set: (value) => setFilter(value as LibraryFilter),
    },
    { kind: "action", label: t({ message: "Every console", comment: "Discover's Filter: no maker or kind picked" }), value: tick(!picked.length && !forms.length), run: clear },
    ...makers.map((brand): Row => ({ kind: "action", label: makerName(brand), value: tick(picked.includes(brand)), run: () => toggleMaker(brand) })),
    ...CONSOLE_FORMS.map((f): Row => ({ kind: "action", label: i18n._(FORM_NAMES[f]), value: tick(forms.includes(f)), run: () => toggleForm(f) })),
  ];
  const list = useRows(rows, onClose, LAYER.overlay, true, t({ message: "Pick", comment: "Controller hint in Discover's Filter list: ticks or unticks the focused maker or kind" }));
  return (
    <div className="tv-dropdown" role="dialog" aria-label={filterName()} style={{ left }}>
      <RowList rows={rows} {...list} />
    </div>
  );
}

export function Discover({ state, memory, focused, onFocus, onGlobal, back, onDetails, onDownloads, consoleName, onGot, search: typed, flash: showFlash }: Props) {
  const { bridge, consoles } = useTv();
  const { client, settings: launcherSettings } = useLauncher();
  // Apps the player hid from Browse (browse-hidden.ts) stay out of Discover too.
  const hiddenIds = useMemo(() => new Set((launcherSettings.browseHidden ?? []).map((h) => h.id)), [launcherSettings.browseHidden]);
  const [filter, setFilter] = useState(state.filter);
  const [sort, setSort] = useState(state.sort);
  const [makers, setMakers] = useState(state.makers);
  const [forms, setForms] = useState(state.forms);
  const [consoleId, setConsoleId] = useState(state.consoleId);
  const [zone, setZone] = useState<"consoles" | "grid">("grid");
  const [choosing, setChoosing] = useState(false);
  const [, setLoaded] = useState(0);
  /** Games got during this visit: they stay put, ticked once installed, until Discover is left. */
  const [kept, setKept] = useState<ReadonlySet<string>>(() => new Set());
  // The catalog is asked once typing pauses, not at every letter.
  const [search, setSearch] = useState(typed);
  useEffect(() => {
    const timer = setTimeout(() => setSearch(typed.trim()), 350);
    return () => clearTimeout(timer);
  }, [typed]);
  const known = useMemo(() => [...consoles.ids()].map((id) => consoles.get(id)).filter((c) => !c.special).sort(consoles.compare), [consoles]);
  const brands = useMemo(() => [...new Set(known.map((c) => c.brand))], [known]);
  const shown = useMemo(() => consolesMatching(known, makers, forms), [known, makers, forms]);
  const narrowed = makers.length > 0 || forms.length > 0;
  /** The consoles the games are for: the one chosen, or those the filter leaves; null is any. */
  const onConsoles = useMemo(() => (consoleId ? new Set([consoleId]) : narrowed ? new Set(shown.map((c) => c.id)) : null), [consoleId, narrowed, shown]);
  /** What the catalog is asked: as narrow as it takes (a few consoles, or the makers'); onConsoles narrows the rest here. */
  const query = useMemo(() => {
    if (consoleId) return { console: consoleId };
    if (!narrowed) return {};
    if (shown.length <= MAX_CONSOLES) return { consoles: shown.map((c) => c.id) };
    return makers.length ? { consoles: makers.map((m) => `maker:${m}`) } : {};
  }, [consoleId, narrowed, shown, makers]);
  /** No console is of those makers and kinds (an Xbox handheld): nothing to ask for. */
  const none = onConsoles !== null && onConsoles.size === 0;
  /**
   * By release date: every game of the consoles picked is loaded and ordered here, as the catalog can't; not for the
   * whole catalog, which would mean looking up the year of every game in it.
   */
  const byYear = sort === "released" || sort === "released-new";
  const pickConsole = byYear && onConsoles === null;
  const asked: Sort = byYear ? "name" : sort;
  const key = `${JSON.stringify(query)}|${search || asked}`;
  if (!state.pages.has(key)) state.pages.set(key, { entries: [], cursor: null, done: false, loading: false, error: null });
  const page = state.pages.get(key)!;

  const loadMore = useCallback(() => {
    const p = state.pages.get(key)!;
    if (p.loading || p.done || none || pickConsole) return;
    p.loading = true;
    bridge
      .discover({ ...query, ...(search ? { search } : { sort: asked }), cursor: p.cursor, limit: 48 })
      .then((next) => {
        p.entries = [...p.entries, ...next.items];
        p.cursor = next.nextCursor;
        p.done = next.isDone;
        p.error = null;
      })
      .catch((e) => void (p.error = e instanceof Error ? e.message : String(e)))
      .finally(() => {
        p.loading = false;
        setLoaded((n) => n + 1);
      });
  }, [bridge, query, none, pickConsole, search, asked, key, state.pages]);

  useEffect(() => {
    if (filter !== "inLibrary" && !page.entries.length) loadMore();
  }, [filter, key, page.entries.length, loadMore]);

  const catalog = useMemo(() => page.entries.filter((e) => !hiddenIds.has(e.id)).map((e) => bridge.view(e)), [page.entries, bridge, hiddenIds]);
  // Memoized: the grid reports the focused app up on every change of identity, so a new copy each render would loop.
  const listed = useMemo(
    () =>
      discoverApps(bridge.apps.filter((a) => matchesSearch(a, search)), catalog, filter, onConsoles, kept).map((a) =>
        kept.has(a.id) && isInstalled(a) ? { ...a, fresh: true } : a,
      ),
    [bridge.apps, catalog, filter, onConsoles, kept, search],
  );

  // Pages of games already in the library leave little to show: keep loading until there's a screenful. By release
  // date, until there's all of them.
  useEffect(() => {
    if (filter !== "inLibrary" && (listed.length < 24 || byYear) && page.entries.length && !page.done && !page.loading && !page.error) loadMore();
  }, [filter, listed.length, byYear, page.entries.length, page.done, page.loading, page.error, loadMore]);

  // Their years, once they're all there (game-years.ts), and then the order; until then, a moment's wait.
  const allThere = !byYear || pickConsole || filter === "inLibrary" || page.done;
  const years = useGameYears(client, listed.flatMap((a) => a.entry.games.map((g) => g.slug)), byYear && !pickConsole && allThere);
  const sorting = byYear && !pickConsole && (!allThere || listed.some((a) => a.entry.games[0] && !(a.entry.games[0].slug in years)));
  const apps = useMemo(() => (!byYear ? listed : pickConsole || sorting ? [] : sortApps(listed, sort, years)), [byYear, pickConsole, sorting, listed, sort, years]);

  /** What Filter/Sort has picked, at the top right: the order, the library's choice, then makers and kinds. */
  const picks = [
    i18n._(CATALOG_SORTS.find((s) => s.id === sort)!.label),
    i18n._(LIBRARY_FILTERS.find((f) => f.id === filter)!.label),
    ...brands.filter((b) => makers.includes(b)).map(makerName),
    ...CONSOLE_FORMS.filter((f) => forms.includes(f)).map((f) => i18n._(FORM_NAMES[f])),
  ];
  const consoleChips = useMemo(() => [{ id: null, label: t`All consoles` }, ...shown.map((c) => ({ id: c.id as string | null, label: c.short }))], [shown]);
  const at = Math.max(0, consoleChips.findIndex((c) => c.id === consoleId));

  /** The console LB / RB came to, its name large over the games for a moment, so the change can't be missed. */
  /** Each LB / RB, a new number: the console chip they came to pops again. */
  const [pop, setPop] = useState(0);
  const showConsole = (id: string | null) => setConsoleId((state.consoleId = id));
  /** LB / RB: the next or previous of the consoles the filter leaves, after All, round from the last to All. */
  const shoulders = (action: InputAction) => {
    if (action !== "tabPrev" && action !== "tabNext") return false;
    const by = action === "tabPrev" ? -1 : 1;
    const next = consoleChips[(at + by + consoleChips.length) % consoleChips.length];
    showConsole(next.id);
    showFlash(next.id ? consoles.get(next.id).name : next.label);
    setPop((n) => n + 1);
    return true;
  };
  /** X: Filter's list, but on a game that uses X itself. */
  const filterKey = (action: InputAction, game?: TvApp | null) => {
    if (action !== "secondary" || usesX(game)) return false;
    setChoosing(true);
    return true;
  };
  const filterHint = { action: "secondary" as const, label: filterName() };
  const gridGlobal = (action: InputAction) => shoulders(action) || filterKey(action, focused) || onGlobal(action);
  /** New makers or kinds: the console shown stays only if it's still among those left. */
  const narrow = (nextMakers: string[], nextForms: ConsoleForm[]) => {
    setMakers((state.makers = nextMakers));
    setForms((state.forms = nextForms));
    if (consoleId && !consolesMatching(known, nextMakers, nextForms).some((c) => c.id === consoleId)) showConsole(null);
  };
  const toggled = <T,>(list: T[], item: T) => (list.includes(item) ? list.filter((x) => x !== item) : [...list, item]);

  // The consoles' row: a console shows as soon as it's reached.
  useInputHandler(
    {
      onAction(action) {
        if (shoulders(action) || filterKey(action) || onGlobal(action)) return;
        if (action === "left") showConsole(consoleChips[Math.max(0, at - 1)].id);
        else if (action === "right") showConsole(consoleChips[Math.min(consoleChips.length - 1, at + 1)].id);
        else if (action === "accept" || action === "down" || action === "back") setZone("grid");
      },
      hints: () => [{ action: "accept", label: games() }, filterHint, { action: "tabNext", label: consolesHint() }],
    },
    undefined,
    zone === "consoles" && !choosing,
  );

  // Filter's list under its button, and the console shown in view.
  const filterButton = useRef<HTMLButtonElement>(null);
  const [dropLeft, setDropLeft] = useState(0);
  useLayoutEffect(() => {
    if (choosing) setDropLeft(filterButton.current?.offsetLeft ?? 0);
  }, [choosing]);
  useEffect(() => {
    document.querySelector(".tv-chip-row.consoles .chosen")?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [zone, consoleId]);

  const loading = filter !== "inLibrary" && page.loading && !page.entries.length;
  /** What the grid shows: each its own place to come back to. */
  const view = `${filter}:${sort}:${makers.join(",") || "*"}:${forms.join(",") || "*"}:${consoleId ?? "*"}|${search}`;
  const emptyText = none
    ? { title: t`No consoles match this filter`, text: t`Change the filter, or choose Every console.` }
    : pickConsole
      ? { title: t`Pick a console to sort by release date`, text: t`LB / RB go through the consoles; Filter/Sort narrows them to makers or kinds.` }
      : sorting && !page.error
        ? { title: t`Sorting by release date…`, text: "" }
        : page.error && filter !== "inLibrary"
          ? { title: t`Couldn't reach the catalog`, text: page.error }
          : filter === "inLibrary"
            ? { title: t`Everything in your library is installed`, text: t`Games you add but haven't installed show here.` }
            : search && !loading
              ? { title: t`Nothing in the catalog matches “${search}”`, text: t`Search again, or B to clear the search.` }
              : { title: loading ? t`Loading the catalog…` : t`Nothing to find here`, text: loading ? "" : t`Try another console, or All.` };

  return (
    <div className="tv-discover">
      <GameHeader app={zone === "grid" ? focused : null} consoleName={focused && zone === "grid" ? consoleName(focused) : ""} />
      <div className="tv-filter-summary" aria-label={filterName()}>
        {search && (
          <span className="searched">
            <Search aria-hidden="true" />“{search}”
          </span>
        )}
        {picks.map((pick) => (
          <span key={pick}>{pick}</span>
        ))}
      </div>
      <div className={`tv-chip-row consoles${zone === "consoles" ? " active" : ""}`} role="toolbar" aria-label={t`Consoles`}>
        <button ref={filterButton} type="button" className={`tv-filter tv-filter-button${narrowed || filter !== "notInLibrary" || sort !== "added" ? " chosen" : ""}`} aria-haspopup="dialog" onClick={() => setChoosing(true)}>
          <Glyph action="secondary" size={20} />
          {filterName()}
          <ChevronDown aria-hidden="true" className="tv-filter-caret" />
        </button>
        {consoleChips.map((c, i) => (
          <button
            key={c.id === consoleId && pop ? `${c.id ?? "*"}:${pop}` : (c.id ?? "*")}
            type="button"
            className={`tv-filter${c.id === consoleId ? " chosen" : ""}${c.id === consoleId && pop ? " pop" : ""}${zone === "consoles" && i === at ? " focused" : ""}`}
            onClick={() => showConsole(c.id)}
          >
            {c.label}
          </button>
        ))}
      </div>
      {choosing && (
        <ConsoleFilter
          filter={filter}
          setFilter={(f) => setFilter((state.filter = f))}
          sort={sort}
          setSort={(s) => setSort((state.sort = s))}
          makers={brands}
          picked={makers}
          forms={forms}
          toggleMaker={(brand) => narrow(toggled(makers, brand), forms)}
          toggleForm={(f) => narrow(makers, toggled(forms, f))}
          clear={() => narrow([], [])}
          onClose={() => setChoosing(false)}
          left={dropLeft}
        />
      )}
      <GridTab
        key={view}
        tabId={`discover:${view}`}
        apps={apps}
        memory={memory}
        onFocus={onFocus}
        onGlobal={gridGlobal}
        back={back}
        onDetails={onDetails}
        onDownloads={onDownloads}
        onGet={(app) => {
          setKept((k) => new Set(k).add(app.id));
          onGot(app);
        }}
        active={zone === "grid" && !choosing}
        onEdge={(a) => a === "up" && setZone("consoles")}
        onNearEnd={filter !== "inLibrary" ? loadMore : undefined}
        onPointerFocus={() => setZone("grid")}
        empty={emptyText}
        moreHints={usesX(focused) ? [{ action: "tabNext", label: consolesHint() }] : [filterHint, { action: "tabNext", label: consolesHint() }]}
      />
    </div>
  );
}
