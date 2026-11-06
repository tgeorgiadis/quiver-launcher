/**
 * Big Picture's own state, in bigpicture.json beside upstream's files (spec §6).
 * Upstream already keeps last played (installs[id].lastPlayed), so it isn't
 * duplicated here. Fields this version doesn't know are kept as they are.
 * Every change makes new objects, so React can watch it with useSyncExternalStore.
 */

export type UpdateBehavior = "ask" | "off" | "auto";
export type ConsoleTabOrder = "manufacturerThenYear" | "gameCount" | "alphabetical";
export type AllSort = "name" | "recent";
/** After Get in Discover: stay there (get several in a row), or go to the game in the library. */
export type AfterGet = "stay" | "library";

export type TvSettings = {
  offerTvModeOnControllerConnect: boolean;
  updateBehavior: UpdateBehavior;
  showHomeTab: boolean;
  consoleTabOrder: ConsoleTabOrder;
  allSort: AllSort;
  /** The monitor's name; null is the one the desktop window is on. */
  monitor: string | null;
  /** Multiplies the size worked out from the window's height (720p is 1). */
  uiScale: number;
  reduceMotion: boolean;
  afterGet: AfterGet;
  /** Holding Start + Select for a second closes the game being played (src-tauri/src/tv/pads.rs). */
  closeGameCombo: boolean;
  /** Linux: while a game runs, holding Select makes the right stick a mouse pointer (src-tauri/src/tv/pointer.rs). */
  pointerWithSelect: boolean;
  /** Add an app offers "A program on this computer" (programs/ownPrograms.tsx). Off: Quiver 3 never lists them. */
  allowPrograms: boolean;
};

/** A picture of the player's own, by URL or file path, in place of the catalog's. */
export type TvArt = { grid?: string; hero?: string; logo?: string };

export type TvAppState = {
  /** A console id (catalog facets), or "apps" / "other". */
  consoleOverride?: string;
  playCount?: number;
  /** When Big Picture saw it installed (ms). */
  installedAt?: number;
  /** "Skip this version" in the update prompt: no prompt again until a newer one. */
  skippedVersion?: string;
  /** The release file the player picked when several fit. */
  assetPattern?: string;
  /** The program the player picked to start it, when its folder has several (file name; programs/programs.ts). */
  program?: string;
  art?: TvArt;
};

/** "Use the same games folder as Quiver 3" (share/, PLAN-v4.md §12). */
export type V3ShareState = {
  on: boolean;
  /** The one-time question for people who imported earlier: don't ask again. */
  dontAsk?: boolean;
  /** Changes to Quiver 3's apps.json waiting for it to close. */
  pending?: unknown[];
  /** Quiver 3's library as it was when this was turned on (in its own Backups folder). */
  backup?: string;
};

export type TvSidecar = {
  version: number;
  settings: TvSettings;
  apps: Record<string, TvAppState>;
  v3Share?: V3ShareState;
};

export const SIDECAR_VERSION = 1;

export const DEFAULT_SETTINGS: TvSettings = {
  offerTvModeOnControllerConnect: true,
  updateBehavior: "ask",
  showHomeTab: true,
  consoleTabOrder: "manufacturerThenYear",
  allSort: "name",
  monitor: null,
  uiScale: 1,
  reduceMotion: false,
  afterGet: "stay",
  closeGameCombo: true,
  pointerWithSelect: true,
  allowPrograms: false,
};

type Raw = Record<string, unknown>;
const isObject = (v: unknown): v is Raw => typeof v === "object" && v !== null && !Array.isArray(v);
const oneOf = <T extends string>(v: unknown, options: readonly T[], fallback: T): T =>
  options.includes(v as T) ? (v as T) : fallback;
const bool = (v: unknown, fallback: boolean) => (typeof v === "boolean" ? v : fallback);

