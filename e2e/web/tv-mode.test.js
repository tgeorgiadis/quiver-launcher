/**
 * TV mode's screens (src/tv), over the desktop, each from an app of its own. (tv-size.test.js has its size on the screen.)
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import { installed, state } from "./controller.js";
import * as reach from "../reach.js";

after(stop);

const selectedTab = (s) => s.page.locator(".tv-tab.selected").innerText();
/** A controller's press: down, then up. */
const press = async (s, key) => {
  await s.pad(key, true);
  await s.pad(key, false);
};

test("the top bar's TV button opens TV mode over the desktop, and its Settings go back", async () => {
  const s2 = await openApp({ state: installed });
  try {
    await s2.page.locator("article[data-slug]").first().waitFor();
    await s2.page.locator(".tv-button").click();
    await s2.page.locator(".tv-root").waitFor();
    assert.equal(await s2.page.locator(".tv-desktop").getAttribute("inert"), "", "the desktop can't be reached under it");
    assert.equal(await s2.app.execute(() => window.__files.settings.tvMode), "on");
    await press(s2, "Start");
    await s2.page.locator(".tv-settings li", { hasText: "Switch to desktop mode" }).click();
    await s2.page.locator(".tv-root").waitFor({ state: "detached" });
    await s2.page.locator(".tv-button").waitFor();
    assert.equal(await s2.app.execute(() => window.__files.settings.tvMode), "off");
  } finally {
    await s2.close();
  }
});

test("a controller's shoulder buttons go between the tabs, and its triggers to Discover and Downloads", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  try {
    assert.equal(await selectedTab(s2), "Home");
    await press(s2, "RightTrigger");
    await s2.until(async () => (await selectedTab(s2)) === "All");
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)).startsWith("Downloads"));
    await press(s2, "LeftTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
  } finally {
    await s2.close();
  }
});

test("TV mode's buttons are the player's: with A and B swapped, B changes a setting and A goes back", async () => {
  const swapped = { tvMode: "on", pad: { confirm: ["East"], back: ["South"] } };
  const s2 = await openApp({ state: { ...installed, settings: swapped }, ready: ".tv-tab.selected" });
  try {
    await press(s2, "Start");
    const first = s2.page.locator(".tv-settings li").first();
    await first.waitFor();
    const before = await first.innerText();
    await press(s2, "East");
    await s2.until(async () => (await first.innerText()) !== before);
    await press(s2, "South");
    await s2.page.locator(".tv-settings").waitFor({ state: "detached" });
  } finally {
    await s2.close();
  }
});

test("Steam's Game Mode opens in TV mode, whose Settings have Close; with it off, a controller reaches the desktop's Close", async () => {
  const tv = await openApp({ state: installed, width: 1113, height: 696, config: { gameMode: true }, ready: ".tv-tab.selected" });
  try {
    await press(tv, "Start");
    await tv.page.locator(".tv-settings li", { hasText: "Close Quiver" }).waitFor();
  } finally {
    await tv.close();
  }
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "off" } }, width: 1113, height: 696, config: { gameMode: true } });
  try {
    assert.equal(await s2.page.locator(".tv-root").count(), 0);
    assert.equal(await s2.page.locator(".window-controls button").count(), 1);
    await reach.install(s2.app);
    await reach.pressOn(s2.app, ".topbar button[aria-label='Settings']", "DPadRight");
    await s2.until(() => s2.app.execute(() => document.activeElement?.matches(".close-window")));
  } finally {
    await s2.close();
  }
});

test("a game with an update waiting is on Home's Updates shelf", async () => {
  const waiting = { ...installed, installs: { "entry_test-port": { ...state.installs["entry_test-port"], version: "0.7.0" } } };
  const tv = await openApp({ state: { ...waiting, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  try {
    await tv.page.getByText("Updates available").waitFor();
  } finally {
    await tv.close();
  }
});

test("TV mode's settings are kept in the launcher's, only those changed", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  try {
    await press(s2, "Start");
    // Clicked, not focused first: the row clicked changes.
    await s2.page.locator(".tv-settings li", { hasText: "Reduce motion" }).click();
    await s2.page.locator(".tv-root.tv-reduce-motion").waitFor();
    assert.deepEqual(await s2.app.execute(() => window.__files.settings.tv), { reduceMotion: true });
  } finally {
    await s2.close();
  }
});
