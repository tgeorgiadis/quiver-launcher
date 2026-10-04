import { useEffect, useState } from "react";
import { ArrowLeft, Download, Play, Search, Settings2, ShieldCheck, Trash2 } from "lucide-react";
import type { AppQuery, Entry, Page } from "@quiver/api";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, Score } from "@quiver/ui";
import { availableOn, hasUpdate, useLauncher } from "./store";
import { LibraryPage, withOverrides } from "./library";
import { Readme, RepositoryLink, Shortcuts, Tags, Versions, useSource } from "./detail";
import { ControlSettings } from "./controls";
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
          <button className={tab === "library" ? "active" : ""} onClick={() => (setTab("library"), setOpen(null))}>
            Library <span className="count">{library.length}</span>
          </button>
          <button className={tab === "browse" ? "active" : ""} onClick={() => (setTab("browse"), setOpen(null))}>
            Browse
          </button>
        </nav>
        <AccountButton onSignIn={() => setSigningIn(true)} />
        <button className="icon" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
          <Settings2 size={18} />
        </button>
      </header>
      <main className={open ? "app-main" : ""}>
        {/* Kept mounted under an app's page, so going back keeps the filters and scroll. */}
        <div hidden={Boolean(open)}>
          <Notice />
          <OldLibrary onDone={() => setTab("library")} />
          {tab === "library" ? (
            <LibraryPage onOpen={setOpen} onBrowse={() => setTab("browse")} action={(entry) => <Action entry={entry} />} />
          ) : (
            <BrowsePage onOpen={setOpen} />
          )}
        </div>
        {open && (
          <AppPage
            key={open.id}
            entry={open}
            back={tab === "library" ? "Library" : "Browse"}
            onClose={() => setOpen(null)}
            onSignIn={() => setSigningIn(true)}
          />
        )}
      </main>
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
  const { client, config, library, remember, consoles, consoleNames } = useLauncher();
  const [search, setSearch] = useState("");
  // The site's catalog filters; the platform starts at this computer's.
  const [filters, setFilters] = useState<Omit<AppQuery, "search" | "cursor" | "limit">>({ os: config.os, sort: "added" });
  const [pages, setPages] = useState<Page<Entry>[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const query = useDebounced(search, 250);
  const searching = Boolean(query.trim());

  function load(cursor?: string | null) {
    setLoading(true);
    setError(null);
    client
      // A search orders by relevance.
      .apps({ ...filters, search: query, sort: searching ? undefined : filters.sort, cursor })
      .then((page) => {
        remember(page.items);
        setPages((p) => (cursor ? [...p, page] : [page]));
      })
      .catch(() => setError("Couldn't reach quiverlauncher.com. Check your connection and try again."))
      .finally(() => setLoading(false));
  }
  useEffect(() => load(), [query, JSON.stringify(filters), client]); // eslint-disable-line react-hooks/exhaustive-deps
  const set = (key: keyof typeof filters) => (e: { target: { value: string } }) => setFilters({ ...filters, [key]: e.target.value || undefined });
  const brands = [...new Set(consoles.map((c) => c.brand))];

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
        <AddRepository onOpen={onOpen} />
      </div>
      <div className="toolbar filters">
        <select aria-label="Sort" value={searching ? "" : (filters.sort ?? "added")} disabled={searching} onChange={set("sort")}>
          {searching && <option value="">Most relevant</option>}
          <option value="added">Recently added</option>
          <option value="updated">Recently updated</option>
          <option value="rating">Top rated</option>
          <option value="name">Name A–Z</option>
        </select>
        <select aria-label="Project type" value={filters.projectType ?? ""} onChange={set("projectType")}>
          <option value="">All project types</option>
          <option value="port">Port</option>
          <option value="tool">Tool</option>
          <option value="emulator">Emulator</option>
          <option value="game">Standalone game</option>
        </select>
        <select aria-label="Platform" value={filters.os ?? ""} onChange={set("os")}>
          <option value="">All platforms</option>
          {Object.entries(OS_NAMES).map(([id, name]) => (
            <option key={id} value={id}>
              {name}
              {id === config.os ? " (this computer)" : ""}
            </option>
          ))}
        </select>
        <select aria-label="Console" value={filters.console ?? ""} onChange={set("console")}>
          <option value="">All consoles</option>
          {brands.map((brand) => (
            <optgroup key={brand} label={brand === "OtherPlatforms" ? "Other platforms" : brand}>
              <option value={`maker:${brand}`}>{brand === "OtherPlatforms" ? "All other platforms" : `All ${brand}`}</option>
              {consoles
                .filter((c) => c.brand === brand)
                .map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
            </optgroup>
          ))}
        </select>
        <select aria-label="AI use" value={filters.ai ?? ""} onChange={set("ai")}>
          <option value="">Show all apps</option>
          <option value="no-generated">Hide mostly AI-generated apps</option>
          <option value="no-ai">Hide apps with any AI use</option>
        </select>
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
                consoleNames={consoleNames}
                onOpen={() => onOpen(entry)}
                badge={inLibrary.has(entry.id) ? "In library" : undefined}
                action={<Action entry={entry} />}
              />
            ))}
          </div>
          {!loading && !entries.length && <p className="empty">Nothing matches that search and those filters.</p>}
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

