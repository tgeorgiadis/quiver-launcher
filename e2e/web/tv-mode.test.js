/**
 * TV mode's pages, each from an app of its own. (tv-size.test.js has its size on the screen.)
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import { installed, state } from "./controller.js";
import * as reach from "../reach.js";

after(stop);

test("TV mode is the usual pages, larger: it stays on the tab it's turned on from, with Browse's search, and goes back", async () => {
  const s2 = await openApp({ state: installed });
  try {
    await s2.page.locator("article[data-slug]").first().waitFor();
    await s2.page.locator(".topbar nav button", { hasText: "Browse" }).click();
    await s2.page.locator(".tv-button").click();
    await s2.page.locator(".shell.tv-mode").waitFor();
    assert.equal(await s2.app.execute(() => document.querySelector(".topbar nav button.active").innerText.includes("Browse")), true, "still on Browse");
    await s2.page.locator("main input[type='search'], main .search input").first().waitFor();
    await s2.page.locator(".topbar nav button", { hasText: "Library" }).click();
    await s2.page.locator(".library-grid.size-large.image-box article[data-slug]").first().waitFor();
    await s2.page.locator(".tv-leave").click();
    await s2.page.locator(".shell:not(.tv-mode) .tv-button").waitFor();
  } finally {
    await s2.close();
  }
});

test("TV mode's top bar fits on one line at a Steam Deck's size, and Game Mode has only Close of the window's buttons", async () => {
  for (const gameMode of [true, false]) {
    const s2 = await openApp({ state: installed, width: 1113, height: 696, config: { gameMode } });
    try {
      await s2.page.locator(".topbar").waitFor();
      if (!gameMode) await s2.page.locator(".tv-button").click();
      await s2.page.locator(".shell.tv-mode .tv-leave").waitFor();
      const bar = await s2.app.execute(() => {
        const height = (selector) => document.querySelector(selector).getBoundingClientRect().height;
        const top = document.querySelector(".topbar");
        return { clock: height(".tv-clock"), leave: height(".tv-leave"), fits: top.scrollWidth <= top.clientWidth, controls: document.querySelectorAll(".window-controls button").length, close: document.querySelector(".window-controls button:last-child").matches(".close-window"), closeShows: document.querySelector(".close-window").getBoundingClientRect().right <= innerWidth + 0.5 };
      });
      assert.ok(bar.clock < 30, `the clock on one line: ${bar.clock}`);
      assert.ok(bar.leave < 40, `Leave TV mode on one line: ${bar.leave}`);
      assert.equal(bar.fits, true, "nothing past the bar's end");
      assert.equal(bar.controls, gameMode ? 1 : 3);
      assert.equal(bar.close, true, "Close is one of them");
      assert.equal(bar.closeShows, true, "and on the screen");
    } finally {
      await s2.close();
    }
  }
});

test("Game Mode has Close with TV mode off too, and a controller can reach it", async () => {
  const s2 = await openApp({ state: { ...installed, settings: { tvMode: "off" } }, width: 1113, height: 696, config: { gameMode: true } });
  try {
    await s2.page.locator(".shell:not(.tv-mode) .topbar").waitFor();
    assert.equal(await s2.page.locator(".window-controls button").count(), 1);
    await reach.install(s2.app);
    await reach.pressOn(s2.app, ".topbar button[aria-label='Settings']", "DPadRight");
    await s2.until(() => s2.app.execute(() => document.activeElement?.matches(".close-window")));
  } finally {
    await s2.close();
  }
});

test("TV mode's library has no banner offering updates: the Downloads button's count says so", async () => {
  const waiting = { ...installed, installs: { "entry_test-port": { ...state.installs["entry_test-port"], version: "0.7.0" } } };
  const usual = await openApp({ state: waiting });
  try {
    await usual.page.locator(".downloads-strip").waitFor();
  } finally {
    await usual.close();
  }
  const tv = await openApp({ state: { ...waiting, settings: { tvMode: "on" } } });
  try {
    await tv.page.locator(".library-grid").waitFor();
    await tv.page.locator(".downloads-button .downloads-count").waitFor();
    assert.equal(await tv.page.locator(".downloads-strip").count(), 0);
  } finally {
    await tv.close();
  }
});

test("a player who kept TV mode's old screen gets the one TV mode, and Settings has no choice of look", async () => {
  const tv = await openApp({ state: { ...installed, settings: { tvMode: "on", tvLook: "screen" } } });
  try {
    await tv.page.locator(".shell.tv-mode .topbar").waitFor();
    await tv.page.locator(".library-grid").waitFor();
    await tv.page.locator(".topbar button[aria-label='Settings']").click();
    await tv.page.locator(".settings-nav button", { hasText: "General" }).first().click();
    await tv.page.locator(".settings-pane label", { hasText: "TV mode" }).first().waitFor();
    assert.equal(await tv.page.getByText("TV mode's look").count(), 0);
  } finally {
    await tv.close();
  }
});

test("TV mode's library has Group by and View options, for what TV mode leaves to the player", async () => {
  const tv = await openApp({ state: { ...installed, settings: { tvMode: "on" } }, width: 1113, height: 696 });
  const { page: p, app: a } = tv;
  try {
    await p.locator(".library-grid").waitFor();
    await reach.install(a);
    const tools = p.locator(".library-tools");
    await tools.locator(".group-by select").waitFor();
    const rows = await tools.evaluate((t) => new Set([...t.children].map((c) => Math.round(c.getBoundingClientRect().y))).size);
    assert.equal(rows, 1, "the tools on one row, at a Deck's size");

    assert.doesNotMatch(await reach.pressOn(a, 'button[aria-label="View options"]', "South"), /^not reached/);
    const panel = p.locator(".view-panel");
    await panel.waitFor();
    for (const name of ["One card for each game", "Show names on cards", "Show Continue playing", "Show installed and update icons"]) await panel.getByText(name).waitFor();
    for (const name of ["Layout", "Card image", "Card size"]) assert.equal(await panel.getByText(name, { exact: true }).count(), 0, `${name} is TV mode's`);

    await panel.getByText("Show names on cards").click();
    await p.locator(".layout-grid.no-names.size-large").waitFor();
    assert.deepEqual(await a.execute(() => window.__files.settings.library.layout ?? "grid"), "grid");
  } finally {
    await tv.close();
  }
});
