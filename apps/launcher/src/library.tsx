/**
 * The library: tabs (All, Installed, Not installed, Updates, Hidden and the
 * player's playlists) over a search, a sort, the catalog's filters and optional
 * sections by the original console. A playlist holds apps picked by hand,
 * saved filters, or both; one can be shared on quiverlauncher.com, and a
 * playlist someone shared can be followed.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Filter, Library, ListPlus, MoreHorizontal, Plus, Search, X } from "lucide-react";
import { listSlug, listUrl, type Entry, type SharedList } from "@quiverlauncher/api";
import { EntryCard, tagLabel } from "@quiverlauncher/ui";
import { hasUpdate, useLauncher, type LibraryItem } from "./store";
import { useAccount } from "./account";
import { hasFilters, inCollection, matches, type AppFacts, type Collection } from "./sync";
import { isOwn } from "./custom";
import { track } from "./telemetry";

type Tab = "all" | "installed" | "not-installed" | "updates" | "hidden" | string;
/** The library's filters, as the catalog's: one choice of each. */
type Filters = { console?: string; projectType?: string; ai?: "no-generated" | "no-ai"; tag?: string; installed?: "yes" | "no" };

/** The catalog entry as this player named and pictured it. */
export const withOverrides = (entry: Entry, o?: { name?: string; cover?: string }): Entry => ({
  ...entry,
  ...(o?.name ? { projectName: o.name } : {}),
  ...(o?.cover ? { libraryArt: { ...entry.libraryArt, header: o.cover, hero: o.cover } } : {}),
});

