/** What the face buttons do on a game's tile, wherever it is (spec §5.2). */
import { useTv } from "../context";
import type { TvApp } from "../bridge";
import type { ButtonHint } from "../input/router";

export type Back = { label: string; go: () => void };

export const CANCEL_HOLD_MS = 1000;

export const isBusy = (app: TvApp) => app.state === "downloading" || app.state === "updating" || app.state === "queued";

export function tileHints(app: TvApp | null | undefined, back?: Back): ButtonHint[] {
  const hints: ButtonHint[] = [];
  switch (app?.state) {
    case "installed":
      hints.push({ action: "accept", label: "Play" });
      break;
    case "updateAvailable":
      hints.push({ action: "accept", label: "Play" }, { action: "secondary", label: "Update" });
      break;
    case "downloading":
    case "updating":
      hints.push({ action: "accept", label: "Downloads" });
      break;
    case "queued":
      hints.push({ action: "accept", label: "Downloads" }, { action: "secondary", label: "Cancel", hold: true });
      break;
    case "error":
      // An installed game whose launch failed plays again; a failed download tries again.
      hints.push({ action: "accept", label: app.installedVersion ? "Play" : "Retry" });
      break;
    case "notInstalled":
      // In the library but never installed: Install. From the catalog: Get adds it and downloads it.
      hints.push({ action: "accept", label: app.inLibrary ? "Install" : "Get" });
      break;
  }
  if (app) hints.push({ action: "details", label: "Details" });
  if (back) hints.push({ action: "back", label: back.label });
  return hints;
}

/** `onGet`: a game was got (added and downloading) from this view. */
export function useTileActions(onDownloads: () => void, onGet?: (app: TvApp) => void) {
  const { bridge, play } = useTv();
  return {
    /** A: play, get, retry, or see the download. */
    primary(app: TvApp | null | undefined) {
      if (!app) return;
      if (app.state === "installed" || app.state === "updateAvailable" || (app.state === "error" && app.installedVersion)) play(app.id);
      else if (isBusy(app)) onDownloads();
      else {
        void bridge.install(app.entry);
        onGet?.(app);
      }
    },
    /** X: a manual update ignores the update setting and a skipped version (spec §9). */
    update(app: TvApp | null | undefined) {
      if (app?.state === "updateAvailable") void bridge.update(app.id);
    },
    updateAll(apps: readonly TvApp[]) {
      for (const app of apps) if (app.state === "updateAvailable") void bridge.update(app.id);
    },
    holdFor: (app: TvApp | null | undefined) => (app?.state === "queued" ? CANCEL_HOLD_MS : undefined),
    /** Held X on a queued download: out of the queue. One already downloading can't be stopped (as upstream). */
    cancel(app: TvApp | null | undefined) {
      if (app?.state === "queued") bridge.cancelQueued(app.id);
    },
  };
}
