/**
 * The library: tabs (All, Installed, Not installed, Updates, Hidden and the
 * player's playlists) over a search, a sort, the catalog's filters and optional
 * sections by the original console. A playlist holds apps picked by hand,
 * saved filters, or both; one can be shared on quiverlauncher.com, and a
 * playlist someone shared can be followed. The Sidebar layout shows the same
 * library as a list down the left, with the app picked in it open beside it.
 */
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { ChevronDown, ChevronsDownUp, ChevronsUpDown, Clock, CloudDownload, Compass, HardDrive, LayoutGrid, Library, ListPlus, MoreHorizontal, PanelLeft, Play, Plus, Search, SlidersHorizontal, X } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Plural, Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import { listSlug, listUrl, type Entry, type SharedList } from "@quiverlauncher/api";
import { PlatformIcons, SourcedImage, heroSources, tagLabel } from "@quiverlauncher/ui";
import { hasUpdate, useLauncher, type LibraryItem } from "./store";
import { haloMoved, pageArea } from "./spatial";
import { jumpLetters, letterOf } from "./tv-library";
import { LetterIndex } from "./letter-index";
import { versionLabel } from "./versions";
import { useAccount } from "./account";
import { hasFilters, inCollection, matches, type AppFacts, type Collection } from "./sync";
import { isOwn } from "./custom";
import { LIBRARY_SORTS, compareApps, isLibrarySort, type LibrarySort } from "./sort";
import { keepsTab, keptFilters, type LibraryFilters } from "./view";
import { track } from "./telemetry";
import { CardArt, GroupBySelect, PlatformChoice, ViewOptions, useLibraryView, usePlatforms, useSidebarFits } from "./library-view";
import { PLATFORMS, forPlatforms } from "./platforms";
import { gridGroupBy, isStack, sections, stacks, type Stack } from "./group";
import { FiltersPanel, SidebarLayout, SidebarList } from "./library-sidebar";
import { byWhen } from "./sidebar";
import { GameTile, LibraryTile } from "./library-tile";
import { closesOutside } from "./overlay";
import { boxZoom, windowBox, zoom } from "./zoom";

type Tab = "all" | "installed" | "not-installed" | "updates" | "hidden" | string;
/** The library's filters, as the catalog's: one choice of each. */
type Filters = LibraryFilters;

type Overrides = { name?: string; cover?: string; consoles?: string[] };
// Made once for an entry and its overrides, so a card given it keeps it till either changes (LibraryTile's memo).
const overridden = new WeakMap<Entry, WeakMap<Overrides, Entry>>();
/** The catalog entry as this player named and pictured it. */
export function withOverrides(entry: Entry, o?: Overrides): Entry {
  if (!o) return entry;
  let mine = overridden.get(entry);
  if (!mine) overridden.set(entry, (mine = new WeakMap()));
  let made = mine.get(o);
  if (!made)
    mine.set(
      o,
      (made = {
        ...entry,
        ...(o.name ? { projectName: o.name } : {}),
        ...(o.consoles ? { consoles: o.consoles } : {}),
        ...(o.cover ? { libraryArt: { ...entry.libraryArt, header: o.cover, hero: o.cover } } : {}),
      }),
    );
  return made;
}

