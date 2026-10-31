import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { compareApps, sortTitle, type LibrarySort, type SortFacts } from "./sort";

const app = (projectName: string, game?: string) => ({ id: projectName, projectName, games: game ? [{ id: game, slug: game, title: game }] : [] }) as unknown as Entry;
const sorted = (sort: LibrarySort, apps: Entry[], facts: Record<string, Partial<SortFacts>> = {}) =>
  [...apps].sort((a, b) => compareApps(sort, a, b, (e) => ({ installed: false, ...facts[e.id] }))).map((e) => e.projectName);

describe("library sorting", () => {
  it("leaves out a leading article", () => {
    expect(sortTitle("The Legend of Zelda")).toBe("Legend of Zelda");
    expect(sortTitle("A Link to the Past")).toBe("Link to the Past");
    expect(sortTitle("An Untitled Goose")).toBe("Untitled Goose");
    expect(sortTitle("Theme Park")).toBe("Theme Park");
    expect(sortTitle("A")).toBe("A");
    expect(sorted("name", [app("The Zelda Port"), app("Banjo"), app("A Mario Port"), app("ship 2")])).toEqual(["Banjo", "A Mario Port", "ship 2", "The Zelda Port"]);
  });

  it("sorts by game, then name", () => {
    const apps = [app("Ship of Harkinian", "The Legend of Zelda: Ocarina of Time"), app("BK Recomp", "Banjo-Kazooie"), app("2 Ship 2 Harkinian", "The Legend of Zelda: Majora's Mask"), app("Mine")];
    expect(sorted("game", apps)).toEqual(["BK Recomp", "2 Ship 2 Harkinian", "Ship of Harkinian", "Mine"]);
  });

  it("puts installed apps first or last", () => {
    const apps = [app("C"), app("A"), app("B")];
    const facts = { B: { installed: true }, C: { installed: true } };
    expect(sorted("installed", apps, facts)).toEqual(["B", "C", "A"]);
    expect(sorted("not-installed", apps, facts)).toEqual(["A", "B", "C"]);
  });

  it("puts the most recently played or added first", () => {
    const apps = [app("A"), app("B"), app("C")];
    expect(sorted("played", apps, { B: { lastPlayed: 5 }, C: { lastPlayed: 9 } })).toEqual(["C", "B", "A"]);
    expect(sorted("added", apps, { A: { addedAt: 3 }, B: { addedAt: 1 } })).toEqual(["A", "B", "C"]);
  });

  it("puts the app with the newest release first, by its verified one when it has one", () => {
    const released = (name: string, verified?: number, last?: number) => ({ ...app(name), ...(verified ? { verified: { releasedAt: verified } } : {}), lastReleaseAt: last }) as Entry;
    // B's newest release isn't verified yet: its verified one counts.
    const apps = [released("A", 3), released("B", 2, 9), released("C", undefined, 5), released("D")];
    expect(sorted("updated", apps)).toEqual(["C", "A", "B", "D"]);
  });
});
