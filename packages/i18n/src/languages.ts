/** The language Quiver is written in: every string is English in the code, and a translation falls back to it. */
export const SOURCE = "en";

/**
 * The languages Quiver comes in, each named in itself. Adding one: its code
 * here (a BCP 47 tag, like the file's name), then `pnpm --filter
 * @quiverlauncher/i18n extract` makes its file in locales/ to translate.
 */
export const LANGUAGES = [
  { code: "en", name: "English" },
  { code: "pt-BR", name: "Português (Brasil)" },
  // Latin America's Spanish: most of our Spanish-speaking players are there, and Spain's get it too.
  { code: "es-419", name: "Español (Latinoamérica)" },
  { code: "de", name: "Deutsch" },
  { code: "fr", name: "Français" },
  { code: "ru", name: "Русский" },
  { code: "it", name: "Italiano" },
  { code: "ko", name: "한국어" },
] as const;

export type Language = (typeof LANGUAGES)[number]["code"];