export const newKey = () => `c_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
/** A playlist's filters from the library's, and back. */
const toPlaylist = (f: Filters) => ({
  tags: f.tag ? [f.tag] : [],
  consoles: f.console ? [f.console] : [],
  projectTypes: f.projectType ? [f.projectType] : undefined,
  ai: f.ai,
  installed: f.installed,
});
const fromPlaylist = (c: Collection): Filters => ({
  tag: c.tags[0],
  console: c.consoles[0],
  projectType: c.projectTypes?.[0],
  ai: c.ai,
  installed: c.installed,
});
const PROJECT_TYPES: Record<string, MessageDescriptor> = {
  port: msg({ message: "Port", comment: "Project type: a game ported to another platform" }),
  tool: msg({ message: "Tool", comment: "Project type" }),
  emulator: msg({ message: "Emulator", comment: "Project type" }),
  game: msg({ message: "Standalone game", comment: "Project type" }),
};
const AI: Record<string, MessageDescriptor> = { "no-generated": msg`Hide mostly AI-generated`, "no-ai": msg`Hide any AI use` };

export function LibraryPage({
  onOpen,
  onBrowse,
  onAdd,
  onSignIn,
  downloads,
  action,
  page,
}: {
  onOpen: (e: Entry) => void;
  onBrowse: () => void;
  /** Adds an app the catalog doesn't have. */
  onAdd: () => void;
  onSignIn: () => void;
  /** The way to Downloads at the foot of the Sidebar layout. */
  downloads?: ReactNode;
  action: (entry: Entry) => ReactNode;
  /** An app's page, beside the sidebar in the Sidebar layout. */
  page?: (entry: Entry) => ReactNode;
}) {
  const { client, library: all, catalog, installs, jobs, get, add, settings, setSettings, collections, saveCollection, consoles, consoleNames, cache } = useLauncher();
  const { view, groupBy } = useLibraryView();
  // The cards' Get or Play and opening, the same functions every render, so a card is drawn again only as its app changes.
  const latest = useRef({ action, onOpen });
  latest.current = { action, onOpen };
  const cardAction = useCallback((entry: Entry) => latest.current.action(entry), []);
  const cardOpen = useCallback((entry: Entry) => latest.current.onOpen(entry), []);
  // Sections folded away while the library is open: those named, or (`all`) every one but those named, sections
  // that show later included. Grouped another way, the library starts with every section folded, so the player opens
  // the one they want rather than scrolling past the rest.
  const [folds, setFolds] = useState({ by: groupBy, all: false, names: new Set<string>() });
  if (folds.by !== groupBy) setFolds({ by: groupBy, all: Boolean(groupBy), names: new Set() });
  // Not the list without a heading, which would have no way to open again.
  const folded = { has: (name: string) => name !== "" && folds.all !== folds.names.has(name) };
  const fold = (name: string) => setFolds((f) => ({ ...f, names: new Set(f.names.has(name) ? [...f.names].filter((n) => n !== name) : [...f.names, name]) }));
  // One button folds every section away, or opens them all once they're all folded.
  const foldAll = (names: string[]) => {
    const all = names.length > 0 && names.every((n) => folded.has(n));
    return (
      <button
        type="button"
        className="tool icon-only fold-all"
        aria-label={all ? t`Expand all sections` : t`Collapse all sections`}
        title={all ? t`Expand all sections` : t`Collapse all sections`}
        disabled={!names.length}
        onClick={() => setFolds({ by: groupBy, all: !all, names: new Set() })}
      >
        {all ? <ChevronsUpDown size={16} /> : <ChevronsDownUp size={16} />}
      </button>
    );
  };
  // The Sidebar layout, where the window is wide enough for it; a grid where it isn't.
  const fits = useSidebarFits();
  const sidebar = view.layout === "sidebar" && fits && Boolean(page);
  // The app open beside the sidebar; the first one until the player picks one.
  const [selected, setSelected] = useState<string>();
  const { user, unshareList } = useAccount();
  // The tab and filters as the player left them, across Browse and restarts (kept below).
  const [tab, setTab] = useState<Tab>(() => (keepsTab(settings.libraryTab, collections) ? settings.libraryTab! : "all"));
  const [editing, setEditing] = useState<Collection | null>(null);
  const [sharing, setSharing] = useState<Collection | null>(null);
  const [following, setFollowing] = useState(false);
  const [search, setSearch] = useState("");
  // Kept with this computer's other preferences.
  const sort: LibrarySort = isLibrarySort(settings.librarySort) ? settings.librarySort : "played";
  const setSort = (librarySort: LibrarySort) => setSettings({ ...settings, librarySort: librarySort === "played" ? undefined : librarySort });
  const [filters, setFilters] = useState<Filters>(() => keptFilters(settings.libraryFilters));
  const [filtersOpen, setFiltersOpen] = useState(false);
  // Kept with this computer's other preferences once they settle: two changes at once (a playlist's filters cleared as it opens) save together.
  useEffect(() => {
    const libraryTab = keepsTab(tab, collections) ? tab : undefined;
    const set = Object.fromEntries(Object.entries(filters).filter(([, value]) => value));
    const libraryFilters = Object.keys(set).length ? set : undefined;
    if (libraryTab === settings.libraryTab && JSON.stringify(libraryFilters) === JSON.stringify(settings.libraryFilters)) return;
    setSettings({ ...settings, libraryTab, libraryFilters });
  }, [tab, filters]); // eslint-disable-line react-hooks/exhaustive-deps
  const { platforms, picked: pickedPlatforms } = usePlatforms();
  // Every platform's apps, until the library is left.
  const [everywhere, setEverywhere] = useState(false);
  // A mouse wheel scrolls the tab row sideways when it has more tabs than fit.
  const tabRow = useRef<HTMLElement>(null);
  useEffect(() => {
    const row = tabRow.current;
    if (!row) return;
    const wheel = (e: WheelEvent) => {
      if (row.scrollWidth <= row.clientWidth || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
      e.preventDefault();
      row.scrollLeft += e.deltaY;
    };
    row.addEventListener("wheel", wheel, { passive: false });
    return () => row.removeEventListener("wheel", wheel);
  });
  // The tabs share the tools' row only while every tab fits beside them; otherwise they fold into a menu at the
  // tools' start, rather than take a row of their own from a short screen (a Steam Deck's).
  const bar = useRef<HTMLDivElement>(null);
  // The grid, when it's one by name: the letters beside it go to its apps.
  const letterGrid = useRef<HTMLDivElement>(null);
  const [stacked, setStacked] = useState(false);
  const [narrow, setNarrow] = useState(false);
  // How wide the tabs are, measured while they show, for whether they'd fit once folded.
  const tabsWide = useRef(0);
  const fitTabs = () => {
    const row = tabRow.current;
    const tools = bar.current?.querySelector<HTMLElement>(".library-tools");
    if (!bar.current || !tools) return;
    const across = (el: HTMLElement) => {
      const items = ([...el.children] as HTMLElement[]).filter((item) => !item.matches(".library-show"));
      return items.length ? items[items.length - 1].offsetLeft + items[items.length - 1].offsetWidth - items[0].offsetLeft : 0;
    };
    if (row) tabsWide.current = across(row);
    const room = bar.current.clientWidth;
    // The gap between them (.library-bar's), and the room the search grows into when it has focus, so focusing it
    // never moves the tabs. The search measured at its width beside the tabs, 240 (app.css): grown with the focus,
    // focusing it folded the tabs away from the d-pad on its way to them, and stretched across a folded row, the
    // tabs never came back. At 1040 or less the tools have a row of their own anyway.
    const search = tools.querySelector<HTMLElement>(".library-search");
    const tools240 = across(tools) - (search ? search.offsetWidth - 240 : 0);
    setStacked(room <= 1040 || tabsWide.current + tools240 + 20 + 80 > room);
    // As app.css's @container library-bar (max-width: 520px): a phone, where Group by goes into View options.
    setNarrow(room <= 520);
  };
  useEffect(fitTabs);
  useEffect(() => {
    if (!bar.current || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(() => fitTabs());
    observer.observe(bar.current);
    return () => observer.disconnect();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const hidden = new Set(settings.hidden);
  const collection = collections.find((c) => c.key === tab);
  const makers = Object.fromEntries(consoles.map((c) => [c.id, c.brand]));
  // A followed playlist's apps come from the shared playlist on the site: undefined while loading, null once it's not shared, "failed" offline.
  const [list, setList] = useState<SharedList | null | undefined | "failed">();
  useEffect(() => {
    setList(undefined);
    if (!collection?.follows) return;
    let live = true;
    client.sharedList(collection.follows).then(
      (l) => live && setList(l),
      () => live && setList("failed"),
    );
    return () => void (live = false);
  }, [client, collection?.follows]);
  const loaded = list && list !== "failed" ? list : undefined;
  const shown = all.filter((i) => hidden.has(i.id) === (tab === "hidden"));
  const updates = shown.filter((i) => !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id]));
  const words = search.toLowerCase().split(/\s+/).filter(Boolean);
  const inLibrary = new Map(all.map((i) => [i.id, i]));
  const facts = (entry: Entry, item?: LibraryItem): AppFacts => ({
    id: entry.id,
    tags: [...entry.tags, ...(item?.overrides?.tags ?? [])],
    consoles: entry.consoles ?? [],
    installed: Boolean(installs[entry.id]),
    // Apps the player added match only the filters there's data for.
    projectType: isOwn(entry.id) ? undefined : entry.projectType,
    aiLevel: entry.aiLevel,
    names: [entry.projectName, ...entry.games.map((g) => g.title)],
  });
  const sortFacts = (e: Entry) => ({ lastPlayed: installs[e.id]?.lastPlayed, installedAt: installs[e.id]?.installedAt, addedAt: inLibrary.get(e.id)?.addedAt, installed: Boolean(installs[e.id]) });
  // What the tab holds before the search and filters: library apps, plus picked or listed apps not in the library.
  const candidates: { entry: Entry; item?: LibraryItem }[] = collection?.follows
    ? (loaded?.items ?? []).flatMap((i) => {
        if (i.kind !== "entry") return [];
        const item = inLibrary.get(i.entry.id);
        return [{ entry: item && catalog[item.id] ? withOverrides(catalog[item.id], item.overrides) : i.entry, item }];
      })
    : [
        ...shown.flatMap((i) => (catalog[i.id] ? [{ entry: withOverrides(catalog[i.id], i.overrides), item: i }] : [])),
        ...(collection?.apps ?? []).flatMap((id) => (!inLibrary.has(id) && catalog[id] ? [{ entry: catalog[id] }] : [])),
      ];
  // The player's own tags, to filter on as well as the catalog's.
  const ownTags = [...new Set(candidates.flatMap((c) => c.item?.overrides?.tags ?? []))];
  const inTab = (entry: Entry, item?: LibraryItem) => {
    if (collection?.follows) return true;
    const installed = Boolean(installs[entry.id]);
    if (tab === "installed" || tab === "not-installed") return installed === (tab === "installed");
    if (tab === "updates") return Boolean(item && updates.includes(item));
    if (!collection) return Boolean(item);
    return inCollection(collection, facts(entry, item), makers);
  };
  const filtering = Object.values(filters).some(Boolean);
  const matching = candidates.filter(({ entry, item }) => {
    const text = [entry.projectName, ...entry.games.map((g) => g.title), ...entry.tags, ...(item?.overrides?.tags ?? [])].join(" ").toLowerCase();
    return words.every((w) => text.includes(w)) && inTab(entry, item) && (!filtering || matches(toPlaylist(filters), facts(entry, item), makers));
  });
  // Apps for other platforms than the ones picked here, unless installed here; shown for now when the player asks.
  const elsewhere = new Set(matching.flatMap(({ entry }) => (!installs[entry.id] && !forPlatforms(entry, platforms) ? [entry.id] : [])));
  // How many of them each platform not picked would show, by it in the platforms' menu.
  const hiddenFor = Object.fromEntries(
    PLATFORMS.filter((p) => !platforms.includes(p)).map((p) => [p, matching.filter(({ entry }) => elsewhere.has(entry.id) && forPlatforms(entry, [p])).length]),
  );
  // A grid of games is by the games' names, where it would be by the apps'; its letters too.
  const order: LibrarySort = !sidebar && view.byGame && sort === "name" ? "game" : sort;
  const items = matching
    .filter(({ entry }) => everywhere || !elsewhere.has(entry.id))
    .sort((a, b) => compareApps(order, a.entry, b.entry, sortFacts))
    .map(({ entry }) => entry);
  // The platforms say so while they hide apps, or are the player's own pick, or are set aside for now.
  const platformMenu = (elsewhere.size > 0 || everywhere || pickedPlatforms) && (
    <Popover
      label={everywhere || !elsewhere.size ? t`Platforms` : plural(elsewhere.size, { one: "Platforms: # app hidden", other: "Platforms: # apps hidden" })}
      icon={
        <>
          <PlatformIcons os={platforms} />
          {everywhere ? <Trans comment="The library's platform menu while it shows every platform's apps">All</Trans> : elsewhere.size > 0 && <span className="tool-count">{elsewhere.size}</span>}
        </>
      }
      role="group"
      className={`tool platform-filter${!everywhere && elsewhere.size ? " on" : ""}`}
      panel="view-panel platform-panel"
      align="end"
    >
      {() => <PlatformChoice onChange={() => setEverywhere(false)} hidden={everywhere ? undefined : hiddenFor} />}
    </Popover>
  );
  const platformNote = elsewhere.size > 0 && (
    <p className="library-note platform-note muted">
      {everywhere ? (
        <Plural value={elsewhere.size} one="Showing # app for other platforms." other="Showing # apps for other platforms." />
      ) : (
        <Plural value={elsewhere.size} one="# app for other platforms isn't shown." other="# apps for other platforms aren't shown." />
      )}
      <button type="button" className="text-button" onClick={() => setEverywhere(!everywhere)}>
        {everywhere ? <Trans comment="Hides the library's apps for other platforms again">Hide them</Trans> : <Trans comment="Shows the library's apps for other platforms, until the library is left">Show them</Trans>}
      </button>
    </p>
  );
  // Removed from the library (here, elsewhere or by signing out) but its files are still here.
  const loose = Object.keys(installs).flatMap((id) => (catalog[id] && !all.some((i) => i.id === id) ? [catalog[id]] : []));
  // Their section's heading, also its key among the sidebar's groups.
  const LOOSE = t`Installed, not in your library`;
  // Apps installed here but not in the library (another account's, say), added at once.
  const addAll = (className?: string, text = t`Add all`) => (
    <button type="button" className={className} onClick={() => loose.forEach((entry) => add(entry))}>
      <Plus size={14} /> {text}
    </button>
  );
  const dialogs = (
    <>
      {editing && (
        <PlaylistEditor
          collection={editing}
          library={candidates.map((c) => c.entry)}
          ownTags={ownTags}
          on={sharedEntries}
          onClose={(saved) => {
            setEditing(null);
            if (!saved) return;
            setTab(saved.removed ? "all" : saved.key);
            // Its filters are the playlist now.
            setFilters({});
            setFiltersOpen(false);
          }}
        />
      )}
      {sharing && <ShareDialog collection={sharing} entries={sharedEntries(sharing)} onSignIn={onSignIn} onClose={() => setSharing(null)} />}
      {following && (
        <FollowDialog
          onClose={(key) => {
            setFollowing(false);
            if (key) setTab(key);
          }}
        />
      )}
    </>
  );
  /** The app whose icon a playlist's tab shows: the one chosen while it's still on it, else its first. */
  function iconOf(key: Tab) {
    const c = collections.find((c) => c.key === key);
    if (!c || c.follows) return undefined;
    const on = sharedEntries(c);
    return on.find((e) => e.id === c.icon) ?? on[0];
  }
  /** The catalog apps on a playlist now, as it shows them (not hidden ones), which sharing it puts on the shared playlist. */
  function sharedEntries(c: Collection) {
    const ids = new Set(c.apps ?? []);
    return [
      ...all.flatMap((i) =>
        catalog[i.id] && !hidden.has(i.id) && (ids.has(i.id) || (hasFilters(c) && matches(c, facts(catalog[i.id], i), makers))) ? [catalog[i.id]] : [],
      ),
      ...[...ids].flatMap((id) => (!inLibrary.has(id) && catalog[id] ? [catalog[id]] : [])),
    ];
  }
  if (!all.length && !loose.length && !collections.length)
    return (
      <div className="empty">
        <Library size={40} strokeWidth={1.2} />
        <h2>
          <Trans>Your library is empty</Trans>
        </h2>
        <p>
          <Trans comment="Get is the label of the button that installs an app">Find a port in the catalog and press Get. It's added here and downloads straight away.</Trans>
        </p>
        <div className="row">
          <button className="primary" onClick={onBrowse}>
            <Trans>Browse the catalog</Trans>
          </button>
          <button onClick={onAdd}>
            <Plus size={15} /> <Trans>Add an app</Trans>
          </button>
          <button onClick={() => setFollowing(true)}>
            <ListPlus size={15} /> <Trans>Add a shared playlist</Trans>
          </button>
        </div>
        {dialogs}
      </div>
    );
  const updateCount = updates.length;
  const hiddenCount = hidden.size;
  const tabs: [Tab, string][] = [
    ["all", t({ message: "All", comment: "Library tab: every app in the library" })],
    ["installed", t({ message: "Installed", context: "tab", comment: "Library tab" })],
    ["not-installed", t({ message: "Not installed", context: "tab", comment: "Library tab" })],
    ...(updates.length || tab === "updates" ? [["updates", t({ message: `Updates (${updateCount})`, comment: "Library tab: apps with an update" })] as [Tab, string]] : []),
    ...(hidden.size || tab === "hidden" ? [["hidden", t({ message: `Hidden (${hiddenCount})`, comment: "Library tab: apps the player hid" })] as [Tab, string]] : []),
    ...collections.map((c): [Tab, string] => [c.key, c.name]),
  ];
  const playlistName = collection?.name ?? "";
  const playlistMenu = collection && (
    <Menu
      label={t`${playlistName} options`}
      icon={<MoreHorizontal size={15} />}
      items={collection.follows
        ? [
            [
              t`Make a copy`,
              () => {
                if (!loaded) return;
                // Its apps join the library (nothing downloads), so the copy shows them on every computer.
                const entries = loaded.items.flatMap((i) => (i.kind === "entry" ? [i.entry] : []));
                cache(entries);
                entries.filter((e) => !inLibrary.has(e.id)).forEach((e) => add(e));
                const copy: Collection = { key: newKey(), name: t`${playlistName} (copy)`.slice(0, 60), tags: [], consoles: [], apps: entries.map((e) => e.id), order: collections.length };
                saveCollection(copy);
                setTab(copy.key);
              },
            ],
            [t({ message: "Remove", comment: "Removes a followed playlist from the library" }), () => (saveCollection({ ...collection, removed: true }), setTab("all"))],
          ]
        : [
            [t({ message: "Edit", comment: "Edits a playlist" }), () => setEditing(collection)],
            [collection.shared ? t`Shared playlist` : t({ message: "Share", comment: "Shares a playlist" }), () => setSharing(collection)],
            [
              collection.shared ? t`Delete (stops sharing it)` : t({ message: "Delete", comment: "Deletes a playlist" }),
              () => {
                // Deleting a shared playlist stops sharing it.
                if (collection.shared && user) void unshareList(collection.shared.slug).catch(() => {});
                saveCollection({ ...collection, removed: true });
                setTab("all");
              },
            ],
          ]}
    />
  );
  const addPlaylist = (
    <Menu
      label={t`Add a playlist`}
      icon={<Plus size={15} />}
      items={[
        [t`New playlist`, () => setEditing({ key: newKey(), name: "", tags: [], consoles: [], order: collections.length })],
        [t`Add a shared playlist`, () => setFollowing(true)],
      ]}
    />
  );
  const set = (key: keyof Filters) => (e: { target: { value: string } }) => setFilters({ ...filters, [key]: e.target.value || undefined });
  const clearFilter = (key: keyof Filters) => setFilters({ ...filters, [key]: undefined });
  const savePlaylist = () => setEditing({ key: newKey(), name: "", order: collections.length, ...toPlaylist(filters), ...(search.trim() ? { search: search.trim() } : {}) });
  const owner = loaded?.owner.name;
  const unavailable = loaded?.unavailable ?? 0;
  const notes = (
    <>
      {tab === "updates" && updates.length > 0 && (
        <div className="library-note">
          <p className="muted">
            <Plural value={updateCount} one="# app has an update." other="# apps have updates." />
          </p>
          <button className="primary" onClick={() => updates.forEach((i) => get(catalog[i.id]))}>
            <Trans>Update all ({updateCount})</Trans>
          </button>
        </div>
      )}
      {collection?.follows && loaded && (
        <p className="muted playlist-note">
          <Trans>A playlist by {owner}, kept up to date with theirs.</Trans>
          {unavailable ? <> <Plural value={unavailable} one="# of its apps left the catalog." other="# of its apps left the catalog." /></> : ""}
        </p>
      )}
      {collection?.follows && list === null && (
        <p className="muted playlist-note">
          <Trans>This playlist isn't shared any more.</Trans>
        </p>
      )}
      {collection?.follows && list === "failed" && (
        <p className="muted playlist-note">
          <Trans>Couldn't reach quiverlauncher.com to show this playlist. Check your connection.</Trans>
        </p>
      )}
    </>
  );
  const empty = collection && !search && !filtering ? (collection.follows ? t`Nothing in this playlist.` : t`No apps in this playlist yet. Add some from each app's menu, or edit its filters.`) : t`Nothing here.`;
  if (sidebar && page) {
    // Under headings by console, or by when they were played or added; else one list.
    const groups: [string, Entry[]][] = groupBy
      ? sections(items, groupBy, { names: consoleNames, makers })
      : sort === "played"
        ? byWhen(items, (e) => installs[e.id]?.lastPlayed)
        : sort === "added"
          ? byWhen(items, (e) => inLibrary.get(e.id)?.addedAt, undefined, t`Not in your library`)
          : items.length
            ? [["", items]]
            : [];
    if (loose.length && tab === "all" && !search && !filtering) groups.push([LOOSE, loose]);
    const listed = groups.flatMap(([, entries]) => entries);
    const open = listed.find((e) => e.id === selected) ?? listed[0];
    const installedOnly = filters.installed === "yes";
    return (
      <SidebarLayout
        side={
          <>
            <div className="sidebar-top">
              <div className="sidebar-row-tools">
                <span className="quiet-select sidebar-show">
                  <select aria-label={t({ message: "Show", comment: "Picks which library tab or playlist the sidebar lists" })} value={collection || tabs.some(([key]) => key === tab) ? tab : "all"} onChange={(e) => setTab(e.target.value)}>
                    {tabs.map(([key, name]) => (
                      <option key={key} value={key}>
                        {name}
                      </option>
                    ))}
                  </select>
                  <ChevronDown size={14} aria-hidden="true" />
                </span>
                {playlistMenu}
                {addPlaylist}
                {/* The way back to a grid or a list. */}
                <Popover label={t`View options`} icon={<PanelLeft size={16} />} role="group" panel="view-panel">
                  {() => <ViewOptions />}
                </Popover>
              </div>
              <div className="sidebar-row-tools page-tools">
                <label className="search library-search sidebar-search">
                  <Search size={15} />
                  <input aria-label={t`Search your library`} placeholder={t({ message: "Search", comment: "Placeholder of the library's search field" })} value={search} onChange={(e) => setSearch(e.target.value)} />
                  {search && (
                    <button type="button" className="search-clear" aria-label={t`Clear search`} onClick={() => setSearch("")}>
                      <X size={13} />
                    </button>
                  )}
                </label>
                <button
                  className={`tool icon-only${sort === "played" ? " on" : ""}`}
                  aria-pressed={sort === "played"}
                  aria-label={t`Recently played first`}
                  title={sort === "played" ? t`Recently played first (press for A to Z)` : t`Recently played first`}
                  // A to Z by the name the sidebar shows.
                  onClick={() => setSort(sort === "played" ? (view.nameOrder === "game" ? "game" : "name") : "played")}
                >
                  <Clock size={16} />
                </button>
                <button
                  className={`tool icon-only${installedOnly ? " on" : ""}`}
                  aria-pressed={installedOnly}
                  aria-label={t`Installed only`}
                  title={t`Installed only`}
                  onClick={() => setFilters({ ...filters, installed: installedOnly ? undefined : "yes" })}
                >
                  <HardDrive size={16} />
                </button>
                {platformMenu}
                <button
                  className={`tool icon-only${filtersOpen || filtering ? " on" : ""}`}
                  aria-label={t`Filters`}
                  title={t`Filters`}
                  aria-expanded={filtersOpen}
                  aria-controls="library-filters"
                  onClick={() => setFiltersOpen(!filtersOpen)}
                >
                  <SlidersHorizontal size={16} />
                </button>
              </div>
              {/* The filters on, each removable, while the panel is away. */}
              {filtering && !filtersOpen && (
                <div className="filter-bar sidebar-pills" role="group" aria-label={t`Filters on`}>
                  <FilterFields value={filters} set={set} entries={candidates.map((c) => c.entry)} ownTags={ownTags} pills={{ all: false, clear: clearFilter }} />
                </div>
              )}
              {filtersOpen && (
                <FiltersPanel onClose={() => setFiltersOpen(false)}>
                  <div className="filter-bar">
                    <FilterFields value={filters} set={set} entries={candidates.map((c) => c.entry)} ownTags={ownTags} pills={{ all: true, clear: clearFilter }} />
                  </div>
                  <div className="filter-bar sidebar-filters-foot">
                    <span className="view-label">
                      <Trans comment="Label of the library's sort order">Sort</Trans>
                    </span>
                    <span className="quiet-select">
                      <select aria-label={t({ message: "Sort", comment: "Label of the library's sort order" })} value={sort} onChange={(e) => setSort(e.target.value as LibrarySort)}>
                        {LIBRARY_SORTS.map(([value, label]) => (
                          <option key={value} value={value}>
                            {i18n._(label)}
                          </option>
                        ))}
                      </select>
                      <ChevronDown size={14} aria-hidden="true" />
                    </span>
                  </div>
                  <div className="filter-bar sidebar-filters-foot">
                    <span className="view-label">
                      <Trans comment="Label of how the library is split into sections (by console, say)">Sections</Trans>
                    </span>
                    <GroupBySelect />
                    {foldAll(groups.map(([heading]) => heading).filter(Boolean))}
                  </div>
                  <div className="filter-bar sidebar-filters-foot">
                    {/* A playlist of whatever matches these filters, now and later. */}
                    <button type="button" className="save-playlist" disabled={!filtering && !search.trim()} onClick={savePlaylist}>
                      <ListPlus size={14} /> <Trans>Save as playlist</Trans>
                    </button>
                    {filtering && (
                      <button type="button" className="text-button" onClick={() => setFilters({})}>
                        <Trans>Clear all</Trans>
                      </button>
                    )}
                  </div>
                </FiltersPanel>
              )}
              {/* No line for the apps for other platforms: the platforms' menu says how many, by each. */}
              {notes}
            </div>
            {listed.length ? <SidebarList groups={groups} folded={folded} onFold={fold} selected={open?.id} onSelect={(e) => (setSelected(e.id), pageArea().scrollTo(0, 0))} actions={{ [LOOSE]: addAll("sidebar-group-action", t`Add all to library`) }} withMenu={(e) => inLibrary.has(e.id)} /> : <p className="sidebar-empty muted">{empty}</p>}
            <div className="sidebar-foot">
              <button type="button" className="sidebar-add" onClick={onAdd}>
                <Plus size={15} /> <Trans>Add an app</Trans>
              </button>
              {downloads}
            </div>
          </>
        }
      >
        {open ? <div key={open.id}>{page(open)}</div> : <p className="empty">{empty}</p>}
        {dialogs}
      </SidebarLayout>
    );
  }
  // The grid's sections, when it has them: by game only in the sidebar.
  const shelvesBy = gridGroupBy(groupBy);
  const shelves = shelvesBy ? sections(items, shelvesBy, { names: consoleNames, makers }) : [];
  // The app played last, whatever the tab, search or filters, so the page doesn't jump as they change; unless turned off.
  const played = view.continuePlaying && all
    .filter((i) => !hidden.has(i.id) && catalog[i.id] && installs[i.id]?.lastPlayed)
    .sort((a, b) => installs[b.id].lastPlayed! - installs[a.id].lastPlayed!)[0];
  const resume = played && withOverrides(catalog[played.id], played.overrides);
  // By name or by game, and not in sections: the letters the apps file under, for the triggers and the index.
  const byLetter = !shelvesBy && (order === "name" || order === "game");
  const letterFor = (e: Entry) => letterOf(order === "game" ? (e.games[0]?.title ?? e.projectName) : e.projectName);
  const letters = byLetter ? jumpLetters(items, order).map((l) => l.letter) : [];
  const card = (entry: Entry | Stack) =>
    isStack(entry) ? (
      <GameTile
        key={`game:${entry.game.id}`}
        stack={entry}
        letter={byLetter ? letterOf(entry.game.title) : undefined}
        image={view.image}
        size={view.size}
        names={view.names}
        marks={view.marks}
        inLibrary={(id) => inLibrary.has(id)}
        action={cardAction}
        onOpen={cardOpen}
      />
    ) : (
      <LibraryTile
        key={entry.id}
        entry={entry}
        letter={byLetter ? letterFor(entry) : undefined}
        image={view.image}
        size={view.size}
        names={view.names}
        nameOrder={view.nameOrder}
        marks={view.marks}
        inLibrary={inLibrary.has(entry.id)}
        action={cardAction}
        onOpen={cardOpen}
      />
    );
  // The player's view: the cards' picture and size, names or not. A sidebar that doesn't fit is a grid.
  const gridClass = `catalog-grid library-grid layout-grid image-${view.image} size-${view.size}${view.names ? "" : " no-names"}`;
  // The triggers jump through a grid: a letter at a time by name, else a screen. A card for each game, or each app.
  const grid = (entries: Entry[]) => (
    <div className={gridClass} data-jump={byLetter ? "letters" : "pages"} ref={byLetter ? letterGrid : undefined}>
      {(view.byGame ? stacks(entries, sortFacts) : entries).map(card)}
    </div>
  );
  const on = Object.values(filters).filter(Boolean).length;
  // An empty library over apps still installed here: their section says it all.
  const nothing = items.length === 0 && !(loose.length && tab === "all" && !search && !filtering);
  return (
    <>
      {resume && <ContinuePlaying entry={resume} played={installs[resume.id].lastPlayed!} onOpen={() => onOpen(resume)} action={action(resume)} />}
      <div ref={bar} className={`library-bar${stacked ? " stacked" : ""}`}>
        {!stacked && (
          <nav ref={tabRow} className="playlists" aria-label={t`Playlists`}>
            {tabs.map(([key, name]) => (
              <span key={key} className="playlist-tab">
                <button className={tab === key ? "active" : ""} aria-pressed={tab === key} onClick={() => setTab(key)}>
                  <PlaylistIcon entry={iconOf(key)} />
                  {name}
                </button>
                {/* The open playlist's menu: edit, share or delete it; a followed one is copied or removed. */}
                {tab === key && collection && playlistMenu}
              </span>
            ))}
            {addPlaylist}
            {collections.length > 6 && (
              <select aria-label={t`All playlists`} value={collection ? tab : ""} onChange={(e) => e.target.value && setTab(e.target.value)}>
                <option value="">{t({ message: "More…", comment: "Picks a playlist that isn't among the tabs" })}</option>
                {collections.map((c) => (
                  <option key={c.key} value={c.key}>
                    {c.name}
                  </option>
                ))}
              </select>
            )}
          </nav>
        )}
        <div className="library-tools page-tools">
          {/* The tabs, folded: every tab and playlist in one menu, the open playlist's menu and Add a playlist beside it. */}
          {stacked && (
            <span className="library-show">
              <span className="quiet-select">
                <select aria-label={t({ message: "Show", comment: "Picks which library tab or playlist the library shows, where the tabs don't fit" })} value={collection || tabs.some(([key]) => key === tab) ? tab : "all"} onChange={(e) => setTab(e.target.value)}>
                  {tabs.map(([key, name]) => (
                    <option key={key} value={key}>
                      {name}
                    </option>
                  ))}
                </select>
                <ChevronDown size={14} aria-hidden="true" />
              </span>
              {playlistMenu}
              {addPlaylist}
            </span>
          )}
          <label className="search library-search">
            <Search size={15} />
            <input aria-label={t`Search your library`} placeholder={t`Search your library`} value={search} onChange={(e) => setSearch(e.target.value)} />
            {search && (
              <button type="button" className="search-clear" aria-label={t`Clear search`} onClick={() => setSearch("")}>
                <X size={13} />
              </button>
            )}
          </label>
          <span className="quiet-select library-sort">
            <select aria-label={t({ message: "Sort", comment: "Label of the library's sort order" })} value={sort} onChange={(e) => setSort(e.target.value as LibrarySort)}>
              {LIBRARY_SORTS.map(([value, label]) => (
                <option key={value} value={value}>
                  {i18n._(label)}
                </option>
              ))}
            </select>
            <ChevronDown size={14} aria-hidden="true" />
          </span>
          {!narrow && <GroupBySelect grid />}
          {shelvesBy && foldAll(shelves.map(([name]) => name))}
          {platformMenu}
          {/* On a phone, Filters and Add an app are icons (their names kept for a screen reader). */}
          <button
            className={`tool filters-tool${filtersOpen || filtering ? " on" : ""}`}
            aria-expanded={filtersOpen}
            aria-controls="library-filters"
            onClick={() => setFiltersOpen(!filtersOpen)}
          >
            <SlidersHorizontal size={15} />{" "}
            <span className="tool-label">
              <Trans>Filters</Trans>
            </span>
            {filtering && <span className="tool-count">{on}</span>}
          </button>
          <Popover label={t`View options`} icon={<LayoutGrid size={17} />} role="group" className="tool icon-only" panel="view-panel" align="end">
            {() => <ViewOptions groups={narrow} />}
          </Popover>
          <button className="primary add-app" onClick={onAdd}>
            <Plus size={15} />{" "}
            <span className="tool-label">
              <Trans>Add an app</Trans>
            </span>
          </button>
        </div>
      </div>
      {/* Filters: pills to pick from while open; the ones on stay, each removable, while it's closed. */}
      {(filtersOpen || filtering) && (
        <div id="library-filters" className="filter-bar" role="group" aria-label={t`Library filters`}>
          <FilterFields
            value={filters}
            set={set}
            entries={candidates.map((c) => c.entry)}
            ownTags={ownTags}
            pills={{ all: filtersOpen, clear: clearFilter }}
          />
          {filtering && (
            <span className="filter-actions">
              <button type="button" className="text-button" onClick={() => setFilters({})}>
                <Trans>Clear all</Trans>
              </button>
              <button
                type="button"
                className="save-playlist"
                onClick={savePlaylist}
              >
                <ListPlus size={14} /> <Trans>Save as playlist</Trans>
              </button>
            </span>
          )}
        </div>
      )}
      {notes}
      {/* Empty but for other platforms' apps: the way to them under it. */}
      {nothing && (
        <div className="empty">
          <p>{empty}</p>
          {platformNote}
        </div>
      )}
      {shelvesBy
        ? shelves.map(([name, entries]) => (
            <section key={name} className="console-section" aria-label={name}>
              {/* Its heading folds its apps away, and back. */}
              <h2>
                <button type="button" className="section-fold" aria-expanded={!folded.has(name)} onClick={() => fold(name)}>
                  {name}
                  <ChevronDown size={18} aria-hidden="true" />
                </button>
              </h2>
              {!folded.has(name) && grid(entries)}
            </section>
          ))
        : byLetter && letters.length > 1
          ? (
              <div className="letters-wrap">
                {grid(items)}
                <LetterIndex grid={letterGrid} letters={letters} />
              </div>
            )
          : grid(items)}
      {!nothing && platformNote}
      {loose.length > 0 && (
        <section className="loose">
          <header className="loose-heading">
            <h2>{LOOSE}</h2>
            {addAll()}
          </header>
          {/*
            The library's own cards, in its view, each one stop for the d-pad: Add in Play's place, which Y presses, and
            A its page. Uninstalling is on that page, as for any app.
          */}
          <div className={gridClass}>
            {loose.map((entry) => (
              <LibraryTile
                key={entry.id}
                entry={entry}
                image={view.image}
                size={view.size}
                names={view.names}
                nameOrder={view.nameOrder}
                marks={view.marks}
                inLibrary={false}
                onAdd={add}
                action={cardAction}
                onOpen={cardOpen}
              />
            ))}
          </div>
        </section>
      )}
      {dialogs}
    </>
  );
}

