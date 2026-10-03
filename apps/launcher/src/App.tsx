import { useEffect, useState } from "react";
import { Download, Library, Play, Search, Settings2, ShieldCheck, Trash2, X } from "lucide-react";
import type { Entry, Page } from "@quiver/api";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, Score, coverOf } from "@quiver/ui";
import { availableOn, hasUpdate, useLauncher, type LibraryItem } from "./store";
import { useAccount } from "./account";
import { native } from "./native";
import { isCustom } from "./custom";

type Tab = "library" | "browse";

export function App() {
  const { library } = useLauncher();
  // A new player starts in the catalog; everyone else in their library.
  const [tab, setTab] = useState<Tab>(library.length ? "library" : "browse");
  const [open, setOpen] = useState<Entry | null>(null);
  const [signingIn, setSigningIn] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
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
        <AccountButton onSignIn={() => setSigningIn(true)} />
        <button className="icon" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
          <Settings2 size={18} />
        </button>
      </header>
      <main>
        <Notice />
        <OldLibrary onDone={() => setTab("library")} />
        {tab === "library" ? (
          <LibraryPage onOpen={setOpen} onBrowse={() => setTab("browse")} />
        ) : (
          <BrowsePage onOpen={setOpen} />
        )}
      </main>
      {open && <Detail entry={open} onClose={() => setOpen(null)} onSignIn={() => setSigningIn(true)} />}
      {signingIn && <SignIn onClose={() => setSigningIn(false)} />}
      {settingsOpen && <SettingsDialog onClose={() => setSettingsOpen(false)} />}
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
  const { library, catalog, installs, jobs, get, add, remove } = useLauncher();
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<"played" | "name" | "added">("played");
  const updates = library.filter((i) => !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id]));
  const words = search.toLowerCase().split(/\s+/).filter(Boolean);
  const order = {
    played: (i: LibraryItem) => -(installs[i.id]?.lastPlayed ?? 0),
    added: (i: LibraryItem) => -i.addedAt,
    name: () => 0,
  }[sort];
  const items = library
    .flatMap((i) => (catalog[i.id] ? [{ item: i, entry: withOverrides(catalog[i.id], i.overrides) }] : []))
    .filter(({ entry, item }) => {
      const text = [entry.projectName, ...entry.games.map((g) => g.title), ...entry.tags, ...(item.overrides?.tags ?? [])].join(" ").toLowerCase();
      return words.every((w) => text.includes(w));
    })
    .sort((a, b) => order(a.item) - order(b.item) || a.entry.projectName.localeCompare(b.entry.projectName))
    .map(({ entry }) => entry);
  // Removed from the library (here, elsewhere or by signing out) but its files are still here.
  const loose = Object.keys(installs).flatMap((id) => (catalog[id] && !library.some((i) => i.id === id) ? [catalog[id]] : []));
  if (!library.length && !loose.length)
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
        {updates.length > 0 && (
          <button className="primary" onClick={() => updates.forEach((i) => get(catalog[i.id]))}>
            Update all ({updates.length})
          </button>
        )}
      </div>
      <div className="catalog-grid">
        {items.map((entry) => (
          <EntryCard key={entry.id} entry={entry} onOpen={() => onOpen(entry)} action={<Action entry={entry} />} />
        ))}
      </div>
      {loose.length > 0 && (
        <section className="loose">
          <h2>Installed, not in your library</h2>
          <div className="catalog-grid">
            {loose.map((entry) => (
              <EntryCard
                key={entry.id}
                entry={entry}
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
    </>
  );
}

/** The catalog entry as this player named and pictured it. */
const withOverrides = (entry: Entry, o?: { name?: string; cover?: string }): Entry => ({
  ...entry,
  ...(o?.name ? { projectName: o.name } : {}),
  ...(o?.cover ? { libraryArt: { ...entry.libraryArt, header: o.cover, hero: o.cover } } : {}),
});

/** An app the catalog doesn't list, straight from its GitHub repository. */
function AddRepository({ onOpen }: { onOpen: (e: Entry) => void }) {
  const { addRepository } = useLauncher();
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!open) return <button onClick={() => setOpen(true)}>Add from GitHub</button>;
  return (
    <form
      className="add-repo"
      onSubmit={(e) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        addRepository(input).then((r) => {
          setBusy(false);
          if ("error" in r) return setError(r.error);
          setOpen(false);
          setInput("");
          onOpen(r.entry);
        });
      }}
    >
      <input autoFocus aria-label="GitHub repository" placeholder="owner/name or github.com address" value={input} onChange={(e) => setInput(e.target.value)} />
      <button className="primary" disabled={busy}>
        Add
      </button>
      <button type="button" onClick={() => setOpen(false)}>
        Cancel
      </button>
      {error && <p className="job-error">{error}</p>}
    </form>
  );
}

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
        <AddRepository onOpen={onOpen} />
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
  if (!entry.verified) return <p className="unavailable">No approved release yet</p>;
  return (
    <button className="primary wide" onClick={() => get(entry)}>
      <Download size={15} /> Get
    </button>
  );
}

