/**
 * The launcher's screens as on an Android phone, without one: its web side
 * (apps/launcher) in Chromium at a phone's size, with touch and the phone's
 * pixel density, against the mock catalog. The Rust side is stood in for here
 * (`native.ts`'s calls, answered as Android's build would), so this checks how
 * the screens lay out, not installs; journeys under tauri-driver do those.
 *
 * QUIVER_PHONE_API points it at another catalog, such as the real one
 * (https://api.quiverlauncher.com/api/v1), to see real names and artwork.
 */
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { chromium, devices } from "playwright";
import { startMockApi } from "./mock-api.js";
import { standInForRust } from "./stand-in.js";

const launcher = resolve(import.meta.dirname, "../apps/launcher");

/** An announcement long enough to wrap, with a link, as the site sends them. */
export const ANNOUNCEMENTS = [
  {
    id: "a2",
    message: "Quiver Launcher 4 is out for Android! Apps install from their APKs, and your library syncs between your phone and computer. [Read more](https://quiverlauncher.com/)",
    parts: [
      { text: "Quiver Launcher 4 is out for Android! Apps install from their APKs, and your library syncs between your phone and computer. " },
      { href: "https://quiverlauncher.com/", text: "Read more" },
    ],
    publishedAt: Date.now() - 3600_000,
  },
  { id: "a1", message: "Welcome to Quiver Launcher v4!", parts: [{ text: "Welcome to Quiver Launcher v4!" }], publishedAt: Date.now() - 86400_000 },
];

/**
 * Starts the web side and Chromium as a phone (Pixel 7 unless `device` says
 * otherwise). `state` is what the launcher's saved files hold (library,
 * installs, settings…); `theme` the phone's light or dark; `computerOnly` the
 * apps (slugs) with no Android release.
 */
export async function openPhone({ device = "Pixel 7", theme = "dark", state = {}, computerOnly = [] } = {}) {
  const mock = process.env.QUIVER_PHONE_API ? null : await startMockApi();
  const api = process.env.QUIVER_PHONE_API ?? mock.api;
  const { createServer } = await import(pathToFileURL(createRequire(`${launcher}/`).resolve("vite")).href);
  const vite = await createServer({ root: launcher, configFile: `${launcher}/vite.config.ts`, logLevel: "error", server: { port: 0, strictPort: false } });
  await vite.listen();
  const url = vite.resolvedUrls.local[0];
  const browser = await chromium.launch();
  const context = await browser.newContext({ ...devices[device], colorScheme: theme });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  if (mock) {
    // The mock catalog has no announcements: these, so the banner shows.
    await page.route("**/api/v1/announcements", (route) => route.fulfill({ json: { items: ANNOUNCEMENTS } }));
    // Its apps are for computers; here they're for Android too, so screens show Get and Play as a phone's would. Those in
    // `computerOnly` (slugs) stay computers' only.
    const forAndroid = (value) => {
      if (Array.isArray(value)) return value.map(forAndroid);
      if (!value || typeof value !== "object") return value;
      const out = Object.fromEntries(Object.entries(value).map(([k, v]) => [k, forAndroid(v)]));
      if (Array.isArray(value.supportedOS) && !computerOnly.includes(value.slug)) out.supportedOS = [...value.supportedOS, "android"];
      return out;
    };
    await page.route("**/api/v1/apps**", async (route) => {
      // A list the page asks for as a test's phone closes finds the catalog gone: that's no failure of the page's.
      const response = await route.fetch().catch(() => null);
      const body = await response?.text().catch(() => null);
      if (body == null) return route.abort().catch(() => {});
      let json;
      try {
        json = JSON.parse(body);
      } catch {
        return route.fulfill({ response, body }).catch(() => {});
      }
      await route.fulfill({ response, json: forAndroid(json) }).catch(() => {});
    });
  }
  // As the Android build answers: its apps in its own folder, and the window always filling the screen.
  await page.addInitScript(standInForRust, { api, state, maximized: true, config: { os: "android", arch: "arm64", appsDir: "/data/user/0/com.quiverlauncher.app/files/apps", home: null } });
  await page.goto(url);
  await page.locator(".topbar").waitFor();
  return {
    page,
    errors,
    api: mock,
    /** Pages sent to the phone's browser, oldest first. */
    opened: () => page.evaluate(() => window.__opened),
    async close() {
      await browser.close();
      await vite.close();
      mock?.close();
    },
  };
}

