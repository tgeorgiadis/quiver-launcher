/**
 * Downloads, like Steam's: a button in the top bar in every layout, a strip
 * above the library while something is happening, and the Sidebar layout's
 * foot all open one page of what's downloading, the updates waiting, and what
 * finished since the launcher started.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, card, until;

before(async () => {
  // The update is found from Browse, so Browse shows apps in the library. The usage-data notice already answered: the
  // strip offering updates is one of the banners that wait behind it.
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ browseShowsLibrary: true, telemetryNoticeSeen: true }));
  s = await launch({ data });
  ({ app, api, card, until } = s);
});

after(() => {
  api?.stall(false);
  return s?.close();
});

const button = () => app.$(".topbar .downloads-button");
const page = () => app.$(".downloads-page");
const section = async (name) => (await page()).$(`section[aria-label="${name}"]`);
/** The apps in a section of the page, by slug. */
const rows = (name) =>
  app.execute((name) => [...document.querySelectorAll(`.downloads-page section[aria-label="${name}"] .download-row`)].map((r) => r.dataset.slug), name);

test("a download shows on the top-bar button, in the strip, and on the page with its Cancel", async () => {
  assert.equal(await (await button()).getAttribute("aria-label"), "Downloads");
  api.stall(true);
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await until(() => api.stalled() === 1);
  await until(async () => (await (await button()).getAttribute("aria-label")).startsWith("Downloads: 1 downloading"));
  const strip = await app.$(".downloads-strip");
  await until(async () => (await strip.getText()).startsWith("Downloading Test Port"));

  await (await strip.$("button=See downloads")).click();
  await (await page()).waitForDisplayed();
  assert.equal(await (await button()).getAttribute("aria-current"), "page");
  assert.deepEqual(await rows("Downloading"), ["test-port"]);
  assert.ok(await (await (await section("Downloading")).$("button[aria-label='Cancel downloading Test Port']")).isDisplayed());
  assert.equal(await (await (await section("Updates available")).$(".muted")).getText(), "Everything is up to date.");

  // It finishes while the page is open, and moves to Recently finished with its Play.
  api.stall(false);
  await until(async () => (await rows("Recently finished")).includes("test-port"), 30000);
  assert.deepEqual(await rows("Downloading"), []);
  const done = await (await section("Recently finished")).$(".download-row");
  assert.match(await (await done.$(".download-detail")).getText(), /^Installed v1\.0\.0 · Today /);
  assert.ok(await (await done.$("button*=Play")).isDisplayed());
  assert.equal(await (await button()).getAttribute("aria-label"), "Downloads");

  // Back goes to Browse, where it started, and the strip has gone.
  await (await (await page()).$(".back-link")).click();
  await (await card("test-port")).waitForDisplayed();
  assert.ok(!(await (await app.$(".downloads-strip")).isExisting()));
});

test("an update waiting shows as a count, in the strip, and on the page, which installs it", async () => {
  api.release("1.1.0");
  // Browse, opened again, brings in the new release.
  await (await app.$("button*=Library")).click();
  await (await app.$("button=Browse")).click();
  await (await (await card("test-port")).$("button=Update to v1.1.0")).waitForDisplayed();
  await (await app.$("button*=Library")).click();
  await until(async () => (await (await button()).getAttribute("aria-label")) === "Downloads: 1 waiting");
  assert.equal(await (await app.$(".downloads-count")).getText(), "1");
  const strip = await app.$(".downloads-strip");
  assert.equal(await (await strip.$("p")).getText(), "1 update available.");
  // Not now puts it away until another update comes.
  await (await strip.$("button=Not now")).click();
  await until(async () => !(await (await app.$(".downloads-strip")).isExisting()));

  await (await button()).click();
  assert.deepEqual(await rows("Updates available"), ["test-port"]);
  const update = await (await section("Updates available")).$(".download-row");
  assert.equal(await (await update.$(".download-detail")).getText(), "v1.1.0 is out. You have v1.0.0.");
  // What's new in it, and Update: Play comes once it's updated.
  assert.deepEqual(await update.$$(".download-action button").map((b) => b.getText()), ["See changelog", "Update to v1.1.0"]);
  // The name opens the app's page, and Back comes back here.
  await (await update.$("button.download-name")).click();
  await (await app.$(".app-page")).waitForDisplayed();
  await (await app.$(".app-page .back-link")).click();
  await (await page()).waitForDisplayed();

  await (await (await (await section("Updates available")).$(".download-row")).$("button=Update to v1.1.0")).click();
  await until(async () => (await rows("Updates available")).length === 0, 30000);
  await until(async () => {
    const detail = await app.$(".downloads-page section[aria-label='Recently finished'] .download-detail");
    return (await detail.isExisting()) && (await detail.getText()).startsWith("Updated to v1.1.0");
  }, 30000);
  // One row for the app, its latest download.
  assert.deepEqual(await rows("Recently finished"), ["test-port"]);
  const finished = await (await section("Recently finished")).$(".download-row");
  assert.deepEqual(await finished.$$(".download-action button").map((b) => b.getText()), ["See changelog", "Play"]);
  await (await (await section("Recently finished")).$("button=Clear all")).click();
  assert.deepEqual(await rows("Recently finished"), []);
  await app.keys("Escape");
  await (await card("test-port")).waitForDisplayed();
  api.release("1.0.0");
});

test("in the Sidebar layout the way to Downloads is at the sidebar's foot, in place of the strip", async () => {
  await (await app.$("button[aria-label='View options']")).click();
  await (await (await app.$(".view-panel [aria-label='Layout']")).$("button=Sidebar")).click();
  await app.keys("Escape");
  const foot = () => app.$(".sidebar-foot .sidebar-downloads");
  await (await foot()).waitForDisplayed();
  assert.equal(await (await foot()).getText(), "Downloads");

  api.release("1.2.0");
  await (await app.$("button=Browse")).click();
  await (await (await card("test-port")).$("button=Update to v1.2.0")).waitForDisplayed();
  // Browse's strip is only for downloads under way: the updates waiting are the library's to offer.
  assert.ok(!(await (await app.$(".downloads-strip")).isExisting()));
  await (await app.$("button*=Library")).click();
  await until(async () => (await (await foot()).getText()) === "1 update available");
  assert.ok(!(await (await app.$(".downloads-strip")).isExisting()), "no strip beside the sidebar");

  await (await foot()).click();
  await (await page()).waitForDisplayed();
  assert.deepEqual(await rows("Updates available"), ["test-port"]);
  await (await (await page()).$(".back-link")).click();
  await (await app.$(".library-sidebar")).waitForDisplayed();
  api.release("1.0.0");
});
