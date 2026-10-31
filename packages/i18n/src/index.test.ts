import { describe, expect, it } from "vitest";
import { generateMessageId } from "@lingui/message-utils/generateMessageId";
import { parsePo, pick } from "./index";

describe("pick", () => {
  it("takes the same tag", () => expect(pick(["pt-BR"])).toBe("pt-BR"));
  it("takes the same language from another country", () => expect(pick(["pt-PT", "en"])).toBe("pt-BR"));
  it("ignores case", () => expect(pick(["PT-br"])).toBe("pt-BR"));
  it("goes down the list to one it has", () => expect(pick(["ja-JP", "pt"])).toBe("pt-BR"));
  it("falls back to English", () => expect(pick(["ja-JP", undefined])).toBe("en"));
  it("keeps English first when it's wanted first", () => expect(pick(["en-US", "pt-BR"])).toBe("en"));
});

describe("parsePo", () => {
  const po = [
    'msgid ""',
    'msgstr ""',
    '"Language: pt-BR\\n"',
    "",
    "#. Interface size",
    "#: ../../apps/launcher/src/settings-page.tsx",
    'msgid "Large"',
    'msgstr "Grande"',
    "",
    'msgctxt "tab"',
    'msgid "Installed"',
    'msgstr "Instalados"',
    "",
    'msgid "Not yet"',
    'msgstr ""',
    "",
    'msgid "Say \\"{name}\\""',
    'msgstr ""',
    '"Diga \\"{name}\\""',
    "",
    '#~ msgid "Gone"',
    '#~ msgstr "Sumiu"',
  ].join("\n");

  it("reads each translation under Lingui's id for it", async () => {
    const { i18n } = await import("./index");
    i18n.loadAndActivate({ locale: "pt-BR", messages: parsePo(po) });
    try {
      expect(i18n._({ id: generateMessageId("Large"), message: "Large" })).toBe("Grande");
      expect(i18n._({ id: generateMessageId("Installed", "tab"), message: "Installed" })).toBe("Instalados");
      expect(i18n._({ id: generateMessageId('Say "{name}"'), message: 'Say "{name}"', values: { name: "Ana" } })).toBe('Diga "Ana"');
    } finally {
      i18n.loadAndActivate({ locale: "en", messages: {} });
    }
  });

  it("leaves out what isn't translated, and what nothing uses any more", () => {
    const ids = Object.keys(parsePo(po));
    expect(ids).toHaveLength(3);
    expect(ids).not.toContain(generateMessageId("Not yet"));
    expect(ids).not.toContain(generateMessageId("Gone"));
  });
});
