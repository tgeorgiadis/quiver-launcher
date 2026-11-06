/**
 * Discover (spec §8): catalog games and library games not yet installed, with
 * a filter row above (library: not in it / in it / all; then consoles). Up from
 * the grid's top row goes to the filters. A gets a game: adds it to the library
 * and downloads it in one press. Pages load as focus nears the end.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { Entry } from "@quiverlauncher/api";
import { useTv, useInputHandler } from "../context";
import { isInstalled, type TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import { GameHeader } from "./GameHeader";
import { GridTab, type Back } from "./GridTab";
import { discoverApps, matchesSearch, type LibraryFilter } from "./lists";

/** Kept by the shell: Discover comes back as it was left. */
export type DiscoverState = {
  filter: LibraryFilter;
  consoleId: string | null;
  /** Catalog pages loaded, per console ("*" for all). */
  pages: Map<string, { entries: Entry[]; cursor: string | null; done: boolean; loading: boolean; error: string | null }>;
};

export const newDiscoverState = (): DiscoverState => ({ filter: "notInLibrary", consoleId: null, pages: new Map() });

const LIBRARY_CHIPS: { id: LibraryFilter; label: string }[] = [
  { id: "notInLibrary", label: "Not in library" },
  { id: "inLibrary", label: "In library" },
  { id: "all", label: "All" },
];

type Chip = { kind: "library"; id: LibraryFilter; label: string } | { kind: "console"; id: string | null; label: string };

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

