/**
 * The player's library and what's installed, kept on this device.
 *
 * A library item only points at its catalog entry (plus the player's own
 * overrides); names, artwork and files always come from the catalog. The
 * last catalog data seen is cached so the library shows instantly offline.
 */
import { createContext, useContext, useEffect, useMemo, useRef, useState, type Dispatch, type ReactNode, type SetStateAction } from "react";
import { msg, plural, t } from "@lingui/core/macro";
import { ApiError, createClient, createGithub, createGitlab, githubRepository, parseRepository, type Asset, type Client, type Console, type Entry, type Github, type Os, type HistoryRelease, type Release, type ReleaseScan, type ReleaseStatus, type UnverifiedRelease, type Withdrawn } from "@quiverlauncher/api";
import type { HiddenApp } from "./browse-hidden";
import { native, type Config, type LaunchOptions, type OldApp, type OldFilter, type Progress, type StateProblem, type V3Library, type WindowsRunner } from "./native";
import { Recovery } from "./recovery";
import { assetFilterFor, bestAssets, installable, installFormat, isMsi, pickedBefore, releaseFor } from "./assets";
import { cameAfter, fromSite, installedFile, insteadOf, newestUnblocked, sameChecksum, unverifiedFile, versionLabel, withdrawnAsBlocked } from "./versions";
import { captureError, captureFailure, Problem, reasonOf, reportStartupFailure, setTelemetryAppsDir, track, type TelemetryContext } from "./telemetry";
import { AccountProvider, useAccount } from "./account";
import { applyServer, applyServerCollections, changesFrom, hasFilters, joinAccount, matches, newKey, type Collection } from "./sync";
import type { Platform } from "./platforms";
import { setBindings, setScrolling, type Bindings } from "./spatial";
import { musicForGame, setMusic } from "./music";
import { appsOfRepository, bestGameArt, customEntry, customKey, folderNameFor, freeFolder, isCustom, isLocal, localEntry, localKey, withGame, type CustomApp, type GameArt, type LocalApp } from "./custom";
import { artFiles, type Pictures } from "./custom-art";
import { followTheme, type Theme } from "./theme";
import type { LibraryView } from "./view";
import { nearestInterfaceSize, pageZoom } from "./zoom";
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
  overrides?: {
    name?: string;
    /** A cover's address, which syncs with the account. */
    cover?: string;
    /** The rest of the player's pictures, and any cover from a file: this device only (custom-art.tsx). */
    pictures?: Pictures;
    tags?: string[];
    consoles?: string[];
  };
  /** Changed here and not yet saved to the account. */
  pending?: { added?: boolean; name?: boolean; cover?: boolean; tags?: boolean; consoles?: boolean; removed?: boolean };
  /** The account this item last synced with. */
  account?: string;
  /** Removed here; kept until the removal reaches its account. */
  removed?: boolean;
};
/** Catalog data for a library app, with releases pulled since. */
export type CatalogEntry = Entry & {
  withdrawn?: Withdrawn[];
  /**
   * The newest release an install here can update to, as the verified release may only have another system's build.
   * `of` says which verified release and build it was looked up for (`forHereKey`); none found, no `release`.
   */
  forHere?: { of: string; release?: { version: string; releasedAt: number } };
};
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
  /** A release the player said to skip: not offered or installed by itself, until a newer one comes. */
  declined?: string;
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
  /** The empty files its folder has, once the player or Settings chose them (Add files to folder); unset, the catalog's. Kept through updates. */
  files?: string[];
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
type InstallOptions = { app?: CustomApp; pin?: boolean; unverified?: boolean; blocked?: string; auto?: boolean; asset?: string; platform?: Os };

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
  /** Imports a Quiver Launcher 3 library's apps and tag filters, saying how many apps are done as it goes; resolves to the names of those it couldn't. */
  importOld: (library: Pick<V3Library, "apps" | "filters">, onProgress?: (done: number) => void) => Promise<string[]>;
  /** The Quiver Launcher 3 library in a folder the player picked, its installs in `apps` if given: null when there isn't one. */
  findOld: (path: string, apps?: string[]) => Promise<V3Library | null>;
  /** Installs of Quiver Launcher 3's for apps in the library that show as not installed here, once found: a library brought over before Quiver could see them. */
  oldInstalls: Record<string, Install> | null;
  /** Answers the offer of `oldInstalls`: played from where they are, or left. */
  answerOldInstalls: (use: boolean) => void;
  /** Asks for the folder an app is already installed in and plays it from there, as Quiver Launcher 3's Locate existing install did. */
  locateInstall: (entry: Entry) => Promise<void>;
  remember: (entries: Entry[]) => void;
  /** Keeps catalog data for apps that aren't in the library, such as those on a copy of a shared playlist. */
  cache: (entries: Entry[]) => void;
  /**
   * Adds the app to the library and installs it: its newest release, or the one given; `pin` then keeps it on that one.
   * With no verified release, its newest that isn't blocked, once the player confirms.
   * `unverified`: a release Quiver hasn't verified, which the player confirmed.
   * `blocked`: why Quiver blocked it, for one the player insisted on.
   * `asset`: the name of the release's file the player picked, in place of the one Quiver would choose.
   * `platform`: the system whose build to install, as Windows' through Wine on Linux; later updates keep to it.
   */
  get: (entry: Entry, release?: Release, options?: { pin?: boolean; unverified?: boolean; blocked?: string; asset?: string; platform?: Os }) => Promise<void>;
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
  lookUpRepository: (input: string) => Promise<{ error: string } | { listed: Entry[] } | { preview: RepositoryPreview }>;
  /** The catalog's apps for an app the player added from its repository, once the catalog lists it; several when the repository ships several. */
  listedAs: (id: string) => Promise<Entry[]>;
  /** Repositories the player added that the catalog lists an app of now, that isn't in the library yet. */
  inCatalog: ReadonlySet<string>;
  /** The game an app the player added plays: its artwork, consoles and "Based on". Null for none. */
  setGame: (id: string, art: GameArt | null) => void;
  /**
   * Moves an app the player added onto the catalog's app for it: same files
   * and folder, play time, launch options and the player's name, artwork and
   * tags. One from its repository then updates from the catalog; a local one's
   * files stay the player's own, never updated or deleted.
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
  /** Adds an app the player picked out, without downloading it, and says so: its card in Browse looks no different. */
  addToLibrary: (entry: Entry) => void;
  setUpdates: (id: string, updates: Install["updates"]) => void;
  /** Stops offering this release of an app; a newer one is offered as usual. */
  declineUpdate: (id: string, version: string) => void;
  /** Sets how an installed app starts; nothing set goes back to the launcher's choice. */
  setLaunch: (id: string, launch: LaunchOptions) => void;
  /** The empty files the player chose for an app's folder, which updates keep to. */
  setFiles: (id: string, files: string[]) => void;
  /** Sets the player's own name and artwork; undefined shows the catalog's. */
  customize: (id: string, overrides: { name?: string; cover?: string; pictures?: Pictures }) => void;
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
  /** Takes the new settings, or how to change them as they are by then, for a change that may come after another. */
  setSettings: Dispatch<SetStateAction<Settings>>;
  /** A short note for the player, such as what signing in brought over; `stays` until OK, for one that must be read. */
  notice: { text: string; stays?: boolean } | null;
  setNotice: (text: string | null, options?: { stays?: boolean }) => void;
  dismiss: (id: string) => void;
  /** The folders of installed apps that live in the apps folder, which moving it takes along. */
  appFolders: () => string[];
  /** The app whose folder the player is renaming; null closes the prompt. */
  renaming: Entry | null;
  askRename: (entry: Entry | null) => void;
  /** The app whose folder the player is adding files to, from its menu; null closes the prompt. */
  addingFiles: Entry | null;
  askAddFiles: (entry: Entry | null) => void;
  /** The app whose versions are open from its menu's Change version; null closes them. */
  changingVersion: Entry | null;
  changeVersion: (entry: Entry | null) => void;
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

