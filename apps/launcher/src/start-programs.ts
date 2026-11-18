import { ApiError, type Client, type Entry } from "@quiverlauncher/api";
import type { Install } from "./store";

type Os = "windows" | "linux" | "macos";

/**
 * The programs Play looks for first: the catalog's as it is now, so a fix to it reaches apps already installed, else
 * the ones saved when the app was installed (an app the catalog no longer has, or the player's own files). A Windows
 * build run through Wine looks for Windows' programs.
 */
export function startPatterns(install: Pick<Install, "executables" | "wine" | "local">, entry: Pick<Entry, "launcher"> | undefined, os: string): string[] {
  const runs = (install.wine ? "windows" : os) as Os;
  // The player's own files, kept under the catalog's name: what was saved for them.
  if (install.local || !entry?.launcher || !["windows", "linux", "macos"].includes(runs)) return install.executables;
  return entry.launcher.preferredExecutables?.[runs] ?? [];
}

const KEY = "quiver.startProgramsReported";

function reported(): string[] {
  try {
    const kept: unknown = JSON.parse(localStorage.getItem(KEY) ?? "[]");
    return Array.isArray(kept) ? kept.filter((k): k is string => typeof k === "string") : [];
  } catch {
    return [];
  }
}

/**
 * After Play: when the catalog names programs for the app and its folder has none of them, tells the site which it
 * has (start-program on the site), once for each app and version. Never in the way of playing.
 */
export async function reportMissedPrograms(
  install: Pick<Install, "folder" | "dir" | "version" | "wine">,
  entry: Pick<Entry, "id" | "slug" | "catalogId">,
  patterns: string[],
  os: string,
  via: { missed: (folder: string, dir: string | undefined, preferred: string[], wine: boolean) => Promise<string[] | null>; send: Client["startProgram"] },
): Promise<void> {
  if (!patterns.length || !entry.slug) return;
  const key = `${entry.id}@${install.version}`;
  if (reported().includes(key)) return;
  const programs = await via.missed(install.folder, install.dir, patterns, Boolean(install.wine));
  if (!programs) return;
  const runs = (install.wine ? "windows" : os) as Os;
  try {
    await via.send(entry, { os: runs, version: install.version, programs });
  } catch (error) {
    // Asked again next time unless the site said no to this report.
    if (!(error instanceof ApiError) || error.status < 400 || error.status >= 500 || error.status === 429) return;
  }
  try {
    localStorage.setItem(KEY, JSON.stringify([...reported().slice(-199), key]));
  } catch {
    // Not kept: it's asked again next time.
  }
}
