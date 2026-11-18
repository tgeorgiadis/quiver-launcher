/**
 * A game that closed with an error or crashed right after it started (src-tauri/src/tv/session.rs) shows as the
 * app's error, in both modes, like a launch that failed.
 */
import { useEffect, useRef } from "react";
import { listen } from "@tauri-apps/api/event";
import { useLauncher } from "../../store";

type Failure = {
  /** The app's id, when the launcher started it for one. */
  id: string | null;
  folder: string;
  message: string;
  early: boolean;
};

export function useFailures() {
  const launcher = useLauncher();
  const latest = useRef(launcher);
  latest.current = launcher;

  useEffect(() => {
    const stop = listen<Failure>("app-failed", ({ payload }) => {
      const { installs, fail } = latest.current;
      const id = payload.id ?? Object.keys(installs).find((key) => installs[key].folder === payload.folder);
      if (!id) return;
      fail(id, payload.message);
    });
    return () => void stop.then((s) => s());
  }, []);
}
