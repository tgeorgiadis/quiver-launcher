/**
 * Which program starts a game, when the player hasn't chosen one (store.tsx's Play). The names to look for, best
 * first: the catalog's for this computer (the install's, else the catalog's now), then on Linux and macOS the
 * catalog's Windows names without ".exe" (Star Fox Enhanced names starfox_pc.exe, and its Linux build has
 * starfox_pc). When none is a program in the folder and it has more than one, the player picks (src-tauri/src/tv/
 * programs.rs). On Windows only when the launcher would be guessing: several programs side by side, as in
 * Bloodborne's Windows port, whose first program is a GPU check.
 */

type ByOs = Partial<Record<string, string[]>>;

/** Glob patterns (as upstream's preferred executables) to look for, best first. */
export function programHints(os: string, executables: string[], byOs: ByOs | undefined): string[] {
  const own = executables.length ? executables : (byOs?.[os] ?? []);
  const fromWindows = os === "windows" ? [] : (byOs?.windows ?? []).filter((p) => /\.exe$/i.test(p)).map((p) => p.replace(/\.exe$/i, ""));
  return [...own, ...fromWindows.filter((p) => !own.includes(p))];
}