const DAY = 86_400_000;
/** "Played today", "Played yesterday", "Played 3 days ago": as the catalog's relative times, in whole sentences to translate. */
const playedAgo = (time: number, now = Date.now()) => {
  const days = Math.floor(Math.max(0, now - time) / DAY);
  if (days < 1) return t`Played today`;
  if (days === 1) return t`Played yesterday`;
  if (days < 7) return t`${plural(days, { one: "Played # day ago", other: "Played # days ago" })}`;
  if (days < 30) {
    const weeks = Math.floor(days / 7);
    return t({ message: plural(weeks, { one: "Played # wk ago", other: "Played # wk ago" }), comment: "wk: weeks, short" });
  }
  if (days < 365) {
    const months = Math.floor(days / 30);
    return t({ message: plural(months, { one: "Played # mo ago", other: "Played # mo ago" }), comment: "mo: months, short" });
  }
  const years = Math.floor(days / 365);
  return t({ message: plural(years, { one: "Played # yr ago", other: "Played # yrs ago" }), comment: "yr: years, short" });
};

/**
 * What this computer has of an app, for the line under its card: its state (app.css colours some) and its words. A
 * download's progress is in the card's button; this only says one is under way.
 */
export function copyOf(c: { busy?: "downloading" | "installing"; update: boolean; installed: boolean; lastPlayed?: number; inLibrary: boolean }) {
  if (c.busy) return { state: "busy", text: c.busy === "installing" ? t`Installing` : t`Downloading` };
  if (c.update) return { state: "update", text: t`Update available` };
  if (c.lastPlayed) return { state: "played", text: playedAgo(c.lastPlayed) };
  if (c.installed) return { state: "ready", text: t`Installed, not played yet` };
  return { state: "missing", text: c.inLibrary ? t`Not installed` : t`Not in your library` };
}