/** The empty file that has many apps keep their saves and settings in their own folder. */
export const PORTABLE_FILE = "portable.txt";
/** Whether files can be added to an app's folder: not a program the player added from where it is, or one its Windows installer put elsewhere. */
export const filesAddable = (install: Install | undefined) => Boolean(install && !install.program && install.installer !== "msi");
/** File names without blanks or repeats; on Windows, names in different case are one file. */
export const uniqueFiles = (names: string[]) => {
  const seen = new Set<string>();
  return names.map((n) => n.trim()).filter((n) => n && !seen.has(n.toLowerCase()) && seen.add(n.toLowerCase()));
};
/**
 * The files an install keeps to itself, rather than following the catalog's: an update's are what it had, and Settings'
 * default only reaches a new install, as adding `portable.txt` to one already played would seem to lose its saves.
 */
export const installFiles = (was: Install | undefined, catalog: string[], portableByDefault: boolean | undefined) =>
  was ? was.files : portableByDefault ? uniqueFiles([...catalog, PORTABLE_FILE]) : undefined;

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

/**
 * The site's verified release when it's one that was withdrawn since: the site
 * should have moved verified back to an earlier release, and until it has,
 * nothing installs this one without the player choosing it and insisting.
 */
export const pulledVerified = (entry: CatalogEntry | undefined) => {
  const verified = entry?.verified?.version;
  return verified ? entry.withdrawn?.find((w) => bare(w.version) === bare(verified)) : undefined;
};

const newerThan = (release: { version: string; releasedAt?: number }, install: Install) => bare(install.version) !== bare(release.version) && cameAfter(release, install);

/** What `forHere` was looked up for: this verified release, and the kind of build installed. */
export const forHereKey = (verified: { version: string }, install: Install) =>
  `${bare(verified.version)}:${install.wine ? "windows" : "own"}:${install.installer === "msi" ? "msi" : "files"}`;

/** Whether there might be an update, which only the app's releases can say: the catalog's verified release is newer. */
export const mayUpdate = (entry: CatalogEntry | undefined, install: Install | undefined) =>
  // Only newer releases: a pulled one rolls back through the withdrawn warning instead, and a withdrawn one is never an update.
  Boolean(entry?.verified && install && !install.local && !pulledVerified(entry) && newerThan(entry.verified, install));

/**
 * The release this install could update to, whatever the player chose to do about it. None until `forHere` is looked
 * up: a false update is worse than one that shows a moment late, as installing it could replace what works.
 */
export function updateFor(entry: CatalogEntry | undefined, install: Install | undefined) {
  if (!mayUpdate(entry, install)) return undefined;
  const found = entry!.forHere;
  if (found?.of !== forHereKey(entry!.verified!, install!)) return undefined;
  return found.release && newerThan(found.release, install!) ? found.release : undefined;
}

/** The update the player said to skip, while it's still the one there is. */
export function declinedUpdate(entry: CatalogEntry | undefined, install: Install | undefined) {
  const update = updateFor(entry, install);
  return update && install?.declined && bare(install.declined) === bare(update.version) ? update : undefined;
}

/** Auto Update leaves this release alone: the player went back from it. */
export function skipped(entry: CatalogEntry | undefined, install: Install | undefined) {
  const update = updateFor(entry, install);
  return Boolean(update && install?.skip && bare(install.skip) === bare(update.version));
}

export function hasUpdate(entry: CatalogEntry | undefined, install: Install | undefined) {
  return Boolean(updateFor(entry, install) && install?.updates !== "pinned" && !declinedUpdate(entry, install));
}

/**
 * The install a Quiver Launcher 3 app comes over with, staying where 3.x put
 * it; none when it wasn't installed. It has no release date, so its version
 * number says which releases are newer. A version 3.x didn't know comes as
 * 3.x's own 0.0.0 (v3.rs), so any verified release is an update.
 */
export function adoptedInstall(old: OldApp, entry: Entry, os: Os): Install | undefined {
  if (!old.dir || !old.version) return undefined;
  const executables = entry.launcher.preferredExecutables?.[os as "windows" | "linux" | "macos"] ?? [];
  // One 3.x installed with its Windows installer starts the program it linked.
  const msi = old.program ? { installer: "msi" as const, program: old.program } : {};
  return { dir: old.dir, version: old.version, folder: entry.launcher.folderName || entry.slug, executables: old.program ? [] : executables, ...msi };
}

/**
 * An app the player has pointed at a new folder, given the install it had: what they chose and its history stay, and
 * what described the old copy goes, as the folder says what's there now.
 */
export function relocatedInstall(now: Install | undefined, found: Install): Install {
  if (!now) return found;
  const { lastPlayed, installedAt, playTime, updates, skip, declined, launch, pick, files } = now;
  return { ...found, lastPlayed, installedAt, playTime, updates, skip, declined, launch, pick, files };
}

