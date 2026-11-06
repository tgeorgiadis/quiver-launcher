/**
 * What usage data says about a launcher as it stands, not what the player just did: how it's set up, how big its
 * library is, and which versions of catalog apps it has. Only choices and counts: never a path, the GitHub token, or
 * the name of an app the player added.
 */
import { isCustom, isLocal } from "./custom";
import { hasUpdate, languageOf, tvModeOn, type CatalogEntry, type Install, type LibraryItem, type Settings } from "./store";
import type { Config } from "./native";

/** More than this and they'd outrun what usage data holds while it can't send yet; the ones played last go. */
export const MOST_APPS_SENT = 40;

/** Once a day per computer is enough to count who has which version. */
const SENT_KEY = "quiver-usage-apps-sent";

/** How this launcher is set up. At most 15 properties: with what every event carries, that's Google Analytics' 25. */
export function settingsSummary(settings: Settings, config: Pick<Config, "gameMode">, hasGithubToken: boolean, screen: { width: number; height: number; scale: number } | null) {
  return {
    theme: settings.theme ?? "system",
    language: languageOf(settings),
    tv_mode: tvModeOn(settings, config),
    close_to_tray: Boolean(settings.closeToTray),
    fullscreen: Boolean(settings.fullscreen),
    background_image: Boolean(settings.libraryBackground),
    background_music: Boolean(settings.music),
    preview_updates: Boolean(settings.previewUpdates),
    github_token: hasGithubToken,
    interface_scale: settings.scale ?? 1,
    windows_runner: settings.windowsRunner?.kind ?? "automatic",
    library_layout: settings.library?.layout ?? "default",
    library_sort: settings.librarySort ?? "default",
    screen: screen ? `${screen.width}x${screen.height}` : null,
    display_scale: screen?.scale ?? null,
  };
}

/** How big the library is and how its apps are kept up to date. */
export function librarySummary(library: LibraryItem[], installs: Record<string, Install>, catalog: Record<string, CatalogEntry>, settings: Pick<Settings, "hidden">, signedIn: boolean) {
  const installed = library.filter((item) => installs[item.id]);
  return {
    apps: library.length,
    installed: installed.length,
    catalog_apps: library.filter((item) => !isCustom(item.id) && !isLocal(item.id)).length,
    custom_apps: library.filter((item) => isCustom(item.id)).length,
    local_apps: library.filter((item) => isLocal(item.id)).length,
    updates_available: installed.filter((item) => hasUpdate(catalog[item.id], installs[item.id])).length,
    auto_update: installed.filter((item) => installs[item.id].updates === "auto").length,
    pinned: installed.filter((item) => installs[item.id].updates === "pinned").length,
    hidden: settings.hidden?.length ?? 0,
    signed_in: signedIn,
  };
}

/** Each catalog app installed here and its version, the ones played last first; apps the player added aren't named at all. */
export function appsOnComputer(library: LibraryItem[], installs: Record<string, Install>, catalog: Record<string, CatalogEntry>) {
  return library
    .filter((item) => installs[item.id] && item.slug && !isCustom(item.id) && !isLocal(item.id))
    .sort((a, b) => (installs[b.id].lastPlayed ?? 0) - (installs[a.id].lastPlayed ?? 0))
    .slice(0, MOST_APPS_SENT)
    .map((item) => ({
      slug: item.slug,
      version: installs[item.id].version,
      updates: installs[item.id].updates ?? "notify",
      update_available: hasUpdate(catalog[item.id], installs[item.id]),
    }));
}

/** Whether this computer's apps are due to be counted today, marking them as counted when they are. */
export function appsDueToday(now: Date) {
  const today = now.toISOString().slice(0, 10);
  try {
    if (localStorage.getItem(SENT_KEY) === today) return false;
    localStorage.setItem(SENT_KEY, today);
    return true;
  } catch {
    // Without storage it can't tell, and every launch counting them would be worse than none.
    return false;
  }
}
