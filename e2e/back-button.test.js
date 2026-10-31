/**
 * Back without the back link: a mouse's back button, Alt+Left, Escape or a
 * controller's B close the dialog on top, else go back a page, as the
 * "← Browse" link does. The mouse's buttons bind in Settings with the keys,
 * and Settings is a page too.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s;

before(async () => {
  s = await launch();
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

/**
 * A mouse's side button (3 is back, 4 forward), pressed and let go. WebKitGTK's
 * driver only has the three main buttons, so the events come from the page.
 */
const mouse = (button) =>
  s.app.execute((button) => {
    const target = document.activeElement ?? document.body;
    for (const type of ["mousedown", "mouseup"]) target.dispatchEvent(new MouseEvent(type, { button, bubbles: true, cancelable: true }));
  }, button);
/** A controller button, pressed and let go, as the Rust side reports it. */
const pad = async (key) => {
  for (const down of [true, false])
    await s.app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
};
const onPage = () => s.app.$(".app-page").then((p) => p.isDisplayed());
const openPage = async () => {
  await (await (await s.card("test-port")).$(".card-open")).click();
  await s.until(onPage);
};
const backInBrowse = () => s.until(async () => !(await onPage()) && (await (await s.card("test-port")).isDisplayed()));

test("the mouse's back button goes back a page", async () => {
  await openPage();
  await mouse(3);
  await backInBrowse();
});

test("Alt+Left and a controller's B go back a page too", async () => {
  await openPage();
  await s.app.keys(["Alt", "ArrowLeft"]);
  await backInBrowse();
  await openPage();
  await pad("East");
  await backInBrowse();
});

test("with a dialog open, Back closes it and stays on the page", async () => {
  await openPage();
  await (await (await s.app.$(".topbar")).$("button=Sign in")).click();
  await (await s.app.$('[role="dialog"]')).waitForDisplayed();
  await mouse(3);
  await s.until(async () => !(await s.app.$('[role="dialog"]').then((d) => d.isExisting())));
  assert.equal(await onPage(), true);
  await mouse(3);
  await backInBrowse();
});

test("Settings opened over a page goes back to it", async () => {
  await openPage();
  await s.settings();
  assert.equal(await onPage(), false);
  assert.equal(await (await s.app.$("section.settings-page .back-link")).getText(), "Test Port");
  await mouse(3);
  await s.until(onPage);
  await mouse(3);
  await backInBrowse();
});

test("Settings shows Back on Esc, Alt+Left and the mouse's back button, and the mouse's buttons rebind", async () => {
  await s.settings("Controllers");
  const back = await s.app.$("button[aria-label='Cancel / Back: keyboard']");
  assert.equal(await back.getText(), "Esc / Alt+← / Mouse back");
  const options = await s.app.$("button[aria-label='Options: keyboard']");
  await options.click();
  await mouse(4);
  await s.until(async () => (await options.getText()) === "Mouse forward");
  await s.until(() => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).keys?.options?.[0] === "MouseForward");
  await s.closeSettings();
});
