import { useEffect, useState } from "react";
import { Download, Library, Play, Search, ShieldCheck, Trash2, X } from "lucide-react";
import type { Entry, Page } from "@quiver/api";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, Score, coverOf } from "@quiver/ui";
import { availableOn, hasUpdate, useLauncher } from "./store";

type Tab = "library" | "browse";

export function App() {
  const { library } = useLauncher();
  // A new player starts in the catalog; everyone else in their library.
  const [tab, setTab] = useState<Tab>(library.length ? "library" : "browse");
  const [open, setOpen] = useState<Entry | null>(null);
  return (
    <div className="shell">
      <header className="topbar">
        <strong className="brand">Quiver</strong>
        <nav>
          <button className={tab === "library" ? "active" : ""} onClick={() => setTab("library")}>
            Library <span className="count">{library.length}</span>
          </button>
          <button className={tab === "browse" ? "active" : ""} onClick={() => setTab("browse")}>
            Browse
          </button>
        </nav>
      </header>
      <main>
        <OldLibrary onDone={() => setTab("library")} />
        {tab === "library" ? (
          <LibraryPage onOpen={setOpen} onBrowse={() => setTab("browse")} />
        ) : (
          <BrowsePage onOpen={setOpen} />
        )}
      </main>
      {open && <Detail entry={open} onClose={() => setOpen(null)} />}
      <ChooseFile />
    </div>
  );
}

/** Offers to bring over a Quiver Launcher 3 library, installs and all. */
function OldLibrary({ onDone }: { onDone: () => void }) {
  const { oldApps, importOld } = useLauncher();
  const [state, setState] = useState<"idle" | "busy" | string[]>("idle");
  if (Array.isArray(state))
    return state.length ? (
      <div className="banner" role="status">
        <p>Your apps are in your library. These aren't in the catalog yet, so they weren't brought over: {state.join(", ")}.</p>
        <button onClick={() => setState("idle")}>OK</button>
      </div>
    ) : null;
  if (!oldApps.length) return null;
  return (
    <div className="banner">
      <p>
        Quiver Launcher 3 is on this computer with {oldApps.length} {oldApps.length === 1 ? "app" : "apps"}. Bring them
        over? Installed apps stay where they are, so nothing is downloaded again.
      </p>
      <button
        className="primary"
        disabled={state === "busy"}
        onClick={() => {
          setState("busy");
          importOld().then(
            (missing) => {
              setState(missing);
              onDone();
            },
            () => setState("idle"),
          );
        }}
      >
        {state === "busy" ? "Bringing them over…" : "Bring them over"}
      </button>
    </div>
  );
}

function LibraryPage({ onOpen, onBrowse }: { onOpen: (e: Entry) => void; onBrowse: () => void }) {
  const { library, catalog, installs, jobs, get } = useLauncher();
  const updates = library.filter((i) => !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id]));
  const items = library.flatMap((i) => (catalog[i.id] ? [{ ...catalog[i.id], ...withOverrides(i.overrides) }] : []));
  if (!items.length)
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
  return (
    <>
      {updates.length > 0 && (
        <div className="toolbar">
          <button className="primary" onClick={() => updates.forEach((i) => get(catalog[i.id]))}>
            Update all ({updates.length})
          </button>
        </div>
      )}
      <div className="catalog-grid">
        {items.map((entry) => (
          <EntryCard key={entry.id} entry={entry} onOpen={() => onOpen(entry)} action={<Action entry={entry} />} />
        ))}
      </div>
    </>
  );
}

const withOverrides = (o?: { name?: string; cover?: string }) => ({
  ...(o?.name ? { projectName: o.name } : {}),
  ...(o?.cover ? { libraryArt: { header: o.cover } } : {}),
});