function Detail({ entry: opened, onClose, onSignIn }: { entry: Entry; onClose: () => void; onSignIn: () => void }) {
  const { library, installs, catalog, remove } = useLauncher();
  const item = library.find((i) => i.id === opened.id);
  const inLibrary = Boolean(item);
  // The player's own name and artwork, live as they change them.
  const entry = withOverrides(catalog[opened.id] ?? opened, item?.overrides);
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
          {item && <Customize id={item.id} />}
          {!isCustom(entry.id) && <Review entry={entry} onSignIn={onSignIn} />}
        </div>
      </section>
    </div>
  );
}

/** Overrides the catalog's name and artwork for this player; empty shows the catalog's again. */
function Customize({ id }: { id: string }) {
  const { library, catalog, customize } = useLauncher();
  const current = library.find((i) => i.id === id)?.overrides ?? {};
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(current.name ?? "");
  const [cover, setCover] = useState(current.cover ?? "");
  if (!open) return <button onClick={() => setOpen(true)}>Change name or artwork</button>;
  const badCover = cover.trim() !== "" && !/^https:\/\/\S+$/.test(cover.trim());
  return (
    <form
      className="customize"
      onSubmit={(e) => {
        e.preventDefault();
        if (badCover) return;
        customize(id, { name: name.trim() || undefined, cover: cover.trim() || undefined });
        setOpen(false);
      }}
    >
      <input aria-label="Name" maxLength={200} placeholder={catalog[id]?.projectName} value={name} onChange={(e) => setName(e.target.value)} />
      <input aria-label="Artwork address" placeholder="Artwork address (https://…)" value={cover} onChange={(e) => setCover(e.target.value)} />
      {badCover && <p className="job-error">Use an https:// image address.</p>}
      <div className="row">
        <button className="primary">Save</button>
        <button type="button" onClick={() => (setName(""), setCover(""))}>
          Use the catalog's
        </button>
      </div>
    </form>
  );
}

const RESULTS = [
  ["runs", "Runs well"],
  ["issues", "Runs with issues"],
  ["broken", "Doesn't run"],
] as const;

/** Signed in, a player who installed the app can say how it runs, for the release they have. */
function Review({ entry, onSignIn }: { entry: Entry; onSignIn: () => void }) {
  const { user, review } = useAccount();
  const { installs, config, catalog } = useLauncher();
  const [result, setResult] = useState<(typeof RESULTS)[number][0] | null>(null);
  const [body, setBody] = useState("");
  const [state, setState] = useState<"idle" | "sending" | "sent" | string>("idle");
  const install = installs[entry.id];
  if (!install) return null;
  if (!user)
    return (
      <div className="review">
        <button onClick={onSignIn}>Sign in to review</button>
      </div>
    );
  if (state === "sent") return <p className="review muted">Thanks, your review is posted.</p>;
  return (
    <form
      className="review"
      onSubmit={(e) => {
        e.preventDefault();
        if (!result || config.os === "unknown") return;
        setState("sending");
        // The site only takes a release that's still published.
        const pulled = catalog[entry.id]?.withdrawn?.some((w) => w.version === install.version);
        const entryReleaseId = pulled ? undefined : install.releaseId;
        review({ entryId: entry.id, result, body: body.trim() || undefined, platform: config.os, entryReleaseId }).then(
          () => setState("sent"),
          (error) => setState(error instanceof Error ? error.message.replace(/^.*ConvexError: /, "") : "Couldn't post your review."),
        );
      }}
    >
      <h3>How does v{install.version} run for you?</h3>
      <div className="row">
        {RESULTS.map(([value, label]) => (
          <button type="button" key={value} className={result === value ? "chosen" : ""} onClick={() => setResult(value)}>
            {label}
          </button>
        ))}
      </div>
      <textarea maxLength={500} placeholder="Anything others should know? (optional)" value={body} onChange={(e) => setBody(e.target.value)} />
      {state !== "idle" && state !== "sending" && <p className="job-error">{state}</p>}
      <button className="primary" disabled={!result || state === "sending"}>
        Post review
      </button>
    </form>
  );
}

function AccountButton({ onSignIn }: { onSignIn: () => void }) {
  const { ready, user } = useAccount();
  const { unsynced, signOut } = useLauncher();
  const [asked, setAsked] = useState(false);
  if (!ready) return null;
  return (
    <div className="account">
      {user ? (
        <>
          <span className="muted">{asked ? `${unsynced} ${unsynced === 1 ? "change hasn't" : "changes haven't"} synced yet.` : user.name}</span>
          <button onClick={() => (unsynced && !asked ? setAsked(true) : signOut().then(() => setAsked(false)))}>
            {asked ? "Sign out anyway" : "Sign out"}
          </button>
          {asked && <button onClick={() => setAsked(false)}>Stay signed in</button>}
        </>
      ) : (
        <button onClick={onSignIn}>Sign in</button>
      )}
    </div>
  );
}

/** Signing in is optional: it syncs the library and lets players review apps. */
function SignIn({ onClose }: { onClose: () => void }) {
  const { signIn, signInWith } = useAccount();
  const [create, setCreate] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  return (
    <div className="overlay" onClick={onClose}>
      <section className="detail choose" role="dialog" aria-label="Sign in" onClick={(e) => e.stopPropagation()}>
        <form
          className="detail-body"
          onSubmit={(e) => {
            e.preventDefault();
            setBusy(true);
            signIn(username.trim(), password, create)
              .then((message) => (message ? setError(message) : onClose()))
              .finally(() => setBusy(false));
          }}
        >
          <h2>{create ? "Create your Quiver account" : "Sign in to Quiver"}</h2>
          <p className="muted">Your library syncs across your devices, and you can review apps. Everything works without an account too.</p>
          {(["github", "discord"] as const).map((provider) => (
            <button
              key={provider}
              type="button"
              disabled={busy}
              onClick={() => {
                setBusy(true);
                setWaiting(true);
                setError(null);
                signInWith(provider)
                  .then((message) => (message ? setError(message) : onClose()))
                  .finally(() => (setBusy(false), setWaiting(false)));
              }}
            >
              Continue with {provider === "github" ? "GitHub" : "Discord"}
            </button>
          ))}
          {waiting && <p className="muted">Finish signing in in your browser.</p>}
          <p className="muted or">or</p>
          <input autoFocus required placeholder="Username" autoComplete="username" value={username} onChange={(e) => setUsername(e.target.value)} />
          <input
            required
            type="password"
            placeholder="Password"
            autoComplete={create ? "new-password" : "current-password"}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          {error && <p className="job-error">{error}</p>}
          <button className="primary" disabled={busy}>
            {create ? "Create account" : "Sign in"}
          </button>
          <button type="button" onClick={() => (setCreate(!create), setError(null))}>
            {create ? "I already have an account" : "Create an account"}
          </button>
        </form>
      </section>
    </div>
  );
}

/** Preferences for this computer. */
function SettingsDialog({ onClose }: { onClose: () => void }) {
  const { settings, setSettings, config } = useLauncher();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="overlay" onClick={onClose}>
      <section className="detail choose" role="dialog" aria-label="Settings" onClick={(e) => e.stopPropagation()}>
        <div className="detail-body settings">
          <h2>Settings</h2>
          <label className="toggle">
            <input
              type="checkbox"
              checked={Boolean(settings.fullscreen)}
              onChange={(e) => {
                setSettings({ ...settings, fullscreen: e.target.checked });
                void native.setFullscreen(e.target.checked);
              }}
            />
            Full screen (good for a TV or Steam Deck)
          </label>
          <label>
            Interface size{" "}
            <select value={settings.scale ?? 1} onChange={(e) => setSettings({ ...settings, scale: Number(e.target.value) })}>
              <option value={0.9}>Small</option>
              <option value={1}>Normal</option>
              <option value={1.15}>Large</option>
              <option value={1.3}>Extra large</option>
            </select>
          </label>
          <p className="muted">Apps are installed in {config.appsDir}</p>
          <button className="primary" onClick={onClose}>
            Done
          </button>
        </div>
      </section>
    </div>
  );
}

function Notice() {
  const { notice, setNotice } = useLauncher();
  if (!notice) return null;
  return (
    <div className="banner" role="status">
      <p>{notice}</p>
      <button onClick={() => setNotice(null)}>OK</button>
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