/**
 * What sticks out past the screen's sides, so the page scrolls sideways: how
 * far it scrolls, and the outermost elements that stick out (not those inside
 * something that clips or scrolls them).
 */
export function sideways(page) {
  return page.evaluate(() => {
    const width = document.documentElement.clientWidth;
    // The page scrolls in main, under the top bar: what's past its sides is past the window's.
    const page = document.querySelector(".shell > main");
    const clipped = (el) => {
      for (let p = el.parentElement; p && p !== document.body && p !== page; p = p.parentElement) {
        const { overflowX } = getComputedStyle(p);
        if (overflowX !== "visible") return true;
      }
      return false;
    };
    const out = [...document.querySelectorAll("body *")].filter((el) => {
      if (el.closest("[hidden]") || clipped(el)) return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && (r.right > width + 1 || r.left < -1);
    });
    const outermost = out.filter((el) => !out.includes(el.parentElement));
    const name = (el) => `${el.tagName.toLowerCase()}${[...el.classList].map((c) => `.${c}`).join("")}${el.textContent ? ` "${el.textContent.trim().slice(0, 30)}"` : ""}`;
    return { by: Math.max(document.documentElement.scrollWidth - width, page ? page.scrollWidth - page.clientWidth : 0), elements: outermost.slice(0, 10).map(name) };
  });
}

/**
 * Text squeezed so narrow it breaks inside words, a few letters to a line:
 * each such element, by its first words.
 */
export function squeezed(page) {
  return page.evaluate(() =>
    [...document.querySelectorAll("p, h1, h2, h3, span, a, button, label, li")]
      .filter((el) => {
        if (el.closest("[hidden]") || !el.textContent.trim()) return false;
        const r = el.getBoundingClientRect();
        const lines = r.height / parseFloat(getComputedStyle(el).lineHeight || "20");
        // Narrower than a short word, over several lines.
        return r.width > 0 && r.width < 90 && lines >= 3 && el.textContent.trim().length > 20;
      })
      .map((el) => `${el.tagName.toLowerCase()} "${el.textContent.trim().slice(0, 40)}" (${Math.round(el.getBoundingClientRect().width)}px wide)`),
  );
}

/**
 * Text cut off at the screen's edge by something that clips it, with no way
 * to scroll to the rest. Text that ends in "…", or scrolls by itself
 * (`.scrolling-text`), is cut off on purpose.
 */
export function cutOff(page) {
  return page.evaluate(() => {
    const width = document.documentElement.clientWidth;
    // The page's own scrolling (main, under the top bar) is up and down: text past its sides is cut off.
    const page = document.querySelector(".shell > main");
    const meant = (el) => {
      for (let p = el; p && p !== document.body && p !== page; p = p.parentElement) {
        const style = getComputedStyle(p);
        if (style.textOverflow === "ellipsis" || /auto|scroll/.test(style.overflowX) || p.classList.contains("scrolling-text")) return true;
      }
      return false;
    };
    return [...document.querySelectorAll("body *")]
      .filter((el) => {
        if (el.closest("[hidden]") || ![...el.childNodes].some((n) => n.nodeType === Node.TEXT_NODE && n.textContent.trim())) return false;
        const r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0 && r.right > width + 1 && !meant(el);
      })
      .map((el) => `${el.tagName.toLowerCase()} "${el.textContent.trim().slice(0, 40)}" (to ${Math.round(el.getBoundingClientRect().right)}px of ${width})`);
  });
}

/** Whether the player can pinch to zoom: the viewport tag lets them. */
export function zoomable(page) {
  return page.evaluate(() => {
    const content = document.querySelector('meta[name="viewport"]')?.getAttribute("content") ?? "";
    return !/user-scalable\s*=\s*(no|0)/.test(content) && !/maximum-scale\s*=\s*1(\.0*)?(\D|$)/.test(content);
  });
}
