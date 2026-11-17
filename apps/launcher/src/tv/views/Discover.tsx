/**
 * Discover: catalog games and library games not yet installed, with two rows above:
 * the library filter (not in it / in it / all) and Filter, which narrows the consoles
 * to makers and to home consoles or handhelds; under them, the consoles left. Up
 * from the grid's top row goes to the consoles, and up again to the filters. LB / RB
 * step through the consoles from anywhere. A gets a game: adds it to the library and
 * downloads it in one press. Pages load as focus nears the end.
 */
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ChevronDown } from "lucide-react";
import type { Entry } from "@quiverlauncher/api";
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import { CONSOLE_FORMS, consolesMatching, type ConsoleForm } from "../../console-forms";
import { useTv, useInputHandler } from "../context";
import { isInstalled, type TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";
import { GameHeader } from "./GameHeader";
import { GridTab, type Back } from "./GridTab";
import { RowList, useRows, type Row } from "./Settings";
import { discoverApps, matchesSearch, type LibraryFilter } from "./lists";

/** Kept by the shell: Discover comes back as it was left. */
export type DiscoverState = {
  filter: LibraryFilter;
  /** The makers (the catalog's brands) and kinds the consoles are narrowed to; none is any. */
  makers: string[];
  forms: ConsoleForm[];
  consoleId: string | null;
  /** Catalog pages loaded, per question asked of the catalog. */
  pages: Map<string, { entries: Entry[]; cursor: string | null; done: boolean; loading: boolean; error: string | null }>;
};

export const newDiscoverState = (): DiscoverState => ({ filter: "notInLibrary", makers: [], forms: [], consoleId: null, pages: new Map() });

/** The most consoles the catalog takes in one question (App.tsx's MAX_SYSTEMS). */
const MAX_CONSOLES = 8;

const LIBRARY_CHIPS: { id: LibraryFilter; label: MessageDescriptor }[] = [
  { id: "notInLibrary", label: msg({ message: "Not in library", comment: "Discover's filter: games not in the library yet" }) },
  { id: "inLibrary", label: msg({ message: "In library", comment: "Discover's filter: games in the library but not installed" }) },
  { id: "all", label: msg({ message: "All", comment: "Discover's filter: games in the library or not" }) },
];

const FORM_NAMES: Record<ConsoleForm, MessageDescriptor> = {
  home: msg({ message: "Home consoles", comment: "Discover's filter: consoles played on a TV, not handhelds" }),
  handheld: msg({ message: "Handhelds", comment: "Discover's filter: handheld consoles, such as the Game Boy or PSP" }),
};

const makerName = (brand: string) => (brand === "OtherPlatforms" ? t`Other platforms` : brand);

/** Discover's hint for going back down from the filters to the games. */
const games = () => t({ message: "Games", comment: "Controller hint on Discover's filters: back down to the games" });
/** And for the shoulder buttons, which step through the consoles there. */
const consolesHint = () => t({ message: "Consoles", comment: "Controller hint on Discover: the shoulder buttons go to the next or previous console" });

/** The top row: the library filters, then Filter. */
type Chip = { kind: "library"; id: LibraryFilter; label: string } | { kind: "filter"; label: string };

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
};

/** Filter's list: makers and kinds, each ticked or not with A, any number of them; B closes it. */
function ConsoleFilter({ makers, picked, forms, toggleMaker, toggleForm, clear, onClose, left }: {
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
    { kind: "action", label: t({ message: "Every console", comment: "Discover's Filter: no maker or kind picked" }), value: tick(!picked.length && !forms.length), run: clear },
    ...makers.map((brand): Row => ({ kind: "action", label: makerName(brand), value: tick(picked.includes(brand)), run: () => toggleMaker(brand) })),
    ...CONSOLE_FORMS.map((f): Row => ({ kind: "action", label: i18n._(FORM_NAMES[f]), value: tick(forms.includes(f)), run: () => toggleForm(f) })),
  ];
  const list = useRows(rows, onClose, LAYER.overlay, true, t({ message: "Pick", comment: "Controller hint in Discover's Filter list: ticks or unticks the focused maker or kind" }));
  return (
    <div className="tv-dropdown" role="dialog" aria-label={t`Filter consoles`} style={{ left }}>
      <RowList rows={rows} {...list} />
    </div>
  );
}

