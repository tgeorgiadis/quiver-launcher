/**
 * Against the real catalog (read-only: no account, nothing written to the
 * site). Runs in CI on Linux and Windows, where quiverlauncher.com's Convex
 * deployment is reachable, and reports how fast the catalog shows.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s;
before(async () => {
  s = await launch({ env: { QUIVER_API: "https://api.quiverlauncher.com/api/v1", QUIVER_CONVEX: "https://convex.quiverlauncher.com" } });
});
after(() => s?.close());

const cards = () => s.app.execute(() => [...document.querySelectorAll("article")].map((e) => e.dataset.slug));

test("the real catalog shows quickly, and all of it pages in", async () => {
  const { app, until } = s;
  await until(async () => (await app.$$("article")).length > 0, 30000);
  // Milliseconds since the page started loading, on the app's own clock.
  console.log(`first ports shown ${Math.round(await app.execute(() => performance.now()))} ms after the page started`);
  for (let more = await app.$("button=Show more"); await more.isExisting(); more = await app.$("button=Show more")) {
    const before = (await cards()).length;
    const started = Date.now();
    await more.click();
    await until(async () => (await cards()).length > before || !(await app.$("button=Show more").isExisting()), 20000);
    console.log(`next page: ${(await cards()).length} ports after ${Date.now() - started} ms`);
  }
  console.log(`whole catalog for this computer: ${(await cards()).length} ports`);
  await app.execute(() => window.scrollTo(0, 0));
});

test("real apps and games open as pages, and going back never blanks the window", async () => {
  const { app, until } = s;
  const page = () => app.$(".page-layer:not([hidden]) .app-page");
  const heading = () => page().then((p) => p.$("h1")).then((h) => h.getText()).catch(() => "");
  const filled = () => app.execute(() => document.getElementById("root").childElementCount > 0);
  // A page that fails shows a message in its place and logs it; neither may happen with real data.
  const problems = async () => {
    const log = join(s.data, "quiver.log");
    const logged = existsSync(log) ? readFileSync(log, "utf8").split("\n").filter((line) => / ui: /.test(line)) : [];
    const shown = await app.execute(() => [...document.querySelectorAll('[role="alert"] h2')].map((h) => h.textContent));
    return [...logged, ...shown];
  };
  await until(async () => (await app.$$("article")).length > 0, 30000);
  // The first few apps of the catalog open, and Back returns to the catalog.
  for (const slug of (await cards()).slice(0, 3)) {
    await (await (await app.$(`article[data-slug="${slug}"]`)).$(".card-open")).click();
    await until(async () => (await heading()) !== "", 20000);
    console.log(`${slug}: page "${await heading()}"`);
    // Its README and repository arrive after the first paint.
    await new Promise((r) => setTimeout(r, 2000));
    assert.deepEqual(await problems(), [], `${slug}'s page hit a problem`);
    await (await (await page()).$("button.back-link")).click();
    await (await app.$(`article[data-slug="${slug}"]`)).waitForDisplayed({ timeout: 10000 });
    assert.ok(await filled(), `the window went blank after ${slug}`);
  }
  // As on quiverlauncher.com: searching "mario" shows Mario's Tennis among the games, with its page.
  const box = await app.$("input[placeholder^=Search]");
  await box.setValue("mario");
  const game = await app.$('.search-games button[data-game="marios-tennis"]');
  await game.waitForDisplayed({ timeout: 20000 });
  assert.match(await game.getText(), /Mario's Tennis[\s\S]*ways? to play →/);
  await game.click();
  await until(async () => (await heading()) === "Mario's Tennis", 20000);
  await (await (await page()).$(".catalog-grid article")).waitForDisplayed({ timeout: 20000 });
  assert.match(await (await (await page()).$(".detail-tags")).getText(), /Virtual Boy/);
  const ways = await app.execute(() => [...document.querySelectorAll(".game-page article")].map((a) => a.dataset.slug));
  console.log(`Mario's Tennis: ${ways.join(", ")}`);
  assert.ok(ways.includes("marios-tennis-mariotennisvirtualboyrecomp"));
  // One of its apps, with its README from the site, then back through the game to the search.
  await (await (await app.$('.game-page article[data-slug="marios-tennis-mariotennisvirtualboyrecomp"]')).$(".card-open")).click();
  await until(async () => (await heading()) === "MarioTennisVirtualBoyRecomp", 20000);
  await (await (await page()).$(".readme")).waitForDisplayed({ timeout: 20000 });
  await (await (await page()).$("button.back-link")).click();
  await until(async () => (await heading()) === "Mario's Tennis", 20000);
  await (await (await page()).$("button.back-link")).click();
  await game.waitForDisplayed({ timeout: 10000 });
  assert.ok(await filled());
  await box.click();
  await app.keys(["Control", "a"]);
  await app.keys("Backspace");
  await until(async () => !(await app.$(".search-games").isExisting()), 20000);
  assert.deepEqual(await problems(), []);
});

test("a real app's player feedback shows as on the website", async () => {
  const { app, until } = s;
  const box = await app.$("input[placeholder^=Search]");
  await box.setValue("ConkerBFDReloaded");
  const card = await app.$('article[data-slug="conker-s-bad-fur-day-conkerbfdreloaded"]');
  await card.waitForDisplayed({ timeout: 20000 });
  await (await card.$(".card-open")).click();
  const page = () => app.$(".page-layer:not([hidden]) .app-page");
  // Its Player feedback tab, with the count the website shows.
  let count = "";
  await until(async () => {
    for (const t of await (await page()).$$("button[role=tab]")) {
      const label = await t.getText();
      if (label.startsWith("Player feedback")) return (count = label.replace("Player feedback", "").trim()), await t.click(), true;
    }
    return false;
  }, 20000);
  const reviews = () =>
    app.execute(() => [...document.querySelectorAll(".page-layer:not([hidden]) .feedback article.review")].map((r) => r.querySelector(".review-author strong").textContent));
  await until(async () => (await reviews()).length >= 2, 20000);
  console.log(`ConkerBFDReloaded: ${count} on the tab, ${(await reviews()).length} shown (${(await reviews()).join(", ")})`);
  assert.ok(Number(count) >= 2);
  await app.keys("Escape");
  await box.click();
  await app.keys(["Control", "a"]);
  await app.keys("Backspace");
});

test("Get installs verified ports, and Play starts one", async () => {
  const { app, until } = s;
  const results = [];
  for (const slug of (await cards()).slice(0, 8)) {
    const card = await app.$(`article[data-slug="${slug}"]`);
    const get = await card.$("button=Get");
    if (!(await get.isExisting())) continue;
    const started = Date.now();
    await get.click();
    // Wait for Play, an error, or a choice between files.
    await until(async () => {
      const choose = await app.$('[role="dialog"][aria-label="Choose a download"] button');
      if (await choose.isExisting()) await choose.click();
      return (await card.$("button*=Play").isExisting()) || (await card.$('[role="alert"]').isExisting());
    }, 300000);
    const error = await card.$('[role="alert"] p');
    const outcome = (await error.isExisting()) ? await error.getText() : "installed";
    console.log(`${slug}: ${outcome} (${Math.round((Date.now() - started) / 1000)} s)`);
    results.push({ slug, outcome });
    assert.doesNotMatch(outcome, /doesn't match the file Quiver checked/, `${slug} failed its checksum`);
    if (results.filter((r) => r.outcome === "installed").length >= 2) break;
  }
  assert.ok(results.some((r) => r.outcome === "installed"), JSON.stringify(results));
  const installed = readdirSync(s.apps).filter((d) => existsSync(join(s.apps, d, ".quiver-version")));
  console.log(`installed folders: ${installed.join(", ")}`);
  assert.ok(installed.length > 0);

  // Play the first one: it starts without the launcher reporting a problem.
  const played = await app.$(`article[data-slug="${results.find((r) => r.outcome === "installed").slug}"]`);
  await (await played.$("button*=Play")).click();
  await new Promise((r) => setTimeout(r, 3000));
  const problem = await played.$('[role="alert"] p');
  assert.ok(!(await problem.isExisting()), (await problem.isExisting()) ? await problem.getText() : "");
  console.log("Play started it");
});
