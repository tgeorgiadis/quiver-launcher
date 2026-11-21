import { startTransition, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ArrowUp, BookOpen, Save, ChevronDown, Cloud, CircleArrowDown, CircleCheck, CirclePlay, CircleUser, Compass, Disc3, Download, Eye, EyeOff, FolderInput, FolderOpen, FolderPen, FolderSearch, FolderSync, History, Info, MessagesSquare, LayoutGrid, Library, LogOut, PanelLeft, Mail, PackagePlus, Pencil, Pin, Play, Plus, Search, SettingsIcon, RotateCcw, ShieldAlert, ShieldCheck, ShieldX, SlidersHorizontal, Smile, Trash2, TriangleAlert, Tv, Wrench, X } from "lucide-react";
import type { AppQuery, Asset, Entry, GameMatch, Os, Page, ProjectType } from "@quiverlauncher/api";
import { msg, plural, t } from "@lingui/core/macro";
import { Plural, Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import { Artwork, EntryCard, OS_NAMES, PlatformIcons, ReleaseBadge, SourcedImage, heroSources } from "@quiverlauncher/ui";
import { PORTABLE_FILE, availableOn, canGet, catalogOs, declinedUpdate, earlyGetOffer, earlyOffer, filesAddable, folderNameOf, hasUpdate, locatable, movable, noProgram, pulledVerified, renamable, rootOf, samePath, skipped, stillWaiting, tvModeOn, updateFor, updatesByItself, updatesOf, useLauncher, type AppMove, type Choice } from "./store";
import { TabPrompt } from "./pad-hints";
import { Dialog, FilterPill, LibraryPage, MultiPill, Popover, consoleOptions, filterLabel, pickedLabel, projectTypeOptions, withOverrides, type MultiOption } from "./library";
import { BrowseCardOptions, useBrowseCards, useLibraryView, useSidebarFits } from "./library-view";
import { DownloadsButton, DownloadsPage, DownloadsStrip, SidebarDownloads } from "./downloads-page";
import { VersionsDialog, PanelFormHead, ProjectDetails, Readme, ReleasesTab, RepositoryLink, Shortcuts, Consoles, Tags, Versions, checkingOf, foldersOn, hoursFrom, scanText, useAppDetail, useListed, useReleaseHistory, useReleases } from "./detail";
import { UpdateBanner, useUpdateCheck } from "./update";
import { useAnnouncementsCheck } from "./announcements";
import { AnnouncementBanner, AnnouncementsButton, AnnouncementsDialog, useTidyDismissals } from "./announcements-ui";
import { LaunchOptionsButton, outsideFolder } from "./launch-options";
import { AddFilesButton, AddFilesDialogBody } from "./app-files";
import { versionLabel, wayOffLabel } from "./versions";
import { FeedbackBox, FeedbackTab, useOwnFeedback, type Intent } from "./feedback";
import { CommunityButton, CommunityPage, DiscussionNavProvider, DiscussionPage, DiscussionsTab, useDiscussionCount } from "./discussions";
import { CorrectionStatusPanel, ReportProblem } from "./correction";
import { GamePage, GamesSection, gameVersionLabel, type GameLink } from "./game";
import { WaysToPlay } from "./ways-to-play";
import { Discover, MoreLikeThis } from "./discover";
import { ErrorBoundary } from "./boundary";
import { FORWARD, pageArea, toTop } from "./spatial";
import { track, trackScreen } from "./telemetry";
import { TelemetryNotice, useTelemetrySetup } from "./telemetry-ui";
import { managesCatalog, useAccount, type CatalogDefaults } from "./account";
import { native } from "./native";
import { sizeLabel } from "./downloads";
import { correctionClient } from "./corrections";
import { isCustom, isLocal, isOwn } from "./custom";
import { ArtFields, artValues, artValuesOk, fromArtValues } from "./custom-art";
import { AddApp, ChangeGame, LinkToCatalog } from "./own";
import { ModsButton } from "./mods";
import { MediaTab } from "./media";
import { PlayStats } from "./library-sidebar";
import { useTopBarFit } from "./top-bar-fit";
import { ResizeEdges, WindowControls, moveWindow, movesByPage, resizeEdges, useWindowButtons } from "./window-bar";
import { SettingsPage, useDiscordPresence, useTray } from "./settings-page";
import { useDownloadCache } from "./cache-fetch";
import { CacheChoice } from "./cache-page";
import { MANAGE_TAGS } from "./tag-settings";
import { OPEN_APP } from "./cloud-saves-page";
import { OldLibrary } from "./old-library";
import { Setup, showsSetup } from "./setup";
import { savesChanged, savesOf, savesOn } from "./saves";
import type { CloudUsage } from "./cloud-saves";
import { SavesTab } from "./saves-tab";
import { FilePicker } from "./file-picker";
import { BrowseMenu, fileName } from "./card-menu";
import { browseShown, hiddenMatches, toggleHidden, type HiddenView } from "./browse-hidden";
import { closesOutside } from "./overlay";
import { OwnCopyChips, OwnCopyPrompt } from "./own-copy-setup";
import { copiesNeeded } from "./own-copy";
import { GameCopiesNotice } from "./own-copy-settings";
import { AppUpdatesNotice } from "./app-updates";

type Tab = "library" | "browse";
/** A page over the library or catalog: an app's, an original game's, Downloads or Settings. */
/** Where an app's page opens: its Releases, with a version's notes open (Downloads' See changelog), its Player feedback, or its Discussions (a discussion's app name). */
export type PageAt = { releases: string } | { feedback: true } | { readme: true } | { discussions: true };
type View =
  | { kind: "app"; entry: Entry; at?: PageAt }
  | { kind: "game"; game: GameLink }
  | { kind: "downloads" }
  | { kind: "settings"; section?: "library" | "copies" | "cloud" | "updates" }
  | { kind: "community" }
  | { kind: "discussion"; id: string };
const viewKey = (v: View) => (v.kind === "app" ? `app:${v.entry.id}` : v.kind === "game" ? `game:${v.game.slug}` : v.kind === "discussion" ? `discussion:${v.id}` : v.kind);
const shorten = (text: string) => (text.length > 40 ? `${text.slice(0, 39).trimEnd()}…` : text);
/** "v1.2" for "1.2" or "v1.2": releases are tagged either way. */
/** "v1.2" and "1.2" are the same release. */
/** The website, where accounts are created and passwords reset. */
const SITE = "https://quiverlauncher.com";
const bare = (version: string) => version.trim().replace(/^v/i, "");

export function App() {
  useTelemetrySetup();
  useUpdateCheck();
  useTray();
  useDiscordPresence();
  useDownloadCache();
  const { library, client, settings, setSettings, config, catalog } = useLauncher();
  // TV mode, a larger look over the library and Browse: turned on, or by itself in Steam's Game Mode.
  const tv = tvModeOn(settings, config);
  const setTv = (on: boolean) => setSettings({ ...settings, tvMode: on ? "on" : "off" });
  useAnnouncementsCheck(client);
  useTidyDismissals();
  // A new player starts in the catalog; everyone else in their library.
  const [tab, setTab] = useState<Tab>(library.length ? "library" : "browse");
  // Pages opened from the list, each over the one before: Back goes down one.
  const [stack, setStack] = useState<View[]>([]);
  const [search, setSearch] = useState("");
  // Browse's filters as the player picked them, kept while they're in the library: Browse starts again from them.
  const [browseKept, setBrowseKept] = useState<KeptBrowse | null>(null);
  const [signingIn, setSigningIn] = useState(false);
  // Read before this start counts as one (useTelemetrySetup).
  const [settingUp, setSettingUp] = useState(() => showsSetup(settings));
  const [adding, setAdding] = useState(false);
  const [announcementsOpen, setAnnouncementsOpen] = useState(false);
  // Where each covered page was scrolled to and what had focus, to put back on the way back.
  const covered = useRef<{ y: number; focus: Element | null }[]>([]);
  // The pages Back left, the last one left last: Forward opens them again, until another page is opened.
  const ahead = useRef<View[]>([]);
  const push = useCallback((view: View) => {
    const here = { y: pageArea().scrollTop, focus: document.activeElement };
    setStack((s) => {
      covered.current[s.length] = here;
      // A page already open further down is gone back to, so the stack stays short.
      const i = s.findIndex((v) => viewKey(v) === viewKey(view));
      return i >= 0 ? s.slice(0, i + 1) : [...s, view];
    });
  }, []);
  const open = useCallback(
    (view: View) => {
      ahead.current = [];
      push(view);
    },
    [push],
  );
  const openApp = useCallback((entry: Entry, at?: PageAt) => open({ kind: "app", entry, at }), [open]);
  const openGame = useCallback((game: GameLink) => open({ kind: "game", game }), [open]);
  const openDownloads = useCallback(() => open({ kind: "downloads" }), [open]);
  const openSettings = useCallback(() => open({ kind: "settings" }), [open]);
  const openCommunity = useCallback(() => open({ kind: "community" }), [open]);
  const openDiscussion = useCallback((id: string) => open({ kind: "discussion", id }), [open]);
  // A discussion's app name opens that app's Discussions; an app not in the catalog loaded here is asked for by its slug.
  const openAppDiscussions = useCallback(
    async (entryId: string, slug?: string) => {
      const entry = catalog[entryId] ?? (slug ? (await client.app(slug).catch(() => null))?.entry : undefined);
      if (entry) openApp(entry, { discussions: true });
    },
    [catalog, client, openApp],
  );
  const discussionNav = useMemo(
    () => ({ openDiscussion, openCommunity, openApp: openAppDiscussions, signIn: () => setSigningIn(true) }),
    [openDiscussion, openCommunity, openAppDiscussions],
  );
  // An app's Tags menu asks for the player's tags, in Settings' Library section (tag-settings.tsx).
  useEffect(() => {
    const manage = () => open({ kind: "settings", section: "library" });
    window.addEventListener(MANAGE_TAGS, manage);
    return () => window.removeEventListener(MANAGE_TAGS, manage);
  }, [open]);
  // Settings' Cloud saves lists apps, and each can ask for its page (cloud-saves-page.tsx).
  useEffect(() => {
    const page = (e: Event) => openApp((e as CustomEvent<Entry>).detail);
    window.addEventListener(OPEN_APP, page);
    return () => window.removeEventListener(OPEN_APP, page);
  }, [openApp]);
  // Read from the stack as shown, not in setStack's updater, which React can run twice.
  const shown = useRef<View[]>([]);
  const goBack = useCallback(() => {
    const left = shown.current.at(-1);
    if (left) ahead.current.push(left);
    setStack((s) => s.slice(0, -1));
  }, []);
  const goForward = useCallback(() => {
    // An app the player added has no page once it's removed: there's no catalog to show it from.
    ahead.current = ahead.current.filter((view) => view.kind !== "app" || !isOwn(view.entry.id) || library.some((i) => i.id === view.entry.id));
    const next = ahead.current.pop();
    if (next) push(next);
  }, [push, library]);
  const switchTab = (next: Tab) => {
    setTab(next);
    setStack([]);
    covered.current = [];
    ahead.current = [];
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
    shown.current = stack;
  }, [stack]);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && pages.current && !document.querySelector('[role="dialog"]')) goBack();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [goBack]);
  // Forward (spatial.ts): not from under a dialog, a menu or a dropdown's list, which would stay open over the new page.
  useEffect(() => {
    const onForward = () => void (!document.querySelector('[role="dialog"], [role="menu"], [role="listbox"], .menu.floating') && goForward());
    window.addEventListener(FORWARD, onForward);
    return () => window.removeEventListener(FORWARD, onForward);
  }, [goForward]);
  const top = stack.at(-1);
  // Usage data: the screen shown, a catalog app's or game's by its slug (never one the player added).
  const screen = !top ? tab : top.kind === "app" ? `app:${isOwn(top.entry.id) ? "" : top.entry.slug}` : top.kind === "game" ? `game:${top.game.slug}` : top.kind;
  useEffect(() => {
    const [name, slug] = screen.split(":");
    trackScreen({ screen: name, ...(slug !== undefined ? { slug: slug || null } : {}) });
  }, [screen]);
  useEffect(() => void (adding && trackScreen({ screen: "add_app" })), [adding]);
  useEffect(() => void (signingIn && trackScreen({ screen: "sign_in" })), [signingIn]);
  const query = search.trim();
  const searched = shorten(query);
  const listName = tab === "library" ? t`Library` : query ? t`Back to results for “${searched}”` : t({ message: "Browse", comment: "The tab with the catalog of apps" });
  /** What Back on the page at `i` returns to. */
  const backFrom = (i: number) => {
    const below = stack[i - 1];
    return !below
      ? listName
      : below.kind === "app"
        ? below.entry.projectName
        : below.kind === "game"
          ? (below.game.title ?? t`Back`)
          : below.kind === "downloads"
            ? t`Downloads`
            : below.kind === "community"
              ? t`Community`
              : below.kind === "discussion"
                ? t`Back`
                : t`Settings`;
  };
  // The Sidebar layout's foot says how downloads are going, in place of the strip.
  const { view: libraryView, set: setLibraryView } = useLibraryView();
  const sidebar = libraryView.layout === "sidebar";
  const sidebarFits = useSidebarFits();
  const strip = !(tab === "library" && libraryView.layout === "sidebar" && sidebarFits);
  const action = (entry: Entry) => <Action entry={entry} />;
  const downloadAction = (entry: Entry) => <Action entry={entry} reasonElsewhere />;
  // Without the system's title bar, the top bar's empty space moves the window.
  const { buttons, maximized } = useWindowButtons();
  const topBar = useRef<HTMLElement>(null);
  useTopBarFit(topBar);
  return (
    <DiscussionNavProvider value={discussionNav}>
    <div className={tv ? "shell tv-mode" : "shell"}>
      {resizeEdges(config.os, buttons, maximized) && <ResizeEdges />}
      <header ref={topBar} className={buttons === "none" ? "topbar" : "topbar with-window-controls"} onMouseDown={buttons === "window" ? (e) => moveWindow(e, movesByPage(config.x11, maximized)) : undefined}>
        {/* As on the website; it goes to the library. For a mouse: the Library tab beside it does the same for the d-pad and Tab. */}
        <button className="brand" tabIndex={-1} aria-label={t`Quiver Launcher: go to your library`} onClick={() => switchTab("library")}>
          <img src="/quiver-icon-96.png" alt="" width="34" height="34" />
          <span>
            Quiver<span className="brand-label">LAUNCHER</span>
          </span>
        </button>
        {/* data-tabs: a controller's shoulder buttons go between them, as between TV mode's tabs (spatial.ts). */}
        <nav data-tabs>
          {tv && <TabPrompt step="prevTab" />}
          <button className={tab === "library" ? "active" : ""} aria-current={tab === "library" && !top ? "page" : undefined} onClick={() => switchTab("library")}>
            <Library size={16} aria-hidden="true" /> <Trans>Library</Trans> <span className="count">{library.length}</span>
          </button>
          <button className={tab === "browse" ? "active" : ""} aria-current={tab === "browse" && !top ? "page" : undefined} onClick={() => switchTab("browse")}>
            <Compass size={16} aria-hidden="true" /> <Trans comment="The tab with the catalog of apps">Browse</Trans>
          </button>
          {tv && <TabPrompt step="nextTab" />}
        </nav>
        {tv && <Clock />}
        <AccountButton onSignIn={() => setSigningIn(true)} />
        {/* The library as a grid or a list, from wherever the library is, as TV mode is beside it; none where the sidebar doesn't fit (a phone). */}
        {tab === "library" && sidebarFits && !tv && (
          <button
            className="icon layout-button"
            aria-label={sidebar ? t`Show the library as a grid` : t`Show the library as a list`}
            title={sidebar ? t`Show the library as a grid` : t`Show the library as a list`}
            onClick={() => setLibraryView({ layout: sidebar ? "grid" : "sidebar" })}
          >
            {sidebar ? <LayoutGrid size={19} /> : <PanelLeft size={19} />}
          </button>
        )}
        {tv ? (
          <button type="button" className="tv-leave" onClick={() => setTv(false)}>
            <LogOut size={17} aria-hidden="true" />{" "}
            <span className="tv-leave-label">
              <Trans comment="Button: back from TV mode to the usual launcher">Leave TV mode</Trans>
            </span>
          </button>
        ) : (
          <button className="icon tv-button" aria-label={t`TV mode`} title={t`TV mode: for a controller, the couch and a Steam Deck`} onClick={() => setTv(true)}>
            <Tv size={19} />
          </button>
        )}
        <CommunityButton open={top?.kind === "community"} onOpen={openCommunity} />
        <AnnouncementsButton onOpen={() => setAnnouncementsOpen(true)} />
        <DownloadsButton open={top?.kind === "downloads"} onOpen={openDownloads} />
        <button className={`icon settings-button${top?.kind === "settings" ? " active" : ""}`} aria-label={t`Settings`} title={t`Settings`} aria-current={top?.kind === "settings" ? "page" : undefined} onClick={openSettings}>
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
                  <DownloadsPage back={backFrom(i)} onBack={goBack} onOpen={openApp} action={downloadAction} />
                ) : view.kind === "settings" ? (
                  <SettingsPage back={backFrom(i)} onBack={goBack} onRunSetup={() => setSettingUp(true)} onOpen={openApp} start={view.section} />
                ) : view.kind === "community" ? (
                  <CommunityPage back={backFrom(i)} onBack={goBack} />
                ) : view.kind === "discussion" ? (
                  <DiscussionPage id={view.id} back={backFrom(i)} onBack={goBack} />
                ) : (
                  <GamePage game={view.game} back={backFrom(i)} onBack={goBack} onOpenApp={openApp} action={action} />
                )}
              </ErrorBoundary>
            </div>
          ))}
        </ErrorBoundary>
        {/* Kept mounted under the pages too, so going back keeps the search, filters and scroll. */}
        <div hidden={Boolean(top)}>
          {/* A download under way always shows how it's going, over whichever banner has its turn. */}
          {strip && <DownloadsStrip onOpen={openDownloads} active offerUpdates={tab === "library" && !tv} />}
          {/*
            One banner at a time, the most pressing first (app.css shows only the first that has something to say):
            the usage-data notice, the launcher's update, updates for apps, bringing over Quiver Launcher 3, then the
            newest announcement. Dealing with one shows the next.
          */}
          <div className="banner-stack">
            <TelemetryNotice />
            <UpdateBanner />
            {/* The updates waiting: the library's to offer, not Browse's; in TV mode, the Downloads button's count says so. */}
            {strip && tab === "library" && !tv && <DownloadsStrip onOpen={openDownloads} active={false} />}
            {/* Finishing an import shows the library, under whatever page is open. */}
            <OldLibrary
              onDone={() => {
                setTab("library");
                // Back from the page shows the library from its top, not where Browse was scrolled.
                covered.current[0] = { y: 0, focus: null };
              }}
            />
            {/* Once, for a player set up before Game copies: where their ROMs and disc images are. */}
            <GameCopiesNotice onOpen={() => open({ kind: "settings", section: "copies" })} />
            {/* Once, for a player set up before there was a choice: how apps update. */}
            <AppUpdatesNotice onOpen={() => open({ kind: "settings", section: "updates" })} />
            {/* Across the window under the top bar in the Sidebar layout, edge to edge. */}
            <AnnouncementBanner edge={!strip} onOpen={() => setAnnouncementsOpen(true)} onOpenApp={openApp} />
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
            <BrowsePage onOpen={openApp} onOpenGame={openGame} search={search} onSearch={setSearch} kept={browseKept} onKeep={setBrowseKept} />
          )}
        </div>
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
      <RenamePrompt />
      <PlaceAppPrompt />
      <AddFilesPrompt />
      <ChangeVersion />
      <ProgramPrompt />
      <SavesConflictPrompt />
      <SavesPlacesPrompt />
      <SavesFullPrompt onFreeUp={() => open({ kind: "settings", section: "cloud" })} />
      <OwnCopyPrompt />
      <Notice />
      {/* Over every dialog that asks for a file or a folder, in Steam's Game Mode. */}
      <FilePicker />
    </div>
    </DiscussionNavProvider>
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
  kept,
  onKeep,
}: {
  onOpen: (e: Entry, at?: PageAt) => void;
  onOpenGame: (game: GameLink) => void;
  search: string;
  onSearch: (search: string) => void;
  /** The filters the player picked when Browse was last open. */
  kept: KeptBrowse | null;
  onKeep: (kept: KeptBrowse) => void;
}) {
  const { client, config, library, remember, consoles, consoleNames, settings, setSettings, jobs, withShownTags } = useLauncher();
  const saved = useAccount().user?.catalogDefaults;
  // As in Quiver Launcher 3, apps already in the library stay out of Browse unless the player asks for them.
  const showOwned = Boolean(settings.browseShowsLibrary);
  const setShowOwned = (on: boolean) => setSettings({ ...settings, browseShowsLibrary: on || undefined });
  const looks = useBrowseCards();
  // Apps the player hid from Browse (browse-hidden.ts): out of it as soon as they're hidden, unlike the library's.
  const hidden = new Set(settings.browseHidden?.map((h) => h.id));
  const [hiddenView, setHiddenView] = useState<HiddenView>("hide");
  const only = hiddenView === "only";
  // Only hidden apps: asked for by themselves, as the catalog's pages could have them anywhere in it, and empty for pages of them.
  const [hiddenApps, setHiddenApps] = useState<Entry[] | null>(null);
  useEffect(() => {
    if (!only) return setHiddenApps(null);
    let live = true;
    Promise.allSettled((settings.browseHidden ?? []).map((h) => client.app(h.slug))).then((answers) => {
      if (!live) return;
      // One the catalog no longer lists just doesn't show.
      const found = answers.flatMap((a) => (a.status === "fulfilled" ? [a.value.entry] : []));
      if (!found.length && answers.some((a) => a.status === "rejected")) setError(t`Couldn't reach quiverlauncher.com. Check your connection and try again.`);
      remember(found);
      setHiddenApps(found);
    });
    return () => void (live = false);
  }, [only, client]); // eslint-disable-line react-hooks/exhaustive-deps
  // The last one shown again: back to the rest, rather than a view of nothing.
  useEffect(() => {
    if (!hidden.size) setHiddenView("hide");
  }, [hidden.size]);
  const libraryNow = useRef(library);
  libraryNow.current = library;
  // The apps left out: those in the library when their page came. One added since stays, with its badge.
  const [owned, setOwned] = useState<Set<string>>(new Set());
  // The site's catalog filters: the ones saved on the account ("Save as my
  // default" on the website), but the platform starts at what this computer
  // runs ("here"), since only those apps can be installed here.
  const fromSaved = (d?: CatalogDefaults): BrowseFilters => ({
    platforms: ["here"],
    sort: SORTS.find((s) => s === d?.sort) ?? "added",
    projectTypes: d?.projectTypes?.filter((t): t is ProjectType => t in PROJECT_TYPES).slice(0, 4),
    consoles: d?.console ? [d.console] : undefined,
    ai: d?.ai === "no-generated" || d?.ai === "no-ai" ? d.ai : undefined,
    setup: d?.setup === "easy" ? "easy" : undefined,
  });
  const [filters, setFilters] = useState(() => kept?.filters ?? fromSaved(saved));
  // The account can load after Browse opens: its saved filters apply until the player picks their own.
  const picked = useRef(Boolean(kept));
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

  // The platforms to ask the catalog for: "here" as catalogOs has it, which on Linux is every one (its apps and
  // Windows' run there), narrowed to what runs here once they come (`runs`).
  const askedOs = filters.platforms?.map((p) => (p === "here" ? catalogOs(config.os) : p));
  const platforms = askedOs?.some((p) => !p) ? undefined : [...new Set(askedOs as Os[])];
  function load(cursor?: string | null) {
    const request = ++latest.current;
    setLoading(true);
    setError(null);
    client
      // A search orders by relevance.
      .apps({ ...filters, platforms, search: query, sort: searching ? undefined : filters.sort, cursor })
      .then((page) => {
        if (request !== latest.current) return;
        remember(page.items);
        const mine = new Set(libraryNow.current.map((i) => i.id));
        const left = page.items.filter((e) => mine.has(e.id)).map((e) => e.id);
        // Drawn a little at a time, between frames: a page of cards at once held a scrolling d-pad up a moment on a
        // Steam Deck. Still loading till they're in, so the last page's Show more doesn't ask for them again.
        startTransition(() => {
          setOwned((o) => new Set([...(cursor ? o : []), ...left]));
          setPages((p) => (cursor ? [...p, page] : [page]));
          setLoading(false);
        });
      })
      .catch(() => {
        if (request !== latest.current) return;
        setError(t`Couldn't reach quiverlauncher.com. Check your connection and try again.`);
        setLoading(false);
      });
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
  const makers = Object.fromEntries(consoles.map((c) => [c.id, c.brand]));
  const [filtersOpen, setFiltersOpen] = useState(false);
  // Only what this computer runs: the platforms' default.
  const here = filters.platforms?.length === 1 && filters.platforms[0] === "here";
  // How many filters differ from the defaults: any platforms but what this computer runs count.
  const filtering = [filters.projectTypes?.length, filters.consoles?.length, filters.ai, filters.setup, !here].filter(Boolean).length;
  // An order the player picked (the sort, or a shelf's See all): the whole list in it, in place of the front page, as a
  // search or a filter has it; its pill goes back to the front page.
  const [sorted, setSorted] = useState(kept?.sorted ?? false);
  // What the player picked, for Browse to start from when they come back to it.
  useEffect(() => {
    if (picked.current) onKeep({ filters, sorted });
  }, [filters, sorted]); // eslint-disable-line react-hooks/exhaustive-deps
  const sortName = (sort: BrowseFilters["sort"]) =>
    ({ added: t`Recently added`, updated: t`Recently updated`, rating: t`Top rated`, name: t`Name A–Z` })[sort ?? "added"];
  // Apps in your library shown, a choice kept from before: on the Filters button's count, not a row of its own.
  const filterCount = filtering + (showOwned ? 1 : 0) + (hiddenView !== "hide" ? 1 : 0);
  // What each filter of several choices has picked, as its pill reads: unset while it's none.
  const consolesOn = pickedLabel("console", filters.consoles, consoleNames);
  const typesOn = pickedLabel("projectType", filters.projectTypes, consoleNames);
  /** A filter's pill: `on` names it while it differs from the default, and removing it goes back to the default. */
  const pill = (key: "ai" | "setup", on: string | undefined, select: ReactNode) =>
    !on && !filtersOpen ? null : (
      <FilterPill key={key} on={on} onClear={() => choose({ ...filters, [key]: undefined })}>
        {select}
      </FilterPill>
    );
  /** A filter of several choices, as the website's: removing it goes back to the default, `none` picked when it's unset. */
  const list = <K extends "platforms" | "consoles" | "projectTypes">(
    key: K,
    label: string,
    text: string,
    on: string | undefined,
    options: MultiOption[],
    { none, max }: { none?: BrowseFilters[K]; max?: number } = {},
  ) =>
    !on && !filtersOpen ? null : (
      <MultiPill
        key={key}
        label={label}
        text={text}
        on={on}
        onClear={() => choose({ ...filters, [key]: none })}
        options={options}
        picked={filters[key] ?? []}
        onChange={(picked) => choose({ ...filters, [key]: picked.length || none ? picked : undefined })}
        max={max}
      />
    );

  // Linux's own alone, beside what it runs: "Linux only".
  const osChoice = (os: Os) =>
    os === "linux" && config.os === "linux" ? t({ message: "Linux only", comment: "Browse's platform filter on Linux: its own apps, not Windows' too" }) : (OS_NAMES[os] ?? os);
  const name = OS_NAMES[config.os] ?? config.os;
  const hereLabel =
    config.os === "linux"
      ? t({ message: "Linux and Windows (this computer)", comment: "Browse's platform filter on Linux: the apps it runs, its own and Windows' through Proton" })
      : config.os === "android"
        ? t`${name} (this device)`
        : t`${name} (this computer)`;
  const platformsOn = filters.platforms?.length ? filters.platforms.map((p) => (p === "here" ? hereLabel : osChoice(p))).join(", ") : undefined;
  const inLibrary = new Set(library.map((i) => i.id));
  // What this computer runs, where the catalog was asked for more (Linux's and Windows' apps: catalogOs).
  const runs = (e: Entry) => !filters.platforms?.length || filters.platforms.some((p) => (p === "here" ? availableOn(e, config.os) : e.supportedOS.includes(p)));
  const matching = only ? (hiddenApps ?? []).filter((e) => hidden.has(e.id) && hiddenMatches(e, query, filters, makers)) : pages.flatMap((p) => p.items);
  const all = matching.filter(runs);
  const shown = (o: { showOwned?: boolean; view?: HiddenView }) => browseShown(all, { owned, showOwned, hidden, view: hiddenView, ...o });
  const entries = shown({});
  // What the notes under the list offer to show: apps in the library, and hidden ones.
  const left = showOwned ? 0 : shown({ showOwned: true }).length - entries.length;
  const hiddenLeft = hiddenView === "hide" ? shown({ view: "show" }).length - entries.length : 0;
  // Only hidden apps has those in the library whichever way this is, so it offers nothing there.
  const mine = showOwned && hiddenView !== "only" ? entries.filter((e) => inLibrary.has(e.id)).length : 0;
  const last = only ? undefined : pages.at(-1);
  const waiting = only ? !hiddenApps && !error : loading;
  const typed = search.trim();
  return (
    <>
      {/* The search in the row's middle, as large as the library's; sort, Filters and Submit an app at its end. */}
      <div className="browse-head">
      <div className="browse-bar">
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
          {/* How Browse's cards look, as the library's View menu sets its own; Settings has the same. */}
          <Popover label={t`View options`} icon={<LayoutGrid size={17} />} role="group" className="tool icon-only" panel="view-panel" align="end">
            {() => <BrowseCardOptions />}
          </Popover>
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
        {/* The search on a row of its own under the tools, centred and larger, as the library's. */}
        <div className="search-row">
          <label className="search browse-search">
            <Search size={17} />
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
                <X size={14} />
              </button>
            )}
          </label>
        </div>
      </div>
      </div>
      {/*
        Filters: pills to pick from while open; the ones that differ from the defaults stay, each removable, while it's
        closed, with a picked order's first. Apps in your library shown has a pill only beside others, or with it open.
      */}
      {(filtersOpen || filtering > 0 || hiddenView !== "hide" || (sorted && !searching)) && (
        <div id="browse-filters" className="filter-bar" role="group" aria-label={t`Catalog filters`}>
          <span className="filter-pills">
            {sorted && !searching && (
              <FilterPill on={sortName(filters.sort)} onClear={() => (setSorted(false), choose({ ...filters, sort: fromSaved(saved).sort }))}>
                <span className="pill-label">{sortName(filters.sort)}</span>
              </FilterPill>
            )}
            {/* What it runs ("here"): on Linux, Linux's apps and Windows' (through Proton), as the library shows them. None ticked is every platform. */}
            {list(
              "platforms",
              t`Platform`,
              here ? hereLabel : (platformsOn ?? t`All platforms`),
              here ? undefined : (platformsOn ?? t`All platforms`),
              [
                { value: "here", label: hereLabel },
                ...Object.keys(OS_NAMES)
                  .filter((id) => id !== config.os || config.os === "linux")
                  .map((id) => ({ value: id, label: osChoice(id as Os) })),
              ],
              { none: ["here"] },
            )}
            {list("consoles", t`Console`, consolesOn ?? t`Any console`, consolesOn, consoleOptions(consoles, consoleNames), { max: MAX_SYSTEMS })}
            {list("projectTypes", t`Project type`, typesOn ?? t`Any project type`, typesOn, projectTypeOptions())}
            {pill(
              "ai",
              filters.ai && filterLabel("ai", filters.ai, consoleNames),
              <select aria-label={t`AI use`} value={filters.ai ?? ""} onChange={set("ai")}>
                <option value="">{t`Any AI use`}</option>
                <option value="no-generated">{t`Hide mostly AI-generated apps`}</option>
                <option value="no-ai">{t`Hide apps with any AI use`}</option>
              </select>,
            )}
            {pill(
              "setup",
              filters.setup && t`Easy setup`,
              <select
                aria-label={t`Setup`}
                title={t`Easy setup: Quiver Launcher finds your copy of the game and puts it where the app needs it.`}
                value={filters.setup ?? ""}
                onChange={set("setup")}
              >
                <option value="">{t`Any setup`}</option>
                <option value="easy">{t`Easy setup only`}</option>
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
            {(filtersOpen || hiddenView !== "hide") && hidden.size > 0 && (
              <FilterPill
                on={hiddenView === "show" ? t`Hidden apps shown` : hiddenView === "only" ? t`Hidden apps only` : undefined}
                onClear={() => setHiddenView("hide")}
              >
                <select aria-label={t`Hidden apps`} value={hiddenView} onChange={(e) => setHiddenView(e.target.value as HiddenView)}>
                  <option value="hide">{t`Hide hidden apps`}</option>
                  <option value="show">{t`Show hidden apps`}</option>
                  <option value="only">{t`Only hidden apps`}</option>
                </select>
              </FilterPill>
            )}
            {hiddenView === "only" && hidden.size > 0 && (
              <button type="button" className="text-button" onClick={() => setSettings({ ...settings, browseHidden: undefined })}>
                <Plural value={hidden.size} one="Unhide # app" other="Unhide all # apps" />
              </button>
            )}
          </span>
          {filtering > 0 && (
            <span className="filter-actions">
              <button type="button" className="text-button" onClick={() => choose({ platforms: ["here"], sort: filters.sort })}>
                <Trans>Clear all</Trans>
              </button>
            </span>
          )}
        </div>
      )}
      {/* The front page, while the player isn't looking for something in particular. */}
      {!typed && !filtering && !sorted && !error && hiddenView === "hide" && (
        <Discover
          onOpen={onOpen}
          showOwned={showOwned}
          hidden={hidden}
          action={(entry) => <BrowseAction entry={entry} onOpen={onOpen} />}
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
          <div className={looks.grid}>
            {entries.map((entry) => (
              <EntryCard
                key={entry.id}
                entry={withShownTags(entry)}
                {...looks.card(entry)}
                // A download's progress, or what went wrong, shows over the art without hover.
                className={jobs[entry.id] ? "busy" : undefined}
                consoleNames={consoleNames}
                onOpen={() => onOpen(entry)}
                badge={hidden.has(entry.id) ? t`Hidden` : inLibrary.has(entry.id) ? t`In library` : undefined}
                action={<BrowseAction entry={entry} onOpen={onOpen} />}
              />
            ))}
          </div>
          {/* Or apps came, and none of them is for here, with no note below to say why: Only hidden apps, with none in these filters. */}
          {!waiting && (!all.length || (!entries.length && !left && !hiddenLeft && last?.isDone)) && (
            <p className="empty">
              <Trans>Nothing matches that search and those filters.</Trans>
            </p>
          )}
          {!waiting && left > 0 && (
            <p className={entries.length ? "owned-note" : "empty"}>
              <Plural value={left} one="# app here is already in your library." other="# apps here are already in your library." />{" "}
              <button type="button" className="link-button" onClick={() => setShowOwned(true)}>
                <Plural value={left} one="Show it" other="Show them" />
              </button>
            </p>
          )}
          {!waiting && hiddenLeft > 0 && (
            <p className={entries.length || left ? "owned-note" : "empty"}>
              <Plural value={hiddenLeft} one="# app here is hidden." other="# apps here are hidden." />{" "}
              <button type="button" className="link-button" onClick={() => setHiddenView("show")}>
                <Plural value={hiddenLeft} one="Show it" other="Show them" />
              </button>
            </p>
          )}
          {!waiting && mine > 0 && (
            <p className="owned-note">
              <Trans>Apps in your library are shown here too.</Trans>{" "}
              <button type="button" className="link-button" onClick={() => setShowOwned(false)}>
                <Trans>Hide them</Trans>
              </button>
            </p>
          )}
          {waiting ? (
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

/** A Browse card's button, and the menu its card opens (a right-click, a long press): its repository, README and feedback, and hiding it. */
function BrowseAction({ entry, onOpen }: { entry: Entry; onOpen: (e: Entry, at?: PageAt) => void }) {
  return (
    <>
      <Action entry={entry} />
      <BrowseMenu entry={entry} onOpen={() => onOpen(entry)} onReadme={() => onOpen(entry, { readme: true })} onFeedback={() => onOpen(entry, { feedback: true })} />
    </>
  );
}

/** The one button a card needs: Add, Install, progress, Play or Update. `apart`: the page says why a release was withdrawn itself. */
/** `reasonElsewhere`: a failure's reason is shown beside it (the Downloads page's row), so only its buttons here. */
function Action({ entry, apart, reasonElsewhere }: { entry: Entry; apart?: boolean; reasonElsewhere?: boolean }) {
  const { config, library, installs, jobs, catalog, get, play, dismiss, setUpdates, cancel, locateInstall, setLaunch, setNotice, addToLibrary, appMoves } = useLauncher();
  const job = jobs[entry.id];
  const install = installs[entry.id];
  // Its verified release was withdrawn too: Choose a version, in place of installing it.
  const [versions, setVersions] = useState(false);
  // Get's arrow: the version, and on Linux the build, to get, in place of Get's own choice.
  const [getting, setGetting] = useState(false);
  const noWayOff = Boolean(pulledVerified(catalog[entry.id] ?? entry));
  // On its page, Play says so when it opens the setup of the player's copy first (own-copy-setup.tsx's chip says which).
  const setsUp = apart && install && foldersOn(config.os) && copiesNeeded(catalog[entry.id] ?? entry, install, config.os).length > 0;
  const playLabel = setsUp ? (
    <>
      <Disc3 size={15} /> <Trans comment="Button that sets up the player's copy of the game, then starts the app">Set up and play</Trans>
    </>
  ) : (
    <>
      <Play size={15} /> <Trans comment="Button that starts an app">Play</Trans>
    </>
  );
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
        {!reasonElsewhere && (
          <p>
            <TriangleAlert size={15} aria-hidden="true" /> <span>{job.error}</span>
          </p>
        )}
        <div className="row">
          {/* Its file was gone or not the one Quiver checked: the newest other verified release, in its place. */}
          {instead && (
            <button className="primary" onClick={() => get(catalog[entry.id] ?? entry, instead)}>
              <Trans>Install {other} instead</Trans>
            </button>
          )}
          {/* Its folder is gone: moved, most likely, so where it went comes before downloading it again. */}
          {install && locatable(entry.id, install, job) && foldersOn(config.os) && (
            <button className="primary" onClick={() => void locateInstall(entry)}>
              <Trans>Locate existing install…</Trans>
            </button>
          )}
          {/* Nothing in its folder looks like its program: the player can say which it is. */}
          {install && noProgram(job) && (
            <button
              className="primary"
              onClick={() =>
                void native.pickExecutable(install.folder, install.dir).then(
                  (executable) => {
                    if (!executable) return;
                    setLaunch(entry.id, { ...install.launch, executable });
                    dismiss(entry.id);
                    void play(entry.id, executable);
                  },
                  (e) => setNotice(String(e), { stays: true }),
                )
              }
            >
              <Trans>Choose its executable…</Trans>
            </button>
          )}
          {/* A local app has nothing to download: Try again plays it again. */}
          <button onClick={() => (install?.local ? (dismiss(entry.id), void play(entry.id)) : get(entry))}>
            <Trans>Try again</Trans>
          </button>
          <button onClick={() => dismiss(entry.id)}>
            <Trans>Dismiss</Trans>
          </button>
          {/* On its page, one not installed can be found on this computer instead: after the buttons that retry. */}
          {!install && apart && locatable(entry.id, install, job) && foldersOn(config.os) && (
            <button onClick={() => void locateInstall(entry)}>
              <FolderSearch size={17} /> <Trans>Locate existing install…</Trans>
            </button>
          )}
        </div>
      </div>
    );
  }
  const moving = appMoves[entry.id];
  if (moving) return <MovingBar moving={moving} />;
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
    const next = versionLabel(updateFor(catalog[entry.id] ?? entry, install)?.version);
    return (
      <div className="row">
        <button className="primary" onClick={() => play(entry.id)}>
          {playLabel}
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
          {playLabel}
        </button>
        {install.unverified &&
          (install.blocked ? (
            <span className="blocked-mark" role="img" aria-label={t`Quiver blocked ${version}`} title={t`Quiver blocked ${version}: ${why}`}>
              <ShieldX size={14} />
            </span>
          ) : (
            <span className="unverified-mark" role="img" aria-label={t`${version} was installed before Quiver offered it`} title={t`${version} was installed before Quiver offered it`}>
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
        {playLabel}
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
  // Not in the library yet: Add puts it there without downloading it, and Install takes its place.
  if (!library.some((i) => i.id === entry.id))
    return (
      <button className="primary wide" key="add" onClick={() => addToLibrary(entry)}>
        <Plus size={15} aria-hidden="true" /> <Trans comment="Button that adds an app to the library without installing it">Add</Trans>
      </button>
    );
  if (noWayOff) return <div className="row">{choose}</div>;
  const name = entry.projectName;
  const builds = config.os === "linux" && entry.supportedOS.includes("linux") && entry.supportedOS.includes("windows");
  const getIt = (
    <button className="primary wide" onClick={() => get(entry)}>
      <Download size={15} /> <Trans comment="Button that installs an app">Install</Trans>
    </button>
  );
  // A phone has one build to get, and an app's versions are on its page: Install alone.
  if (config.os === "android") return getIt;
  // Its own key: as a download's row it was, React would keep Cancel's button as the arrow, the focus still on it.
  return (
    <div className="get-split" key="get">
      {getIt}
      <button
        className="primary get-more"
        title={builds ? t`Choose a version or platform` : t`Choose a version`}
        aria-label={builds ? t`Choose a version or platform of ${name}` : t`Choose a version of ${name}`}
        onClick={() => setGetting(true)}
      >
        <ChevronDown size={16} strokeWidth={2.5} aria-hidden="true" />
      </button>
      {getting && <VersionsDialog entry={catalog[entry.id] ?? entry} getting onClose={() => setGetting(false)} />}
    </div>
  );
}

/**
 * An app's own page, laid out like its page on quiverlauncher.com. Beside the
 * library's sidebar it has no way back, and shows the player's own play.
 */
/** Each tab of an app's page's icon: outlined and grey, filled in the accent while it's open (app.css). */
const TAB_ICONS = { overview: Info, media: CirclePlay, releases: CircleArrowDown, feedback: Smile, discussions: MessagesSquare, computer: SlidersHorizontal, saves: Save };
/** A tab of an app's page: which, its name, and a count beside it. */
type PageTab = readonly [keyof typeof TAB_ICONS, string, string | undefined];

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
  /** Opened at its Releases, with that version's notes open, at its feedback, or at its README, rather than at the top of its Overview. */
  at?: PageAt;
  /**
   * Over TV mode: a larger header with its art, read from across a room, and only the buttons for playing it there
   * (its folder and uninstalling are on its Manage tab); Back (B) goes home; the README waits to be asked for.
   */
  tv?: boolean;
  onClose?: () => void;
  onOpenGame: (game: GameLink) => void;
  /** Opens another app's page, from More like this. */
  onOpenApp: (entry: Entry, at?: PageAt) => void;
  onSignIn: () => void;
}) {
  const { config, library, installs, jobs, catalog, remove, confirmUninstall, askRename, askName, managing, askManage, consoleNames, setUpdates, declineUpdate, get, switched, switchToCatalog, locateInstall, askPlace, appMoves, settings } = useLauncher();
  // An app the player added and switched to the catalog's shows the catalog's.
  const id = switched[opened.id] ?? opened.id;
  const item = library.find((i) => i.id === id);
  // The player's own name and artwork, live as they change them.
  const entry = withOverrides(catalog[id] ?? opened, item?.overrides);
  const switchable = useListed(entry).filter((app) => !library.some((i) => i.id === app.id));
  const install = installs[entry.id];
  const job = jobs[entry.id];
  // Apps the player added have no page on the site: no releases or feedback there.
  const site = !isOwn(entry.id);
  const { source, detail } = useAppDetail(entry);
  const releases = useReleases(entry, site);
  const { own, refresh } = useOwnFeedback(entry, site);
  const { user } = useAccount();
  const [tab, setTab] = useState<"overview" | "media" | "releases" | "feedback" | "discussions" | "computer" | "saves">(
    at && "releases" in at ? "releases" : at && "feedback" in at ? "feedback" : at && "discussions" in at ? "discussions" : "overview",
  );
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
  // Asked for from its menu (All options…): its Manage tab, once.
  useEffect(() => {
    if (managing !== id) return;
    if (item || install) setTab("computer");
    askManage(null);
  }, [managing]); // eslint-disable-line react-hooks/exhaustive-deps
  const readmeAsked = !!at && "readme" in at;
  const [readme, setReadme] = useState(readmeAsked);
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
  const threads = useDiscussionCount(entry.id, site);
  const checking = checkingOf(detail, releases);
  // Only when the app has videos, as on the website.
  const media = detail?.media ?? [];
  const overviewTab: PageTab = ["overview", t({ message: "Overview", comment: "Tab of an app's page" }), undefined];
  const computerTab: PageTab = ["computer", t({ message: "Manage", comment: "Tab of an app's page: its shortcuts, setup and place in the library" }), undefined];
  const savesTab: PageTab[] = install && savesOn(config.os) ? [["saves", t({ message: "Saves", comment: "Tab of an installed app's page: its save backups" }), undefined]] : [];
  // One the player added has no site page, so only its README and what this computer has of it, each a tab as a
  // catalog app's are; a local app has no README, so no tabs.
  const ownTabs: PageTab[] = [...(isLocal(entry.id) ? [] : [overviewTab]), ...(item || install ? [computerTab] : []), ...savesTab];
  const siteTabs: PageTab[] = [
    overviewTab,
    ...(media.length ? [["media", t({ message: "Media", comment: "Tab of an app's page, with its videos" }), String(media.length)] as const] : []),
    ["releases", t({ message: "Releases", comment: "Tab of an app's page" }), history.items ? `${history.items.length}${history.more ? "+" : ""}` : undefined],
    ["feedback", t`Player feedback`, String(said)],
    ["discussions", t({ message: "Discussions", comment: "Tab on an app's page: players' threads about it" }), threads === undefined ? undefined : threads >= 100 ? "100+" : String(threads)],
    ...(item || install ? [computerTab] : []),
    ...savesTab,
  ];
  const tabs = site ? siteTabs : ownTabs.length > 1 ? ownTabs : [];
  const hero = entry.libraryArt?.hero || entry.libraryArt?.header;
  const [problem, setProblem] = useState<string | null>(null);
  // The installed release was withdrawn: why, over the way off it.
  const pulled = install && catalog[entry.id]?.withdrawn?.find((w) => w.version === install.version);
  const eyebrow = [...new Set((entry.consoles ?? []).map((c) => consoleNames[c] ?? c.toUpperCase()))].join(", ");
  // Put in the sentences below by name.
  const verifiedVersion = versionLabel(entry.verified?.version);
  // The release this computer could update to, which may not be the verified one: that may be for another system.
  const known = catalog[entry.id] ?? entry;
  const update = updateFor(known, install);
  const updateVersion = versionLabel(update?.version);
  // Waiting 48 hours for updates: the newest release, which the player can take early for this app.
  // Not installed: the same release, to install in the offered one's place.
  const early = jobs[entry.id] ? undefined : (earlyOffer(known, install) ?? (canGet(known, config.os) ? earlyGetOffer(known, install) : undefined));
  const earlyVersion = versionLabel(early?.version);
  const installedVersion = versionLabel(install?.version);
  const pulledVersion = versionLabel(pulled ? pulled.version : undefined);
  const blockedWhy = install?.blocked ?? "";
  const app = entry.projectName;
  // What this computer has of the app, and its setup: a tab of its own, or the page itself for a local app.
  const computer = (item || install) && (
    <section className="app-panel computer-panel" aria-label={t({ message: "Manage", comment: "Tab of an app's page: its shortcuts, setup and place in the library" })}>
      {/* A tab says it already. */}
      {!tabs.length && (
        <h3>
          <Trans comment="Tab of an app's page: its shortcuts, setup and place in the library">Manage</Trans>
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
        {filesAddable(install) && foldersOn(config.os) && !(job && !("error" in job)) && <AddFilesButton entry={entry} />}
        {install?.installer && !job && <ReinstallButton id={entry.id} />}
        {renamable(install) && foldersOn(config.os) && !(job && !("error" in job)) && (
          <button className="panel-action opens" onClick={() => askRename(entry)}>
            <FolderPen size={16} aria-hidden="true" /> <Trans>Rename folder</Trans>
          </button>
        )}
        {/* Its own folder somewhere else, as its menu's Move to… and Install to… do. */}
        {foldersOn(config.os) && !isLocal(entry.id) && !(job && !("error" in job)) &&
          (appMoves[entry.id] ? (
            <button className="panel-action" disabled>
              <FolderInput size={16} aria-hidden="true" /> <Trans>Moving…</Trans>
            </button>
          ) : movable(install) ? (
            <button className="panel-action opens" onClick={() => askPlace(entry)}>
              <FolderInput size={16} aria-hidden="true" /> <Trans>Move to…</Trans>
            </button>
          ) : (
            !install &&
            canGet(known, config.os) &&
            !pulledVerified(known) && (
              <button className="panel-action opens" onClick={() => askPlace(entry)}>
                <FolderInput size={16} aria-hidden="true" /> <Trans>Install to…</Trans>
              </button>
            )
          ))}
        {item && !isLocal(item.id) && <Versions entry={entry} source={source} />}
        {locatable(entry.id, install, job) && foldersOn(config.os) && (
          <button className="panel-action opens" onClick={() => void locateInstall(entry)}>
            <FolderSearch size={16} aria-hidden="true" /> <Trans>Locate existing install…</Trans>
          </button>
        )}
        {item && <UpdatesChoice id={item.id} />}
        {item && <PrereleasesChoice id={item.id} />}
        <CacheChoice entry={entry} />
      </div>
      {item && (
        <div className="panel-group">
          <h4>
            <Trans>Library</Trans>
          </h4>
          <Customize id={item.id} />
          {isOwn(item.id) && <ChangeGame id={item.id} />}
          {isLocal(item.id) && <LinkToCatalog id={item.id} />}
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
              {/* In the library: a double click on its name, or the pencil beside it, renames it. */}
              <div className="app-title">
                <h1 onDoubleClick={item && !tv ? () => askName(item.id) : undefined}>{entry.projectName}</h1>
                {item && !tv && (
                  <button type="button" className="title-rename" aria-label={t`Rename ${app}`} title={t({ message: "Rename", comment: "Button beside an app's name on its page: gives it a name of the player's own" })} onClick={() => askName(item.id)}>
                    <Pencil size={15} aria-hidden="true" />
                  </button>
                )}
                {/* Signed in as one who looks after the catalog: its entry on the website's admin pages, to change it there. */}
                {site && !tv && managesCatalog(user) && (
                  <button
                    type="button"
                    className="title-rename title-admin"
                    aria-label={t({ message: "Edit in admin", comment: "Button beside an app's name on its page, only for catalog maintainers and admins: opens the app's entry on the website's admin pages" })}
                    title={t`Open ${app} in the catalog's admin pages on quiverlauncher.com`}
                    onClick={() => void native.openUrl(`${SITE}/admin/entries/${encodeURIComponent(entry.id)}/overview`)}
                  >
                    <Wrench size={15} aria-hidden="true" />
                  </button>
                )}
              </div>
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
                  {/* The other apps that play its game, best first: which is best is easy to miss on the game's page alone. */}
                  {site && <WaysToPlay entry={entry} onOpenApp={onOpenApp} onOpenGame={onOpenGame} />}
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
                        ? verifiedVersion
                        : t`${verifiedVersion} · downloads can't be checked`}
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
                    <ReleaseBadge state="unverified" title={t`Installed before Quiver offered it`} />
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
              {/* Already on this computer: an app needn't be in the library, or downloaded, for this. */}
              {!install && locatable(entry.id, install, job) && foldersOn(config.os) && !(job && "error" in job) && (
                <button onClick={() => void locateInstall(entry)}>
                  <FolderSearch size={17} /> <Trans>Locate existing install…</Trans>
                </button>
              )}
              {install && foldersOn(config.os) && !tv && (
                <button onClick={() => (setProblem(null), native.openFolder(entry.id).catch((e) => setProblem(String(e))))}>
                  <FolderOpen size={17} /> <Trans>Open folder</Trans>
                </button>
              )}
              {/* Out of Browse, as its card's menu has it. */}
              {site && !item && <BrowseHideButton entry={entry} />}
              {/* Its files go; the app stays in the library to get again. Taking it out is under Manage. */}
              {install && !install.local && !(job && !("error" in job)) && !tv && (
                <button className="danger" onClick={() => confirmUninstall({ entry })}>
                  <Trash2 size={17} /> <Trans>Uninstall</Trans>
                </button>
              )}
              {/* Something on the page wrong or missing: to the maintainers. */}
              {site && <ReportProblem entry={entry} installed={install && !install.local ? install.version : undefined} onSignIn={onSignIn} />}
              {install && foldersOn(config.os) && !(job && !("error" in job)) && <OwnCopyChips entry={entry} install={install} />}
            </div>
            {/* When it was added to the library is the sidebar's to say; here, what playing it has come to. */}
            {!onClose && <PlayStats install={install} item={item} added={false} />}
            {problem && (
              <p className="job-error" role="status">
                {problem}
              </p>
            )}
            {switchable.length > 0 && (
              <p className="release-pinned-line">
                <ShieldCheck size={14} aria-hidden="true" />
                <span>
                  {install ? (
                    <Trans>Quiver&apos;s catalog lists this app now, and checks its releases. Switching keeps its files, play time and your settings.</Trans>
                  ) : (
                    <Trans>Quiver&apos;s catalog lists this app now, and checks its releases.</Trans>
                  )}
                </span>
                {switchable.length === 1 ? (
                  <button type="button" className="link-button" onClick={() => switchToCatalog(entry.id, switchable[0])}>
                    <Trans>Switch to the catalog&apos;s</Trans>
                  </button>
                ) : (
                  // One repository's several apps: the player says which this is.
                  switchable.map((app) => {
                    const name = app.projectName;
                    return (
                      <button type="button" key={app.id} className="link-button" onClick={() => switchToCatalog(entry.id, app)}>
                        <Trans>Switch to {name}</Trans>
                      </button>
                    );
                  })
                )}
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
                    <Trans>Install {verifiedVersion} instead</Trans>
                  </button>
                )}
              </p>
            )}
            {install?.differs && (
              <p className="release-pinned-line differs" role="alert">
                <ShieldAlert size={14} aria-hidden="true" />
                <span>
                  <Trans>The {installedVersion} Quiver offers isn&apos;t the file you installed.</Trans>
                </span>
                <button type="button" className="link-button" onClick={() => get(entry)}>
                  <Trans>Reinstall {installedVersion}</Trans>
                </button>
              </p>
            )}
            {install && !install.local && install.updates === "pinned" && (
              <p className="release-pinned-line">
                <Pin size={14} aria-hidden="true" />
                <span>
                  {/* Quietly: no badge or count, but a pin never hides that there's more. */}
                  {update ? (
                    <Trans>Pinned to {installedVersion}. {updateVersion} is available.</Trans>
                  ) : (
                    <Trans>Pinned to {installedVersion}.</Trans>
                  )}
                </span>
                <button type="button" className="link-button" onClick={() => setUpdates(entry.id, undefined)}>
                  <Trans>Unpin</Trans>
                </button>
              </p>
            )}
            {update && hasUpdate(known, install) && (
              <p className="release-pinned-line">
                <span>
                  <Trans>{updateVersion} is available.</Trans>
                </span>
                <button type="button" className="link-button" onClick={() => declineUpdate(entry.id, update.version)}>
                  <Trans>Skip this update</Trans>
                </button>
              </p>
            )}
            {early && (
              <p className="release-pinned-line">
                <span>
                  {stillWaiting(early.releasedAt) ? (
                    <Trans>{earlyVersion} is less than 48 hours old, so Quiver Launcher isn't offering it yet.</Trans>
                  ) : (
                    <Trans>{earlyVersion} is out, but Quiver Launcher isn't offering it yet.</Trans>
                  )}
                </span>
                <button type="button" className="link-button" onClick={() => void get(known, undefined, { early: true })}>
                  {install ? <Trans>Update early…</Trans> : <Trans>Install latest…</Trans>}
                </button>
              </p>
            )}
            {update && install?.updates !== "pinned" && declinedUpdate(known, install) && (
              <p className="release-pinned-line">
                <span>
                  <Trans>You skipped {updateVersion}. Quiver Launcher offers the next new release.</Trans>
                </span>
                <button type="button" className="link-button" onClick={() => get(known)}>
                  <Trans>Update to {updateVersion}</Trans>
                </button>
              </p>
            )}
            {install && !install.local && updatesOf(settings, install) === "auto" && skipped(known, install) && (
              <p className="release-pinned-line">
                <span>
                  <Trans>Auto Update won&apos;t put {updateVersion} back. It installs the next new release.</Trans>
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
        {tabs.length > 0 && (
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
        <div ref={content} className="app-content" role={tabs.length ? "tabpanel" : undefined}>
          {tab === "overview" && (
            <>
              {/* Short, general facts first, over the README, which can run long. */}
              {site && <ProjectDetails entry={entry} detail={detail} source={source} />}
              {/* Under its facts, as on the website: something there wrong or missing, for maintainers to fix. */}
              {site && <CorrectionStatusPanel entry={entry} />}
              {/* In TV mode, long and full of links: there when asked for. */}
              {tv && !readme ? (
                <button type="button" className="panel-action opens tv-readme" onClick={() => setReadme(true)}>
                  <BookOpen size={18} aria-hidden="true" /> <Trans>Read the README</Trans>
                </button>
              ) : (
                <Readme entry={entry} source={source} asked={readmeAsked} />
              )}
            </>
          )}
          {tab === "media" && <MediaTab videos={media} slug={entry.slug} />}
          {tab === "releases" && <ReleasesTab entry={entry} detail={detail} history={history} checking={checking} openVersion={at && "releases" in at ? at.releases : undefined} />}
          {(tabs.length ? tab === "computer" : true) && computer}
          {tab === "saves" && savesTab.length > 0 && <SavesTab entry={entry} />}
          {tab === "discussions" && <DiscussionsTab entryId={entry.id} />}
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
      {site && <MoreLikeThis entry={entry} onOpen={onOpenApp} action={(e) => <BrowseAction entry={e} onOpen={onOpenApp} />} />}
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
type BrowseFilters = Omit<AppQuery, "search" | "cursor" | "limit" | "projectType" | "os" | "console" | "platforms"> & { platforms?: (Os | "here")[] };
/** The most consoles and makers the catalog takes at once (the site's catalogFilters.ts). */
const MAX_SYSTEMS = 8;
/** Browse's filters as the player left them, and whether they picked its sort. */
type KeptBrowse = { filters: BrowseFilters; sorted: boolean };

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
 * How an installed app updates on this computer, whatever the player's default (Settings, Updates). A local app has
 * no releases to update from; a Windows installer's setup waits for the player, so it never updates by itself.
 */
function UpdatesChoice({ id }: { id: string }) {
  const { installs, settings, setUpdates } = useLauncher();
  const install = installs[id];
  if (!install || install.local) return null;
  const auto = updatesByItself(install);
  const installedVersion = versionLabel(install.version);
  const byDefault =
    settings.autoUpdates && auto
      ? t({ message: "Use my default (install automatically)", comment: "How an app updates: whatever Settings, Updates says, which is to install them automatically" })
      : t({ message: "Use my default (offer them)", comment: "How an app updates: whatever Settings, Updates says, which is to offer new releases to install" });
  return (
    <label className="panel-field">
      <span className="field-label">
        <Trans comment="How an installed app gets new releases">Updates</Trans>
      </span>
      <select
        value={install.updates === "auto" && !auto ? "ask" : (install.updates ?? "default")}
        onChange={(e) => setUpdates(id, e.target.value === "default" ? undefined : (e.target.value as "auto" | "ask" | "pinned"))}
      >
        <option value="default">{byDefault}</option>
        <option value="ask">{t({ message: "Offer them", comment: "Updates: offer new releases to install" })}</option>
        {auto && <option value="auto">{t`Install automatically`}</option>}
        <option value="pinned">{t`Always stay on ${installedVersion}`}</option>
      </select>
    </label>
  );
}

/**
 * Whether an installed app gets pre-releases, whatever the player's default (Settings, Updates). A version it's set to
 * stay on wins over either.
 */
function PrereleasesChoice({ id }: { id: string }) {
  const { installs, settings, setPrereleases } = useLauncher();
  const install = installs[id];
  if (!install || install.local || isCustom(id)) return null;
  const byDefault = settings.prereleases
    ? t({ message: "Use my default (include pre-releases)", comment: "Which releases an app gets: whatever Settings, Updates says, which is to include pre-releases" })
    : t({ message: "Use my default (main releases only)", comment: "Which releases an app gets: whatever Settings, Updates says, which is main releases only" });
  return (
    <label className="panel-field">
      <span className="field-label">
        <Trans comment="Which of an app's releases it gets: main releases only, or pre-releases too">Releases</Trans>
      </span>
      <select value={install.prereleases ?? "default"} onChange={(e) => setPrereleases(id, e.target.value === "default" ? undefined : (e.target.value as "include" | "main"))}>
        <option value="default">{byDefault}</option>
        <option value="main">{t({ message: "Main releases only", comment: "Which releases an app gets: only the ones its developer didn't mark as pre-releases" })}</option>
        <option value="include">{t({ message: "Include pre-releases", comment: "Which releases an app gets: pre-releases too" })}</option>
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

/** Whether Browse shows the app (browse-hidden.ts), beside its Get on its page. */
function BrowseHideButton({ entry }: { entry: Entry }) {
  const { settings, setSettings } = useLauncher();
  const hidden = settings.browseHidden?.some((h) => h.id === entry.id);
  return (
    <button onClick={() => setSettings({ ...settings, browseHidden: toggleHidden(settings.browseHidden, entry) })}>
      {hidden ? <Eye size={17} /> : <EyeOff size={17} />} {hidden ? t`Show in Browse` : t`Hide from Browse`}
    </button>
  );
}

/** Overrides the catalog's name and artwork for this player; empty shows the catalog's again. */
function Customize({ id }: { id: string }) {
  const { config, library, catalog, customize } = useLauncher();
  const current = library.find((i) => i.id === id)?.overrides ?? {};
  const [open, setOpen] = useState(false);
  const [name, setName] = useState(current.name ?? "");
  const [art, setArt] = useState(artValues(current));
  if (!open)
    return (
      <button className="panel-action opens" onClick={() => setOpen(true)}>
        <Pencil size={16} aria-hidden="true" /> <Trans>Change name or artwork</Trans>
      </button>
    );
  // Closing without saving puts back what's saved.
  const close = () => (setName(current.name ?? ""), setArt(artValues(current)), setOpen(false));
  const bad = !artValuesOk(art);
  return (
    <form
      className="panel-form customize"
      aria-label={t`Name and artwork`}
      onSubmit={(e) => {
        e.preventDefault();
        if (bad) return;
        customize(id, { name: name.trim() || undefined, ...fromArtValues(art) });
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
      {/* Android's file picker gives no path to copy a picture from. */}
      <ArtFields values={art} catalog={catalog[id]} canPick={foldersOn(config.os)} onChange={setArt} />
      {bad && (
        <p className="job-error">
          <Trans>Use an https:// image address.</Trans>
        </p>
      )}
      <button type="button" className="link-button" onClick={() => (setName(""), setArt(artValues({})))}>
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

/** How long a notice that needs no answer shows, in ms. */
const NOTICE_SHOWS = 8000;

/**
 * What just happened, over whatever is showing, in every mode and at any scroll: not a banner, which a player scrolled
 * down the page or in TV mode never saw. One that must be read (something failed) takes the focus until OK; another
 * shows for a moment without taking it from the player, gone sooner with a click.
 */
function Notice() {
  const { notice, setNotice } = useLauncher();
  useEffect(() => {
    if (!notice || notice.stays) return;
    const timer = setTimeout(() => setNotice(null), NOTICE_SHOWS);
    return () => clearTimeout(timer);
  }, [notice]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!notice) return null;
  const close = () => setNotice(null);
  if (!notice.stays) {
    return (
      <div className={`toast${notice.action ? " has-action" : ""}`} role="status" onClick={close}>
        <p>{notice.text}</p>
        {/* Its click closes the toast too. */}
        {notice.action && (
          <button type="button" className="toast-action" onClick={notice.action.act}>
            {notice.action.label}
          </button>
        )}
      </div>
    );
  }
  return (
    <Dialog label={t({ message: "Notice", comment: "Name, for a screen reader, of the dialog saying something failed or must be read" })} onClose={close}>
      <div className="detail-body notice-prompt">
        <p>{notice.text}</p>
        <div className="row">
          <button className="primary" autoFocus onClick={close}>
            <Trans>OK</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}

/** Play, for an app whose saves changed both here and in the account since they last matched: which the player keeps. */
/**
 * The account had no room for a backup: said once, with the two ways to make some. The backup is on this computer
 * either way, and is sent by itself once there's room.
 */
function SavesFullPrompt({ onFreeUp }: { onFreeUp: () => void }) {
  const { savesFull: id, closeSavesFull: close, catalog, installs, cloudCatchUp, setNotice } = useLauncher();
  const account = useAccount();
  const api = account.cloudSaves;
  const [usage, setUsage] = useState<CloudUsage | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    setUsage(null);
    if (id && api) api.usage().then(setUsage, () => {});
  }, [id, Boolean(api)]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!id || !api) return null;
  const name = catalog[id]?.projectName ?? "";
  const used = usage ? sizeLabel(usage.used) : "";
  const limit = usage ? sizeLabel(usage.limit) : "";
  // The app keeping the most, when it's installed here: its switch is this computer's to set.
  const [largest] = usage?.apps ?? [];
  const largestInstall = largest && installs[largest.entryId];
  const largestName = largest?.name ?? "";
  const largestSize = largest ? sizeLabel(largest.size) : "";
  const count = largest?.copies.length ?? 0;
  const turnOff = () => {
    if (!largest || !largestInstall) return;
    setBusy(true);
    native
      .savesCloudOff(savesOf(largest.entryId, largestInstall, catalog[largest.entryId]), true)
      .then(() => api.remove({ entryId: largest.entryId }))
      .then(
        () => {
          savesChanged(largest.entryId);
          cloudCatchUp();
          setNotice(t`Cloud saves are off for ${largestName}, and its copies were removed from your account.`);
        },
        () => setNotice(t`Couldn't reach your cloud saves. Check your connection and try again.`, { stays: true }),
      )
      .finally(() => (setBusy(false), close()));
  };
  return (
    <Dialog label={t`Your cloud saves are full`} onClose={close}>
      <div className="detail-body program-prompt saves-full">
        <h2>
          <Cloud size={18} aria-hidden="true" /> <Trans>Your cloud saves are full</Trans>
        </h2>
        <p className="muted">
          {usage ? t`${used} of ${limit} used.` : null} {t`The newest backup of ${name} is on this computer only, and so is each one after it until there's room. Quiver sends it by itself then.`}
        </p>
        <div className="choices">
          <button autoFocus disabled={busy} onClick={() => (close(), onFreeUp())}>
            <span>
              <strong>
                <Trans>Free up space</Trans>
              </strong>
              <small>
                <Trans>See what each app keeps in your account, and remove copies you don&apos;t need.</Trans>
              </small>
            </span>
          </button>
          {largest && largestInstall && (
            <button disabled={busy} onClick={turnOff}>
              <span>
                <strong>{t`Turn off cloud saves for ${largestName}`}</strong>
                <small>
                  {plural(count, {
                    one: `It keeps the most. Its # copy (${largestSize}) is removed from your account. Its saves and backups on this computer stay.`,
                    other: `It keeps the most. Its # copies (${largestSize}) are removed from your account. Its saves and backups on this computer stay.`,
                  })}
                </small>
              </span>
            </button>
          )}
        </div>
        <div className="row">
          <button disabled={busy} onClick={close}>
            <Trans>Not now</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}

function SavesConflictPrompt() {
  const { savesConflict: conflict, catalog, installs, jobs, get } = useLauncher();
  if (!conflict) return null;
  // Saves from a newer version than the one installed: updating first is the way that can't go wrong, when there's an update.
  const latest = catalog[conflict.entry.id];
  const update = conflict.newer && latest && !jobs[conflict.entry.id] ? updateFor(latest, installs[conflict.entry.id]) : undefined;
  const updateTo = versionLabel(update?.version);
  const madeWith = versionLabel(conflict.newer?.cloud);
  const have = versionLabel(conflict.newer?.here);
  const name = conflict.entry.projectName;
  const when = (at: number) => new Date(at).toLocaleString(i18n.locale, { dateStyle: "medium", timeStyle: "short" });
  const { cloud, here } = conflict;
  // The other saves are the account's, or the ones in the player's sync folder (sync-folder.ts).
  const folder = conflict.from === "folder";
  const device = cloud.device;
  const cloudWhen = when(cloud.madeAt);
  const hereWhen = here ? when(here.at) : "";
  return (
    <Dialog label={t`Which saves of ${name}?`} onClose={() => conflict.answer(null)}>
      <div className="detail-body program-prompt saves-conflict">
        <h2>
          {folder ? <FolderSync size={18} aria-hidden="true" /> : <Cloud size={18} aria-hidden="true" />} {t`Which saves of ${name} do you want to keep?`}
        </h2>
        {conflict.places?.length ? (
          <>
            <p className="muted">
              {folder ? (
                <Trans>The saves in your sync folder would go in a folder Quiver hasn&apos;t kept this app&apos;s saves in on this computer. Only use them if this is where the app saves:</Trans>
              ) : (
                <Trans>Your cloud saves would go in a folder Quiver hasn&apos;t kept this app&apos;s saves in on this computer. Only use them if this is where the app saves:</Trans>
              )}
            </p>
            <SavePlaces places={conflict.places} />
          </>
        ) : conflict.newer ? (
          <p className="muted">
            {folder
              ? update
                ? t`The saves in your sync folder were made with ${madeWith} of this app, and this computer has ${have}. An older version may not be able to load them. Update first, then play.`
                : t`The saves in your sync folder were made with ${madeWith} of this app, and this computer has ${have}. An older version may not be able to load them. The ones you don't pick are kept as a backup on the app's Saves tab.`
              : update
                ? t`Your cloud saves were made with ${madeWith} of this app, and this computer has ${have}. An older version may not be able to load them. Update first, then play.`
                : t`Your cloud saves were made with ${madeWith} of this app, and this computer has ${have}. An older version may not be able to load them. The ones you don't pick are kept as a backup on the app's Saves tab.`}
          </p>
        ) : (
          <p className="muted">
            {t`${name} was played on this computer and on another one since your saves last synced, so there are two different sets of saves now. Choose which to continue from. The other set is kept as a backup on the app's Saves tab.`}
          </p>
        )}
        <div className="choices">
          {update && latest && (
            <button
              autoFocus
              className="saves-update"
              onClick={() => {
                // Nothing starts and the saves stay as they are: after the update, Play brings the other saves in.
                conflict.answer(null);
                void get(latest);
              }}
            >
              <span>
                <strong>{t`Update to ${updateTo}`}</strong>
                <small>
                  {folder ? <Trans>Then press Play, and the saves in your sync folder are put in place.</Trans> : <Trans>Then press Play, and your cloud saves are put in place.</Trans>}
                </small>
              </span>
            </button>
          )}
          <button autoFocus={!update} onClick={() => conflict.answer("cloud")}>
            <span>
              <strong>
                {folder ? (
                  conflict.newer ? (
                    <Trans>Use the saves in my sync folder anyway</Trans>
                  ) : (
                    <Trans>Use the saves in my sync folder</Trans>
                  )
                ) : conflict.newer ? (
                  <Trans>Use my cloud saves anyway</Trans>
                ) : (
                  <Trans>Use my cloud saves</Trans>
                )}
              </strong>
              <small>{conflict.newer ? t`From ${device}, ${cloudWhen}. The saves here are backed up first.` : t`From ${device}, ${cloudWhen}`}</small>
            </span>
          </button>
          <button onClick={() => conflict.answer("here")}>
            <span>
              <strong>
                <Trans>Play with the saves on this computer</Trans>
              </strong>
              <small>
                {folder
                  ? conflict.places?.length
                    ? t`The saves in your sync folder stay there as a backup.`
                    : here
                      ? t`Last backed up ${hereWhen}. The saves in your sync folder stay there as a backup. These become your newest.`
                      : t`The saves in your sync folder stay there as a backup. These become your newest.`
                  : conflict.places?.length
                    ? t`Your cloud saves stay in your account as a backup.`
                    : here
                      ? t`Last backed up ${hereWhen}. Your cloud saves stay in your account as a backup. These become your newest.`
                      : t`Your cloud saves stay in your account as a backup. These become your newest.`}
              </small>
            </span>
          </button>
        </div>
        <div className="row">
          <button onClick={() => conflict.answer(null)}>
            <Trans>Don&apos;t play yet</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}

/** The folders a backup would be put in, as they are on this computer. */
function SavePlaces({ places }: { places: { template: string; path: string }[] }) {
  return (
    <ul className="save-places">
      {places.map((place) => (
        <li key={place.template}>{place.path || place.template}</li>
      ))}
    </ul>
  );
}

/** Restore, for a backup whose saves were in a folder this computer hasn't kept that app's saves in: the player sees the folder and says whether to go ahead. */
function SavesPlacesPrompt() {
  const { savesPlaces: asked } = useLauncher();
  if (!asked) return null;
  const name = asked.name;
  return (
    <Dialog label={t`Restore to this folder?`} onClose={() => asked.answer(false)}>
      <div className="detail-body program-prompt saves-conflict">
        <h2>
          <Cloud size={18} aria-hidden="true" /> {t`Restore ${name}'s saves to this folder?`}
        </h2>
        <p className="muted">
          <Trans>This backup&apos;s saves were in a folder Quiver hasn&apos;t kept this app&apos;s saves in on this computer. Only restore it if this is where the app saves:</Trans>
        </p>
        <SavePlaces places={asked.places} />
        <div className="row">
          <button onClick={() => asked.answer(false)}>
            <Trans>Cancel</Trans>
          </button>
          <button className="primary" onClick={() => asked.answer(true)}>
            <Trans>Restore</Trans>
          </button>
        </div>
      </div>
    </Dialog>
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
  const { confirming, confirmUninstall, uninstall, remove, library, installs } = useLauncher();
  const [portable, setPortable] = useState(false);
  const going = confirming ? installs[confirming.entry.id] : undefined;
  // Saves kept in its folder leave with it, which a player wouldn't otherwise expect of an uninstall.
  useEffect(() => {
    setPortable(false);
    if (!going) return;
    let current = true;
    native.appFiles(going.folder, going.dir, [PORTABLE_FILE]).then(
      (found) => current && setPortable(Boolean(found?.length)),
      () => {},
    );
    return () => void (current = false);
  }, [confirming]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!confirming) return null;
  const { entry, then } = confirming;
  const name = entry.projectName;
  // Out of the library too: said only when it's in there.
  const leaves = confirming.remove && library.some((i) => i.id === entry.id);
  const close = () => confirmUninstall(null);
  const executable = going?.launch?.executable && outsideFolder(going.launch.executable, going.program) ? going.launch.executable : undefined;
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
        {portable && (
          <p>
            <Trans>This app may keep your saves in its folder. They&apos;ll go to the trash too.</Trans>
          </p>
        )}
        {/* What it starts is somewhere else (Launch options): only its folder goes, so the player knows that stays. */}
        {executable && (
          <p>
            <Trans>
              It starts <code>{executable}</code>. That file is outside the app&apos;s folder, so it won&apos;t be deleted. You can delete it yourself if you
              don&apos;t need it.
            </Trans>
          </p>
        )}
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

/** An app's versions, from its menu's Change version. */
function ChangeVersion() {
  const { changingVersion, changeVersion, catalog } = useLauncher();
  if (!changingVersion) return null;
  return <VersionsDialog entry={catalog[changingVersion.id] ?? changingVersion} onClose={() => changeVersion(null)} />;
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
  const install = renaming ? installs[renaming.id] : undefined;
  const folder = install && folderNameOf(install);
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

/**
 * The player's own name for an app in their library, in place of the catalog's: from its menu's Rename…, or its
 * name on its page. Empty, or the catalog's own, shows the catalog's again. Its artwork stays as it is.
 */
function RenamePrompt() {
  const { naming, askName, library, catalog, customize } = useLauncher();
  const item = naming ? library.find((i) => i.id === naming) : undefined;
  const [name, setName] = useState("");
  const original = naming ? catalog[naming]?.projectName : undefined;
  const now = item?.overrides?.name ?? original ?? "";
  useEffect(() => {
    // Its name now fills the field as it opens, selected to type over.
    setName(now);
  }, [naming]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!naming || !item) return null;
  const app = now;
  const close = () => askName(null);
  const save = (to: string) => {
    const next = to.trim();
    customize(naming, { ...item.overrides, name: next && next !== original ? next : undefined });
    close();
  };
  return (
    <Dialog label={t`Rename ${app}`} onClose={close}>
      <form className="detail-body rename-folder" onSubmit={(e) => (e.preventDefault(), save(name))}>
        <h2>
          <Pencil size={18} /> <Trans>Rename {app}</Trans>
        </h2>
        <input autoFocus aria-label={t`Name`} maxLength={200} placeholder={original} value={name} onChange={(e) => setName(e.target.value)} onFocus={(e) => e.currentTarget.select()} />
        <p className="muted">
          <Trans>Only your library shows this name. Its folder keeps the name it has.</Trans>
        </p>
        <div className="row">
          <button className="primary">
            <Trans>Save</Trans>
          </button>
          <button type="button" onClick={close}>
            <Trans>Cancel</Trans>
          </button>
          {original && item.overrides?.name && (
            <button type="button" className="link-button" onClick={() => save("")}>
              <Trans>Use the catalog&apos;s name</Trans>
            </button>
          )}
        </div>
      </form>
    </Dialog>
  );
}

/** How far an app's move to another folder has got: a sweep until it's known to be copying, then its share copied. */
function MovingBar({ moving }: { moving: AppMove }) {
  const percent = moving.size ? Math.round((moving.copied / moving.size) * 100) : null;
  return (
    <div className={percent === null ? "progress waiting" : "progress"} aria-label={t`Moving`}>
      <div className="bar" style={{ width: `${percent ?? 100}%` }} />
      <span>{percent === null ? t`Moving…` : t`Moving ${percent}%`}</span>
    </div>
  );
}

/**
 * Where an app goes, from its menu's Install to… or Move to…: one of the apps folders, or a folder the player
 * chooses. Moving is the apps folder's safe move, for this one app, and its bar shows here and on the app's page.
 */
function PlaceAppPrompt() {
  const { placing: asked, askPlace, installs, catalog, config, appsFolders, appMoves, get, moveApp, setNotice } = useLauncher();
  const placing = asked?.entry;
  const [to, setTo] = useState<string | null>(null);
  const [chosen, setChosen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const install = placing ? installs[placing.id] : undefined;
  const here = install && (install.dir ? rootOf(install) : config.appsDir);
  const folders = appsFolders().map((f) => f.path);
  useEffect(() => {
    setChosen(null);
    setError(null);
    // The first folder it isn't in already.
    setTo(folders.find((f) => !here || !samePath(f, here)) ?? null);
  }, [asked]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!placing) return null;
  const entry = catalog[placing.id] ?? placing;
  const app = entry.projectName;
  // The version it was going to install, when one was chosen (Choose a version…): said, so it's clear which goes.
  const version = asked?.release ? versionLabel(asked.release.version) : null;
  const moving = appMoves[placing.id];
  const close = () => askPlace(null);
  // The apps folders, and where it is now and the folder chosen, when neither is one.
  const options = [...folders, ...[here, chosen].filter((p): p is string => Boolean(p))].filter((p, i, all) => all.findIndex((q) => samePath(p, q)) === i);
  const choose = async () => {
    const picked = await native.pickFolder(install ? t`Choose where to move ${app}` : t`Choose where to install ${app}`).catch(() => null);
    if (picked) {
      setChosen(picked);
      setTo(picked);
      setError(null);
    }
  };
  const go = () => {
    if (!to) return;
    setError(null);
    if (!install) {
      close();
      void get(entry, asked?.release, { ...asked?.options, to });
      return;
    }
    moveApp(placing.id, to).then(
      ({ left }) => {
        close();
        setNotice(left ? t`${app} is moved. Its old folder couldn't all be deleted: ${left}` : t`${app} is moved.`, left ? { stays: true } : undefined);
      },
      (e) => setError(e instanceof Error ? e.message : String(e)),
    );
  };
  return (
    <Dialog label={install ? t`Move ${app}` : version ? t`Install ${app} ${version} to` : t`Install ${app} to`} onClose={close}>
      <div className="detail-body place-app">
        <h2>
          <FolderOpen size={18} /> {install ? <Trans>Move {app}</Trans> : version ? <Trans>Install {app} {version} to</Trans> : <Trans>Install {app} to</Trans>}
        </h2>
        <div className="move-choice" role="radiogroup" aria-label={t`Folder`}>
          {options.map((path) => {
            const current = Boolean(here && samePath(path, here));
            return (
              <label key={path}>
                <input type="radio" name="place-app" checked={Boolean(to && samePath(to, path))} disabled={current || Boolean(moving)} onChange={() => setTo(path)} />
                <span className="place-app-folder">
                  <span className="apps-folder-name">{path}</span>
                  {samePath(path, config.appsDir) && (
                    <span className="apps-folder-badge">
                      <Trans comment="The apps folder new apps install in">Default</Trans>
                    </span>
                  )}
                  {current && (
                    <span className="muted">
                      {" "}
                      <Trans>It's here now.</Trans>
                    </span>
                  )}
                </span>
              </label>
            );
          })}
        </div>
        <div className="row place-app-choose">
          <button type="button" disabled={Boolean(moving)} onClick={() => void choose()}>
            <Trans>Choose a folder…</Trans>
          </button>
        </div>
        <p className="muted">
          {install ? (
            <Trans>It gets a folder of its own in there, with its saves and settings. Desktop and Steam shortcuts you made before point at the old place, so make them again.</Trans>
          ) : (
            <Trans>It gets a folder of its own in there, and updates keep installing into it.</Trans>
          )}
        </p>
        {moving && <MovingBar moving={moving} />}
        {error && (
          <p className="job-error" role="status">
            {error}
          </p>
        )}
        <div className="row">
          <button className="primary" disabled={!to || Boolean(moving)} onClick={go}>
            {install ? <Trans comment="Moves an app's folder">Move</Trans> : <Trans comment="Installs an app">Install</Trans>}
          </button>
          <button type="button" onClick={close}>
            {moving ? <Trans>Close</Trans> : <Trans>Cancel</Trans>}
          </button>
        </div>
      </div>
    </Dialog>
  );
}

/** Add files to folder (app-files.tsx), from an app's menu. */
function AddFilesPrompt() {
  const { addingFiles, askAddFiles } = useLauncher();
  if (!addingFiles) return null;
  const close = () => askAddFiles(null);
  return (
    <Dialog label={t`Add files to folder`} onClose={close}>
      <AddFilesDialogBody key={addingFiles.id} entry={addingFiles} onClose={close} />
    </Dialog>
  );
}

function ChooseFile() {
  const { choice } = useLauncher();
  if (!choice) return null;
  // Its own state for each choice, so Show all files starts closed every time.
  return <ChooseFileDialog key={`${choice.entry.id}@${choice.version}`} choice={choice} />;
}

function ChooseFileDialog({ choice }: { choice: Choice }) {
  // Open from the start when nothing else is offered: then the files a maintainer didn't tick are all there is.
  const [showAll, setShowAll] = useState(choice.assets.length === 0);
  const unapproved = new Set(choice.unapproved);
  const { config } = useLauncher();
  // A phone or tablet is a device, not a computer.
  const device = config.os === "android";
  const name = choice.entry.projectName;
  const version = versionLabel(choice.version);
  // The release's other files this computer can install, whatever system the site says they're for.
  const offered = new Set(choice.assets.map((a) => a.id));
  const rest = choice.all.filter((a) => !offered.has(a.id));
  // What the site says each is for, with its name under it.
  const labelled = (a: Asset) => (
    <span>
      <strong>
        {a.os === "unknown" ? t`Platform unspecified` : (OS_NAMES[a.os] ?? a.os)}
        {a.architecture !== "unknown" ? ` · ${a.architecture}` : ""}
        {unapproved.has(a.id)
          ? ` · ${
              // Its scan isn't done, which says more than "Not approved": most files the site leaves out are waiting on theirs.
              a.scan?.verdict === "pending"
                ? t({ message: "Virus scan not finished", comment: "A release file Quiver doesn't offer while its virus scan runs, in Which download?'s Show all files" })
                : t({ message: "Not approved", comment: "A release file a maintainer didn't tick for the app, in Which download?'s Show all files" })
            }`
          : ""}
      </strong>
      <small>{a.filename}</small>
    </span>
  );
  return (
    <div className="overlay">
      <section className="detail choose" role="dialog" aria-label={t`Choose a download`}>
        <div className="detail-body">
          <h2>
            <Trans>Which download?</Trans>
          </h2>
          <p className="muted">
            {choice.unmatched
              ? device
                ? t`None of ${name} ${version}'s files are marked for your device. If you know which one runs on it, pick it.`
                : t`None of ${name} ${version}'s files are marked for your computer. If you know which one runs on it, pick it.`
              : device
                ? t`${name} ${version} has more than one file for your device.`
                : t`${name} ${version} has more than one file for your computer.`}
          </p>
          {choice.assets.length > 0 && (
            <div className="choices">
              {choice.assets.map((a) => (
                <button key={a.id} onClick={() => choice.resolve(a)}>
                  {choice.unmatched ? labelled(a) : a.filename}
                </button>
              ))}
            </div>
          )}
          {rest.length > 0 &&
            (showAll ? (
              <>
                <p className="muted">
                  {choice.assets.length > 0 ? (
                    device ? (
                      <Trans>The release's other files. They may not run on your device.</Trans>
                    ) : (
                      <Trans>The release's other files. They may not run on your computer.</Trans>
                    )
                  ) : device ? (
                    <Trans>The release's files Quiver doesn't offer. They may not run on your device.</Trans>
                  ) : (
                    <Trans>The release's files Quiver doesn't offer. They may not run on your computer.</Trans>
                  )}
                </p>
                <div className="choices">
                  {rest.map((a) => (
                    <button key={a.id} onClick={() => choice.resolve(a)}>
                      {labelled(a)}
                    </button>
                  ))}
                </div>
              </>
            ) : (
              <button type="button" className="show-all" aria-expanded={false} onClick={() => setShowAll(true)}>
                <Trans>Show all files</Trans>
              </button>
            ))}
          {showAll && choice.unapproved.length > 0 && <SuggestPlatform choice={choice} files={[...choice.assets, ...rest]} />}
          <button onClick={() => choice.resolve(null)}>
            <Trans>Cancel</Trans>
          </button>
        </div>
      </section>
    </div>
  );
}

/**
 * Under Which download?'s files Quiver doesn't offer: tells the maintainers, in a click, which file is the build for
 * which system, so they can tick it and set its platform. Sent as a correction to the app's downloads, as Report a
 * problem sends one; signed in only, as that is.
 */
function SuggestPlatform({ choice, files }: { choice: Choice; files: Asset[] }) {
  const { config, installs } = useLauncher();
  const { user, suggestCorrection } = useAccount();
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState(choice.unapproved[0]);
  const [os, setOs] = useState<string>(config.os in OS_NAMES ? config.os : "windows");
  const [state, setState] = useState<"busy" | "sent" | { error: string }>();
  if (!user)
    return (
      <p className="muted">
        <Trans>Sign in to tell the maintainers which file is for which system.</Trans>
      </p>
    );
  if (state === "sent")
    return (
      <p className="correction-sent" role="status">
        <CircleCheck size={14} aria-hidden="true" /> <Trans>Sent. A maintainer will take a look.</Trans>
      </p>
    );
  if (!open)
    return (
      <button type="button" className="show-all" onClick={() => setOpen(true)}>
        <Trans>Tell the maintainers which file is for which system</Trans>
      </button>
    );
  const send = () => {
    const asset = files.find((a) => a.id === file);
    if (!asset) return;
    setState("busy");
    // For the maintainers, who read corrections in English.
    const system = OS_NAMES[os] ?? os;
    suggestCorrection({
      entryId: choice.entry.id,
      topic: "downloads",
      body: `${asset.filename} in ${versionLabel(choice.version)} is the ${system} build${choice.unapproved.includes(asset.id) ? ", but it isn't offered" : `, but it's marked for ${asset.os === "unknown" ? "no platform" : (OS_NAMES[asset.os] ?? asset.os)}`}. Please tick it and set its platform to ${system}. (Sent from Which download? in Quiver Launcher.)`,
      client: correctionClient(config, installs[choice.entry.id]?.version),
    }).then(
      () => (track("correction_sent", { topic: "downloads", from: "file_choice" }), setState("sent")),
      (error) => setState({ error: error instanceof Error ? error.message : String(error) }),
    );
  };
  return (
    <div className="suggest-platform" role="group" aria-labelledby="suggest-platform-title">
      <h3 id="suggest-platform-title">
        <Trans>Submit a correction for a release file's platform</Trans>
      </h3>
      <label className="field">
        <Trans>File</Trans>
        <select value={file} onChange={(e) => setFile(e.target.value)}>
          {files.map((a) => (
            <option key={a.id} value={a.id}>
              {a.filename}
            </option>
          ))}
        </select>
      </label>
      <label className="field">
        <Trans comment="Which download?: the system a release file is the build for, to tell the maintainers">Is for</Trans>
        <select value={os} onChange={(e) => setOs(e.target.value)}>
          {Object.entries(OS_NAMES).map(([id, name]) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
      </label>
      <button type="button" disabled={state === "busy"} onClick={send}>
        <Trans comment="Sends the maintainers which system a release file is for">Send</Trans>
      </button>
      {typeof state === "object" && (
        <p className="job-error" role="status">
          {state.error}
        </p>
      )}
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
  const { asking, choice, installs } = useLauncher();
  // After a file choice already open for another app (Update all), not on top of it.
  if (!asking || choice) return null;
  const { entry, file, reasons, scan, checkEndsAt, early, resolve } = asking;
  // Put in the sentences below by name.
  const release = versionLabel(asking.version);
  const app = entry.projectName;
  const name = file?.name ?? "";
  const unchecked = file?.why === "unchecked";
  // A file a maintainer didn't tick, picked from Show all files.
  const unapproved = file?.why === "unapproved";
  const waits = checkEndsAt !== undefined;
  const hours = waits ? hoursFrom(checkEndsAt) : 0;
  // Newest right away, where only its scan makes it ask: that's the question, not that it isn't offered.
  const scanned = !file && early === "scan";
  const title = unapproved
    ? t`Install a file Quiver doesn't offer?`
    : unchecked
      ? t`Install a file Quiver didn't check?`
      : early === "asked" && waits
        ? installs[asking.entry.id]
          ? t`Update to ${release} before its 48 hours are up?`
          : t`Install ${release} before its 48 hours are up?`
        : scanned
          ? t`Install ${release} with a virus scan warning?`
          : t`Install ${release} before Quiver offers it?`;
  const scanLine = scan && (
    <p className="scan-line">
      VirusTotal: {scanText(scan)}{" "}
      {scan.url && (
        <a href={scan.url} target="_blank" rel="noreferrer">
          <Trans>See the report</Trans>
        </a>
      )}
    </p>
  );
  return (
    // Each question its own: the next one in turn starts afresh, its first button focused.
    <Dialog key={`${entry.id}@${release}:${name}`} label={unchecked || unapproved || scanned ? title : t`Install a release before Quiver offers it`} onClose={() => resolve(false)}>
      <div className="detail-body unverified-prompt">
        <h2>
          <ShieldAlert size={18} /> {title}
        </h2>
        <p>
          {!file
            ? early === "asked" && waits
              ? // They chose to wait for updates: what that wait is for, and that this skips it.
                t`${release} is less than 48 hours old. You've chosen to wait 48 hours for updates, so that if a release turns out to be malicious, Quiver has time to withdraw it before it reaches you. Installing it now skips that wait.`
              : scanned
                ? t`Apps are set to update to the newest release right away, and ${release} is the newest. Quiver Launcher is asking in this case because of its virus scan.`
                : `${early ? "" : `${t`Quiver doesn't offer a release of ${app} yet.`} `}${t`Quiver Launcher isn't offering this release as an update yet.`}`
            : unapproved
              ? scan?.verdict === "pending"
                ? t`Quiver doesn't offer ${name} for ${app}, and its virus scan hasn't finished.`
                : t`Quiver doesn't offer ${name} for ${app}: it may be for another system, or missed when the downloads were picked.`
              : unchecked
                ? t`Quiver checked ${release}'s other files, not ${name}. It downloads as the developer published it.`
                : t`${name} changed after ${release} finished its 48-hour wait, and the new file hasn't been out for 48 hours yet. It installs the file Quiver saw, and refuses one that changed since.`}
        </p>
        {/* Asked because of its scan: the scan first, as what the sentence above leads to. */}
        {scanned && scanLine}
        {reasons.length > 0 && (
          <ul className="unverified-reasons">
            {reasons.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        )}
        {!scanned && scanLine}
        {waits && (
          <p className="wait-line">
            <ShieldCheck size={14} aria-hidden="true" />{" "}
            {plural(hours, { one: "If you wait, its 48 hours are up in about # hour.", other: "If you wait, its 48 hours are up in about # hours." })}
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

/** The time, as a console's home screen shows it. */
function Clock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(timer);
  }, []);
  return <span className="tv-clock">{now.toLocaleTimeString(i18n.locale, { hour: "numeric", minute: "2-digit" })}</span>;
}
