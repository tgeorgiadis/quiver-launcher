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
/**
 * What flashed up over the page (Flash.tsx), and whether a tab popped, from now on: watched for as it happens, as on a
 * slow machine the flash can be gone again before a check.
 */
const watchFlashes = (s) =>
  s.page.evaluate(() => {
    window.__flashes = [];
    window.__popped = false;
    new MutationObserver(() => {
      const flash = document.querySelector(".tv-flash")?.textContent;
      if (flash && !window.__flashes.includes(flash)) window.__flashes.push(flash);
      if (document.querySelector(".tv-tab.selected.pop")) window.__popped = true;
    }).observe(document.body, { subtree: true, childList: true, attributes: true, characterData: true });
  });
const flashed = (s, text) => s.until(() => s.page.evaluate((t) => window.__flashes.includes(t), text));

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
    await watchFlashes(s2);
    await press(s2, "RightTrigger");
    await s2.until(async () => (await selectedTab(s2)) === "All");
    // Each change flashes the tab's name over the page, and pops it in the tab strip.
    await flashed(s2, "All");
    assert.equal(await s2.page.evaluate(() => window.__popped), true);
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
    await flashed(s2, "Discover");
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)).startsWith("Downloads"));
    await press(s2, "LeftTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
  } finally {
    await s2.close();
  }
});