const newKey = () => `c_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
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
const PROJECT_TYPES: Record<string, string> = { port: "Port", tool: "Tool", emulator: "Emulator", game: "Standalone game" };
const AI: Record<string, string> = { "no-generated": "Hide mostly AI-generated", "no-ai": "Hide any AI use" };

export function LibraryPage({
  onOpen,
  onBrowse,
  onAdd,
  onSignIn,
  action,
}: {
  onOpen: (e: Entry) => void;
  onBrowse: () => void;
  /** Adds an app the catalog doesn't have. */
  onAdd: () => void;
  onSignIn: () => void;
  action: (entry: Entry) => ReactNode;
}) {
  const { client, library: all, catalog, installs, jobs, get, add, remove, settings, setSettings, collections, saveCollection, consoles, consoleNames, cache } = useLauncher();
  const { user, unshareList } = useAccount();
  const [tab, setTab] = useState<Tab>("all");
  const [editing, setEditing] = useState<Collection | null>(null);
  const [sharing, setSharing] = useState<Collection | null>(null);
  const [following, setFollowing] = useState(false);
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<"played" | "name" | "added">("played");
  const [filters, setFilters] = useState<Filters>({});
  const [filtersOpen, setFiltersOpen] = useState(false);
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
  });
  const order = {
    played: (e: Entry) => -(installs[e.id]?.lastPlayed ?? 0),
    added: (e: Entry) => -(inLibrary.get(e.id)?.addedAt ?? 0),
    name: () => 0,
  }[sort];
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
  const items = candidates
    .filter(({ entry, item }) => {
      const text = [entry.projectName, ...entry.games.map((g) => g.title), ...entry.tags, ...(item?.overrides?.tags ?? [])].join(" ").toLowerCase();
      return words.every((w) => text.includes(w)) && inTab(entry, item) && (!filtering || matches(toPlaylist(filters), facts(entry, item), makers));
    })
    .sort((a, b) => order(a.entry) - order(b.entry) || a.entry.projectName.localeCompare(b.entry.projectName))
    .map(({ entry }) => entry);
  // Removed from the library (here, elsewhere or by signing out) but its files are still here.
  const loose = Object.keys(installs).flatMap((id) => (catalog[id] && !all.some((i) => i.id === id) ? [catalog[id]] : []));
  const dialogs = (
    <>
      {editing && (
        <PlaylistEditor
          collection={editing}
          library={candidates.map((c) => c.entry)}
          ownTags={ownTags}
          onClose={(saved) => {
            setEditing(null);
            if (!saved) return;
            setTab(saved.removed ? "all" : saved.key);
            // Its filters are the playlist now.
            setFilters({});
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
        <h2>Your library is empty</h2>
        <p>Find a port in the catalog and press Get. It's added here and downloads straight away.</p>
        <div className="row">
          <button className="primary" onClick={onBrowse}>
            Browse the catalog
          </button>
          <button onClick={onAdd}>
            <Plus size={15} /> Add an app
          </button>
          <button onClick={() => setFollowing(true)}>
            <ListPlus size={15} /> Add a shared playlist
          </button>
        </div>
        {dialogs}
      </div>
    );
  const tabs: [Tab, string][] = [
    ["all", "All"],
    ["installed", "Installed"],
    ["not-installed", "Not installed"],
    ...(updates.length || tab === "updates" ? [["updates", `Updates (${updates.length})`] as [Tab, string]] : []),
    ...(hidden.size || tab === "hidden" ? [["hidden", `Hidden (${hidden.size})`] as [Tab, string]] : []),
    ...collections.map((c): [Tab, string] => [c.key, c.name]),
  ];
  const own = collections.filter((c) => !c.follows);
  const card = (entry: Entry) => {
    const item = inLibrary.get(entry.id);
    return (
      <EntryCard
        key={entry.id}
        entry={entry}
        consoleNames={consoleNames}
        onOpen={() => onOpen(entry)}
        badge={collection?.follows && item ? "In library" : undefined}
        action={
          item ? (
            <div className="card-actions">
              {action(entry)}
              <PlaylistPicker id={entry.id} playlists={own} />
            </div>
          ) : (
            action(entry)
          )
        }
      />
    );
  };
  const grid = (entries: Entry[]) => <div className="catalog-grid">{entries.map(card)}</div>;
  const set = (key: keyof Filters) => (e: { target: { value: string } }) => setFilters({ ...filters, [key]: e.target.value || undefined });
  return (
    <>
      <nav className="playlists" aria-label="Playlists">
        {tabs.map(([key, name]) => (
          <span key={key} className="playlist-tab">
            <button className={tab === key ? "active" : ""} aria-pressed={tab === key} onClick={() => setTab(key)}>
              {name}
            </button>
            {/* The open playlist's menu: edit, share or delete it; a followed one is copied or removed. */}
            {tab === key && collection && (
              <Menu
                label={`${collection.name} options`}
                icon={<MoreHorizontal size={15} />}
                items={collection.follows
                  ? [
                      [
                        "Make a copy",
                        () => {
                          if (!loaded) return;
                          // Its apps join the library (nothing downloads), so the copy shows them on every computer.
                          const entries = loaded.items.flatMap((i) => (i.kind === "entry" ? [i.entry] : []));
                          cache(entries);
                          entries.filter((e) => !inLibrary.has(e.id)).forEach((e) => add(e));
                          const copy: Collection = { key: newKey(), name: `${collection.name} (copy)`.slice(0, 60), tags: [], consoles: [], apps: entries.map((e) => e.id), order: collections.length };
                          saveCollection(copy);
                          setTab(copy.key);
                        },
                      ],
                      ["Remove", () => (saveCollection({ ...collection, removed: true }), setTab("all"))],
                    ]
                  : [
                      ["Edit", () => setEditing(collection)],
                      [collection.shared ? "Shared playlist" : "Share", () => setSharing(collection)],
                      [
                        collection.shared ? "Delete (stops sharing it)" : "Delete",
                        () => {
                          // Deleting a shared playlist stops sharing it.
                          if (collection.shared && user) void unshareList(collection.shared.slug).catch(() => {});
                          saveCollection({ ...collection, removed: true });
                          setTab("all");
                        },
                      ],
                    ]}
              />
            )}
          </span>
        ))}
        <Menu
          label="Add a playlist"
          icon={<Plus size={15} />}
          items={[
            ["New playlist", () => setEditing({ key: newKey(), name: "", tags: [], consoles: [], order: collections.length })],
            ["Add a shared playlist", () => setFollowing(true)],
          ]}
        />
        {collections.length > 6 && (
          <select aria-label="All playlists" value={collection ? tab : ""} onChange={(e) => e.target.value && setTab(e.target.value)}>
            <option value="">More…</option>
            {collections.map((c) => (
              <option key={c.key} value={c.key}>
                {c.name}
              </option>
            ))}
          </select>
        )}
      </nav>
      <div className="toolbar">
        <label className="search">
          <Search size={16} />
          <input placeholder="Search your library" value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>
        <select aria-label="Sort" value={sort} onChange={(e) => setSort(e.target.value as typeof sort)}>
          <option value="played">Recently played</option>
          <option value="added">Recently added</option>
          <option value="name">Name</option>
        </select>
        <button className={filtersOpen ? "on" : ""} aria-expanded={filtersOpen} onClick={() => setFiltersOpen(!filtersOpen)}>
          <Filter size={14} /> Filters{filtering ? ` · ${Object.values(filters).filter(Boolean).length}` : ""}
        </button>
        <label className="toggle">
          <input type="checkbox" checked={Boolean(settings.byConsole)} onChange={(e) => setSettings({ ...settings, byConsole: e.target.checked })} />
          Group by console
        </label>
        <button onClick={onAdd}>
          <Plus size={15} /> Add an app
        </button>
        {updates.length > 0 && (
          <button className="primary" onClick={() => updates.forEach((i) => get(catalog[i.id]))}>
            Update all ({updates.length})
          </button>
        )}
      </div>
      {filtersOpen && (
        <div className="toolbar filters" aria-label="Library filters">
          <FilterFields value={filters} set={set} entries={candidates.map((c) => c.entry)} ownTags={ownTags} />
        </div>
      )}
      {filtering && (
        <div className="filter-chips">
          {(Object.entries(filters) as [keyof Filters, string | undefined][]).map(
            ([key, value]) =>
              value && (
                <button key={key} className="chip on" aria-label={`Remove filter ${label(key, value, consoleNames)}`} onClick={() => setFilters({ ...filters, [key]: undefined })}>
                  {label(key, value, consoleNames)} <X size={12} />
                </button>
              ),
          )}
          <button
            className="primary save-playlist"
            onClick={() => setEditing({ key: newKey(), name: "", order: collections.length, ...toPlaylist(filters) })}
          >
            Save as playlist
          </button>
        </div>
      )}
      {collection?.follows && loaded && (
        <p className="muted playlist-note">
          A playlist by {loaded.owner.name}, kept up to date with theirs.{loaded.unavailable ? ` ${loaded.unavailable} of its apps left the catalog.` : ""}
        </p>
      )}
      {collection?.follows && list === null && <p className="muted playlist-note">This playlist isn't shared any more.</p>}
      {collection?.follows && list === "failed" && <p className="muted playlist-note">Couldn't reach quiverlauncher.com to show this playlist. Check your connection.</p>}
      {/* An empty library over apps still installed here: their section says it all. */}
      {items.length === 0 && !(loose.length && tab === "all" && !search && !filtering) && (
        <p className="empty">
          {collection && !search && !filtering ? (collection.follows ? "Nothing in this playlist." : "No apps in this playlist yet. Add some from each app's menu, or edit its filters.") : "Nothing here."}
        </p>
      )}
      {settings.byConsole
        ? sections(items, consoleNames).map(([name, entries]) => (
            <section key={name} className="console-section">
              <h2>{name}</h2>
              {grid(entries)}
            </section>
          ))
        : grid(items)}
      {loose.length > 0 && (
        <section className="loose">
          <h2>Installed, not in your library</h2>
          <div className="catalog-grid">
            {loose.map((entry) => (
              <EntryCard
                key={entry.id}
                entry={entry}
                consoleNames={consoleNames}
                onOpen={() => onOpen(entry)}
                action={
                  <div className="row">
                    <button className="primary" onClick={() => add(entry)}>
                      Add
                    </button>
                    <button onClick={() => remove(entry.id)}>Uninstall</button>
                  </div>
                }
              />
            ))}
          </div>
        </section>
      )}
      {dialogs}
    </>
  );
}

/** How a filter reads on its chip. */
function label(key: keyof Filters, value: string, consoleNames: Record<string, string>) {
  if (key === "console") return value.startsWith("maker:") ? `All ${value.slice(6) === "OtherPlatforms" ? "other platforms" : value.slice(6)}` : (consoleNames[value] ?? value.toUpperCase());
  if (key === "projectType") return PROJECT_TYPES[value] ?? value;
  if (key === "ai") return AI[value];
  if (key === "installed") return value === "yes" ? "Installed" : "Not installed";
  return tagLabel(value);
}

/** The catalog's filters, over the apps at hand: their consoles, kinds and tags. */
function FilterFields({ value, set, entries, ownTags = [] }: { value: Filters; set: (key: keyof Filters) => (e: { target: { value: string } }) => void; entries: Entry[]; ownTags?: string[] }) {
  const { consoles, consoleNames } = useLauncher();
  const here = new Set(entries.flatMap((e) => e.consoles ?? []));
  const present = consoles.filter((c) => here.has(c.id) || value.console === c.id);
  const brands = [...new Set(present.map((c) => c.brand))];
  const tags = [...new Set([...entries.flatMap((e) => e.tags), ...ownTags, ...(value.tag ? [value.tag] : [])])].sort();
  return (
    <>
      <select aria-label="Console" value={value.console ?? ""} onChange={set("console")}>
        <option value="">All consoles</option>
        {brands.map((brand) => (
          <optgroup key={brand} label={brand === "OtherPlatforms" ? "Other platforms" : brand}>
            <option value={`maker:${brand}`}>{brand === "OtherPlatforms" ? "All other platforms" : `All ${brand}`}</option>
            {present
              .filter((c) => c.brand === brand)
              .map((c) => (
                <option key={c.id} value={c.id}>
                  {consoleNames[c.id] ?? c.name}
                </option>
              ))}
          </optgroup>
        ))}
      </select>
      <select aria-label="Project type" value={value.projectType ?? ""} onChange={set("projectType")}>
        <option value="">All project types</option>
        {Object.entries(PROJECT_TYPES).map(([id, name]) => (
          <option key={id} value={id}>
            {name}
          </option>
        ))}
      </select>
      <select aria-label="AI use" value={value.ai ?? ""} onChange={set("ai")}>
        <option value="">Show all apps</option>
        <option value="no-generated">Hide mostly AI-generated apps</option>
        <option value="no-ai">Hide apps with any AI use</option>
      </select>
      <select aria-label="Tag" value={value.tag ?? ""} onChange={set("tag")}>
        <option value="">All tags</option>
        {tags.map((t) => (
          <option key={t} value={t}>
            {tagLabel(t)}
          </option>
        ))}
      </select>
      <select aria-label="Installed" value={value.installed ?? ""} onChange={set("installed")}>
        <option value="">Installed or not</option>
        <option value="yes">Installed only</option>
        <option value="no">Not installed only</option>
      </select>
    </>
  );
}

/**
 * A button with a panel that floats above the page (so a scrolling row or a
 * card can't clip it), under the button or above it. Closes on Escape or
 * Back, a click outside, or focus moving away.
 */
function Popover({ label, icon, role, up, children }: { label: string; icon: ReactNode; role: "menu" | "group"; up?: boolean; children: (close: () => void) => ReactNode }) {
  const [at, setAt] = useState<{ left: number; top: number; bottom: number } | null>(null);
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const close = () => setAt(null);
  useEffect(() => {
    if (!at) return;
    const inside = (n: EventTarget | null) => n instanceof Node && Boolean(button.current?.contains(n) || panel.current?.contains(n));
    const away = (e: MouseEvent) => !inside(e.target) && close();
    const key = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return;
      // Only this closes: a page under it doesn't go back too.
      e.stopPropagation();
      close();
      button.current?.focus();
    };
    const blur = (e: Event) => {
      if (!inside((e as FocusEvent).relatedTarget)) close();
    };
    const nodes = [button.current, panel.current];
    window.addEventListener("mousedown", away);
    window.addEventListener("keydown", key, true);
    window.addEventListener("resize", close);
    nodes.forEach((n) => n?.addEventListener("focusout", blur));
    return () => {
      window.removeEventListener("mousedown", away);
      window.removeEventListener("keydown", key, true);
      window.removeEventListener("resize", close);
      nodes.forEach((n) => n?.removeEventListener("focusout", blur));
    };
  }, [at]);
  const open = () => {
    const r = button.current!.getBoundingClientRect();
    // The interface size zooms the page; the panel's position is set in unzoomed pixels.
    const zoom = Number(getComputedStyle(document.documentElement).zoom) || 1;
    setAt({ left: r.left / zoom, top: r.bottom / zoom + 6, bottom: (window.innerHeight - r.top) / zoom + 6 });
  };
  return (
    <>
      <button ref={button} className="menu-button" aria-label={label} title={label} aria-haspopup={role === "menu" ? "menu" : "true"} aria-expanded={Boolean(at)} onClick={() => (at ? close() : open())}>
        {icon}
      </button>
      {at &&
        createPortal(
          <div
            ref={panel}
            tabIndex={-1}
            className={`menu floating${up ? " up" : ""}`}
            role={role}
            aria-label={role === "menu" ? label : "Playlists for this app"}
            style={up ? { left: Math.max(8, at.left - 200), bottom: at.bottom } : { left: at.left, top: at.top }}
          >
            {children(close)}
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

/** On a library card: which of the player's playlists the app is on, and a new one. */
function PlaylistPicker({ id, playlists }: { id: string; playlists: Collection[] }) {
  return (
    <Popover label="Add to playlist" icon={<ListPlus size={15} />} role="group" up>
      {() => <PlaylistChoices id={id} playlists={playlists} />}
    </Popover>
  );
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
        <input aria-label="New playlist" placeholder="New playlist…" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
      </form>
    </>
  );
}

/** Apps under their first original console, sections by name, "Other" last. */
function sections(entries: Entry[], names: Record<string, string>): [string, Entry[]][] {
  const groups = new Map<string, Entry[]>();
  for (const e of entries) {
    const id = e.consoles?.[0];
    const name = id ? (names[id] ?? id.toUpperCase()) : "Other";
    groups.set(name, [...(groups.get(name) ?? []), e]);
  }
  return [...groups].sort(([a], [b]) => (a === "Other" ? 1 : b === "Other" ? -1 : a.localeCompare(b)));
}

/** A dialog: closes on Escape or outside. */
function Dialog({ label, onClose, children }: { label: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="overlay" onClick={onClose}>
      <section className="detail choose" role="dialog" aria-label={label} onClick={(e) => e.stopPropagation()}>
        {children}
      </section>
    </div>
  );
}

/** Names a playlist and sets its filters. Without filters it holds only the apps picked for it. */
function PlaylistEditor({ collection, library, ownTags, onClose }: { collection: Collection; library: Entry[]; ownTags: string[]; onClose: (saved?: Collection) => void }) {
  const { saveCollection } = useLauncher();
  const [name, setName] = useState(collection.name);
  const [filters, setFilters] = useState<Filters>(fromPlaylist(collection));
  const isNew = !collection.name;
  const set = (key: keyof Filters) => (e: { target: { value: string } }) => setFilters({ ...filters, [key]: e.target.value || undefined });
  const picked = collection.apps?.length ?? 0;
  return (
    <Dialog label={isNew ? "New playlist" : "Edit playlist"} onClose={() => onClose()}>
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
            tags: filters.tag === was.tag ? collection.tags : next.tags,
            consoles: filters.console === was.console ? collection.consoles : next.consoles,
            projectTypes: filters.projectType === was.projectType ? collection.projectTypes : next.projectTypes,
          };
          saveCollection(saved);
          onClose(saved);
        }}
      >
        <h2>{isNew ? "New playlist" : "Edit playlist"}</h2>
        <input autoFocus required aria-label="Playlist name" placeholder="Name, like Favourites" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
        <p className="muted">
          {picked ? `${picked} ${picked === 1 ? "app" : "apps"} picked for it. ` : ""}Add apps from each app's menu. It also shows apps that match these filters:
        </p>
        <div className="toolbar filters">
          <FilterFields value={filters} set={set} entries={library} ownTags={ownTags} />
        </div>
        <div className="row">
          <button className="primary">Save</button>
          <button type="button" onClick={() => onClose()}>
            Cancel
          </button>
        </div>
      </form>
    </Dialog>
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
    <textarea aria-label="Description" maxLength={500} placeholder="What's it for? (optional)" value={description} onChange={(e) => setDescription(e.target.value)} />
  );
  // Only catalog apps: those the player added themselves aren't shared.
  const shareable = entries.filter((e) => !isOwn(e.id));
  const left = entries.length - shareable.length;
  const share = () => {
    setBusy(true);
    setMessage(null);
    shareList({ collectionKey: collection.key, name: collection.name, ...(description.trim() ? { description: description.trim() } : {}), apps: shareable.map((e) => ({ entryId: e.id })) })
      .then(
        (next) => (
          track("playlist_shared", { apps: shareable.length, update: next === slug }),
          setSharedAs(next),
          saveCollection({ ...collection, shared: { slug: next } }),
          setMessage(next === slug ? "The shared playlist is up to date." : null)
        ),
        (e) => setMessage(e instanceof Error ? e.message : String(e)),
      )
      .finally(() => setBusy(false));
  };
  return (
    <Dialog label="Share a playlist" onClose={onClose}>
      <div className="detail-body share">
        <h2>{slug ? `${collection.name} is shared` : `Share ${collection.name}`}</h2>
        {!user ? (
          <>
            <p className="muted">Sign in to share a playlist others can add to their library.</p>
            <button className="primary" onClick={() => (onClose(), onSignIn())}>
              Sign in
            </button>
          </>
        ) : slug ? (
          <>
            <p className="muted">Anyone with the link can see it on quiverlauncher.com and add it to their library. Changes you make here reach them when you update it.</p>
            <input readOnly aria-label="Shared playlist link" value={listUrl(slug)} onFocus={(e) => e.target.select()} />
            {describe}
            <div className="row">
              <button className="primary" onClick={() => void navigator.clipboard?.writeText(listUrl(slug)).then(() => setMessage("Link copied."))}>
                Copy link
              </button>
              <button disabled={busy} onClick={share}>
                Update shared playlist
              </button>
              <button
                className="danger"
                disabled={busy}
                onClick={() => {
                  setBusy(true);
                  unshareList(slug).then(
                    () => (setSharedAs(undefined), saveCollection({ ...collection, shared: undefined }), onClose()),
                    () => (setMessage("Couldn't stop sharing it. Try again."), setBusy(false)),
                  );
                }}
              >
                Stop sharing
              </button>
            </div>
          </>
        ) : (
          <>
            <p className="muted">
              Puts its {shareable.length} {shareable.length === 1 ? "app" : "apps"} on a page anyone with the link can see, and add to their library.
              {left ? ` ${left} you added yourself ${left === 1 ? "isn't" : "aren't"} shared.` : ""}
            </p>
            {describe}
            <button className="primary" disabled={busy || !shareable.length} onClick={share}>
              Share
            </button>
          </>
        )}
        {message && <p className="muted" role="status">{message}</p>}
        <button onClick={onClose}>Done</button>
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
      <Dialog label="Add a shared playlist" onClose={() => onClose()}>
        <form
          className="detail-body"
          onSubmit={(e) => {
            e.preventDefault();
            const slug = listSlug(input);
            if (!slug) return setError("Paste the playlist's link, like quiverlauncher.com/lists/…");
            setBusy(true);
            setError(null);
            client.sharedList(slug).then(
              (l) => (setBusy(false), l ? setList(l) : setError("That playlist isn't shared any more, or the link is wrong.")),
              () => (setBusy(false), setError("Couldn't reach quiverlauncher.com. Check your connection and try again.")),
            );
          }}
        >
          <h2>Add a shared playlist</h2>
          <p className="muted">Someone shared a playlist? Paste its link to add it to your library. It stays up to date with theirs.</p>
          <input autoFocus aria-label="Shared playlist link" placeholder="https://quiverlauncher.com/lists/…" value={input} onChange={(e) => setInput(e.target.value)} />
          {error && <p className="job-error">{error}</p>}
          <button className="primary" disabled={busy || !input.trim()}>
            {busy ? "Looking it up…" : "Look it up"}
          </button>
        </form>
      </Dialog>
    );
  const apps = list.items.flatMap((i) => (i.kind === "entry" ? [i.entry] : []));
  const existing = collections.find((c) => c.follows === list.slug);
  return (
    <Dialog label="Add a shared playlist" onClose={() => onClose()}>
      <div className="detail-body">
        <h2>{list.name}</h2>
        <p className="muted">
          A playlist by {list.owner.name} · {apps.length} {apps.length === 1 ? "app" : "apps"}
        </p>
        {list.description && <p>{list.description}</p>}
        <ul className="list-preview">
          {apps.slice(0, 8).map((e) => (
            <li key={e.id}>{e.projectName}</li>
          ))}
          {apps.length > 8 && <li className="muted">and {apps.length - 8} more</li>}
        </ul>
        <p className="muted">Apps you don't have yet show with Get; nothing downloads until you press it.</p>
        <button
          className="primary"
          onClick={() => {
            if (existing) return onClose(existing.key);
            const playlist: Collection = { key: newKey(), name: list.name.slice(0, 60), tags: [], consoles: [], follows: list.slug, order: collections.length };
            saveCollection(playlist);
            onClose(playlist.key);
          }}
        >
          {existing ? "Open it" : "Add to my library"}
        </button>
      </div>
    </Dialog>
  );
}
