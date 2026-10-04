/**
 * App and game pages, as on quiverlauncher.com: opening and leaving pages
 * never blanks the window, a search shows the original games it matches
 * above the apps, and a game's page compares the apps that play it. The
 * catalog comes from the site's Convex queries.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let s, app, api, card, until;

before(async () => {
  // Two apps a page, so paging through Convex's cursor is tried too.
  api = await startMockApi({ pageSize: 2 });
  s = await launch({ api });
  ({ app, card, until } = s);
  await (await card("test-port")).waitForDisplayed({ timeout: 20000 });
  // Newer WebView2 returns a Promise from scrollTo; act like it everywhere, so a page that returns it from an effect fails here too.
  await app.execute(() => {
    const scroll = window.scrollTo.bind(window);
    window.scrollTo = (...args) => (scroll(...args), Promise.resolve());
  });
});

after(async () => {
  await s?.close();
  api?.close();
});

// Pages under the top one stay mounted, hidden.
const page = () => app.$(".page-layer:not([hidden]) .app-page");
/** The catalog's cards, in order. */
const listed = () => app.execute(() => [...document.querySelectorAll("main > div:not(.page-layer) article")].map((a) => a.dataset.slug));
const text = async (selector, scope = page()) => (await (await scope).$(selector)).getText();
const search = async (value) => {
  const box = await app.$("input[placeholder^=Search]");
  if (value) return box.setValue(value);
  // Typed away, so the page hears it.
  await box.click();
  await app.keys(["Control", "a"]);
  await app.keys("Backspace");
};
const gameButton = (slug) => app.$(`.search-games button[data-game="${slug}"]`);
/** Errors the window logged, which a blank window would have. */
const uiErrors = () => {
  const log = join(s.data, "quiver.log");
  return existsSync(log) ? readFileSync(log, "utf8").split("\n").filter((line) => / ui: /.test(line)) : [];
};
const rootFilled = () => app.execute(() => document.getElementById("root").childElementCount > 0);

test("opening an app and going back never blanks the window", async () => {
  await (await (await card("test-port")).$(".card-open")).click();
  await until(async () => (await text("h1").catch(() => "")) === "Test Port");
  await (await (await page()).$("button.back-link")).click();
  await (await card("test-port")).waitForDisplayed();
  assert.ok(await rootFilled());
  // Escape goes back too, and the catalog is still there after a second visit.
  await (await (await card("test-port")).$(".card-open")).click();
  await (await page()).waitForDisplayed();
  await app.keys("Escape");
  await (await card("test-port")).waitForDisplayed();
  assert.ok(await rootFilled());
  assert.deepEqual(uiErrors(), []);
});

test("Show more pages in the rest of the catalog with Convex's cursor", async () => {
  await until(async () => (await listed()).length === 2);
  assert.deepEqual(await listed(), ["test-port", "tampered-port"]);
  await (await app.$("button=Show more")).click();
  await until(async () => (await listed()).length === 3);
  assert.deepEqual(await listed(), ["test-port", "tampered-port", "test-remake"]);
  assert.ok(!(await app.$("button=Show more").isExisting()), "the last page has no Show more");
  const last = api.convexQueries().filter((q) => q.path === "catalog:list").at(-1);
  assert.deepEqual(last.args.paginationOpts, { numItems: 48, cursor: "2" });
});

test("a search shows the games it matches above the apps, from the site's Convex queries", async () => {
  await search("test po");
  const section = await app.$("section[aria-label='Matching games']");
  await section.waitForDisplayed({ timeout: 10000 });
  assert.equal((await text("h2", section)).toUpperCase(), "GAMES");
  const game = await gameButton("test-port");
  assert.match(await game.getText(), /Test Port \(original\)/);
  assert.match(await game.getText(), /2 ways to play →/);
  assert.equal(await (await app.$(".results-bar")).getText(), "Results for “test po”");
  // Apps follow under their own label.
  assert.equal(await app.execute(() => document.querySelector(".search-games").nextElementSibling.textContent.toUpperCase()), "APPS");
  assert.ok(api.convexQueries().some((q) => q.path === "catalog:matchingGames" && q.args.search === "test po"));
  assert.ok(api.convexQueries().some((q) => q.path === "catalog:list" && q.args.search === "test po"));
  // One letter isn't enough to look for games.
  await search("t");
  await until(async () => !(await section.isExisting()) || !(await section.isDisplayed()));
});

