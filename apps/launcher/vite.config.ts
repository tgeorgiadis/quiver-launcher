import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { getConfig } from "@lingui/conf";
import linguiMacro from "@lingui/babel-plugin-lingui-macro";

// The translations' config (@quiverlauncher/i18n), named here: Lingui would look for it where Vite was started from,
// which for the e2e tests is another folder.
const linguiConfig = getConfig({ configPath: fileURLToPath(new URL("../../packages/i18n/lingui.config.ts", import.meta.url)) });

export default defineConfig({
  plugins: [
    // Translations: the macros turn `t` and <Trans> into catalog lookups, keeping the English so a string not yet
    // translated still shows.
    react({ babel: { plugins: [[linguiMacro, { descriptorFields: "message", linguiConfig }]] } }),
  ],
  clearScreen: false,
  server: { port: 1420, strictPort: true },
});
