/** The Rust side of the launcher (src-tauri/src/lib.rs). */
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import type { Os } from "@quiver/api";

export type Config = { api: string; os: Os; arch: string; appsDir: string };
export type Progress = { id: string; phase: "downloading" | "installing"; received: number; total: number | null };
export type InstallRequest = {
  id: string;
  url: string;
  filename: string;
  checksum?: string;
  folder: string;
  filesToAdd: string[];
  version: string;
};

export const native = {
  config: () => invoke<Config>("config"),
  readState: <T>(name: "library" | "installs" | "catalog") => invoke<T | null>("read_state", { name }),
  writeState: (name: "library" | "installs" | "catalog", value: unknown) => invoke<void>("write_state", { name, value }),
  install: (request: InstallRequest) => invoke<{ dir: string; version: string }>("install", { request }),
  launch: (folder: string, preferred: string[]) => invoke<void>("launch", { folder, preferred }),
  uninstall: (folder: string) => invoke<void>("uninstall", { folder }),
  onProgress: (handler: (p: Progress) => void) => listen<Progress>("install-progress", (e) => handler(e.payload)),
};
