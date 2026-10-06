import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ChevronDown, Compass, Download, FolderOpen, Library, Pin, Play, Plus, Search, Settings2, ShieldAlert, ShieldCheck, ShieldX, SlidersHorizontal, Trash2, X } from "lucide-react";
import type { AppQuery, Entry, GameMatch, Page, ProjectType } from "@quiverlauncher/api";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, ReleaseBadge, Score } from "@quiverlauncher/ui";
import { availableOn, hasUpdate, skipped, useLauncher } from "./store";
import { FilterPill, LibraryPage, filterLabel, withOverrides } from "./library";
import { ViewOptions } from "./library-view";
import { CheckingStatus, ProjectDetails, Readme, ReleasesTab, RepositoryLink, Shortcuts, Tags, Versions, checkingOf, useAppDetail, useReleaseHistory, useReleases } from "./detail";
import { versionLabel } from "./versions";
import { FeedbackTab, ReportPrompt, useOwnFeedback, type Intent } from "./feedback";
import { GamePage, GamesSection, gameVersionLabel, type GameLink } from "./game";
import { ErrorBoundary } from "./boundary";
import { ControlSettings } from "./controls";
import { track } from "./telemetry";
import { TelemetryNotice, TelemetrySetting, useTelemetrySetup } from "./telemetry-ui";
import { useAccount, type CatalogDefaults } from "./account";
import { native } from "./native";
import { isLocal, isOwn } from "./custom";
import { AddApp } from "./own";
import type { Theme } from "./theme";

type Tab = "library" | "browse";
/** A page over the library or catalog: an app's, or an original game's. */
type View = { kind: "app"; entry: Entry } | { kind: "game"; game: GameLink };
const viewKey = (v: View) => (v.kind === "app" ? `app:${v.entry.id}` : `game:${v.game.slug}`);
const shorten = (text: string) => (text.length > 40 ? `${text.slice(0, 39).trimEnd()}…` : text);
/** "v1.2" for "1.2" or "v1.2": releases are tagged either way. */
/** "v1.2" and "1.2" are the same release. */
const bare = (version: string) => version.trim().replace(/^v/i, "");