/** The line under a library card's name: what this computer has of the app. */
export function CopyStatus({ entry, inLibrary }: { entry: Entry; inLibrary: boolean }) {
  const { installs, jobs, catalog } = useLauncher();
  const install = installs[entry.id];
  const job = jobs[entry.id];
  const { state, text } = copyOf({
    busy: job && !("error" in job) ? job.phase : undefined,
    update: Boolean(install && hasUpdate(catalog[entry.id] ?? entry, install)),
    installed: Boolean(install),
    lastPlayed: install?.lastPlayed,
    inLibrary,
  });
  return <span data-state={state}>{text}</span>;
}

/** The app played last, large on its art, with its Play button: the top of the library. */
/**
 * The app played last, large on its art: when, its name and version, and its buttons. Play; Update beside it when
 * there's one; and its page in the catalog. A download under way, or one that went wrong, has the card's usual button.
 */
function ContinuePlaying({ entry, played, onOpen, action }: { entry: Entry; played: number; onOpen: () => void; action: ReactNode }) {
  const { installs, jobs, catalog, play, get } = useLauncher();
  const art = entry.libraryArt?.hero || entry.libraryArt?.header;
  const install = installs[entry.id];
  const latest = catalog[entry.id] ?? entry;
  const update = install && !jobs[entry.id] && hasUpdate(latest, install);
  const name = entry.projectName;
  const newer = versionLabel(latest.verified?.version);
  return (
    <section className={`continue app-backdrop${art ? " has-art" : ""}`} aria-label={t`Continue playing`}>
      {art && <SourcedImage key={art} className="backdrop-art" sources={heroSources(art)} alt="" priority onFail={() => {}} />}
      <div className="continue-inner">
        <span className="continue-label">
          <Play size={12} fill="currentColor" aria-hidden="true" />
          <Trans>Continue playing</Trans>
          <span className="continue-meta">{playedAgo(played)}</span>
        </span>
        <h2 className="continue-title">
          {name}
          {install && !install.local && <span className="continue-version">{versionLabel(install.version)}</span>}
        </h2>
        <div className="continue-action">
          {install && !jobs[entry.id] ? (
            <>
              <button type="button" className="primary continue-play" onClick={() => void play(entry.id)}>
                <Play size={16} fill="currentColor" aria-hidden="true" /> <Trans comment="Button: start the app">Play</Trans>
              </button>
              {update && (
                <button type="button" className="continue-update" onClick={() => void get(latest)}>
                  <CloudDownload size={16} aria-hidden="true" /> <Trans>Update to {newer}</Trans>
                </button>
              )}
            </>
          ) : (
            action
          )}
          <button type="button" className="continue-page" title={t`See it in the catalog`} aria-label={t`See ${name} in the catalog`} onClick={onOpen}>
            <Compass size={18} aria-hidden="true" />
          </button>
        </div>
      </div>
    </section>
  );
}

