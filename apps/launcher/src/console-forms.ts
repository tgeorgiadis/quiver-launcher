/**
 * Consoles by kind: played at home on a TV, or in the hand. With the catalog's makers (its facets' `brand`), the
 * categories consoles can be narrowed to, such as every Nintendo handheld (TV mode's Discover).
 */
import type { Console } from "@quiverlauncher/api";

export type ConsoleForm = "home" | "handheld";
export const CONSOLE_FORMS: readonly ConsoleForm[] = ["home", "handheld"];

/** Played in the hand; the Switch is both. */
const HANDHELD = new Set(["gb", "gbc", "gba", "vb", "nds", "3ds", "gg", "psp", "switch"]);
const BOTH = new Set(["switch"]);
/** Neither a home console nor a handheld: arcade machines, computers and phones. */
const NEITHER = new Set(["arcade", "pc", "mobile"]);

/** A console's kinds, by the catalog's id: a console it doesn't know yet is a home console. */
export function formsOf(id: string): ConsoleForm[] {
  if (NEITHER.has(id)) return [];
  if (BOTH.has(id)) return ["home", "handheld"];
  return HANDHELD.has(id) ? ["handheld"] : ["home"];
}

/** The consoles of a maker (the facets' `brand`) and of a kind; null is any. */
export const consolesMatching = <C extends Pick<Console, "id" | "brand">>(consoles: readonly C[], maker: string | null, form: ConsoleForm | null): C[] =>
  consoles.filter((c) => (!maker || c.brand === maker) && (!form || formsOf(c.id).includes(form)));
