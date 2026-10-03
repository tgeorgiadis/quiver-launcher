/**
 * Starts the real app against the mock catalog: under tauri-driver, or on
 * Windows on its own with msedgedriver attached to its WebView2 (whose
 * runtime no longer takes a debugging port from the driver).
 */
import { spawn } from "node:child_process";
import { mkdtempSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { remote } from "webdriverio";
import { startMockApi } from "./mock-api.js";

const exe = process.platform === "win32" ? ".exe" : "";
const binary = resolve(import.meta.dirname, `../apps/launcher/src-tauri/target/debug/quiver-launcher${exe}`);
const windows = process.platform === "win32";

/**
 * One running copy of the app. Pass `api` to share a mock catalog between
 * copies, `data` to reopen a copy's data folder, and a distinct `port` for
 * each copy running at once.
 */
export async function launch({ env: extra = {}, api: shared, data = mkdtempSync(join(tmpdir(), "quiver-data-")), port = 4444 } = {}) {
  const api = shared ?? (await startMockApi());
  const env = {
    ...process.env,
    QUIVER_API: api.api,
    QUIVER_ACCOUNT_API: api.account,
    QUIVER_GITHUB_API: api.github,
    QUIVER_BROWSER: resolve(import.meta.dirname, "fake-browser.js"),
    QUIVER_DATA: data,
    QUIVER_V3_DATA: join(data, "none"),
    ...extra,
  };
  const debugPort = port + 1;
  const processes = windows
    ? [
        spawn(binary, [], { env: { ...env, QUIVER_DEBUG_PORT: String(debugPort) }, stdio: "inherit" }),
        // msedgedriver, matching the WebView2 version (NATIVE_DRIVER in CI).
        spawn(process.env.NATIVE_DRIVER ?? "msedgedriver", [`--port=${port}`], { stdio: "inherit", shell: true }),
      ]
    : [spawn(join(homedir(), ".cargo/bin/tauri-driver"), ["--port", String(port), "--native-port", String(debugPort)], { env, stdio: "inherit" })];
  // taskkill /T also stops what the driver's .cmd wrapper started.
  const kill = () => processes.forEach((p) => (windows ? spawn("taskkill", ["/pid", String(p.pid), "/T", "/F"]) : p.kill()));
  let app;
  try {
    if (windows) await debuggerUp(debugPort);
    else await new Promise((r) => setTimeout(r, 1500));
    app = await remote({
      hostname: "127.0.0.1",
      port,
      logLevel: "error",
      capabilities: windows
        ? { browserName: "webview2", "wdio:enforceWebDriverClassic": true, "ms:edgeOptions": { debuggerAddress: `127.0.0.1:${debugPort}` } }
        : { "wdio:enforceWebDriverClassic": true, "tauri:options": { application: binary } },
    });
  } catch (error) {
    // Don't leave the app or driver holding the test process open.
    kill();
    if (!shared) api.close();
    throw error;
  }
  return {
    app,
    api,
    data,
    apps: join(data, "apps"),
    card: (slug) => app.$(`article[data-slug="${slug}"]`),
    until: (check, timeout = 20000) => app.waitUntil(check, { timeout, interval: 200 }),
    async close() {
      await app.deleteSession().catch(() => {});
      kill();
      if (!shared) api.close();
      await new Promise((r) => setTimeout(r, 500));
    },
  };
}

/** Waits for WebView2's debugging port, which the app opens as it starts. */
async function debuggerUp(port) {
  for (const deadline = Date.now() + 60000; Date.now() < deadline; await new Promise((r) => setTimeout(r, 250))) {
    if (await fetch(`http://127.0.0.1:${port}/json/version`).then((r) => r.ok, () => false)) return;
  }
  throw new Error(`WebView2 didn't open its debugging port ${port}`);
}
