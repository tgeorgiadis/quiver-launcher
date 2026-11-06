/**
 * TV mode's own settings, kept in the launcher's settings (`tv`, only what differs from the defaults). Everything
 * else TV mode shows is the launcher's: installed and played times, the download picked, skipped updates, the
 * player's consoles and pictures for an app, and the interface size.
 */

export type UpdateBehavior = "ask" | "off" | "auto";
export type ConsoleTabOrder = "manufacturerThenYear" | "gameCount" | "alphabetical";
export type AllSort = "name" | "recent";
/** After Get in Discover: stay there (get several in a row), or go to the game in the library. */
export type AfterGet = "stay" | "library";

export type TvSettings = {
  /** Outside TV mode, a note offers it when a controller connects (ControllerToast.tsx). */
  offerOnController: boolean;
  /** What Play does when the game has an update (updates/policy.ts). */
  updateBehavior: UpdateBehavior;
  showHomeTab: boolean;
  consoleTabOrder: ConsoleTabOrder;
  allSort: AllSort;
  /** The monitor's name; null is the one the window is on. */
  monitor: string | null;
  reduceMotion: boolean;
  afterGet: AfterGet;
  /** Holding Start + Select for a second closes the game being played (src-tauri/src/tv/session.rs). */
  closeGameCombo: boolean;
  /** Linux: while a game runs, holding Select makes the right stick a mouse pointer (src-tauri/src/tv/pointer.rs). */
  pointerWithSelect: boolean;
};

export const DEFAULT_SETTINGS: TvSettings = {
  offerOnController: true,
  updateBehavior: "ask",
  showHomeTab: true,
  consoleTabOrder: "manufacturerThenYear",
  allSort: "name",
  monitor: null,
  reduceMotion: false,
  afterGet: "stay",
  closeGameCombo: true,
  pointerWithSelect: true,
};

type Raw = Record<string, unknown>;
const isObject = (v: unknown): v is Raw => typeof v === "object" && v !== null && !Array.isArray(v);
const oneOf = <T extends string>(v: unknown, options: readonly T[], fallback: T): T => (options.includes(v as T) ? (v as T) : fallback);
const bool = (v: unknown, fallback: boolean) => (typeof v === "boolean" ? v : fallback);

/** The saved settings over the defaults: a value of the wrong kind is the default. */
export function tvSettings(raw: unknown): TvSettings {
  const s = isObject(raw) ? raw : {};
  const d = DEFAULT_SETTINGS;
  return {
    offerOnController: bool(s.offerOnController, d.offerOnController),
    updateBehavior: oneOf(s.updateBehavior, ["ask", "off", "auto"], d.updateBehavior),
    showHomeTab: bool(s.showHomeTab, d.showHomeTab),
    consoleTabOrder: oneOf(s.consoleTabOrder, ["manufacturerThenYear", "gameCount", "alphabetical"], d.consoleTabOrder),
    allSort: oneOf(s.allSort, ["name", "recent"], d.allSort),
    monitor: typeof s.monitor === "string" && s.monitor ? s.monitor : null,
    reduceMotion: bool(s.reduceMotion, d.reduceMotion),
    afterGet: oneOf(s.afterGet, ["stay", "library"], d.afterGet),
    closeGameCombo: bool(s.closeGameCombo, d.closeGameCombo),
    pointerWithSelect: bool(s.pointerWithSelect, d.pointerWithSelect),
  };
}

/** What to save: the changed settings over the saved ones, without those back at their default. */
export function savedTvSettings(saved: unknown, patch: Partial<TvSettings>): Partial<TvSettings> | undefined {
  const next = tvSettings({ ...tvSettings(saved), ...patch });
  const changed = Object.fromEntries(Object.entries(next).filter(([k, v]) => DEFAULT_SETTINGS[k as keyof TvSettings] !== v));
  return Object.keys(changed).length ? changed : undefined;
}
