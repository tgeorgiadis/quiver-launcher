/**
 * Discover: catalog games and library games not yet installed, with
 * a filter row above (library: not in it / in it / all; a maker; home consoles or
 * handhelds; then the consoles those leave). Up from the grid's top row goes to the
 * filters. LB / RB step through the consoles, from the grid or the filters. A gets
 * a game: adds it to the library and downloads it in one press. Pages load as focus
 * nears the end.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { Entry } from "@quiverlauncher/api";
import { msg, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import { CONSOLE_FORMS, consolesMatching, type ConsoleForm } from "../../console-forms";
import { useTv, useInputHandler } from "../context";
import { isInstalled, type TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import { GameHeader } from "./GameHeader";
import { GridTab, type Back } from "./GridTab";
import { discoverApps, matchesSearch, type LibraryFilter } from "./lists";

/** Kept by the shell: Discover comes back as it was left. */
export type DiscoverState = {
  filter: LibraryFilter;
  /** The maker (the catalog's brand) and the kind the consoles are narrowed to; null is any. */
  maker: string | null;
  form: ConsoleForm | null;
  consoleId: string | null;
  /** Catalog pages loaded, per question asked of the catalog. */
  pages: Map<string, { entries: Entry[]; cursor: string | null; done: boolean; loading: boolean; error: string | null }>;
};

export const newDiscoverState = (): DiscoverState => ({ filter: "notInLibrary", maker: null, form: null, consoleId: null, pages: new Map() });

/** The most consoles the catalog takes in one question (App.tsx's MAX_SYSTEMS). */
const MAX_CONSOLES = 8;

const FORM_NAMES: Record<ConsoleForm, MessageDescriptor> = {
  home: msg({ message: "Home consoles", comment: "Discover's filter: consoles played on a TV, not handhelds" }),
  handheld: msg({ message: "Handhelds", comment: "Discover's filter: handheld consoles, such as the Game Boy or PSP" }),
};

const LIBRARY_CHIPS: { id: LibraryFilter; label: MessageDescriptor }[] = [
  { id: "notInLibrary", label: msg({ message: "Not in library", comment: "Discover's filter: games not in the library yet" }) },
  { id: "inLibrary", label: msg({ message: "In library", comment: "Discover's filter: games in the library but not installed" }) },
  { id: "all", label: msg({ message: "All", comment: "Discover's filter: games in the library or not" }) },
];

/** Discover's hint for going back down from the filters to the games. */
const games = () => t({ message: "Games", comment: "Controller hint on Discover's filters: back down to the games" });
/** And for the shoulder buttons, which step through the consoles there. */
const consolesHint = () => t({ message: "Consoles", comment: "Controller hint on Discover: the shoulder buttons go to the next or previous console" });

