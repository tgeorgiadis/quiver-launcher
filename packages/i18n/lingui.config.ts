import { fileURLToPath } from "node:url";
import { defineConfig } from "@lingui/conf";
import { formatter } from "@lingui/format-po";
import { LANGUAGES, SOURCE } from "./src/languages";

export default defineConfig({
  // Here, wherever it's read from: the launcher's (and the site's) lingui.config re-exports this one.
  rootDir: fileURLToPath(new URL(".", import.meta.url)),
  sourceLocale: SOURCE,
  locales: LANGUAGES.map((l) => l.code),
  fallbackLocales: { default: SOURCE },
  // The macros' `t` and <Trans> look up through this package, so whatever uses one (a test too) has English set up.
  runtimeConfigModule: ["@quiverlauncher/i18n", "i18n"],
  catalogs: [
    {
      path: "<rootDir>/locales/{locale}",
      include: ["<rootDir>/../../apps/launcher/src", "<rootDir>/../ui/src"],
      exclude: ["**/*.test.*", "**/node_modules/**"],
    },
  ],
  // Where each string is used, without line numbers, so a catalog changes only when its strings do.
  format: formatter({ lineNumbers: false }),
});