test("on Discover the shoulder buttons step through the consoles, round to All, and Filter (X) narrows them", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  const consoles = () => s2.page.locator(".tv-chip-row.consoles .tv-filter:not(.tv-filter-button)");
  const chosen = () => s2.page.locator(".tv-chip-row.consoles .tv-filter.chosen:not(.tv-filter-button)").innerText();
  try {
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
    await s2.until(async () => (await chosen()) === "All consoles");
    // The test catalog's consoles, by year: SNES, then N64; then round to All. Discover stays the tab, and the
    // console's full name flashes up over the games.
    await watchFlashes(s2);
    await press(s2, "RightTrigger");
    await s2.until(async () => (await chosen()) === "SNES");
    await flashed(s2, "Super Nintendo Entertainment System");
    await press(s2, "RightTrigger");
    await s2.until(async () => (await chosen()) === "N64");
    await press(s2, "RightTrigger");
    await s2.until(async () => (await chosen()) === "All consoles");
    await press(s2, "LeftTrigger");
    await s2.until(async () => (await chosen()) === "N64");
    assert.equal(await selectedTab(s2), "Discover");
    await s2.page.locator(".tv-flash").waitFor({ state: "detached" });
    // Filter: X opens its list from the games, under its button. Its first line is the library's choice, left and right.
    /** What Filter/Sort picked, as the top right shows it. */
    const picks = { innerText: async () => (await s2.page.locator(".tv-filter-summary span").allInnerTexts()).join(", ") };
    const row = (name) => s2.page.locator(".tv-dropdown li", { hasText: name });
    assert.equal(await picks.innerText(), "Recently added, Not in library");
    await press(s2, "West");
    await s2.page.locator(".tv-dropdown").waitFor();
    // Sort first: left and right change the catalog's order; then the library's choice.
    await press(s2, "DPadRight");
    await s2.until(async () => (await picks.innerText()) === "Recently updated, Not in library");
    await press(s2, "DPadLeft");
    await s2.until(async () => (await picks.innerText()) === "Recently added, Not in library");
    await press(s2, "DPadDown");
    await press(s2, "DPadRight");
    await s2.until(async () => (await picks.innerText()) === "Recently added, In library");
    await press(s2, "DPadLeft");
    await s2.until(async () => (await picks.innerText()) === "Recently added, Not in library");
    // Handhelds: neither console is one, so the one shown goes back to All, and there's nothing.
    await row("Handhelds").click();
    await s2.until(async () => (await chosen()) === "All consoles");
    await s2.page.getByText("No consoles match this filter").waitFor();
    assert.equal(await consoles().filter({ hasText: "N64" }).count(), 0);
    // More than one at once: Home consoles too, and Nintendo; then B closes the list.
    await row("Home consoles").click();
    await row("Nintendo").click();
    await consoles().filter({ hasText: "N64" }).waitFor();
    await press(s2, "East");
    await s2.page.locator(".tv-dropdown").waitFor({ state: "detached" });
    assert.equal(await picks.innerText(), "Recently added, Not in library, Nintendo, Home consoles, Handhelds");
    assert.deepEqual(await consoles().allInnerTexts(), ["All consoles", "SNES", "N64"]);
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

test("Play on a game that needs the player's own copy opens its setup over TV mode, and the controller works it", async () => {
  const needsCopy = (api) =>
    api.setEntry("test-port", {
      launcher: { folderName: "test-port", filesToAdd: ["portable.txt"], setupFiles: [{ id: "rom", label: "Test Port ROM", accepted: [], destination: "baserom.z64" }] },
    });
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, prepare: needsCopy, ready: ".tv-tab.selected" });
  try {
    await press(s2, "RightTrigger");
    await s2.until(async () => (await selectedTab(s2)) === "All");
    await s2.page.locator(".tv-tile.focused").waitFor();
    await press(s2, "South");
    // Upstream's setup (own-copy-setup.tsx), reachable over TV mode rather than in the desktop underneath it.
    const prompt = s2.page.locator(".own-copy-prompt");
    await prompt.waitFor();
    assert.equal(await prompt.evaluate((el) => el.closest("[inert]") === null), true);
    assert.equal(await s2.app.execute(() => window.__calls.some((c) => c.cmd === "launch")), false, "not started without its copy");
    // The controller is the dialog's while it's open: the d-pad goes through its buttons, B closes it, and TV mode has
    // the controller again.
    const focused = () => s2.page.evaluate(() => document.activeElement?.textContent?.trim());
    await s2.until(async () => (await focused()) === "Choose a file…");
    await press(s2, "DPadRight");
    await s2.until(async () => (await focused()) === "Play without it");
    await press(s2, "East");
    await prompt.waitFor({ state: "detached" });
    await press(s2, "LeftTrigger");
    await s2.until(async () => (await selectedTab(s2)) === "Home");
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

test("Discover sorts by release date once a console is picked, not the whole catalog", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  try {
    await press(s2, "RightTrigger2");
    await s2.until(async () => (await selectedTab(s2)) === "Discover");
    await s2.page.locator(".tv-chip-row.consoles").waitFor();
    await press(s2, "West");
    await s2.page.locator(".tv-dropdown").waitFor();
    // Sort, first: Recently added, Recently updated, Top rated, Name A–Z, then Release date, oldest first.
    for (let i = 0; i < 4; i++) await press(s2, "DPadRight");
    await s2.until(async () => (await s2.page.locator(".tv-filter-summary span").first().innerText()) === "Release date, oldest first");
    await press(s2, "East");
    await s2.page.locator(".tv-dropdown").waitFor({ state: "detached" });
    await s2.page.getByText("Pick a console to sort by release date").waitFor();
    // A console: its games, in order.
    await press(s2, "RightTrigger");
    await s2.page.getByText("Pick a console to sort by release date").waitFor({ state: "detached" });
    await s2.page.locator(".tv-discover [data-index]", { hasText: "Tampered Port" }).first().waitFor();
  } finally {
    await s2.close();
  }
});

test("X in the library's tabs opens Sort: a pick sorts every tab, flashes its name and is kept", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, ready: ".tv-tab.selected" });
  try {
    await press(s2, "RightTrigger");
    await s2.until(async () => (await selectedTab(s2)) === "All");
    await s2.page.locator(".tv-hints-right", { hasText: "Sort" }).waitFor();
    await press(s2, "West");
    const list = s2.page.locator(".tv-sort-list");
    await list.waitFor();
    assert.deepEqual(
      (await list.locator("li .tv-settings-label").allInnerTexts()).slice(0, 4),
      ["Name", "Game", "Release date, oldest first", "Release date, newest first"],
    );
    await watchFlashes(s2);
    await list.locator("li", { hasText: "Most played" }).click();
    await list.waitFor({ state: "detached" });
    await flashed(s2, "Most played");
    assert.deepEqual(await s2.app.execute(() => window.__files.settings.tv), { sort: "playtime" });
    // B closes it without changing anything.
    await press(s2, "West");
    await list.waitFor();
    await press(s2, "East");
    await list.waitFor({ state: "detached" });
    assert.deepEqual(await s2.app.execute(() => window.__files.settings.tv), { sort: "playtime" });
  } finally {
    await s2.close();
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
