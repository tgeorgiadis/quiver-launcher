import { describe, expect, it } from "vitest";
import type { Console } from "@quiverlauncher/api";
import { APPS, ConsoleCatalog, OTHER } from "./consoles";
import { normalizeTag, resolveConsole, type Resolvable } from "./resolve";
import { ALL, buildTabs, cycleTab, keepTab } from "./tabs";

/** The catalog's /facets consoles as of 2026-10-09. */
const FACETS: Console[] = [
  ["Nintendo", "nes", "Nintendo Entertainment System"], ["Nintendo", "snes", "Super Nintendo Entertainment System"],
  ["Nintendo", "n64", "Nintendo 64"], ["Nintendo", "gcn", "GameCube"], ["Nintendo", "wii", "Wii"], ["Nintendo", "wiiu", "Wii U"],
  ["Nintendo", "switch", "Nintendo Switch"], ["Nintendo", "gb", "Game Boy"], ["Nintendo", "vb", "Virtual Boy"],
  ["Nintendo", "gbc", "Game Boy Color"], ["Nintendo", "gba", "Game Boy Advance"], ["Nintendo", "nds", "Nintendo DS"],
  ["Nintendo", "3ds", "Nintendo 3DS"], ["PlayStation", "ps1", "PlayStation"], ["PlayStation", "ps2", "PlayStation 2"],
  ["PlayStation", "ps3", "PlayStation 3"], ["PlayStation", "ps4", "PlayStation 4"], ["PlayStation", "psp", "PlayStation Portable"],
  ["Xbox", "og xbox", "Xbox"], ["Xbox", "x360", "Xbox 360"], ["Sega", "sms", "Sega Master System"], ["Sega", "gg", "Sega Game Gear"],
  ["Sega", "gen", "Sega Genesis / Mega Drive"], ["Sega", "segacd", "Sega CD / Mega-CD"], ["Sega", "32x", "Sega 32X"],
  ["Sega", "saturn", "Sega Saturn"], ["Sega", "dreamcast", "Sega Dreamcast"], ["OtherPlatforms", "arcade", "Arcade"],
  ["OtherPlatforms", "pc", "PC"], ["OtherPlatforms", "mobile", "Mobile"],
].map(([brand, id, name]) => ({ brand, id, name }));

const catalog = new ConsoleCatalog(FACETS);
const app = (over: Partial<Resolvable> = {}): Resolvable => ({ consoles: [], projectType: "port", tags: [], ...over });

describe("resolveConsole", () => {
  it("puts the player's choice first", () => {
    expect(resolveConsole(app({ basedOn: "n64", consoles: ["n64"] }), "pc")).toBe("pc");
  });

  it("uses the console version the app is based on before its games' consoles", () => {
    expect(resolveConsole(app({ basedOn: "ps1", consoles: ["n64", "ps1"] }))).toBe("ps1");
  });

  it("falls back to the first of its games' consoles, in the catalog's order", () => {
    expect(resolveConsole(app({ consoles: ["gb", "gbc", "gba"] }))).toBe("gb");
  });

  it("puts tools and emulators in Apps & Tools even when they name consoles", () => {
    expect(resolveConsole(app({ projectType: "emulator", consoles: ["gba", "gbc", "gb"] }))).toBe(APPS);
    expect(resolveConsole(app({ projectType: "tool", basedOn: "wii", consoles: ["wii"] }))).toBe(APPS);
  });

  it("puts an app with no consoles but an app or tool tag in Apps & Tools", () => {
    expect(resolveConsole(app({ tags: ["Speedrun", "Utility"] }))).toBe(APPS);
  });

  it("puts anything else in Other", () => {
    expect(resolveConsole(app({ tags: ["nintendo", "recomp"] }))).toBe(OTHER);
  });

  it("treats spaces, underscores and case in tags alike", () => {
    expect(normalizeTag(" Game_Boy  Color ")).toBe("game-boy-color");
  });
});

