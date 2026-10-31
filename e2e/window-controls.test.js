/**
 * Without the system's title bar (the default), the window's own buttons sit
 * at the right of the top bar; Settings brings the system's title bar back,
 * and the buttons go, until next time too.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s;
before(async () => {
  s = await launch();
});
after(() => s?.close());

const buttons = () => s.app.execute(() => [...document.querySelectorAll(".topbar .window-controls button")].map((b) => b.getAttribute("aria-label")));
const titleBarSwitch = async () => (await s.app.$("label*=Use the system's title bar")).$("input");
const saved = () => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8"));

test("the top bar has the window's buttons, last after Settings", async () => {
  await s.until(async () => (await buttons()).length === 3);
  assert.deepEqual(await buttons(), ["Minimize", "Maximize", "Close"]);
  const last = await s.app.execute(() => document.querySelector(".topbar").lastElementChild.className);
  assert.equal(last, "window-controls");
  // In the window's corner: the bar has no padding on that side.
  assert.equal(await s.app.execute(() => getComputedStyle(document.querySelector(".topbar")).paddingRight), "0px");
});

test("the buttons are on the right before the account part has loaded", async () => {
  // At startup the account part (Sign in, or the player's name) isn't there yet.
  const gearRight = () => s.app.execute(() => Math.round(document.querySelector("button[aria-label=Settings]").getBoundingClientRect().right));
  const before = await gearRight();
  await s.app.execute(() => document.querySelector(".topbar .account").style.setProperty("display", "none"));
  assert.equal(await gearRight(), before);
  await s.app.execute(() => document.querySelector(".topbar .account").style.removeProperty("display"));
});

test("Settings brings the system's title bar back, and the buttons go", async () => {
  await s.settings("General");
  const toggle = await titleBarSwitch();
  await toggle.waitForExist({ timeout: 10000 });
  assert.equal(await toggle.isSelected(), false);
  await toggle.click();
  await s.closeSettings();
  await s.until(async () => (await buttons()).length === 0);
  await s.until(() => saved().systemTitleBar === true);
  // The gear is back 6px (its own padding aside) from the window's edge.
  assert.equal(await s.app.execute(() => getComputedStyle(document.querySelector(".topbar")).paddingRight), "6px");
});

test("it stays on next time, and turning it off brings the buttons back", async () => {
  const data = s.data;
  await s.close();
  s = await launch({ data });
  await (await s.app.$("button[aria-label=Settings]")).waitForDisplayed({ timeout: 20000 });
  assert.deepEqual(await buttons(), []);
  await s.settings("General");
  const toggle = await titleBarSwitch();
  await toggle.waitForExist({ timeout: 10000 });
  assert.equal(await toggle.isSelected(), true);
  await toggle.click();
  await s.closeSettings();
  await s.until(async () => (await buttons()).length === 3);
  await s.until(() => saved().systemTitleBar === false);
});
