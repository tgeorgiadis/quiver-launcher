/**
 * Back to top: on a long page, a button in the corner, Home, or a
 * controller's R3 goes back to the top, with the focus on the top control so
 * the next move starts from there.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s;

before(async () => {
  // A large interface in a small window, so the catalog runs well past a screen.
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ scale: 2 }));
  s = await launch({ data });
  await s.app.setWindowSize(900, 600);
  await (await s.app.$("article[data-slug]")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

/** A controller button, pressed and let go, as the Rust side reports it. */
const pad = async (key) => {
  for (const down of [true, false])
    await s.app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
};
const scrolled = () => s.app.execute(() => document.querySelector(".shell > main").scrollTop);
const button = () => s.app.$("button[aria-label='Back to top']");
/** Focuses the last card's link and scrolls it into view, as moving down to it would. */
const toBottom = async () => {
  await s.app.execute(() => {
    const last = [...document.querySelectorAll("main article .card-open")].at(-1);
    last.focus();
    document.querySelector(".shell > main").scrollTo(0, document.querySelector(".shell > main").scrollHeight);
  });
  await s.until(async () => (await scrolled()) > 0 && (await button().then((b) => b.isDisplayed())));
};
/** The focused control's distance from the top of the page, and whether it's in the page's own content. */
const focusAt = () =>
  s.app.execute(() => {
    const el = document.activeElement;
    return { top: el.getBoundingClientRect().top + document.querySelector(".shell > main").scrollTop, inMain: Boolean(el.closest("main")) };
  });

test("the button shows only once the page has scrolled, and takes it back to the top", async () => {
  assert.equal(await button().then((b) => b.isExisting()), false);
  await toBottom();
  await (await button()).click();
  await s.until(async () => (await scrolled()) === 0);
  await s.until(async () => !(await button().then((b) => b.isExisting())));
});

test("Home goes back to the top and moves the focus up with it", async () => {
  await toBottom();
  await s.app.keys("Home");
  await s.until(async () => (await scrolled()) === 0);
  const at = await focusAt();
  assert.ok(at.inMain);
  assert.ok(at.top < 600, `the focus is near the top (${at.top})`);
});

test("a controller's R3 goes back to the top, and the next move starts there", async () => {
  await toBottom();
  const bottom = await scrolled();
  await pad("RightThumb");
  await s.until(async () => (await scrolled()) === 0);
  const at = await focusAt();
  assert.ok(at.inMain && at.top < 600, `the focus is near the top (${at.top})`);
  // Moving down from the top doesn't jump back to the bottom.
  await pad("DPadDown");
  await new Promise((r) => setTimeout(r, 800));
  assert.ok((await scrolled()) < bottom / 2);
});

test("Home in a text field stays the field's", async () => {
  const search = await s.app.$("input[placeholder^=Search]");
  await search.click();
  await search.setValue("test");
  await s.app.execute(() => document.querySelector(".shell > main").scrollTo(0, 400));
  await s.app.execute(() => document.querySelector("input[placeholder^=Search]").focus({ preventScroll: true }));
  await s.app.keys("Home");
  await new Promise((r) => setTimeout(r, 500));
  assert.equal(await s.app.execute(() => document.activeElement?.getAttribute("placeholder")?.startsWith("Search")), true);
  assert.equal(await s.app.execute(() => document.activeElement.selectionStart), 0);
  await search.clearValue();
});

test("Back to top is in Settings with the other bindings, on Home and R3", async () => {
  await s.settings("Controllers");
  assert.equal(await (await s.app.$("button[aria-label='Back to top: keyboard']")).getText(), "Home");
  assert.equal(await (await s.app.$("button[aria-label='Back to top: controller']")).getText(), "R3");
  await s.closeSettings();
});