export function Discover({ state, memory, focused, onFocus, onGlobal, back, onDetails, onDownloads, consoleName, onGot, search: typed }: Props) {
  const { bridge, consoles } = useTv();
  const [filter, setFilter] = useState(state.filter);
  const [makers, setMakers] = useState(state.makers);
  const [forms, setForms] = useState(state.forms);
  const [consoleId, setConsoleId] = useState(state.consoleId);
  const [zone, setZone] = useState<"filters" | "consoles" | "grid">("grid");
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
  const key = `${JSON.stringify(query)}|${search}`;
  if (!state.pages.has(key)) state.pages.set(key, { entries: [], cursor: null, done: false, loading: false, error: null });
  const page = state.pages.get(key)!;

  const loadMore = useCallback(() => {
    const p = state.pages.get(key)!;
    if (p.loading || p.done || none) return;
    p.loading = true;
    bridge
      .discover({ ...query, search: search || undefined, cursor: p.cursor, limit: 48 })
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
  }, [bridge, query, none, search, key, state.pages]);

  useEffect(() => {
    if (filter !== "inLibrary" && !page.entries.length) loadMore();
  }, [filter, key, page.entries.length, loadMore]);

  const catalog = useMemo(() => page.entries.map((e) => bridge.view(e)), [page.entries, bridge]);
  // Memoized: the grid reports the focused app up on every change of identity, so a new copy each render would loop.
  const apps = useMemo(
    () =>
      discoverApps(bridge.apps.filter((a) => matchesSearch(a, search)), catalog, filter, onConsoles, kept).map((a) =>
        kept.has(a.id) && isInstalled(a) ? { ...a, fresh: true } : a,
      ),
    [bridge.apps, catalog, filter, onConsoles, kept, search],
  );

  // Pages of games already in the library leave little to show: keep loading until there's a screenful.
  useEffect(() => {
    if (filter !== "inLibrary" && apps.length < 24 && page.entries.length && !page.done && !page.loading && !page.error) loadMore();
  }, [filter, apps.length, page.entries.length, page.done, page.loading, page.error, loadMore]);

  const filterLabel = narrowed
    ? t({ message: `Filter: ${[...brands.filter((b) => makers.includes(b)).map(makerName), ...CONSOLE_FORMS.filter((f) => forms.includes(f)).map((f) => i18n._(FORM_NAMES[f]))].join(", ")}`, comment: "Discover's Filter button, with the makers and kinds picked" })
    : t({ message: "Filter", comment: "Discover's button that narrows the consoles to makers, home consoles or handhelds" });
  const chips: Chip[] = [...LIBRARY_CHIPS.map((c) => ({ kind: "library" as const, id: c.id, label: i18n._(c.label) })), { kind: "filter", label: filterLabel }];
  const consoleChips = useMemo(() => [{ id: null, label: t`All consoles` }, ...shown.map((c) => ({ id: c.id as string | null, label: c.short }))], [shown]);
  const [chip, setChip] = useState(0);
  const at = Math.max(0, consoleChips.findIndex((c) => c.id === consoleId));

  const showConsole = (id: string | null) => setConsoleId((state.consoleId = id));
  /** LB / RB: the next or previous of the consoles the filter leaves, after All, round from the last to All. */
  const shoulders = (action: InputAction) => {
    if (action !== "tabPrev" && action !== "tabNext") return false;
    const by = action === "tabPrev" ? -1 : 1;
    showConsole(consoleChips[(at + by + consoleChips.length) % consoleChips.length].id);
    return true;
  };
  const gridGlobal = (action: InputAction) => shoulders(action) || onGlobal(action);
  /** New makers or kinds: the console shown stays only if it's still among those left. */
  const narrow = (nextMakers: string[], nextForms: ConsoleForm[]) => {
    setMakers((state.makers = nextMakers));
    setForms((state.forms = nextForms));
    if (consoleId && !consolesMatching(known, nextMakers, nextForms).some((c) => c.id === consoleId)) showConsole(null);
  };
  const toggled = <T,>(list: T[], item: T) => (list.includes(item) ? list.filter((x) => x !== item) : [...list, item]);
  const pick = (c: Chip) => {
    if (c.kind === "library") {
      setFilter((state.filter = c.id));
      setZone("grid");
    } else setChoosing(true);
  };

  // The top row: the library filters and Filter.
  useInputHandler(
    {
      onAction(action) {
        if (shoulders(action) || onGlobal(action)) return;
        if (action === "left") setChip((i) => Math.max(0, i - 1));
        else if (action === "right") setChip((i) => Math.min(chips.length - 1, i + 1));
        else if (action === "accept") pick(chips[chip]);
        else if (action === "down") setZone("consoles");
        else if (action === "back") setZone("grid");
      },
      hints: () => [
        chips[chip]?.kind === "library"
          ? { action: "accept" as const, label: t({ message: "Show these", comment: "Controller hint on Discover's filters: shows the games the focused filter picks" }) }
          : { action: "accept" as const, label: t({ message: "Choose", comment: "Controller hint on Discover's Filter: opens its list of makers and kinds" }) },
        { action: "tabNext", label: consolesHint() },
        { action: "back", label: games() },
      ],
    },
    undefined,
    zone === "filters" && !choosing,
  );
  // The consoles' row: a console shows as soon as it's reached.
  useInputHandler(
    {
      onAction(action) {
        if (shoulders(action) || onGlobal(action)) return;
        if (action === "left") showConsole(consoleChips[Math.max(0, at - 1)].id);
        else if (action === "right") showConsole(consoleChips[Math.min(consoleChips.length - 1, at + 1)].id);
        else if (action === "up") setZone("filters");
        else if (action === "accept" || action === "down" || action === "back") setZone("grid");
      },
      hints: () => [
        { action: "accept", label: games() },
        { action: "tabNext", label: consolesHint() },
      ],
    },
    undefined,
    zone === "consoles",
  );

  // Keep the focused chips in view, and Filter's list under its button.
  const filterButton = useRef<HTMLButtonElement>(null);
  const [dropLeft, setDropLeft] = useState(0);
  useLayoutEffect(() => {
    if (choosing) setDropLeft(filterButton.current?.offsetLeft ?? 0);
  }, [choosing]);
  useEffect(() => {
    if (zone === "filters") document.querySelector(".tv-chip-row.top .focused")?.scrollIntoView({ block: "nearest", inline: "nearest" });
    document.querySelector(".tv-chip-row.consoles .chosen")?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [chip, zone, consoleId]);

  const loading = filter !== "inLibrary" && page.loading && !page.entries.length;
  /** What the grid shows: each its own place to come back to. */
  const view = `${filter}:${makers.join(",") || "*"}:${forms.join(",") || "*"}:${consoleId ?? "*"}|${search}`;
  const emptyText = none
    ? { title: t`No consoles match this filter`, text: t`Change the filter, or choose Every console.` }
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
      <div className={`tv-chip-row top${zone === "filters" ? " active" : ""}`} role="toolbar" aria-label={t`Filters`}>
        {chips.map((c, i) => (
          <button
            key={c.kind === "library" ? c.id : "filter"}
            ref={c.kind === "filter" ? filterButton : undefined}
            type="button"
            className={`tv-filter${c.kind === "library" ? (c.id === filter ? " chosen" : "") : narrowed ? " chosen split" : " split"}${zone === "filters" && i === chip ? " focused" : ""}`}
            aria-haspopup={c.kind === "filter" ? "dialog" : undefined}
            onClick={() => {
              setChip(i);
              setZone("filters");
              pick(c);
            }}
          >
            {c.label}
            {c.kind === "filter" && <ChevronDown aria-hidden="true" className="tv-filter-caret" />}
          </button>
        ))}
      </div>
      <div className={`tv-chip-row consoles${zone === "consoles" ? " active" : ""}`} role="toolbar" aria-label={t`Consoles`}>
        {consoleChips.map((c, i) => (
          <button
            key={c.id ?? "*"}
            type="button"
            className={`tv-filter${c.id === consoleId ? " chosen" : ""}${zone === "consoles" && i === at ? " focused" : ""}`}
            onClick={() => showConsole(c.id)}
          >
            {c.label}
          </button>
        ))}
      </div>
      {choosing && (
        <ConsoleFilter
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
        moreHints={[{ action: "tabNext", label: consolesHint() }]}
      />
    </div>
  );
}
