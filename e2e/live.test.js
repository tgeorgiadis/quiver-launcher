/**
 * Against the real catalog (read-only: no account, nothing written to the
 * site). Runs in CI on Linux and Windows, where api.quiverlauncher.com is
 * reachable, and reports how fast the catalog shows.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s;
before(async () => {
  s = await launch({ env: { QUIVER_API: "https://api.quiverlauncher.com/api/v1" } });
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