/** Saved settings over the defaults: a value of the wrong kind is the default, an unknown one is kept. */
function settingsFrom(raw: unknown): TvSettings {
  const s = isObject(raw) ? raw : {};
  const d = DEFAULT_SETTINGS;
  const scale = typeof s.uiScale === "number" && Number.isFinite(s.uiScale) ? Math.min(2, Math.max(0.5, s.uiScale)) : d.uiScale;
  return {
    ...s,
    offerTvModeOnControllerConnect: bool(s.offerTvModeOnControllerConnect, d.offerTvModeOnControllerConnect),
    updateBehavior: oneOf(s.updateBehavior, ["ask", "off", "auto"], d.updateBehavior),
    showHomeTab: bool(s.showHomeTab, d.showHomeTab),
    consoleTabOrder: oneOf(s.consoleTabOrder, ["manufacturerThenYear", "gameCount", "alphabetical"], d.consoleTabOrder),
    allSort: oneOf(s.allSort, ["name", "recent"], d.allSort),
    monitor: typeof s.monitor === "string" && s.monitor ? s.monitor : null,
    uiScale: scale,
    reduceMotion: bool(s.reduceMotion, d.reduceMotion),
    afterGet: oneOf(s.afterGet, ["stay", "library"], d.afterGet),
    closeGameCombo: bool(s.closeGameCombo, d.closeGameCombo),
    pointerWithSelect: bool(s.pointerWithSelect, d.pointerWithSelect),
    allowPrograms: bool(s.allowPrograms, d.allowPrograms),
  };
}

/** The sidecar from whatever was read: nothing, an older file, or one with fields from a newer fork. */
export function normalize(raw: unknown): TvSidecar {
  const r = isObject(raw) ? raw : {};
  const apps = isObject(r.apps)
    ? Object.fromEntries(Object.entries(r.apps).filter(([, v]) => isObject(v))) as Record<string, TvAppState>
    : {};
  return {
    ...r,
    version: typeof r.version === "number" ? r.version : SIDECAR_VERSION,
    settings: settingsFrom(r.settings),
    apps,
    ...(isObject(r.v3Share)
      ? {
          v3Share: {
            ...r.v3Share,
            on: bool(r.v3Share.on, false),
            pending: Array.isArray(r.v3Share.pending) ? r.v3Share.pending : [],
          } as V3ShareState,
        }
      : {}),
  };
}

export type SidecarIO = {
  read(): Promise<unknown>;
  write(value: TvSidecar): Promise<void>;
};

export class SidecarStore {
  private data: TvSidecar;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private writing: Promise<void> = Promise.resolve();
  private readonly listeners = new Set<() => void>();

  constructor(
    private readonly io: SidecarIO,
    initial: unknown = null,
    private readonly delay = 500,
    private readonly onError: (error: unknown) => void = (e) => console.error("bigpicture.json:", e),
  ) {
    this.data = normalize(initial);
  }

  static async load(io: SidecarIO, delay?: number, onError?: (error: unknown) => void) {
    const raw = await io.read().catch(() => null);
    return new SidecarStore(io, raw, delay, onError);
  }

  /** The current state; a new object after every change. */
  get snapshot(): TvSidecar {
    return this.data;
  }

  get settings(): TvSettings {
    return this.data.settings;
  }

  app(id: string): TvAppState {
    return this.data.apps[id] ?? {};
  }

  setSettings(patch: Partial<TvSettings>) {
    this.change({ ...this.data, settings: settingsFrom({ ...this.data.settings, ...patch }) });
  }

  setV3Share(patch: Partial<V3ShareState>) {
    this.change({ ...this.data, v3Share: { on: false, ...this.data.v3Share, ...patch } });
  }

  updateApp(id: string, patch: Partial<TvAppState> | ((app: TvAppState) => TvAppState)) {
    const before = this.app(id);
    const after = typeof patch === "function" ? patch(before) : { ...before, ...patch };
    this.change({ ...this.data, apps: { ...this.data.apps, [id]: after } });
  }

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => void this.listeners.delete(listener);
  };

  /** Writes now instead of after the delay, such as when Big Picture closes. */
  flush(): Promise<void> {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
      this.save();
    }
    return this.writing;
  }

  private change(next: TvSidecar) {
    this.data = next;
    for (const listener of this.listeners) listener();
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.timer = null;
      this.save();
    }, this.delay);
  }

  /** One write at a time, in order: a slow write never lands after a newer one. */
  private save() {
    const value = this.data;
    this.writing = this.writing.then(() => this.io.write(value)).catch(this.onError);
  }
}
