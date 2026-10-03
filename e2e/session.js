/** Starts the real app under tauri-driver against the mock catalog. */
import { spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { remote } from "webdriverio";
import { startMockApi } from "./mock-api.js";

const binary = resolve(import.meta.dirname, "../apps/launcher/src-tauri/target/debug/quiver-launcher");

/**
 * One running copy of the app. Pass `api` to share a mock catalog between
 * copies, `data` to reopen a copy's data folder, and a distinct `port` for
 * each copy running at once.
 */
export async function launch({ env = {}, api: shared, data = mkdtempSync(join(tmpdir(), "quiver-data-")), port = 4444 } = {}) {
  const api = shared ?? (await startMockApi());
  const driver = spawn(join(homedir(), ".cargo/bin/tauri-driver"), ["--port", String(port), "--native-port", String(port + 1)], {
    env: {
      ...process.env,
      QUIVER_API: api.api,
      QUIVER_ACCOUNT_API: api.account,
      QUIVER_GITHUB_API: api.github,
      QUIVER_BROWSER: resolve(import.meta.dirname, "fake-browser.js"),
      QUIVER_DATA: data,
      QUIVER_V3_DATA: join(data, "none"),
      ...env,
    },
    stdio: "inherit",
  });
  await new Promise((r) => setTimeout(r, 1500));
  const app = await remote({
    hostname: "127.0.0.1",
    port,
    logLevel: "error",
    capabilities: { "wdio:enforceWebDriverClassic": true, "tauri:options": { application: binary } },
  });
  return {
    app,
    api,
    data,
    apps: join(data, "apps"),
    card: (slug) => app.$(`article[data-slug="${slug}"]`),
    until: (check, timeout = 20000) => app.waitUntil(check, { timeout, interval: 200 }),
    async close() {
      await app.deleteSession().catch(() => {});
      driver.kill();
      if (!shared) api.close();
      await new Promise((r) => setTimeout(r, 500));
    },
  };
}
