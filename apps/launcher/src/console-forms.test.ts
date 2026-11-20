import { expect, test } from "vitest";
import { consolesMatching, formsOf, type ConsoleForm } from "./console-forms";

test("a console is a home console, a handheld, both (the Switch) or neither", () => {
  expect(formsOf("n64")).toEqual(["home"]);
  expect(formsOf("gba")).toEqual(["handheld"]);
  expect(formsOf("switch")).toEqual(["home", "handheld"]);
  expect(formsOf("pc")).toEqual([]);
  // One the catalog added since: at home until said otherwise.
  expect(formsOf("ps5")).toEqual(["home"]);
});

test("consoles narrow to any of the makers picked and any of the kinds, or both", () => {
  const consoles = [
    { id: "n64", brand: "Nintendo" },
    { id: "gba", brand: "Nintendo" },
    { id: "switch", brand: "Nintendo" },
    { id: "gg", brand: "Sega" },
    { id: "psp", brand: "PlayStation" },
    { id: "ps2", brand: "PlayStation" },
    { id: "pc", brand: "OtherPlatforms" },
  ];
  const ids = (makers: string[], forms: ConsoleForm[]) => consolesMatching(consoles, makers, forms).map((c) => c.id);
  expect(ids([], [])).toEqual(["n64", "gba", "switch", "gg", "psp", "ps2", "pc"]);
  expect(ids(["Nintendo"], ["handheld"])).toEqual(["gba", "switch"]);
  expect(ids(["Nintendo", "Sega"], ["handheld"])).toEqual(["gba", "switch", "gg"]);
  expect(ids([], ["home"])).toEqual(["n64", "switch", "ps2"]);
  expect(ids([], ["home", "handheld"])).toEqual(["n64", "gba", "switch", "gg", "psp", "ps2"]);
  expect(ids(["PlayStation"], [])).toEqual(["psp", "ps2"]);
  expect(ids(["Xbox"], [])).toEqual([]);
});
