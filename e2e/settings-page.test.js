/**
 * Settings is a page: its sections in a list beside the one picked, or, in a
 * narrow window, the list on its own and a section at a time, where Back goes
 * to the list before it leaves Settings. A controller's B does as Escape does.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s;

before(async () => {
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ telemetryNoticeSeen: true }));
  s = await launch({ data });
  await s.app.setWindowSize(1280, 800);
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => s.app.$("section.settings-page");
const heading = async () => (await (await page()).$("h1")).getText();
const pane = async () => (await (await page()).$("[role=region]")).getAttribute("aria-label");
const shown = async () => (await page()).isExisting();
const focused = () => s.app.execute(() => document.activeElement?.textContent?.trim());
const pad = async (key) => {
  for (const down of [true, false])
    await s.app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
};

test("side by side: General first, a section picked from the list, and Back leaves Settings", async () => {
  await s.settings();
  assert.equal(await heading(), "Settings");
  assert.equal(await pane(), "General");
  await (await s.app.$("label*=Full screen")).waitForDisplayed();
  await (await (await s.app.$('nav[aria-label="Settings sections"]')).$("button*=Updates")).click();
  assert.equal(await pane(), "Updates");
  await (await s.app.$("button=Check for updates")).waitForDisplayed();
  await s.app.keys("Escape");
  await s.until(async () => !(await shown()));
});

test("narrow: the list, then one section, and Back goes to the list first", async () => {
  // Large in the smallest window: too narrow for the list beside a section, with the top bar's gear still in the window.
  await s.settings("General");
  // By its label: TV mode's dropdown comes before it.
  await (await s.app.$("//section[contains(@class, 'settings-page')]//label[contains(., 'Interface size')]/select")).selectByVisibleText("Large");
  await s.closeSettings();
  await s.app.setWindowSize(720, 800);
  await s.settings();
  assert.equal(await (await (await page()).$("[role=region]")).isExisting(), false);
  await (await (await s.app.$('nav[aria-label="Settings sections"]')).$("button*=Controllers")).click();
  assert.equal(await heading(), "Controllers");
  assert.equal(await (await s.app.$('nav[aria-label="Settings sections"]')).isExisting(), false);
  assert.equal(await focused(), "Settings");
  await (await s.app.$("button[aria-label='Back to top: keyboard']")).waitForDisplayed();

  await s.app.keys("Escape");
  await s.until(async () => (await heading()) === "Settings");
  assert.equal(await focused(), "Controllers");
  // A controller's B the same way: into Updates, then back to the list, then out.
  await (await (await s.app.$('nav[aria-label="Settings sections"]')).$("button*=Updates")).click();
  await pad("East");
  await s.until(async () => (await heading()) === "Settings");
  await pad("East");
  await s.until(async () => !(await shown()));
});
