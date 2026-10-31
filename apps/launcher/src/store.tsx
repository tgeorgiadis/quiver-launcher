/**
 * The player's library and what's installed, kept on this device.
 *
 * A library item only points at its catalog entry (plus the player's own
 * overrides); names, artwork and files always come from the catalog. The
 * last catalog data seen is cached so the library shows instantly offline.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { msg, plural, t } from "@lingui/core/macro";
import { ApiError, createClient, createGithub, createGitlab, githubRepository, parseRepository, type Asset, type Client, type Console, type Entry, type Github, type Os, type HistoryRelease, type Release, type ReleaseScan, type UnverifiedRelease, type Withdrawn } from "@quiverlauncher/api";
import { native, type Config, type LaunchOptions, type OldApp, type Progress, type StateProblem, type V3Library } from "./native";
import { Recovery } from "./recovery";
import { assetFilterFor, bestAssets, installable, installFormat, isMsi, pickedBefore } from "./assets";
import { fromSite, installedFile, insteadOf, newestUnblocked, sameChecksum, unverifiedFile, versionLabel, withdrawnAsBlocked } from "./versions";
import { Problem, reasonOf, setTelemetryAppsDir, track } from "./telemetry";
import { AccountProvider, useAccount } from "./account";
import { applyServer, applyServerCollections, changesFrom, hasFilters, joinAccount, type Collection } from "./sync";
import { setBindings, type Bindings } from "./spatial";
import { bestGameArt, customEntry, customKey, folderNameFor, freeFolder, isCustom, isLocal, localEntry, localKey, type CustomApp, type GameArt, type LocalApp } from "./custom";
import { followTheme, type Theme } from "./theme";
import type { LibraryView } from "./view";
import { activate, pick, type Language } from "@quiverlauncher/i18n";

/** The language the launcher shows in: the one picked in Settings, or else the system's nearest. */
export const languageOf = (settings: Settings): Language => settings.language ?? pick(navigator.languages);

export type { CustomApp, Collection, GameArt, LocalApp };

export type LibraryItem = {
  /** The catalog entry's id, "github:owner/repo" or "gitlab:…" for a custom app, "local:…" for a local one. */
  id: string;
  slug: string;
  /** A repository the catalog doesn't list. */
  custom?: CustomApp;
  /** On this computer only: a program already here, or a folder the player fills. Never synced. */
  local?: LocalApp;
  /** A game's artwork and consoles the player matched to an app they added; this device only. */
  art?: GameArt;
  addedAt: number;
  /** The player's own choices; empty shows the catalog's. */
  overrides?: { name?: string; cover?: string; tags?: string[]; consoles?: string[] };
  /** Changed here and not yet saved to the account. */
  pending?: { added?: boolean; name?: boolean; cover?: boolean; tags?: boolean; consoles?: boolean; removed?: boolean };
  /** The account this item last synced with. */
  account?: string;
  /** Removed here; kept until the removal reaches its account. */
  removed?: boolean;
};
/** Catalog data for a library app, with releases pulled since. */
export type CatalogEntry = Entry & { withdrawn?: Withdrawn[] };
export type Install = {
  /** Set for an install adopted from Quiver Launcher 3, which stays where it was. */
  dir?: string;
  version: string; releasedAt?: number; releaseId?: string; folder: string; executables: string[]; wine?: boolean;
  lastPlayed?: number;
  /** When it was installed here, not updated: a game's card plays the port got last until another is played (group.ts). */
  installedAt?: number;
  /** How long the player has played it, in seconds: each session the launcher started, until the app closed. */
  playTime?: number;
  /** Install new releases without asking, or stay on this one (only ever the player's own choice). Unset: offer them. */
  updates?: "auto" | "pinned";
  /** A verified version Auto Update leaves alone: the one the player went back from by picking an older release. */
  skip?: string;
  /** Installed before Quiver verified it; cleared once the site verifies this version with the same file. */
  unverified?: true;
  /** Installed though Quiver blocked it, the player having insisted: why it was blocked. Cleared as `unverified` is. */
  blocked?: string;
  /** The verified release of this version isn't the file that was installed. */
  differs?: true;
  /** The installed file's checksum (`sha256:<hex>`), when it had one. */
  checksum?: string;
  /** The player's own files (a local app): never downloaded, updated or deleted. */
  local?: true;
  /** A program the player picked, started exactly where it is; for a Windows installer, the program it installed. */
  program?: string;
  /** Installed by its Windows installer (MSI), wherever its setup put it: Windows updates and uninstalls it, never on its own. */
  installer?: "msi";
  /** The player's own way of starting it; kept through updates. */
  launch?: LaunchOptions;
  /** The file the player picked among several for this computer, as a filter (`assetFilterFor`): updates take it again. */
  pick?: string;
};
/** A repository the catalog doesn't list, before it's added: its latest release and the files for this computer. */
export type RepositoryPreview = { app: CustomApp; latest?: Release; files: Asset[] };
/**
 * What an app is doing, or what went wrong: `download` when getting or updating it failed, not starting or deleting it;
 * `instead`, a release to install in place of one whose file was gone or wasn't the file Quiver checked.
 */
export type Job = Progress | { error: string; download?: true; instead?: Release };
/** A download that finished since the launcher started: Downloads lists it under Recently finished. */
export type Finished = { id: string; version: string; update: boolean; at: number };
/** Several files suit this computer: the player picks one. */
/** `unmatched`: none of the files is marked for this computer, so these are all the release's. */
export type Choice = { entry: Entry; version: string; assets: Asset[]; unmatched?: boolean; resolve: (asset: Asset | null) => void };
/** Deleting an app's files, waiting on the player's yes: `remove` takes it out of the library too, and `then` runs after. */
export type Confirming = { entry: Entry; remove?: boolean; then?: () => void };
/**
 * What Quiver hasn't verified, waiting on the player's yes before it installs (`resolve(true)`): the newest release of
 * an app with no verified one, or a file of a verified release that `changed` upstream since or that Quiver has no
 * checksum for (`unchecked`).
 */
export type Asking = {
  entry: Entry;
  version: string;
  file?: { name: string; why: "changed" | "unchecked" };
  /** Why the release isn't verified, worded for players. */
  reasons: string[];
  scan?: ReleaseScan;
  /** When it's verified by itself, if nothing changes. */
  checkEndsAt?: number;
  resolve: (install: boolean) => void;
};
/** How an install goes, as `get` takes it; `app`, a custom app's repository not in the library yet; `auto`, Auto Update's. */
type InstallOptions = { app?: CustomApp; pin?: boolean; unverified?: boolean; blocked?: string; auto?: boolean };

