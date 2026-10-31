import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ArrowUp, BookOpen, ChevronDown, CircleArrowDown, CirclePlay, CircleUser, Compass, Download, Eye, EyeOff, FolderOpen, FolderPen, History, Info, LayoutGrid, Library, LogOut, Monitor, PanelLeft, Mail, PackagePlus, Pencil, Pin, Play, Plus, Search, SettingsIcon, RotateCcw, ShieldAlert, ShieldCheck, ShieldX, SlidersHorizontal, Smile, Trash2, Tv, X } from "lucide-react";
import type { AppQuery, Entry, GameMatch, Os, Page, ProjectType } from "@quiverlauncher/api";
import { msg, plural, t } from "@lingui/core/macro";
import { Plural, Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, ReleaseBadge, SourcedImage, boxOf, cardSources, heroSources } from "@quiverlauncher/ui";
import { availableOn, canGet, catalogOs, hasUpdate, pulledVerified, renamable, skipped, tvModeOn, useLauncher } from "./store";
import { TvHome } from "./tv";
import { TabPrompt } from "./pad-hints";
import { Dialog, FilterPill, LibraryPage, Popover, filterLabel, withOverrides } from "./library";
import { CATALOG_BOX, useLibraryView, useSidebarFits } from "./library-view";
import { DownloadsButton, DownloadsPage, DownloadsStrip, SidebarDownloads } from "./downloads-page";
import { VersionsDialog, PanelFormHead, ProjectDetails, Readme, ReleasesTab, RepositoryLink, Shortcuts, Consoles, Tags, Versions, checkingOf, foldersOn, hoursFrom, scanText, useAppDetail, useListed, useReleaseHistory, useReleases } from "./detail";
import { UpdateBanner, useUpdateCheck } from "./update";
import { useAnnouncementsCheck } from "./announcements";
import { AnnouncementBanner, AnnouncementsButton, AnnouncementsDialog, useTidyDismissals } from "./announcements-ui";
import { LaunchOptionsButton } from "./launch-options";
import { versionLabel, wayOffLabel } from "./versions";
import { FeedbackBox, FeedbackTab, useOwnFeedback, type Intent } from "./feedback";
import { GamePage, GamesSection, gameVersionLabel, type GameLink } from "./game";
import { Discover, MoreLikeThis } from "./discover";
import { ErrorBoundary } from "./boundary";
import { pageArea, toTop } from "./spatial";
import { track } from "./telemetry";
import { TelemetryNotice, useTelemetrySetup } from "./telemetry-ui";
import { useAccount, type CatalogDefaults } from "./account";
import { native } from "./native";
import { isLocal, isOwn } from "./custom";
import { AddApp } from "./own";
import { ModsButton } from "./mods";
import { MediaTab } from "./media";
import { PlayStats } from "./library-sidebar";
import { WindowControls, moveWindow, useWindowButtons } from "./window-bar";
import { SettingsPage } from "./settings-page";
import { OldLibrary } from "./old-library";
import { Setup, showsSetup } from "./setup";
import { FilePicker } from "./file-picker";
import { fileName } from "./card-menu";
import { closesOutside } from "./overlay";

type Tab = "library" | "browse";
/** A page over the library or catalog: an app's, an original game's, Downloads or Settings. */
/** Where an app's page opens: its Releases, with a version's notes open (Downloads' See changelog). */
export type PageAt = { releases: string };
type View = { kind: "app"; entry: Entry; at?: PageAt } | { kind: "game"; game: GameLink } | { kind: "downloads" } | { kind: "settings" };
const viewKey = (v: View) => (v.kind === "app" ? `app:${v.entry.id}` : v.kind === "game" ? `game:${v.game.slug}` : v.kind);
const shorten = (text: string) => (text.length > 40 ? `${text.slice(0, 39).trimEnd()}…` : text);
/** "v1.2" for "1.2" or "v1.2": releases are tagged either way. */
/** "v1.2" and "1.2" are the same release. */
/** The website, where accounts are created and passwords reset. */
const SITE = "https://quiverlauncher.com";
const bare = (version: string) => version.trim().replace(/^v/i, "");

