import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { jumpLetters, letterOf } from "./letters";

const entry = (name: string, game?: string) => ({ id: name, projectName: name, games: game ? [{ id: game, slug: game, title: game }] : [] }) as unknown as Entry;

describe("the letters of a list", () => {
  it("files a title under its letter as sorting reads it", () => {
    expect(letterOf("The Legend of Zelda")).toBe("L");
    expect(letterOf("élan")).toBe("E");
    expect(letterOf("2 Ship 2 Harkinian")).toBe("#");
    expect(letterOf("Окно")).toBe("О");
  });

  it("offers the letters of a list by name or game, each at its first app", () => {
    const byName = [entry("Bloodborne PC"), entry("Dusklight"), entry("Ship of Harkinian"), entry("Starship")];
    expect(jumpLetters(byName, "name")).toEqual([
      { letter: "B", index: 0 },
      { letter: "D", index: 1 },
      { letter: "S", index: 2 },
    ]);
    const byGame = [entry("Bloodborne PC"), entry("Ship of Harkinian", "The Legend of Zelda: Ocarina of Time"), entry("Dusklight", "The Legend of Zelda: Twilight Princess"), entry("Starship", "Star Fox 64")];
    expect(jumpLetters(byGame, "game").map((l) => l.letter)).toEqual(["B", "L", "S"]);
    expect(jumpLetters(byName, "played")).toEqual([]);
  });
});