/** Whether starting an app failed because its folder is gone: lib.rs's FOLDER_GONE. */
export const folderGone = (job: Job | undefined) => Boolean(job && "error" in job && job.error === "This app's folder isn't there any more.");

/** Whether the player can point Quiver at where an app is: it has no install here, or the one here lost its folder. Never during a download, or for the player's own files. */
export const locatable = (id: string, install: Install | undefined, job: Job | undefined) =>
  !isLocal(id) && !install?.local && (!install || folderGone(job)) && !(job && !("error" in job));

/**
 * The catalog's entry for a 3.x app: the one 3.x knew first, as one repository can ship several apps, each with an
 * entry of its own (a port of two games); else the one for its repository.
 */
export function catalogMatch<T>(old: OldApp, ids: Map<string, T>): T | undefined {
  return (old.catalogId && ids.get(`id:${old.catalogId}`)) || (old.repository && ids.get(`${old.provider}:${old.repository}`.toLowerCase())) || undefined;
}

/**
 * The install an app has after bringing a 3.x library over again: what it has here stays, so one downloaded since
 * isn't pointed back at 3.x's copy, unless that's another of 3.x's folders, which is the one 3.x says is this app's.
 */
export function keptInstall(now: Install | undefined, adopted: Install): Install {
  return !now ? adopted : now.dir && now.dir !== adopted.dir ? { ...now, ...adopted } : now;
}

/** The installs 3.x has for apps in the library that have none here, by app. */
export function looseInstalls(apps: OldApp[], ids: Map<string, { id: string }>, library: LibraryItem[], installs: Record<string, Install>, catalog: Record<string, Entry>, os: Os) {
  const loose: Record<string, Install> = {};
  const has = (id: string | undefined): id is string => Boolean(id && library.some((i) => i.id === id && !i.removed));
  for (const old of apps) {
    // A catalog app that couldn't be read when it came over is in the library as a custom one.
    const custom = old.repository && (old.provider === "github" || old.provider === "gitlab") ? customKey(old.provider, old.repository) : undefined;
    const id = [catalogMatch(old, ids)?.id, custom].find(has);
    if (!id || loose[id] || installs[id] || !catalog[id]) continue;
    const install = adoptedInstall(old, catalog[id], os);
    if (install) loose[id] = install;
  }
  return loose;
}

/**
 * The tags the player gave a 3.x app, which come over as their own: its tags less the catalog's, the ones 3.x last
 * had from it and the entry's now (a 3.x from before it kept them has only those), as many as the tag field takes.
 */
export function importedTags(old: OldApp, entry: Entry): string[] {
  const catalogs = new Set([...(old.catalogTags ?? []), ...entry.tags, ...(entry.consoles ?? [])].map((t) => t.toLowerCase()));
  const own = (old.tags ?? []).map((t) => t.trim().slice(0, 50)).filter((t) => t && !catalogs.has(t.toLowerCase()));
  return own.filter((t, i) => own.findIndex((o) => o.toLowerCase() === t.toLowerCase()) === i).slice(0, 20);
}

/**
 * What a 3.x library brings along besides its apps, as far as it can say before the catalog is asked: the apps
 * with tags of the player's own (those 3.x didn't have from the catalog, or any on an app the catalog doesn't
 * know), its tag filters and its hidden apps. Only apps it knows where they come from come over.
 */
export function bringsAlong({ apps, filters }: Pick<V3Library, "apps" | "filters">) {
  const coming = apps.filter((a) => a.repository || a.catalogId);
  const own = (a: OldApp) => {
    // A catalog app from a 3.x that didn't keep the catalog's tags can't tell them apart.
    if (!a.catalogTags && a.catalogId) return false;
    const catalogs = new Set((a.catalogTags ?? []).map((t) => t.toLowerCase()));
    return (a.tags ?? []).some((t) => !catalogs.has(t.toLowerCase()));
  };
  return { tagged: coming.filter(own).length, filters: filters?.length ?? 0, hidden: coming.filter((a) => a.hidden).length };
}

/**
 * An app brought over from 3.x: its id here, the tags 3.x matched its filters on, and what a playlist matches here:
 * its tags and platforms.
 */
export type ImportedApp = { id: string; old: string[]; tags: string[]; platforms?: Entry["supportedOS"] };

/** 3.x's tags for a platform, which a playlist here filters by platform instead. */
const PLATFORM_TAGS: Record<string, Platform> = { windows: "windows", linux: "linux", macos: "macos", mac: "macos", osx: "macos", android: "android" };

/**
 * The playlist filters that could show what a 3.x filter showed, the closest first: its platforms' tags as platforms
 * (any of them; or one, with all of one other tag), then its tags as tags (any of them). None for one that leaves
 * tags out, as a playlist can't.
 */
function rulesFor(f: OldFilter): Pick<Collection, "tags" | "platforms">[] {
  if (!f.tags.length || f.exclude.length) return [];
  const platforms = [...new Set(f.tags.flatMap((t) => PLATFORM_TAGS[t.toLowerCase()] ?? []))];
  const tags = f.tags.filter((t) => !PLATFORM_TAGS[t.toLowerCase()]);
  const rules: Pick<Collection, "tags" | "platforms">[] = [];
  if (platforms.length && (f.all ? platforms.length === 1 && tags.length <= 1 : !tags.length)) rules.push({ tags, platforms });
  if (!f.all || f.tags.length === 1) rules.push({ tags: f.tags });
  return rules;
}

/**
 * Playlists for the player's 3.x tag filters, each showing the apps its filter showed. One a playlist's filters can
 * follow keeps following them (rulesFor), with any app it showed that they don't find here (one tagged with a
 * console, say) picked for it; when they'd find more than it showed, the next. Any other is a playlist of the apps
 * it showed, picked by hand, and none when it showed none.
 */