export function App() {
  useTelemetrySetup();
  useUpdateCheck();
  const { library, client, settings, setSettings, config } = useLauncher();
  // TV mode (tv.tsx) in place of the library and Browse: turned on, or by itself in Steam's Game Mode.
  const tv = tvModeOn(settings, config);
  const setTv = (on: boolean) => setSettings({ ...settings, tvMode: on ? "on" : "off" });
  useAnnouncementsCheck(client);
  useTidyDismissals();
  // A new player starts in the catalog; everyone else in their library.
  const [tab, setTab] = useState<Tab>(library.length ? "library" : "browse");
  // Pages opened from the list, each over the one before: Back goes down one.
  const [stack, setStack] = useState<View[]>([]);
  const [search, setSearch] = useState("");
  const [signingIn, setSigningIn] = useState(false);
  // Read before this start counts as one (useTelemetrySetup).
  const [settingUp, setSettingUp] = useState(() => showsSetup(settings));
  const [adding, setAdding] = useState(false);
  const [announcementsOpen, setAnnouncementsOpen] = useState(false);
  // Where each covered page was scrolled to and what had focus, to put back on the way back.
  const covered = useRef<{ y: number; focus: Element | null }[]>([]);
  const open = useCallback((view: View) => {
    const here = { y: pageArea().scrollTop, focus: document.activeElement };
    setStack((s) => {
      covered.current[s.length] = here;
      // A page already open further down is gone back to, so the stack stays short.
      const i = s.findIndex((v) => viewKey(v) === viewKey(view));
      return i >= 0 ? s.slice(0, i + 1) : [...s, view];
    });
  }, []);
  const openApp = useCallback((entry: Entry, at?: PageAt) => open({ kind: "app", entry, at }), [open]);
  const openGame = useCallback((game: GameLink) => open({ kind: "game", game }), [open]);
  const openDownloads = useCallback(() => open({ kind: "downloads" }), [open]);
  const openSettings = useCallback(() => open({ kind: "settings" }), [open]);
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
    pageArea().scrollTo(0, at?.y ?? 0);
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
  const screen = !top ? tab : top.kind === "app" ? `app:${isOwn(top.entry.id) ? "" : top.entry.slug}` : top.kind === "game" ? `game:${top.game.slug}` : top.kind;
  useEffect(() => {
    const [name, slug] = screen.split(":");
    track("screen_viewed", { screen: name, ...(slug !== undefined ? { slug: slug || null } : {}) });
  }, [screen]);
  useEffect(() => void (adding && track("screen_viewed", { screen: "add_app" })), [adding]);
  useEffect(() => void (signingIn && track("screen_viewed", { screen: "sign_in" })), [signingIn]);
  const query = search.trim();
  const searched = shorten(query);
  const listName = tab === "library" ? t`Library` : query ? t`Back to results for “${searched}”` : t({ message: "Browse", comment: "The tab with the catalog of apps" });
  /** What Back on the page at `i` returns to. */
  const backFrom = (i: number) => {
    const below = stack[i - 1];
    return !below ? listName : below.kind === "app" ? below.entry.projectName : below.kind === "game" ? (below.game.title ?? t`Back`) : below.kind === "downloads" ? t`Downloads` : t`Settings`;
  };
  // The Sidebar layout's foot says how downloads are going, in place of the strip.
  const { view: libraryView, set: setLibraryView } = useLibraryView();
  const sidebar = libraryView.layout === "sidebar";
  const sidebarFits = useSidebarFits();
  const strip = !(tab === "library" && libraryView.layout === "sidebar" && sidebarFits);
  const action = (entry: Entry) => <Action entry={entry} />;
  // Without the system's title bar, the top bar's empty space moves the window.
  const { buttons, maximized } = useWindowButtons();
  return (
    <div className="shell" data-tv={tv || undefined}>
      <header className={buttons === "none" ? "topbar" : "topbar with-window-controls"} onMouseDown={buttons === "window" ? moveWindow : undefined}>
        {/* As on the website; it goes to the library. */}
        <button className="brand" aria-label={t`Quiver Launcher: go to your library`} onClick={() => switchTab("library")}>
          <img src="/quiver-icon-96.png" alt="" width="34" height="34" />
          <span>
            Quiver<span className="brand-label">LAUNCHER</span>
          </span>
        </button>
        {/* data-tabs: a controller's shoulder buttons go between them, as between TV mode's tabs (spatial.ts). */}
        <nav data-tabs>
          <button className={tab === "library" ? "active" : ""} aria-current={tab === "library" && !top ? "page" : undefined} onClick={() => switchTab("library")}>
            <Library size={16} aria-hidden="true" /> <Trans>Library</Trans> <span className="count">{library.length}</span>
          </button>
          <button className={tab === "browse" ? "active" : ""} aria-current={tab === "browse" && !top ? "page" : undefined} onClick={() => switchTab("browse")}>
            <Compass size={16} aria-hidden="true" /> <Trans comment="The tab with the catalog of apps">Browse</Trans>
          </button>
        </nav>
        <AccountButton onSignIn={() => setSigningIn(true)} />
        {/* The library as a grid or with its sidebar, from wherever the library is, as TV mode is beside it; none where the sidebar doesn't fit (a phone). */}
        {tab === "library" && sidebarFits && (
          <button
            className="icon layout-button"
            aria-label={sidebar ? t`Show the library as a grid` : t`Show the library with a sidebar`}
            title={sidebar ? t`Show the library as a grid` : t`Show the library with a sidebar`}
            onClick={() => setLibraryView({ layout: sidebar ? "grid" : "sidebar" })}
          >
            {sidebar ? <LayoutGrid size={19} /> : <PanelLeft size={19} />}
          </button>
        )}
        <button className="icon tv-button" aria-label={t`TV mode`} title={t`TV mode: for a controller, the couch and a Steam Deck`} onClick={() => setTv(true)}>
          <Tv size={19} />
        </button>
        <AnnouncementsButton onOpen={() => setAnnouncementsOpen(true)} />
        <DownloadsButton open={top?.kind === "downloads"} onOpen={openDownloads} />
        <button className={`icon settings-button${top?.kind === "settings" ? " active" : ""}`} aria-label={t`Settings`} aria-current={top?.kind === "settings" ? "page" : undefined} onClick={openSettings}>
          <SettingsIcon size={19} />
        </button>
        <WindowControls buttons={buttons} maximized={maximized} />
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
          fallback={(error, reset) => {
            const why = error.message;
            return (
              <div className="banner" role="alert">
                <p>
                  <Trans>That page hit a problem as it closed: {why}</Trans>
                </p>
                <button onClick={reset}>
                  <Trans>OK</Trans>
                </button>
              </div>
            );
          }}
        >
          {stack.map((view, i) => (
            <div key={viewKey(view)} className="page-layer" hidden={i !== stack.length - 1}>
              <ErrorBoundary fallback={(error) => <PageProblem error={error} back={backFrom(i)} onBack={goBack} />}>
                {view.kind === "app" ? (
                  <AppPage entry={view.entry} at={view.at} tv={tv} back={backFrom(i)} onClose={goBack} onOpenGame={openGame} onOpenApp={openApp} onSignIn={() => setSigningIn(true)} />
                ) : view.kind === "downloads" ? (
                  <DownloadsPage back={backFrom(i)} onBack={goBack} onOpen={openApp} action={action} />
                ) : view.kind === "settings" ? (
                  <SettingsPage back={backFrom(i)} onBack={goBack} onRunSetup={() => setSettingUp(true)} />
                ) : (
                  <GamePage game={view.game} back={backFrom(i)} onBack={goBack} onOpenApp={openApp} action={action} />
                )}
              </ErrorBoundary>
            </div>
          ))}
        </ErrorBoundary>
        {/* TV mode in place of the library and Browse; the pages still open over it. */}
        {tv && (
          <div className="tv-layer" hidden={Boolean(top)}>
            <TvHome onOpen={openApp} onLeave={() => setTv(false)} onDownloads={openDownloads} onSettings={openSettings} action={action} />
          </div>
        )}
        {/*
          Kept mounted under the pages too, so going back keeps the search, filters and scroll; not under TV mode, which
          has its own, where it only did all its work again with each change (a download's progress, say).
        */}
        {!tv && (
          <div hidden={Boolean(top)}>
            {/* A download under way always shows how it's going, over whichever banner has its turn. */}
            {strip && <DownloadsStrip onOpen={openDownloads} active offerUpdates={tab === "library"} />}
            {/*
              One banner at a time, the most pressing first (app.css shows only the first that has something to say):
              what the player just did, the usage-data notice, the launcher's update, updates for apps, bringing
              over Quiver Launcher 3, then the newest announcement. Dealing with one shows the next.
            */}
            <div className="banner-stack">
              <Notice />
              <TelemetryNotice />
              <UpdateBanner />
              {/* The updates waiting: the library's to offer, not Browse's. */}
              {strip && tab === "library" && <DownloadsStrip onOpen={openDownloads} active={false} />}
              {/* Finishing an import shows the library, under whatever page is open. */}
              <OldLibrary
                onDone={() => {
                  setTab("library");
                  // Back from the page shows the library from its top, not where Browse was scrolled.
                  covered.current[0] = { y: 0, focus: null };
                }}
              />
              {/* Across the window under the top bar in the Sidebar layout, edge to edge. */}
              <AnnouncementBanner edge={!strip} onOpen={() => setAnnouncementsOpen(true)} />
            </div>
            {tab === "library" ? (
              <LibraryPage
                onOpen={openApp}
                onBrowse={() => switchTab("browse")}
                onAdd={() => setAdding(true)}
                onSignIn={() => setSigningIn(true)}
                downloads={<SidebarDownloads onOpen={openDownloads} />}
                action={action}
                page={(entry) => (
                  <ErrorBoundary fallback={(error) => <PageProblem error={error} />}>
                    <AppPage entry={entry} onOpenGame={openGame} onOpenApp={openApp} onSignIn={() => setSigningIn(true)} />
                  </ErrorBoundary>
                )}
              />
            ) : (
              <BrowsePage onOpen={openApp} onOpenGame={openGame} search={search} onSearch={setSearch} />
            )}
          </div>
        )}
      </main>
      <BackToTop />
      {/* Under signing in, which its last step opens. */}
      {settingUp && (
        <Setup
          onClose={(brought) => {
            setSettingUp(false);
            if (brought) switchTab("library");
          }}
          onSignIn={() => setSigningIn(true)}
        />
      )}
      {signingIn && <SignIn onClose={() => setSigningIn(false)} />}
      {announcementsOpen && <AnnouncementsDialog onClose={() => setAnnouncementsOpen(false)} />}
      {adding && <AddApp onClose={() => setAdding(false)} onOpen={openApp} />}
      <ChooseFile />
      <UnverifiedPrompt />
      <UninstallPrompt />
      <RenameFolderPrompt />
      <ProgramPrompt />
      {/* Over every dialog that asks for a file or a folder, in Steam's Game Mode. */}
      <FilePicker />
    </div>
  );
}

/**
 * Once a page has scrolled a screen or more, a button to go back to its top.
 * Home, and a controller's R3, do the same (rebindable in Settings).
 */
function BackToTop() {
  const [shown, setShown] = useState(false);
  useEffect(() => {
    const check = () => {
      const page = pageArea();
      setShown(page.scrollTop > page.clientHeight);
    };
    check();
    // The page scrolls in its own area (app.css), whose scroll events don't bubble: caught on the way down.
    window.addEventListener("scroll", check, { capture: true, passive: true });
    window.addEventListener("resize", check);
    return () => {
      window.removeEventListener("scroll", check, { capture: true });
      window.removeEventListener("resize", check);
    };
  }, []);
  if (!shown) return null;
  return (
    // A click doesn't take the focus, so it goes up from wherever it was.
    <button className="back-to-top" aria-label={t`Back to top`} title={t`Back to top`} onMouseDown={(e) => e.preventDefault()} onClick={() => toTop(document.activeElement)}>
      <ArrowUp size={20} />
    </button>
  );
}

/** In place of a page that failed: what went wrong, and the way back (none beside the library's sidebar). */
function PageProblem({ error, back, onBack }: { error: Error; back?: string; onBack?: () => void }) {
  return (
    <section className="app-page" role="alert">
      {onBack && (
        <div className="backdrop-inner">
          <button className="back-link" onClick={onBack}>
            <ArrowLeft size={15} /> {back}
          </button>
        </div>
      )}
      <div className="empty">
        <h2>
          <Trans>This page hit a problem</Trans>
        </h2>
        <p>{error.message}</p>
        {onBack && (
          <button className="primary" onClick={onBack}>
            <Trans>Go back</Trans>
          </button>
        )}
      </div>
    </section>
  );
}

