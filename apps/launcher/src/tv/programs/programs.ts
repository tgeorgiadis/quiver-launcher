/**
 * Which program starts a game on Linux (and macOS), when the catalog doesn't
 * name one for this computer. In order:
 *   1. the program the player picked before;
 *   2. the catalog's names for this computer (the install's, else the catalog's now);
 *   3. the catalog's Windows names without ".exe": Star Fox Enhanced names
 *      starfox_pc.exe, and its Linux build has starfox_pc.
 * When none of them is a program in the folder and it has more than one,
 * the player picks, and that's remembered (bigpicture.json).
 *
 * On Windows the player picks only when upstream would be guessing: several
 * programs side by side (src-tauri/src/tv/programs.rs), as in
 * Bloodborne's Windows port, whose first program is a GPU check.
 */

type ByOs = Partial<Record<string, string[]>>;

/** Glob patterns (as upstream's preferred executables) to look for, best first. */
export function programHints(os: string, executables: string[], byOs: ByOs | undefined, picked?: string): string[] {
  const own = executables.length ? executables : (byOs?.[os] ?? []);
  const fromWindows = os === "windows" ? [] : (byOs?.windows ?? []).filter((p) => /\.exe$/i.test(p)).map((p) => p.replace(/\.exe$/i, ""));
  return [...(picked ? [literal(picked)] : []), ...own, ...fromWindows.filter((p) => !own.includes(p))];
}

/** A file name as a glob pattern that matches only itself. */
export function literal(name: string): string {
  return name.replace(/[[\]*?]/g, (c) => `[${c}]`);
}

/** The file name of a path relative to the game's folder. */
export function fileName(path: string): string {
  return path.split(/[\\/]/).pop() ?? path;
}
