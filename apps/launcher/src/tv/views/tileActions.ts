/** What the face buttons do on a game's tile, wherever it is. */
import { t } from "@lingui/core/macro";
import { useTv } from "../context";
import type { TvApp } from "../bridge";
import type { ButtonHint } from "../input/router";

export type Back = { label: string; go: () => void };

export const CANCEL_HOLD_MS = 1000;

export const isBusy = (app: TvApp) => app.state === "downloading" || app.state === "updating" || app.state === "queued";

/** A tile's and a Downloads row's hints, in the language shown when they're read. */
export const HINT = {
  play: () => t({ message: "Play", comment: "Button that starts an app" }),
  update: () => t({ message: "Update", comment: "Button: update the one app with an update" }),
  downloads: () => t`Downloads`,
  cancel: () => t`Cancel`,
  retry: () => t({ message: "Retry", comment: "Controller hint: tries a failed download again" }),
  install: () => t({ message: "Install", comment: "Controller hint: installs a game that's in the library but not installed" }),
  get: () => t({ message: "Get", comment: "Button that installs an app" }),
  details: () => t({ message: "Details", comment: "Controller hint: what Options does on an app card, opening its page" }),
};

/** A game whose own X is in use: its update, or holding to cancel its waiting download. Elsewhere X opens a list. */
export const usesX = (app: TvApp | null | undefined) => app?.state === "updateAvailable" || app?.state === "queued";

export function tileHints(app: TvApp | null | undefined, back?: Back): ButtonHint[] {
  const hints: ButtonHint[] = [];
  switch (app?.state) {
    case "installed":
      hints.push({ action: "accept", label: HINT.play() });
      break;
    case "updateAvailable":
      hints.push({ action: "accept", label: HINT.play() }, { action: "secondary", label: HINT.update() });
      break;
    case "downloading":
    case "updating":
      hints.push({ action: "accept", label: HINT.downloads() });
      break;
    case "queued":
      hints.push({ action: "accept", label: HINT.downloads() }, { action: "secondary", label: HINT.cancel(), hold: true });
      break;
    case "error":
      // An installed game whose launch failed plays again; a failed download tries again.
      hints.push({ action: "accept", label: app.installedVersion ? HINT.play() : HINT.retry() });
      break;
    case "notInstalled":
      // In the library but never installed: Install. From the catalog: Get adds it and downloads it.
      hints.push({ action: "accept", label: app.inLibrary ? HINT.install() : HINT.get() });
      break;
  }
  if (app) hints.push({ action: "details", label: HINT.details() });
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
    /** X: a manual update ignores the update setting and a skipped version. */
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
