/**
 * A download can be cancelled: nothing is left behind, and an update that's
 * cancelled leaves the version already installed as it was.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, card, until;

before(async () => {
  // The update is started from Browse, so Browse shows apps in the library (browse-owned.test.js covers leaving them out).
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ browseShowsLibrary: true }));
  s = await launch({ data });
  ({ app, api, apps, card, until } = s);
});

after(() => {
  api?.stall(false);
  return s?.close();
});

const staging = () => (existsSync(join(apps, ".staging")) ? readdirSync(join(apps, ".staging")) : []);

test("cancelling a first download leaves nothing behind, and Get starts again", async () => {
  api.stall(true);
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await until(() => api.stalled() === 1);
  await (await port.$("button[aria-label='Cancel downloading Test Port']")).click();
  // The launcher hangs up, and the card is back to Get with no error.
  await until(() => api.stalled() === 0);
  await (await port.$("button=Get")).waitForDisplayed();
  assert.ok(!(await (await port.$('[role="alert"]')).isExisting()), "no error");
  assert.ok(!existsSync(join(apps, "test-port")), "nothing was installed");
  assert.deepEqual(staging(), [], "the half-downloaded file is gone");

  api.stall(false);
  await (await port.$("button=Get")).click();
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  assert.equal(readFileSync(join(apps, "test-port", ".quiver-version"), "utf8"), "1.0.0");
});

test("cancelling an update keeps the version that was installed", async () => {
  api.release("1.1.0");
  api.stall(true);
  await (await app.$("button*=Library")).click();
  await (await app.$("button=Browse")).click();
  const port = await card("test-port");
  await (await port.$("button=Update to v1.1.0")).click();
  await until(() => api.stalled() === 1);
  // On the app's page too.
  await (await port.$(".card-open")).click();
  const page = await app.$(".app-page");
  await (await page.$("button[aria-label='Cancel downloading Test Port']")).click();
  await (await page.$("button=Update to v1.1.0")).waitForDisplayed();
  assert.equal(readFileSync(join(apps, "test-port", ".quiver-version"), "utf8"), "1.0.0");
  assert.ok(existsSync(join(apps, "test-port", "data/level1.dat")), "its files are all there");
  assert.deepEqual(staging(), []);

  // And it still updates when asked again.
  api.stall(false);
  await (await page.$("button=Update to v1.1.0")).click();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "1.1.0", 30000);
  await app.keys("Escape");
  api.release("1.0.0");
});
