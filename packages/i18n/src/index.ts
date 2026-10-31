/// <reference types="vite/client" />
/**
 * Quiver's translations, shared by the launcher and quiverlauncher.com: one
 * catalog per language in locales/, made from the English in the code by
 * `lingui extract`, and the language a page shows in. Catalogs are read here
 * as they are, with no build step, so anything built with Vite can use them.
 */
import { i18n, type Messages } from "@lingui/core";
import { compileMessage } from "@lingui/message-utils/compileMessage";
import { generateMessageId } from "@lingui/message-utils/generateMessageId";
import { LANGUAGES, SOURCE, type Language } from "./languages";

export { i18n };
export { LANGUAGES, SOURCE, type Language };

// A string with no translation yet shows its English, placeholders and plurals worked out here, even in a release build
// (which leaves the compiler out unless it's asked for).
i18n.setMessagesCompiler(compileMessage);
i18n.loadAndActivate({ locale: SOURCE, messages: {} });

/** Catalogs by path, each loading its .po file's text: what `import.meta.glob("…/*.po", { query: "?raw", import: "default" })` gives. */
export type Catalogs = Record<string, () => Promise<string>>;

// The launcher's and the shared components' strings; a site adds its own catalogs to `activate`.
const shared: Catalogs = import.meta.glob<string>("../locales/*.po", { query: "?raw", import: "default" });

/**
 * Our language closest to the first of `wanted` (BCP 47 tags, most wanted
 * first) we have one for: the same tag, or else the same language, so pt-PT
 * gets Brazilian Portuguese. English when none is near.
 */
export function pick(wanted: readonly (string | undefined)[]): Language {
  const base = (tag: string) => tag.toLowerCase().split("-")[0];
  for (const tag of wanted) {
    if (!tag) continue;
    const found =
      LANGUAGES.find((l) => l.code.toLowerCase() === tag.toLowerCase()) ?? LANGUAGES.find((l) => base(l.code) === base(tag));
    if (found) return found.code;
  }
  return SOURCE;
}

/**
 * The translations in a .po file's text, by the id Lingui gives each message
 * (from its English and context), compiled once here. A message with no
 * translation is left out, so it shows in English.
 */
export function parsePo(text: string): Messages {
  const messages: Messages = {};
  let entry: Record<string, string> = {};
  let field: string | null = null;
  const done = () => {
    if (entry.msgid && entry.msgstr) messages[generateMessageId(entry.msgid, entry.msgctxt ?? "")] = compileMessage(entry.msgstr);
    entry = {};
    field = null;
  };
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    // Comments, and entries no string uses any more (#~).
    if (!line || line.startsWith("#")) {
      if (!line) done();
      continue;
    }
    const start = /^(msgctxt|msgid|msgstr)\s+(".*")$/.exec(line);
    if (start) {
      // A new entry's first line with no blank line between: the last one ends here.
      if (start[1] !== "msgstr" && "msgstr" in entry) done();
      field = start[1];
      entry[field] = JSON.parse(start[2]);
    } else if (field && line.startsWith('"')) {
      // A long string carried on over the next lines.
      entry[field] += JSON.parse(line);
    }
  }
  done();
  return messages;
}

/** The catalog for `language` among `catalogs` (its file named after the language's code), read; none, nothing. */
async function load(catalogs: Catalogs, language: Language): Promise<Messages> {
  const path = Object.keys(catalogs).find((p) => p.endsWith(`/${language}.po`));
  return path ? parsePo(await catalogs[path]()) : {};
}

/**
 * Shows `language` from now on, its catalogs loaded first: the shared one
 * (unless `shared` is false, for a page with none of its strings), and
 * `more` (a site's own). The page's `lang` follows, for screen readers and
 * hyphenation.
 */
export async function activate(language: Language, more: Catalogs[] = [], { shared: withShared = true } = {}): Promise<void> {
  const catalogs = withShared ? [shared, ...more] : more;
  const loaded = language === SOURCE ? [] : await Promise.all(catalogs.map((c) => load(c, language)));
  i18n.loadAndActivate({ locale: language, messages: Object.assign({}, ...loaded) });
  document.documentElement.lang = language;
}
