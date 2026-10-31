/**
 * The launcher's web side (apps/launcher) on its own, in Playwright's WebKit (the engine of Linux's and a Steam Deck's
 * WebKitGTK) at the window's size, against the mock catalog, with the Rust side stood in for in the page
 * (stand-in.js). For what happens on the screen: how a controller, the keys and the mouse get around it. Installs,
 * files, updates and the window itself are tauri-driver's (the tests one folder up, against the real app).
 *
 * A controller's presses go straight to the page, as gamepad.rs's events reach it, without a round trip through a
 * WebDriver session and the Rust side, which on a CI runner could take longer than spatial.ts's 300 ms before a held
 * direction repeats. The page's clock is Playwright's, so a test can move it on.
 */
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { webkit } from "playwright";
import { startMockApi } from "../mock-api.js";
import { standInForRust } from "../stand-in.js";

const launcher = resolve(import.meta.dirname, "../../apps/launcher");
// One server for the launcher's files and one browser, for every app a test file opens.
let shared;
async function start() {
  const { createServer } = await import(pathToFileURL(createRequire(`${launcher}/`).resolve("vite")).href);
  const vite = await createServer({ root: launcher, configFile: `${launcher}/vite.config.ts`, logLevel: "error", server: { port: 0, strictPort: false } });
  await vite.listen();
  const browser = await webkit.launch();
  return { vite, browser, url: vite.resolvedUrls.local[0] };
}

/** An app with `state` as its saved files (settings, library, installs…), opened at the window's size. */
export async function openApp({ state = {}, width = 1280, height = 800 } = {}) {
  shared ??= await start();
  const api = await startMockApi();
  const context = await shared.browser.newContext({ viewport: { width, height } });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.addInitScript(standInForRust, { api: api.api, state, config: { convex: api.convex, accountApi: api.account, githubApi: api.github, gitlabApi: api.gitlab } });
  await page.goto(shared.url);
  await page.locator(".topbar").waitFor();
  /** WebdriverIO's execute, for reach.js: a function and its arguments, run in the page. */
  const execute = (fn, ...args) => page.evaluate(({ fn, args }) => (0, eval)(`(${fn})`)(...args), { fn: fn.toString(), args });
  const emit = (event, payload) => page.evaluate(([event, payload]) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event, payload }), [event, payload]);
  return {
    page,
    api,
    errors,
    app: { execute },
    /** Sends an event as the Rust side would (gamepad.rs's "pads", "pad-labels"…). */
    emit,
    /** A controller's button going down or up. */
    pad: (key, down) => emit("pad", { key, down }),
    until: (check, timeout = 10000) => waitUntil(check, timeout),
    async close() {
      await context.close();
      api.close();
    },
  };
}

/** Closes the server and browser, once a test file is done with its apps. */
export async function stop() {
  if (!shared) return;
  const { vite, browser } = shared;
  shared = undefined;
  await browser.close();
  await vite.close();
}

async function waitUntil(check, timeout) {
  for (const end = Date.now() + timeout; ; await new Promise((r) => setTimeout(r, 50))) {
    if (await check()) return;
    if (Date.now() > end) throw new Error(`still not so after ${timeout} ms: ${check}`);
  }
}
