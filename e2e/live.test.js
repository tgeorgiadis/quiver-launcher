/**
 * Against the real catalog (read-only: no account, nothing written to the
 * site). Runs in CI, where api.quiverlauncher.com is reachable.
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

test("the real catalog lists ports, and Get installs a verified one", async () => {
  const { app, until } = s;
  await until(async () => (await app.$$("article")).length > 0, 30000);
  const slugs = await app.execute(() => [...document.querySelectorAll("article")].map((e) => e.dataset.slug));
  console.log(`catalog shows ${slugs.length} ports for this computer`);
  const results = [];
  for (const slug of slugs.slice(0, 6)) {
    const card = await app.$(`article[data-slug="${slug}"]`);
    const get = await card.$("button=Get");
    if (!(await get.isExisting())) continue;
    await get.click();
    // Wait for Play, an error, or a choice between files.
    await until(async () => {
      const choose = await app.$('[role="dialog"][aria-label="Choose a download"] button');
      if (await choose.isExisting()) await choose.click();
      return (await card.$("button*=Play").isExisting()) || (await card.$('[role="alert"]').isExisting());
    }, 300000);
    const error = await card.$('[role="alert"] p');
    const outcome = (await error.isExisting()) ? await error.getText() : "installed";
    console.log(`${slug}: ${outcome}`);
    results.push({ slug, outcome });
    assert.doesNotMatch(outcome, /doesn't match the file Quiver checked/, `${slug} failed its checksum`);
    if (results.filter((r) => r.outcome === "installed").length >= 2) break;
  }
  assert.ok(results.some((r) => r.outcome === "installed"), JSON.stringify(results));
  const installed = readdirSync(s.apps).filter((d) => existsSync(join(s.apps, d, ".quiver-version")));
  console.log(`installed folders: ${installed.join(", ")}`);
  assert.ok(installed.length > 0);
});