/** An app's own page, laid out like its page on quiverlauncher.com. */
function AppPage({ entry: opened, back, onClose, onSignIn }: { entry: Entry; back: string; onClose: () => void; onSignIn: () => void }) {
  const { library, installs, catalog, remove, consoleNames } = useLauncher();
  const item = library.find((i) => i.id === opened.id);
  // The player's own name and artwork, live as they change them.
  const entry = withOverrides(catalog[opened.id] ?? opened, item?.overrides);
  const install = installs[entry.id];
  const source = useSource(entry);
  const hero = entry.libraryArt?.hero || entry.libraryArt?.header;
  useEffect(() => window.scrollTo(0, 0), []);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && !document.querySelector('[role="dialog"]') && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const eyebrow = [...new Set((entry.consoles ?? []).map((c) => consoleNames[c] ?? c.toUpperCase()))].join(", ");
  return (
    <section className="app-page" aria-label={entry.projectName}>
      <div className={`app-backdrop${hero ? " has-art" : ""}`}>
        {hero && <img className="backdrop-art" src={hero} alt="" />}
        <div className="backdrop-inner">
          <button className="back-link" onClick={onClose}>
            <ArrowLeft size={15} /> {back}
          </button>
          <div className="app-hero">
            <Artwork src={entry.artwork ?? entry.libraryArt?.logo} name={entry.projectName} />
            <div>
              <div className="eyebrow">{[eyebrow, PROJECT_TYPES[entry.projectType]].filter(Boolean).join(" / ").toUpperCase()}</div>
              <h1>{entry.projectName}</h1>
              {entry.description && <p className="app-tagline">{entry.description}</p>}
              {entry.games.length > 0 && (
                <div className="based-on">
                  <span className="based-on-label">Based on</span>
                  {entry.games.map((g) => (
                    <span key={g.id} className="game-chip">
                      {g.title}
                    </span>
                  ))}
                </div>
              )}
              <div className="facts">
                <PlatformIcons os={entry.supportedOS} />
                <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />
                {entry.verified && (
                  <span className="verified">
                    <ShieldCheck size={14} />
                    {entry.verified.pinned ? `v${entry.verified.version} · verified` : `v${entry.verified.version} · files can't be verified`}
                  </span>
                )}
                {install && <span className="muted">Installed {/^v/i.test(install.version) ? "" : "v"}{install.version}</span>}
              </div>
              <div className="app-actions">
                <Action entry={entry} />
                {source && <RepositoryLink source={source} />}
                {item && (
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
          </div>
        </div>
      </div>
      <div className="app-columns">
        <div className="app-content">
          <Readme entry={entry} source={source} />
          {!isCustom(entry.id) && <Review entry={entry} onSignIn={onSignIn} />}
        </div>
        {(item || install) && (
          <aside className="app-panel" aria-label="On this computer">
            <h3>On this computer</h3>
            <Shortcuts entry={entry} />
            {item && <AppOptions id={item.id} />}
            {item && <Versions entry={entry} source={source} />}
            {item && <Tags id={item.id} />}
            {item && <Customize id={item.id} />}
          </aside>
        )}
      </div>
    </section>
  );
}

const PROJECT_TYPES: Record<string, string> = { port: "Port", tool: "Tool", emulator: "Emulator", game: "Standalone game" };

/** This computer's choices for an app: how it updates, and whether the library shows it. */
function AppOptions({ id }: { id: string }) {
  const { installs, setUpdates, settings, setSettings } = useLauncher();
  const install = installs[id];
  const hidden = settings.hidden?.includes(id);
  return (
    <div className="row options">
      {install && (
        <label>
          Updates{" "}
          <select value={install.updates ?? "ask"} onChange={(e) => setUpdates(id, e.target.value === "ask" ? undefined : (e.target.value as "auto" | "pinned"))}>
            <option value="ask">Offer them</option>
            <option value="auto">Install automatically</option>
            <option value="pinned">Stay on v{install.version}</option>
          </select>
        </label>
      )}
      <button
        onClick={() =>
          setSettings({ ...settings, hidden: hidden ? settings.hidden!.filter((h) => h !== id) : [...(settings.hidden ?? []), id] })
        }
      >
        {hidden ? "Show in library" : "Hide from library"}
      </button>
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
          <ControlSettings />
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
