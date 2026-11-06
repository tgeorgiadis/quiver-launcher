/**
 * Consoles for Big Picture's tabs (spec §7). The catalog's facets name them
 * and give each its maker; this adds a short label for a crowded tab strip
 * and the release year, which orders a maker's consoles. Ids are the
 * catalog's own ("gcn", "gen", "og xbox"…).
 */
import type { Console } from "@quiverlauncher/api";

export const APPS = "apps";
export const OTHER = "other";

export type ConsoleInfo = {
  id: string;
  name: string;
  short: string;
  /** The catalog's maker: "Nintendo", "PlayStation", "Xbox", "Sega", "OtherPlatforms". */
  brand: string;
  year?: number;
  /** Apps & Tools and Other: always after the real consoles. */
  special: boolean;
};

const META: Record<string, { short: string; year: number }> = {
  nes: { short: "NES", year: 1983 },
  gb: { short: "GB", year: 1989 },
  snes: { short: "SNES", year: 1990 },
  vb: { short: "VB", year: 1995 },
  n64: { short: "N64", year: 1996 },
  gbc: { short: "GBC", year: 1998 },
  gcn: { short: "GC", year: 2001 },
  gba: { short: "GBA", year: 2001 },
  nds: { short: "DS", year: 2004 },
  wii: { short: "Wii", year: 2006 },
  "3ds": { short: "3DS", year: 2011 },
  wiiu: { short: "Wii U", year: 2012 },
  switch: { short: "Switch", year: 2017 },
  sms: { short: "SMS", year: 1986 },
  gen: { short: "MD", year: 1988 },
  gg: { short: "GG", year: 1990 },
  segacd: { short: "SCD", year: 1991 },
  "32x": { short: "32X", year: 1994 },
  saturn: { short: "SAT", year: 1994 },
  dreamcast: { short: "DC", year: 1998 },
  ps1: { short: "PS1", year: 1994 },
  ps2: { short: "PS2", year: 2000 },
  psp: { short: "PSP", year: 2004 },
  ps3: { short: "PS3", year: 2006 },
  ps4: { short: "PS4", year: 2013 },
  "og xbox": { short: "Xbox", year: 2001 },
  x360: { short: "360", year: 2005 },
  arcade: { short: "Arcade", year: 1970 },
  pc: { short: "PC", year: 1981 },
  mobile: { short: "Mobile", year: 2007 },
};

/** The spec's maker order (§7.1): Nintendo, Sega, Sony, Microsoft, then everything else. */
const BRAND_ORDER = ["nintendo", "sega", "playstation", "xbox"];

const SPECIAL: ConsoleInfo[] = [
  { id: APPS, name: "Apps & Tools", short: "Apps", brand: "", special: true },
  { id: OTHER, name: "Other", short: "Other", brand: "", special: true },
];

export class ConsoleCatalog {
  private readonly byId = new Map<string, ConsoleInfo>();
  /** Where each console came in the catalog's facets: the tie-break after maker and year. */
  private readonly facetIndex = new Map<string, number>();

  constructor(facets: Console[]) {
    facets.forEach((c, i) => {
      const meta = META[c.id];
      this.byId.set(c.id, { id: c.id, name: c.name, short: meta?.short ?? c.name, brand: c.brand, year: meta?.year, special: false });
      this.facetIndex.set(c.id, i);
    });
    for (const s of SPECIAL) this.byId.set(s.id, s);
  }

  /** Any id, even one the facets don't list yet (offline, or a console added since): it's shown by its id. */
  get(id: string): ConsoleInfo {
    return this.byId.get(id) ?? { id, name: id, short: META[id]?.short ?? id, brand: "", year: META[id]?.year, special: false };
  }

  /** The catalog's consoles (not Apps & Tools or Other), in its own order. */
  ids(): string[] {
    return [...this.facetIndex.keys()];
  }

  /** Orders consoles by maker, then year, then the catalog's own order. */
  compare = (a: ConsoleInfo, b: ConsoleInfo): number => {
    const rank = (c: ConsoleInfo) => {
      const i = BRAND_ORDER.indexOf(c.brand.toLowerCase());
      return i < 0 ? BRAND_ORDER.length : i;
    };
    return (
      Number(a.special) - Number(b.special) ||
      rank(a) - rank(b) ||
      (a.year ?? 9999) - (b.year ?? 9999) ||
      (this.facetIndex.get(a.id) ?? 999) - (this.facetIndex.get(b.id) ?? 999) ||
      a.name.localeCompare(b.name)
    );
  };
}
