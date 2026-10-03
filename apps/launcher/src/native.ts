/** The Rust side of the launcher (src-tauri/src/lib.rs). */
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import type { Os } from "@quiver/api";

type StateFile = "library" | "installs" | "catalog" | "settings";
/** An app in a Quiver Launcher 3 library, with its installed copy if any. */
export type OldApp = { name: string; repository?: string; provider: string; dir?: string; version?: string };
export type Config = { api: string; convex: string; accountApi?: string; githubApi: string; returnTo: string; os: Os; arch: string; appsDir: string };
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

export const native = {
  config: () => invoke<Config>("config"),
  readState: <T>(name: StateFile) => invoke<T | null>("read_state", { name }),
  writeState: (name: StateFile, value: unknown) => invoke<void>("write_state", { name, value }),
  install: (request: InstallRequest) => invoke<{ dir: string; version: string }>("install", { request }),
  launch: (folder: string, dir: string | undefined, preferred: string[], wine: boolean) =>
    invoke<void>("launch", { folder, dir, preferred, wine }),
  uninstall: (folder: string, dir?: string) => invoke<void>("uninstall", { folder, dir }),
  secretGet: (key: string) => invoke<string | null>("secret_get", { key }),
  secretSet: (key: string, value: string | null) => invoke<void>("secret_set", { key, value }),
  /** Opens a sign-in page in the browser; resolves once it returns to the launcher. */
  browserSignIn: (url: string) => invoke<{ code: string } | { error: string }>("browser_sign_in", { url }),
  setFullscreen: (on: boolean) => invoke<void>("set_fullscreen", { on }),
  logError: (message: string) => invoke<void>("log_error", { message }),
  findV3Library: () => invoke<OldApp[]>("find_v3_library"),
  onProgress: (handler: (p: Progress) => void) => listen<Progress>("install-progress", (e) => handler(e.payload)),
};