function BrowsePage({
  onOpen,
  onOpenGame,
  search,
  onSearch,
}: {
  onOpen: (e: Entry) => void;
  onOpenGame: (game: GameLink) => void;
  search: string;
  onSearch: (search: string) => void;
}) {
  const { client, config, library, remember, consoles, consoleNames, settings, setSettings, jobs } = useLauncher();
  const saved = useAccount().user?.catalogDefaults;
  // As in Quiver Launcher 3, apps already in the library stay out of Browse unless the player asks for them.
  const showOwned = Boolean(settings.browseShowsLibrary);
  const setShowOwned = (on: boolean) => setSettings({ ...settings, browseShowsLibrary: on || undefined });
  const libraryNow = useRef(library);
  libraryNow.current = library;
  // The apps left out: those in the library when their page came. One added since stays, with its badge.
  const [owned, setOwned] = useState<Set<string>>(new Set());
  // The site's catalog filters: the ones saved on the account ("Save as my
  // default" on the website), but the platform starts at what this computer
  // runs ("here"), since only those apps can be installed here.
  const fromSaved = (d?: CatalogDefaults): BrowseFilters => ({
    os: "here",
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
      .apps({ ...filters, os: filters.os === "here" ? catalogOs(config.os) : filters.os, search: query, sort: searching ? undefined : filters.sort, cursor })
      .then((page) => {
        if (request !== latest.current) return;
        remember(page.items);
        const mine = new Set(libraryNow.current.map((i) => i.id));
        const left = page.items.filter((e) => mine.has(e.id)).map((e) => e.id);
        setOwned((o) => new Set([...(cursor ? o : []), ...left]));
        setPages((p) => (cursor ? [...p, page] : [page]));
      })
      .catch(() => request === latest.current && setError(t`Couldn't reach quiverlauncher.com. Check your connection and try again.`))
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
  // How many filters differ from the defaults: any platform but what this computer runs counts.
  const filtering = [filters.projectTypes?.length, filters.console, filters.ai, filters.os !== "here"].filter(Boolean).length;
  // An order the player picked (the sort, or a shelf's See all): the whole list in it, in place of the front page, as a
  // search or a filter has it; its pill goes back to the front page.
  const [sorted, setSorted] = useState(false);
  const sortName = (sort: BrowseFilters["sort"]) =>
    ({ added: t`Recently added`, updated: t`Recently updated`, rating: t`Top rated`, name: t`Name A–Z` })[sort ?? "added"];
  // Apps in your library shown, a choice kept from before: on the Filters button's count, not a row of its own.
  const filterCount = filtering + (showOwned ? 1 : 0);
  const types = filters.projectTypes ?? [];
  /** A filter's pill: `on` names it while it differs from the default, and removing it goes back to the default. */
  const pill = (key: "os" | "console" | "projectTypes" | "ai", on: string | undefined, select: ReactNode) =>
    !on && !filtersOpen ? null : (
      <FilterPill key={key} on={on} onClear={() => choose({ ...filters, [key]: key === "os" ? "here" : undefined })}>
        {select}
      </FilterPill>
    );

  // Linux's own alone, beside what it runs: "Linux only".
  const osChoice = (os: Os) =>
    os === "linux" && config.os === "linux" ? t({ message: "Linux only", comment: "Browse's platform filter on Linux: its own apps, not Windows' too" }) : (OS_NAMES[os] ?? os);
  const name = OS_NAMES[config.os] ?? config.os;
  const hereLabel =
    config.os === "linux"
      ? t({ message: "Linux and Windows (this computer)", comment: "Browse's platform filter on Linux: the apps it runs, its own and Windows' through Proton" })
      : t`${name} (this computer)`;
  const inLibrary = new Set(library.map((i) => i.id));
  // What this computer runs, where the catalog was asked for more (Linux's and Windows' apps: catalogOs).
  const all = pages.flatMap((p) => p.items).filter((e) => filters.os !== "here" || availableOn(e, config.os));
  const entries = showOwned ? all : all.filter((e) => !owned.has(e.id));
  const left = all.length - entries.length;
  const mine = showOwned ? all.filter((e) => inLibrary.has(e.id)).length : 0;
  const last = pages.at(-1);
  const typed = search.trim();
  return (
    <>
      {/* As the library's: the search, then sort, Filters and Submit an app, all one height. */}
      <div className="browse-bar">
        <label className="search browse-search">
          <Search size={15} />
          <input
            // Not on a phone, where the focus brings up the keyboard over the page each time Browse opens.
            autoFocus={config.os !== "android" && config.os !== "ios"}
            aria-label={t`Search the catalog`}
            placeholder={t`Search ports, games and tools`}
            value={search}
            onChange={(e) => onSearch(e.target.value)}
          />
          {search && (
            <button type="button" className="search-clear" aria-label={t`Clear search`} onClick={() => onSearch("")}>
              <X size={13} />
            </button>
          )}
        </label>
        <div className="browse-tools page-tools">
          <span className="quiet-select">
            <select
              aria-label={t({ message: "Sort", comment: "The order the catalog's apps are listed in" })}
              value={searching ? "" : (filters.sort ?? "added")}
              disabled={searching}
              onChange={(e) => (setSorted(true), set("sort")(e))}
            >
              {searching && <option value="">{t`Most relevant`}</option>}
              <option value="added">{t`Recently added`}</option>
              <option value="updated">{t`Recently updated`}</option>
              <option value="rating">{t`Top rated`}</option>
              <option value="name">{t`Name A–Z`}</option>
            </select>
            <ChevronDown size={14} aria-hidden="true" />
          </span>
          <button
            className={`tool${filtersOpen || filterCount ? " on" : ""}`}
            aria-expanded={filtersOpen}
            aria-controls="browse-filters"
            onClick={() => setFiltersOpen(!filtersOpen)}
          >
            <SlidersHorizontal size={15} /> <Trans>Filters</Trans>
            {filterCount > 0 && <span className="tool-count">{filterCount}</span>}
          </button>
          {/* New apps come to the catalog through the website's form; adding one to the library is the library's. */}
          <button
            className="tool submit-app"
            aria-label={t`Submit an app`}
            title={t`Suggest an app for the catalog, on quiverlauncher.com`}
            onClick={() => void native.openUrl(`${SITE}/submit`)}
          >
            <PackagePlus size={15} aria-hidden="true" /> <span><Trans>Submit an app</Trans></span>
          </button>
        </div>
      </div>
      {/*
        Filters: pills to pick from while open; the ones that differ from the defaults stay, each removable, while it's
        closed, with a picked order's first. Apps in your library shown has a pill only beside others, or with it open.
      */}
      {(filtersOpen || filtering > 0 || (sorted && !searching)) && (
        <div id="browse-filters" className="filter-bar" role="group" aria-label={t`Catalog filters`}>
          {sorted && !searching && (
            <FilterPill on={sortName(filters.sort)} onClear={() => (setSorted(false), choose({ ...filters, sort: fromSaved(saved).sort }))}>
              <span className="pill-label">{sortName(filters.sort)}</span>
            </FilterPill>
          )}
          {pill(
            "os",
            filters.os === "here" ? undefined : filters.os ? osChoice(filters.os) : t`All platforms`,
            <select aria-label={t`Platform`} value={filters.os ?? ""} onChange={set("os")}>
              {/* What it runs: on Linux, Linux's apps and Windows' (through Proton), as the library shows them. */}
              <option value="here">{hereLabel}</option>
              <option value="">{t`All platforms`}</option>
              {Object.keys(OS_NAMES)
                .filter((id) => id !== config.os || config.os === "linux")
                .map((id) => (
                  <option key={id} value={id}>
                    {osChoice(id as Os)}
                  </option>
                ))}
            </select>,
          )}
          {pill(
            "console",
            filters.console && filterLabel("console", filters.console, consoleNames),
            <select aria-label={t`Console`} value={filters.console ?? ""} onChange={set("console")}>
              <option value="">{t`Any console`}</option>
              {brands.map((brand) => (
                <optgroup key={brand} label={brand === "OtherPlatforms" ? t`Other platforms` : brand}>
                  <option value={`maker:${brand}`}>
                    {brand === "OtherPlatforms" ? t`All other platforms` : t({ message: `All ${brand}`, comment: "Every console a maker made, as in All Nintendo" })}
                  </option>
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
              aria-label={t`Project type`}
              value={types.length > 1 ? "several" : (types[0] ?? "")}
              onChange={(e) => choose({ ...filters, projectTypes: e.target.value ? [e.target.value as ProjectType] : undefined })}
            >
              <option value="">{t`Any project type`}</option>
              {/* Several kinds come only from filters saved on the website. */}
              {types.length > 1 && (
                <option value="several" disabled>
                  {types.map((type) => i18n._(PROJECT_TYPES[type])).join(", ")}
                </option>
              )}
              {Object.entries(PROJECT_TYPES).map(([id, name]) => (
                <option key={id} value={id}>
                  {i18n._(name)}
                </option>
              ))}
            </select>,
          )}
          {pill(
            "ai",
            filters.ai && filterLabel("ai", filters.ai, consoleNames),
            <select aria-label={t`AI use`} value={filters.ai ?? ""} onChange={set("ai")}>
              <option value="">{t`Any AI use`}</option>
              <option value="no-generated">{t`Hide mostly AI-generated apps`}</option>
              <option value="no-ai">{t`Hide apps with any AI use`}</option>
            </select>,
          )}
          {/* Up here with the filters, not under the list, which loads more as it's reached. */}
          {(filtersOpen || showOwned) && (
            <FilterPill on={showOwned ? t`Apps in your library shown` : undefined} onClear={() => setShowOwned(false)}>
              <select aria-label={t`Apps in your library`} value={showOwned ? "show" : "hide"} onChange={(e) => setShowOwned(e.target.value === "show")}>
                <option value="hide">{t`Hide apps in your library`}</option>
                <option value="show">{t`Show apps in your library`}</option>
              </select>
            </FilterPill>
          )}
          {filtering > 0 && (
            <span className="filter-actions">
              <button type="button" className="text-button" onClick={() => choose({ os: "here", sort: filters.sort })}>
                <Trans>Clear all</Trans>
              </button>
            </span>
          )}
        </div>
      )}
      {/* The front page, while the player isn't looking for something in particular. */}
      {!typed && !filtering && !sorted && !error && (
        <Discover
          onOpen={onOpen}
          showOwned={showOwned}
          action={(entry) => <Action entry={entry} />}
          onSeeAll={(to) => {
            choose({ ...filters, ...to });
            if ("sort" in to) setSorted(true);
            // To the whole list, at the top, the front page gone: in the shelf's order, or of its console.
            requestAnimationFrame(() => pageArea().scrollTo({ top: 0 }));
          }}
        />
      )}
      {error ? (
        <div className="empty">
          <p>{error}</p>
          <button className="primary" onClick={() => load()}>
            <Trans>Try again</Trans>
          </button>
        </div>
      ) : (
        <>
          {typed && (
            <p className="results-bar">
              <Trans>Results for “{typed}”</Trans>
            </p>
          )}
          {games.length > 0 && (
            <>
              <GamesSection games={games} onOpen={onOpenGame} />
              <h2 className="catalog-section-label">
                <Trans>Apps</Trans>
              </h2>
            </>
          )}
          <div className="catalog-grid boxes">
            {entries.map((entry) => (
              <EntryCard
                key={entry.id}
                entry={entry}
                coverSources={boxOf(entry) ? cardSources(boxOf(entry)!, CATALOG_BOX) : undefined}
                // A download's progress, or what went wrong, shows over the art without hover.
                className={jobs[entry.id] ? "busy" : undefined}
                consoleNames={consoleNames}
                onOpen={() => onOpen(entry)}
                badge={inLibrary.has(entry.id) ? t`In library` : undefined}
                action={<Action entry={entry} />}
              />
            ))}
          </div>
          {!loading && !all.length && (
            <p className="empty">
              <Trans>Nothing matches that search and those filters.</Trans>
            </p>
          )}
          {!loading && left > 0 && (
            <p className={entries.length ? "owned-note" : "empty"}>
              <Plural value={left} one="# app here is already in your library." other="# apps here are already in your library." />{" "}
              <button type="button" className="link-button" onClick={() => setShowOwned(true)}>
                <Plural value={left} one="Show it" other="Show them" />
              </button>
            </p>
          )}
          {!loading && mine > 0 && (
            <p className="owned-note">
              <Trans>Apps in your library are shown here too.</Trans>{" "}
              <button type="button" className="link-button" onClick={() => setShowOwned(false)}>
                <Trans>Hide them</Trans>
              </button>
            </p>
          )}
          {loading ? (
            <div className="more">
              <span className="spinner" />
            </div>
          ) : (
            last && !last.isDone && <ShowMore onMore={() => load(last.nextCursor)} />
          )}
        </>
      )}
    </>
  );
}

/**
 * The catalog's Show more, which presses itself as it nears the screen, so
 * scrolling to the end goes on through the catalog. Each page's comes anew
 * (the spinner's in its place meanwhile), so one still near asks again.
 */
function ShowMore({ onMore }: { onMore: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  const more = useRef(onMore);
  more.current = onMore;
  useEffect(() => {
    // Hidden under a page, it isn't near the screen.
    const near = new IntersectionObserver(([seen]) => seen.isIntersecting && more.current(), { rootMargin: "0px 0px 600px 0px" });
    near.observe(ref.current!);
    return () => near.disconnect();
  }, []);
  return (
    <div className="more" ref={ref}>
      <button onClick={onMore}>
        <Trans>Show more</Trans>
      </button>
    </div>
  );
}

/** The one button a card needs: Get, progress, Play or Update. `apart`: the page says why a release was withdrawn itself. */
function Action({ entry, apart }: { entry: Entry; apart?: boolean }) {
  const { config, installs, jobs, catalog, get, play, dismiss, setUpdates, cancel } = useLauncher();
  const job = jobs[entry.id];
  const install = installs[entry.id];
  // Its verified release was withdrawn too: Choose a version, in place of installing it.
  const [versions, setVersions] = useState(false);
  const noWayOff = Boolean(pulledVerified(catalog[entry.id] ?? entry));
  const choose = (
    <>
      <button className="primary" onClick={() => setVersions(true)}>
        <History size={15} aria-hidden="true" /> <Trans>Choose a version</Trans>
      </button>
      {versions && <VersionsDialog entry={catalog[entry.id] ?? entry} onClose={() => setVersions(false)} />}
    </>
  );
  if (job && "error" in job) {
    const instead = job.instead;
    const other = versionLabel(instead?.version);
    return (
      <div className="job-error" role="alert">
        <p>{job.error}</p>
        <div className="row">
          {/* Its file was gone or not the one Quiver checked: the newest other verified release, in its place. */}
          {instead && (
            <button className="primary" onClick={() => get(catalog[entry.id] ?? entry, instead)}>
              <Trans>Install {other} instead</Trans>
            </button>
          )}
          {/* A local app has nothing to download: Try again plays it again. */}
          <button onClick={() => (install?.local ? (dismiss(entry.id), void play(entry.id)) : get(entry))}>
            <Trans>Try again</Trans>
          </button>
          <button onClick={() => dismiss(entry.id)}>
            <Trans>Dismiss</Trans>
          </button>
        </div>
      </div>
    );
  }
  if (job) {
    const percent = job.total ? Math.round((job.received / job.total) * 100) : null;
    const name = entry.projectName;
    const bar = (
      <div className="progress" aria-label={job.phase === "installing" ? t`Installing` : t`Downloading`}>
        <div className="bar" style={{ width: `${job.phase === "installing" ? 100 : (percent ?? 5)}%` }} />
        <span>{job.setup ? (config.os === "android" ? t`Installing: confirm it when Android asks` : t`Installing: finish its setup window`) : job.phase === "installing" ? t`Installing…` : percent === null ? t`Downloading…` : t`Downloading ${percent}%`}</span>
      </div>
    );
    // Once its files are going in, an install finishes: stopping then could leave the app half there.
    if (job.phase === "installing") return bar;
    return (
      <div className="row progress-row">
        {bar}
        <button className="icon cancel-download" aria-label={t`Cancel downloading ${name}`} title={t`Cancel download`} onClick={() => cancel(entry.id)}>
          <X size={15} />
        </button>
      </div>
    );
  }
  const pulled = install && catalog[entry.id]?.withdrawn?.find((w) => w.version === install.version);
  if (pulled) {
    const row = (
      <div className="row">
        {/* A withdrawn release outranks a pin: the way off it is right here. */}
        {noWayOff ? (
          choose
        ) : (
          <button className="primary" onClick={() => (install.updates === "pinned" && setUpdates(entry.id, undefined), get(catalog[entry.id]))}>
            {wayOffLabel(install, catalog[entry.id].verified ?? { version: "" })}
          </button>
        )}
        <button onClick={() => play(entry.id)}>
          <Trans>Play anyway</Trans>
        </button>
      </div>
    );
    if (apart) return row;
    const version = versionLabel(pulled.version);
    const reason = pulled.reason;
    return (
      <div className="job-error" role="alert">
        <p>
          <Trans>{version} was withdrawn: {reason}</Trans>
        </p>
        {row}
      </div>
    );
  }
  if (install && hasUpdate(catalog[entry.id] ?? entry, install)) {
    const next = versionLabel((catalog[entry.id] ?? entry).verified?.version);
    return (
      <div className="row">
        <button className="primary" onClick={() => play(entry.id)}>
          <Play size={15} /> <Trans comment="Button that starts an app">Play</Trans>
        </button>
        <button onClick={() => get(catalog[entry.id] ?? entry)}>
          <Trans>Update to {next}</Trans>
        </button>
      </div>
    );
  }
  if (install && (install.updates === "pinned" || install.unverified)) {
    const version = versionLabel(install.version);
    const why = install.blocked ?? "";
    return (
      <div className="row pinned-action">
        <button className="primary wide" onClick={() => play(entry.id)}>
          <Play size={15} /> <Trans comment="Button that starts an app">Play</Trans>
        </button>
        {install.unverified &&
          (install.blocked ? (
            <span className="blocked-mark" role="img" aria-label={t`Quiver blocked ${version}`} title={t`Quiver blocked ${version}: ${why}`}>
              <ShieldX size={14} />
            </span>
          ) : (
            <span className="unverified-mark" role="img" aria-label={t`${version} is unverified`} title={t`${version} is unverified`}>
              <ShieldAlert size={14} />
            </span>
          ))}
        {install.updates === "pinned" && (
          <span className="pin-mark" role="img" aria-label={t`Pinned to ${version}`} title={t`Pinned to ${version}`}>
            <Pin size={14} />
          </span>
        )}
      </div>
    );
  }
  if (install)
    return (
      <button className="primary wide" onClick={() => play(entry.id)}>
        <Play size={15} /> <Trans comment="Button that starts an app">Play</Trans>
      </button>
    );
  if (!availableOn(entry, config.os)) {
    const os = OS_NAMES[config.os] ?? config.os;
    return (
      <p className="unavailable">
        <Trans>Not available for {os}</Trans>
      </p>
    );
  }
  // With no verified release, Get offers its newest after a warning; with none upstream either, there's nothing to get.
  if (!canGet(entry, config.os))
    return (
      <p className="unavailable">
        <Trans>No approved release yet</Trans>
      </p>
    );
  if (noWayOff) return <div className="row">{choose}</div>;
  return (
    <button className="primary wide" onClick={() => get(entry)}>
      <Download size={15} /> <Trans comment="Button that installs an app">Get</Trans>
    </button>
  );
}

/**
 * An app's own page, laid out like its page on quiverlauncher.com. Beside the
 * library's sidebar it has no way back, and shows the player's own play.
 */
/** Each tab of an app's page's icon: outlined and grey, filled in the accent while it's open (app.css). */
const TAB_ICONS = { overview: Info, media: CirclePlay, releases: CircleArrowDown, feedback: Smile, computer: Monitor };

function AppPage({
  entry: opened,
  back,
  at,
  tv = false,
  onClose,
  onOpenGame,
  onOpenApp,
  onSignIn,
}: {
  entry: Entry;
  /** What Back goes to; none beside the library's sidebar. */
  back?: string;
  /** Opened at its Releases, with that version's notes open, rather than its Overview. */
  at?: PageAt;
  /**
   * Over TV mode: a larger header with its art, read from across a room, and only the buttons for playing it there
   * (its folder and uninstalling are on its On this computer tab); Back (B) goes home; the README waits to be asked for.
   */
  tv?: boolean;
  onClose?: () => void;
  onOpenGame: (game: GameLink) => void;
  /** Opens another app's page, from More like this. */
  onOpenApp: (entry: Entry) => void;
  onSignIn: () => void;
}) {
  const { config, library, installs, jobs, catalog, add, remove, confirmUninstall, askRename, consoleNames, setUpdates, get, switched, switchToCatalog } = useLauncher();
  // An app the player added and switched to the catalog's shows the catalog's.
  const id = switched[opened.id] ?? opened.id;
  const item = library.find((i) => i.id === id);
  // The player's own name and artwork, live as they change them.
  const entry = withOverrides(catalog[id] ?? opened, item?.overrides);
  const listed = useListed(entry);
  const install = installs[entry.id];
  const job = jobs[entry.id];
  // Apps the player added have no page on the site: no releases or feedback there.
  const site = !isOwn(entry.id);
  const { source, detail } = useAppDetail(entry);
  const releases = useReleases(entry, site);
  const { own, refresh } = useOwnFeedback(entry, site);
  const { user } = useAccount();
  const [tab, setTab] = useState<"overview" | "media" | "releases" | "feedback" | "computer">(at ? "releases" : "overview");
  // Another tab: its content keeps the last one's height until it has come (nothing loading in it, and no longer
  // changing size), so More like this, under it, doesn't come up into sight for a moment and go down again.
  const content = useRef<HTMLDivElement>(null);
  const lastHeight = useRef(0);
  useEffect(() => {
    const el = content.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const sized = new ResizeObserver(() => void (el.style.minHeight || (lastHeight.current = el.offsetHeight)));
    sized.observe(el);
    return () => sized.disconnect();
  }, []);
  useLayoutEffect(() => {
    const el = content.current;
    if (!el || !lastHeight.current) return;
    el.style.minHeight = `${lastHeight.current}px`;
    // Its own height, under the one it's held at: from its top to its last part's foot.
    const natural = () => (el.lastElementChild ? el.lastElementChild.getBoundingClientRect().bottom - el.getBoundingClientRect().top : 0);
    const start = performance.now();
    let [was, still, frame] = [-1, 0, 0];
    const watch = () => {
      const now = natural();
      still = !el.querySelector(".spinner") && Math.abs(now - was) < 1 ? still + 1 : 0;
      was = now;
      if (still >= 8 || performance.now() - start > 4000) return void (el.style.minHeight = "");
      frame = requestAnimationFrame(watch);
    };
    frame = requestAnimationFrame(watch);
    return () => {
      cancelAnimationFrame(frame);
      el.style.minHeight = "";
    };
  }, [tab]);
  const [readme, setReadme] = useState(false);
  // Every release, asked for once the player opens the Releases tab or points
  // at it: most visits never do, and an app's history is one of the site's
  // biggest reads.
  const [historyFor, setHistoryFor] = useState<string | null>(null);
  if (tab === "releases" && historyFor !== entry.slug) setHistoryFor(entry.slug);
  const wantHistory = () => setHistoryFor(entry.slug);
  const history = useReleaseHistory(entry, site && historyFor === entry.slug);
  // Sharing feedback asked for here; signing in comes first when needed.
  const [intent, setIntent] = useState<Intent | null>(null);
  const share = (next: Intent) => {
    setTab("feedback");
    setIntent(next);
    if (!user) onSignIn();
  };
  const said = entry.recommended + entry.reportIssues + entry.reportBroken;
  const checking = checkingOf(detail, releases);
  // Only when the app has videos, as on the website.
  const media = detail?.media ?? [];
  const tabs = [
    ["overview", t({ message: "Overview", comment: "Tab of an app's page" }), undefined],
    ...(media.length ? ([["media", t({ message: "Media", comment: "Tab of an app's page, with its videos" }), String(media.length)]] as const) : []),
    ["releases", t({ message: "Releases", comment: "Tab of an app's page" }), history.items && `${history.items.length}${history.more ? "+" : ""}`],
    ["feedback", t`Player feedback`, String(said)],
    ...(item || install ? ([["computer", t`On this computer`, undefined]] as const) : []),
  ] as const;
  const hero = entry.libraryArt?.hero || entry.libraryArt?.header;
  const [problem, setProblem] = useState<string | null>(null);
  // The installed release was withdrawn: why, over the way off it.
  const pulled = install && catalog[entry.id]?.withdrawn?.find((w) => w.version === install.version);
  const eyebrow = [...new Set((entry.consoles ?? []).map((c) => consoleNames[c] ?? c.toUpperCase()))].join(", ");
  // Put in the sentences below by name.
  const verifiedVersion = versionLabel(entry.verified?.version);
  const installedVersion = versionLabel(install?.version);
  const pulledVersion = versionLabel(pulled ? pulled.version : undefined);
  const blockedWhy = install?.blocked ?? "";
  // What this computer has of the app, and its setup: a tab of its own for a catalog app, the page itself for one of the player's own.
  const computer = (item || install) && (
    <section className="app-panel computer-panel" aria-label={t`On this computer`}>
      {/* A tab says it already. */}
      {!site && (
        <h3>
          <Trans>On this computer</Trans>
        </h3>
      )}
      {/* A group with nothing in it for this app (no shortcuts on macOS, say) doesn't show. */}
      <div className="panel-group">
        <h4>
          <Trans>Shortcuts</Trans>
        </h4>
        <Shortcuts entry={entry} />
      </div>
      <div className="panel-group">
        <h4>
          <Trans comment="Heading over an app's mods, launch options, versions and updates">Setup</Trans>
        </h4>
        <ModsButton entry={entry} />
        {install && foldersOn(config.os) && <LaunchOptionsButton id={entry.id} />}
        {install?.installer === "msi" && !job && <ReinstallButton id={entry.id} />}
        {renamable(install) && foldersOn(config.os) && !(job && !("error" in job)) && (
          <button className="panel-action opens" onClick={() => askRename(entry)}>
            <FolderPen size={16} aria-hidden="true" /> <Trans>Rename folder</Trans>
          </button>
        )}
        {item && !isLocal(item.id) && <Versions entry={entry} source={source} />}
        {item && <UpdatesChoice id={item.id} />}
      </div>
      {item && (
        <div className="panel-group">
          <h4>
            <Trans>Library</Trans>
          </h4>
          <Customize id={item.id} />
          <HideButton id={item.id} />
          <Consoles id={item.id} />
          <Tags id={item.id} />
          <button
            className="panel-action danger"
            onClick={() => {
              // Deleting its files asks first; taking it out of the library alone doesn't.
              if (install && !install.local) return confirmUninstall({ entry, remove: true, then: onClose });
              remove(entry.id);
              onClose?.();
            }}
          >
            <Trash2 size={16} aria-hidden="true" />
            {install && !install.local ? t`Uninstall and remove` : t`Remove from library`}
          </button>
          {install?.local && (
            <p className="muted small">
              <Trans>Removing it from your library leaves its files where they are.</Trans>
            </p>
          )}
        </div>
      )}
    </section>
  );
  return (
    <section className="app-page" data-tv={tv || undefined} aria-label={entry.projectName}>
      <div className={`app-backdrop${hero ? " has-art" : ""}`}>
        {hero && <SourcedImage key={hero} className="backdrop-art" sources={heroSources(hero)} alt="" priority onFail={() => {}} />}
        <div className="backdrop-inner">
          {onClose && !tv && (
            <button className="back-link" onClick={onClose}>
              <ArrowLeft size={15} /> {back}
            </button>
          )}
          <div className="app-hero">
            <Artwork src={entry.artwork ?? entry.libraryArt?.logo} name={entry.projectName} />
            <div className="app-heading">
              <div className="eyebrow">{[eyebrow, PROJECT_TYPES[entry.projectType] && i18n._(PROJECT_TYPES[entry.projectType])].filter(Boolean).join(" / ").toLocaleUpperCase(i18n.locale)}</div>
              <h1>{entry.projectName}</h1>
              {entry.games.length > 0 && (
                <div className="based-on">
                  <span className="based-on-label">
                    <Trans>Based on</Trans>
                  </span>
                  {/* As on the website, each opens the original game's page. */}
                  {entry.games.map((g) => (
                    <button key={g.id} type="button" className="game-chip" data-game={g.slug} onClick={() => onOpenGame(g)}>
                      {g.title}
                    </button>
                  ))}
                  {entry.basedOn && <span className="based-on-version">{gameVersionLabel(entry.basedOn, consoleNames)}</span>}
                </div>
              )}
              {entry.description && <p className="app-tagline">{entry.description}</p>}
            </div>
            {/* The version, apart from the rest at the top right. */}
            {(entry.verified || install) && (
              <div className="app-version">
                {/* A withdrawn one says so, never "verified" (the site should have moved verified back). */}
                {entry.verified && pulledVerified(catalog[entry.id]) ? (
                  <span className="verified withdrawn" title={pulledVerified(catalog[entry.id])?.reason}>
                    <ShieldX size={15} />
                    <Trans>{verifiedVersion} · withdrawn</Trans>
                  </span>
                ) : entry.verified && (
                  <span className="verified">
                    <ShieldCheck size={15} />
                    {isOwn(entry.id)
                      ? t`${verifiedVersion} · not checked by Quiver`
                      : entry.verified.pinned
                        ? t`${verifiedVersion} · verified`
                        : t`${verifiedVersion} · files can't be verified`}
                  </span>
                )}
                {install && !install.local && (
                  <span className="muted">
                    <Trans>Installed {installedVersion}</Trans>
                  </span>
                )}
                {install?.unverified &&
                  (install.blocked ? (
                    <ReleaseBadge state="blocked" title={t`Quiver blocked it: ${blockedWhy}`} />
                  ) : (
                    <ReleaseBadge state="unverified" title={t`Installed before Quiver verified it`} />
                  ))}
              </div>
            )}
          </div>
          <div className="app-main-actions">
            {pulled && (
              <div className="withdrawn-notice" role="alert">
                <ShieldX size={18} aria-hidden="true" />
                <p>
                  <strong>
                    <Trans>{pulledVersion} was withdrawn</Trans>
                  </strong>
                  <span>{pulled.reason}</span>
                </p>
              </div>
            )}
            <div className="app-actions" data-autofocus={tv || undefined}>
              <Action entry={entry} apart />
              {install && foldersOn(config.os) && !tv && (
                <button onClick={() => (setProblem(null), native.openFolder(entry.id).catch((e) => setProblem(String(e))))}>
                  <FolderOpen size={17} /> <Trans>Open folder</Trans>
                </button>
              )}
              {/* Installed here, not in the library: a way in. */}
              {install && !item && (
                <button onClick={() => add(entry)}>
                  <Plus size={17} /> <Trans>Add to library</Trans>
                </button>
              )}
              {/* Its files go; the app stays in the library to get again. Taking it out is under On this computer. */}
              {install && !install.local && !(job && !("error" in job)) && !tv && (
                <button className="danger" onClick={() => confirmUninstall({ entry })}>
                  <Trash2 size={17} /> <Trans>Uninstall</Trans>
                </button>
              )}
            </div>
            {/* When it was added to the library is the sidebar's to say; here, what playing it has come to. */}
            {!onClose && <PlayStats install={install} item={item} added={false} />}
            {problem && (
              <p className="job-error" role="status">
                {problem}
              </p>
            )}
            {listed && !library.some((i) => i.id === listed.id) && (
              <p className="release-pinned-line">
                <ShieldCheck size={14} aria-hidden="true" />
                <span>
                  {install ? (
                    <Trans>Quiver&apos;s catalog lists this app now, and checks its releases. Switching keeps its files, play time and your settings.</Trans>
                  ) : (
                    <Trans>Quiver&apos;s catalog lists this app now, and checks its releases.</Trans>
                  )}
                </span>
                <button type="button" className="link-button" onClick={() => switchToCatalog(entry.id, listed)}>
                  <Trans>Switch to the catalog&apos;s</Trans>
                </button>
              </p>
            )}
            {install?.blocked && !install.local && (
              <p className="release-pinned-line blocked" role="alert">
                <ShieldX size={14} aria-hidden="true" />
                <span>
                  <Trans>Quiver blocked {installedVersion}: {blockedWhy}</Trans>
                </span>
                {entry.verified && bare(entry.verified.version) !== bare(install.version) && (
                  <button type="button" className="link-button" onClick={() => get(entry)}>
                    <Trans>Install the verified {verifiedVersion}</Trans>
                  </button>
                )}
              </p>
            )}
            {install?.differs && (
              <p className="release-pinned-line differs" role="alert">
                <ShieldAlert size={14} aria-hidden="true" />
                <span>
                  <Trans>The verified {installedVersion} isn&apos;t the file you installed.</Trans>
                </span>
                <button type="button" className="link-button" onClick={() => get(entry)}>
                  <Trans>Install the verified {installedVersion}</Trans>
                </button>
              </p>
            )}
            {install && !install.local && install.updates === "pinned" && (
              <p className="release-pinned-line">
                <Pin size={14} aria-hidden="true" />
                <span>
                  {/* Quietly: no badge or count, but a pin never hides that there's more. */}
                  {entry.verified && bare(entry.verified.version) !== bare(install.version) && entry.verified.releasedAt > (install.releasedAt ?? 0) ? (
                    <Trans>Pinned to {installedVersion}. {verifiedVersion} is available.</Trans>
                  ) : (
                    <Trans>Pinned to {installedVersion}.</Trans>
                  )}
                </span>
                <button type="button" className="link-button" onClick={() => setUpdates(entry.id, undefined)}>
                  <Trans>Unpin</Trans>
                </button>
              </p>
            )}
            {install && !install.local && install.updates === "auto" && skipped(entry, install) && (
              <p className="release-pinned-line">
                <span>
                  <Trans>Auto Update won&apos;t put {verifiedVersion} back. It installs the next new release.</Trans>
                </span>
                <button type="button" className="link-button" onClick={() => setUpdates(entry.id, "pinned")}>
                  <Trans>Always stay on {installedVersion}</Trans>
                </button>
              </p>
            )}
          </div>
          {/* What players said, at the foot of the art across from the buttons; opened, the player's own say. */}
          {site && (
            <div className="app-feedback-spot">
              <FeedbackBox entry={entry} count={said} own={own} onShare={share} />
            </div>
          )}
        </div>
      </div>
      <div className="detail-bar">
        {site && (
          <div className="detail-tabs" role="tablist" aria-label={t`App sections`}>
            <TabPrompt step="prevTab" />
            {tabs.map(([key, label, count]) => {
              const Icon = TAB_ICONS[key];
              return (
              <button
                key={key}
                type="button"
                role="tab"
                aria-selected={tab === key}
                onClick={() => setTab(key)}
                {...(key === "releases" ? { onPointerEnter: wantHistory, onFocus: wantHistory } : {})}
              >
                <Icon className="tab-icon" size={19} aria-hidden="true" />
                {label}
                {count !== undefined && <span className="tab-count">{count}</span>}
              </button>
              );
            })}
            <TabPrompt step="nextTab" />
          </div>
        )}
        {/* A catalog app's platforms and repository are in its Project details, beside every tab. */}
        {!site && (
          <div className="app-links">
            <PlatformIcons os={entry.supportedOS} />
            {source && <RepositoryLink source={source} />}
          </div>
        )}
      </div>
      <div className="app-columns">
        <div ref={content} className="app-content" role={site ? "tabpanel" : undefined}>
          {tab === "overview" && (
            <>
              {/* Short, general facts first, over the README, which can run long. */}
              {site && <ProjectDetails entry={entry} detail={detail} source={source} />}
              {/* In TV mode, long and full of links: there when asked for. */}
              {tv && !readme ? (
                <button type="button" className="panel-action opens tv-readme" onClick={() => setReadme(true)}>
                  <BookOpen size={18} aria-hidden="true" /> <Trans>Read the README</Trans>
                </button>
              ) : (
                <Readme entry={entry} source={source} />
              )}
            </>
          )}
          {tab === "media" && <MediaTab videos={media} slug={entry.slug} />}
          {tab === "releases" && <ReleasesTab entry={entry} detail={detail} history={history} checking={checking} openVersion={at?.releases} />}
          {(site ? tab === "computer" : true) && computer}
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
      </div>
      {site && <MoreLikeThis entry={entry} onOpen={onOpenApp} action={(e) => <Action entry={e} />} />}
    </section>
  );
}

// Names put in the language when shown, not here, where the module loads before any is picked.
const PROJECT_TYPES: Record<string, MessageDescriptor> = {
  port: msg({ message: "Port", comment: "A kind of project: a game brought to other platforms" }),
  tool: msg({ message: "Tool", comment: "A kind of project" }),
  emulator: msg`Emulator`,
  game: msg`Standalone game`,
};
const SORTS = ["added", "updated", "rating", "name"] as const;
/** Browse's catalog filters; several kinds of project come from saved filters. */
/** The catalog's filters, with "here" for the platforms this computer runs (catalogOs). */
type BrowseFilters = Omit<AppQuery, "search" | "cursor" | "limit" | "projectType" | "os"> & { os?: Os | "here" };

/** Runs a Windows installer's setup again for the installed version, as to repair it. */
function ReinstallButton({ id }: { id: string }) {
  const { reinstall } = useLauncher();
  return (
    <button className="panel-action" onClick={() => void reinstall(id)}>
      <RotateCcw size={16} aria-hidden="true" /> <Trans>Run installer again</Trans>
    </button>
  );
}

/**
 * How an installed app updates on this computer. A local app has no releases
 * to update from; a Windows installer's setup waits for the player, so it
 * never updates by itself.
 */
function UpdatesChoice({ id }: { id: string }) {
  const { installs, setUpdates } = useLauncher();
  const install = installs[id];
  if (!install || install.local) return null;
  const auto = !install.installer;
  const installedVersion = versionLabel(install.version);
  return (
    <label className="panel-field">
      <span className="field-label">
        <Trans comment="How an installed app gets new releases">Updates</Trans>
      </span>
      <select
        value={install.updates === "auto" && !auto ? "ask" : (install.updates ?? "ask")}
        onChange={(e) => setUpdates(id, e.target.value === "ask" ? undefined : (e.target.value as "auto" | "pinned"))}
      >
        <option value="ask">{t({ message: "Offer them", comment: "Updates: offer new releases to install" })}</option>
        {auto && <option value="auto">{t`Install automatically`}</option>}
        <option value="pinned">{t`Always stay on ${installedVersion}`}</option>
      </select>
    </label>
  );
}

/** Whether the library on this computer shows the app. */
function HideButton({ id }: { id: string }) {
  const { settings, setSettings } = useLauncher();
  const hidden = settings.hidden?.includes(id);
  return (
    <button
      className="panel-action"
      onClick={() =>
        setSettings({ ...settings, hidden: hidden ? settings.hidden!.filter((h) => h !== id) : [...(settings.hidden ?? []), id] })
      }
    >
      {hidden ? <Eye size={16} aria-hidden="true" /> : <EyeOff size={16} aria-hidden="true" />}
      {hidden ? t`Show in library` : t`Hide from library`}
    </button>
  );
}

/** Overrides the catalog's name and artwork for this player; empty shows the catalog's again. */
function Customize({ id }: { id: string }) {
  const { library, catalog, customize } = useLauncher();
  const current = library.find((i) => i.id === id)?.overrides ?? {};
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(current.name ?? "");
  const [cover, setCover] = useState(current.cover ?? "");
  if (!open)
    return (
      <button className="panel-action opens" onClick={() => setOpen(true)}>
        <Pencil size={16} aria-hidden="true" /> <Trans>Change name or artwork</Trans>
      </button>
    );
  // Closing without saving puts back what's saved.
  const close = () => (setName(current.name ?? ""), setCover(current.cover ?? ""), setOpen(false));
  const badCover = cover.trim() !== "" && !/^https:\/\/\S+$/.test(cover.trim());
  return (
    <form
      className="panel-form customize"
      aria-label={t`Name and artwork`}
      onSubmit={(e) => {
        e.preventDefault();
        if (badCover) return;
        customize(id, { name: name.trim() || undefined, cover: cover.trim() || undefined });
        setOpen(false);
      }}
    >
      <PanelFormHead title={t`Name and artwork`} onClose={close} />
      <label className="panel-field">
        <span className="field-label">
          <Trans>Name</Trans>
        </span>
        <input aria-label={t`Name`} maxLength={200} placeholder={catalog[id]?.projectName} value={name} onChange={(e) => setName(e.target.value)} />
      </label>
      <label className="panel-field">
        <span className="field-label">
          <Trans>Artwork</Trans>
        </span>
        <input aria-label={t`Artwork address`} placeholder="https://…" value={cover} onChange={(e) => setCover(e.target.value)} />
      </label>
      {badCover && (
        <p className="job-error">
          <Trans>Use an https:// image address.</Trans>
        </p>
      )}
      <button type="button" className="link-button" onClick={() => (setName(""), setCover(""))}>
        <Trans>Use the catalog&apos;s name and artwork</Trans>
      </button>
      <div className="row">
        <button className="primary">
          <Trans>Save</Trans>
        </button>
        <button type="button" onClick={close}>
          <Trans>Cancel</Trans>
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
  const name = user?.name ?? "";
  return (
    <div className="account">
      {user ? (
        // One button with the player's name; Sign out is in its menu.
        <Popover
          label={t`Account: ${name}`}
          className="account-button"
          align="end"
          role="menu"
          panel="account-menu"
          icon={
            <>
              <CircleUser size={17} aria-hidden="true" />
              <span>{user.name}</span>
              <ChevronDown size={14} aria-hidden="true" />
            </>
          }
        >
          {(close) =>
            asked ? (
              <>
                <p className="account-menu-note">
                  <Plural value={unsynced} one="# change hasn't synced yet." other="# changes haven't synced yet." />
                </p>
                <button role="menuitem" onClick={() => void signOut().then(() => (setAsked(false), close()))}>
                  <LogOut size={15} aria-hidden="true" /> <Trans>Sign out anyway</Trans>
                </button>
                <button role="menuitem" onClick={() => (setAsked(false), close())}>
                  <Trans>Stay signed in</Trans>
                </button>
              </>
            ) : (
              <>
                {/* Where the button is only its icon (a phone), the name is here. */}
                <p className="account-menu-name">
                  <Trans>
                    Signed in as <strong>{name}</strong>
                  </Trans>
                </p>
                {user.addEmail && (
                  <button role="menuitem" title={t`So you can reset your password if you forget it`} onClick={() => (void native.openUrl(`${SITE}/account#email`), close())}>
                    <Mail size={15} aria-hidden="true" /> <Trans>Add an email</Trans>
                  </button>
                )}
                <button role="menuitem" onClick={() => (unsynced ? setAsked(true) : void signOut().then(close))}>
                  <LogOut size={15} aria-hidden="true" /> <Trans>Sign out</Trans>
                </button>
              </>
            )
          }
        </Popover>
      ) : (
        <button onClick={onSignIn}>
          <Trans>Sign in</Trans>
        </button>
      )}
    </div>
  );
}

/** Signing in is optional: it syncs the library and lets players review apps. */
function SignIn({ onClose }: { onClose: () => void }) {
  const { signIn, signInWith, emailLogin } = useAccount();
  const [create, setCreate] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  // Shown as it's typed, when the player asks: Steam's keyboard on a Steam Deck shows no key it pressed.
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [openedSite, setOpenedSite] = useState(false);
  // The GitHub or Discord sign-in started last. Another started, or the dialog closing, lets go of it, and its result is ignored.
  const attempt = useRef(0);
  useEffect(() => () => void native.browserSignInCancel(), []);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  return (
    <div className="overlay" {...closesOutside(onClose)}>
      <section className="detail choose" role="dialog" aria-label={t`Sign in`} onClick={(e) => e.stopPropagation()}>
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
          <h2>{create ? t`Create your Quiver account` : t`Sign in to Quiver`}</h2>
          <p className="muted">
            <Trans>Your library syncs across your devices, and you can review apps. Everything works without an account too.</Trans>
          </p>
          {(["github", "discord"] as const).map((provider) => {
            const service = provider === "github" ? "GitHub" : "Discord";
            return (
              <button
                key={provider}
                type="button"
                disabled={busy}
                // Pressed again, or the other pressed, while one waits: that one starts over (its page opening again), as a
                // browser page left behind, or never shown, would otherwise keep the player from signing in.
                onClick={() => {
                  const mine = ++attempt.current;
                  setWaiting(true);
                  setError(null);
                  void signInWith(provider).then((message) => {
                    if (mine !== attempt.current) return;
                    setWaiting(false);
                    if (message) setError(message);
                    else (track("signed_in", { method: provider }), onClose());
                  });
                }}
              >
                <Trans>Continue with {service}</Trans>
              </button>
            );
          })}
          {waiting && (
            <p className="muted">
              <Trans>Finish signing in in your browser.</Trans>
            </p>
          )}
          <p className="muted or">
            <Trans comment="Between the ways to sign in: with GitHub or Discord, or with a username">or</Trans>
          </p>
          <input
            autoFocus
            required
            placeholder={emailLogin ? t`Email or username` : t`Username`}
            autoComplete="username"
            value={username}
            onChange={(e) => setUsername(e.target.value)}
          />
          <span className="password-field">
            <input
              required
              type={showPassword ? "text" : "password"}
              placeholder={t`Password`}
              autoComplete={create ? "new-password" : "current-password"}
              autoCapitalize="off"
              autoCorrect="off"
              spellCheck={false}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
            <button type="button" aria-label={showPassword ? t`Hide password` : t`Show password`} title={showPassword ? t`Hide password` : t`Show password`} aria-pressed={showPassword} onClick={() => setShowPassword(!showPassword)}>
              {showPassword ? <EyeOff size={16} aria-hidden="true" /> : <Eye size={16} aria-hidden="true" />}
            </button>
          </span>
          {error && <p className="job-error">{error}</p>}
          <button className="primary" disabled={busy}>
            {create ? t`Create account` : t`Sign in`}
          </button>
          {emailLogin ? (
            // The site emails a link to confirm the address, which only works in the browser that asked for it.
            <>
              <button type="button" onClick={() => (setOpenedSite(true), void native.openUrl(`${SITE}/signup`))}>
                <Trans>Create an account</Trans>
              </button>
              <button type="button" className="link-button" onClick={() => (setOpenedSite(true), void native.openUrl(`${SITE}/forgot-password`))}>
                <Trans>Forgot your password?</Trans>
              </button>
              {openedSite && (
                <p className="muted">
                  <Trans>Finish in your browser, then sign in here.</Trans>
                </p>
              )}
            </>
          ) : (
            <button type="button" onClick={() => (setCreate(!create), setError(null))}>
              {create ? t`I already have an account` : t`Create an account`}
            </button>
          )}
        </form>
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
      <button onClick={() => setNotice(null)}>
        <Trans>OK</Trans>
      </button>
    </div>
  );
}

/** Play, for an app with more than one of its programs picked (its menu's Play with): which one starts. */
function ProgramPrompt() {
  const { choosingProgram: id, chooseProgram, installs, catalog, play } = useLauncher();
  const install = id ? installs[id] : undefined;
  const programs = install?.launch?.programs ?? [];
  if (!id || !programs.length) return null;
  const close = () => chooseProgram(null);
  const name = catalog[id]?.projectName;
  return (
    <Dialog label={name ? t`Play ${name}` : t({ message: "Play", comment: "Button that starts an app" })} onClose={close}>
      <div className="detail-body program-prompt">
        <h2>
          <Play size={18} aria-hidden="true" /> {name ? t`Start which of ${name}'s programs?` : t`Start which program?`}
        </h2>
        <div className="choices">
          {programs.map((p, i) => (
            <button key={p} autoFocus={i === 0} onClick={() => (close(), void play(id, p))}>
              <strong>{fileName(p)}</strong>
              {p !== fileName(p) && <span className="muted">{p}</span>}
            </button>
          ))}
        </div>
        <p className="muted small">
          <Trans>The programs Play offers are under Play with in the app&apos;s menu.</Trans>
        </p>
      </div>
    </Dialog>
  );
}

/** Deleting an app's files takes a yes. Cancel comes first, where a controller starts, and B or Escape is Cancel too. */
function UninstallPrompt() {
  const { confirming, confirmUninstall, uninstall, remove, library } = useLauncher();
  if (!confirming) return null;
  const { entry, then } = confirming;
  const name = entry.projectName;
  // Out of the library too: said only when it's in there.
  const leaves = confirming.remove && library.some((i) => i.id === entry.id);
  const close = () => confirmUninstall(null);
  return (
    <Dialog label={t`Uninstall ${name}`} onClose={close}>
      <div className="detail-body uninstall-prompt">
        <h2>
          <Trash2 size={18} /> {leaves ? t`Uninstall ${name} and remove it from your library?` : t`Uninstall ${name}?`}
        </h2>
        <p>
          {leaves
            ? t`Its files go to the trash, and it leaves your library.`
            : confirming.remove
              ? t`Its files go to the trash.`
              : t`Its files go to the trash. ${name} stays in your library, so you can get it again.`}
        </p>
        <div className="row">
          <button className="primary" autoFocus onClick={close}>
            <Trans>Cancel</Trans>
          </button>
          <button
            className="danger"
            onClick={() => {
              close();
              void (confirming.remove ? remove(entry.id) : uninstall(entry.id));
              then?.();
            }}
          >
            {leaves ? t`Uninstall and remove` : t`Uninstall`}
          </button>
        </div>
      </div>
    </Dialog>
  );
}

/**
 * A new name for an app's folder in the apps folder. Its updates go on
 * installing there; shortcuts made before point at the old name.
 */
function RenameFolderPrompt() {
  const { renaming, askRename, renameFolder, installs, setNotice } = useLauncher();
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const folder = renaming ? installs[renaming.id]?.folder : undefined;
  useEffect(() => {
    setName(folder ?? "");
    setError(null);
    // The folder's name fills the field as it opens.
  }, [renaming]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!renaming || folder === undefined) return null;
  const app = renaming.projectName;
  const close = () => askRename(null);
  return (
    <Dialog label={t`Rename ${app}'s folder`} onClose={close}>
      <form
        className="detail-body rename-folder"
        onSubmit={(e) => {
          e.preventDefault();
          setBusy(true);
          setError(null);
          renameFolder(renaming.id, name).then(
            (renamed) => {
              setBusy(false);
              close();
              if (renamed !== folder) setNotice(t`${app}'s folder is now ${renamed}.`);
            },
            (e) => (setBusy(false), setError(e instanceof Error ? e.message : String(e))),
          );
        }}
      >
        <h2>
          <FolderPen size={18} /> <Trans>Rename {app}&apos;s folder</Trans>
        </h2>
        <input autoFocus required aria-label={t`Folder name`} maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
        <p className="muted">
          <Trans>Updates keep installing into it. Desktop and Steam shortcuts you made before point at the old name, so make them again.</Trans>
        </p>
        {error && (
          <p className="job-error" role="status">
            {error}
          </p>
        )}
        <div className="row">
          <button className="primary" disabled={busy}>
            <Trans comment="Renames an app's folder">Rename</Trans>
          </button>
          <button type="button" onClick={close}>
            <Trans>Cancel</Trans>
          </button>
        </div>
      </form>
    </Dialog>
  );
}

function ChooseFile() {
  const { choice } = useLauncher();
  if (!choice) return null;
  const name = choice.entry.projectName;
  const version = versionLabel(choice.version);
  return (
    <div className="overlay">
      <section className="detail choose" role="dialog" aria-label={t`Choose a download`}>
        <div className="detail-body">
          <h2>
            <Trans>Which download?</Trans>
          </h2>
          <p className="muted">
            {choice.unmatched
              ? t`None of ${name} ${version}'s files are marked for your computer. If you know which one runs on it, pick it.`
              : t`${name} ${version} has more than one file for your computer.`}
          </p>
          <div className="choices">
            {choice.assets.map((a) => (
              <button key={a.id} onClick={() => choice.resolve(a)}>
                {/* What the site says each is for, as the app's release files show it. */}
                {choice.unmatched ? (
                  <span>
                    <strong>
                      {a.os === "unknown" ? t`Platform unspecified` : (OS_NAMES[a.os] ?? a.os)} · {a.architecture}
                    </strong>
                    <small>{a.filename}</small>
                  </span>
                ) : (
                  a.filename
                )}
              </button>
            ))}
          </div>
          <button onClick={() => choice.resolve(null)}>
            <Trans>Cancel</Trans>
          </button>
        </div>
      </section>
    </div>
  );
}

/**
 * Asks before an install takes what Quiver hasn't verified, as Change version asks before an unverified release:
 * the newest release of an app with no verified one, or a file of a verified release that changed since or that
 * Quiver has no checksum for. Waiting is the easy choice (Cancel, when nothing says it's verified by itself): it's
 * first, and where a controller starts.
 */
function UnverifiedPrompt() {
  const { asking } = useLauncher();
  if (!asking) return null;
  const { entry, file, reasons, scan, checkEndsAt, resolve } = asking;
  // Put in the sentences below by name.
  const release = versionLabel(asking.version);
  const app = entry.projectName;
  const name = file?.name ?? "";
  const unchecked = file?.why === "unchecked";
  const waits = checkEndsAt !== undefined;
  const hours = waits ? hoursFrom(checkEndsAt) : 0;
  const title = unchecked ? t`Install a file Quiver didn't check?` : t`Install ${release} before it's verified?`;
  return (
    // Each question its own: the next one in turn starts afresh, its first button focused.
    <Dialog key={`${entry.id}@${release}:${name}`} label={unchecked ? title : t`Install a release before it's verified`} onClose={() => resolve(false)}>
      <div className="detail-body unverified-prompt">
        <h2>
          <ShieldAlert size={18} /> {title}
        </h2>
        <p>
          {!file
            ? `${t`${app} has no verified release yet.`} ${t`Quiver hasn't verified this release. It installs the files Quiver saw when it came out, and refuses any that changed since.`}`
            : unchecked
              ? t`Quiver checked ${release}'s other files, not ${name}. It downloads as the developer published it.`
              : t`${name} changed after Quiver verified ${release}, and Quiver hasn't verified the new file yet. It installs the file Quiver saw, and refuses one that changed since.`}
        </p>
        {reasons.length > 0 && (
          <ul className="unverified-reasons">
            {reasons.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
        {scan && (
          <p className="scan-line">
            VirusTotal: {scanText(scan)}{" "}
            {scan.url && (
              <a href={scan.url} target="_blank" rel="noreferrer">
                <Trans>See the report</Trans>
              </a>
            )}
          </p>
        )}
        {waits && (
          <p className="wait-line">
            <ShieldCheck size={14} aria-hidden="true" />{" "}
            {plural(hours, { one: "If you wait, it's verified in about # hour.", other: "If you wait, it's verified in about # hours." })}
          </p>
        )}
        <div className="row">
          <button className="primary" autoFocus onClick={() => resolve(false)}>
            {waits ? t({ message: "Wait", comment: "Button: waits for the release to be verified instead of installing it now" }) : t`Cancel`}
          </button>
          <button onClick={() => resolve(true)}>
            <Trans>Install anyway</Trans>
          </button>
        </div>
      </div>
    </Dialog>
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