type Chip =
  | { kind: "library"; id: LibraryFilter; label: string }
  | { kind: "maker"; id: string | null; label: string }
  | { kind: "form"; id: ConsoleForm | null; label: string }
  | { kind: "console"; id: string | null; label: string };

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
  const [maker, setMaker] = useState(state.maker);
  const [form, setForm] = useState(state.form);
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
  const known = useMemo(() => [...consoles.ids()].map((id) => consoles.get(id)).filter((c) => !c.special).sort(consoles.compare), [consoles]);
  const shown = useMemo(() => consolesMatching(known, maker, form), [known, maker, form]);
  const narrowed = maker !== null || form !== null;
  /** The consoles the games are for: the one chosen, or those the maker and kind leave; null is any. */
  const onConsoles = useMemo(() => (consoleId ? new Set([consoleId]) : narrowed ? new Set(shown.map((c) => c.id)) : null), [consoleId, narrowed, shown]);
  /** What the catalog is asked: as narrow as it takes (a few consoles, or a maker's); onConsoles narrows the rest here. */
  const query = useMemo(() => {
    if (consoleId) return { console: consoleId };
    if (!narrowed) return {};
    if (shown.length <= MAX_CONSOLES) return { consoles: shown.map((c) => c.id) };
    return maker ? { console: `maker:${maker}` } : {};
  }, [consoleId, narrowed, shown, maker]);
  /** No console is both that maker's and that kind (an Xbox handheld): nothing to ask for. */
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

  const makers = useMemo(() => [...new Set(known.map((c) => c.brand))], [known]);
  const chips: Chip[] = useMemo(
    () => [
      ...LIBRARY_CHIPS.map((c) => ({ kind: "library" as const, id: c.id, label: i18n._(c.label) })),
      { kind: "maker" as const, id: null, label: t({ message: "All makers", comment: "Discover's filter: consoles by every maker" }) },
      ...makers.map((brand) => ({ kind: "maker" as const, id: brand, label: brand === "OtherPlatforms" ? t`Other platforms` : brand })),
      { kind: "form" as const, id: null, label: t({ message: "Home & handheld", comment: "Discover's filter: home consoles and handhelds alike" }) },
      ...CONSOLE_FORMS.map((f) => ({ kind: "form" as const, id: f, label: i18n._(FORM_NAMES[f]) })),
      { kind: "console" as const, id: null, label: t`All consoles` },
      ...shown.map((c) => ({ kind: "console" as const, id: c.id, label: c.short })),
    ],
    [makers, shown],
  );
  const [chip, setChip] = useState(0);
  const chosen = (c: Chip) =>
    c.kind === "library" ? c.id === filter : c.kind === "maker" ? c.id === maker : c.kind === "form" ? c.id === form : c.id === consoleId;
  /** To a chip: a console applies as soon as it's reached; the other filters wait for A. */
  const toChip = (i: number) => {
    const next = Math.min(chips.length - 1, Math.max(0, i));
    setChip(next);
    if (chips[next].kind === "console") choose(chips[next]);
  };
  /** LB / RB: the next or previous of the consoles the filters leave, after All, round from the last to All. */
  const stepConsole = (by: number) => {
    const ids = [null, ...shown.map((c) => c.id)];
    const next = ids[(Math.max(0, ids.indexOf(consoleId)) + by + ids.length) % ids.length];
    setConsoleId((state.consoleId = next));
    if (zone === "filters") setChip(chips.findIndex((c) => c.kind === "console" && c.id === next));
  };
  const shoulders = (action: InputAction) => {
    if (action !== "tabPrev" && action !== "tabNext") return false;
    stepConsole(action === "tabPrev" ? -1 : 1);
    return true;
  };
  const gridGlobal = (action: InputAction) => shoulders(action) || onGlobal(action);
  /** A maker or a kind: the console shown stays only if it's still among those left. */
  const narrow = (nextMaker: string | null, nextForm: ConsoleForm | null) => {
    setMaker((state.maker = nextMaker));
    setForm((state.form = nextForm));
    if (consoleId && !consolesMatching(known, nextMaker, nextForm).some((c) => c.id === consoleId)) setConsoleId((state.consoleId = null));
  };
  const choose = (c: Chip) => {
    if (c.kind === "library") setFilter((state.filter = c.id));
    else if (c.kind === "maker") narrow(c.id, form);
    else if (c.kind === "form") narrow(maker, c.id);
    else setConsoleId((state.consoleId = c.id));
  };

  useInputHandler(
    {
      onAction(action) {
        if (shoulders(action) || onGlobal(action)) return;
        if (action === "left") toChip(chip - 1);
        else if (action === "right") toChip(chip + 1);
        else if (action === "accept") {
          choose(chips[chip]);
          setZone("grid");
        } else if (action === "down" || action === "back") setZone("grid");
      },
      hints: () => [
        ...(chips[chip]?.kind !== "console"
          ? [{ action: "accept" as const, label: t({ message: "Show these", comment: "Controller hint on Discover's filters: shows the games the focused filter picks" }) }]
          : [{ action: "accept" as const, label: games() }]),
        { action: "tabNext", label: consolesHint() },
        { action: "back", label: games() },
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
  /** What the grid shows: each its own place to come back to. */
  const view = `${filter}:${maker ?? "*"}:${form ?? "*"}:${consoleId ?? "*"}|${search}`;
  const emptyText = none
    ? { title: t`No consoles are both`, text: t`Choose another maker, or Home & handheld.` }
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
      <div className={`tv-chip-row${zone === "filters" ? " active" : ""}`} role="toolbar" aria-label={t`Filters`}>
        {chips.map((c, i) => (
          <button
            key={`${c.kind}:${c.id}`}
            type="button"
            className={`tv-filter${chosen(c) ? " chosen" : ""}${zone === "filters" && i === chip ? " focused" : ""}${i > 0 && chips[i - 1].kind !== c.kind ? " split" : ""}`}
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
        active={zone === "grid"}
        onEdge={(a) => a === "up" && setZone("filters")}
        onNearEnd={filter !== "inLibrary" ? loadMore : undefined}
        onPointerFocus={() => setZone("grid")}
        empty={emptyText}
        moreHints={[{ action: "tabNext", label: consolesHint() }]}
      />
    </div>
  );
}
