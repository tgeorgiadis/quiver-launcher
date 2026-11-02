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
/** A controller button going down or up, as the Rust side reports it. */
const padEvent = (key, down) => s.app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
/** A controller button, pressed and let go. */
const pad = async (key) => {
  for (const down of [true, false]) await padEvent(key, down);
};
const onPage = () => s.app.$(".app-page").then((p) => p.isDisplayed());
const openPage = async () => {
  // On its artwork: the middle of a box card is its Get button.
  await (await (await s.card("test-port")).$(".card-open")).click({ y: -100 });
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

test("the mouse's forward button and Alt+Right open again the page Back left", async () => {
  await openPage();
  await mouse(3);
  await backInBrowse();
  await mouse(4);
  await s.until(onPage);
  await mouse(3);
  await backInBrowse();
  await s.app.keys(["Alt", "ArrowRight"]);
  await s.until(onPage);
  await mouse(3);
  await backInBrowse();
});

test("opening another page or another tab leaves Forward nothing to open", async () => {
  await openPage();
  await mouse(3);
  await backInBrowse();
  await s.settings();
  await mouse(4);
  await s.app.pause(500);
  assert.equal(await onPage(), false);
  await mouse(3);
  await backInBrowse();

  await openPage();
  await mouse(3);
  await backInBrowse();
  await (await s.app.$("button*=Library")).click();
  await mouse(4);
  await s.app.pause(500);
  assert.equal(await onPage(), false);
  await (await s.app.$("button=Browse")).click();
  await backInBrowse();
});

test("Forward, given a controller button, opens no page under a dropdown's list", async () => {
  await s.settings("Controllers");
  const forward = await s.app.$("button[aria-label='Forward: controller']");
  await forward.click();
  await pad("LeftPaddle1");
  await s.until(async () => (await forward.getText()) !== "—");
  await s.closeSettings();
  await backInBrowse();

  await openPage();
  await mouse(3);
  await backInBrowse();
  await s.app.execute(() => [...document.querySelectorAll("select")].find((el) => el.offsetParent).focus());
  await pad("South");
  const list = await s.app.$(".select-picker");
  await list.waitForDisplayed();
  await pad("LeftPaddle1");
  await s.app.pause(500);
  assert.equal(await onPage(), false, "a page opened under the dropdown's list");
  await pad("East");
  await s.until(async () => !(await list.isExisting()));
});

test("Forward on a controller opens one page a press, however long it's held", async () => {
  // Two pages to go forward through: the app's, then Settings over it.
  await openPage();
  await s.settings();
  await mouse(3);
  await s.until(onPage);
  await mouse(3);
  await backInBrowse();
  await padEvent("LeftPaddle1", true);
  await s.app.pause(800);
  await padEvent("LeftPaddle1", false);
  assert.equal(await onPage(), true, "held, it went on past the app's page");
  await mouse(3);
  await backInBrowse();
});

test("Settings shows Back on Esc, Alt+Left and the mouse's back button, and the mouse's buttons rebind", async () => {
  await s.settings("Controllers");
  const back = await s.app.$("button[aria-label='Cancel / Back: keyboard']");
  assert.equal(await back.getText(), "Esc / Alt+← / Mouse back");
  assert.equal(await (await s.app.$("button[aria-label='Forward: keyboard']")).getText(), "Alt+→ / Mouse forward");
  const options = await s.app.$("button[aria-label='Options: keyboard']");
  await options.click();
  await mouse(4);
  await s.until(async () => (await options.getText()) === "Mouse forward");
  await s.until(() => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).keys?.options?.[0] === "MouseForward");
  await s.closeSettings();
});
