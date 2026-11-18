/** TV mode's own Rust commands (src-tauri/src/tv). The launcher's are in ../native.ts. */
import { invoke } from "@tauri-apps/api/core";

export const tvNative = {
  /** Whether the launcher was started with --tv-mode, and QUIVER_TV_DEBUG (an input overlay for testing controllers). */
  launchArgs: () => invoke<{ tvMode: boolean; debug: boolean }>("tv_launch_args"),
  /** Brings the window in front with the keyboard; resolves to whether it's focused now. */
  focus: () => invoke<boolean>("tv_focus"),
  /** A smaller copy (PNG) of a web picture far bigger than it's shown (art/fitted.ts). */
  fitArt: (url: string) => invoke<ArrayBuffer>("tv_fit_art", { url }),
  /** Fullscreen on this monitor (or stay where it is); resolves to the monitor used. */
  useMonitor: (name: string | null) => invoke<string | null>("tv_use_monitor", { name }),
  monitors: () => invoke<string[]>("tv_monitors"),
  /** The programs in a game's folder (relative paths), and the file name of the first one matching `hints` (programs/programs.ts). `unsure`: Windows, where the launcher's own pick would be a guess. */
  programs: (folder: string, dir: string | undefined, hints: string[]) =>
    invoke<{ matched: string | null; choices: string[]; unsure: boolean }>("tv_programs", { folder, dir, hints }),
};