type Launcher = {
  config: Config;
  client: Client;
  github: Github;
  /** The player's GitHub token (GITHUB_TOKEN); saving one keeps it in the keychain, none removes it. */
  githubToken?: string;
  saveGithubToken: (token: string | undefined) => Promise<void>;
  gitlab: ReturnType<typeof createGitlab>;
  /** The catalog's consoles, and their names by id. */
  consoles: Console[];
  consoleNames: Record<string, string>;
  library: LibraryItem[];
  installs: Record<string, Install>;
  catalog: Record<string, CatalogEntry>;
  jobs: Record<string, Job>;
  /** Downloads finished since the launcher started, newest first; not kept when it closes. */
  finished: Finished[];
  clearFinished: () => void;
  choice: Choice | null;
  /** What Quiver hasn't verified that an install asks the player about, one at a time. */
  asking: Asking | null;
  confirming: Confirming | null;
  /** Asks the player before uninstalling (or uninstalling and removing) an app; null closes the prompt. */
  confirmUninstall: (confirming: Confirming | null) => void;
  /** The app whose Play asks which of the player's programs to start; null closes the prompt. */
  choosingProgram: string | null;
  chooseProgram: (id: string | null) => void;
  /** The Quiver Launcher 3 library the launcher found by itself, until a library is brought over; undefined while it looks. */
  oldLibrary: V3Library | null | undefined;
  /** Imports these Quiver Launcher 3 apps, saying how many are done as it goes; resolves to the names of those it couldn't. */
  importOld: (apps: OldApp[], onProgress?: (done: number) => void) => Promise<string[]>;
  /** The Quiver Launcher 3 library in a folder the player picked, its installs in `apps` if given: null when there isn't one. */
  findOld: (path: string, apps?: string) => Promise<V3Library | null>;
  remember: (entries: Entry[]) => void;
  /** Keeps catalog data for apps that aren't in the library, such as those on a copy of a shared playlist. */
  cache: (entries: Entry[]) => void;
  /**
   * Adds the app to the library and installs it: its newest release, or the one given; `pin` then keeps it on that one.
   * With no verified release, its newest that isn't blocked, once the player confirms.
   * `unverified`: a release Quiver hasn't verified, which the player confirmed.
   * `blocked`: why Quiver blocked it, for one the player insisted on.
   */
  get: (entry: Entry, release?: Release, options?: { pin?: boolean; unverified?: boolean; blocked?: string }) => Promise<void>;
  /** Starts an app: `executable`, else its one program the player picked, else asks which of them, else the launcher's own choice. */
  play: (id: string, executable?: string) => Promise<void>;
  /** Runs a Windows installer's setup again, for the version installed. */
  reinstall: (id: string) => Promise<void>;
  /** Stops a download; whatever was installed before stays as it was. */
  cancel: (id: string) => void;
  /** Uninstalls the app and takes it out of the library. */
  remove: (id: string) => Promise<void>;
  /** Deletes the app's files (to the trash), keeping it in the library to get again. */
  uninstall: (id: string) => Promise<void>;
  /** A GitHub or GitLab repository: the catalog's app when it lists it, else what adding it would install. */
  lookUpRepository: (input: string) => Promise<{ error: string } | { listed: Entry } | { preview: RepositoryPreview }>;
  /** The catalog's app for an app the player added from its repository, once the catalog lists it; else null. */
  listedAs: (id: string) => Promise<Entry | null>;
  /**
   * Moves an app the player added from its repository onto the catalog's app
   * for it: same files and folder, play time, launch options and the
   * player's name, artwork and tags; its updates then come from the catalog.
   */
  switchToCatalog: (id: string, entry: Entry) => void;
  /** Apps switched to the catalog's since the launcher started: the old key to the new. */
  switched: Record<string, string>;
  /** Adds a repository the catalog doesn't list and installs it. */
  addCustomApp: (app: CustomApp, latest: Release | undefined, art?: GameArt) => Entry;
  /** Adds a program on this computer, or makes a folder in the apps folder to fill; this device only. */
  addLocalApp: (app: { kind: "program"; path: string; name: string } | { kind: "folder"; name: string }, art?: GameArt) => Promise<Entry>;
  /** Adds an app without installing it, such as one installed but not in the library. */
  add: (entry: Entry) => void;
  setUpdates: (id: string, updates: Install["updates"]) => void;
  /** Sets how an installed app starts; nothing set goes back to the launcher's choice. */
  setLaunch: (id: string, launch: LaunchOptions) => void;
  /** Sets the player's own name and artwork; undefined shows the catalog's. */
  customize: (id: string, overrides: { name?: string; cover?: string }) => void;
  /** The player's own tags on an app; hand-picked collections are tags. */
  setTags: (id: string, tags: string[]) => void;
  /** The consoles the player says an app is for, by id; undefined, or the catalog's own, shows the catalog's again. */
  setConsoles: (id: string, consoles?: string[]) => void;
  /** Saved library filters, in order. */
  collections: Collection[];
  /** Adds or changes a collection; `removed` deletes it. */
  saveCollection: (collection: Collection) => void;
  /** Changes made here that the account doesn't have yet. */
  unsynced: number;
  /** Signs out, taking the library along except the apps installed on this computer, which stay until another account signs in. */
  signOut: () => Promise<void>;
  settings: Settings;
  setSettings: (settings: Settings) => void;
  /** A short note for the player, such as what signing in brought over. */
  notice: string | null;
  setNotice: (notice: string | null) => void;
  dismiss: (id: string) => void;
  /** The folders of installed apps that live in the apps folder, which moving it takes along. */
  appFolders: () => string[];
  /** The app whose folder the player is renaming; null closes the prompt. */
  renaming: Entry | null;
  askRename: (entry: Entry | null) => void;
  /** Renames an installed app's folder in the apps folder, as a folder can be named; resolves to the name it got. */
  renameFolder: (id: string, name: string) => Promise<string>;
  /** Moves the installed apps to another folder, where apps install from then on. */
  moveApps: (to: string) => Promise<void>;
};

const LauncherContext = createContext<Launcher | null>(null);

export const useLauncher = () => useContext(LauncherContext)!;

const LauncherNow = createContext<{ readonly current: Launcher } | null>(null);
/**
 * The launcher as it is when a button is pressed, without drawing again with each change to it as useLauncher does:
 * for a card kept as it is while its own app doesn't change (library-tile.tsx), in a library of hundreds.
 */
export const useLauncherNow = () => useContext(LauncherNow)!;

/** How long looking up a repository may take before it gives up. */
const LOOKUP_TIMEOUT = 30_000;
/** The release a player gets: the newest that isn't a pre-release, else the newest. */
const latestOf = (releases: Release[]) => releases.find((r) => !r.prerelease) ?? releases[0];
/** A custom app's repository from its key, for one not in the library; GitHub and GitLab don't mind its lower case. */
const fromKey = (id: string): CustomApp => {
  const repository = id.slice(id.indexOf(":") + 1);
  return { provider: id.startsWith("gitlab:") ? "gitlab" : "github", repository, name: repository.split("/").at(-1)! };
};

/**
 * Whether the player can rename an app's folder: one Quiver keeps in the apps
 * folder. Not a 3.x install or a program left where it is, and not a Windows
 * installer's, whose files are wherever its setup put them.
 */
export const renamable = (install: Install | undefined) => Boolean(install && !install.dir && install.installer !== "msi");

/** Whether this computer can install the app. */
export const availableOn = (entry: Entry, os: string) =>
  entry.supportedOS.some((o) => o === os || o === "unknown" || (os === "linux" && o === "windows"));

/**
 * The platform to ask the catalog for, for the apps this computer can run: its own, but on Linux none, as its apps and
 * Windows' both run there (through Proton or Wine) and the catalog takes one platform; what comes is narrowed to
 * `availableOn` instead. Linux asked for its own showed a Steam Deck none of Windows' apps.
 */
export const catalogOs = (os: Os) => (os === "linux" ? undefined : os);

/**
 * Whether Get can install the app on this computer: from its verified release, or with none (a nightly whose builds
 * never wait out their 48 hours, say) from its newest release upstream, after a warning.
 */
export const canGet = (entry: Entry, os: string) => availableOn(entry, os) && Boolean(entry.verified || entry.lastReleaseVersion);

/** "v1.2" and "1.2" are the same release. */
const bare = (version: string) => version.trim().replace(/^v/i, "");

/** Auto Update leaves this verified release alone: the player went back from it. */
export const skipped = (entry: Entry | undefined, install: Install | undefined) =>
  Boolean(install?.skip && entry?.verified && bare(install.skip) === bare(entry.verified.version));

/**
 * The site's verified release when it's one that was withdrawn since: the site
 * should have moved verified back to an earlier release, and until it has,
 * nothing installs this one without the player choosing it and insisting.
 */
export const pulledVerified = (entry: CatalogEntry | undefined) => {
  const verified = entry?.verified?.version;
  return verified ? entry.withdrawn?.find((w) => bare(w.version) === bare(verified)) : undefined;
};

export function hasUpdate(entry: CatalogEntry | undefined, install: Install | undefined) {
  // Only newer releases: a pulled one rolls back through the withdrawn warning instead, and a withdrawn one is never an update.
  return Boolean(
    entry?.verified &&
      install &&
      !pulledVerified(entry) &&
      install.updates !== "pinned" &&
      bare(install.version) !== bare(entry.verified.version) &&
      entry.verified.releasedAt > (install.releasedAt ?? 0),
  );
}