describe("ConsoleCatalog", () => {
  it("orders by maker as the spec does (Nintendo, Sega, Sony, Microsoft), then by year", () => {
    const ids = ["x360", "gen", "ps1", "gba", "n64", "pc", "nes", "og xbox", "dreamcast"];
    const sorted = ids.map((id) => catalog.get(id)).sort(catalog.compare).map((c) => c.id);
    expect(sorted).toEqual(["nes", "n64", "gba", "gen", "dreamcast", "ps1", "og xbox", "x360", "pc"]);
  });

  it("names a console the facets don't list by its id, and keeps it ordered", () => {
    const unknown = catalog.get("neo");
    expect(unknown.name).toBe("neo");
    expect([unknown, catalog.get("n64")].sort(catalog.compare).map((c) => c.id)).toEqual(["n64", "neo"]);
  });

  it("has short labels for the crowded strip", () => {
    expect(catalog.get("gcn").short).toBe("GC");
    expect(catalog.get("og xbox").short).toBe("Xbox");
    expect(catalog.get(APPS).name).toBe("Apps & Tools");
  });
});

describe("buildTabs", () => {
  const settings = { showHomeTab: true, consoleTabOrder: "manufacturerThenYear" as const };

  it("is Home, All, consoles with installed games, Discover, Downloads", () => {
    const tabs = buildTabs(["n64", "ps1", "n64"], catalog, settings, 2);
    expect(tabs.map((t) => t.id)).toEqual(["home", "all", "console:n64", "console:ps1", "discover", "downloads"]);
    expect(tabs.find((t) => t.id === "console:n64")?.count).toBe(2);
    expect(tabs.at(-1)?.count).toBe(2);
  });

  it("leaves out Home when it's turned off", () => {
    expect(buildTabs([], catalog, { ...settings, showHomeTab: false }, 0).map((t) => t.id)).toEqual(["all", "discover", "downloads"]);
  });

  it("has no tab for a console without installed games", () => {
    expect(buildTabs(["n64"], catalog, settings, 0).some((t) => t.id === "console:ps1")).toBe(false);
  });

  it("orders console tabs by game count, then name", () => {
    const tabs = buildTabs(["ps1", "n64", "n64", "gcn", "gcn"], catalog, { ...settings, consoleTabOrder: "gameCount" }, 0);
    expect(tabs.filter((t) => t.kind === "console").map((t) => t.id)).toEqual(["console:gcn", "console:n64", "console:ps1"]);
  });

  it("orders console tabs alphabetically", () => {
    const tabs = buildTabs(["ps1", "n64", "gcn"], catalog, { ...settings, consoleTabOrder: "alphabetical" }, 0);
    expect(tabs.filter((t) => t.kind === "console").map((t) => t.label)).toEqual(["GameCube", "Nintendo 64", "PlayStation"]);
  });

  it("puts Apps & Tools and Other after every console, in any order", () => {
    for (const consoleTabOrder of ["manufacturerThenYear", "gameCount", "alphabetical"] as const) {
      const tabs = buildTabs([OTHER, OTHER, OTHER, APPS, "x360"], catalog, { ...settings, consoleTabOrder }, 0);
      expect(tabs.filter((t) => t.kind === "console").map((t) => t.id).slice(0, 1)).toEqual(["console:x360"]);
      expect(tabs.filter((t) => t.console?.special).length).toBe(2);
    }
  });

  it("keeps the selected tab by id, and moves to All when it disappears", () => {
    const before = buildTabs(["n64", "ps1"], catalog, settings, 0);
    const after = buildTabs(["n64"], catalog, settings, 0);
    expect(keepTab(after, "console:n64").id).toBe("console:n64");
    expect(keepTab(after, "console:ps1")).toEqual(ALL);
    expect(keepTab(before, "discover").id).toBe("discover");
  });
});

describe("cycleTab", () => {
  const tabs = buildTabs(["n64", "ps1"], catalog, { showHomeTab: true, consoleTabOrder: "manufacturerThenYear" }, 0);

  it("goes from the library tab to Discover, Downloads, and back to it", () => {
    expect(cycleTab(tabs, "console:n64", "console:n64", 1)).toBe("discover");
    expect(cycleTab(tabs, "discover", "console:n64", 1)).toBe("downloads");
    expect(cycleTab(tabs, "downloads", "console:n64", 1)).toBe("console:n64");
  });

  it("goes the other way round with LT", () => {
    expect(cycleTab(tabs, "console:ps1", "console:ps1", -1)).toBe("downloads");
    expect(cycleTab(tabs, "downloads", "console:ps1", -1)).toBe("discover");
    expect(cycleTab(tabs, "discover", "console:ps1", -1)).toBe("console:ps1");
  });

  it("comes back to All when the console's tab has gone", () => {
    expect(cycleTab(tabs, "downloads", "console:gcn", 1)).toBe("all");
  });
});
