import { expect, test } from "vitest";
import { consolesMatching, formsOf } from "./console-forms";

test("a console is a home console, a handheld, both (the Switch) or neither", () => {
  expect(formsOf("n64")).toEqual(["home"]);
  expect(formsOf("gba")).toEqual(["handheld"]);
  expect(formsOf("switch")).toEqual(["home", "handheld"]);
  expect(formsOf("pc")).toEqual([]);
  // One the catalog added since: at home until said otherwise.
  expect(formsOf("ps5")).toEqual(["home"]);
});

test("consoles narrow to a maker's, a kind's, or both", () => {
  const consoles = [
    { id: "n64", brand: "Nintendo" },
    { id: "gba", brand: "Nintendo" },
    { id: "switch", brand: "Nintendo" },
    { id: "psp", brand: "PlayStation" },
    { id: "ps2", brand: "PlayStation" },
    { id: "pc", brand: "OtherPlatforms" },
  ];
  const ids = (maker: string | null, form: "home" | "handheld" | null) => consolesMatching(consoles, maker, form).map((c) => c.id);
  expect(ids(null, null)).toEqual(["n64", "gba", "switch", "psp", "ps2", "pc"]);
  expect(ids("Nintendo", "handheld")).toEqual(["gba", "switch"]);
  expect(ids(null, "home")).toEqual(["n64", "switch", "ps2"]);
  expect(ids("PlayStation", null)).toEqual(["psp", "ps2"]);
  expect(ids("Xbox", null)).toEqual([]);
});