/** How a filter reads on its chip, here and in the catalog's. */
export function filterLabel(key: keyof Filters, value: string, consoleNames: Record<string, string>) {
  if (key === "console") {
    if (!value.startsWith("maker:")) return consoleNames[value] ?? value.toUpperCase();
    const brand = value.slice(6);
    return brand === "OtherPlatforms" ? t`All other platforms` : t`All ${brand}`;
  }
  if (key === "projectType") return PROJECT_TYPES[value] ? i18n._(PROJECT_TYPES[value]) : value;
  if (key === "ai") return AI[value] && i18n._(AI[value]);
  if (key === "installed") return value === "yes" ? t`Installed` : t`Not installed`;
  return tagLabel(value);
}

/**
 * The catalog's filters, over the apps at hand: their consoles, kinds and
 * tags. As `pills` (the library's filter row), each is a compact dropdown,
 * removable once it's on; `all: false` shows only the ones on.
 */
function FilterFields({
  value,
  set,
  entries,
  ownTags = [],
  pills,
}: {
  value: Filters;
  set: (key: keyof Filters) => (e: { target: { value: string } }) => void;
  entries: Entry[];
  ownTags?: string[];
  pills?: { all: boolean; clear: (key: keyof Filters) => void };
}) {
  const { consoles, consoleNames } = useLauncher();
  const here = new Set(entries.flatMap((e) => e.consoles ?? []));
  const present = consoles.filter((c) => here.has(c.id) || value.console === c.id);
  const brands = [...new Set(present.map((c) => c.brand))];
  const tags = [...new Set([...entries.flatMap((e) => e.tags), ...ownTags, ...(value.tag ? [value.tag] : [])])].sort();
  /** What a filter's empty choice says: in full in a dialog, short on a pill. */
  const none = (full: string, short: string) => (pills ? short : full);
  const pill = (key: keyof Filters, select: ReactNode) => {
    if (!pills) return select;
    const chosen = value[key];
    if (!chosen && !pills.all) return null;
    return (
      <FilterPill key={key} on={chosen ? filterLabel(key, chosen, consoleNames) : undefined} onClear={() => pills.clear(key)}>
        {select}
      </FilterPill>
    );
  };
  return (
    <>
      {pill(
        "console",
        <select aria-label={t`Console`} value={value.console ?? ""} onChange={set("console")}>
          <option value="">{none(t`All consoles`, t`Any console`)}</option>
          {brands.map((brand) => (
            <optgroup key={brand} label={brand === "OtherPlatforms" ? t`Other platforms` : brand}>
              <option value={`maker:${brand}`}>{brand === "OtherPlatforms" ? t`All other platforms` : t`All ${brand}`}</option>
              {present
                .filter((c) => c.brand === brand)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {consoleNames[c.id] ?? c.name}
                  </option>
                ))}
            </optgroup>
          ))}
        </select>,
      )}
      {pill(
        "projectType",
        <select aria-label={t`Project type`} value={value.projectType ?? ""} onChange={set("projectType")}>
          <option value="">{none(t`All project types`, t`Any project type`)}</option>
          {Object.entries(PROJECT_TYPES).map(([id, name]) => (
            <option key={id} value={id}>
              {i18n._(name)}
            </option>
          ))}
        </select>,
      )}
      {pill(
        "ai",
        <select aria-label={t`AI use`} value={value.ai ?? ""} onChange={set("ai")}>
          <option value="">{none(t`Show all apps`, t`Any AI use`)}</option>
          <option value="no-generated">{t`Hide mostly AI-generated apps`}</option>
          <option value="no-ai">{t`Hide apps with any AI use`}</option>
        </select>,
      )}
      {pill(
        "tag",
        <select aria-label={t({ message: "Tag", comment: "Filter by one of an app's tags" })} value={value.tag ?? ""} onChange={set("tag")}>
          <option value="">{none(t`All tags`, t`Any tag`)}</option>
          {tags.map((tag) => (
            <option key={tag} value={tag}>
              {tagLabel(tag)}
            </option>
          ))}
        </select>,
      )}
      {pill(
        "installed",
        <select aria-label={t({ message: "Installed", comment: "Filter by whether an app is installed" })} value={value.installed ?? ""} onChange={set("installed")}>
          <option value="">{t`Installed or not`}</option>
          <option value="yes">{t`Installed only`}</option>
          <option value="no">{t`Not installed only`}</option>
        </select>,
      )}
    </>
  );
}