export function App() {
  useTelemetrySetup();
  const { library } = useLauncher();
  // A new player starts in the catalog; everyone else in their library.
  const [tab, setTab] = useState<Tab>(library.length ? "library" : "browse");
  // Pages opened from the list, each over the one before: Back goes down one.
  const [stack, setStack] = useState<View[]>([]);
  const [search, setSearch] = useState("");
  const [signingIn, setSigningIn] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [adding, setAdding] = useState(false);
  // Where each covered page was scrolled to and what had focus, to put back on the way back.
  const covered = useRef<{ y: number; focus: Element | null }[]>([]);
  const open = useCallback((view: View) => {
    const here = { y: window.scrollY, focus: document.activeElement };
    setStack((s) => {
      covered.current[s.length] = here;
      // A page already open further down is gone back to, so the stack stays short.
      const i = s.findIndex((v) => viewKey(v) === viewKey(view));
      return i >= 0 ? s.slice(0, i + 1) : [...s, view];
    });
  }, []);
  const openApp = useCallback((entry: Entry) => open({ kind: "app", entry }), [open]);
  const openGame = useCallback((game: GameLink) => open({ kind: "game", game }), [open]);
  const goBack = useCallback(() => setStack((s) => s.slice(0, -1)), []);
  const switchTab = (next: Tab) => {
    setTab(next);
    setStack([]);
    covered.current = [];
  };
  const depth = useRef(0);
  useLayoutEffect(() => {
    const back = stack.length < depth.current;
    depth.current = stack.length;
    const at = back ? covered.current[stack.length] : undefined;
    // Braces matter: scrollTo returns a Promise in newer WebView2, and an effect's return value is its cleanup.
    window.scrollTo(0, at?.y ?? 0);
    if (at?.focus instanceof HTMLElement && at.focus.isConnected) at.focus.focus({ preventScroll: true });
  }, [stack]);
  // Escape, or a controller's Back, goes back a page; an open dialog takes it instead.
  // Listening from the start puts this ahead of any dialog's own listener, so a dialog closing on the same Escape still counts.
  const pages = useRef(0);
  useLayoutEffect(() => {
    pages.current = stack.length;
  }, [stack]);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && pages.current && !document.querySelector('[role="dialog"]')) goBack();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [goBack]);
  const top = stack.at(-1);
  // Usage data: the screen shown, a catalog app's or game's by its slug (never one the player added).
  const screen = !top ? tab : top.kind === "app" ? `app:${isOwn(top.entry.id) ? "" : top.entry.slug}` : `game:${top.game.slug}`;
  useEffect(() => {
    const [name, slug] = screen.split(":");
    track("screen_viewed", { screen: name, ...(slug !== undefined ? { slug: slug || null } : {}) });
  }, [screen]);
  useEffect(() => void (settingsOpen && track("screen_viewed", { screen: "settings" })), [settingsOpen]);
  useEffect(() => void (adding && track("screen_viewed", { screen: "add_app" })), [adding]);
  useEffect(() => void (signingIn && track("screen_viewed", { screen: "sign_in" })), [signingIn]);
  const query = search.trim();
  const listName = tab === "library" ? "Library" : query ? `Back to results for “${shorten(query)}”` : "Browse";
  /** What Back on the page at `i` returns to. */
  const backFrom = (i: number) => {
    const below = stack[i - 1];
    return !below ? listName : below.kind === "app" ? below.entry.projectName : (below.game.title ?? "Back");
  };
  const action = (entry: Entry) => <Action entry={entry} />;
  return (
    <div className="shell">
      <header className="topbar">
        {/* As on the website; it goes to the library. */}
        <button className="brand" aria-label="Quiver Launcher: go to your library" onClick={() => switchTab("library")}>
          <img src="/quiver-icon-96.png" alt="" width="34" height="34" />
          <span>
            Quiver<span className="brand-label">LAUNCHER</span>
          </span>
        </button>
        <nav>
          <button className={tab === "library" ? "active" : ""} onClick={() => switchTab("library")}>
            <Library size={16} aria-hidden="true" /> Library <span className="count">{library.length}</span>
          </button>
          <button className={tab === "browse" ? "active" : ""} onClick={() => switchTab("browse")}>
            <Compass size={16} aria-hidden="true" /> Browse
          </button>
        </nav>
        <AccountButton onSignIn={() => setSigningIn(true)} />
        <button className="icon" aria-label="Settings" onClick={() => setSettingsOpen(true)}>
          <Settings2 size={18} />
        </button>
      </header>
      <main className={top ? "app-main" : ""}>
        {/*
          Every page in the stack stays mounted, hidden under the top one, so
          Back finds it as it was left. One that fails shows why and a way back;
          the boundary around them all catches a page failing as it closes, and
          OK brings back the pages still open.
        */}
        <ErrorBoundary
          resetKey={stack.map(viewKey).join(" ")}
          fallback={(error, reset) => (
            <div className="banner" role="alert">
              <p>That page hit a problem as it closed: {error.message}</p>
              <button onClick={reset}>OK</button>
            </div>
          )}
        >
          {stack.map((view, i) => (
            <div key={viewKey(view)} className="page-layer" hidden={i !== stack.length - 1}>
              <ErrorBoundary fallback={(error) => <PageProblem error={error} back={backFrom(i)} onBack={goBack} />}>
                {view.kind === "app" ? (
                  <AppPage entry={view.entry} back={backFrom(i)} onClose={goBack} onOpenGame={openGame} onSignIn={() => setSigningIn(true)} />
                ) : (
                  <GamePage game={view.game} back={backFrom(i)} onBack={goBack} onOpenApp={openApp} action={action} />
                )}
              </ErrorBoundary>
            </div>
          ))}
        </ErrorBoundary>
        {/* Kept mounted under the pages too, so going back keeps the search, filters and scroll. */}
        <div hidden={Boolean(top)}>
          <TelemetryNotice />
          <Notice />
          {/* Finishing an import shows the library, under whatever page is open. */}
          <OldLibrary
            onDone={() => {
              setTab("library");
              // Back from the page shows the library from its top, not where Browse was scrolled.
              covered.current[0] = { y: 0, focus: null };
            }}
          />
          {tab === "library" ? (
            <LibraryPage onOpen={openApp} onBrowse={() => switchTab("browse")} onAdd={() => setAdding(true)} onSignIn={() => setSigningIn(true)} action={action} />
          ) : (
            <BrowsePage onOpen={openApp} onOpenGame={openGame} onAdd={() => setAdding(true)} search={search} onSearch={setSearch} />
          )}
        </div>
      </main>
      {signingIn && <SignIn onClose={() => setSigningIn(false)} />}
      {settingsOpen && <SettingsDialog onClose={() => setSettingsOpen(false)} />}
      {adding && <AddApp onClose={() => setAdding(false)} onOpen={openApp} />}
      <ChooseFile />
    </div>
  );
}

