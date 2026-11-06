/**
 * TV mode's size on the screen: it sizes itself by the window's height (src/tv/TvHost.tsx's `--u`, 1 at 720p), so 4K
 * shows as 1080p does where a TV with no display scale of its own gives the launcher twice the pixels to be small in,
 * with the interface size on top; the page itself isn't zoomed. Outside TV mode the interface size is all that zooms
 * the page.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

/** The page's zoom, and the top bar's height on the screen (WebKit's boxes are in the page's pixels: zoom.ts). */
async function measure(page) {
  // The bar shows a moment before the store's effect zooms the page.
  await page.waitForFunction(() => document.documentElement.style.zoom);
  return page.evaluate(() => {
    const zoom = Number(getComputedStyle(document.documentElement).zoom);
    return { zoom, bar: document.querySelector(".topbar").offsetHeight * zoom };
  });
}

/** TV mode's unit, and its tab strip's height on the screen. */
async function measureTv(page) {
  await page.locator(".tv-tab.selected").waitFor();
  return page.evaluate(() => {
    const root = document.querySelector(".tv-root");
    const zoom = Number(getComputedStyle(document.documentElement).zoom);
    return { zoom, u: parseFloat(root.style.getPropertyValue("--u")), strip: document.querySelector(".tv-tab").getBoundingClientRect().height };
  });
}

async function opened(settings, width, height, check) {
  const s = await openApp({ state: { settings }, width, height });
  try {
    await check(s.page);
  } finally {
    await s.close();
  }
}

test("TV mode at 4K is twice its size at 1080p, on the screen, without zooming the page", async () => {
  let at1080p;
  await opened({ tvMode: "on" }, 1920, 1080, async (page) => (at1080p = await measureTv(page)));
  await opened({ tvMode: "on" }, 3840, 2160, async (page) => {
    const at4k = await measureTv(page);
    assert.equal(at1080p.zoom, 1);
    assert.equal(at4k.zoom, 1);
    assert.ok(Math.abs(at1080p.u - 1.5) < 0.001, `--u at 1080p: ${at1080p.u}`);
    assert.ok(Math.abs(at4k.u - 3) < 0.001, `--u at 4K: ${at4k.u}`);
    assert.ok(Math.abs(at4k.strip / at1080p.strip - 2) < 0.05, `a tab: ${at1080p.strip} and ${at4k.strip}`);
  });
});

test("TV mode's interface size goes over the screen's", async () => {
  await opened({ tvMode: "on", scale: 1.5 }, 3840, 2160, async (page) => {
    const { u } = await measureTv(page);
    assert.ok(Math.abs(u - 4.5) < 0.001, `--u: ${u}`);
  });
});

test("TV mode's size follows the window", async () => {
  await opened({ tvMode: "on" }, 1280, 720, async (page) => {
    assert.ok(Math.abs((await measureTv(page)).u - 1) < 0.001);
    await page.setViewportSize({ width: 3840, height: 2160 });
    await page.waitForFunction(() => Math.abs(parseFloat(document.querySelector(".tv-root").style.getPropertyValue("--u")) - 3) < 0.001);
  });
});

test("outside TV mode, a 4K screen is left to the interface size", async () => {
  await opened({ tvMode: "off", scale: 1.5 }, 3840, 2160, async (page) => {
    assert.ok(Math.abs((await measure(page)).zoom - 1.5) < 0.001);
  });
});

test("the largest interface size leaves the top bar on the screen", async () => {
  for (const language of ["en", "de", "fr"])
    for (const [width, height] of [[1280, 800], [3840, 2160]])
      await opened({ tvMode: "off", scale: 2, language }, width, height, async (page) => {
        await measure(page);
        const over = await page.evaluate(() => {
          const bar = document.querySelector(".topbar");
          return bar.scrollWidth - bar.clientWidth;
        });
        assert.equal(over, 0, `${language} at ${width}x${height}`);
      });
});

test("Large and Extra large, from before there were percentages, come back as 125%", async () => {
  await opened({ tvMode: "off", scale: 1.3 }, 1920, 1080, async (page) => {
    assert.ok(Math.abs((await measure(page)).zoom - 1.25) < 0.001);
  });
});
