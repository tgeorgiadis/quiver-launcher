/** The Rust side of the launcher (src-tauri/src/lib.rs). */
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import type { ModSettings, ModSource, Os } from "@quiverlauncher/api";

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
export type V3Library = { folder: string; apps: OldApp[] };
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
  /** Where anonymous usage data goes (telemetry.ts). */
  posthogHost: string;
  version: string;
  /** The home folder and user name, which usage data never contains. */
  home: string | null;
  user: string | null;
  /** Under gamescope, as a Steam Deck's Game Mode runs it: TV mode comes on by itself. Missing from an older build. */
  gameMode?: boolean;
  /** Steam's on-screen keyboard can be brought up for a text field (keyboard.rs). Missing from an older build. */
  steamKeyboard?: boolean;
};
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
  filesToAdd: string[];
  version: string;
  /** The catalog's preferred programs, for finding the one a Windows installer installed. */
  preferred?: string[];
  /** The program the app started with before an update. */
  program?: string;
};
/** What an install did. A Windows installer (MSI) puts its files where its setup chose. */
export type Installed = {
  dir: string;
  version: string;
  /** The downloaded file's `sha256:<hex>`. Missing from the web side's stand-in for the Rust side. */
  checksum?: string;
  installer: "msi" | null;
  /** The program a Windows installer installed, when the launcher could tell. */
  program: string | null;
  /** Where it installed, to start looking from when the player chooses its program. */
  location: string | null;
  /** Windows wants a restart to finish. */
  restart: boolean;
};

/** How the player wants an app started, when the launcher's own choice isn't right. */
export type LaunchOptions = {
  /** The program to start: inside the app's folder (relative), or anywhere for a program the player added. */
  executable?: string;
  runner?: { kind: "direct" } | { kind: "wine" } | { kind: "proton"; path: string };
  /** What Settings runs every Windows program with on Linux, where the app has no runner of its own (never saved with an app). */
  windowsRunner?: WindowsRunner;
  /** As Steam takes them: `NAME=value` first sets the environment, `%command%` is where the game goes, otherwise arguments. */
  command?: string;
  /** The programs Play starts, relative to the app's folder: with more than one, Play asks which. The launcher itself reads only `executable`. */
  programs?: string[];
};
export type Proton = { name: string; path: string };
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

export const native = {
  config: () => invoke<Config>("config"),
  readState: <T>(name: StateFile) => invoke<T | null>("read_state", { name }),
  writeState: (name: StateFile, value: unknown) => invoke<void>("write_state", { name, value }),
  stateProblems: () => invoke<StateProblem[]>("state_problems"),
  listSnapshots: () => invoke<Snapshot[]>("list_snapshots"),
  restoreSnapshot: (id: string) => invoke<void>("restore_snapshot", { id }),
  startEmptyLibrary: () => invoke<void>("start_empty_library"),
  /** Fails with "cancelled" when the player stops it, "missing" when its file is gone (a 404), or "mismatch" when the download isn't the file its checksum names. */
  install: (request: InstallRequest) => invoke<Installed>("install", { request }),
  /** Starts an app; `id` names it in `onAppClosed` once it closes. */
  launch: (id: string | undefined, folder: string, dir: string | undefined, preferred: string[], wine: boolean, program?: string, launch?: LaunchOptions) =>
    invoke<void>("launch", { id, folder, dir, preferred, wine, program, launch }),
  /** An app started with an id has closed, after running this long. */
  onAppClosed: (handler: (p: { id: string; seconds: number }) => void) => listen<{ id: string; seconds: number }>("app-closed", (e) => handler(e.payload)),
  /** Stops an app's install while it downloads; the install then fails with "cancelled". */
  cancelInstall: (id: string) => invoke<void>("cancel_install", { id }),
  /** Fails with "cancelled" when the player says no to Windows uninstalling it. */
  uninstall: (folder: string, dir?: string) => invoke<void>("uninstall", { folder, dir }),
  secretGet: (key: string) => invoke<string | null>("secret_get", { key }),
  secretSet: (key: string, value: string | null) => invoke<void>("secret_set", { key, value }),
  /** Opens a sign-in page in the browser; resolves once it returns to the launcher. */
  browserSignIn: (url: string) => invoke<{ code: string } | { error: string }>("browser_sign_in", { url }),
  /** Stops waiting for the sign-in: browserSignIn then resolves to the error "cancelled". Starting another does this too. */
  browserSignInCancel: () => invoke<void>("browser_sign_in_cancel"),
  /** Full screen, or back; `systemTitleBar` is the setting, which full screen on Windows needs to know. */
  setFullscreen: (on: boolean, systemTitleBar: boolean) => invoke<void>("set_fullscreen", { on, titleBar: systemTitleBar }),
  /** The system's title bar back on, or off for the launcher's own buttons (a Mac always keeps its own). */
  setSystemTitleBar: (on: boolean) => invoke<void>("set_system_title_bar", { on }),
  /** The window's own buttons, for the top bar when the system's title bar is off. */
  minimize: () => getCurrentWindow().minimize(),
  toggleMaximize: () => getCurrentWindow().toggleMaximize(),
  closeWindow: () => getCurrentWindow().close(),
  startDragging: () => getCurrentWindow().startDragging(),
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
  /** The Quiver Launcher 3 library in a folder, or the folder around it; with `apps`, its installs in that Apps folder. */
  findV3LibraryIn: (path: string, apps?: string) => invoke<V3Library | null>("find_v3_library_in", { path, apps }),
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
  renameAppFolder: (id: string, from: string, to: string) => invoke<void>("rename_app_folder", { id, from, to }),
  /** Why the installed apps in `folders` can't move to `to`, or null. */
  appsFolderRefusal: (to: string, folders: string[]) => invoke<string | null>("apps_folder_refusal", { to, folders }),
  /** Moves the installed apps to `to`, which becomes the apps folder; resolves to it. */
  moveAppsFolder: (to: string, folders: string[]) => invoke<string>("move_apps_folder", { to, folders }),
  onAppsMoving: (handler: (m: { done: number; total: number; folder: string }) => void) =>
    listen<{ done: number; total: number; folder: string }>("apps-moving", (e) => handler(e.payload)),
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
};