test("a game opens as a page with its apps best first, and Back retraces the way", async () => {
  await search("test po");
  await (await gameButton("test-port")).waitForDisplayed({ timeout: 10000 });
  await (await gameButton("test-port")).click();
  await until(async () => (await text("h1").catch(() => "")) === "Test Port (original)");
  assert.equal(await text(".eyebrow"), "THE ORIGINAL GAME");
  assert.match(await text(".game-description"), /The 1996 original\./);
  assert.equal(await text(".detail-tags"), "Nintendo 64");
  assert.match(await (await (await page()).$(".box-art img")).getAttribute("src"), /\/art\/capsule\.png$/);
  assert.match(await (await (await page()).$(".backdrop-art")).getAttribute("src"), /\/art\/hero\.png$/);
  await (await (await page()).$(".catalog-grid")).waitForDisplayed({ timeout: 10000 });
  assert.equal(await text(".tab-count"), "2");
  assert.equal(await text(".section-top .muted"), "Best first, by player feedback");
  // The site lists Test Remake first; players say Test Port runs, so it comes first.
  const order = await app.execute(() => [...document.querySelectorAll(".game-page article")].map((a) => a.dataset.slug));
  assert.deepEqual(order, ["test-port", "test-remake"]);
  assert.equal(await text("button.back-link"), "Back to results for “test po”");

  // An app from the game's page, from the keyboard as with a controller, and back to the game with focus where it was.
  await app.execute(() => document.querySelector('.page-layer:not([hidden]) article[data-slug="test-remake"] .card-open').focus());
  await app.keys("Enter");
  await until(async () => (await text("h1").catch(() => "")) === "Test Remake");
  assert.equal(await text("button.back-link"), "Test Port (original)");
  await app.keys("Escape");
  await until(async () => (await text("h1").catch(() => "")) === "Test Port (original)");
  assert.equal(await app.execute(() => document.activeElement?.closest("article")?.dataset.slug), "test-remake");
  // Its "Based on" chip goes back to the game already open, not a second copy of it.
  await (await (await (await page()).$('article[data-slug="test-remake"]')).$(".card-open")).click();
  await until(async () => (await text("h1").catch(() => "")) === "Test Remake");
  await (await (await page()).$('.app-hero button.game-chip[data-game="test-port"]')).click();
  await until(async () => (await text("h1").catch(() => "")) === "Test Port (original)");
  assert.equal(await text("button.back-link"), "Back to results for “test po”");

  // Escape returns to the search as it was.
  await app.keys("Escape");
  await (await gameButton("test-port")).waitForDisplayed();
  assert.equal(await (await app.$("input[placeholder^=Search]")).getValue(), "test po");
  assert.ok(await (await card("test-port")).isDisplayed());
  assert.deepEqual(uiErrors(), []);
});

test("an app's Based on chip opens its game, even one with no artwork", async () => {
  await search("");
  await (await card("tampered-port")).waitForDisplayed({ timeout: 10000 });
  await (await (await card("tampered-port")).$(".card-open")).click();
  await until(async () => (await text("h1").catch(() => "")) === "Tampered Port");
  await (await (await page()).$('.app-hero button.game-chip[data-game="tampered-port"]')).click();
  await until(async () => (await text("h1").catch(() => "")) === "Tampered Port (original)");
  await (await (await page()).$(".catalog-grid")).waitForDisplayed({ timeout: 10000 });
  assert.equal(await text(".tab-count"), "1");
  assert.equal(await text(".section-top .muted"), "Community ports and recreations");
  assert.equal(await text("button.back-link"), "Tampered Port");
  assert.ok(!(await (await page()).$(".backdrop-art").isExisting()), "no backdrop without art");
  assert.ok(!(await (await page()).$(".game-description").isExisting()), "no empty description");
  assert.ok(await (await page()).$(".app-hero .artwork svg").isExisting(), "the placeholder icon stands in for box art");
  // Escape goes back one page at a time.
  await app.keys("Escape");
  await until(async () => (await text("h1").catch(() => "")) === "Tampered Port");
  await app.keys("Escape");
  await (await card("tampered-port")).waitForDisplayed();
  assert.deepEqual(uiErrors(), []);
});

test("the Quiver logo, as on the website, goes to the library", async () => {
  const logo = await app.$(".topbar button.brand");
  await until(() => app.execute(() => document.querySelector(".topbar .brand img").naturalWidth > 0));
  await logo.click();
  await (await app.$("h2=Your library is empty")).waitForDisplayed();
  await (await app.$("button=Browse")).click();
  await (await card("test-port")).waitForDisplayed();
});

// Last: it leaves an error in the log on purpose.
test("a page that fails says so and goes back, keeping the search", async () => {
  await search("broken");
  await (await gameButton("broken-game")).waitForDisplayed({ timeout: 10000 });
  await (await gameButton("broken-game")).click();
  const problem = await app.$(".page-layer:not([hidden]) [role=alert]");
  await problem.waitForDisplayed({ timeout: 10000 });
  assert.match(await problem.getText(), /This page hit a problem/);
  assert.equal(await text("button.back-link", problem), "Back to results for “broken”");
  await until(() => uiErrors().length > 0, 5000); // The error is in quiver.log.
  // Escape works there too, and the catalog is as it was.
  await app.keys("Escape");
  await (await gameButton("broken-game")).waitForDisplayed();
  assert.equal(await (await app.$("input[placeholder^=Search]")).getValue(), "broken");
});
