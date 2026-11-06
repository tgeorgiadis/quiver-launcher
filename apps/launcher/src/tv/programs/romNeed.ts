/** The game waiting for its own copy of the game (a ROM), if any: programs/usePrograms.ts sets it when a game fails, RomDialog.tsx shows it. */
export type RomNeed = {
  id: string;
  title: string;
  /** The file name the game looks for, when its output names one. */
  file: string | null;
  /** The folder it looks in, relative to `dir` ("" is beside its program). */
  into: string;
  /** The folder of the game's program. */
  dir: string;
  readme: string | null;
};

let current: RomNeed | null = null;
const listeners = new Set<() => void>();

/** The game waiting for its ROM, if any (programs/usePrograms.ts sets it when a game fails). */
export const romNeed = {
  get: () => current,
  set(need: RomNeed | null) {
    current = need;
    for (const listener of listeners) listener();
  },
  subscribe(listener: () => void) {
    listeners.add(listener);
    return () => void listeners.delete(listener);
  },
};
