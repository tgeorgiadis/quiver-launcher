/**
 * The top bar in a small window: it compacts as what's in it needs (top-bar-fit.ts), and the window's buttons stay in
 * the corner whatever the window's width, the language or the player's name.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

/** tauri.conf.json's minWidth, and on past where a long name goes back in the bar. */
const WIDTHS = [760, 800, 860, 900, 960, 1000, 1060, 1100, 1200, 1280, 1600];

const measure = (page) =>
  page.evaluate(() => {
    const shell = document.querySelector(".shell");
    const bar = document.querySelector(".topbar");
    return {
      past: shell.scrollWidth - shell.clientWidth,
      closeShows: document.querySelector(".close-window").getBoundingClientRect().right <= innerWidth + 0.5,
      level: Number(bar.dataset.compact ?? 0),
      overflows: "overflows" in bar.dataset,
    };
  });

/** Every width, narrowest first, as the window's dragged. The level at each. */
async function acrossWidths(page, name) {
  const levels = [];
  for (const width of WIDTHS) {
    await page.setViewportSize({ width, height: 700 });
    // The bar's fitted before the frame paints: two frames and it's settled.
    await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))));
    const at = await measure(page);
    assert.equal(at.past, 0, `${name} at ${width}: nothing scrolls sideways`);
    assert.equal(at.overflows, false, `${name} at ${width}: it fits`);
    assert.equal(at.closeShows, true, `${name} at ${width}: Close is on the screen`);
    levels.push(at.level);
  }
  return levels;
}

for (const language of ["en", "de"]) {
  test(`in ${language}, the bar fits every width with the window's buttons in sight`, async () => {
    const opened = await openApp({ state: { settings: { language } } });
    try {
      const levels = await acrossWidths(opened.page, language);
      assert.equal(levels.at(-1), 0, "a wide window isn't compacted");
      await opened.page.setViewportSize({ width: 760, height: 700 });
      const signIn = opened.page.locator(".account button");
      await opened.until(async () => (await signIn.evaluate((b) => b.offsetHeight)) < 40);
    } finally {
      await opened.close();
    }
  });
}

test("a long name leaves the bar when it doesn't fit, and is in the account's menu instead", async () => {
  const name = "A rather long player name here";
  const opened = await openApp({ state: { settings: { language: "de" } } });
  const { page } = opened;
  try {
    await page.locator(".account button").click();
    const dialog = page.locator('[role="dialog"]');
    await dialog.waitFor();
    // Over to creating an account, in whatever words: the form's last plain button.
    await dialog.locator('form > button[type="button"]').last().click();
    await dialog.locator('input[autocomplete="new-password"]').waitFor();
    await dialog.locator('input[autocomplete="username"]').fill(name);
    await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
    await dialog.locator("button.primary").click();
    const account = page.locator(".account-button");
    await account.waitFor();

    await acrossWidths(page, "signed in");
    await page.setViewportSize({ width: 1000, height: 700 });
    await opened.until(async () => (await measure(page)).level > 0);
    await account.click();
    await page.locator(".account-menu-name").getByText(name).waitFor();
    await page.keyboard.press("Escape");

    await page.setViewportSize({ width: 1600, height: 700 });
    await opened.until(async () => (await measure(page)).level === 0);
    await account.getByText(name).waitFor();
  } finally {
    await opened.close();
  }
});

test("the window's buttons stay in the corner of a bar still too wide", async () => {
  const opened = await openApp({ state: { settings: { language: "de" } } });
  const { page } = opened;
  try {
    await page.setViewportSize({ width: 760, height: 700 });
    // More in the bar than any language has: past what compacting can make room for.
    await page.evaluate(() => {
      const spare = document.createElement("span");
      spare.style.cssText = "flex: none; width: 400px";
      document.querySelector(".topbar nav").after(spare);
    });
    await opened.until(async () => (await measure(page)).overflows);
    const at = await measure(page);
    assert.ok(at.past > 0, "the bar scrolls sideways");
    assert.equal(at.closeShows, true);
    await page.evaluate(() => document.querySelector(".shell").scrollTo(10000, 0));
    assert.equal((await measure(page)).closeShows, true, "and when scrolled to its end");
  } finally {
    await opened.close();
  }
});
