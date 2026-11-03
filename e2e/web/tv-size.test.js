/**
 * TV mode's size on the screen: the same at 4K as at 1080p, where a TV with no display scale of its own gives the
 * launcher twice the pixels to be small in. Outside TV mode the interface size is all that zooms the page.
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

async function opened(settings, width, height, check) {
  const s = await openApp({ state: { settings }, width, height });
  try {
    await check(s.page);
  } finally {
    await s.close();
  }
}

test("TV mode at 4K is twice its size at 1080p, on the screen", async () => {
  let at1080p;
  await opened({ tvMode: "on" }, 1920, 1080, async (page) => (at1080p = await measure(page)));
  await opened({ tvMode: "on" }, 3840, 2160, async (page) => {
    const at4k = await measure(page);
    assert.ok(Math.abs(at1080p.zoom - 1.15) < 0.001, `zoom at 1080p: ${at1080p.zoom}`);
    assert.ok(Math.abs(at4k.zoom - 2.3) < 0.001, `zoom at 4K: ${at4k.zoom}`);
    assert.ok(Math.abs(at4k.bar / at1080p.bar - 2) < 0.05, `the bar: ${at1080p.bar} and ${at4k.bar}`);
  });
});

test("TV mode's interface size goes over the screen's", async () => {
  await opened({ tvMode: "on", scale: 1.5 }, 3840, 2160, async (page) => {
    const { zoom } = await measure(page);
    assert.ok(Math.abs(zoom - 3.45) < 0.001, `zoom: ${zoom}`);
  });
});

test("TV mode's size follows the window", async () => {
  await opened({ tvMode: "on" }, 1280, 800, async (page) => {
    assert.ok(Math.abs((await measure(page)).zoom - 1.15) < 0.001);
    await page.setViewportSize({ width: 3840, height: 2160 });
    await page.waitForFunction(() => Math.abs(Number(getComputedStyle(document.documentElement).zoom) - 2.3) < 0.001);
  });
});

test("outside TV mode, a 4K screen is left to the interface size", async () => {
  await opened({ tvMode: "off", scale: 1.5 }, 3840, 2160, async (page) => {
    assert.ok(Math.abs((await measure(page)).zoom - 1.5) < 0.001);
  });
});

test("the largest interface size leaves the top bar on the screen", async () => {
  for (const language of ["en", "de", "fr"])
    for (const [tvMode, width, height] of [["on", 1280, 800], ["on", 3840, 2160], ["off", 1280, 800]])
      await opened({ tvMode, scale: 2, language }, width, height, async (page) => {
        await measure(page);
        const over = await page.evaluate(() => {
          const bar = document.querySelector(".topbar");
          return bar.scrollWidth - bar.clientWidth;
        });
        assert.equal(over, 0, `${language}, TV mode ${tvMode} at ${width}x${height}`);
      });
});

test("Large and Extra large, from before there were percentages, come back as 125%", async () => {
  await opened({ tvMode: "off", scale: 1.3 }, 1920, 1080, async (page) => {
    assert.ok(Math.abs((await measure(page)).zoom - 1.25) < 0.001);
  });
});
