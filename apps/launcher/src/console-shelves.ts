/**
 * Browse's console shelves: one for each maker's consoles, as the console filter's "All Nintendo" has them, then one
 * for the handhelds whoever made them. Each is a list of console filters (ids, or "maker:Nintendo"), the same the
 * filter takes, so a shelf's See all is that filter on.
 */
import type { Console } from "@quiverlauncher/api";

/** The makers with a shelf, in this order. */
export const MAKER_SHELVES = ["Nintendo", "PlayStation", "Xbox", "Sega", "OtherPlatforms"];
/** The handhelds' console ids: PSP, Vita, DS, 3DS, Game Boy, Game Boy Color and Game Boy Advance. */
export const PORTABLE_CONSOLES = ["psp", "vita", "nds", "3ds", "gb", "gbc", "gba"];

export type ConsoleShelf = { id: string; consoles: string[] };

/** The shelves for the catalog's consoles: a maker's where it has any, the handhelds' where it has one. */
export function consoleShelves(consoles: readonly Pick<Console, "id" | "brand">[]): ConsoleShelf[] {
  const makers = MAKER_SHELVES.filter((brand) => consoles.some((c) => c.brand === brand)).map((brand) => ({
    id: `maker:${brand}`,
    consoles: [`maker:${brand}`],
  }));
  const portable = PORTABLE_CONSOLES.filter((id) => consoles.some((c) => c.id === id));
  return portable.length ? [...makers, { id: "portable", consoles: portable }] : makers;
}