function BrowsePage({ onOpen }: { onOpen: (e: Entry) => void }) {
  const { client, config, library, remember } = useLauncher();
  const [search, setSearch] = useState("");
  const [allPlatforms, setAllPlatforms] = useState(false);
  const [pages, setPages] = useState<Page<Entry>[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const query = useDebounced(search, 250);
  const os = allPlatforms ? undefined : config.os;

  function load(cursor?: string | null) {
    setLoading(true);
    setError(null);
    client
      .apps({ search: query, os, cursor })
      .then((page) => {
        remember(page.items);
        setPages((p) => (cursor ? [...p, page] : [page]));
      })
      .catch(() => setError("Couldn't reach quiverlauncher.com. Check your connection and try again."))
      .finally(() => setLoading(false));
  }
  useEffect(() => load(), [query, os, client]); // eslint-disable-line react-hooks/exhaustive-deps

  const inLibrary = new Set(library.map((i) => i.id));
  const entries = pages.flatMap((p) => p.items);
  const last = pages.at(-1);
  return (
    <>
      <div className="toolbar">
        <label className="search">
          <Search size={16} />
          <input
            autoFocus
            placeholder="Search ports, games and tools"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
        <label className="toggle">
          <input type="checkbox" checked={allPlatforms} onChange={(e) => setAllPlatforms(e.target.checked)} />
          Show apps for other platforms
        </label>
      </div>
      {error ? (
        <div className="empty">
          <p>{error}</p>
          <button className="primary" onClick={() => load()}>
            Try again
          </button>
        </div>
      ) : (
        <>
          <div className="catalog-grid">
            {entries.map((entry) => (
              <EntryCard
                key={entry.id}
                entry={entry}
                onOpen={() => onOpen(entry)}
                badge={inLibrary.has(entry.id) ? "In library" : undefined}
                action={<Action entry={entry} />}
              />
            ))}
          </div>
          {!loading && !entries.length && <p className="empty">Nothing matches that search.</p>}
          {loading ? (
            <div className="more">
              <span className="spinner" />
            </div>
          ) : (
            last && !last.isDone && (
              <div className="more">
                <button onClick={() => load(last.nextCursor)}>Show more</button>
              </div>
            )
          )}
        </>
      )}
    </>
  );
}

/** The one button a card needs: Get, progress, Play or Update. */
function Action({ entry }: { entry: Entry }) {
  const { config, installs, jobs, catalog, get, play, dismiss } = useLauncher();
  const job = jobs[entry.id];
  const install = installs[entry.id];
  if (job && "error" in job)
    return (
      <div className="job-error" role="alert">
        <p>{job.error}</p>
        <div className="row">
          <button onClick={() => get(entry)}>Try again</button>
          <button onClick={() => dismiss(entry.id)}>Dismiss</button>
        </div>
      </div>
    );
  if (job) {
    const percent = job.total ? Math.round((job.received / job.total) * 100) : null;
    return (
      <div className="progress" aria-label={job.phase === "installing" ? "Installing" : "Downloading"}>
        <div className="bar" style={{ width: `${job.phase === "installing" ? 100 : (percent ?? 5)}%` }} />
        <span>{job.phase === "installing" ? "Installing…" : percent === null ? "Downloading…" : `Downloading ${percent}%`}</span>
      </div>
    );
  }
  const pulled = install && catalog[entry.id]?.withdrawn?.find((w) => w.version === install.version);
  if (pulled)
    return (
      <div className="job-error" role="alert">
        <p>
          v{pulled.version} was withdrawn: {pulled.reason}
        </p>
        <div className="row">
          <button onClick={() => get(catalog[entry.id])}>Install v{catalog[entry.id].verified?.version}</button>
          <button onClick={() => play(entry.id)}>Play anyway</button>
        </div>
      </div>
    );
  if (install && hasUpdate(catalog[entry.id] ?? entry, install))
    return (
      <div className="row">
        <button className="primary" onClick={() => play(entry.id)}>
          <Play size={15} /> Play
        </button>
        <button onClick={() => get(catalog[entry.id] ?? entry)}>Update to v{(catalog[entry.id] ?? entry).verified?.version}</button>
      </div>
    );
  if (install)
    return (
      <button className="primary wide" onClick={() => play(entry.id)}>
        <Play size={15} /> Play
      </button>
    );
  if (!availableOn(entry, config.os))
    return <p className="unavailable">Not available for {OS_NAMES[config.os] ?? config.os}</p>;
  return (
    <button className="primary wide" onClick={() => get(entry)}>
      <Download size={15} /> Get
    </button>
  );
}

function Detail({ entry, onClose }: { entry: Entry; onClose: () => void }) {
  const { library, installs, remove } = useLauncher();
  const inLibrary = library.some((i) => i.id === entry.id);
  const install = installs[entry.id];
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="overlay" onClick={onClose}>
      <section className="detail" role="dialog" aria-label={entry.projectName} onClick={(e) => e.stopPropagation()}>
        <button className="close" onClick={onClose} aria-label="Close">
          <X size={18} />
        </button>
        <div className="detail-hero">
          <Artwork src={entry.libraryArt?.hero || coverOf(entry)} name={entry.projectName} className="cover-photo" />
        </div>
        <div className="detail-body">
          <h2>{entry.projectName}</h2>
          {entry.games.length > 0 && <p className="muted">Based on {entry.games.map((g) => g.title).join(", ")}</p>}
          <div className="facts">
            <PlatformIcons os={entry.supportedOS} />
            <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />
            {entry.verified && (
              <span className="verified">
                <ShieldCheck size={14} />
                {entry.verified.pinned ? `v${entry.verified.version} · verified` : `v${entry.verified.version} · files can't be verified`}
              </span>
            )}
            {install && <span className="muted">Installed v{install.version}</span>}
          </div>
          <p className="description">{entry.description}</p>
          <div className="row">
            <Action entry={entry} />
            {inLibrary && (
              <button
                className="danger"
                onClick={() => {
                  remove(entry.id);
                  onClose();
                }}
              >
                <Trash2 size={15} /> {install ? "Uninstall and remove" : "Remove from library"}
              </button>
            )}
          </div>
        </div>
      </section>
    </div>
  );
}

function ChooseFile() {
  const { choice } = useLauncher();
  if (!choice) return null;
  return (
    <div className="overlay">
      <section className="detail choose" role="dialog" aria-label="Choose a download">
        <div className="detail-body">
          <h2>Which download?</h2>
          <p className="muted">
            {choice.entry.projectName} v{choice.version} has more than one file for your computer.
          </p>
          <div className="choices">
            {choice.assets.map((a) => (
              <button key={a.id} onClick={() => choice.resolve(a)}>
                {a.filename}
              </button>
            ))}
          </div>
          <button onClick={() => choice.resolve(null)}>Cancel</button>
        </div>
      </section>
    </div>
  );
}

function useDebounced<T>(value: T, ms: number) {
  const [current, setCurrent] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setCurrent(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return current;
}
