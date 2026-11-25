/**
 * Scrolling down Browse's whole catalog, under the front page's shelves, with a controller's right stick (issue 542):
 * the next page is asked for a little before the end, so the stick doesn't run into the end and stop, and asking for
 * it doesn't hold the page still. Both happen only going down, where the pages load.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

let s, page;

before(async () => {
  // Enough apps for more pages than the first, on the shelves' two consoles.
  s = await openApp({ prepare: (api) => api.addApps(150, (i) => ({ consoles: [i % 2 ? "n64" : "snes"] })) });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await page.getByRole("button", { name: /^Browse ?\d*$/ }).click();
  await page.locator(".discover .shelf-row article").first().waitFor();
  await page.locator(".more button").waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

const nextPages = () => s.api.catalogQueries().filter((q) => q.path === "catalog:list" && q.args.paginationOpts.cursor).length;

/** The page scrolled till Show more is `below` pixels under what shows, with the focus on the last card above it. */
const showMoreBelow = (below) =>
  page.evaluate((below) => {
    const area = document.querySelector(".shell > main");
    area.scrollTop += document.querySelector(".more").getBoundingClientRect().top - area.getBoundingClientRect().bottom - below;
    [...document.querySelectorAll(".discover ~ * article .card-open")].at(-1).focus({ preventScroll: true });
    return Math.round(document.querySelector(".more").getBoundingClientRect().top - area.getBoundingClientRect().bottom);
  }, below);

test("the next page is asked for before Show more is on the screen", async () => {
  const asked = nextPages();
  assert.ok((await showMoreBelow(1000)) > 900, "the page scrolled to there, well past the 600 pixels it loads ahead");
  await page.waitForTimeout(500);
  assert.equal(nextPages(), asked, "not while the end is far off");
  await showMoreBelow(300);
  // The cards come to more than a page's.
  await page.waitForFunction(() => document.querySelectorAll(".discover ~ * article").length > 48);
  assert.equal(nextPages(), asked + 1);
});

test("asking for the next page doesn't hold the stick's scrolling up", async () => {
  // Its answer waits till the page has said it's coming (its spinner), so the two can't come at once.
  let answer;
  const answered = new Promise((resolve) => (answer = resolve));
  await page.route(/\/api\/v1\/apps\?.*cursor=/, async (route) => {
    await Promise.race([answered, new Promise((r) => setTimeout(r, 5000))]);
    await route.continue();
  });
  // Far enough from the end that the stick scrolls a while before the next page is asked for.
  assert.ok((await showMoreBelow(1000)) > 900, "the page scrolled to there, well past the 600 pixels it loads ahead");
  await page.waitForTimeout(500);
  // How the page keeps up: a message to itself, sent again as each arrives, gets through between whatever else it
  // does. One render of the whole page (the shelves, every card so far) from when the next page is asked for to the
  // spinner let none through.
  await page.evaluate(() => {
    const at = (window.__scroll = { pings: [], asked: 0, spinner: 0 });
    const fetched = window.fetch;
    window.fetch = function (input, ...rest) {
      if (!at.asked && String(input?.url ?? input).includes("cursor=")) at.asked = performance.now();
      return fetched.call(this, input, ...rest);
    };
    new MutationObserver(() => {
      if (!at.spinner && document.querySelector(".more .spinner")) at.spinner = performance.now();
    }).observe(document.body, { childList: true, subtree: true });
    const channel = new MessageChannel();
    channel.port1.onmessage = () => {
      at.pings.push(performance.now());
      if (!at.spinner) channel.port2.postMessage(0);
    };
    channel.port2.postMessage(0);
  });
  await s.pad("RightStickDown", true);
  try {
    await page.waitForFunction(() => window.__scroll.spinner, null, { timeout: 15000 });
  } finally {
    await s.pad("RightStickDown", false);
    answer();
    await page.unrouteAll({ behavior: "wait" });
  }
  const { asked, spinner, between } = await page.evaluate(() => {
    const { pings, asked, spinner } = window.__scroll;
    return { asked, spinner, between: pings.filter((p) => p > asked && p < spinner).length };
  });
  assert.ok(asked > 0, "the stick scrolled on to where the next page is asked for");
  assert.ok(between >= 3, `the page kept up while it went to the spinner (${between} messages through in ${Math.round(spinner - asked)} ms)`);
});
