import { readdirSync, readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { parsePo } from "./index";
import { LANGUAGES, SOURCE } from "./languages";

const locales = new URL("../locales/", import.meta.url);
const count = (text: string) => (text.match(/^msgid "[^"]/gm) ?? []).length;

describe("each catalog", () => {
  it("is one of our languages", () => {
    const codes = LANGUAGES.map((l) => `${l.code}.po`);
    for (const file of readdirSync(locales)) expect(codes).toContain(file);
  });
  for (const { code } of LANGUAGES.filter((l) => l.code !== SOURCE)) {
    it(`reads whole: ${code}`, () => {
      const text = readFileSync(new URL(`${code}.po`, locales), "utf8");
      // Every string translated comes through, none lost to a line read wrong.
      const translated = (text.match(/^msgstr "[^"]/gm) ?? []).length - (text.match(/^#~ msgstr "[^"]/gm) ?? []).length;
      expect(Object.keys(parsePo(text))).toHaveLength(translated);
      expect(translated).toBeLessThanOrEqual(count(text));
    });
  }
});
