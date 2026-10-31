/**
 * Builds the app for the journeys (a debug build, no installer) with a test
 * PostHog token, so usage data runs as in a release; session.js sends it to
 * the mock API, and a test token is never sent to PostHog itself.
 */
import { spawnSync } from "node:child_process";

const { status } = spawnSync("pnpm", ["--filter", "@quiver/launcher", "tauri", "build", "--debug", "--no-bundle"], {
  stdio: "inherit",
  shell: process.platform === "win32",
  env: { ...process.env, VITE_POSTHOG_PROJECT_TOKEN: "phc_test", VITE_POSTHOG_DEV: "" },
});
process.exit(status ?? 1);