export function Discover({ state, memory, focused, onFocus, onGlobal, back, onDetails, onDownloads, consoleName, onGot, search: typed }: Props) {
  const { bridge, consoles } = useTv();
  const [filter, setFilter] = useState(state.filter);
  const [consoleId, setConsoleId] = useState(state.consoleId);
  const [zone, setZone] = useState<"filters" | "grid">("grid");
  const [, setLoaded] = useState(0);
  /** Games got during this visit: they stay put, ticked once installed, until Discover is left. */
  const [kept, setKept] = useState<ReadonlySet<string>>(() => new Set());
  // The catalog is asked once typing pauses, not at every letter.
  const [search, setSearch] = useState(typed);
  useEffect(() => {
    const timer = setTimeout(() => setSearch(typed.trim()), 350);
    return () => clearTimeout(timer);
  }, [typed]);
  const key = `${consoleId ?? "*"}|${search}`;
  if (!state.pages.has(key)) state.pages.set(key, { entries: [], cursor: null, done: false, loading: false, error: null });
  const page = state.pages.get(key)!;

  const loadMore = useCallback(() => {
    const p = state.pages.get(key)!;
    if (p.loading || p.done) return;
    p.loading = true;
    bridge
      .discover({ console: consoleId ?? undefined, search: search || undefined, cursor: p.cursor, limit: 48 })
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
  }, [bridge, consoleId, search, key, state.pages]);

  useEffect(() => {
    if (filter !== "inLibrary" && !page.entries.length) loadMore();
  }, [filter, key, page.entries.length, loadMore]);

  const catalog = useMemo(() => page.entries.map((e) => bridge.view(e)), [page.entries, bridge]);
  // Memoized: the grid reports the focused app up on every change of identity, so a new copy each render would loop.
  const apps = useMemo(
    () =>
      discoverApps(bridge.apps.filter((a) => matchesSearch(a, search)), catalog, filter, consoleId, kept).map((a) =>
        kept.has(a.id) && isInstalled(a) ? { ...a, fresh: true } : a,
      ),
    [bridge.apps, catalog, filter, consoleId, kept, search],
  );

  // Pages of games already in the library leave little to show: keep loading until there's a screenful.
  useEffect(() => {
    if (filter !== "inLibrary" && apps.length < 24 && page.entries.length && !page.done && !page.loading && !page.error) loadMore();
  }, [filter, apps.length, page.entries.length, page.done, page.loading, page.error, loadMore]);

  const chips: Chip[] = useMemo(() => {
    const known = [...consoles.ids()].map((id) => consoles.get(id)).filter((c) => !c.special).sort(consoles.compare);
    return [
      ...LIBRARY_CHIPS.map((c) => ({ kind: "library" as const, ...c })),
      { kind: "console" as const, id: null, label: "All consoles" },
      ...known.map((c) => ({ kind: "console" as const, id: c.id, label: c.short })),
    ];
  }, [consoles]);
  const [chip, setChip] = useState(0);
  const chosen = (c: Chip) => (c.kind === "library" ? c.id === filter : c.id === consoleId);
  const consoleChip = () => Math.max(0, chips.findIndex((c) => c.kind === "console" && c.id === consoleId));
  /** To a chip: a console applies as soon as it's reached; a library filter waits for A. */
  const toChip = (i: number) => {
    const next = Math.min(chips.length - 1, Math.max(0, i));
    setChip(next);
    if (chips[next].kind === "console") choose(chips[next]);
  };
  /** In the games, LB / RB first go up to the consoles (from the one shown); pressed again there, they change tabs. */
  const gridGlobal = (action: InputAction) => {
    if (action === "tabPrev" || action === "tabNext") {
      setChip(consoleChip());
      setZone("filters");
      return true;
    }
    return onGlobal(action);
  };
  const choose = (c: Chip) => {
    if (c.kind === "library") setFilter((state.filter = c.id));
    else setConsoleId((state.consoleId = c.id));
  };

  useInputHandler(
    {
      onAction(action) {
        if (onGlobal(action)) return;
        if (action === "left") toChip(chip - 1);
        else if (action === "right") toChip(chip + 1);
        else if (action === "accept") {
          choose(chips[chip]);
          setZone("grid");
        } else if (action === "down" || action === "back") setZone("grid");
      },
      hints: () => [
        ...(chips[chip]?.kind === "library" ? [{ action: "accept" as const, label: "Show these" }] : [{ action: "accept" as const, label: "Games" }]),
        { action: "tabNext", label: "Tabs" },
        { action: "back", label: "Games" },
      ],
    },
    undefined,
    zone === "filters",
  );

  // Keep the focused chip in view.
  useEffect(() => {
    if (zone === "filters") document.querySelector(".tv-chip-row .focused")?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }, [chip, zone]);

  const loading = filter !== "inLibrary" && page.loading && !page.entries.length;
  const emptyText =
    page.error && filter !== "inLibrary"
      ? { title: "Couldn't reach the catalog", text: page.error }
      : filter === "inLibrary"
        ? { title: "Everything in your library is installed", text: "Games you add but haven't installed show here." }
        : search && !loading
          ? { title: `Nothing in the catalog matches “${search}”`, text: "Search again, or B to clear the search." }
          : { title: loading ? "Loading the catalog…" : "Nothing to find here", text: loading ? "" : "Try another console, or All." };

  return (
    <div className="tv-discover">
      <GameHeader app={zone === "grid" ? focused : null} consoleName={focused && zone === "grid" ? consoleName(focused) : ""} />
      <div className={`tv-chip-row${zone === "filters" ? " active" : ""}`} role="toolbar" aria-label="Filters">
        {chips.map((c, i) => (
          <button
            key={`${c.kind}:${c.id}`}
            type="button"
            className={`tv-filter${chosen(c) ? " chosen" : ""}${zone === "filters" && i === chip ? " focused" : ""}${c.kind === "console" && c.id === null ? " split" : ""}`}
            onClick={() => {
              setChip(i);
              choose(c);
            }}
          >
            {c.label}
          </button>
        ))}
      </div>
      <GridTab
        key={`${filter}:${key}`}
        tabId={`discover:${filter}:${key}`}
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
        active={zone === "grid"}
        onEdge={(a) => a === "up" && setZone("filters")}
        onNearEnd={filter !== "inLibrary" ? loadMore : undefined}
        onPointerFocus={() => setZone("grid")}
        empty={emptyText}
      />
    </div>
  );
}