export function importedPlaylists(filters: OldFilter[], apps: ImportedApp[]): Pick<Collection, "name" | "tags" | "platforms" | "apps">[] {
  const has = (tags: string[], want: string[], all: boolean) => {
    const set = new Set(tags.map((t) => t.toLowerCase()));
    return all ? want.every((t) => set.has(t.toLowerCase())) : want.some((t) => set.has(t.toLowerCase()));
  };
  // As 3.x filtered (TagHelper.MatchesDisplayFilter).
  const shows = (f: OldFilter, tags: string[]) => (!f.tags.length || has(tags, f.tags, f.all)) && (!f.exclude.length || !has(tags, f.exclude, f.excludeAll));
  return filters.flatMap((f) => {
    const name = f.name.slice(0, 60);
    const showed = [...new Set(apps.filter((a) => shows(f, a.old)).map((a) => a.id))];
    for (const rule of rulesFor(f)) {
      const facts = (a: ImportedApp) => ({ tags: a.tags, consoles: [], installed: false, platforms: a.platforms });
      const found = new Set(apps.filter((a) => matches({ ...rule, consoles: [] }, facts(a))).map((a) => a.id));
      const picked = showed.filter((id) => !found.has(id));
      if ([...found].every((id) => showed.includes(id))) return [{ name, ...rule, ...(picked.length ? { apps: picked } : {}) }];
    }
    return showed.length ? [{ name, tags: [], apps: showed }] : [];
  });
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

/** Whether TV mode, the library and Browse's larger look, is on: turned on, or left to itself in Steam's Game Mode. */
export const tvModeOn = (settings: Pick<Settings, "tvMode">, config: Pick<Config, "gameMode">) =>
  settings.tvMode === "on" || (settings.tvMode !== "off" && Boolean(config.gameMode));

/** This computer's preferences; they don't sync. */
export type Settings = {
  /** What runs Windows programs on Linux, for every app without a runner of its own; unset, Automatic (a Proton, else Wine). */
  windowsRunner?: WindowsRunner;
  /** An app's page's Project details folded away, for every app. */
  projectDetailsClosed?: boolean;
  /** TV mode: on, off, or unset, on by itself in Steam's Game Mode. */
  tvMode?: "on" | "off";
  fullscreen?: boolean;
  /** The system's title bar over the window; unset, the window's buttons are in the top bar instead. */
  systemTitleBar?: boolean;
  /** Closing the window hides it in the tray, the launcher still running (tray.rs; Windows only). */
  closeToTray?: boolean;
  scale?: number;
  /** How many times as far a mouse wheel's turn scrolls, as in Quiver Launcher 3 (spatial.ts); unset, the system's. */
  scrollSpeed?: number;
  /** The wheel and the d-pad's steps scroll at once, not easing there. */
  smoothScrollOff?: boolean;
  /** A sound file played behind the launcher, looped (music.ts), as in Quiver Launcher 3. */
  music?: string;
  /** Its volume, 0 to 1; unset, music.ts's default. */
  musicVolume?: number;
  /** A picture behind the library's grid (library-background.tsx), as in Quiver Launcher 3. */
  libraryBackground?: string;
  /** Its opacity, 0 to 1; unset, library-background.tsx's default. */
  libraryBackgroundOpacity?: number;
  /** Light or dark; unset follows the system. */
  theme?: Theme;
  /** The language Quiver shows in (@quiverlauncher/i18n); unset follows the system. */
  language?: Language;
  /** New installs get `portable.txt` in their folder, beside what the catalog asks for. */
  portableByDefault?: boolean;
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
  /** How Browse's cards look and which details they show (browse-cards.ts); only what differs from the defaults. */
  browseCards?: { look?: string; details?: string[] };
  /** Apps hidden from Browse here (browse-hidden.ts); apart from `hidden`, the library's. */
  browseHidden?: HiddenApp[];
  /** How the library is sorted (sort.ts); unset is recently played. */
  librarySort?: string;
  /** The library's tab, when it's one kept (library.tsx keepsTab); unset is All. */
  libraryTab?: string;
  /** The library's filters (library.tsx keptFilters); unset is none. */
  libraryFilters?: Record<string, string>;
  /** The platforms whose apps the library shows here (platforms.ts); unset is this device's own. */
  platforms?: string[];
  /** How the library shows its apps (view.ts); only what differs from the defaults. */
  library?: Partial<LibraryView>;
  /** The Sidebar layout's sidebar as wide as the player dragged it, in the page's pixels; unset fits the window. */
  sidebarWidth?: number;
  /** Anonymous usage data turned off here (telemetry.ts); signed in, the account's setting wins. */
  telemetryOff?: boolean;
  /** The player said yes to usage data here, which it needs first in some countries (telemetry.ts). */
  telemetryConsent?: boolean;
  /** The first-run notice about usage data was answered. */
  telemetryNoticeSeen?: boolean;
  /** Quiver Launcher has started here before. */
  launched?: boolean;
  /** The first-run notice offering to bring over a Quiver Launcher 3 library was answered. */
  oldLibraryNoticeSeen?: boolean;
  /** A library that was here already was looked over once for installs of Quiver Launcher 3's, and any offered were answered. */
  oldInstallsChecked?: boolean;
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

/** What usage data says about this copy of the launcher, and takes out of what it sends. */
export const telemetryContext = (config: Config): TelemetryContext => ({
  host: config.usageHost,
  version: config.version,
  os: config.os,
  arch: config.arch,
  home: config.home,
  user: config.user,
  appsDir: config.appsDir,
});

/** The launcher can't go on to the point where usage data starts by itself. */
export const startupFailed = () => native.config().then((config) => reportStartupFailure(telemetryContext(config)), () => {});

export function LauncherProvider({ children }: { children: ReactNode }) {
  const [saved, setSaved] = useState<Saved | null>(null);
  const [problems, setProblems] = useState<StateProblem[]>([]);
  const [broken, setBroken] = useState<Error | null>(null);
  useEffect(() => {
    // A file that can't be read isn't started over: the first save would replace what a snapshot could restore.
    native.stateProblems().then((found) => {
      if (found.length) {
        found.forEach((p) => captureError(new Error(`A saved file is ${p.problem}: ${p.name}`), { handled: "recovery" }));
        void startupFailed();
        return setProblems(found);
      }
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
        if (settings?.scale) settings.scale = nearestInterfaceSize(settings.scale);
        setSaved({ config, library: library ?? [], installs: installs ?? {}, catalog: catalog ?? {}, settings: settings ?? {}, collections: collections ?? [], githubToken: token });
      });
    }).catch((e) => setBroken(e instanceof Error ? e : new Error(String(e))));
  }, []);
  // To the last-resort boundary (main.tsx), for its message and report, not a window left blank.
  if (broken) throw broken;
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
  const [addingFiles, askAddFiles] = useState<Entry | null>(null);
  const [changingVersion, changeVersion] = useState<Entry | null>(null);
  const [choosingProgram, chooseProgram] = useState<string | null>(null);
  const [oldLibrary, setOldLibrary] = useState<V3Library | null | undefined>(undefined);
  const [oldInstalls, setOldInstalls] = useState<Record<string, Install> | null>(null);
  const [notice, showNotice] = useState<Launcher["notice"]>(null);
  const setNotice = useMemo<Launcher["setNotice"]>(() => (text, options) => showNotice(text === null ? null : { text, ...options }), []);
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
    if (saved.library.length) {
      setOldLibrary(null);
      // Unless it came over before Quiver could see all of 3.x's installs: asked once, and again if the catalog can't be read.
      if (!saved.settings.oldInstallsChecked)
        native
          .findV3Library()
          .then(async (old) => {
            const ids = old?.apps.some((a) => a.dir) ? await catalogRepositories() : new Map<string, { id: string }>();
            const loose = looseInstalls(old?.apps ?? [], ids, saved.library, saved.installs, saved.catalog, saved.config.os);
            if (Object.keys(loose).length) setOldInstalls(loose);
            else setSettings((s) => ({ ...s, oldInstallsChecked: true }));
          })
          .catch(() => {});
    } else native.findV3Library().then(setOldLibrary, () => setOldLibrary(null));
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
  const tv = tvModeOn(settings, config);
  // Again as the window changes: TV mode's going full screen, or the launcher moving to another screen.
  useEffect(() => {
    const apply = () => document.documentElement.style.setProperty("zoom", String(pageZoom(settings.scale ?? 1, tv, window.innerWidth, window.innerHeight)));
    apply();
    window.addEventListener("resize", apply);
    return () => window.removeEventListener("resize", apply);
  }, [settings.scale, tv]);
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
    setScrolling(settings);
    setMusic(settings);
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
          (releases) => setCatalog((c) => ({ ...c, [item.id]: { ...customEntry(item.id, custom, latestOf(releases), art), forHere: c[item.id]?.forHere } })),
          () => setCatalog((c) => (c[item.id] ? c : { ...c, [item.id]: customEntry(item.id, custom, undefined, art) })),
        );
        continue;
      }
      client.app(item.slug).then(
        (d) => setCatalog((c) => ({ ...c, [item.id]: { ...d.entry, withdrawn: d.withdrawn, forHere: c[item.id]?.forHere } })),
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
      const update = updateFor(entry, install);
      const waits = update && (waiting.current.get(`${item.id}@${bare(update.version)}`) ?? 0) > Date.now();
      if (entry && install?.updates === "auto" && !install.installer && !jobs[item.id] && hasUpdate(entry, install) && !skipped(entry, install) && !waits)
        void installEntry(entry, undefined, { auto: true });
    }
  }, [catalog]); // eslint-disable-line react-hooks/exhaustive-deps

  /** The newest of an app's releases this computer can install: for an install, of the build it has. */
  const releaseHere = (entry: Entry, list: Release[], custom: boolean, was?: Install, platform?: Os) => {
    const pulled = catalog[entry.id]?.withdrawn ?? [];
    const listed = list.filter((r) => !pulled.some((w) => bare(w.version) === bare(r.version)));
    // A custom app takes its newest that isn't a pre-release, as `latestOf`; a file it picked that's named differently now, it picks again.
    const pool = custom && listed.some((r) => !r.prerelease) ? listed.filter((r) => !r.prerelease) : listed;
    return releaseFor(pool, config.os, config.arch, {
      filter: custom ? undefined : (r) => (r.installationOverride ?? entry.launcher).releaseAssetFilter,
      kind: was ? (was.installer === "msi" ? "msi" : "files") : undefined,
      platform: platform ?? (was ? (was.wine ? "windows" : config.os) : undefined),
    });
  };

  // The catalog's verified release may only have another system's build: which release an install here can update to
  // is for its releases to say. Asked once for each verified release, only of apps it's newer than what's installed.
  const lookingUp = useRef(new Set<string>());
  useEffect(() => {
    for (const item of library) {
      const entry = catalog[item.id];
      const install = installs[item.id];
      if (!entry || !install || !mayUpdate(entry, install)) continue;
      const of = forHereKey(entry.verified!, install);
      const asked = `${item.id}@${of}`;
      if (entry.forHere?.of === of || lookingUp.current.has(asked)) continue;
      lookingUp.current.add(asked);
      const custom = isCustom(item.id) ? (item.custom ?? fromKey(item.id)) : null;
      (custom ? releasesOf(custom) : client.releases(entry.slug, 100).then((p) => p.items))
        .then((list) => {
          const found = releaseHere(entry, list, Boolean(custom), install);
          const release = found && { version: found.version, releasedAt: found.releasedAt };
          setCatalog((c) => (c[item.id] ? { ...c, [item.id]: { ...c[item.id], forHere: { of, ...(release ? { release } : {}) } } } : c));
        })
        // Offline, or the site's down: no update shows, and it's asked again when something next changes.
        .catch(() => {})
        .finally(() => lookingUp.current.delete(asked));
    }
  }, [catalog, installs, library, client]); // eslint-disable-line react-hooks/exhaustive-deps

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
    const portableByDefault = settings.portableByDefault && config.os !== "android" && config.os !== "ios";
    let version = chosen?.version ?? entry.verified?.version;
    let format: string | undefined;
    // A release Quiver hasn't verified, or a file of one it has, that the player said to install.
    let unverified = Boolean(options.unverified);
    // What was downloaded, for telling the site when its file was gone or wasn't the one Quiver checked.
    let tried: { release: Release; asset: Asset } | undefined;
    const custom = isCustom(entry.id) ? (app ?? allItems.find((i) => i.id === entry.id)?.custom ?? fromKey(entry.id)) : null;
    // An update installs the same kind as before: a Windows installer's setup, or files in the app's folder.
    const kind = was ? (was.installer === "msi" ? "msi" : "files") : undefined;
    // The system whose build it runs: the player's choice, else the one installed, so an update keeps to Windows' build
    // through Wine. Switching drops the file picked for the other one.
    const platform = options.platform ?? (was?.wine ? "windows" : undefined);
    const lastPick = options.platform && was && options.platform !== (was.wine ? "windows" : config.os) ? undefined : was?.pick;
    const noAnswer = () => new Problem(msg`Couldn't reach quiverlauncher.com. Check your connection and try again.`);
    const stop = () => {
      setJobs(({ [entry.id]: _, ...rest }) => rest);
      track("app_install_cancelled", { ...ref, version: version ?? null });
    };
    track("app_install_started", { ...ref, version: version ?? null, update: Boolean(from), verified: !unverified, blocked: Boolean(blocked) });
    try {
      // The newest this computer can install, which may not be the newest there is: what a new install starts from, and an update goes to.
      const listed = chosen
        ? undefined
        : custom
          ? await releasesOf(custom)
          : (await client.releases(entry.slug, 100).catch(() => {
              throw noAnswer();
            })).items;
      const here = listed && releaseHere(entry, listed, Boolean(custom), was, options.platform);
      let release = chosen ?? here ?? (custom ? latestOf(listed!) : listed![0]);
      // An update with no release for the build installed: never another system's file in its place.
      const stray = Boolean(was && listed?.length && !here);
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
      // The file the player picked on the app's Releases tab: that one, unless it isn't the kind installed.
      const clicked = options.asset ? release.assets.find((a) => a.filename === options.asset) : undefined;
      let candidates = stray
        ? []
        : clicked
          ? !kind || isMsi(clicked.filename) === (kind === "msi") ? [clicked] : []
          : bestAssets(release.assets, config.os, config.arch, settings.releaseAssetFilter, kind, platform);
      // A file the player picked that a later release names differently: they pick again, even from one.
      const missed = !stray && !candidates.length && !clicked && Boolean(custom && settings.releaseAssetFilter);
      if (missed) candidates = bestAssets(release.assets, config.os, config.arch, undefined, kind, platform);
      let [asset, ...others] = candidates;
      // An automatic update never stops to ask: it waits until the launcher starts again, or the player updates it.
      const waitForPlayer = (until = Infinity) => {
        waiting.current.set(`${entry.id}@${bare(release!.version)}`, until);
        setJobs(({ [entry.id]: _, ...rest }) => rest);
      };
      if (!asset && kind && bestAssets(release.assets, config.os, config.arch).length && !bestAssets(release.assets, config.os, config.arch, undefined, kind).length)
        throw new Problem(
          kind === "msi"
            ? msg`This release has no Windows installer (MSI) to update it with. Uninstall it, then get it again to install this release's download.`
            : msg`This release only comes as a Windows installer (MSI). Uninstall it, then get it again to install it that way.`,
        );
      // None marked for this computer: the player picks from the files there are, as they may know which runs.
      const unmatched = !asset;
      if (unmatched && !stray) [asset, ...others] = installable(release.assets, config.os).filter((a) => !kind || isMsi(a.filename) === (kind === "msi"));
      if (!asset) {
        if (auto) return waitForPlayer();
        throw new Problem(msg`This release has no download for your computer.`);
      }
      // The file the player picked last time, when it's still one of these: taken again without asking.
      const again = pickedBefore([asset, ...others], lastPick);
      let pick = lastPick;
      // Picked by name: later releases keep to it, as to a file picked from several.
      if (clicked) {
        const files = installable(release.assets, config.os).map((a) => a.filename);
        pick = files.length > 1 ? assetFilterFor(clicked.filename, files) : undefined;
      } else if (again) asset = again;
      else if (others.length || missed || unmatched) {
        if (auto) return waitForPlayer();
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
        if (auto) return waitForPlayer(asset.checkEndsAt ?? Infinity);
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
      const catalogFiles = settings.filesToAdd ?? [];
      const files = installFiles(was, catalogFiles, portableByDefault);
      const done = await native.install({
        id: entry.id,
        url: asset.url,
        filename: asset.filename,
        // A file still in its own wait is checked against what Quiver saw of it, as any other.
        checksum: asset.checksum ?? asset.unverifiedChecksum,
        folder,
        dir: was?.dir,
        filesToAdd: files ?? catalogFiles,
        version: release.version,
        preferred: executables,
        program: was?.installer === "msi" ? was.program : undefined,
      });
      // A Windows installer's program, when Quiver can't tell which it is: the player says, as in Quiver Launcher 3.
      const msi = done.installer === "msi";
      const program = msi ? (done.program ?? (await native.pickProgram(done.location ?? undefined).catch(() => null)) ?? undefined) : undefined;
      const name = entry.projectName;
      if (done.restart) setNotice(t`${name} is installed. Windows says to restart the computer to finish setting it up.`, { stays: true });
      // Picking a version only installs it; staying on it is the player's own choice (`pin`).
      // Going back from the verified release: Auto Update waits for the next one rather than undoing it.
      const known = catalog[entry.id] ?? entry;
      const newest = (was && known.verified && known.forHere?.of === forHereKey(known.verified, was) && known.forHere.release) || known.verified;
      const skip = chosen && newest && release.releasedAt < newest.releasedAt ? newest.version : undefined;
      // The same version again keeps what the player said about the next one.
      const declined = was?.declined && bare(was.version) === bare(release.version) ? was.declined : undefined;
      // The file's own, which tells it from the one its release is verified with later.
      const checksum = done.checksum ?? asset.checksum ?? asset.unverifiedChecksum;
      setInstalls((i) => {
        const updates = pin ? "pinned" : i[entry.id]?.updates;
        // Installed now, or as long ago as it was before this update (an install from before there was a time, none).
        const installedAt = i[entry.id] ? i[entry.id].installedAt : Date.now();
        // The other system's build in place of the one there was: the programs the player picked, and what ran them,
        // were the old build's. Its command line stays.
        const wine = asset.os === "windows" && config.os === "linux";
        const old = i[entry.id]?.launch;
        const launch = old && Boolean(i[entry.id]?.wine) !== wine ? (old.command ? { command: old.command } : undefined) : old;
        return { ...i, [entry.id]: { dir: i[entry.id]?.dir, lastPlayed: i[entry.id]?.lastPlayed, ...(installedAt ? { installedAt } : {}), playTime: i[entry.id]?.playTime, ...(launch ? { launch } : {}), ...(updates ? { updates } : {}), ...(skip ? { skip } : {}), ...(declined ? { declined } : {}), ...(unverified ? { unverified: true } : {}), ...(blocked ? { blocked } : {}), ...(checksum ? { checksum } : {}), ...(pick ? { pick } : {}), ...(files ? { files } : {}), version: release.version, releasedAt: release.releasedAt, releaseId: release.id, folder, executables: msi ? [] : executables, ...(msi ? { installer: "msi" as const, ...(program ? { program } : {}) } : {}), ...(wine ? { wine: true } : {}) } };
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
      captureFailure("install", problem, { ...ref, version: version ?? null, update: Boolean(from), format: format ?? null });
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

  /** Every app the catalog lists, with its repository. */
  async function catalogStatuses() {
    const all: ReleaseStatus[] = [];
    for (let cursor: string | null = null, done = false; !done; ) {
      const page = await client.releaseStatus(cursor);
      all.push(...page.items);
      [cursor, done] = [page.nextCursor, page.isDone];
    }
    return all;
  }

  /** Repositories the catalog lists, to send them to the catalog's app instead. */
  async function catalogRepositories() {
    const ids = new Map<string, { id: string; slug: string }>();
    for (const s of await catalogStatuses()) {
      if (s.repository) ids.set(`${s.provider}:${s.repository}`.toLowerCase(), s);
      // And by its id, as Quiver Launcher 3 kept it (importOld), under a key no provider has.
      ids.set(`id:${s.id}`, s);
    }
    return ids;
  }

  /** The catalog's apps for each of `statuses`, as far as the site says. */
  const entriesOf = async (statuses: ReleaseStatus[]) =>
    (await Promise.all(statuses.map((s) => client.app(s.slug).catch(() => null)))).flatMap((detail) => (detail ? [detail.entry] : []));

  /** The catalog's apps for a repository, or a preview of its latest release. */
  async function lookUpRepository(input: string): ReturnType<Launcher["lookUpRepository"]> {
    const repository = parseRepository(input);
    if (!repository) return { error: t`Enter a GitHub or GitLab repository: owner/name, or its github.com or gitlab.com address.` };
    // Already added: its page.
    const key = customKey(repository.provider, repository.repository);
    if (library.some((i) => i.id === key) && catalog[key]) return { listed: [catalog[key]] };
    try {
      const listed = await entriesOf(appsOfRepository(await catalogStatuses().catch((): ReleaseStatus[] => []), repository));
      if (listed.length) return { listed };
      const latest = latestOf(await releasesOf(repository));
      const app: CustomApp = { ...repository, name: repository.repository.split("/").at(-1)! };
      return { preview: { app, latest, files: latest ? bestAssets(latest.assets, config.os, config.arch) : [] } };
    } catch (e) {
      return { error: e instanceof Error ? e.message : String(e) };
    }
  }

  // Fetched once a session, for the pages of apps the player added; again after a failure.
  const listed = useRef<ReturnType<typeof catalogStatuses> | null>(null);
  const statuses = () =>
    (listed.current ??= catalogStatuses().catch((e) => {
      listed.current = null;
      throw e;
    }));
  const [switched, setSwitched] = useState<Record<string, string>>({});
  const [statusList, setStatusList] = useState<ReleaseStatus[]>([]);
  // The repositories the player added that the catalog lists now, for a mark on their cards; only asked with one to ask about.
  const hasCustom = library.some((i) => i.custom);
  useEffect(() => {
    if (!hasCustom) return;
    let live = true;
    statuses().then(
      (all) => live && setStatusList(all),
      () => {},
    );
    return () => void (live = false);
  }, [hasCustom]); // eslint-disable-line react-hooks/exhaustive-deps
  const inCatalog = useMemo(
    () => new Set(library.flatMap((i) => (i.custom && appsOfRepository(statusList, i.custom).some((s) => !library.some((l) => l.id === s.id)) ? [i.id] : []))),
    [library, statusList],
  );
  // The pictures nothing uses any more: replaced, or their app removed. Once a run, as the library was saved at the last.
  useEffect(() => {
    void native.keepArtwork(artFiles(allItems.map((i) => i.overrides?.pictures))).catch(() => {});
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

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
    addToLibrary(entry) {
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      add(entry);
      const name = entry.projectName;
      setNotice(t`${name} is in your library.`);
    },
    setUpdates: (id, updates) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], updates } } : i)),
    declineUpdate: (id, version) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], declined: version } } : i)),
    setLaunch: (id, launch) =>
      setInstalls((i) => {
        if (!i[id]) return i;
        const { launch: _, ...install } = i[id];
        const set = Object.fromEntries(Object.entries(launch).filter(([, v]) => v !== undefined && v !== "")) as LaunchOptions;
        return { ...i, [id]: Object.keys(set).length ? { ...install, launch: set } : install };
      }),
    setFiles: (id, files) => setInstalls((i) => (i[id] ? { ...i, [id]: { ...i[id], files } } : i)),
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
      return app ? entriesOf(appsOfRepository(await statuses(), app)) : [];
    },
    inCatalog,
    setGame(id, art) {
      setLibrary((l) => l.map((i) => (i.id === id ? { ...i, art: art ?? undefined } : i)));
      setCatalog((c) => (c[id] ? { ...c, [id]: withGame(c[id], art) } : c));
    },
    switchToCatalog(id, entry) {
      const old = library.find((i) => i.id === id);
      if (!(old?.custom || old?.local) || library.some((i) => i.id === entry.id)) return;
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
        // A local app's files stay the player's own, under the catalog's name for them.
        if (was.local) return { ...rest, [entry.id]: was };
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
    customize: (id, { name, cover, pictures }) =>
      setLibrary((l) =>
        l.map((i) => {
          if (i.id !== id) return i;
          const was = i.overrides ?? {};
          const pending = i.account
            ? { ...i.pending, ...(name !== was.name ? { name: true } : {}), ...(cover !== was.cover ? { cover: true } : {}) }
            : i.pending;
          return { ...i, overrides: { ...was, name, cover, pictures }, pending };
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
        return setNotice(t`${version} of ${name} was withdrawn: ${reason} Choose another version from its page.`, { stays: true });
      }
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      add(entry);
      await installEntry(entry, release, { pin: options?.pin, unverified: options?.unverified || Boolean(options?.blocked), blocked: options?.blocked, asset: options?.asset, platform: options?.platform });
    },
    async play(id, executable) {
      const install = installs[id];
      if (!install) return;
      // The programs the player picked for Play: one starts; more, and they choose.
      const programs = install.launch?.programs ?? [];
      if (!executable && programs.length > 1) return chooseProgram(id);
      const chosen = executable ?? programs[0];
      const launch = { ...install.launch, ...(chosen ? { executable: chosen } : {}), windowsRunner: settings.windowsRunner };
      let program = install.program;
      // A Windows installer's program the player didn't choose after its setup: they choose it now.
      if (install.installer === "msi" && !program) {
        program = (await native.pickProgram().catch(() => null)) ?? undefined;
        if (!program) return;
      }
      setInstalls((i) => ({ ...i, [id]: { ...i[id], lastPlayed: Date.now(), ...(program ? { program } : {}) } }));
      const ref = appRef(id, catalog[id]?.slug ?? allItems.find((i) => i.id === id)?.slug ?? "");
      await native.launch(id, install.folder, install.dir, install.executables, Boolean(install.wine), program, launch).then(
        () => (musicForGame(), track("app_launched", ref)),
        (e) => (fail(id, e), track("app_launch_failed", { ...ref, reason: reasonOf(e) }), captureFailure("launch", e, { ...ref, wine: Boolean(install.wine) })),
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
        captureFailure("reinstall", e, appRef(id, entry.slug));
      }
    },
    async remove(id) {
      // Saying no when Windows asks to uninstall it keeps it, in the library too.
      if (await uninstall(id).then(() => false, (e) => (captureFailure("remove", e, appRef(id, catalog[id]?.slug ?? "")), e === "cancelled"))) return;
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
        (e) => void (e !== "cancelled" && (fail(id, e), captureFailure("uninstall", e, appRef(id, catalog[id]?.slug ?? "")))),
      ),
    oldLibrary,
    findOld: (path, apps) => native.findV3LibraryIn(path, apps),
    oldInstalls,
    answerOldInstalls(use) {
      const loose = oldInstalls;
      setOldInstalls(null);
      setSettings((s) => ({ ...s, oldInstallsChecked: true }));
      // Anything installed here since it was found stays.
      if (use && loose) setInstalls((i) => ({ ...loose, ...i }));
    },
    async locateInstall(entry) {
      const name = entry.projectName;
      const was = installs[entry.id];
      const path = await native.pickFolder(t`Choose the folder ${name} is installed in`);
      if (!path) return;
      const found = await native.findExistingInstall(path).catch(() => null);
      const install = found && adoptedInstall({ name, provider: "github", dir: path, version: found.version, program: found.program ?? undefined }, catalog[entry.id] ?? entry, config.os);
      if (!install) return setNotice(t`That folder doesn't have an installed app in it.`, { stays: true });
      // From its page it may not be in the library yet.
      setCatalog((c) => ({ ...c, [entry.id]: { ...c[entry.id], ...entry } }));
      add(entry);
      // Anything installed here while the player was choosing stays.
      setInstalls((i) => ({ ...i, [entry.id]: i[entry.id] === was ? relocatedInstall(was, install) : i[entry.id] }));
      // What it failed with is over, not a download that's started since.
      setJobs(({ [entry.id]: job, ...rest }) => (job && !("error" in job) ? { ...rest, [entry.id]: job } : rest));
    },
    /**
     * Adds the 3.x apps, keeping their installs: the catalog's as the catalog has them (found by repository, or by
     * the entry 3.x knew), any other from GitHub or GitLab as a custom app. Returns the names of those it couldn't:
     * ones 3.x didn't say where they're from. The player's tag filters come over as playlists.
     */
    async importOld({ apps, filters = [] }, onProgress) {
      const ids = await catalogRepositories();
      let done = 0;
      // Each asks the site, or its repository's host, so several at a time; then they're added in the library's order.
      const found = await inTurns(apps, 6, async (old) => {
        const match = catalogMatch(old, ids);
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
      const tags = new Map<string, string[]>();
      const hidden: string[] = [];
      const imported: ImportedApp[] = [];
      apps.forEach((old, i) => {
        const it = found[i];
        if (!it) return void missing.push(old.name);
        let entry: Entry;
        if (it.kind === "catalog") {
          const { detail } = it;
          entry = detail.entry;
          setCatalog((c) => ({ ...c, [detail.entry.id]: { ...detail.entry, withdrawn: detail.withdrawn, forHere: c[detail.entry.id]?.forHere } }));
          add(entry);
        } else entry = addCustom(it.custom, it.latest, it.art);
        const install = adoptedInstall(old, entry, config.os);
        if (install) setInstalls((i) => ({ ...i, [entry.id]: keptInstall(i[entry.id], install) }));
        const own = importedTags(old, entry);
        if (own.length) tags.set(entry.id, own);
        if (old.hidden) hidden.push(entry.id);
        const have = library.find((l) => l.id === entry.id)?.overrides?.tags ?? [];
        imported.push({ id: entry.id, old: old.tags ?? [], tags: [...entry.tags, ...have, ...own], platforms: entry.supportedOS });
      });
      // Their tags join any the app has here already, and the apps they hid in 3.x stay hidden.
      if (tags.size)
        setLibrary((l) =>
          l.map((i) => {
            const own = tags.get(i.id);
            if (!own) return i;
            const have = i.overrides?.tags ?? [];
            const all = [...have, ...own.filter((t) => !have.some((h) => h.toLowerCase() === t.toLowerCase()))].slice(0, 20);
            return all.length === have.length ? i : { ...i, overrides: { ...i.overrides, tags: all }, pending: i.account ? { ...i.pending, tags: true } : i.pending };
          }),
        );
      if (hidden.length) setSettings((s) => ({ ...s, hidden: [...new Set([...(s.hidden ?? []), ...hidden])] }));
      // Their tag filters, as playlists; one named as a playlist here already leaves that one as it is.
      const names = new Set(collections.map((c) => c.name.toLowerCase()));
      const playlists = importedPlaylists(filters, imported).filter((p) => !names.has(p.name.toLowerCase()) && names.add(p.name.toLowerCase()));
      if (playlists.length)
        setCollections((all) => [
          ...all,
          ...playlists.map((p, i) => ({ key: newKey(), consoles: [], ...p, order: collections.length + i, account: userId, pending: userId ? true : undefined })),
        ]);
      // The player brought a library over: the one found by itself isn't offered again.
      setOldLibrary(null);
      setOldInstalls(null);
      return missing;
    },
    dismiss: (id) => setJobs(({ [id]: _, ...rest }) => rest),
    appFolders,
    renaming,
    askRename,
    addingFiles,
    askAddFiles,
    changingVersion,
    changeVersion,
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
