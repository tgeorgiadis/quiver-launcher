/**
 * A game that closed with an error or crashed right after it started (src-tauri/src/tv/session.rs) shows as the
 * app's error, in both modes, like a launch that failed. One that said it needs the player's own copy of the game
 * (a ROM) opens the ROM dialog too (RomDialog.tsx).
 */
import { useEffect, useRef } from "react";
import { listen } from "@tauri-apps/api/event";
import { t } from "@lingui/core/macro";
import { useLauncher } from "../../store";
import { romNeed } from "./romNeed";

type Failure = {
  /** The app's id, when the launcher started it for one. */
  id: string | null;
  folder: string;
  message: string;
  early: boolean;
  /** It said it needs the player's own copy of the game (src-tauri/src/tv/romneed.rs). */
  rom?: { file: string | null; into: string; dir: string; readme: string | null } | null;
};

export function useFailures() {
  const launcher = useLauncher();
  const latest = useRef(launcher);
  latest.current = launcher;

  useEffect(() => {
    const stop = listen<Failure>("app-failed", ({ payload }) => {
      const { installs, fail, catalog, library } = latest.current;
      const id = payload.id ?? Object.keys(installs).find((key) => installs[key].folder === payload.folder);
      if (!id) return;
      if (payload.rom) {
        // Before fail(): Notices leaves out the game the dialog is about.
        const entry = catalog[id];
        const title = library.find((i) => i.id === id)?.overrides?.name || entry?.projectName || entry?.name || t({ message: "This game", comment: "A game with no name known, as in “This game needs your own copy of the game”" });
        romNeed.set({ id, title, ...payload.rom });
      }
      fail(id, payload.message);
    });
    return () => void stop.then((s) => s());
  }, []);
}
