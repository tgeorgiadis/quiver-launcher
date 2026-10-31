import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { getConfig } from "@lingui/conf";
import linguiMacro from "@lingui/babel-plugin-lingui-macro";

// The components' strings go through Lingui's macros (@quiverlauncher/i18n), as in the launcher's and the site's builds.
const linguiConfig = getConfig({ configPath: fileURLToPath(new URL("../i18n/lingui.config.ts", import.meta.url)) });

export default defineConfig({
  plugins: [react({ babel: { plugins: [[linguiMacro, { descriptorFields: "message", linguiConfig }]] } })],
});
