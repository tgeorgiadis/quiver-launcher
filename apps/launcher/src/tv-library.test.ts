import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { jumpLetters, letterOf, libraryConsoles, pickLibrary, type TvApp, type TvPick } from "./tv-library";
import type { Collection } from "./sync";

const app = (name: string, more: Partial<TvApp> & { game?: string; consoles?: string[]; tags?: string[] } = {}): TvApp => ({
  entry: { id: name, projectName: name, games: more.game ? [{ id: more.game, slug: more.game, title: more.game }] : [], tags: [], consoles: more.consoles ?? [] } as unknown as Entry,
  addedAt: more.addedAt ?? 0,
  installed: more.installed ?? false,
  lastPlayed: more.lastPlayed,
  update: more.update ?? false,
  tags: more.tags ?? [],
});
const pick = (more: Partial<TvPick> = {}): TvPick => ({ search: "", show: "all", console: "", sort: "name", ...more });
const names = (apps: TvApp[]) => apps.map((a) => a.entry.projectName);

const apps = [
  app("Ship of Harkinian", { game: "The Legend of Zelda: Ocarina of Time", consoles: ["n64"], installed: true, lastPlayed: 5 }),
  app("Dusklight", { game: "The Legend of Zelda: Twilight Princess", consoles: ["gcn"], installed: true, update: true, lastPlayed: 9 }),
  app("Starship", { game: "Star Fox 64", consoles: ["n64"], addedAt: 3 }),
  app("Bloodborne PC", { consoles: ["ps4"], tags: ["horror"] }),
];

describe("TV mode's library", () => {
  it("finds apps by their name, their game's, or a tag, every word", () => {
    expect(names(pickLibrary(apps, pick({ search: "zelda" })))).toEqual(["Dusklight", "Ship of Harkinian"]);
    expect(names(pickLibrary(apps, pick({ search: "zelda twilight" })))).toEqual(["Dusklight"]);
    expect(names(pickLibrary(apps, pick({ search: "HORROR" })))).toEqual(["Bloodborne PC"]);
  });

  it("shows a tab, a console, or both", () => {
    expect(names(pickLibrary(apps, pick({ show: "installed" })))).toEqual(["Dusklight", "Ship of Harkinian"]);
    expect(names(pickLibrary(apps, pick({ show: "not-installed" })))).toEqual(["Bloodborne PC", "Starship"]);
    expect(names(pickLibrary(apps, pick({ show: "updates" })))).toEqual(["Dusklight"]);
    expect(names(pickLibrary(apps, pick({ console: "n64" })))).toEqual(["Ship of Harkinian", "Starship"]);
    expect(names(pickLibrary(apps, pick({ console: "n64", show: "installed" })))).toEqual(["Ship of Harkinian"]);
  });

  it("shows a playlist's apps, picked or matching its filters", () => {
    const playlists = [{ key: "c_1", name: "Fox", tags: [], consoles: [], apps: ["Starship"] } as unknown as Collection, { key: "c_2", name: "GameCube", tags: [], consoles: ["gcn"] } as unknown as Collection];
    expect(names(pickLibrary(apps, pick({ show: "c_1" }), playlists))).toEqual(["Starship"]);
    expect(names(pickLibrary(apps, pick({ show: "c_2" }), playlists))).toEqual(["Dusklight"]);
  });

  it("sorts as the library does", () => {
    expect(names(pickLibrary(apps, pick({ sort: "played" })))).toEqual(["Dusklight", "Ship of Harkinian", "Bloodborne PC", "Starship"]);
    expect(names(pickLibrary(apps, pick({ sort: "added" })))[0]).toBe("Starship");
  });

  it("files a title under its letter as sorting reads it", () => {
    expect(letterOf("The Legend of Zelda")).toBe("L");
    expect(letterOf("élan")).toBe("E");
    expect(letterOf("2 Ship 2 Harkinian")).toBe("#");
    expect(letterOf("Окно")).toBe("О");
  });

  it("offers the letters of a list by name or game, each at its first app", () => {
    const byName = pickLibrary(apps, pick({ sort: "name" })).map((a) => a.entry);
    expect(jumpLetters(byName, "name")).toEqual([
      { letter: "B", index: 0 },
      { letter: "D", index: 1 },
      { letter: "S", index: 2 },
    ]);
    const byGame = pickLibrary(apps, pick({ sort: "game" })).map((a) => a.entry);
    expect(jumpLetters(byGame, "game").map((l) => l.letter)).toEqual(["B", "L", "S"]);
    expect(jumpLetters(byName, "played")).toEqual([]);
  });

  it("lists the library's consoles, the most apps first", () => {
    expect(libraryConsoles(apps)).toEqual([
      { id: "n64", apps: 2 },
      { id: "gcn", apps: 1 },
      { id: "ps4", apps: 1 },
    ]);
  });
});
