/**
 * A library card's menus with a controller, its playlists and what A does on it, a game's card, and apps not in the library. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { app, back, changed, everything, focusIs, installed, open, page, pause, s, state, toMenuItem, where, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("the new playlist's field lets the d-pad back out, with no playlists to go to instead", async () => {
  await toMenuItem(app, "Add to playlist");
  await reach.press(app, "DPadRight");
  assert.equal(await focusIs(app), "New playlist", "the field has the focus, with nothing else in the list");
  await reach.press(app, "DPadLeft");
  await pause(100);
  assert.equal(await focusIs(app), "Add to playlist");
  assert.equal(await app.execute(() => Boolean(document.querySelector("[aria-label='New playlist']"))), false, "the submenu closed");
  assert.equal(await app.execute(() => Boolean(document.querySelector('.game-menu'))), true, "the menu stayed");
});

for (const mode of ["the usual library", "TV mode"]) {
  test(`in ${mode}, what Add to Steam says shows wherever the page is scrolled: a failure takes the focus, a success doesn't`, async () => {
    const s2 = await openApp({ state: installed });
    try {
      const a = s2.app;
      await s2.page.locator("article[data-slug]").first().waitFor();
      if (mode === "TV mode") {
        await s2.page.locator(".tv-button").click();
        await s2.page.locator(".tv-mode article[data-slug]").first().waitFor();
      }
      await reach.install(a);
      const steamAnswers = (answer) =>
        s2.page.evaluate((answer) => {
          const { invoke } = window.__TAURI_INTERNALS__;
          window.__TAURI_INTERNALS__.invoke = (cmd, args) => (cmd !== "add_to_steam" ? invoke(cmd, args) : answer.fails ? Promise.reject(answer.text) : Promise.resolve(answer.text));
        }, answer);
      await steamAnswers({ fails: true, text: "Steam said no." });
      await toMenuItem(a, "Add to Steam", { manage: true });
      await reach.press(a, "South");
      await s2.page.locator('[role="dialog"][aria-label="Notice"] >> text=Steam said no.').waitFor();
      await s2.until(async () => (await focusIs(a)) === "OK");
      await reach.press(a, "East");
      await s2.page.locator('[role="dialog"][aria-label="Notice"]').waitFor({ state: "detached" });

      await steamAnswers({ text: "Test Port is in Steam." });
      await toMenuItem(a, "Add to Steam", { manage: true });
      await reach.press(a, "South");
      const toast = s2.page.locator('.toast[role="status"]');
      await toast.waitFor();
      assert.ok(await toast.evaluate((el) => { const r = el.getBoundingClientRect(); return r.top >= 0 && r.bottom <= window.innerHeight; }), "it's on the screen");
      assert.notEqual(await focusIs(a), "Test Port is in Steam.", "it leaves the focus alone");
    } finally {
      await s2.close();
    }
  });
}

test("where Steam is running Quiver, Add to Steam is greyed out, saying why", async () => {
  const s2 = await openApp({ state: installed, config: { steamRunsUs: true } });
  try {
    const a = s2.app;
    await s2.page.locator("article[data-slug]").first().waitFor();
    await reach.install(a);
    await reach.pressOn(a, ".library-tile .card-open", "North");
    await a.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].find((b) => b.innerText.includes("Manage")).focus());
    await reach.press(a, "DPadRight");
    const item = s2.page.locator('.game-submenu [role="menuitem"]', { hasText: "Add to Steam" });
    assert.equal(await item.isDisabled(), true);
    await s2.page.locator(".game-submenu .game-menu-note", { hasText: "Steam is running Quiver" }).waitFor();
  } finally {
    await s2.close();
  }
});

test("a library card's menu: Y opens it, the d-pad goes through it and into Manage, B steps out and closes it", async () => {
  await reach.explore(app);
  await reach.pressOn(app, ".library-tile .card-open", "North");
  const items = await app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].map((b) => b.innerText.trim()));
  const seen = new Set([await reach.focused(app)]);
  for (let i = 0; i < items.length; i++) await reach.press(app, "DPadDown"), seen.add(await reach.focused(app));
  assert.equal(seen.size, items.length, "down goes to every item, then round again");
  await app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].find((b) => b.innerText.includes("Manage")).focus());
  await reach.press(app, "DPadRight");
  assert.match(await reach.focused(app), /in Manage/);
  await reach.press(app, "East");
  assert.match(await reach.focused(app), /"Manage"/);
  await reach.press(app, "East");
  assert.equal(await app.execute(() => Boolean(document.querySelector('[role="menu"]'))), false);
});

test("the playlist dialogs, from Add a playlist", async () => {
  // The menu opens with the focus on its first item.
  for (const [item, downs] of [["New playlist", 0], ["Add a shared playlist", 1]]) {
    await open('button[aria-label="Add a playlist"]');
    for (let i = 0; i < downs; i++) await reach.press(app, "DPadDown");
    await reach.press(app, "South");
    assert.equal(await changed("main"), `dialog: ${item}`);
    await everything(item);
    assert.equal(await back(), "main");
  }
});

test("a game's card: Y lists its ports, B and Get there put the focus back on the card, Cancel keeps it on the port's line, and A plays the port played or got last", async () => {
  // Test Port's game, with Test Remake too: one card for both, as View options' One card for each game has them.
  const game = await openApp({
    state: { ...state, settings: { library: { byGame: true } }, library: [...state.library, { id: "entry_test-remake", slug: "test-remake", addedAt: Date.now() - 86400_000 }] },
    // A computer none of the mock's files are for, so Get asks which.
    config: { arch: "arm64" },
  });
  const { app: g } = game;
  try {
    const CARD = 'article[data-game="test-port"] .card-open';
    await game.page.locator(CARD).waitFor();
    await reach.install(g);
    assert.equal(await game.page.locator("article.library-tile").count(), 1, "one card for the game's two ports");
    assert.doesNotMatch(await reach.pressOn(g, CARD, "North"), /^not reached/);
    const ports = () => g.execute(() => [...document.querySelectorAll(".port-panel .port-open strong")].map((n) => n.textContent));
    await game.until(async () => (await ports()).length === 2);
    assert.deepEqual(await ports(), ["Test Port", "Test Remake"], "the one played last first");
    const inList = () => g.execute(() => Boolean(document.querySelector(".port-panel")?.contains(document.activeElement)));
    assert.ok(await inList(), "the focus is in the list");
    await reach.press(g, "DPadDown");
    assert.ok(await inList(), "Down stays in the list");
    await reach.press(g, "East");
    await game.until(async () => !(await game.page.locator(".port-panel").count()));
    const onCard = () => g.execute((q) => document.activeElement === document.querySelector(q), CARD);
    assert.ok(await onCard(), "B closes it, the focus on the card");
    await reach.press(g, "South");
    await game.until(() => g.execute(() => window.__calls.some((c) => c.cmd === "launch" && c.args.id === "entry_test-port")));

    // Get from the list: it closes, the focus back on the card, which shows the download.
    const GET = '.port-row[data-slug="test-remake"] .port-action button';
    const get = async () => {
      assert.doesNotMatch(await reach.pressOn(g, GET, "South"), /^not reached/);
      // None of the files are for this computer: which one.
      await game.until(() => g.execute(() => Boolean(document.querySelector('[role="dialog"]'))));
      assert.doesNotMatch(await reach.pressOn(g, '[role="dialog"] button', "South", { text: "linux" }), /^not reached/);
      await game.until(() => g.execute(() => window.__installs.has("entry_test-remake")));
      await game.until(async () => !(await game.page.locator(".port-panel").count()));
      await game.until(onCard);
    };
    await reach.pressOn(g, CARD, "North");
    await get();
    assert.ok(await game.page.locator('article[data-game="test-port"] .tile-main .progress').count(), "the card shows the download");
    // Open again while it downloads: Cancel there keeps the list open, the focus on the port's line, on Get again.
    await reach.pressOn(g, CARD, "North");
    await game.until(async () => (await ports()).length === 2);
    assert.doesNotMatch(await reach.pressOn(g, '.port-row[data-slug="test-remake"] .cancel-download', "South"), /^not reached/);
    await game.until(() => g.execute(() => !window.__installs.has("entry_test-remake") && !document.querySelector('.port-row[data-slug="test-remake"] .progress')));
    await game.until(() => g.execute(() => document.activeElement?.closest(".port-row")?.getAttribute("data-slug") === "test-remake"));
    assert.ok(await inList(), "the list stays open");
    assert.match(await reach.focused(g), /Get/, "the focus on its Get");
    // Got, and done: A on the card starts the port just got, not the one played before, and says so.
    await get();
    await g.execute(() => window.__installs.get("entry_test-remake").resolve({}));
    await game.until(() => g.execute(() => document.querySelector('article[data-game="test-port"] .tile-play')?.innerText.includes("Test Remake") ?? false));
    await reach.press(g, "South");
    await game.until(() => g.execute(() => window.__calls.some((c) => c.cmd === "launch" && c.args.id === "entry_test-remake")));
  } finally {
    await game.close();
  }
});

test("in Steam's desktop layout, A (which comes as Enter) on a library card plays it, as the hints say", async () => {
  await s.emit("pad-labels", { kind: "steam", south: "A", east: "B", west: "X", north: "Y", desktop: true });
  // An event comes after the call that sent it returns (stand-in.js): here before the press.
  await page.evaluate(() => new Promise((r) => setTimeout(r)));
  await page.focus(".library-tile .card-open");
  await page.keyboard.press("Enter");
  await s.until(() => app.execute(() => window.__calls.some((c) => c.cmd === "launch" && c.args.id === "entry_test-port")));
  assert.equal(await where(), "main", "not its page");
});

test("apps installed here but not in the library: each card is one stop, A opens its page, X adds it, and none has Uninstall", async () => {
  const done = { version: "1.0.0", executables: [], lastPlayed: Date.now() - 86400_000 };
  // Removed from the library, their catalog details kept, as the saved catalog keeps them.
  const known = (slug, name) => ({
    id: `entry_${slug}`, slug, name, projectName: name, description: "", games: [], tags: ["port"], projectType: "port", supportedOS: ["linux", "windows"],
    recommended: 0, reviewCount: 0, reportIssues: 0, reportBroken: 0, addedAt: Date.now(), verified: { version: "1.0.0", releasedAt: Date.now(), prerelease: false, pinned: true },
  });
  const loose = await openApp({
    state: {
      ...state,
      installs: { ...state.installs, "entry_tampered-port": { ...done, folder: "tampered-port" }, "entry_test-remake": { ...done, folder: "test-remake" } },
      catalog: { "entry_tampered-port": known("tampered-port", "Tampered Port"), "entry_test-remake": known("test-remake", "Test Remake") },
    },
  });
  const { app: l, page: p } = loose;
  try {
    const CARD = ".loose .library-tile .card-open";
    await p.locator(CARD).nth(1).waitFor();
    await reach.install(l);
    assert.equal(await p.locator(".loose button", { hasText: "Uninstall" }).count(), 0, "no Uninstall: it's on the app's page");
    const at = () =>
      l.execute(() => {
        const el = document.activeElement;
        return el.matches(".loose .card-open") ? [...document.querySelectorAll(".loose .card-open")].indexOf(el) : (el.getAttribute("aria-label") ?? el.className);
      });
    assert.doesNotMatch(await reach.pressOn(l, CARD, "DPadRight"), /^not reached/);
    assert.equal(await at(), 1, "Right goes to the next card, not into the first one's buttons");
    await reach.press(l, "DPadLeft");
    assert.equal(await at(), 0, "and Left back to the first card");
    // A: its page, as a catalog card's.
    await reach.press(l, "South");
    await loose.until(() => l.execute(() => Boolean([...document.querySelectorAll(".page-layer")].find((x) => !x.hidden))));
    await reach.press(l, "East");
    await loose.until(() => l.execute(() => document.activeElement?.matches(".loose .card-open") ?? false));
    // X: Add, into the library, the card going from the section to it.
    const slug = await l.execute(() => document.activeElement.closest("article").getAttribute("data-slug"));
    await reach.press(l, "West");
    await loose.until(() => l.execute((s) => !document.querySelector(`.loose article[data-slug="${s}"]`) && Boolean(document.querySelector(`article[data-slug="${s}"]:not(.loose article)`)), slug));
  } finally {
    await loose.close();
  }
});

// WebKit, here, would show it from :focus-visible alone; Chromium needs app.css's data-navigating, which this doesn't reach.
test("after the mouse, a menu shows where the d-pad is", async () => {
  await page.locator(".library-tile").first().click({ button: "right" });
  await page.locator('[role="menu"] [role="menuitem"]').first().waitFor();
  // Not over the items, whose hover would look the same.
  await page.mouse.move(2, 2);
  const looks = () =>
    app.execute(() => {
      const items = [...document.querySelectorAll('[role="menu"] > .game-menu > button:not(:disabled, .game-menu-main)')];
      const lit = (el) => getComputedStyle(el).backgroundColor;
      const other = items.find((el) => el !== document.activeElement);
      return { focused: lit(document.activeElement), other: lit(other), on: document.activeElement.innerText.trim() };
    });
  await reach.press(app, "DPadDown");
  await page.waitForTimeout(100);
  const moved = await looks();
  assert.notEqual(moved.focused, moved.other, `${moved.on} is lit`);
});
