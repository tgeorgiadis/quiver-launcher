/** The Rust side of the launcher (src-tauri/src/lib.rs). */
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import type { Os } from "@quiver/api";

type StateFile = "library" | "installs" | "catalog" | "settings" | "collections";
/** An app in a Quiver Launcher 3 library, with its installed copy if any. */
export type OldApp = { name: string; repository?: string; provider: string; dir?: string; version?: string };
export type Config = { api: string; convex: string; accountApi?: string; githubApi: string; gitlabApi: string; returnTo: string; os: Os; arch: string; appsDir: string };
export type Progress = { id: string; phase: "downloading" | "installing"; received: number; total: number | null };
export type InstallRequest = {
  id: string;
  url: string;
  filename: string;
  checksum?: string;
  folder: string;
  dir?: string;
  filesToAdd: string[];
  version: string;
};

/** An installed app, for a desktop or Steam shortcut. */
export type ShortcutRequest = {
  name: string;
  folder: string;
  dir?: string;
  preferred: string[];
  wine: boolean;
  /** A program the player picked, started exactly. */
  program?: string;
  art: { icon?: string; header?: string; capsule?: string; hero?: string; logo?: string };
};

export const native = {
  config: () => invoke<Config>("config"),
  readState: <T>(name: StateFile) => invoke<T | null>("read_state", { name }),
  writeState: (name: StateFile, value: unknown) => invoke<void>("write_state", { name, value }),
  install: (request: InstallRequest) => invoke<{ dir: string; version: string }>("install", { request }),
  launch: (folder: string, dir: string | undefined, preferred: string[], wine: boolean, program?: string) =>
    invoke<void>("launch", { folder, dir, preferred, wine, program }),
  uninstall: (folder: string, dir?: string) => invoke<void>("uninstall", { folder, dir }),
  secretGet: (key: string) => invoke<string | null>("secret_get", { key }),
  secretSet: (key: string, value: string | null) => invoke<void>("secret_set", { key, value }),
  /** Opens a sign-in page in the browser; resolves once it returns to the launcher. */
  browserSignIn: (url: string) => invoke<{ code: string } | { error: string }>("browser_sign_in", { url }),
  setFullscreen: (on: boolean) => invoke<void>("set_fullscreen", { on }),
  logError: (message: string) => invoke<void>("log_error", { message }),
  findV3Library: () => invoke<OldApp[]>("find_v3_library"),
  /** Resolves to what to tell the player. */
  createShortcut: (request: ShortcutRequest) => invoke<string>("create_shortcut", { request }),
  addToSteam: (request: ShortcutRequest) => invoke<string>("add_to_steam", { request }),
  openUrl: (url: string) => invoke<void>("open_url", { url }),
  /** Opens an installed app's folder in the file manager. */
  openFolder: (id: string) => invoke<void>("open_folder", { id }),
  /** Makes a folder in the apps folder (or finds it), opens it, and resolves to its name. */
  createAppFolder: (name: string) => invoke<string>("create_app_folder", { name }),
  /** Asks for a program on this computer; null when the player cancels. */
  pickProgram: () => invoke<string | null>("pick_program"),
  controllers: () => invoke<string[]>("controllers"),
  onControllers: (handler: (names: string[]) => void) => listen<string[]>("pads", (e) => handler(e.payload)),
  onPad: (handler: (p: { key: string; down: boolean }) => void) => listen<{ key: string; down: boolean }>("pad", (e) => handler(e.payload)),
  onProgress: (handler: (p: Progress) => void) => listen<Progress>("install-progress", (e) => handler(e.payload)),
};
