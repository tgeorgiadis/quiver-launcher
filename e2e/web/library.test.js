/**
 * The library with a controller: where the focus lands and goes, and its layouts. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { app, back, everything, focusIs, focusedSelector, home, installed, open, page, pause, s, state, where, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("the library: a card's own buttons are its menu's, A plays it and Y opens the menu", async () => {
  await everything("the library", ".library-tile .tile-overlay button");
});

test("the Quiver logo isn't a stop for the d-pad: past the Library tab, it stays", async () => {
  await reach.pressOn(app, ".topbar nav button.active", "DPadLeft");
  await pause(100);
  assert.equal(await app.execute(() => document.activeElement?.matches(".topbar nav button")), true);
  assert.equal(await app.execute(() => document.querySelector(".brand").tabIndex), -1, "nor for Tab");
});

test("the first press lands on Continue playing's Play, else on the library's first card", async () => {
  assert.equal(await focusedSelector(app), "nothing");
  await reach.press(app, "DPadDown");
  assert.equal(await focusedSelector(app), "play");
  const s2 = await openApp({ state: { ...installed, settings: { library: { continuePlaying: false } } } });
  try {
    await s2.page.locator("article[data-slug]").first().waitFor();
    await reach.install(s2.app);
    await reach.press(s2.app, "DPadDown");
    assert.equal(await focusedSelector(s2.app), "card");
  } finally {
    await s2.close();
  }
});

test("with a controller there as the library comes, Play has the focus at once", async () => {
  await s.emit("pads", ["Test pad"]);
  await s.until(async () => (await focusedSelector(app)) === "play");
  await s.emit("pads", []);
});

test("up and down go by way of Search and Play, wherever they start across the screen", async () => {
  // The Add an app button, far from Search: up to Play, not the catalog button beside the banner's foot.
  await reach.pressOn(app, ".add-app", "DPadUp");
  assert.equal(await focusedSelector(app), "play");
  await reach.press(app, "DPadDown");
  assert.equal(await focusedSelector(app), "search");
  await reach.press(app, "DPadDown");
  assert.equal(await focusedSelector(app), "card");
  await reach.press(app, "DPadUp");
  assert.equal(await focusedSelector(app), "search");
  // From the far end of the top bar, down: Play too, not past it, or whatever's under that button.
  await reach.pressOn(app, ".topbar button[aria-label='Settings']", "DPadDown");
  const notices = await app.execute(() => document.querySelectorAll(".banner-stack button").length);
  if (notices) await reach.pressOn(app, ".banner-stack button", "DPadDown");
  assert.equal(await focusedSelector(app), "play");
});

test("up to the top of a scrolled page takes one press, not one to scroll and one to move", async () => {
  const s2 = await openApp({ state: installed, height: 520 });
  try {
    const a = s2.app;
    await s2.page.locator("article[data-slug]").first().waitFor();
    await reach.install(a);
    await reach.pressOn(a, ".library-tile .card-open", "DPadUp");
    const seen = [await focusIs(a)];
    for (let i = 0; i < 6 && !(await a.execute(() => Boolean(document.activeElement?.closest(".topbar")))); i++) {
      await reach.press(a, "DPadUp");
      const now = await focusIs(a);
      assert.notEqual(now, seen.at(-1), `every Up moves the focus: after ${seen.join(", ")}`);
      seen.push(now);
    }
    assert.equal(await a.execute(() => Boolean(document.activeElement?.closest(".topbar"))), true);
    await s2.until(() => a.execute(() => document.querySelector(".shell > main").scrollTop === 0));
  } finally {
    await s2.close();
  }
});

test("into the library's sidebar from the app beside it, the d-pad lands on the app open", async () => {
  const many = { library: [...state.library, { id: "entry_test-remake", slug: "test-remake", addedAt: Date.now() - 86400_000 }], installs: state.installs };
  const s2 = await openApp({ state: { ...many, settings: { library: { layout: "sidebar" } } }, height: 700 });
  try {
    const a = s2.app;
    await s2.page.locator(".sidebar-row").first().waitFor();
    await reach.install(a);
    await s2.page.locator('.sidebar-row[data-slug="test-remake"]').click();
    await pause(200);
    await reach.pressOn(a, ".library-pane .back-link, .library-pane .app-actions button, .library-pane button", "DPadLeft");
    assert.equal(await a.execute(() => document.activeElement?.dataset.slug), "test-remake");
  } finally {
    await s2.close();
  }
});

test("Installed and Not installed are only filters: not tabs that can disagree with them", async () => {
  const tabs = await app.execute(() => [...document.querySelectorAll(".playlists button, .library-show option")].map((b) => b.innerText || b.label));
  assert.ok(tabs.some((name) => name.trim() === "All"));
  assert.ok(!tabs.some((name) => /installed/i.test(name)), tabs.join());
});

test("the library's sidebar layout", async () => {
  await home();
  await open("button", { text: "Library" });
  await open('button[aria-label="View options"]');
  await app.execute(() => [...document.querySelectorAll("button")].find((b) => b.innerText.trim() === "Sidebar").focus());
  await reach.press(app, "South");
  await back();
  await everything("the sidebar layout");
  // Down from Play stays in the app's pane, to its tabs, rather than going to the sidebar beside it.
  await app.execute(() => [...document.querySelectorAll(".app-actions button")].find((b) => b.innerText.trim() === "Play").focus());
  await reach.press(app, "DPadDown");
  assert.equal(await reach.focused(app), 'button "Overview" in App sections');
});

test("A on a dropdown opens its options: the d-pad goes through them, A picks one, and B closes them and goes no further", async () => {
  await home();
  await open("button", { text: "Library" });
  const SORT = ".library-tools select[aria-label=Sort]";
  const picked = () => app.execute((q) => document.querySelector(q).selectedOptions[0].label, SORT);
  const inList = () => app.execute(() => Boolean(document.querySelector(".select-picker")?.contains(document.activeElement)));
  const was = await picked();
  assert.doesNotMatch(await reach.pressOn(app, SORT, "South"), /^not reached/);
  // Its options, the focus on the one picked now.
  assert.deepEqual(
    await app.execute(() => [...document.querySelectorAll(".select-picker [role=option]")].map((o) => o.textContent)),
    await app.execute((q) => [...document.querySelector(q).options].map((o) => o.label), SORT),
  );
  assert.equal(await app.execute(() => document.activeElement.textContent), was);
  // The d-pad keeps to the list.
  await reach.press(app, "DPadLeft");
  assert.ok(await inList(), "Left stays in the list");
  await reach.press(app, "DPadDown");
  assert.ok(await inList(), "Down stays in the list");
  const next = await app.execute(() => document.activeElement.textContent);
  assert.notEqual(next, was);
  await reach.press(app, "South");
  assert.ok(!(await page.locator(".select-picker").count()), "A on an option closes the list");
  assert.equal(await picked(), next);
  assert.equal(await reach.focused(app), 'select "Sort"');
  // B: the list closes, the sort as it was, and the library stays.
  await reach.press(app, "South");
  assert.ok(await inList());
  await reach.press(app, "DPadDown");
  await reach.press(app, "East");
  assert.ok(!(await page.locator(".select-picker").count()), "B closes the list");
  assert.equal(await picked(), next);
  assert.equal(await reach.focused(app), 'select "Sort"');
  assert.equal(await where(), "main");
});

test("at another interface size, the focus box is on what has the focus and a game's ports line up with its card", async () => {
  // Zoomed as Settings' Interface size zooms the page, which WebKit (a Steam Deck's) measures as it lays it out, not as
  // it shows: the box and the list were drawn a fraction of the way toward the window's corner.
  const game = await openApp({
    state: { ...state, settings: { scale: 1.25, library: { byGame: true } }, library: [...state.library, { id: "entry_test-remake", slug: "test-remake", addedAt: Date.now() - 86400_000 }] },
  });
  const { app: g } = game;
  try {
    const CARD = 'article[data-game="test-port"] .card-open';
    await game.page.locator(CARD).waitFor();
    await reach.install(g);
    assert.doesNotMatch(await reach.pressOn(g, CARD, "North"), /^not reached/);
    await game.until(() => g.execute(() => Boolean(document.querySelector(".port-panel")?.contains(document.activeElement))));
    const [panel, card] = await g.execute((q) => [".port-panel", q].map((s) => document.querySelector(s).getBoundingClientRect().toJSON()), CARD);
    assert.ok(Math.min(Math.abs(panel.left - card.left), Math.abs(panel.right - card.right)) < 2, `the list (${panel.left}–${panel.right}) lines up with the card (${card.left}–${card.right})`);
    // Once its glide there is done.
    const off = () =>
      g.execute(() => {
        const [box, el] = [document.querySelector(".pad-halo"), document.activeElement].map((n) => n.getBoundingClientRect());
        return [box.left + box.width / 2 - (el.left + el.width / 2), box.top + box.height / 2 - (el.top + el.height / 2), box.width - el.width];
      });
    await game.until(async () => {
      const [x, y, wider] = await off();
      return Math.abs(x) < 2 && Math.abs(y) < 2 && wider > 0 && wider < 12;
    }).catch(async () => assert.fail(`the focus box is off what has the focus by ${JSON.stringify(await off())}`));
  } finally {
    await game.close();
  }
});
