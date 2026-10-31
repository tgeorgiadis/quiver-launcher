/**
 * Starts the real app against the mock catalog: under tauri-driver, or on
 * Windows on its own with msedgedriver attached to its WebView2 (whose
 * runtime no longer takes a debugging port from the driver). On Linux with no
 * display (CI), each copy gets a virtual one of its own, so test files can run
 * side by side without taking each other's focus.
 */
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { connect, createServer } from "node:net";
import { homedir, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { remote } from "webdriverio";
import { startMockApi } from "./mock-api.js";
import { focusWindow } from "./x11.js";

const exe = process.platform === "win32" ? ".exe" : "";
const binary = resolve(import.meta.dirname, `../apps/launcher/src-tauri/target/debug/quiver-launcher${exe}`);
const windows = process.platform === "win32";

/**
 * Each copy still running, as what stops it. A test file that ends without closing one (stopped by run.js at its
 * failures or by Ctrl+C, which skips after(), or a test that never closed what it launched) stops it on its way out.
 * Left running, the app and its driver held the file's output open, so `node --test` waited on the file for good.
 */
const running = new Set();
process.on("exit", () => running.forEach((stop) => stop()));
for (const signal of ["SIGINT", "SIGTERM"]) process.once(signal, () => process.exit(1));

/**
 * One running copy of the app. Pass `api` to share a mock catalog between
 * copies, and `data` to reopen a copy's data folder. Each copy takes ports no
 * other is using, so any number can run at once. The first start's setup
 * (setup.tsx) is taken as done, as the tests start from the library, unless
 * `setup`: setup.test.js goes through it.
 */
export async function launch({ env: extra = {}, api: shared, data = mkdtempSync(join(tmpdir(), "quiver-data-")), setup = false } = {}) {
  if (!setup) {
    // Over the settings a test saved before it.
    const file = join(data, "settings.json");
    const saved = existsSync(file) ? JSON.parse(readFileSync(file, "utf8")) : {};
    mkdirSync(data, { recursive: true });
    writeFileSync(file, JSON.stringify({ ...saved, setupDone: true }));
  }
  const api = shared ?? (await startMockApi());
  const port = await freePort();
  const debugPort = await freePort();
  const xvfb = windows || process.env.DISPLAY ? null : await virtualDisplay();
  const env = {
    ...process.env,
    QUIVER_API: api.api,
    QUIVER_CONVEX: api.convex,
    QUIVER_ACCOUNT_API: api.account,
    QUIVER_GITHUB_API: api.github,
    QUIVER_GITLAB_API: api.gitlab,
    QUIVER_THUNDERSTORE: api.thunderstore,
    QUIVER_GAMEBANANA: api.gamebanana,
    // Usage data goes to the mock too, never to PostHog (the e2e build has a test token).
    QUIVER_POSTHOG_HOST: api.posthog,
    QUIVER_BROWSER: resolve(import.meta.dirname, "fake-browser.js"),
    QUIVER_SITE: api.site,
    QUIVER_UPDATE_API: api.updates,
    QUIVER_UPDATE_PUBKEY: api.updatePublicKey,
    QUIVER_DATA: data,
    QUIVER_V3_DATA: join(data, "none"),
    // On it, not on a Wayland desktop that GTK would find on its own (WSLg's, even with WAYLAND_DISPLAY unset).
    ...(xvfb && { DISPLAY: xvfb.display, GDK_BACKEND: "x11" }),
    // WebKitGTK's storage (localStorage, where PostHog keeps who it's sending as) beside the
    // data folder rather than shared by every copy, as on Windows, so copies running at once
    // don't share it.
    ...(!windows && { XDG_DATA_HOME: `${data}-webview` }),
    ...extra,
  };
  const processes = windows
    ? [
        spawn(binary, [], { env: { ...env, QUIVER_DEBUG_PORT: String(debugPort) }, stdio: "inherit" }),
        // msedgedriver, matching the WebView2 version (NATIVE_DRIVER in CI).
        spawn(process.env.NATIVE_DRIVER ?? "msedgedriver", [`--port=${port}`], { stdio: "inherit", shell: true }),
      ]
    : [spawn(join(homedir(), ".cargo/bin/tauri-driver"), ["--port", String(port), "--native-port", String(debugPort)], { env, stdio: "inherit" })];
  // taskkill /T also stops what the driver's .cmd wrapper started.
  const kill = () => processes.forEach((p) => (windows ? spawn("taskkill", ["/pid", String(p.pid), "/T", "/F"]) : p.kill()));
  const stop = () => (kill(), xvfb?.server.kill());
  running.add(stop);
  let app;
  try {
    if (windows) await debuggerUp(debugPort);
    else await listening(port);
    app = await remote({
      hostname: "127.0.0.1",
      port,
      logLevel: "error",
      // The app can take a while to show its first screen on a CI runner; an action right after launch waits for it.
      waitforTimeout: 20000,
      capabilities: windows
        ? { browserName: "webview2", "wdio:enforceWebDriverClassic": true, "ms:edgeOptions": { debuggerAddress: `127.0.0.1:${debugPort}` } }
        : { "wdio:enforceWebDriverClassic": true, "tauri:options": { application: binary } },
    });
    // In front on its display, as a window manager would put it, so it shows its focus and hears a controller (x11.js).
    if (xvfb && !(await focusWindow(xvfb.display).catch(() => false))) console.warn(`The app's window couldn't be given the focus on ${xvfb.display}`);
  } catch (error) {
    // Don't leave the app or driver holding the test process open.
    stop();
    running.delete(stop);
    if (!shared) api.close();
    throw error;
  }
  // WebKitGTK's driver leaves out text it thinks is clipped (an ellipsised title, a
  // line-clamped description) and the line breaks between blocks, so read what
  // the page shows instead, as WebView2's driver does.
  if (!windows) await app.overwriteCommand("getText", async function () { return (await app.execute((el) => el.innerText, this)).trim(); }, true);
  // Its clear empties a field without an input event, so React put the field's text back before the first key and the
  // typing went after it: "t", then "test po", read "ttest po". Selected and deleted as a player would, every change is
  // heard.
  if (!windows)
    await app.overwriteCommand(
      "setValue",
      async function (_setValue, value) {
        await app.execute((el) => (el.focus(), el.select()), this);
        await app.keys("Backspace");
        if (value !== "") await this.addValue(value);
      },
      true,
    );
  return {
    app,
    api,
    data,
    apps: join(data, "apps"),
    // The pointer goes to a card as a player's would: a library card shows its buttons on hover.
    async card(slug) {
      const card = await app.$(`article[data-slug="${slug}"]`);
      // Not one under an open page: moving to it would be refused, after several tries.
      if (await card.isDisplayed()) await card.scrollIntoView({ block: "center" }).then(() => card.moveTo()).catch(() => {});
      return card;
    },
    until: (check, timeout = 20000) => app.waitUntil(check, { timeout, interval: 200 }),
    // The library's tabs and playlists, as its tab row lists them or, where they don't fit beside the tools, its Show
    // menu (library.tsx): their names, the one open, and one opened as a player would.
    tabs: () =>
      app.execute(() => {
        const nav = document.querySelector("nav[aria-label=Playlists]");
        if (nav) return [...nav.querySelectorAll(".playlist-tab > button")].map((b) => b.innerText.trim());
        return [...(document.querySelector(".library-show select")?.options ?? [])].map((o) => o.label);
      }),
    openTab: () =>
      app.execute(() => {
        const nav = document.querySelector("nav[aria-label=Playlists]");
        return nav ? nav.querySelector("button[aria-pressed=true]")?.innerText.trim() : document.querySelector(".library-show select")?.selectedOptions[0]?.label;
      }),
    async showTab(name) {
      const nav = await app.$("nav[aria-label=Playlists]");
      if (await nav.isExisting()) await (await nav.$(`button=${name}`)).click();
      else await (await app.$(".library-show select")).selectByVisibleText(name);
    },
    // A controller's button going down or up, as gamepad.rs sends it. The app ignores one while its window isn't in
    // front (a game is), so a test says so rather than waiting on what the press never did.
    async pad(key, down) {
      assert.ok(await app.execute(() => document.hasFocus()), `the app's window isn't in front, so it ignores ${key} on a controller`);
      await app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
    },
    // A button pressed and let go in one go. Sent apart, a busy runner can take past spatial.ts's 300 ms between
    // them, and a direction held that long repeats: one press down a menu goes two items.
    async tap(key) {
      assert.ok(await app.execute(() => document.hasFocus()), `the app's window isn't in front, so it ignores ${key} on a controller`);
      await app.execute(async (key) => {
        for (const down of [true, false]) await window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } });
      }, key);
    },
    // The usage-data notice a new player sees first, answered OK. The banners show one at a time (App.tsx's
    // banner-stack), so the ones after it, such as bringing over Quiver Launcher 3, wait for it.
    async usageNoticeSeen() {
      await (await (await app.$("[aria-label='Usage data']")).$("button=OK")).click();
    },
    // Settings from the top bar's gear, at one of its sections ("General", "Controllers"…), and back from it.
    async settings(section) {
      await (await app.$("button[aria-label=Settings]")).click();
      const page = await app.$("section.settings-page");
      await page.waitForDisplayed();
      if (section) await (await (await page.$('nav[aria-label="Settings sections"]')).$(`button*=${section}`)).click();
      return page;
    },
    async closeSettings() {
      // In a narrow window a section's back link goes to the list first.
      for (let i = 0; i < 2 && (await (await app.$("section.settings-page")).isExisting()); i++) await (await app.$("section.settings-page .back-link")).click();
    },
    async close() {
      await app.deleteSession().catch(() => {});
      kill();
      if (!shared) api.close();
      await new Promise((r) => setTimeout(r, 500));
      // Once the app has gone, so it doesn't lose its display while still running.
      xvfb?.server.kill();
      running.delete(stop);
    },
  };
}