/**
 * One filter as a compact dropdown: tinted, with its own remove button, while
 * it's on (`on` names it for the button), as in the library's and the
 * catalog's filter rows.
 */
export function FilterPill({ on, onClear, children }: { on?: string; onClear: () => void; children: ReactNode }) {
  return (
    <span className={`pill${on ? " on" : ""}`}>
      {children}
      {on ? (
        <button type="button" className="pill-clear" aria-label={t`Remove filter ${on}`} onClick={onClear}>
          <X size={12} />
        </button>
      ) : (
        <ChevronDown size={13} aria-hidden="true" />
      )}
    </span>
  );
}

/**
 * A button with a panel that floats above the page (so a scrolling row or a
 * card can't clip it), under the button or above it, or at a point `opener`
 * is called with (a right-click). Closes on Escape or Back, a click outside,
 * or focus moving away. Closing it puts focus back on its button or, opened
 * at a point, on what had focus then. A `bare` one has no button, only `opener`.
 */
export function Popover({
  label,
  icon,
  role,
  up,
  align,
  className = "menu-button",
  panel: panelClass,
  panelLabel,
  opener,
  escape,
  bare,
  children,
}: {
  label: string;
  icon: ReactNode;
  role: "menu" | "group";
  up?: boolean;
  /** "end": the panel lines up with the button's right edge, for a button at the right of the page. */
  align?: "end";
  className?: string;
  /** More classes on the panel. */
  panel?: string;
  /** The panel's name, when it isn't the button's. */
  panelLabel?: string;
  /** Set to a function that opens the panel at a point on the page, its first control focused. */
  opener?: { current: ((x: number, y: number) => void) | null };
  /** Set to a function that handles Escape or Back inside the panel first (a submenu closing), true when it did. */
  escape?: { current: (() => boolean) | null };
  bare?: boolean;
  /** Its contents, with a way to close it that puts focus back where it was. */
  children: (close: () => void) => ReactNode;
}) {
  // Where it's open; `keys`, opened by a key or a controller's button (a click with no mouse), not the mouse; `above`,
  // above its button, as tall as the room there; `over`, from that far down the window, over its button, where it fits
  // neither under it nor above it (a game's ports, from a card as tall as a phone's screen).
  const [at, setAt] = useState<{ left: number; right: number; top: number; bottom: number; point?: true; keys?: true; above?: number; over?: number } | null>(null);
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  // What had focus when it opened at a point.
  const back = useRef<HTMLElement | null>(null);
  const close = () => setAt(null);
  const refocus = () => (at?.point ? (back.current ?? button.current) : (button.current ?? back.current))?.focus({ preventScroll: true });
  const done = () => {
    if (panel.current?.contains(document.activeElement)) refocus();
    close();
  };
  useEffect(() => {
    if (!at) return;
    const inside = (n: EventTarget | null) => n instanceof Node && Boolean(button.current?.contains(n) || panel.current?.contains(n));
    const away = (e: MouseEvent) => !inside(e.target) && close();
    const key = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // Only this closes: a page under it doesn't go back too.
      e.stopPropagation();
      if (escape?.current?.()) return;
      close();
      refocus();
    };
    // Focus moving away closes it: once it has settled somewhere else, not just left (a click on its padding, or a
    // test driver's blur before it focuses a field in it, leaves focus on nothing for a moment).
    let later: ReturnType<typeof setTimeout> | undefined;
    const blur = (e: Event) => {
      if (inside((e as FocusEvent).relatedTarget)) return;
      clearTimeout(later);
      later = setTimeout(() => {
        const now = document.activeElement;
        if (now && now !== document.body && !inside(now)) close();
      });
    };
    const nodes = [button.current, panel.current];
    window.addEventListener("mousedown", away);
    window.addEventListener("keydown", key, true);
    window.addEventListener("resize", close);
    nodes.forEach((n) => n?.addEventListener("focusout", blur));
    return () => {
      clearTimeout(later);
      window.removeEventListener("mousedown", away);
      window.removeEventListener("keydown", key, true);
      window.removeEventListener("resize", close);
      nodes.forEach((n) => n?.removeEventListener("focusout", blur));
    };
  }, [at]);
  // Under its button, unless `place` says otherwise (the layout effect below, once it's seen how tall it is).
  const open = (keys: boolean, place: { above?: true; over?: number } = {}) => {
    const r = button.current!.getBoundingClientRect();
    // The interface size zooms the page; the panel's position is set in unzoomed pixels (zoom.ts).
    const [z, box] = [boxZoom(), windowBox()];
    setAt({
      left: r.left / z,
      right: (box.width - r.right) / z,
      top: r.bottom / z + 6,
      bottom: (box.height - r.top) / z + 6,
      ...(keys ? { keys: true } : {}),
      ...(place.above ? { above: (r.top - 14) / z } : {}),
      ...(place.over !== undefined ? { over: place.over } : {}),
    });
  };
  // Its button moved while it's open (the page around it changed: a download's strip came, a card's list grew), so it
  // goes with it rather than staying where it was, apart from it; it closes once its button is gone.
  useEffect(() => {
    if (!at || at.point || bare) return;
    const z = boxZoom();
    let frame = requestAnimationFrame(function follow() {
      const b = button.current;
      if (!b?.isConnected || !b.getClientRects().length) return close();
      const r = b.getBoundingClientRect();
      // Under it again, and placed again before it's seen (the layout effect below).
      if (Math.abs(r.bottom / z + 6 - at.top) > 0.5 || Math.abs(r.left / z - at.left) > 0.5) return open(Boolean(at.keys));
      frame = requestAnimationFrame(follow);
    });
    return () => cancelAnimationFrame(frame);
  }, [at]); // eslint-disable-line react-hooks/exhaustive-deps
  if (opener)
    opener.current = (x, y) => {
      if (!at) back.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      // A point on the screen, as the pointer's is.
      const z = zoom();
      setAt({ left: x / z, right: (window.innerWidth - x) / z, top: y / z, bottom: (window.innerHeight - y) / z, point: true });
    };
  // Opened at a point (a right-click, a long press, a key), or from its button by a key or a controller, the first item
  // that can be picked takes focus (not one disabled, which can't), so keys and controllers work in it at once.
  // Once as it opens: not again as it follows its button.
  const opened = Boolean(at);
  useEffect(() => {
    if (!at?.point && !at?.keys) return;
    const first = panel.current?.querySelector<HTMLElement>(":is(button, input, select, textarea):not(:disabled), a[href], [tabindex]:not([tabindex='-1'])");
    first?.focus({ focusVisible: Boolean(at.keys) } as FocusOptions);
  }, [opened, at?.point && at, at?.keys]); // eslint-disable-line react-hooks/exhaustive-deps
  // Placed again (under its button, above it, kept inside the window): a controller's focus box in it goes with it.
  useEffect(() => void (at && haloMoved()), [at]);
  // Kept inside the window, before it's seen: a panel wider than what's beside its button (View, on a phone) went off the edge.
  useLayoutEffect(() => {
    const p = panel.current;
    if (!at || !p) return;
    // In a box's pixels (zoom.ts).
    const [z, box] = [boxZoom(), windowBox()];
    const room = box.width - 16;
    if (p.getBoundingClientRect().width > room) p.style.width = `${room / z}px`;
    const r = p.getBoundingClientRect();
    const shift = r.left < 8 ? 8 - r.left : r.right > box.width - 8 ? box.width - 8 - r.right : 0;
    if (shift) Object.assign(p.style, { left: `${(r.left + shift) / z}px`, right: "auto" });
    // Cut short under its button (a game's ports under a card low in the window), with more room above it: above it.
    // With hardly room either way (a card as tall as a phone's screen): over the button, as much as fits in the window.
    const b = button.current?.getBoundingClientRect();
    if (at.point || up || at.above !== undefined || at.over !== undefined || !b || (p.scrollHeight <= p.clientHeight + 1 && r.bottom <= box.height - 8)) return;
    const [below, above, tall] = [box.height - b.bottom - 14, b.top - 14, p.scrollHeight * z];
    if (Math.max(below, above) < Math.min(tall, 240)) open(Boolean(at.keys), { over: Math.max(8, Math.min(b.top, box.height - tall - 8)) / z });
    else if (above > below) open(Boolean(at.keys), { above: true });
  }, [at]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <>
      {!bare && (
        <button
          ref={button}
          className={className}
          aria-label={label}
          title={label}
          aria-haspopup={role === "menu" ? "menu" : "true"}
          aria-expanded={Boolean(at)}
          // No mouse's click has a count of 0: Enter, Space, or a controller's Confirm (spatial.ts) pressed it.
          onClick={(e) => (at ? close() : open(e.detail === 0))}
        >
          {icon}
        </button>
      )}
      {at &&
        createPortal(
          <div
            ref={panel}
            tabIndex={-1}
            className={`menu floating${up ? " up" : ""}${panelClass ? ` ${panelClass}` : ""}`}
            role={role}
            aria-label={panelLabel ?? label}
            style={
              at.point
                ? { left: Math.max(8, Math.min(at.left, at.left + at.right - 230)), top: Math.max(8, Math.min(at.top, at.top + at.bottom - 380)) }
                : up
                  ? { left: Math.max(8, at.left - 200), bottom: at.bottom }
                  : at.above !== undefined
                    ? { ...(align ? { left: "auto", right: Math.max(8, at.right) } : { left: at.left }), bottom: at.bottom, maxHeight: at.above, overflowY: "auto" }
                    : at.over !== undefined
                      ? { ...(align ? { left: "auto", right: Math.max(8, at.right) } : { left: at.left }), top: at.over, maxHeight: window.innerHeight / zoom() - at.over - 8, overflowY: "auto" }
                      : {
                          ...(align ? { left: "auto", right: Math.max(8, at.right) } : { left: at.left }),
                          top: at.top,
                          // No taller than the window below its button: a long panel (View, in a short window) scrolls inside.
                          // Above a phone's navigation bar and the controller hints too, or its last option sat under them.
                          maxHeight: `calc(${Math.max(160, window.innerHeight / zoom() - at.top - 8)}px - max(env(safe-area-inset-bottom, 0px), var(--pad-hints, 0px)))`,
                          overflowY: "auto",
                        }
            }
          >
            {children(done)}
          </div>,
          document.body,
        )}
    </>
  );
}