/**
 * The install a Quiver Launcher 3 app comes over with, staying where 3.x put
 * it; none when it wasn't installed. A version 3.x didn't know comes as 3.x's
 * own 0.0.0 (v3.rs), with no release date, so any verified release is an update.
 */
export function adoptedInstall(old: OldApp, entry: Entry, os: Os): Install | undefined {
  if (!old.dir || !old.version) return undefined;
  const executables = entry.launcher.preferredExecutables?.[os as "windows" | "linux" | "macos"] ?? [];
  // One 3.x installed with its Windows installer starts the program it linked.
  const msi = old.program ? { installer: "msi" as const, program: old.program } : {};
  return { dir: old.dir, version: old.version, folder: entry.launcher.folderName || entry.slug, executables: old.program ? [] : executables, ...msi };
}

/** `work` on each item, `at` at a time, its results in the items' order; `each` after each one. */
async function inTurns<T, R>(items: T[], at: number, work: (item: T) => Promise<R>, each?: () => void): Promise<R[]> {
  const results: R[] = new Array(items.length);
  let next = 0;
  const worker = async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await work(items[i]);
      each?.();
    }
  };
  await Promise.all(Array.from({ length: Math.min(at, items.length) }, worker));
  return results;
}

/** Whether TV mode (tv.tsx) shows: turned on, or left to itself in Steam's Game Mode. */
export const tvModeOn = (settings: Pick<Settings, "tvMode">, config: Pick<Config, "gameMode">) =>
  settings.tvMode === "on" || (settings.tvMode !== "off" && Boolean(config.gameMode));

/** This computer's preferences; they don't sync. */
export type Settings = {
  /** An app's page's Project details folded away, for every app. */
  projectDetailsClosed?: boolean;
  /** TV mode (tv.tsx): on, off, or unset, on by itself in Steam's Game Mode. */
  tvMode?: "on" | "off";
  fullscreen?: boolean;
  /** The system's title bar over the window; unset, the window's buttons are in the top bar instead. */
  systemTitleBar?: boolean;
  scale?: number;
  /** Light or dark; unset follows the system. */
  theme?: Theme;
  /** The language Quiver shows in (@quiverlauncher/i18n); unset follows the system. */
  language?: Language;
  /** Apps hidden from the library here. */
  hidden?: string[];
  /** Keyboard and controller bindings; unset uses the defaults. */
  keys?: Bindings;
  pad?: Bindings;
  /** Controllers don't move around the launcher. */
  padOff?: boolean;
  /** No hints along the bottom for what a controller's buttons do. */
  padHintsOff?: boolean;
  /** The library in sections by original console: what groupBy "console" was before there were other sections. */
  byConsole?: boolean;
  /** The library in sections (group.ts); unset is one piece. */
  groupBy?: string;
  /** Browse lists apps already in the library too. */
  browseShowsLibrary?: boolean;
  /** How the library is sorted (sort.ts); unset is recently played. */
  librarySort?: string;
  /** The platforms whose apps the library shows here (platforms.ts); unset is this device's own. */
  platforms?: string[];
  /** How the library shows its apps (view.ts); only what differs from the defaults. */
  library?: Partial<LibraryView>;
  /** The Sidebar layout's sidebar as wide as the player dragged it, in the page's pixels; unset fits the window. */
  sidebarWidth?: number;
  /** Anonymous usage data turned off here (telemetry.ts); signed in, the account's setting wins. */
  telemetryOff?: boolean;
  /** The first-run notice about usage data was answered. */
  telemetryNoticeSeen?: boolean;
  /** Quiver Launcher has started here before. */
  launched?: boolean;
  /** The first-run notice offering to bring over a Quiver Launcher 3 library was answered. */
  oldLibraryNoticeSeen?: boolean;
  /** The first start's setup (setup.tsx) was finished or skipped. */
  setupDone?: boolean;
  /** Offer preview releases of Quiver Launcher too. */
  previewUpdates?: boolean;
  /** Announcements dismissed here, by id (announcements.ts). */
  dismissedAnnouncements?: string[];
  /** Where earlier versions kept the player's GitHub token, as plain text; it moves to the keychain on start (`GITHUB_TOKEN`). */
  githubToken?: string;
};

/** The player's GitHub token, for GitHub's higher limit: in the keychain (secret_set), sent only to GitHub's API, never synced. */
const GITHUB_TOKEN = "githubToken";

/** The GitHub token from the keychain, moving one an earlier version left in settings.json there first. */
async function githubToken(settings: Settings | null): Promise<string | undefined> {
  if (settings?.githubToken) {
    await native.secretSet(GITHUB_TOKEN, settings.githubToken);
    delete settings.githubToken;
    await native.writeState("settings", settings);
  }
  return (await native.secretGet(GITHUB_TOKEN)) ?? undefined;
}

/** An app as usage data names it: a catalog app by its slug; one the player added only by where it's from. */
export const appRef = (id: string, slug: string) => ({
  slug: isCustom(id) || isLocal(id) ? null : slug,
  source: isLocal(id) ? "local" : isCustom(id) ? "custom" : "catalog",
});

type Saved = {
  settings: Settings;
  config: Config;
  library: LibraryItem[];
  installs: Record<string, Install>;
  catalog: Record<string, CatalogEntry>;
  collections: Collection[];
  githubToken?: string;
};

export function LauncherProvider({ children }: { children: ReactNode }) {
  const [saved, setSaved] = useState<Saved | null>(null);
  const [problems, setProblems] = useState<StateProblem[]>([]);
  useEffect(() => {
    // A file that can't be read isn't started over: the first save would replace what a snapshot could restore.
    native.stateProblems().then((found) => {
      if (found.length) return setProblems(found);
      return Promise.all([
        native.config(),
        native.readState<LibraryItem[]>("library"),
        native.readState<Record<string, Install>>("installs"),
        native.readState<Record<string, CatalogEntry>>("catalog"),
        native.readState<Settings>("settings"),
        native.readState<Collection[]>("collections"),
      ]).then(async ([config, library, installs, catalog, settings, collections]) => {
        // The language picked here before anything shows, not the system's for a moment first.
        await activate(languageOf(settings ?? {})).catch(() => {});
        // Without it GitHub still answers, at its lower limit.
        const token = await githubToken(settings).catch(() => undefined);
        setSaved({ config, library: library ?? [], installs: installs ?? {}, catalog: catalog ?? {}, settings: settings ?? {}, collections: collections ?? [], githubToken: token });
      });
    });
  }, []);
  if (problems.length) return <Recovery problems={problems} />;
  if (!saved) return null;
  return (
    <AccountProvider config={saved.config}>
      <LauncherState saved={saved}>{children}</LauncherState>
    </AccountProvider>
  );
}

