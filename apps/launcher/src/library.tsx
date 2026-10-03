/**
 * The library: shelves (All, Installed, Not installed, Updates, Hidden and the
 * player's collections) over a search, a sort, and optional sections by the
 * original console.
 */
import { useEffect, useState, type ReactNode } from "react";
import { Library, Pencil, Plus, Search } from "lucide-react";
import type { Entry } from "@quiver/api";
import { EntryCard } from "@quiver/ui";
import { hasUpdate, useLauncher, type LibraryItem } from "./store";
import { inCollection, type Collection } from "./sync";

type Shelf = "all" | "installed" | "not-installed" | "updates" | "hidden" | string;

/** The catalog entry as this player named and pictured it. */
export const withOverrides = (entry: Entry, o?: { name?: string; cover?: string }): Entry => ({
  ...entry,
  ...(o?.name ? { projectName: o.name } : {}),
  ...(o?.cover ? { libraryArt: { ...entry.libraryArt, header: o.cover, hero: o.cover } } : {}),
});

export function LibraryPage({
  onOpen,
  onBrowse,
  action,
}: {
  onOpen: (e: Entry) => void;
  onBrowse: () => void;
  action: (entry: Entry) => ReactNode;
}) {
  const { library: all, catalog, installs, jobs, get, add, remove, settings, setSettings, collections, consoleNames } = useLauncher();
  const [shelf, setShelf] = useState<Shelf>("all");
  const [editing, setEditing] = useState<Collection | null>(null);
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<"played" | "name" | "added">("played");
  const hidden = new Set(settings.hidden);
  const collection = collections.find((c) => c.key === shelf);
  const shown = all.filter((i) => hidden.has(i.id) === (shelf === "hidden"));
  const updates = shown.filter((i) => !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id]));
  const words = search.toLowerCase().split(/\s+/).filter(Boolean);
  const order = {
    played: (i: LibraryItem) => -(installs[i.id]?.lastPlayed ?? 0),
    added: (i: LibraryItem) => -i.addedAt,
    name: () => 0,
  }[sort];
  const onShelf = (item: LibraryItem, entry: Entry) => {
    const installed = Boolean(installs[item.id]);
    if (shelf === "installed" || shelf === "not-installed") return installed === (shelf === "installed");
    if (shelf === "updates") return updates.includes(item);
    if (!collection) return true;
    return inCollection(collection, { tags: [...entry.tags, ...(item.overrides?.tags ?? [])], consoles: entry.consoles ?? [], installed });
  };
  const items = shown
    .flatMap((i) => (catalog[i.id] ? [{ item: i, entry: withOverrides(catalog[i.id], i.overrides) }] : []))
    .filter(({ entry, item }) => {
      const text = [entry.projectName, ...entry.games.map((g) => g.title), ...entry.tags, ...(item.overrides?.tags ?? [])].join(" ").toLowerCase();
      return words.every((w) => text.includes(w)) && onShelf(item, entry);
    })
    .sort((a, b) => order(a.item) - order(b.item) || a.entry.projectName.localeCompare(b.entry.projectName))
    .map(({ entry }) => entry);
  // Removed from the library (here, elsewhere or by signing out) but its files are still here.
  const loose = Object.keys(installs).flatMap((id) => (catalog[id] && !all.some((i) => i.id === id) ? [catalog[id]] : []));
  if (!all.length && !loose.length)
    return (
      <div className="empty">
        <Library size={40} strokeWidth={1.2} />
        <h2>Your library is empty</h2>
        <p>Find a port in the catalog and press Get. It's added here and downloads straight away.</p>
        <button className="primary" onClick={onBrowse}>
          Browse the catalog
        </button>
      </div>
    );
  const shelves: [Shelf, string][] = [
    ["all", "All"],
    ["installed", "Installed"],
    ["not-installed", "Not installed"],
    ...(updates.length || shelf === "updates" ? [["updates", `Updates (${updates.length})`] as [Shelf, string]] : []),
    ...(hidden.size || shelf === "hidden" ? [["hidden", `Hidden (${hidden.size})`] as [Shelf, string]] : []),
    ...collections.map((c): [Shelf, string] => [c.key, c.name]),
  ];
  const grid = (entries: Entry[]) => (
    <div className="catalog-grid">
      {entries.map((entry) => (
        <EntryCard key={entry.id} entry={entry} consoleNames={consoleNames} onOpen={() => onOpen(entry)} action={action(entry)} />
      ))}
    </div>
  );
  return (
    <>
      <nav className="shelves" aria-label="Shelves">
        {shelves.map(([key, name]) => (
          <button key={key} className={shelf === key ? "active" : ""} aria-pressed={shelf === key} onClick={() => setShelf(key)}>
            {name}
          </button>
        ))}
        <button aria-label="New collection" title="New collection" onClick={() => setEditing({ key: `c_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`, name: "", tags: [], consoles: [], order: collections.length })}>
          <Plus size={15} />
        </button>
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
        <label className="toggle">
          <input type="checkbox" checked={Boolean(settings.byConsole)} onChange={(e) => setSettings({ ...settings, byConsole: e.target.checked })} />
          Group by console
        </label>
        {collection && (
          <button onClick={() => setEditing(collection)}>
            <Pencil size={14} /> Edit collection
          </button>
        )}
        {updates.length > 0 && (
          <button className="primary" onClick={() => updates.forEach((i) => get(catalog[i.id]))}>
            Update all ({updates.length})
          </button>
        )}
      </div>
      {items.length === 0 && <p className="empty">{collection && !search ? "No apps in this collection yet." : "Nothing here."}</p>}
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
      {editing && (
        <CollectionEditor
          collection={editing}
          onClose={(saved) => {
            setEditing(null);
            if (saved) setShelf(saved.removed ? "all" : saved.key);
          }}
        />
      )}
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

/** Names a collection and says which apps it shows. With nothing chosen it's hand-picked. */
function CollectionEditor({ collection, onClose }: { collection: Collection; onClose: (saved?: Collection) => void }) {
  const { saveCollection, library, catalog, consoleNames } = useLauncher();
  const [name, setName] = useState(collection.name);
  const [tags, setTags] = useState(collection.tags.join(", "));
  const [consoles, setConsoles] = useState(collection.consoles);
  const [installed, setInstalled] = useState<"" | "yes" | "no">(collection.installed ?? "");
  const isNew = !collection.name;
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  // Consoles of the apps in the library, and any already chosen.
  const owned = [...new Set([...library.flatMap((i) => catalog[i.id]?.consoles ?? []), ...consoles])];
  const save = (removed?: boolean) => {
    const parsed = tags.split(",").map((t) => t.trim()).filter(Boolean).slice(0, 20);
    const nothing = !parsed.length && !consoles.length && !installed;
    const saved: Collection = {
      ...collection,
      name: name.trim().slice(0, 60),
      // Hand-picked: its own tag, put on apps from their page.
      tags: nothing ? [name.trim().toLowerCase().slice(0, 50)] : parsed,
      consoles,
      installed: installed || undefined,
      removed: removed || undefined,
    };
    saveCollection(saved);
    onClose(saved);
  };
  return (
    <div className="overlay" onClick={() => onClose()}>
      <section className="detail choose" role="dialog" aria-label={isNew ? "New collection" : "Edit collection"} onClick={(e) => e.stopPropagation()}>
        <form
          className="detail-body"
          onSubmit={(e) => {
            e.preventDefault();
            if (name.trim()) save();
          }}
        >
          <h2>{isNew ? "New collection" : "Edit collection"}</h2>
          <input autoFocus required aria-label="Collection name" placeholder="Name, like Favourites" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} />
          <p className="muted">Leave the rest empty to pick its apps yourself, from each app's page. Or show apps that match:</p>
          <input aria-label="Tags" placeholder="Tags, separated by commas" value={tags} onChange={(e) => setTags(e.target.value)} />
          {owned.length > 0 && (
            <div className="chips" aria-label="Consoles">
              {owned.map((id) => (
                <button
                  type="button"
                  key={id}
                  className={`chip${consoles.includes(id) ? " on" : ""}`}
                  aria-pressed={consoles.includes(id)}
                  onClick={() => setConsoles(consoles.includes(id) ? consoles.filter((c) => c !== id) : [...consoles, id])}
                >
                  {consoleNames[id] ?? id.toUpperCase()}
                </button>
              ))}
            </div>
          )}
          <select aria-label="Installed" value={installed} onChange={(e) => setInstalled(e.target.value as "" | "yes" | "no")}>
            <option value="">Installed or not</option>
            <option value="yes">Installed only</option>
            <option value="no">Not installed only</option>
          </select>
          <div className="row">
            <button className="primary">Save</button>
            {!isNew && (
              <button type="button" className="danger" onClick={() => save(true)}>
                Delete collection
              </button>
            )}
            <button type="button" onClick={() => onClose()}>
              Cancel
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
