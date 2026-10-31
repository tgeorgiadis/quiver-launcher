import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { GRID_GROUP_BYS, gridGroupBy, isStack, stacks, type PortFacts } from "./group";

const app = (projectName: string, games: string[] = [], recommended = 0) =>
  ({ id: projectName, projectName, recommended, games: games.map((g) => ({ id: g, slug: g, title: g })) }) as unknown as Entry;
const cards = (apps: Entry[], facts: Record<string, Partial<PortFacts>> = {}) =>
  stacks(apps, (e) => ({ installed: false, ...facts[e.id] })).map((c) => (isStack(c) ? `${c.game.title}: ${c.ports.map((p) => p.projectName).join(", ")}` : c.projectName));

describe("a game's ports as one card", () => {
  it("puts a game's ports on one card where its first one was, and an app of no game on its own", () => {
    const apps = [app("BK Recomp", ["Banjo"]), app("Mine"), app("Ship of Harkinian", ["Zelda"]), app("Banjo PC", ["Banjo"])];
    expect(cards(apps)).toEqual(["Banjo: BK Recomp, Banjo PC", "Mine", "Zelda: Ship of Harkinian"]);
  });

  it("makes a card of a game with only one port too", () => {
    expect(stacks([app("Ship of Harkinian", ["Zelda"])], () => ({ installed: false })).every(isStack)).toBe(true);
  });

  it("puts an app of several games under its first, once", () => {
    expect(cards([app("Collection", ["Sonic 1", "Sonic 2"]), app("Sonic 2 PC", ["Sonic 2"])])).toEqual(["Sonic 1: Collection", "Sonic 2: Sonic 2 PC"]);
  });

  it("puts the port played last first, then those installed, then the best recommended", () => {
    const apps = [app("A", ["G"], 1), app("B", ["G"], 9), app("C", ["G"], 5), app("D", ["G"], 2)];
    expect(cards(apps)).toEqual(["G: B, C, D, A"]);
    expect(cards(apps, { A: { installed: true } })).toEqual(["G: A, B, C, D"]);
    expect(cards(apps, { A: { installed: true }, D: { installed: true, lastPlayed: 3 } })).toEqual(["G: D, A, B, C"]);
  });

  it("puts a port installed since the last was played first, till another is played", () => {
    const apps = [app("A", ["G"]), app("B", ["G"]), app("C", ["G"])];
    const played = { A: { installed: true, lastPlayed: 5, installedAt: 1 } };
    expect(cards(apps, { ...played, C: { installed: true, installedAt: 7 } })).toEqual(["G: C, A, B"]);
    expect(cards(apps, { A: { installed: true, lastPlayed: 9, installedAt: 1 }, C: { installed: true, installedAt: 7 } })).toEqual(["G: A, C, B"]);
  });

  it("leaves sections by game to the sidebar", () => {
    expect(GRID_GROUP_BYS.map(([key]) => key)).not.toContain("game");
    expect(gridGroupBy("game")).toBeUndefined();
    expect(gridGroupBy("console")).toBe("console");
  });
});
