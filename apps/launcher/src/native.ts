/** The Rust side of the launcher (src-tauri/src/lib.rs). */
import { invoke as tauriInvoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { currentMonitor, getCurrentWindow } from "@tauri-apps/api/window";
import type { GitlabLimit, ModSettings, ModSource, Os, SetupFile } from "@quiverlauncher/api";
import { noteFailure } from "./command-failures";
import type { CacheUse } from "./cache";

/** Tauri's, remembering which command failed; the error is the same one, for those who check what it says. */
const invoke = <T>(...[command, args, options]: Parameters<typeof tauriInvoke>) =>
  tauriInvoke<T>(command, args, options).catch((e) => {
    noteFailure(command, e);
    throw e;
  });

type StateFile = "library" | "installs" | "catalog" | "settings" | "collections";
/** An app in a Quiver Launcher 3 library, with its installed copy if any. */
export type OldApp = {
  name: string;
  repository?: string;
  provider: string;
  /** The catalog entry 3.x knew it as, for when its repository moved. */
  catalogId?: string;
  dir?: string;
  /** Set with `dir`: 0.0.0, as 3.x had it, when 3.x didn't know it. */
  version?: string;
  /** The program a Windows installer (MSI) put on the computer, for an app 3.x installed that way. */
  program?: string;
  /** Its copy is in one of 3.x's apps folders, not a folder the player chose for it alone. */
  inApps?: boolean;
  /** Its tags as 3.x showed them, the catalog's among them. */
  tags?: string[];
  /** The catalog's tags when 3.x last followed its entry. */
  catalogTags?: string[];
  /** The player hid it in 3.x's library. */
  hidden?: boolean;
  /** Its picture in 3.x: a file on this computer the player chose, or an https address. */
  icon?: string;
};
/** A file or folder to ask for in the page's own browser (file-picker.tsx). */
export type PickAsk = { id: number; folder: boolean; title: string; dir?: string | null; extensions: string[] };
/** A folder's folders and files, for the page's browser, with places to start from. */
export type Listing = {
  path: string;
  parent?: string | null;
  entries: { name: string; folder: boolean }[];
  places: { name: string; path: string }[];
};
/** A Quiver Launcher 3 library: the folder its apps.json is in, and its apps. */
/** A tag filter the player made in 3.x: apps with any of its tags (all, with `all`), less those with any of `exclude` (all, with `excludeAll`). */
export type OldFilter = { name: string; tags: string[]; all: boolean; exclude: string[]; excludeAll: boolean };
/** `moved`: a folder 3.x's files name for its installs is gone. */
export type V3Library = { folder: string; apps: OldApp[]; filters?: OldFilter[]; moved?: boolean };
export type Config = {
  api: string;
  convex: string;
  accountApi?: string;
  githubApi: string;
  gitlabApi: string;
  returnTo: string;
  os: Os;
  arch: string;
  appsDir: string;
  /** The download cache's folder unless the player picks another (cache.ts): `cache` in the data folder. Missing from an older build. */
  cacheDir?: string;
  /** Where anonymous usage data goes (telemetry.ts). */
  usageHost: string;
  version: string;
  /** The home folder and user name, which usage data never contains. */
  home: string | null;
  user: string | null;
  /** Under gamescope, as a Steam Deck's Game Mode runs it: TV mode comes on by itself. Missing from an older build. */
  gameMode?: boolean;
  /** An X11 desktop on Linux, where the page moves the window itself (window-bar.tsx). Missing from an older build. */
  x11?: boolean;
  /** Steam's on-screen keyboard can be brought up for a text field (keyboard.rs). Missing from an older build. */
  steamKeyboard?: boolean;
  /** Steam is running Quiver, so Quiver can't add games to it now (lib.rs). Missing from an older build. */
  steamRunsUs?: boolean;
  /** The system's version, for usage data (device.rs). Missing from an older build. */
  osVersion?: string | null;
  /** A Steam Deck, for usage data. Missing from an older build. */
  steamDeck?: boolean;
  /** How this copy was installed, for usage data (update.rs). Missing from an older build. */
  package?: string;
};
/** The hardware specs.rs reads. */
export type PcSpecs = { cpu: string | null; gpu: string | null; ramGb: number | null };
/** A repository host's answer, fetched by the Rust side for hosts that don't send CORS headers. */
export type ProviderHttpResponse = { status: number; headers: Record<string, string>; body: string };
export type Progress = {
  id: string;
  phase: "downloading" | "installing";
  received: number;
  total: number | null;
  /** Installing in the app's own setup window (a Windows installer). */
  setup?: true;
};
export type InstallRequest = {
  id: string;
  url: string;
  filename: string;
  checksum?: string;
  folder: string;
  dir?: string;
  /** A new install goes in a folder of its own in here (one the player chose) instead of the apps folder. */
  into?: string;
  filesToAdd: string[];
  version: string;
  /** The catalog's preferred programs, for finding the one a Windows installer installed. */
  preferred?: string[];
  /** The program the app started with before an update. */
  program?: string;
  /** The player's GitHub token, with `url` a file of a GitHub repository asked for through its API (githubAsset). */
  githubToken?: string;
  /** The file's browser link, downloaded without the token when GitHub refuses the token (expired or revoked). */
  fallbackUrl?: string;
  /** The download cache, while it's on (cache.ts): the file comes from it when it's there, and is kept in it as it says. */
  cache?: CacheUse;
};
/** A file in the download cache (cache.rs). */
export type CachedFile = { hash: string; filename: string; path: string; size: number; apps: { app: string; version: string; os?: string; slug?: string }[]; cachedAt: number; usedAt: number };
export type CacheStatus = { files: number; bytes: number; entries: CachedFile[] };
/** What an install did. A Windows installer (MSI) puts its files where its setup chose. */
export type Installed = {
  dir: string;
  version: string;
  /** The downloaded file's `sha256:<hex>`. Missing from the web side's stand-in for the Rust side. */
  checksum?: string;
  /** "msi" for an MSI package; "setup" for a setup program that installed outside the app's folder. */
  installer: "msi" | "setup" | null;
  /** The program a Windows installer installed, when the launcher could tell. */
  program: string | null;
  /** Where it installed, to start looking from when the player chooses its program. */
  location: string | null;
  /** Windows wants a restart to finish. */
  restart: boolean;
  /** A setup program installed it, in the app's folder or outside it. Missing from the web side's stand-in. */
  setup?: boolean;
};

/** A game as Discord shows it while it runs: its name, and a picture Discord can fetch from the web. */
/** A window's place and size, in the screen's pixels. */
export type Rect = { x: number; y: number; width: number; height: number };
export type WindowRect = Rect & { minWidth: number; minHeight: number };
export type DiscordGame ={ name: string; image?: string };

/** How the player wants an app started, when the launcher's own choice isn't right. */
export type LaunchOptions = {
  /** The program to start: inside the app's folder (relative), or anywhere else by its full path, such as a game its installer put on another drive. */
  executable?: string;
  runner?: { kind: "direct" } | { kind: "wine" } | { kind: "proton"; path: string };
  /** What Settings runs every Windows program with on Linux, where the app has no runner of its own (never saved with an app). */
  windowsRunner?: WindowsRunner;
  /** As Steam takes them: `NAME=value` first sets the environment, `%command%` is where the game goes, otherwise arguments. */
  command?: string;
  /** The programs Play starts, relative to the app's folder: with more than one, Play asks which. The launcher itself reads only `executable`. */
  programs?: string[];
  /** The player's copies of the game that it's told about as an argument or environment variable (own-copy.ts); never saved. */
  copies?: LaunchCopy[];
};
/** One of the player's copies, and how the game is told where it is: after `flag`, on its own (`positional`), or in `env`. */
export type LaunchCopy = { flag?: string; positional?: boolean; env?: string; path: string };
/** What checking the player's copy found (own_copy.rs). */
export type CheckedCopy = {
  ok: boolean;
  /** It matched a dump the catalog lists, not just its kind of file. */
  verified: boolean;
  dump?: string | null;
  problem?: string | null;
  size: number;
  sha1?: string | null;
};
/** How far checking or putting a copy in place has got. */
export type CopyProgress = { id: string; step: "check" | "place"; done: number; total: number };
export type Proton = { name: string; path: string };
/** An installed app to move: its folder in the apps folder, or one kept elsewhere (`dir`), as Quiver Launcher 3's are. */
/** `group`: the console folder it's in inside its apps folder ("N64"), which it keeps in the new one. */
export type MovingApp = { id: string; folder: string; dir?: string; group?: string };
/** How far a move has got: `done` of `total` apps, and `copied` of `size` bytes of `folder` while it copies to another drive. */
export type AppsMoving = { done: number; total: number; folder: string; copied: number; size: number };
/** What runs Windows programs on Linux, chosen in Settings for every app: unset, Automatic. */
export type WindowsRunner = { kind: "wine" } | { kind: "proton"; path: string };

/** An installed app, for a desktop or Steam shortcut. */
export type ShortcutRequest = {
  name: string;
  folder: string;
  dir?: string;
  preferred: string[];
  wine: boolean;
  /** A program the player picked, started exactly. */
  program?: string;
  launch?: LaunchOptions;
  art: { icon?: string; header?: string; capsule?: string; hero?: string; logo?: string };
};

export type ModFile = { id: string; name: string; url: string; size: number; description: string };
export type ModPackage = {
  /** `thunderstore:Owner-Name` or `gamebanana:123`. */
  key: string;
  provider: "thunderstore" | "gamebanana";
  name: string;
  author: string;
  description: string;
  version: string;
  updated: number;
  downloads: number;
  rating: number;
  icon: string | null;
  url: string;
  nsfw: boolean;
  dependencies: string[];
  files: ModFile[];
};
export type InstalledMod = { key: string; name: string; version: string; files: string[]; file: string };
/** A newer Quiver Launcher (`check_update`). */
export type LauncherUpdate = {
  version: string;
  notes: string;
  url: string;
  preview: boolean;
  /** Why it can't install itself here: the player gets the release page instead. */
  manual?: string | null;
};
/** A saved file the launcher won't start over (see `state_problems`). */
export type StateProblem = { name: StateFile; problem: "damaged" | "missing" };
/** A snapshot of the library, with when it was taken and how many apps it has. */
export type Snapshot = { id: string; at: number; apps: number };
/** What the controller used last calls its buttons (gamepad.rs): its kind, and its face buttons' labels by place. */
export type PadLabels = {
  kind: "xbox" | "playstation" | "nintendo" | "steam" | "other";
  south: string;
  east: string;
  west: string;
  north: string;
  /** Its presses come as Steam's desktop layout's keys (a Steam Deck's own controls in its desktop): A Enter, B Escape, Y Space, X Steam's keyboard. */
  desktop?: boolean;
};

/** An app, as the Rust side needs it named for its saves. */
export type SavesOf = {
  id: string;
  folder: string;
  dir?: string;
  wine: boolean;
  /** The catalog's places for its saves. */
  locations: { root: string; path: string; os?: string[] }[];
  version?: string;
  /** What it's called, to find folders named like it. */
  names: string[];
};
export type SaveBackup = {
  /** Its file's name, which names it to restore and remove. */
  name: string;
  at: number;
  size: number;
  files: number;
  version?: string | null;
  /** "played", "manual", "update", "uninstall" or "restore". */
  reason: string;
  device: string;
  /** The system its saves are for: "windows" for a Windows app run through Wine. */
  os: string;
  /** The checksum of the save files in it, which is how two copies are told apart. */
  hash: string;
};
/** An app's saves as cloud saves compares them (cloud-saves.ts). */
export type SavesCloud = {
  /** The checksum of the saves as they are now; none when there are none here. */
  hash?: string | null;
  /** Their checksum when they last matched the account's newest copy. */
  synced?: string | null;
  /** The player switched cloud saves off for this app. */
  off: boolean;
  os: "windows" | "linux" | "macos";
};
/** An app's saves beside its backups in the sync folder (sync-folder.ts). */
export type SavesFolder = {
  /** The checksum of the saves as they are now; none when there are none here. */
  hash?: string | null;
  /** Their checksum when they last matched the folder's newest backup. */
  synced?: string | null;
  os: "windows" | "linux" | "macos";
  /** False when the folder can't be found now: a drive that isn't plugged in. */
  there: boolean;
  /** The folder's backups of the app, newest first. The first arrived whole. */
  copies: SaveBackup[];
};
/** What the sync folder holds for one app. `label`: what its folder there is called. */
export type SyncFolderApp = { id: string; label: string; copies: number; size: number; newest: SaveBackup };
/** A place an app's saves are in: as a backup names it, and the folder that is on this computer. */
export type SavePlace = { template: string; path: string; exists: boolean };
export type SavesInfo = {
  places: SavePlace[];
  /** Who said where they are. */
  from: "you" | "catalog" | "found" | "none";
  backups: SaveBackup[];
  /** The player switched cloud saves off for this app. */
  cloudOff?: boolean;
  /** Signs that the places hold more than saves, for the tab to ask about (saves.rs `looks`). */
  look?: SavesLook | null;
};
/** Signs that a backup holds more than saves, and what filled it most. */
export type SavesLook = { reasons: ("big" | "many" | "media" | "tooBig")[]; bytes: number; files: number; kinds: { ext: string; share: number }[] };
/** What to tell the catalog about how an app's backups look; `key` marks it sent. */
export type SavesWarning = SavesLook & { os: "windows" | "linux" | "macos"; source: "catalog" | "found" | "player"; fine: boolean; key: string };

export const native = {
  /** Whether holding these buttons together closes the game being played (unset: Start + Select). */
  closeCombo: (on: boolean, buttons?: string[]) => invoke<void>("close_combo", { on, buttons: buttons ?? null }),
  config: () => invoke<Config>("config"),
  /** The processor, graphics card and memory, for feedback (specs.rs); what can't be read is null. Missing from an older build, which rejects. */
  pcSpecs: () => invoke<PcSpecs>("pc_specs"),
  readState: <T>(name: StateFile) => invoke<T | null>("read_state", { name }),
  writeState: (name: StateFile, value: unknown) => invoke<void>("write_state", { name, value }),
  stateProblems: () => invoke<StateProblem[]>("state_problems"),
  listSnapshots: () => invoke<Snapshot[]>("list_snapshots"),
  restoreSnapshot: (id: string) => invoke<void>("restore_snapshot", { id }),
  startEmptyLibrary: () => invoke<void>("start_empty_library"),
  /** Fails with "cancelled" when the player stops it, "missing" when its file is gone (a 404), or "mismatch" when the download isn't the file its checksum names. */
  install: (request: InstallRequest) => invoke<Installed>("install", { request }),
  /** Downloads a release file into the download cache only (cache.ts). Fails with "cancelled", "missing" or "mismatch" as an install does. */
  cacheFetch: (request: InstallRequest) => invoke<void>("cache_fetch", { request }),
  cacheFetchCancel: () => invoke<void>("cache_fetch_cancel"),
  onCacheProgress: (handler: (p: { id: string; received: number; total?: number | null }) => void) =>
    listen<{ id: string; received: number; total?: number | null }>("cache-progress", (e) => handler(e.payload)),
  /** What's in the cache. `names`, apps' ids to their slugs, names the folders of files an older build kept by hash, which move to them. */
  cacheStatus: (dir: string, names?: Record<string, string>) => invoke<CacheStatus>("cache_status", { dir, names: names ?? null }),
  cacheHas: (dir: string, checksums: string[]) => invoke<boolean[]>("cache_has", { dir, checksums }),
  /** Clears what a launcher closed mid-download left in the cache (`.incoming`, half-written files); nothing while a fetch is under way. How many files went. */
  cacheClean: (dir: string) => invoke<number>("cache_clean", { dir }),
  /** Rotates the cache down to `limit` bytes (null: no limit), never the files of `protect`. */
  cacheTrim: (dir: string, limit: number | null, protect: string[]) => invoke<void>("cache_trim", { dir, limit, protect }),
  /** Removes these files (by hash), or every one Quiver kept there. */
  cacheRemove: (dir: string, hashes?: string[]) => invoke<void>("cache_remove", { dir, hashes: hashes ?? null }),
  /** Shows the cache folder settings.json names, else the default one. */
  cacheOpen: () => invoke<void>("cache_open"),
  /** Shows an app's folder in the cache; fails while none of its files are kept. */
  cacheOpenApp: (app: string) => invoke<void>("cache_open_app", { app }),
  /** Starts an app; `id` names it in `onAppClosed` once it closes. `presence` is what Discord shows the player playing while it runs. */
  launch: (id: string | undefined, folder: string, dir: string | undefined, preferred: string[], wine: boolean, program?: string, launch?: LaunchOptions, presence?: DiscordGame) =>
    invoke<void>("launch", { id, folder, dir, preferred, wine, program, launch, presence }),
  /** Discord's Rich Presence on or off (discord.rs), with its words in the player's language. */
  setDiscordPresence: (enabled: boolean, texts: { idle: string; button: string }) => invoke<void>("set_discord_presence", { enabled, texts }),
  /** An app started with an id has closed, after running this long. */
  onAppClosed: (handler: (p: { id: string; seconds: number }) => void) => listen<{ id: string; seconds: number }>("app-closed", (e) => handler(e.payload)),
  /** Where an app's saves are and its backups (saves.rs). */
  savesInfo: (of: SavesOf) => invoke<SavesInfo | null>("saves_info", { of }),
  /** Backs up an app's saves if they changed; null when there was nothing new. Fails while the app runs. */
  savesBackup: (of: SavesOf, reason: "manual" | "update" | "uninstall") => invoke<SaveBackup | null>("saves_backup", { of, reason }),
  /** Puts a backup back, after backing up what's there; resolves to how many files. */
  /**
   * The folders restoring a backup would write to that this computer doesn't keep the app's saves in yet, for the
   * player to agree to. None for a backup of the places known here. Fails when a place is nowhere an app saves.
   */
  savesRestorePlan: (of: SavesOf, name: string) => invoke<SavePlace[]>("saves_restore_plan", { of, name }),
  /** `confirmed`: the places of `savesRestorePlan` the player agreed to. Without them a backup naming one isn't restored. */
  savesRestore: (of: SavesOf, name: string, confirmed: string[] = []) => invoke<number>("saves_restore", { of, name, confirmed }),
  savesRemove: (id: string, name: string) => invoke<void>("saves_remove", { id, name }),
  /** The folder the player says an app saves in, or none to go back to Quiver's choice. */
  savesChoose: (of: SavesOf, path?: string) => invoke<SavesInfo | null>("saves_choose", { of, path }),
  savesOpen: (of: SavesOf, template: string) => invoke<void>("saves_open", { of, template }),
  /** Before an app starts: notes what's in its likely save places, so `savesPlayed` can tell where it saved. */
  savesWatch: (of: SavesOf) => invoke<void>("saves_watch", { of }),
  /** After an app closed: remembers where it saved and backs its saves up if they changed. */
  savesPlayed: (of: SavesOf) => invoke<SaveBackup | null>("saves_played", { of }),
  /** The folders the launcher saw an app save in that the catalog hasn't been told about, and the system they're for. */
  savesToReport: (of: SavesOf) => invoke<{ os: "windows" | "linux" | "macos"; places: string[] }>("saves_to_report", { of }),
  /** The saves as they are now and when they last matched the account, for cloud saves. */
  savesCloud: (of: SavesOf) => invoke<SavesCloud>("saves_cloud", { of }),
  /** Remembers that saves with this checksum match the account's newest copy. */
  savesSynced: (of: SavesOf, hash: string | null) => invoke<void>("saves_synced", { of, hash }),
  savesCloudOff: (of: SavesOf, off: boolean) => invoke<void>("saves_cloud_off", { of, off }),
  /** Sends one backup to the link the site signed for it. */
  savesUpload: (id: string, name: string, url: string, headers: Record<string, string>) => invoke<void>("saves_upload", { id, name, url, headers }),
  /** Downloads a copy from the account and takes it in among the app's backups, where `savesRestore` can put it back. */
  savesDownload: (of: SavesOf, url: string, size: number, hash: string) => invoke<SaveBackup>("saves_download", { of, url, size, hash }),
  /**
   * The sync folder (sync_folder.rs): a folder of the player's own that backups are also kept in. The folder to use
   * for the one they picked: that one, or a "Quiver Saves" folder inside it.
   */
  savesFolderChoose: (path: string) => invoke<string>("saves_folder_choose", { path }),
  /** Every app with backups in the sync folder, the one backed up last first. */
  savesFolderSummary: (folder: string) => invoke<{ there: boolean; apps: SyncFolderApp[] }>("saves_folder_summary", { folder }),
  savesFolderOpen: (folder: string) => invoke<void>("saves_folder_open", { folder }),
  /** An app's saves as they are now, and the folder's backups of them. */
  savesFolderState: (of: SavesOf, folder: string) => invoke<SavesFolder>("saves_folder_state", { of, folder }),
  /** Remembers that saves with this checksum match the folder's newest backup. */
  savesFolderSynced: (of: SavesOf, folder: string, hash: string | null) => invoke<void>("saves_folder_synced", { of, folder, hash }),
  /** Keeps one of the app's backups in the folder too, as its newest. */
  savesFolderPut: (of: SavesOf, folder: string, name: string) => invoke<SaveBackup>("saves_folder_put", { of, folder, name }),
  /** Takes one of the folder's backups in among the app's own, where `savesRestore` can put it back. */
  savesFolderTake: (of: SavesOf, folder: string, name: string) => invoke<SaveBackup>("saves_folder_take", { of, folder, name }),
  /** Those places were sent to the catalog: they aren't offered by `savesToReport` again. */
  savesReported: (of: SavesOf, places: string[]) => invoke<void>("saves_reported", { of, places }),
  /** The player said the places Quiver backs up hold their saves: the tab stops asking while they stay the places. */
  savesFine: (of: SavesOf) => invoke<SavesInfo | null>("saves_fine", { of }),
  /** How an app's backups look, when the catalog hasn't been told yet; null with nothing to tell. */
  savesWarning: (of: SavesOf) => invoke<SavesWarning | null>("saves_warning", { of }),
  /** That report was sent (or refused): `savesWarning` doesn't offer it again. */
  savesWarned: (of: SavesOf, key: string) => invoke<void>("saves_warned", { of, key }),
  /** Stops an app's install while it downloads; the install then fails with "cancelled". */
  cancelInstall: (id: string) => invoke<void>("cancel_install", { id }),
  /** Fails with "cancelled" when the player says no to Windows uninstalling it. */
  uninstall: (folder: string, dir?: string) => invoke<void>("uninstall", { folder, dir }),
  secretGet: (key: string) => invoke<string | null>("secret_get", { key }),
  secretSet: (key: string, value: string | null) => invoke<void>("secret_set", { key, value }),
  /** GitLab's request limit, read by the Rust side: the page can't see GitLab's rate-limit headers. */
  gitlabLimit: (url: string, token: string | null) => invoke<GitlabLimit>("gitlab_limit", { url, token }),
  /** A repository host's API answer, fetched by Rust for a host that doesn't send CORS headers. */
  providerHttp: (url: string, headers: Record<string, string>) => invoke<ProviderHttpResponse>("provider_http", { request: { url, headers } }),
  /** Opens a sign-in page in the browser; resolves once it returns to the launcher. */
  browserSignIn: (url: string) => invoke<{ code: string } | { error: string }>("browser_sign_in", { url }),
  /** Stops waiting for the sign-in: browserSignIn then resolves to the error "cancelled". Starting another does this too. */
  browserSignInCancel: () => invoke<void>("browser_sign_in_cancel"),
  /** Full screen, or back; `systemTitleBar` is the setting, which full screen on Windows needs to know. */
  setFullscreen: (on: boolean, systemTitleBar: boolean) => invoke<void>("set_fullscreen", { on, titleBar: systemTitleBar }),
  /** The system's title bar back on, or off for the launcher's own buttons (a Mac always keeps its own). */
  setSystemTitleBar: (on: boolean) => invoke<void>("set_system_title_bar", { on }),
  /** Close to tray on or off, and the tray menu's words in the player's language (tray.rs; Windows only). */
  setTray: (closeToTray: boolean, open: string, quit: string) => invoke<void>("set_tray", { closeToTray, open, quit }),
  /** Whether Windows starts this copy of the launcher, in the tray, as the player signs in. */
  startWithWindows: () => invoke<boolean>("start_with_windows"),
  setStartWithWindows: (on: boolean) => invoke<void>("set_start_with_windows", { on }),
  /** The window's own buttons, for the top bar when the system's title bar is off. */
  minimize: () => getCurrentWindow().minimize(),
  toggleMaximize: () => getCurrentWindow().toggleMaximize(),
  closeWindow: () => getCurrentWindow().close(),
  /** The screen the window is on, in its own pixels, and the system's scale for it; null when it can't say. WebView2's `window.screen` is the window's size instead. */
  screen: () =>
    currentMonitor()
      .then((m) => (m ? { width: m.size.width, height: m.size.height, scale: m.scaleFactor } : null)),
  startDragging: () => getCurrentWindow().startDragging(),
  /** The window's place and size in the screen's pixels, and the smallest it can be. */
  windowRect: () => invoke<WindowRect>("window_rect"),
  setWindowRect: (r: Rect) => invoke<void>("set_window_rect", { ...r }),
  setWindowPosition: (x: number, y: number) => invoke<void>("set_window_position", { x, y }),
  /** Whether the window fills the screen: maximized, or full screen. */
  windowState: async () => {
    const window = getCurrentWindow();
    const [maximized, fullscreen] = await Promise.all([window.isMaximized(), window.isFullscreen()]);
    return { maximized, fullscreen };
  },
  onWindowResized: (handler: () => void) => getCurrentWindow().onResized(handler),
  /** The window's title bar and colour scheme; null follows the system. */
  setTheme: (theme: "light" | "dark" | null) => invoke<void>("set_theme", { theme }),
  logError: (message: string) => invoke<void>("log_error", { message }),
  findV3Library: () => invoke<V3Library | null>("find_v3_library"),
  /** Every Quiver Launcher 3 apps folder on this computer: an app in one is in an apps folder, whichever library came over. */
  v3AppsFolders: () => invoke<string[]>("v3_apps_folders"),
  /** The Quiver Launcher 3 library in a folder, or the folder around it; with `apps`, its installs in those Apps folders too. */
  findV3LibraryIn: (path: string, apps?: string[]) => invoke<V3Library | null>("find_v3_library_in", { path, apps }),
  /** The install in a folder the player says holds an app: null when it doesn't. */
  findExistingInstall: (path: string) => invoke<{ version: string; program?: string | null } | null>("find_existing_install", { path }),
  /** Asks for a sound file to play behind the launcher (music.ts), opening in `dir` when given; null when the player cancels. */
  pickMusic: (title: string, dir?: string) => invoke<string | null>("pick_music", { title, dir }),
  /** The music file's bytes, to play. */
  readMusic: (path: string) => invoke<ArrayBuffer>("read_music", { path }),
  /** Asks for a picture to show behind the library's grid (library-background.tsx), opening in `dir` when given; null when the player cancels. */
  pickBackground: (title: string, dir?: string) => invoke<string | null>("pick_background", { title, dir }),
  /** The background picture's bytes, to show. */
  readBackground: (path: string) => invoke<ArrayBuffer>("read_background", { path }),
  /** Asks for a picture and keeps a copy sized for `slot`; the copy's name (custom-art.tsx), null if they cancel. */
  pickArtwork: (title: string, slot: string) => invoke<string | null>("pick_artwork", { title, slot }),
  /** Keeps a copy of a picture on this computer for `slot`, as picking one does: one an app had in Quiver Launcher 3. */
  importArtwork: (path: string, slot: string) => invoke<string>("import_artwork", { path, slot }),
  /** Deletes the kept pictures other than these. */
  keepArtwork: (files: string[]) => invoke<void>("keep_artwork", { files }),
  /** Asks for a folder on this computer, opening in `dir` when given; null when the player cancels. */
  pickFolder: (title: string, dir?: string) => invoke<string | null>("pick_folder", { title, dir }),
  /** In Steam's Game Mode, the pickers ask the page's own browser (file-picker.tsx) instead of the system's dialog. */
  onPick: (handler: (ask: PickAsk) => void) => listen<PickAsk>("pick", (e) => handler(e.payload)),
  /** The page's browser's answer to a pick: the path chosen, or null for Cancel. */
  picked: (id: number, path: string | null) => invoke<void>("picked", { id, path }),
  /** What's in a folder, for the page's browser: `path`, or the home folder. */
  browse: (path: string | null) => invoke<Listing>("browse", { path }),
  /** Resolves to what to tell the player. */
  createShortcut: (request: ShortcutRequest) => invoke<string>("create_shortcut", { request }),
  addToSteam: (request: ShortcutRequest) => invoke<string>("add_to_steam", { request }),
  openUrl: (url: string) => invoke<void>("open_url", { url }),
  /** Android: puts the launcher behind the other apps, as Home does. */
  toBackground: () => invoke<void>("to_background"),
  checkUpdate: (preview: boolean) => invoke<LauncherUpdate | null>("check_update", { preview }),
  /** Installs the update and restarts on it; this copy closes. */
  installUpdate: (version: string) => invoke<void>("install_update", { version }),
  onUpdateProgress: (handler: (p: { done: number; total?: number | null }) => void) =>
    listen<{ done: number; total?: number | null }>("update-progress", (e) => handler(e.payload)),
  /** Opens an installed app's folder in the file manager. */
  openFolder: (id: string) => invoke<void>("open_folder", { id }),
  /** Makes a folder in the apps folder (or finds it), opens it, and resolves to its name. */
  createAppFolder: (name: string) => invoke<string>("create_app_folder", { name }),
  /** Renames an installed app's folder in the apps folder (and its Proton prefix); fails while the launcher has it running. */
  /** `dir`: the folder's own path, for one not at the top of the apps folder (in its console's folder). */
  renameAppFolder: (id: string, from: string, to: string, dir?: string) => invoke<void>("rename_app_folder", { id, from, to, dir: dir ?? null }),
  /** Those of `names` that are files in an installed app's folder. */
  appFiles: (folder: string, dir: string | undefined, names: string[]) => invoke<string[]>("app_files", { folder, dir, names }),
  /** Adds empty files to an installed app's folder and takes others away; returns those that stayed because they weren't empty. */
  setAppFiles: (id: string, folder: string, dir: string | undefined, add: string[], remove: string[]) => invoke<string[]>("set_app_files", { id, folder, dir, add, remove }),
  /** Why these installed apps can't move to `to`, or null. */
  appsFolderRefusal: (to: string, apps: MovingApp[]) => invoke<string | null>("apps_folder_refusal", { to, apps }),
  /**
   * Moves the installed apps to `to`, which becomes the apps folder: those in it, and those kept elsewhere (with a
   * `dir`), as Quiver Launcher 3's are. `inside`: the apps now there under their own folder's name; `left`: old copies
   * that couldn't all be deleted.
   */
  moveAppsFolder: (to: string, apps: MovingApp[]) => invoke<{ appsDir: string; inside: string[]; left: string[]; failed: { folder: string; why: string }[] }>("move_apps_folder", { to, apps }),
  /** Opens an apps folder (the default, or one an installed app is kept in) in the file manager. */
  openAppsFolder: (path: string) => invoke<void>("open_apps_folder", { path }),
  /** Whether each apps folder is there (a drive not plugged in isn't), and the bytes free on its drive. */
  appsFolderStates: (paths: string[]) => invoke<{ exists: boolean; free?: number | null }[]>("apps_folder_states", { paths }),
  /** Removes a console folder its apps all moved out of, once nothing's left in it, and "Ports" around it if that's empty too. */
  removeEmptyConsoleFolder: (path: string) => invoke<void>("remove_empty_console_folder", { path }),
  /** Removes the default apps folder's system folders with nothing in them (Ports\\N64 after its apps went elsewhere). */
  removeEmptySystemFolders: () => invoke<void>("remove_empty_system_folders"),
  /**
   * Moves one app's folder into `to`, beside what's there: `dir`, its folder there; `left`, its old copy if that
   * couldn't all be deleted.
   */
  /** `record`: how the app is recorded once there (in an apps folder, in a console folder), for a move the launcher closes during. */
  moveApp: (id: string, folder: string, dir: string | undefined, to: string, record: { inApps: boolean; group?: string }) =>
    invoke<{ dir: string; left?: string | null }>("move_app", { id, folder, dir, to, inApps: record.inApps, group: record.group ?? null }),
  /** An old copy a move couldn't delete once the app was recorded in its new place. */
  onMoveLeft: (handler: (path: string) => void) => listen<string>("app-move-left", (e) => handler(e.payload)),
  /** How far an app's move has got, in bytes copied (when it's copied to another drive). */
  onAppMoving: (handler: (m: { id: string; copied: number; size: number }) => void) => listen<{ id: string; copied: number; size: number }>("app-moving", (e) => handler(e.payload)),
  onAppsMoving: (handler: (m: AppsMoving) => void) => listen<AppsMoving>("apps-moving", (e) => handler(e.payload)),
  /** An app is in its new folder (`dir`), mid-move. */
  onAppMoved: (handler: (m: { id: string; dir: string }) => void) => listen<{ id: string; dir: string }>("app-moved", (e) => handler(e.payload)),
  /** Asks for a program on this computer, looking in `dir` first; null when the player cancels. */
  pickProgram: (dir?: string) => invoke<string | null>("pick_program", { dir }),
  modPackages: (source: ModSource) => invoke<ModPackage[]>("mod_packages", { source }),
  modFiles: (pkg: ModPackage) => invoke<ModFile[]>("mod_files", { package: pkg }),
  installedMods: (folder: string, dir?: string) => invoke<InstalledMod[]>("installed_mods", { folder, dir }),
  installMod: (folder: string, dir: string | undefined, config: ModSettings, pkg: ModPackage, file: ModFile) =>
    invoke<InstalledMod>("install_mod", { folder, dir, config, package: pkg, file }),
  uninstallMod: (folder: string, dir: string | undefined, config: ModSettings, key: string) => invoke<void>("uninstall_mod", { folder, dir, config, key }),
  /** True when Steam was asked: the launcher runs under Steam or Gamescope. */
  steamKeyboard: (open: boolean) => invoke<boolean>("steam_keyboard", { open }),
  /** Asks which program starts an installed app; relative to its folder, unless it's a program the player added. */
  pickExecutable: (folder: string, dir?: string, program?: string) => invoke<string | null>("pick_executable", { folder, dir, program }),
  /** The programs in an installed app's folder, relative to it. */
  listPrograms: (folder: string, dir?: string) => invoke<string[]>("list_programs", { folder, dir }),
  /** The executable Play starts when the player hasn't chosen one (relative to the app's folder when it's inside); null when Quiver doesn't pick a file. */
  foundProgram: (folder: string, dir: string | undefined, preferred: string[], wine: boolean) => invoke<string | null>("found_program", { folder, dir, preferred, wine }),
  /** Whether Android no longer has an app Quiver installed from an APK; always false elsewhere. */
  androidAppGone: (folder: string, dir?: string) => invoke<boolean>("android_app_gone", { folder, dir }),
  /** The kinds of file in an installed app's folder (extensions, not names), for an error report. */
  fileKinds: (folder: string, dir?: string) => invoke<string[]>("file_kinds", { folder, dir }),
  /** The programs in an app's folder when it has none of `preferred`; null when it has one, or `preferred` is empty. */
  missedPrograms: (folder: string, dir: string | undefined, preferred: string[], wine: boolean) =>
    invoke<string[] | null>("missed_programs", { folder, dir, preferred, wine }),
  listProtons: () => invoke<Proton[]>("list_protons"),
  /** What Automatic runs Windows programs with on Linux, by name (a Proton, else Wine); null with neither. */
  automaticRunner: () => invoke<string | null>("automatic_runner"),
  /** Asks for a Proton folder the launcher didn't find. */
  pickProton: () => invoke<string | null>("pick_proton"),
  controllers: () => invoke<string[]>("controllers"),
  onControllers: (handler: (names: string[]) => void) => listen<string[]>("pads", (e) => handler(e.payload)),
  controllerLabels: () => invoke<PadLabels | null>("controller_labels"),
  onControllerLabels: (handler: (labels: PadLabels | null) => void) => listen<PadLabels | null>("pad-labels", (e) => handler(e.payload)),
  onPad: (handler: (p: { key: string; down: boolean }) => void) => listen<{ key: string; down: boolean }>("pad", (e) => handler(e.payload)),
  onProgress: (handler: (p: Progress) => void) => listen<Progress>("install-progress", (e) => handler(e.payload)),
  /** Copies that could be the player's, in the places players keep them (own_copy.rs). */
  /** `folders`: where the player keeps this game's console's copies, looked in first; what's found there is `mine`. */
  findCopies: (file: SetupFile, folders: string[] = []) => invoke<{ path: string; size: number; mine?: boolean }[]>("find_copies", { file, folders }),
  /** Asks for the player's copy: a file of the kinds it can be, or a folder. */
  pickCopy: (file: SetupFile, dir?: string) => invoke<string | null>("pick_copy", { file, dir }),
  checkCopy: (file: SetupFile, path: string) => invoke<CheckedCopy>("check_copy", { file, path }),
  /** Puts a checked copy where the app reads it; where it went, or null when the app is told by flag or variable. */
  placeCopy: (folder: string, dir: string | undefined, wine: boolean, file: SetupFile, path: string) =>
    invoke<string | null>("place_copy", { folder, dir, wine, file, path }),
  copyPresent: (folder: string, dir: string | undefined, wine: boolean, file: SetupFile, path: string) =>
    invoke<boolean>("copy_present", { folder, dir, wine, file, path }),
  onCopyProgress: (handler: (p: CopyProgress) => void) => listen<CopyProgress>("copy-progress", (e) => handler(e.payload)),
};