/** A small menu of actions behind one button. */
function Menu({ label, icon, items }: { label: string; icon: ReactNode; items: [string, () => void][] }) {
  return (
    <Popover label={label} icon={icon} role="menu">
      {(close) =>
        items.map(([text, act]) => (
          <button key={text} role="menuitem" onClick={() => (close(), act())}>
            {text}
          </button>
        ))
      }
    </Popover>
  );
}

/** At the top of a library card: which of the player's playlists the app is on, and a new one. */
export function PlaylistPicker({ id }: { id: string }) {
  return (
    <Popover label={t`Add to playlist`} icon={<ListPlus size={15} />} role="group" panelLabel={t`Playlists for this app`} align="end">
      {() => <OwnPlaylistChoices id={id} />}
    </Popover>
  );
}

/** The player's own playlists to put an app on, not those they follow: read once it's opened, not with each card drawn. */
function OwnPlaylistChoices({ id }: { id: string }) {
  const { collections } = useLauncher();
  return <PlaylistChoices id={id} playlists={collections.filter((c) => !c.follows)} />;
}

/** Puts an app on the player's playlists, or takes it off; a new playlist starts with it. Also on the app's page. */
export function PlaylistChoices({ id, playlists }: { id: string; playlists: Collection[] }) {
  const { saveCollection, collections } = useLauncher();
  const [name, setName] = useState("");
  const on = (c: Collection) => Boolean(c.apps?.includes(id));
  return (
    <>
      {playlists.map((c) => (
        <button
          key={c.key}
          type="button"
          className={`chip${on(c) ? " on" : ""}`}
          aria-pressed={on(c)}
          onClick={() => saveCollection({ ...c, apps: on(c) ? c.apps!.filter((a) => a !== id) : [...(c.apps ?? []), id] })}
        >
          {c.name}
        </button>
      ))}
      <form
        className="new-playlist"
        onSubmit={(e) => {
          e.preventDefault();
          if (!name.trim()) return;
          saveCollection({ key: newKey(), name: name.trim().slice(0, 60), tags: [], consoles: [], apps: [id], order: collections.length });
          setName("");
        }}
      >
        <input aria-label={t`New playlist`} placeholder={t`New playlist…`} maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
      </form>
    </>
  );
}

/** A dialog: closes on Escape or outside. */
export function Dialog({ label, onClose, children }: { label: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="overlay" {...closesOutside(onClose)}>
      <section className="detail choose" role="dialog" aria-label={label} onClick={(e) => e.stopPropagation()}>
        {children}
      </section>
    </div>
  );
}

/** Names a playlist and sets its filters. Without filters it holds only the apps picked for it. */
function PlaylistEditor({
  collection,
  library,
  ownTags,
  on,
  onClose,
}: {
  collection: Collection;
  library: Entry[];
  ownTags: string[];
  /** The apps a playlist like this one holds. */
  on: (c: Collection) => Entry[];
  onClose: (saved?: Collection) => void;
}) {
  const { saveCollection } = useLauncher();
  const [name, setName] = useState(collection.name);
  const [filters, setFilters] = useState<Filters>(fromPlaylist(collection));
  const [search, setSearch] = useState(collection.search ?? "");
  const [icon, setIcon] = useState(collection.icon);
  const isNew = !collection.name;
  const set = (key: keyof Filters) => (e: { target: { value: string } }) => setFilters({ ...filters, [key]: e.target.value || undefined });
  const picked = collection.apps?.length ?? 0;
  return (
    <Dialog label={isNew ? t`New playlist` : t`Edit playlist`} onClose={() => onClose()}>
      <form
        className="detail-body"
        onSubmit={(e) => {
          e.preventDefault();
          if (!name.trim()) return;
          // One choice of each shows here; a playlist with more keeps them unless that choice changes.
          const was = fromPlaylist(collection);
          const next = toPlaylist(filters);
          const saved: Collection = {
            ...collection,
            ...next,
            name: name.trim().slice(0, 60),
            search: search.trim() || undefined,
            icon,
            tags: filters.tag === was.tag ? collection.tags : next.tags,
            consoles: filters.console === was.console ? collection.consoles : next.consoles,
            projectTypes: filters.projectType === was.projectType ? collection.projectTypes : next.projectTypes,
          };
          saveCollection(saved);
          onClose(saved);
        }}
      >
        <h2>{isNew ? t`New playlist` : t`Edit playlist`}</h2>
        <input autoFocus required aria-label={t`Playlist name`} placeholder={t`Name, like Favourites`} maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
        <p className="muted">
          {picked ? (
            <>
              <Plural value={picked} one="# app picked for it." other="# apps picked for it." />{" "}
            </>
          ) : (
            ""
          )}
          <Trans>Add apps from each app's menu. It also shows apps that match these filters:</Trans>
        </p>
        <div className="toolbar filters">
          <input
            className="playlist-words"
            aria-label={t({ message: "Names with", comment: "Filter: words the apps' names have" })}
            placeholder={t`Names with, like Zelda`}
            maxLength={60}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <FilterFields value={filters} set={set} entries={library} ownTags={ownTags} />
        </div>
        <IconChoice entries={on({ ...collection, ...toPlaylist(filters), search })} value={icon} onChange={setIcon} />
        <div className="row">
          <button className="primary">
            <Trans comment="Saves a playlist">Save</Trans>
          </button>
          <button type="button" onClick={() => onClose()}>
            <Trans>Cancel</Trans>
          </button>
        </div>
      </form>
    </Dialog>
  );
}

