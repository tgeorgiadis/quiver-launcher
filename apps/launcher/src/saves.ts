/**
 * Save backups (saves.rs): how the window names an app to the Rust side, and word of a new backup to an open Saves
 * tab. A backup is made after an app closes, before it updates and before it's uninstalled (store.tsx), and from the
 * tab itself (saves-tab.tsx).
 */
import type { Entry } from "@quiverlauncher/api";
import type { SavesOf } from "./native";
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
