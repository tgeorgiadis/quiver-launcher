import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { getConfig } from "@lingui/conf";
import linguiMacro from "@lingui/babel-plugin-lingui-macro";
import { sentryVitePlugin } from "@sentry/vite-plugin";

// The translations' config (@quiverlauncher/i18n), named here: Lingui would look for it where Vite was started from,
// which for the e2e tests is another folder.
const linguiConfig = getConfig({ configPath: fileURLToPath(new URL("../../packages/i18n/lingui.config.ts", import.meta.url)) });

// A release build uploads its source maps for error reports (telemetry.ts), and ships none of them.
const sentryToken = process.env.SENTRY_AUTH_TOKEN;
const { version } = JSON.parse(readFileSync(new URL("./src-tauri/tauri.conf.json", import.meta.url), "utf8"));

export default defineConfig({
  plugins: [
    // Translations: the macros turn `t` and <Trans> into catalog lookups, keeping the English so a string not yet
    // translated still shows.
    react({ babel: { plugins: [[linguiMacro, { descriptorFields: "message", linguiConfig }]] } }),
    ...(sentryToken
      ? [
          sentryVitePlugin({
            org: "quiver-launcher",
            project: "launcher",
            url: "https://de.sentry.io",
            authToken: sentryToken,
            release: { name: `quiver-launcher@${version}` },
            sourcemaps: { filesToDeleteAfterUpload: ["dist/**/*.map"] },
            telemetry: false,
          }),
        ]
      : []),
  ],
  build: { sourcemap: sentryToken ? "hidden" : false },
  clearScreen: false,
  server: { port: 1420, strictPort: true },
});