/** In place of a page that failed: what went wrong, and the way back. */
function PageProblem({ error, back, onBack }: { error: Error; back: string; onBack: () => void }) {
  return (
    <section className="app-page" role="alert">
      <div className="backdrop-inner">
        <button className="back-link" onClick={onBack}>
          <ArrowLeft size={15} /> {back}
        </button>
      </div>
      <div className="empty">
        <h2>This page hit a problem</h2>
        <p>{error.message}</p>
        <button className="primary" onClick={onBack}>
          Go back
        </button>
      </div>
    </section>
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

function BrowsePage({
  onOpen,
  onOpenGame,
  onAdd,
  search,
  onSearch,
}: {
  onOpen: (e: Entry) => void;
  onOpenGame: (game: GameLink) => void;
  onAdd: () => void;
  search: string;
  onSearch: (search: string) => void;
}) {
  const { client, config, library, remember, consoles, consoleNames } = useLauncher();
  const saved = useAccount().user?.catalogDefaults;
  // The site's catalog filters: the ones saved on the account ("Save as my
  // default" on the website), but the platform starts at this computer's,
  // since only its apps can be installed here.
  const fromSaved = (d?: CatalogDefaults): BrowseFilters => ({
    os: config.os,
    sort: SORTS.find((s) => s === d?.sort) ?? "added",
    projectTypes: d?.projectTypes?.filter((t): t is ProjectType => t in PROJECT_TYPES).slice(0, 4),
    console: d?.console,
    ai: d?.ai === "no-generated" || d?.ai === "no-ai" ? d.ai : undefined,
  });
  const [filters, setFilters] = useState(() => fromSaved(saved));
  // The account can load after Browse opens: its saved filters apply until the player picks their own.
  const picked = useRef(false);
  useEffect(() => {
    if (!picked.current) setFilters(fromSaved(saved));
  }, [JSON.stringify(saved)]); // eslint-disable-line react-hooks/exhaustive-deps
  const choose = (next: BrowseFilters) => {
    picked.current = true;
    setFilters(next);
  };
  const [pages, setPages] = useState<Page<Entry>[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [games, setGames] = useState<GameMatch[]>([]);
  // The search whose games are in.
  const [gamesFor, setGamesFor] = useState<string | null>(null);
  const query = useDebounced(search, 250);
  const searching = Boolean(query.trim());
  // Only the newest request's answer is shown: an older one can arrive after it.
  const latest = useRef(0);

  function load(cursor?: string | null) {
    const request = ++latest.current;
    setLoading(true);
    setError(null);
    client
      // A search orders by relevance.
      .apps({ ...filters, search: query, sort: searching ? undefined : filters.sort, cursor })
      .then((page) => {
        if (request !== latest.current) return;
        remember(page.items);
        setPages((p) => (cursor ? [...p, page] : [page]));
      })
      .catch(() => request === latest.current && setError("Couldn't reach quiverlauncher.com. Check your connection and try again."))
      .finally(() => request === latest.current && setLoading(false));
  }
  useEffect(() => {
    load();
  }, [query, JSON.stringify(filters), client]); // eslint-disable-line react-hooks/exhaustive-deps
  // As on the website: games whose titles match the search, above the apps, whatever the filters.
  useEffect(() => {
    // As on the website, the last search's games go as soon as the search changes.
    setGames([]);
    setGamesFor(null);
    if (query.trim().length < 2) return setGamesFor(query);
    let live = true;
    client.matchingGames(query).then(
      (found) => live && (setGames(found), setGamesFor(query)),
      () => live && (setGames([]), setGamesFor(query)),
    );
    return () => {
      live = false;
    };
  }, [query, client]);
  // Usage data: a search that found no apps, by its length only, once its games are in too.
  const reported = useRef<string | null>(null);
  useEffect(() => {
    const typed = query.trim();
    if (!typed || loading || error || gamesFor !== query || pages.length !== 1 || pages[0].items.length || reported.current === typed) return;
    reported.current = typed;
    const filtered = filtering > 0;
    track("search_no_results", { query_length: typed.length, results: 0, games: games.length, filtered });
  }, [query, loading, error, gamesFor, pages, games]); // eslint-disable-line react-hooks/exhaustive-deps
  const set = (key: keyof typeof filters) => (e: { target: { value: string } }) => choose({ ...filters, [key]: e.target.value || undefined });
  const brands = [...new Set(consoles.map((c) => c.brand))];
  const [filtersOpen, setFiltersOpen] = useState(false);
  // How many filters differ from the defaults: any platform but this computer's counts.
  const filtering = [filters.projectTypes?.length, filters.console, filters.ai, filters.os !== config.os].filter(Boolean).length;
  const types = filters.projectTypes ?? [];
  /** A filter's pill: `on` names it while it differs from the default, and removing it goes back to the default. */
  const pill = (key: "os" | "console" | "projectTypes" | "ai", on: string | undefined, select: ReactNode) =>
    !on && !filtersOpen ? null : (
      <FilterPill key={key} on={on} onClear={() => choose({ ...filters, [key]: key === "os" ? config.os : undefined })}>
        {select}
      </FilterPill>
    );

  const inLibrary = new Set(library.map((i) => i.id));
  const entries = pages.flatMap((p) => p.items);
  const last = pages.at(-1);
  return (
    <>
      {/* As the library's: the search, then sort, Filters and Add an app, all one height. */}
      <div className="browse-bar">
        <label className="search browse-search">
          <Search size={15} />
          <input
            autoFocus
            aria-label="Search the catalog"
            placeholder="Search ports, games and tools"
            value={search}
            onChange={(e) => onSearch(e.target.value)}
          />
          {search && (
            <button type="button" className="search-clear" aria-label="Clear search" onClick={() => onSearch("")}>
              <X size={13} />
            </button>
          )}
        </label>
        <div className="browse-tools page-tools">
          <span className="quiet-select">
            <select aria-label="Sort" value={searching ? "" : (filters.sort ?? "added")} disabled={searching} onChange={set("sort")}>
              {searching && <option value="">Most relevant</option>}
              <option value="added">Recently added</option>
              <option value="updated">Recently updated</option>
              <option value="rating">Top rated</option>
              <option value="name">Name A–Z</option>
            </select>
            <ChevronDown size={14} aria-hidden="true" />
          </span>
          <button
            className={`tool${filtersOpen || filtering ? " on" : ""}`}
            aria-expanded={filtersOpen}
            aria-controls="browse-filters"
            onClick={() => setFiltersOpen(!filtersOpen)}
          >
            <SlidersHorizontal size={15} /> Filters
            {filtering > 0 && <span className="tool-count">{filtering}</span>}
          </button>
          <button className="primary add-app" onClick={onAdd}>
            <Plus size={15} /> Add an app
          </button>
        </div>
      </div>
      {/* Filters: pills to pick from while open; the ones that differ from the defaults stay, each removable, while it's closed. */}
      {(filtersOpen || filtering > 0) && (
        <div id="browse-filters" className="filter-bar" role="group" aria-label="Catalog filters">
          {pill(
            "os",
            filters.os === config.os ? undefined : filters.os ? OS_NAMES[filters.os] : "All platforms",
            <select aria-label="Platform" value={filters.os ?? ""} onChange={set("os")}>
              <option value="">All platforms</option>
              {Object.entries(OS_NAMES).map(([id, name]) => (
                <option key={id} value={id}>
                  {name}
                  {id === config.os ? " (this computer)" : ""}
                </option>
              ))}
            </select>,
          )}
          {pill(
            "console",
            filters.console && filterLabel("console", filters.console, consoleNames),
            <select aria-label="Console" value={filters.console ?? ""} onChange={set("console")}>
              <option value="">Any console</option>
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
            </select>,
          )}
          {pill(
            "projectTypes",
            types.length ? types.map((t) => filterLabel("projectType", t, consoleNames)).join(", ") : undefined,
            <select
              aria-label="Project type"
              value={types.length > 1 ? "several" : (types[0] ?? "")}
              onChange={(e) => choose({ ...filters, projectTypes: e.target.value ? [e.target.value as ProjectType] : undefined })}
            >
              <option value="">Any project type</option>
              {/* Several kinds come only from filters saved on the website. */}
              {types.length > 1 && (
                <option value="several" disabled>
                  {types.map((t) => PROJECT_TYPES[t]).join(", ")}
                </option>
              )}
              {Object.entries(PROJECT_TYPES).map(([id, name]) => (
                <option key={id} value={id}>
                  {name}
                </option>
              ))}
            </select>,
          )}
          {pill(
            "ai",
            filters.ai && filterLabel("ai", filters.ai, consoleNames),
            <select aria-label="AI use" value={filters.ai ?? ""} onChange={set("ai")}>
              <option value="">Any AI use</option>
              <option value="no-generated">Hide mostly AI-generated apps</option>
              <option value="no-ai">Hide apps with any AI use</option>
            </select>,
          )}
          {filtering > 0 && (
            <span className="filter-actions">
              <button type="button" className="text-button" onClick={() => choose({ os: config.os, sort: filters.sort })}>
                Clear all
              </button>
            </span>
          )}
        </div>
      )}
      {error ? (
        <div className="empty">
          <p>{error}</p>
          <button className="primary" onClick={() => load()}>
            Try again
          </button>
        </div>
      ) : (
        <>
          {search.trim() && <p className="results-bar">Results for “{search.trim()}”</p>}
          {games.length > 0 && (
            <>
              <GamesSection games={games} onOpen={onOpenGame} />
              <h2 className="catalog-section-label">Apps</h2>
            </>
          )}
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
  const { config, installs, jobs, catalog, get, play, dismiss, setUpdates } = useLauncher();
  const job = jobs[entry.id];
  const install = installs[entry.id];
  if (job && "error" in job)
    return (
      <div className="job-error" role="alert">
        <p>{job.error}</p>
        <div className="row">
          {/* A local app has nothing to download: Try again plays it again. */}
          <button onClick={() => (install?.local ? (dismiss(entry.id), void play(entry.id)) : get(entry))}>Try again</button>
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
          {versionLabel(pulled.version)} was withdrawn: {pulled.reason}
        </p>
        <div className="row">
          {/* A withdrawn release outranks a pin: the way off it is right here. */}
          <button onClick={() => (install.updates === "pinned" && setUpdates(entry.id, undefined), get(catalog[entry.id]))}>
            {install.updates === "pinned" ? "Unpin and install" : "Install"} {versionLabel(catalog[entry.id].verified?.version)}
          </button>
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
        <button onClick={() => get(catalog[entry.id] ?? entry)}>Update to {versionLabel((catalog[entry.id] ?? entry).verified?.version)}</button>
      </div>
    );
  if (install && (install.updates === "pinned" || install.unverified))
    return (
      <div className="row pinned-action">
        <button className="primary wide" onClick={() => play(entry.id)}>
          <Play size={15} /> Play
        </button>
        {install.unverified &&
          (install.blocked ? (
            <span className="blocked-mark" role="img" aria-label={`Quiver blocked ${versionLabel(install.version)}`} title={`Quiver blocked ${versionLabel(install.version)}: ${install.blocked}`}>
              <ShieldX size={14} />
            </span>
          ) : (
            <span className="unverified-mark" role="img" aria-label={`${versionLabel(install.version)} is unverified`} title={`${versionLabel(install.version)} is unverified`}>
              <ShieldAlert size={14} />
            </span>
          ))}
        {install.updates === "pinned" && (
          <span className="pin-mark" role="img" aria-label={`Pinned to ${versionLabel(install.version)}`} title={`Pinned to ${versionLabel(install.version)}`}>
            <Pin size={14} />
          </span>
        )}
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
function AppPage({
  entry: opened,
  back,
  onClose,
  onOpenGame,
  onSignIn,
}: {
  entry: Entry;
  back: string;
  onClose: () => void;
  onOpenGame: (game: GameLink) => void;
  onSignIn: () => void;
}) {
  const { library, installs, catalog, remove, consoleNames, setUpdates, get } = useLauncher();
  const item = library.find((i) => i.id === opened.id);
  // The player's own name and artwork, live as they change them.
  const entry = withOverrides(catalog[opened.id] ?? opened, item?.overrides);
  const install = installs[entry.id];
  // Apps the player added have no page on the site: no releases or feedback there.
  const site = !isOwn(entry.id);
  const { source, detail } = useAppDetail(entry);
  const releases = useReleases(entry, site);
  const history = useReleaseHistory(entry, site);
  const { own, refresh } = useOwnFeedback(entry, site);
  const { user } = useAccount();
  const [tab, setTab] = useState<"overview" | "releases" | "feedback">("overview");
  // Sharing feedback asked for here; signing in comes first when needed.
  const [intent, setIntent] = useState<Intent | null>(null);
  const share = (next: Intent) => {
    setTab("feedback");
    setIntent(next);
    if (!user) onSignIn();
  };
  const said = entry.recommended + entry.reportIssues + entry.reportBroken;
  const checking = checkingOf(detail, releases);
  const tabs = [
    ["overview", "Overview", undefined],
    ["releases", "Releases", history.items && `${history.items.length}${history.more ? "+" : ""}`],
    ["feedback", "Player feedback", String(said)],
  ] as const;
  const hero = entry.libraryArt?.hero || entry.libraryArt?.header;
  const [problem, setProblem] = useState<string | null>(null);
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
                  {/* As on the website, each opens the original game's page. */}
                  {entry.games.map((g) => (
                    <button key={g.id} type="button" className="game-chip" data-game={g.slug} onClick={() => onOpenGame(g)}>
                      {g.title}
                    </button>
                  ))}
                  {entry.basedOn && <span className="based-on-version">{gameVersionLabel(entry.basedOn, consoleNames)}</span>}
                </div>
              )}
              <div className="facts">
                <PlatformIcons os={entry.supportedOS} />
                {site && (
                  // As on the website: what players said, and the way to it.
                  <button type="button" className="score-link" onClick={() => (said ? setTab("feedback") : share("edit"))}>
                    <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />
                    <span className="score-link-text">{said ? `Feedback from ${said} ${said === 1 ? "player" : "players"}` : "Be the first to say how it runs"}</span>
                  </button>
                )}
                {entry.verified && (
                  <span className="verified">
                    <ShieldCheck size={14} />
                    {isOwn(entry.id)
                      ? `${versionLabel(entry.verified.version)} · not checked by Quiver`
                      : entry.verified.pinned
                        ? `${versionLabel(entry.verified.version)} · verified`
                        : `${versionLabel(entry.verified.version)} · files can't be verified`}
                  </span>
                )}
                {install && !install.local && <span className="muted">Installed {versionLabel(install.version)}</span>}
                {install?.unverified &&
                  (install.blocked ? (
                    <ReleaseBadge state="blocked" title={`Quiver blocked it: ${install.blocked}`} />
                  ) : (
                    <ReleaseBadge state="unverified" title="Installed before Quiver verified it" />
                  ))}
              </div>
              <div className="app-actions">
                <Action entry={entry} />
                {install && (
                  <button onClick={() => (setProblem(null), native.openFolder(entry.id).catch((e) => setProblem(String(e))))}>
                    <FolderOpen size={15} /> Open folder
                  </button>
                )}
                {source && <RepositoryLink source={source} />}
                {item && (
                  <button
                    className="danger"
                    onClick={() => {
                      remove(entry.id);
                      onClose();
                    }}
                  >
                    <Trash2 size={15} /> {install && !install.local ? "Uninstall and remove" : "Remove from library"}
                  </button>
                )}
              </div>
              {problem && (
                <p className="job-error" role="status">
                  {problem}
                </p>
              )}
              {install?.local && <p className="muted">Removing it from your library leaves its files where they are.</p>}
              {install?.blocked && !install.local && (
                <p className="release-pinned-line blocked" role="alert">
                  <ShieldX size={14} aria-hidden="true" />
                  <span>
                    Quiver blocked {versionLabel(install.version)}: {install.blocked}
                  </span>
                  {entry.verified && bare(entry.verified.version) !== bare(install.version) && (
                    <button type="button" className="link-button" onClick={() => get(entry)}>
                      Install the verified {versionLabel(entry.verified.version)}
                    </button>
                  )}
                </p>
              )}
              {install?.differs && (
                <p className="release-pinned-line differs" role="alert">
                  <ShieldAlert size={14} aria-hidden="true" />
                  <span>The verified {versionLabel(install.version)} isn&apos;t the file you installed.</span>
                  <button type="button" className="link-button" onClick={() => get(entry)}>
                    Install the verified {versionLabel(install.version)}
                  </button>
                </p>
              )}
              {install && !install.local && install.updates === "pinned" && (
                <p className="release-pinned-line">
                  <Pin size={14} aria-hidden="true" />
                  <span>
                    Pinned to {versionLabel(install.version)}.
                    {/* Quietly: no badge or count, but a pin never hides that there's more. */}
                    {entry.verified && bare(entry.verified.version) !== bare(install.version) && entry.verified.releasedAt > (install.releasedAt ?? 0)
                      ? ` ${versionLabel(entry.verified.version)} is available.`
                      : ""}
                  </span>
                  <button type="button" className="link-button" onClick={() => setUpdates(entry.id, undefined)}>
                    Unpin
                  </button>
                </p>
              )}
              {install && !install.local && install.updates === "auto" && skipped(entry, install) && (
                <p className="release-pinned-line">
                  <span>
                    Auto Update won&apos;t put {versionLabel(entry.verified?.version)} back. It installs the next new release.
                  </span>
                  <button type="button" className="link-button" onClick={() => setUpdates(entry.id, "pinned")}>
                    Always stay on {versionLabel(install.version)}
                  </button>
                </p>
              )}
              {/* As on the website: a newer release the site is still checking. */}
              {checking && (
                <p className="release-checking-line">
                  <ShieldAlert size={14} aria-hidden="true" />
                  <span>
                    Version {checking.version} is <CheckingStatus checking={checking} />
                  </span>
                </p>
              )}
            </div>
          </div>
        </div>
      </div>
      {site && (
        <div className="detail-tabs" role="tablist" aria-label="App sections">
          {tabs.map(([key, label, count]) => (
            <button key={key} type="button" role="tab" aria-selected={tab === key} onClick={() => setTab(key)}>
              {label}
              {count !== undefined && <span className="tab-count">{count}</span>}
            </button>
          ))}
        </div>
      )}
      <div className="app-columns">
        <div className="app-content" role={site ? "tabpanel" : undefined}>
          {tab === "overview" && <Readme entry={entry} source={source} />}
          {tab === "releases" && <ReleasesTab detail={detail} history={history} />}
          {tab === "feedback" && (
            <FeedbackTab
              entry={entry}
              releases={releases.items}
              own={own}
              onSaved={refresh}
              intent={intent}
              onIntentDone={() => setIntent(null)}
              onShare={share}
            />
          )}
        </div>
        <aside className="app-side">
          {site && tab !== "feedback" && <ReportPrompt entry={entry} count={said} own={own} onShare={share} />}
          {site && <ProjectDetails entry={entry} detail={detail} />}
          {(item || install) && (
            <section className="app-panel" aria-label="On this computer">
              <h3>On this computer</h3>
              <Shortcuts entry={entry} />
              {item && <AppOptions id={item.id} />}
              {item && !isLocal(item.id) && <Versions entry={entry} source={source} />}
              {item && <Tags id={item.id} />}
              {item && <Customize id={item.id} />}
            </section>
          )}
        </aside>
      </div>
    </section>
  );
}

const PROJECT_TYPES: Record<string, string> = { port: "Port", tool: "Tool", emulator: "Emulator", game: "Standalone game" };
const SORTS = ["added", "updated", "rating", "name"] as const;
/** Browse's catalog filters; several kinds of project come from saved filters. */
type BrowseFilters = Omit<AppQuery, "search" | "cursor" | "limit" | "projectType">;

/** This computer's choices for an app: how it updates, and whether the library shows it. */
function AppOptions({ id }: { id: string }) {
  const { installs, setUpdates, settings, setSettings } = useLauncher();
  const install = installs[id];
  const hidden = settings.hidden?.includes(id);
  return (
    <div className="row options">
      {/* A local app has no releases to update from. */}
      {install && !install.local && (
        <label>
          Updates{" "}
          <select value={install.updates ?? "ask"} onChange={(e) => setUpdates(id, e.target.value === "ask" ? undefined : (e.target.value as "auto" | "pinned"))}>
            <option value="ask">Offer them</option>
            <option value="auto">Install automatically</option>
            <option value="pinned">Always stay on {versionLabel(install.version)}</option>
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
              .then((message) => (message ? setError(message) : (track("signed_in", { method: "password", created: create }), onClose())))
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
                  .then((message) => (message ? setError(message) : (track("signed_in", { method: provider }), onClose())))
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
          <label>
            Theme{" "}
            <select
              value={settings.theme ?? "system"}
              onChange={(e) => setSettings({ ...settings, theme: e.target.value === "system" ? undefined : (e.target.value as Theme) })}
            >
              <option value="system">Same as the system</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
            </select>
          </label>
          <section className="settings-section" aria-label="Library">
            <h3>Library</h3>
            <ViewOptions />
          </section>
          <p className="muted">Apps are installed in {config.appsDir}</p>
          <TelemetrySetting />
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
            {choice.entry.projectName} {versionLabel(choice.version)} has more than one file for your computer.
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