/** A playlist's icon on its tab: one of its apps' icons. */
function PlaylistIcon({ entry }: { entry?: Entry }) {
  if (!entry) return null;
  return (
    <span className="playlist-icon" aria-hidden="true">
      <CardArt entry={entry} image="icon" />
    </span>
  );
}

/** Which of a playlist's apps gives it its icon: the first one, or one picked. */
function IconChoice({ entries, value, onChange }: { entries: Entry[]; value?: string; onChange: (id?: string) => void }) {
  if (!entries.length) return null;
  const shown = entries.slice(0, 24);
  // A picked app that left the playlist: back to the first.
  const chosen = shown.some((e) => e.id === value) ? value : undefined;
  return (
    <fieldset className="icon-choice">
      <legend className="muted">
        <Trans>Icon on its tab</Trans>
      </legend>
      <div role="radiogroup" aria-label={t`Icon on its tab`}>
        {shown.map((e, i) => {
          const checked = chosen ? e.id === chosen : i === 0;
          return (
            <button
              key={e.id}
              type="button"
              role="radio"
              aria-checked={checked}
              aria-label={e.projectName}
              title={e.projectName}
              onClick={() => onChange(i === 0 ? undefined : e.id)}
            >
              <CardArt entry={e} image="icon" />
            </button>
          );
        })}
      </div>
    </fieldset>
  );
}

/** Shares a playlist so anyone with the link can see and follow it; sharing again updates it. */
function ShareDialog({ collection: opened, entries, onSignIn, onClose }: { collection: Collection; entries: Entry[]; onSignIn: () => void; onClose: () => void }) {
  const { saveCollection, collections } = useLauncher();
  const { user, shareList, unshareList } = useAccount();
  const [description, setDescription] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  // The playlist as it is now (signing in or syncing changes it), and its shared slug once shared.
  const collection = collections.find((c) => c.key === opened.key) ?? opened;
  const [sharedAs, setSharedAs] = useState<string | undefined>();
  const slug = sharedAs ?? collection.shared?.slug;
  const { client } = useLauncher();
  // A shared playlist's description, to show and keep when it's updated.
  useEffect(() => {
    if (!collection.shared?.slug) return;
    let live = true;
    client.sharedList(collection.shared.slug).then((l) => live && l?.description && setDescription(l.description), () => {});
    return () => void (live = false);
  }, [client, collection.shared?.slug]);
  const describe = (
    <textarea aria-label={t`Description`} maxLength={500} placeholder={t`What's it for? (optional)`} value={description} onChange={(e) => setDescription(e.target.value)} />
  );
  // Only catalog apps: those the player added themselves aren't shared.
  const shareable = entries.filter((e) => !isOwn(e.id));
  const count = shareable.length;
  const left = entries.length - shareable.length;
  const name = collection.name;
  const share = () => {
    setBusy(true);
    setMessage(null);
    shareList({ collectionKey: collection.key, name: collection.name, ...(description.trim() ? { description: description.trim() } : {}), apps: shareable.map((e) => ({ entryId: e.id })) })
      .then(
        (next) => (
          track("playlist_shared", { apps: shareable.length, update: next === slug }),
          setSharedAs(next),
          saveCollection({ ...collection, shared: { slug: next } }),
          setMessage(next === slug ? t`The shared playlist is up to date.` : null)
        ),
        (e) => setMessage(e instanceof Error ? e.message : String(e)),
      )
      .finally(() => setBusy(false));
  };
  return (
    <Dialog label={t`Share a playlist`} onClose={onClose}>
      <div className="detail-body share">
        <h2>{slug ? t`${name} is shared` : t`Share ${name}`}</h2>
        {!user ? (
          <>
            <p className="muted">
              <Trans>Sign in to share a playlist others can add to their library.</Trans>
            </p>
            <button className="primary" onClick={() => (onClose(), onSignIn())}>
              <Trans>Sign in</Trans>
            </button>
          </>
        ) : slug ? (
          <>
            <p className="muted">
              <Trans>Anyone with the link can see it on quiverlauncher.com and add it to their library. Changes you make here reach them when you update it.</Trans>
            </p>
            <input readOnly aria-label={t`Shared playlist link`} value={listUrl(slug)} onFocus={(e) => e.target.select()} />
            {describe}
            <div className="row">
              <button className="primary" onClick={() => void navigator.clipboard?.writeText(listUrl(slug)).then(() => setMessage(t`Link copied.`))}>
                <Trans>Copy link</Trans>
              </button>
              <button disabled={busy} onClick={share}>
                <Trans>Update shared playlist</Trans>
              </button>
              <button
                className="danger"
                disabled={busy}
                onClick={() => {
                  setBusy(true);
                  unshareList(slug).then(
                    () => (setSharedAs(undefined), saveCollection({ ...collection, shared: undefined }), onClose()),
                    () => (setMessage(t`Couldn't stop sharing it. Try again.`), setBusy(false)),
                  );
                }}
              >
                <Trans>Stop sharing</Trans>
              </button>
            </div>
          </>
        ) : (
          <>
            <p className="muted">
              <Plural
                value={count}
                one="Puts its # app on a page anyone with the link can see, and add to their library."
                other="Puts its # apps on a page anyone with the link can see, and add to their library."
              />
              {left ? (
                <>
                  {" "}
                  <Plural value={left} one="# you added yourself isn't shared." other="# you added yourself aren't shared." />
                </>
              ) : (
                ""
              )}
            </p>
            {describe}
            <button className="primary" disabled={busy || !shareable.length} onClick={share}>
              <Trans comment="Shares a playlist">Share</Trans>
            </button>
          </>
        )}
        {message && <p className="muted" role="status">{message}</p>}
        <button onClick={onClose}>
          <Trans comment="Closes a dialog">Done</Trans>
        </button>
      </div>
    </Dialog>
  );
}

/** Adds a playlist someone shared, from its link, as one that follows theirs. */
function FollowDialog({ onClose }: { onClose: (key?: string) => void }) {
  const { client, collections, saveCollection } = useLauncher();
  const [input, setInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [list, setList] = useState<SharedList | null>(null);
  if (!list)
    return (
      <Dialog label={t`Add a shared playlist`} onClose={() => onClose()}>
        <form
          className="detail-body"
          onSubmit={(e) => {
            e.preventDefault();
            const slug = listSlug(input);
            if (!slug) return setError(t`Paste the playlist's link, like quiverlauncher.com/lists/…`);
            setBusy(true);
            setError(null);
            client.sharedList(slug).then(
              (l) => (setBusy(false), l ? setList(l) : setError(t`That playlist isn't shared any more, or the link is wrong.`)),
              () => (setBusy(false), setError(t`Couldn't reach quiverlauncher.com. Check your connection and try again.`)),
            );
          }}
        >
          <h2>
            <Trans>Add a shared playlist</Trans>
          </h2>
          <p className="muted">
            <Trans>Someone shared a playlist? Paste its link to add it to your library. It stays up to date with theirs.</Trans>
          </p>
          <input autoFocus aria-label={t`Shared playlist link`} placeholder="https://quiverlauncher.com/lists/…" value={input} onChange={(e) => setInput(e.target.value)} />
          {error && <p className="job-error">{error}</p>}
          <button className="primary" disabled={busy || !input.trim()}>
            {busy ? t`Looking it up…` : t`Look it up`}
          </button>
        </form>
      </Dialog>
    );
  const apps = list.items.flatMap((i) => (i.kind === "entry" ? [i.entry] : []));
  const existing = collections.find((c) => c.follows === list.slug);
  const owner = list.owner.name;
  const count = apps.length;
  const more = apps.length - 8;
  return (
    <Dialog label={t`Add a shared playlist`} onClose={() => onClose()}>
      <div className="detail-body">
        <h2>{list.name}</h2>
        <p className="muted">{t`A playlist by ${owner} · ${plural(count, { one: "# app", other: "# apps" })}`}</p>
        {list.description && <p>{list.description}</p>}
        <ul className="list-preview">
          {apps.slice(0, 8).map((e) => (
            <li key={e.id}>{e.projectName}</li>
          ))}
          {more > 0 && (
            <li className="muted">
              <Plural value={more} one="and # more" other="and # more" />
            </li>
          )}
        </ul>
        <p className="muted">
          <Trans comment="Get is the label of the button that installs an app">Apps you don't have yet show with Get; nothing downloads until you press it.</Trans>
        </p>
        <button
          className="primary"
          onClick={() => {
            if (existing) return onClose(existing.key);
            const playlist: Collection = { key: newKey(), name: list.name.slice(0, 60), tags: [], consoles: [], follows: list.slug, order: collections.length };
            saveCollection(playlist);
            onClose(playlist.key);
          }}
        >
          {existing ? t`Open it` : t`Add to my library`}
        </button>
      </div>
    </Dialog>
  );
}