/** A port nothing is listening on, from the system. */
function freePort() {
  return new Promise((resolve, reject) => {
    const server = createServer().once("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });
}

/** Waits for tauri-driver to take connections on its port. */
async function listening(port) {
  for (const deadline = Date.now() + 10000; Date.now() < deadline; await new Promise((r) => setTimeout(r, 50))) {
    const up = await new Promise((resolve) => {
      const socket = connect(port, "127.0.0.1", () => (socket.end(), resolve(true))).once("error", () => resolve(false));
    });
    if (up) return;
  }
  throw new Error(`tauri-driver didn't open port ${port}`);
}

/** A virtual X display of its own (Xvfb picks a free number and tells it on fd 3), as xvfb-run makes one. */
function virtualDisplay() {
  return new Promise((resolve, reject) => {
    const server = spawn("Xvfb", ["-displayfd", "3", "-screen", "0", "1280x1024x24", "-nolisten", "tcp"], { stdio: ["ignore", "ignore", "ignore", "pipe"] });
    let out = "";
    server.once("error", reject);
    server.once("exit", (code) => reject(new Error(`Xvfb stopped (${code}) before it had a display`)));
    server.stdio[3].on("data", (chunk) => {
      out += chunk;
      if (out.includes("\n")) resolve({ server, display: `:${out.trim()}` });
    });
  });
}

/** Waits for WebView2's debugging port, which the app opens as it starts. */
async function debuggerUp(port) {
  for (const deadline = Date.now() + 60000; Date.now() < deadline; await new Promise((r) => setTimeout(r, 250))) {
    if (await fetch(`http://127.0.0.1:${port}/json/version`).then((r) => r.ok, () => false)) return;
  }
  throw new Error(`WebView2 didn't open its debugging port ${port}`);
}
