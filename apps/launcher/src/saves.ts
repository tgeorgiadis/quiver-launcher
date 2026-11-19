/**
 * Save backups (saves.rs): how the window names an app to the Rust side, and word of a new backup to an open Saves
 * tab. A backup is made after an app closes, before it updates and before it's uninstalled (store.tsx), and from the
 * tab itself (saves-tab.tsx). After an app closes, the folders the launcher saw it save in are also sent to the
 * catalog, for players who share usage data (`reportSaveLocations`).
 */
import { ApiError, type Client, type Entry } from "@quiverlauncher/api";
import type { native, SavesOf } from "./native";
import type { Install } from "./store";

/** Saves are files the launcher can reach on a desktop; Android keeps an app's files to itself. */
export const savesOn = (os: string) => os === "windows" || os === "linux" || os === "macos";

/** An installed app, for the saves commands. `entry` is its catalog entry, when it has one. */
export function savesOf(id: string, install: Pick<Install, "folder" | "dir" | "wine" | "version" | "program">, entry?: Pick<Entry, "name" | "projectName" | "slug" | "launcher">): SavesOf {
  // Its program's name without the folders or ".exe": many apps name their data folder after it.
  const program = install.program?.split(/[\\/]/).pop()?.replace(/\.[^.]+$/, "");
  const names = [entry?.projectName, entry?.name, entry?.slug, install.folder, program].filter((n): n is string => Boolean(n?.trim()));
  return {
    id,
    folder: install.folder,
    ...(install.dir ? { dir: install.dir } : {}),
    wine: Boolean(install.wine),
    locations: entry?.launcher?.saves?.locations ?? [],
    ...(install.version ? { version: install.version } : {}),
    names: [...new Set(names)],
  };
}

const listeners = new Set<(id: string) => void>();
/** An app's backups changed outside its Saves tab (it closed, or updated): the tab reads them again. */
export function onSavesChanged(listener: (id: string) => void) {
  listeners.add(listener);
  return () => void listeners.delete(listener);
}
export const savesChanged = (id: string) => listeners.forEach((listener) => listener(id));

/**
 * Tells the catalog the folders the launcher saw an app save in, so staff can list them for every player. Each is a
 * path under one of the system's folders ("data/Game/saves") with the system's name: nothing about the player or the
 * computer, and never a folder the player chose. Sent once per folder: one the site took, or refused, isn't sent again,
 * and one that didn't get there (no connection, the site down) goes with the next time the app closes.
 */
export async function reportSaveLocations(
  of: SavesOf,
  entry: Pick<Entry, "slug" | "catalogId">,
  via: { toReport: typeof native.savesToReport; reported: typeof native.savesReported; send: Client["saveLocations"] },
): Promise<void> {
  const { os, places } = await via.toReport(of);
  if (!places.length) return;
  try {
    await via.send(entry, { os, places });
  } catch (error) {
    if (!(error instanceof ApiError) || error.status < 400 || error.status >= 500 || error.status === 429) return;
  }
  await via.reported(of, places);
}