function LauncherState({ saved, children }: { saved: Saved; children: ReactNode }) {
  const [config, setConfig] = useState(saved.config);
  const account = useAccount();
  const [allItems, setLibrary] = useState(saved.library);
  const [installs, setInstalls] = useState(saved.installs);
  const [catalog, setCatalog] = useState(saved.catalog);
  const [jobs, setJobs] = useState<Record<string, Job>>({});
  const [finished, setFinished] = useState<Finished[]>([]);
  const [choice, setChoice] = useState<Choice | null>(null);
  // Asked one at a time, in turn: Update all can come to several.
  const [questions, setQuestions] = useState<Asking[]>([]);
  const [confirming, confirmUninstall] = useState<Confirming | null>(null);
  const [renaming, askRename] = useState<Entry | null>(null);
  const [choosingProgram, chooseProgram] = useState<string | null>(null);
  const [oldLibrary, setOldLibrary] = useState<V3Library | null | undefined>(undefined);
  const [notice, setNotice] = useState<string | null>(null);
  const library = useMemo(() => allItems.filter((i) => !i.removed), [allItems]);
  // The catalog comes from the site's REST API, which Cloudflare caches for every player; only the account uses Convex.
  const client = useMemo(() => createClient(config.api, { onFallback: (e) => track("catalog_fallback_used", { reason: reasonOf(e) }) }), [config.api]);
  const gitlab = useMemo(() => createGitlab(config.gitlabApi), [config.gitlabApi]);
  /** A custom app's releases, newest first. */
  const releasesOf = (app: Pick<CustomApp, "provider" | "repository">) =>
    app.provider === "gitlab" ? gitlab.releases(app.repository) : github.releases(app.repository);
  const [consoles, setConsoles] = useState<Console[]>([]);
  useEffect(() => void client.facets().then((f) => setConsoles(f.consoles), () => {}), [client]);
  const consoleNames = useMemo(() => Object.fromEntries(consoles.map((c) => [c.id, c.name])), [consoles]);
  const [allCollections, setCollections] = useState(saved.collections);
  const collections = useMemo(() => allCollections.filter((c) => !c.removed).sort((a, b) => a.order - b.order), [allCollections]);
  usePersist("collections", allCollections);

  useEffect(() => {
    // A library that has apps already is past bringing one over.
    if (saved.library.length) setOldLibrary(null);
    else native.findV3Library().then(setOldLibrary, () => setOldLibrary(null));
    const off = native.onProgress((p) => setJobs((j) => ({ ...j, [p.id]: p })));
    const closed = native.onAppClosed(({ id, seconds }) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], playTime: (i[id].playTime ?? 0) + seconds } } : i)));
    return () => void [off, closed].forEach((o) => o.then((stop) => stop()));
  }, [saved]);

  usePersist("library", allItems);
  usePersist("installs", installs);
  usePersist("catalog", catalog);
  const [settings, setSettings] = useState(saved.settings);
  usePersist("settings", settings);
  const [githubToken, setGithubToken] = useState(saved.githubToken);
  const github = useMemo(() => createGithub(config.githubApi, githubToken), [config.githubApi, githubToken]);
  // TV mode (tv.tsx) is a size larger again, read from across a room.
  const tv = tvModeOn(settings, config);
  useEffect(() => void document.documentElement.style.setProperty("zoom", String((settings.scale ?? 1) * (tv ? 1.15 : 1))), [settings.scale, tv]);
  // TV mode fills the screen, as a console does, and leaving it puts the window back: unless full screen is the
  // player's own choice (Settings), which stays. Not at the start without TV mode, which would undo --fullscreen.
  const wasTv = useRef(false);
  useEffect(() => {
    if (tv === wasTv.current) return;
    wasTv.current = tv;
    if (!settings.fullscreen) void native.setFullscreen(tv, Boolean(settings.systemTitleBar)).catch(() => {});
  }, [tv]); // eslint-disable-line react-hooks/exhaustive-deps
  // TV mode is dark whatever the theme, as a console's screen is: its pages over it too.
  const theme = tv ? "dark" : settings.theme;
  useEffect(() => {
    void native.setTheme(theme ?? null).catch(() => {});
    return followTheme(theme);
  }, [theme]);
  useEffect(() => void activate(languageOf(settings)).catch(() => {}), [settings.language]);
  useEffect(() => {
    setBindings(settings);
  }, [settings]);

  // Catalog data for library apps: refreshed at start and every so often, fetched for apps another device added.
  const fetched = useRef(new Set<string>());
  // Every half hour, look again for new releases.
  const [round, setRound] = useState(0);
  useEffect(() => {
    const timer = setInterval(() => (fetched.current.clear(), setRound((r) => r + 1)), 30 * 60_000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    for (const item of library) {
      if (fetched.current.has(item.id)) continue;
      fetched.current.add(item.id);
      const { custom, local, art } = item;
      // Nothing to look up for a local app.
      if (local) {
        setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: localEntry(item.id, local, art) }));
        continue;
      }
      if (custom) {
        releasesOf(custom).then(
          (releases) => setCatalog((c) => ({ ...c, [item.id]: customEntry(item.id, custom, latestOf(releases), art) })),
          () => setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: customEntry(item.id, custom, undefined, art) })),
        );
        continue;
      }
      client.app(item.slug).then(
        (d) => setCatalog((c) => ({ ...c, [item.id]: { ...d.entry, withdrawn: d.withdrawn } })),
        () => {},
      );
    }
  }, [library, client, github, gitlab, round]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: fold the account's library in, live.
  const userId = account.user?.id;
  useEffect(() => {
    if (!userId || !account.items) return;
    const server = account.items;
    setLibrary((local) => {
      const joined = joinAccount(local, userId);
      const next = applyServer(joined, server, userId);
      // Apps on this computer only don't go to the account, so they aren't counted.
      const fromDevice = local.filter((i) => !i.account && !i.local).length;
      const fromAccount = next.filter((i) => !local.some((l) => l.id === i.id)).length;
      if (fromDevice || fromAccount)
        setNotice(t`Library synced: ${plural(fromDevice, { one: "# app", other: "# apps" })} from this computer, ${fromAccount} from your account.`);
      return JSON.stringify(next) === JSON.stringify(local) ? local : next;
    });
  }, [userId, account.items]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: save what changed here.
  const pushing = useRef(false);
  useEffect(() => {
    const changes = userId ? changesFrom(allItems, userId) : [];
    if (!userId || !changes.length || pushing.current) return;
    pushing.current = true;
    const sent = new Set(allItems.filter((i) => i.pending && i.account === userId));
    (async () => {
      // Refused changes come back; they can't succeed by retrying, so they count as done too.
      for (let i = 0; i < changes.length; i += 100) await account.save(changes.slice(i, i + 100));
    })()
      .then(() =>
        // Untouched since sending: done. A removal that reached its account is gone.
        setLibrary((l) => l.flatMap((i) => (!sent.has(i) ? [i] : i.removed ? [] : [{ ...i, pending: undefined }]))),
      )
      .catch(() => {})
      .finally(() => (pushing.current = false));
  }, [allItems, userId]); // eslint-disable-line react-hooks/exhaustive-deps

  // Signed in: collections follow the account, and changes made here are saved to it.
  useEffect(() => {
    if (!userId || !account.collections) return;
    const server = account.collections;
    setCollections((local) => {
      const next = applyServerCollections(local, server, userId);
      return JSON.stringify(next) === JSON.stringify(local) ? local : next;
    });
  }, [userId, account.collections]); // eslint-disable-line react-hooks/exhaustive-deps
  const pushingCollections = useRef(false);
  useEffect(() => {
    const changed = allCollections.filter((c) => c.pending && c.account === userId);
    // Until the site has collections, they stay on this device.
    if (!userId || !account.collections || !changed.length || pushingCollections.current) return;
    pushingCollections.current = true;
    const sent = new Set(changed);
    account
      .saveCollections(changed)
      .then(() => setCollections((all) => all.flatMap((c) => (!sent.has(c) ? [c] : c.removed ? [] : [{ ...c, pending: undefined }]))))
      .catch(() => {})
      .finally(() => (pushingCollections.current = false));
  }, [allCollections, userId, account.collections]); // eslint-disable-line react-hooks/exhaustive-deps

  // Automatic updates waiting on a file Quiver hasn't verified, by app and version: until when.
  const waiting = useRef(new Map<string, number>());
  // Apps set to update by themselves; never a Windows installer's, whose setup window waits for the player, nor one
  // whose file for this computer Quiver hasn't verified yet, which waits until it's verified or the player updates it.
  useEffect(() => {
    for (const item of library) {
      const entry = catalog[item.id];
      const install = installs[item.id];
      const waits = entry?.verified && (waiting.current.get(`${item.id}@${bare(entry.verified.version)}`) ?? 0) > Date.now();
      if (entry && install?.updates === "auto" && !install.installer && !jobs[item.id] && hasUpdate(entry, install) && !skipped(entry, install) && !waits)
        void installEntry(entry, undefined, { auto: true });
    }
  }, [catalog]); // eslint-disable-line react-hooks/exhaustive-deps

  // An app installed unverified turns verified once the site verifies that
  // version, if it's the same file; otherwise the player hears it differs. A
  // file still in its own wait is looked at again once that's over; when
  // isn't known, next time the launcher starts.
  const compared = useRef(new Map<string, number>());
  useEffect(() => {
    for (const [id, install] of Object.entries(installs)) {
      const entry = catalog[id];
      const key = `${id}@${install.version}`;
      if (!install.unverified || install.differs || !entry?.verified || bare(entry.verified.version) !== bare(install.version) || (compared.current.get(key) ?? 0) > Date.now()) continue;
      compared.current.set(key, Infinity);
      client
        .releases(entry.slug, 5)
        .then(({ items }) => {
          const release = items.find((r) => bare(r.version) === bare(install.version));
          if (!release) return;
          const file = installedFile(install.checksum, release);
          if (file === "waiting") {
            const ends = release.assets.find((a) => a.unverifiedChecksum && install.checksum && sameChecksum(a.unverifiedChecksum, install.checksum))?.checkEndsAt;
            if (ends) compared.current.set(key, ends);
            return;
          }
          setInstalls((i) => {
            const now = i[id];
            if (now?.version !== install.version) return i;
            if (file === "differs") return { ...i, [id]: { ...now, differs: true } };
            const { unverified: _, differs: __, blocked: ___, ...verified } = now;
            return { ...i, [id]: verified };
          });
        })
        .catch(() => compared.current.delete(key));
    }
  }, [catalog, installs]); // eslint-disable-line react-hooks/exhaustive-deps

  const fail = (id: string, error: unknown, download = false, instead?: Release) =>
    setJobs((j) => ({
      ...j,
      [id]: { error: error instanceof Error ? error.message : String(error), ...(download ? { download: true as const } : {}), ...(instead ? { instead } : {}) },
    }));

  /** Asks the player about something Quiver hasn't verified; resolves to whether they said to install it. */
  function ask(question: Omit<Asking, "resolve">) {
    return new Promise<boolean>((resolve) => {
      const asked: Asking = {
        ...question,
        resolve: (yes) => {
          setQuestions((q) => q.filter((x) => x !== asked));
          resolve(yes);
        },
      };
      setQuestions((q) => [...q, asked]);
    });
  }

  /**
   * The newest release of an app with no verified one that isn't blocked, from
   * the site's history (the list Change version reads, a hundred at a time);
   * from a site without it, its list of unverified ones.
   */
  async function newestUnverified(entry: Entry) {
    const history = await client.releaseHistory(entry.slug, 100).then(
      (page): (HistoryRelease | UnverifiedRelease)[] => page.items,
      (e) => {
        if (e instanceof ApiError && e.status === 404) return client.unverifiedReleases(entry.slug);
        throw e;
      },
    );
    return newestUnblocked(withdrawnAsBlocked(history, (r) => r.version, catalog[entry.id]?.withdrawn));
  }

  /**
   * `app` is the custom app's repository, when it isn't in the library yet; `auto`, an update nobody asked for just
   * now, which doesn't ask: one whose file Quiver hasn't verified waits for it instead.
   */
  async function installEntry(entry: Entry, chosen?: Release, options: InstallOptions = {}) {
    const { app, pin = false, blocked, auto = false } = options;
    // The player's own files are never downloaded over.
    if (isLocal(entry.id) || installs[entry.id]?.local) return;
    setJobs((j) => ({ ...j, [entry.id]: { id: entry.id, phase: "downloading", received: 0, total: null } }));
    const ref = appRef(entry.id, entry.slug);
    const was = installs[entry.id];
    const from = was?.version;
    let version = chosen?.version ?? entry.verified?.version;
    let format: string | undefined;
    // A release Quiver hasn't verified, or a file of one it has, that the player said to install.
    let unverified = Boolean(options.unverified);
    // What was downloaded, for telling the site when its file was gone or wasn't the one Quiver checked.
    let tried: { release: Release; asset: Asset } | undefined;
    const custom = isCustom(entry.id) ? (app ?? allItems.find((i) => i.id === entry.id)?.custom ?? fromKey(entry.id)) : null;
    // An update installs the same kind as before: a Windows installer's setup, or files in the app's folder.
    const kind = was ? (was.installer === "msi" ? "msi" : "files") : undefined;
    const noAnswer = () => new Problem(msg`Couldn't reach quiverlauncher.com. Check your connection and try again.`);
    const stop = () => {
      setJobs(({ [entry.id]: _, ...rest }) => rest);
      track("app_install_cancelled", { ...ref, version: version ?? null });
    };
    track("app_install_started", { ...ref, version: version ?? null, update: Boolean(from), verified: !unverified, blocked: Boolean(blocked) });
    try {
      let release =
        chosen ??
        (custom
          ? latestOf(await releasesOf(custom))
          : (await client.releases(entry.slug).catch(() => {
              throw noAnswer();
            })).items[0]);
      // No verified release: its newest that isn't blocked, once the player says so, as Quiver Launcher 3 offers it.
      if (!release && !custom) {
        const newest = await newestUnverified(entry).catch(() => {
          throw noAnswer();
        });
        if (newest) {
          version = newest.version;
          if (newest.state !== "verified") {
            if (!(await ask({ entry, version: newest.version, reasons: newest.reasons, scan: newest.scan, checkEndsAt: newest.checkEndsAt }))) return stop();
            unverified = true;
          }
          release = fromSite(newest);
        }
      }
      if (!release) throw new Problem(custom ? msg`This repository has no releases yet.` : msg`This app has no approved release yet.`);
      version = release.version;
      const settings = release.installationOverride ?? entry.launcher;
      let candidates = bestAssets(release.assets, config.os, config.arch, settings.releaseAssetFilter, kind);
      // A file the player picked that a later release names differently: they pick again, even from one.
      const missed = !candidates.length && Boolean(custom && settings.releaseAssetFilter);
      if (missed) candidates = bestAssets(release.assets, config.os, config.arch, undefined, kind);
      let [asset, ...others] = candidates;
      if (!asset && kind && bestAssets(release.assets, config.os, config.arch).length)
        throw new Problem(
          kind === "msi"
            ? msg`This release has no Windows installer (MSI) to update it with. Uninstall it, then get it again to install this release's download.`
            : msg`This release only comes as a Windows installer (MSI). Uninstall it, then get it again to install it that way.`,
        );
      // None marked for this computer: the player picks from the files there are, as they may know which runs.
      const unmatched = !asset;
      if (unmatched) [asset, ...others] = installable(release.assets, config.os).filter((a) => !kind || isMsi(a.filename) === (kind === "msi"));
      if (!asset) throw new Problem(msg`This release has no download for your computer.`);
      // The file the player picked last time, when it's still one of these: taken again without asking.
      const again = pickedBefore([asset, ...others], was?.pick);
      let pick = was?.pick;
      if (again) asset = again;
      else if (others.length || missed || unmatched) {
        const choices = [asset, ...others];
        const picked = await new Promise<Asset | null>((resolve) => setChoice({ entry, version: release.version, assets: choices, unmatched, resolve }));
        setChoice(null);
        if (!picked) return stop();
        asset = picked;
        pick = assetFilterFor(picked.filename, choices.map((a) => a.filename));
      }
      // A file of a verified release that isn't verified itself asks first, as an unverified release does. An
      // automatic update waits instead: until the site says it's verified by itself, else till the launcher starts again.
      const why = !custom && !unverified ? unverifiedFile(asset, release) : undefined;
      if (why) {
        if (auto) {
          waiting.current.set(`${entry.id}@${bare(release.version)}`, asset.checkEndsAt ?? Infinity);
          return setJobs(({ [entry.id]: _, ...rest }) => rest);
        }
        if (!(await ask({ entry, version: release.version, file: { name: asset.filename, why }, reasons: [], checkEndsAt: asset.checkEndsAt }))) return stop();
        unverified = true;
      }
      format = installFormat(asset.filename);
      // One folder, one app: uninstalling either would take the other's files.
      const taken = Object.entries(installs).flatMap(([id, i]) => (id !== entry.id && !i.dir ? [i.folder] : []));
      const folder = freeFolder(settings.folderName?.trim() || entry.slug, installs[entry.id]?.folder, taken);
      const runs = asset.os === "windows" ? "windows" : (config.os as "windows" | "linux" | "macos");
      const executables = settings.preferredExecutables?.[runs] ?? [];
      tried = { release, asset };
      const done = await native.install({
        id: entry.id,
        url: asset.url,
        filename: asset.filename,
        // A file still in its own wait is checked against what Quiver saw of it, as any other.
        checksum: asset.checksum ?? asset.unverifiedChecksum,
        folder,
        dir: was?.dir,
        filesToAdd: settings.filesToAdd ?? [],
        version: release.version,
        preferred: executables,
        program: was?.installer === "msi" ? was.program : undefined,
      });
      // A Windows installer's program, when Quiver can't tell which it is: the player says, as in Quiver Launcher 3.
      const msi = done.installer === "msi";
      const program = msi ? (done.program ?? (await native.pickProgram(done.location ?? undefined).catch(() => null)) ?? undefined) : undefined;
      const name = entry.projectName;
      if (done.restart) setNotice(t`${name} is installed. Windows says to restart the computer to finish setting it up.`);
      // Picking a version only installs it; staying on it is the player's own choice (`pin`).
      // Going back from the verified release: Auto Update waits for the next one rather than undoing it.
      const verified = (catalog[entry.id] ?? entry).verified;
      const skip = chosen && verified && release.releasedAt < verified.releasedAt ? verified.version : undefined;
      // The file's own, which tells it from the one its release is verified with later.
      const checksum = done.checksum ?? asset.checksum ?? asset.unverifiedChecksum;
      setInstalls((i) => {
        const updates = pin ? "pinned" : i[entry.id]?.updates;
        // Installed now, or as long ago as it was before this update (an install from before there was a time, none).
        const installedAt = i[entry.id] ? i[entry.id].installedAt : Date.now();
        return { ...i, [entry.id]: { dir: i[entry.id]?.dir, lastPlayed: i[entry.id]?.lastPlayed, ...(installedAt ? { installedAt } : {}), playTime: i[entry.id]?.playTime, ...(i[entry.id]?.launch ? { launch: i[entry.id]?.launch } : {}), ...(updates ? { updates } : {}), ...(skip ? { skip } : {}), ...(unverified ? { unverified: true } : {}), ...(blocked ? { blocked } : {}), ...(checksum ? { checksum } : {}), ...(pick ? { pick } : {}), version: release.version, releasedAt: release.releasedAt, releaseId: release.id, folder, executables: msi ? [] : executables, ...(msi ? { installer: "msi" as const, ...(program ? { program } : {}) } : {}), ...(asset.os === "windows" && config.os === "linux" ? { wine: true } : {}) } };
      });
      setJobs(({ [entry.id]: _, ...rest }) => rest);
      setFinished((f) => [{ id: entry.id, version: release.version, update: Boolean(from), at: Date.now() }, ...f.filter((d) => d.id !== entry.id)]);
      if (from) track("app_updated", { ...ref, from, to: release.version, auto: !chosen && installs[entry.id]?.updates === "auto", verified: !unverified, blocked: Boolean(blocked), format });
      else track("app_installed", { ...ref, version: release.version, verified: !unverified, blocked: Boolean(blocked), format });
    } catch (error) {
      // A Windows installer's setup the player closed is cancelled too.
      if (error === "cancelled") {
        setJobs(({ [entry.id]: _, ...rest }) => rest);
        track("app_install_cancelled", { ...ref, version: version ?? null, format: format ?? null });
        return;
      }
      let problem = error;
      let instead: Release | undefined;
      // Its file gone, or not the one Quiver checked: said in words the player can act on, a 404 never as the
      // network's fault. The site hears of it, once, and reads that release again; the newest other verified
      // release is offered in its place.
      if ((error === "missing" || error === "mismatch") && tried) {
        const { release, asset } = tried;
        const file = asset.filename;
        const listed = versionLabel(release.version);
        problem = new Problem(
          error === "missing" ? msg`${file} was removed from ${listed} by its developer.` : msg`This download doesn't match the file Quiver checked, so it wasn't installed.`,
        );
        if (!custom) {
          void client.downloadProblem(entry, { version: release.version, file, problem: error }).catch(() => {});
          const fits = (r: Release) => bestAssets(r.assets, config.os, config.arch, (r.installationOverride ?? entry.launcher).releaseAssetFilter, kind).length > 0;
          instead = insteadOf((await client.releases(entry.slug).catch(() => null))?.items ?? [], release.version, from, fits);
        }
      }
      fail(entry.id, problem, true, instead);
      track("app_install_failed", { ...ref, version: version ?? null, update: Boolean(from), reason: reasonOf(problem), format: format ?? null });
    }
  }

  function add(entry: Entry, own: Pick<LibraryItem, "custom" | "local" | "art"> = {}) {
    setLibrary((l) => {
      const mine = l.find((i) => i.id === entry.id);
      const added = { pending: { ...mine?.pending, added: true }, account: userId ?? mine?.account, removed: undefined };
      if (mine) return mine.removed ? l.map((i) => (i === mine ? { ...i, ...added } : i)) : l;
      // A local app stays on this computer, out of the account.
      return [...l, { id: entry.id, slug: entry.slug, addedAt: Date.now(), ...own, ...(userId && !own.local ? added : {}) }];
    });
  }

  /** Installed apps' folders in the apps folder: not 3.x installs or programs left where they are. */
  function appFolders() {
    return Object.values(installs).flatMap((i) => (i.dir ? [] : [i.folder]));
  }

  /** Deletes an app's files; a local app's are the player's own and stay. */
  async function uninstall(id: string) {
    const install = installs[id];
    if (!install || install.local) return;
    await native.uninstall(install.folder, install.dir);
    track("app_uninstalled", appRef(id, catalog[id]?.slug ?? allItems.find((i) => i.id === id)?.slug ?? ""));
  }

  /** Repositories the catalog lists, to send them to the catalog's app instead. */
  async function catalogRepositories() {
    const ids = new Map<string, { id: string; slug: string }>();
    for (let cursor: string | null = null, done = false; !done; ) {
      const page = await client.releaseStatus(cursor);
      for (const s of page.items) {
        if (s.repository) ids.set(`${s.provider}:${s.repository}`.toLowerCase(), s);
        // And by its id, as Quiver Launcher 3 kept it (importOld), under a key no provider has.
        ids.set(`id:${s.id}`, s);
      }
      [cursor, done] = [page.nextCursor, page.isDone];
    }
    return ids;
  }

  /** The catalog's app for a repository, or a preview of its latest release. */
  async function lookUpRepository(input: string): ReturnType<Launcher["lookUpRepository"]> {
    const repository = parseRepository(input);
    if (!repository) return { error: t`Enter a GitHub or GitLab repository: owner/name, or its github.com or gitlab.com address.` };
    // Already added: its page.
    const key = customKey(repository.provider, repository.repository);
    if (library.some((i) => i.id === key) && catalog[key]) return { listed: catalog[key] };
    try {
      const listed = (await catalogRepositories().catch(() => new Map())).get(`${repository.provider}:${repository.repository}`.toLowerCase());
      const detail = listed && (await client.app(listed.slug).catch(() => null));
      if (detail) return { listed: detail.entry };
      const latest = latestOf(await releasesOf(repository));
      const app: CustomApp = { ...repository, name: repository.repository.split("/").at(-1)! };
      return { preview: { app, latest, files: latest ? bestAssets(latest.assets, config.os, config.arch) : [] } };
    } catch (e) {
      return { error: e instanceof Error ? e.message : String(e) };
    }
  }

  // Fetched once a session, for the pages of apps the player added; again after a failure.
  const listed = useRef<ReturnType<typeof catalogRepositories> | null>(null);
  const [switched, setSwitched] = useState<Record<string, string>>({});

  /** Adds a custom app for a repository; the caller installs it. */
  function addCustom(custom: CustomApp, latest?: Release, art?: GameArt) {
    const entry = customEntry(customKey(custom.provider, custom.repository), custom, latest, art);
    setCatalog((c) => ({ ...c, [entry.id]: entry }));
    add(entry, { custom, art });
    return entry;
  }

  const value: Launcher = {
    config,
    client,
    github,
    githubToken,
    saveGithubToken: async (token) => {
      await native.secretSet(GITHUB_TOKEN, token ?? null);
      setGithubToken(token);
    },
    gitlab,
    consoles,
    consoleNames,
    collections,
    saveCollection: (c) => {
      if (!c.removed && !allCollections.some((x) => x.key === c.key))
        track(c.follows ? "playlist_followed" : "playlist_created", c.follows ? undefined : { has_filters: hasFilters(c), apps: c.apps?.length ?? 0 });
      setCollections((all) => {
        const synced = c.account ?? userId;
        const next = { ...c, account: synced, pending: synced ? true : undefined };
        // Never synced: a removal just deletes it.
        const keep = c.removed && !synced ? [] : [next];
        return all.some((x) => x.key === c.key) ? all.flatMap((x) => (x.key === c.key ? keep : [x])) : [...all, ...keep];
      });
    },
    setTags: (id, tags) =>
      setLibrary((l) => l.map((i) => (i.id !== id ? i : { ...i, overrides: { ...i.overrides, tags: tags.length ? tags : undefined }, pending: i.account ? { ...i.pending, tags: true } : i.pending }))),
    setConsoles: (id, consoles) => {
      const theirs = catalog[id]?.consoles ?? [];
      const same = consoles && consoles.length === theirs.length && consoles.every((c) => theirs.includes(c));
      const set = same ? undefined : consoles;
      setLibrary((l) => l.map((i) => (i.id !== id ? i : { ...i, overrides: { ...i.overrides, consoles: set }, pending: i.account ? { ...i.pending, consoles: true } : i.pending })));
    },
    library,
    notice,
    setNotice,
    add: (entry) => add(entry),
    setUpdates: (id, updates) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], updates } } : i)),
    setLaunch: (id, launch) =>
      setInstalls((i) => {
        if (!i[id]) return i;
        const { launch: _, ...install } = i[id];
        const set = Object.fromEntries(Object.entries(launch).filter(([, v]) => v !== undefined && v !== "")) as LaunchOptions;
        return { ...i, [id]: Object.keys(set).length ? { ...install, launch: set } : install };
      }),
    lookUpRepository(input) {
      // A lookup that never answers ends with a message, and the button tries again.
      let timer: ReturnType<typeof setTimeout> | undefined;
      const late = new Promise<{ error: string }>((resolve) => {
        timer = setTimeout(() => resolve({ error: t`Looking it up took too long. Check your connection and try again.` }), LOOKUP_TIMEOUT);
      });
      return Promise.race([lookUpRepository(input), late]).finally(() => clearTimeout(timer));
    },
    async listedAs(id) {
      const app = allItems.find((i) => i.id === id)?.custom ?? (isCustom(id) ? fromKey(id) : null);
      if (!app) return null;
      listed.current ??= catalogRepositories().catch((e) => {
        listed.current = null;
        throw e;
      });
      const match = (await listed.current).get(`${app.provider}:${app.repository}`.toLowerCase());
      return match ? ((await client.app(match.slug).catch(() => null))?.entry ?? null) : null;
    },
    switchToCatalog(id, entry) {
      const old = library.find((i) => i.id === id);
      if (!old?.custom || library.some((i) => i.id === entry.id)) return;
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      const synced = userId ?? old.account;
      const moved: LibraryItem = {
        id: entry.id,
        slug: entry.slug,
        addedAt: old.addedAt,
        ...(old.overrides ? { overrides: old.overrides } : {}),
        ...(synced ? { account: synced, pending: { added: true } } : {}),
      };
      // The old one goes as a removal does: from its account too, once synced.
      setLibrary((l) => [...l.flatMap((i) => (i.id === entry.id ? [] : i.id !== id ? [i] : i.account ? [{ ...i, removed: true, pending: { removed: true } }] : [])), moved]);
      // Its files stay where they are; the catalog's releases replace them on its next update.
      setInstalls(({ [id]: was, ...rest }) => {
        if (!was) return rest;
        const { releaseId: _, ...install } = was;
        const executables = entry.launcher.preferredExecutables?.[config.os as "windows" | "linux" | "macos"] ?? was.executables;
        return { ...rest, [entry.id]: { ...install, executables, unverified: true } };
      });
      setCollections((all) =>
        all.map((c) => {
          if (c.removed || !(c.apps?.includes(id) || c.icon === id)) return c;
          const account = c.account ?? userId;
          return { ...c, apps: c.apps?.map((a) => (a === id ? entry.id : a)), ...(c.icon === id ? { icon: entry.id } : {}), account, pending: account ? true : undefined };
        }),
      );
      setSwitched((m) => ({ ...m, [id]: entry.id }));
    },
    switched,
    addCustomApp(app, latest, art) {
      const entry = addCustom(app, latest, art);
      track("app_added", { kind: "repository", host: app.provider });
      void installEntry(entry, undefined, { app });
      return entry;
    },
    async addLocalApp(app, art) {
      const id = localKey();
      let local: LocalApp;
      let install: Install;
      if (app.kind === "program") {
        // It starts where it is, from its own folder.
        const cut = Math.max(app.path.lastIndexOf("/"), app.path.lastIndexOf("\\")) + 1;
        const file = app.path.slice(cut);
        local = { kind: "program", path: app.path, name: app.name };
        // A Windows program on Linux runs through Wine.
        const wine = config.os === "linux" && /\.(exe|bat|cmd)$/i.test(file);
        install = { local: true, version: "", dir: app.path.slice(0, cut), folder: file, executables: [], program: app.path, ...(wine ? { wine } : {}) };
      } else {
        const folder = await native.createAppFolder(folderNameFor(app.name));
        local = { kind: "folder", folder, name: app.name };
        install = { local: true, version: "", folder, executables: [] };
      }
      const entry = localEntry(id, local, art);
      setCatalog((c) => ({ ...c, [id]: entry }));
      add(entry, { local, art });
      setInstalls((i) => ({ ...i, [id]: install }));
      track("app_added", { kind: app.kind, host: null });
      return entry;
    },
    settings,
    setSettings,
    customize: (id, { name, cover }) =>
      setLibrary((l) =>
        l.map((i) => {
          if (i.id !== id) return i;
          const was = i.overrides ?? {};
          const pending = i.account
            ? { ...i.pending, ...(name !== was.name ? { name: true } : {}), ...(cover !== was.cover ? { cover: true } : {}) }
            : i.pending;
          return { ...i, overrides: { ...was, name, cover }, pending };
        }),
      ),
    unsynced: userId ? changesFrom(allItems, userId).length : 0,
    async signOut() {
      track("signed_out");
      await account.signOut();
      // Apps installed here stay in the library, still the account's, so signing back in carries on and another account leaves them out.
      setLibrary((l) => l.filter((i) => i.local || installs[i.id]));
      setCollections([]);
      setNotice(t`Signed out. Apps installed on this computer stay in your library; sign in to see the rest.`);
    },
    installs,
    catalog,
    jobs,
    finished,
    clearFinished: () => setFinished([]),
    choice,
    asking: questions[0] ?? null,
    confirming,
    confirmUninstall,
    choosingProgram,
    chooseProgram,
    cache: (entries) => setCatalog((c) => ({ ...c, ...Object.fromEntries(entries.map((e) => [e.id, { ...c[e.id], ...e }])) })),
    remember: (entries) => {
      const known = new Set(library.map((i) => i.id));
      const fresh = entries.filter((e) => known.has(e.id));
      if (fresh.length) setCatalog((c) => ({ ...c, ...Object.fromEntries(fresh.map((e) => [e.id, { ...c[e.id], ...e }])) }));
    },
    async get(entry, release, options) {
      if (jobs[entry.id] && !("error" in jobs[entry.id])) return;
      // Its verified release was withdrawn: only one the player picks installs, from its versions.
      const pulled = !release && pulledVerified(catalog[entry.id] ?? entry);
      if (pulled) {
        const version = versionLabel(pulled.version);
        const name = entry.projectName;
        // The reason is the site's, as it gave it.
        const reason = pulled.reason;
        return setNotice(t`${version} of ${name} was withdrawn: ${reason} Choose another version from its page.`);
      }
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      add(entry);
      await installEntry(entry, release, { pin: options?.pin, unverified: options?.unverified || Boolean(options?.blocked), blocked: options?.blocked });
    },
    async play(id, executable) {
      const install = installs[id];
      if (!install) return;
      // The programs the player picked for Play: one starts; more, and they choose.
      const programs = install.launch?.programs ?? [];
      if (!executable && programs.length > 1) return chooseProgram(id);
      const chosen = executable ?? programs[0];
      const launch = chosen ? { ...install.launch, executable: chosen } : install.launch;
      let program = install.program;
      // A Windows installer's program the player didn't choose after its setup: they choose it now.
      if (install.installer === "msi" && !program) {
        program = (await native.pickProgram().catch(() => null)) ?? undefined;
        if (!program) return;
      }
      setInstalls((i) => ({ ...i, [id]: { ...i[id], lastPlayed: Date.now(), ...(program ? { program } : {}) } }));
      const ref = appRef(id, catalog[id]?.slug ?? allItems.find((i) => i.id === id)?.slug ?? "");
      await native.launch(id, install.folder, install.dir, install.executables, Boolean(install.wine), program, launch).then(
        () => track("app_launched", ref),
        (e) => (fail(id, e), track("app_launch_failed", { ...ref, reason: reasonOf(e) })),
      );
    },
    async reinstall(id) {
      const install = installs[id];
      const entry = catalog[id];
      if (!install || !entry || (jobs[id] && !("error" in jobs[id]))) return;
      const custom = isCustom(id) ? (allItems.find((i) => i.id === id)?.custom ?? fromKey(id)) : null;
      const same = (r: { version: string }) => bare(r.version) === bare(install.version);
      try {
        const release = custom
          ? (await releasesOf(custom)).find(same)
          : ((await client.releases(entry.slug, 100)).items.find(same) ??
            (install.unverified ? (await client.unverifiedReleases(entry.slug).catch(() => [])).filter(same).map(fromSite)[0] : undefined));
        const version = versionLabel(install.version);
        if (!release) throw new Problem(msg`${version} can't be installed again: its release isn't available any more.`);
        await installEntry(entry, release, { unverified: Boolean(install.unverified), blocked: install.blocked });
      } catch (e) {
        fail(id, e, true);
      }
    },
    async remove(id) {
      // Saying no when Windows asks to uninstall it keeps it, in the library too.
      if (await uninstall(id).then(() => false, (e) => e === "cancelled")) return;
      setInstalls(({ [id]: _, ...rest }) => rest);
      // A synced app is removed from its account too; one that never synced just goes.
      setLibrary((l) =>
        l.flatMap((i) =>
          i.id !== id ? [i] : i.account ? [{ ...i, removed: true, pending: { removed: true } }] : [],
        ),
      );
      setJobs(({ [id]: _, ...rest }) => rest);
    },
    uninstall: (id) =>
      uninstall(id).then(
        () => setInstalls(({ [id]: _, ...rest }) => rest),
        (e) => void (e !== "cancelled" && fail(id, e)),
      ),
    oldLibrary,
    findOld: (path, apps) => native.findV3LibraryIn(path, apps),
    /**
     * Adds the 3.x apps, keeping their installs: the catalog's as the catalog has them (found by repository, or by
     * the entry 3.x knew), any other from GitHub or GitLab as a custom app. Returns the names of those it couldn't:
     * ones 3.x didn't say where they're from.
     */
    async importOld(apps, onProgress) {
      const ids = await catalogRepositories();
      let done = 0;
      // Each asks the site, or its repository's host, so several at a time; then they're added in the library's order.
      const found = await inTurns(apps, 6, async (old) => {
        const match = (old.repository && ids.get(`${old.provider}:${old.repository}`.toLowerCase())) || (old.catalogId && ids.get(`id:${old.catalogId}`));
        const detail = match && (await client.app(match.slug).catch(() => null));
        if (detail) return { kind: "catalog" as const, detail };
        if (!old.repository || !((old.provider === "github" && githubRepository(old.repository)) || old.provider === "gitlab")) return null;
        // Not in the catalog: a custom app, with its game's artwork and consoles as adding one picks them. A
        // repository whose releases can't be read now (gone, or GitHub's hourly limit) still comes over, installs
        // and all: its releases are read when its page is opened.
        const custom: CustomApp = { provider: old.provider, repository: old.repository, name: old.name };
        const [releases, art] = await Promise.all([releasesOf(custom).catch(() => null), bestGameArt(client, old.name).catch(() => undefined)]);
        return { kind: "custom" as const, custom, latest: releases ? latestOf(releases) : undefined, art };
      }, () => onProgress?.(++done));
      const missing: string[] = [];
      apps.forEach((old, i) => {
        const it = found[i];
        if (!it) return void missing.push(old.name);
        let entry: Entry;
        if (it.kind === "catalog") {
          const { detail } = it;
          entry = detail.entry;
          setCatalog((c) => ({ ...c, [detail.entry.id]: { ...detail.entry, withdrawn: detail.withdrawn } }));
          add(entry);
        } else entry = addCustom(it.custom, it.latest, it.art);
        const install = adoptedInstall(old, entry, config.os);
        if (install) setInstalls((i) => ({ ...i, [entry.id]: install }));
      });
      // The player brought a library over: the one found by itself isn't offered again.
      setOldLibrary(null);
      return missing;
    },
    dismiss: (id) => setJobs(({ [id]: _, ...rest }) => rest),
    appFolders,
    renaming,
    askRename,
    async renameFolder(id, name) {
      const install = installs[id];
      if (!install || !renamable(install)) throw new Problem(msg`This app's folder can't be renamed.`);
      if (jobs[id] && !("error" in jobs[id])) throw new Problem(msg`Wait for its download to finish, then rename its folder.`);
      if (!name.trim()) throw new Problem(msg`Enter a name for its folder.`);
      const folder = folderNameFor(name);
      if (folder === install.folder) return folder;
      // One folder, one app, as installing keeps it.
      const taken = Object.entries(installs).some(([other, i]) => other !== id && !i.dir && i.folder.toLowerCase() === folder.toLowerCase());
      if (taken) throw new Problem(msg`Another app in your library uses a folder named ${folder}. Choose another name.`);
      await native.renameAppFolder(id, install.folder, folder);
      setInstalls((i) => ({ ...i, [id]: { ...i[id], folder } }));
      // A folder the player fills keeps its name in the library too, which its page shows.
      const item = library.find((i) => i.id === id);
      if (item?.local?.kind === "folder") {
        const local: LocalApp = { ...item.local, folder };
        setLibrary((l) => l.map((i) => (i.id === id ? { ...i, local } : i)));
        setCatalog((c) => ({ ...c, [id]: localEntry(id, local, item.art) }));
      }
      track("app_folder_renamed", appRef(id, catalog[id]?.slug ?? item?.slug ?? ""));
      return folder;
    },
    async moveApps(to) {
      const appsDir = await native.moveAppsFolder(to, appFolders());
      setConfig((c) => ({ ...c, appsDir }));
      setTelemetryAppsDir(appsDir);
      track("apps_folder_moved", { apps: appFolders().length });
    },
    cancel: (id) => void native.cancelInstall(id).catch(() => {}),
  };
  const now = useRef(value);
  now.current = value;
  return (
    <LauncherNow.Provider value={now}>
      <LauncherContext.Provider value={value}>{children}</LauncherContext.Provider>
    </LauncherNow.Provider>
  );
}

/** Saves a state file whenever it changes after the first render. */
function usePersist(name: "library" | "installs" | "catalog" | "settings" | "collections", value: unknown) {
  const first = useRef(true);
  useEffect(() => {
    if (first.current) first.current = false;
    else native.writeState(name, value);
  }, [name, value]);
}
