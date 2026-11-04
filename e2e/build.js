/**
 * Builds the app for the journeys (a debug build, no installer) with usage data
 * on, as in a release; session.js sends it to the mock API.
 */
import { spawnSync } from "node:child_process";

const { status } = spawnSync("pnpm", ["--filter", "@quiver/launcher", "tauri", "build", "--debug", "--no-bundle"], {
  stdio: "inherit",
  shell: process.platform === "win32",
  env: { ...process.env, VITE_USAGE_DATA: "1", VITE_USAGE_DEV: "", VITE_SENTRY_DSN: "" },
});
process.exit(status ?? 1);
