/**
 * Everywhere with a controller: on each screen, dialog and menu, the d-pad
 * reaches every control the player can see, from where the focus lands (or
 * the first press puts it), and B leaves it. Screens are opened the same way:
 * the control that opens one is moved to with the d-pad and pressed with A
 * (or Y, Options). Presses go in as the Rust side sends a controller's
 * ("pad" events), so this is spatial.ts as a controller drives it: in the
 * launcher's web side on its own (app.js), Test Port installed and played.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import * as reach from "../reach.js";

const state = {
  // A card for each app, as the library starts: a game's card has a test of its own, at the end.
  settings: {},
  library: [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }],
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000, playTime: 5400 } },
};
let s, app, page;

before(async () => {
  s = await openApp({ state });
  ({ app, page } = s);
});

// Each test from the same place, whatever the one before left: the app as it starts, on the library, no page, dialog
// or menu open, with its saved files as they were (the Grid layout, the usual bindings). The page loaded again starts
// it over (stand-in.js keeps them in the page), so this can't fail on its own and one failed test doesn't fail the rest.
beforeEach(async () => {
  await page.reload();
  await page.locator("article[data-slug]").first().waitFor();
  await reach.install(app);
});

after(async () => {
  await s?.close();
  await stop();
});

/** The topmost dialog, else the page showing, else the library or catalog. */
const where = () =>
  app.execute(() => {
    const dialog = [...document.querySelectorAll('[role="dialog"]')].at(-1);
    if (dialog) return `dialog: ${dialog.getAttribute("aria-label")}`;
    const page = [...document.querySelectorAll(".page-layer")].find((l) => !l.hidden);
    return page ? `page: ${page.querySelector("h1, h2")?.innerText ?? "?"}` : "main";
  });
const pause = (ms) => new Promise((r) => setTimeout(r, ms));
/** Once the screen has changed from `from` (and the focus has been put back), what it is. */
const changed = async (from) => {
  for (let i = 0; i < 30 && (await where()) === from; i++) await pause(100);
  await pause(200);
  return where();
};
/** Moves to the control (as the d-pad reaches it) and presses `key` on it; the screen it leads to. */
const open = async (selector, { key = "South", text } = {}) => {
  const from = await where();
  const pressed = await reach.pressOn(app, selector, key, text ? { text } : undefined);
  assert.doesNotMatch(pressed, /^not reached/, `the d-pad gets to ${selector} ${text ?? ""}`);
  return changed(from);
};
/** B, and the screen it goes back to. */
const back = async () => {
  const from = await where();
  await reach.press(app, "East");
  return changed(from);
};
/** B until the library or catalog is all that shows. */
const home = async () => {
  for (let i = 0; i < 5 && (await where()) !== "main"; i++) await back();
};
// The corner's Back to top button is a pointer's: R3 (and Home) do it.
const BACK_TO_TOP = 'button[aria-label="Back to top"]';
// The sidebar's edge is a pointer's (and Tab's): the d-pad goes past it, and View options set its width.
const SIDEBAR_EDGE = ".sidebar-edge";
// A card's only button (Get, Play), or Get and its arrow: Y on the card presses Get, so the d-pad goes card to card
// past them. The arrow's choices are on the app's page too.
const CARD_MAIN = ".card-action > .primary:only-child, .card-action .get-split > .primary";
/** Asserts the d-pad reaches everything showing (but Back to top, the sidebar's edge, a card's only button and `ignore`), each with all of its focus ring showing. */
const everything = async (name, ignore) => {
  const { missed, reached, cut, outOfSight, unmarked } = await reach.explore(app, { ignore: [BACK_TO_TOP, SIDEBAR_EDGE, CARD_MAIN, ignore].filter(Boolean).join(", ") });
  assert.deepEqual(unmarked, [], `${name}: these look no different with the focus on them`);
  assert.deepEqual(outOfSight, [], `${name}: moves that leave the focus out of sight`);
  assert.deepEqual(missed, [], `${name}: the d-pad doesn't get to these (of ${reached + missed.length})`);
  assert.deepEqual(cut, [], `${name}: these focus rings are cut off`);
};
/** An installed app's page: Y on its library card, then App page, its menu's last item. */
const appPage = async () => {
  await home();
  await open("button", { text: "Library" });
  assert.equal(await open(".library-tile .card-open", { key: "North" }), "main");
  for (let i = 0; i < 4; i++) await reach.press(app, "DPadDown");
  assert.match(await reach.focused(app), /App page/);
  await reach.press(app, "South");
  return changed("main");
};
const section = (name) => open('[aria-label="App sections"] button', { text: name });

test("the catalog, its filters, and Sign in, which B closes", async () => {
  assert.equal(await open("button", { text: "Browse" }), "main");
  await everything("Browse");
  await open("button", { text: "Filters" });
  await everything("Browse with its filters");
  assert.equal(await open("button", { text: "Sign in" }), "dialog: Sign in");
  await everything("Sign in");
  assert.equal(await back(), "main");
  // Along the top bar: Right through each of its controls in turn, Left back, never into the page under it.
  const inBar = () => app.execute(() => [...document.querySelectorAll(".topbar button:not(.brand)")].filter((b) => b.getClientRects().length).map((b) => b.getAttribute("aria-label") || b.innerText.replace(/\s+/g, " ").trim()));
  const bar = await inBar();
  await app.execute(() => document.querySelector(".topbar button:not(.brand)").focus());
  const along = [(await reach.focused(app)).match(/"(.*)"/)[1]];
  for (let i = 1; i < bar.length; i++) await reach.press(app, "DPadRight"), along.push((await reach.focused(app)).match(/"(.*)"/)[1]);
  assert.deepEqual(along, bar, "Right goes along the top bar");
  for (let i = bar.length - 2; i >= 0; i--) {
    await reach.press(app, "DPadLeft");
    assert.equal((await reach.focused(app)).match(/"(.*)"/)[1], bar[i], "Left goes back along it");
  }
  // A card's focus: a ring round its box art, not its button's ring cut off inside the card.
  const look = await app.execute(() => {
    const button = document.querySelector("article[data-slug] .card-open");
    button.focus();
    return { outline: getComputedStyle(button).outlineStyle, art: getComputedStyle(button.closest("article").querySelector(".card-cover")).boxShadow };
  });
  assert.equal(look.outline, "none");
  assert.match(look.art, /0px 0px 0px 1px/);
});

test("A opens a catalog card, and Y gets the app", async () => {
  await open("button", { text: "Browse" });
  // Down from a card skips its Get, which Y on the card presses.
  await app.execute(() => document.querySelector('article[data-slug="tampered-port"] .card-open').focus());
  await reach.press(app, "DPadDown");
  assert.doesNotMatch(await reach.focused(app), /"Get"/);
  assert.equal(await open('article[data-slug="tampered-port"] .card-open'), "page: Tampered Port");
  assert.equal(await back(), "main");
  // It has a download for each system: Y asks which.
  assert.equal(await open('article[data-slug="tampered-port"] .card-open', { key: "North" }), "dialog: Choose a download");
  await everything("Choose a download");
  // A on one starts it (what it installs is the real app's to show: journeys.test.js).
  assert.doesNotMatch(await reach.pressOn(app, '[role="dialog"] button', "South", { text: "linux" }), /^not reached/);
  await s.until(() => app.execute(() => window.__calls.some((c) => c.cmd === "install")));
});

test("the library: a card's own buttons are its menu's, A plays it and Y opens the menu", async () => {
  await everything("the library", ".library-tile .tile-overlay button");
});

/** From the card, Y and the d-pad: the menu's item named `text`, in a submenu of Manage when `manage` says so. */
const toMenuItem = async (a, text, { manage = false } = {}) => {
  await reach.pressOn(a, ".library-tile .card-open", "North");
  const move = (name) => a.execute((name) => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].find((b) => b.innerText.trim() === name)?.focus(), name);
  await move(manage ? "Manage" : text);
  if (manage) {
    await reach.press(a, "DPadRight");
    await move(text);
  }
};
const focusIs = (a) => a.execute(() => document.activeElement?.getAttribute("aria-label") || document.activeElement?.innerText.trim());
const installed = { library: state.library, installs: state.installs };

const focusedSelector = (a) => a.execute(() => { const el = document.activeElement; return el?.matches(".continue-play") ? "play" : el?.matches(".library-bar .search input") ? "search" : el?.matches(".card-open") ? "card" : el?.closest(".topbar") ? "bar" : el === document.body ? "nothing" : el?.className || el?.tagName; });

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

test("into Settings' list from the section beside it, the d-pad lands on the section open", async () => {
  await page.locator(".topbar button[aria-label='Settings']").click();
  await page.locator(".settings-nav").waitFor();
  await page.locator('.settings-nav [data-section="updates"]').click();
  await pause(100);
  await reach.pressOn(app, ".settings-pane button, .settings-pane input, .settings-pane select", "DPadLeft");
  assert.equal(await app.execute(() => document.activeElement?.getAttribute("data-section")), "updates");
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

test("TV mode's top bar fits on one line at a Steam Deck's size, and Game Mode leaves the window's buttons to Steam", async () => {
  for (const gameMode of [true, false]) {
    const s2 = await openApp({ state: installed, width: 1113, height: 696, config: { gameMode } });
    try {
      await s2.page.locator(".topbar").waitFor();
      if (!gameMode) await s2.page.locator(".tv-button").click();
      await s2.page.locator(".shell.tv-mode .tv-leave").waitFor();
      const bar = await s2.app.execute(() => {
        const height = (selector) => document.querySelector(selector).getBoundingClientRect().height;
        const top = document.querySelector(".topbar");
        return { clock: height(".tv-clock"), leave: height(".tv-leave"), fits: top.scrollWidth <= top.clientWidth, controls: Boolean(document.querySelector(".window-controls")) };
      });
      assert.ok(bar.clock < 30, `the clock on one line: ${bar.clock}`);
      assert.ok(bar.leave < 40, `Leave TV mode on one line: ${bar.leave}`);
      assert.equal(bar.fits, true, "nothing past the bar's end");
      assert.equal(bar.controls, !gameMode);
    } finally {
      await s2.close();
    }
  }
});

test("Downloads' See changelog shows what's new over the page, read and closed with a controller, and leaves it there", async () => {
  const s2 = await openApp({ state: { library: state.library, installs: { "entry_test-port": { ...state.installs["entry_test-port"], version: "0.7.0" } } } });
  const { page: p, app: a } = s2;
  const notes = (version) => [`What's in ${version}.`, ...Array.from({ length: 14 }, (_, i) => `Change ${i + 1}, long enough to wrap onto a second line in a dialog this wide, as release notes do.`), `[More on ${version}](https://example.com/${version})`].join("\n\n");
  const release = (version, rest = {}) => ({ releaseId: `rel_${version}`, version, releasedAt: Date.parse(`20${version.slice(3, 4)}${version.slice(1, 2)}-01-01`), notes: notes(version), prerelease: false, state: "verified", reasons: [], assets: [], ...rest });
  const items = [release("v1.1.0", { state: "unverified" }), release("v1.0.0"), release("v0.9.0", { state: "unverified" }), release("v0.8.5", { state: "blocked" }), release("v0.8.0", { notes: "" }), release("v0.7.0"), release("v0.6.0")];
  const history = (route) => route.fulfill({ json: { items, nextCursor: null, isDone: true }, headers: { "access-control-allow-origin": "*" } });
  const seeChangelog = () => p.locator(".download-row button", { hasText: "See changelog" }).click();
  const dialog = p.locator('[role="dialog"].changelog');
  const focusText = () => a.execute(() => document.activeElement?.innerText?.trim());
  try {
    await p.route("**/release-history*", history);
    await p.locator(".downloads-button").click();
    await p.locator(".downloads-page").waitFor();
    await reach.install(a);

    assert.doesNotMatch(await reach.pressOn(a, ".download-row button", "South", { text: "See changelog" }), /^not reached/);
    await dialog.locator("article h3").first().waitFor();
    assert.deepEqual(await dialog.locator("article h3").evaluateAll((all) => all.map((h) => h.firstChild.textContent)), ["v1.0.0", "v0.9.0"], "the versions since the one installed");
    assert.equal(await p.locator(".app-page").count(), 0, "still on Downloads");
    assert.equal(await focusText(), "Close", "starting where A can't update");

    await reach.press(a, "DPadDown");
    await s2.until(() => dialog.evaluate((d) => d.scrollTop > 0));
    assert.equal(await focusText(), "Close", "the d-pad scrolls what has no control on the way");
    const { missed, outOfSight, cut } = await reach.explore(a, {});
    assert.deepEqual({ missed, outOfSight, cut }, { missed: [], outOfSight: [], cut: [] }, "the d-pad gets to the links in the notes and the buttons, each showing");

    await reach.press(a, "East");
    await dialog.waitFor({ state: "detached" });
    await p.locator(".downloads-page").waitFor();
    await s2.until(async () => (await focusText())?.includes("See changelog"));

    await seeChangelog();
    await dialog.waitFor();
    await p.keyboard.press("Escape");
    await dialog.waitFor({ state: "detached" });
    assert.equal(await p.locator(".downloads-page").isVisible(), true, "Escape closes it and not the page under it");

    await seeChangelog();
    await p.locator(".overlay").click({ position: { x: 5, y: 5 } });
    await dialog.waitFor({ state: "detached" });

    await seeChangelog();
    await dialog.locator(".changelog-other").click();
    await dialog.waitFor({ state: "detached" });
    await p.locator(".app-page .release-section").waitFor();
    await p.locator(".app-page .back-link").click();
    await p.locator(".downloads-page").waitFor();

    await p.unroute("**/release-history*");
    await p.route("**/release-history*", (route) => route.abort());
    await seeChangelog();
    await dialog.getByText("Couldn't load the release notes.").waitFor();
    await dialog.locator("button", { hasText: "Update to v1.0.0" }).click();
    await dialog.waitFor({ state: "detached" });
  } finally {
    await s2.close();
  }
});

test("the launcher's What's new shows the release's notes in the launcher, with a way on to its page", async () => {
  const launcherUpdate = { version: "4.0.0-beta.2", notes: ["## Better", "The release's own words.", ...Array.from({ length: 40 }, (_, i) => `Change ${i + 1}.`)].join("\n\n"), url: "https://example.com/release", preview: true };
  const s2 = await openApp({ state: { ...installed, launcherUpdate } });
  const { page: p, app: a } = s2;
  const dialog = p.locator('[role="dialog"].changelog');
  const opened = () => a.execute(() => window.__opened);
  try {
    await p.locator(".launcher-update").waitFor();
    await reach.install(a);
    assert.doesNotMatch(await reach.pressOn(a, ".launcher-update button", "South", { text: "What's new" }), /^not reached/);
    await dialog.waitFor();
    assert.equal(await dialog.locator("h2").first().innerText(), "What's new in Quiver Launcher");
    await dialog.getByText("The release's own words.").waitFor();
    assert.deepEqual(await opened(), [], "nothing opens outside the launcher");
    assert.equal(await dialog.evaluate((d) => d.scrollTop), 0, "open at the notes' start");
    await reach.press(a, "DPadLeft");
    assert.equal(await a.execute(() => document.activeElement?.innerText?.trim()), "View v4.0.0-beta.2 on GitHub");
    assert.equal(await dialog.evaluate((d) => d.scrollTop), 0, "moving between the buttons doesn't scroll the notes");

    await dialog.locator("button", { hasText: "View v4.0.0-beta.2 on GitHub" }).click();
    assert.deepEqual(await opened(), [launcherUpdate.url]);

    await reach.press(a, "East");
    await dialog.waitFor({ state: "detached" });
    await p.locator(".launcher-update button", { hasText: "What's new" }).click();
    await dialog.locator("button", { hasText: "Update now" }).click();
    await dialog.waitFor({ state: "detached" });
    await s2.until(() => a.execute(() => window.__calls.some((c) => c.cmd === "install_update")));
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

test("the player's picture shows behind the library's grid and nowhere else, and Settings chooses it", async () => {
  const picture = "/home/player/Pictures/wall.png";
  const s2 = await openApp({ state: { ...installed, pickBackground: picture } });
  const { page: p, app: a } = s2;
  const behind = p.locator(".library-background img");
  const settingsOpen = async (name) => {
    await p.locator(".topbar button[aria-label='Settings']").click();
    await p.locator(".settings-nav button", { hasText: name }).first().click();
  };
  try {
    await p.locator(".library-grid").waitFor();
    assert.equal(await behind.count(), 0, "none until the player chooses one");

    await settingsOpen("Library");
    const setting = p.locator('section[aria-label="Library background"]');
    await setting.waitFor();
    await reach.install(a);
    assert.doesNotMatch(await reach.pressOn(a, 'section[aria-label="Library background"] button', "South", { text: "Choose a picture…" }), /^not reached/);
    await setting.getByText("wall.png").waitFor();
    const preview = setting.locator(".background-preview img");
    await preview.waitFor();
    await setting.locator('input[type="range"]').fill("60");
    await s2.until(() => preview.evaluate((i) => i.style.opacity === "0.6"));

    await s2.page.locator(".topbar nav button", { hasText: "Library" }).click();
    await behind.waitFor({ state: "attached" });
    assert.equal(await behind.evaluate((i) => i.style.opacity), "0.6");
    const placed = await behind.evaluate((i) => {
      const bar = document.querySelector(".topbar").getBoundingClientRect();
      const card = document.querySelector(".library-tile").getBoundingClientRect();
      const box = i.getBoundingClientRect();
      return { wide: box.width === innerWidth, tall: box.height === innerHeight, shown: i.naturalWidth > 0, overBar: document.elementFromPoint(bar.x + bar.width / 2, bar.y + bar.height / 2) === i, overCard: document.elementFromPoint(card.x + card.width / 2, card.y + card.height / 2) === i };
    });
    assert.deepEqual(placed, { wide: true, tall: true, shown: true, overBar: false, overCard: false });
    const scroller = p.locator(".shell > main");
    await scroller.evaluate((m) => m.scrollTo(0, 200));
    assert.equal(await behind.evaluate((i) => i.getBoundingClientRect().y), 0, "stays put as the cards scroll");

    await p.locator(".topbar nav button", { hasText: "Browse" }).click();
    await behind.waitFor({ state: "detached" });
    await p.locator(".topbar nav button", { hasText: "Library" }).click();
    await behind.waitFor({ state: "attached" });
    await p.locator(".library-tile").first().click();
    await p.locator(".app-page").first().waitFor();
    assert.equal(await behind.isVisible(), false, "not behind an app's page");
    await reach.press(a, "East");
    await behind.waitFor();

    await settingsOpen("Library");
    await setting.locator("button", { hasText: "Remove" }).click();
    await setting.locator("button", { hasText: "Choose a picture…" }).waitFor();
    await p.locator(".topbar nav button", { hasText: "Library" }).click();
    await p.locator(".library-grid").waitFor();
    assert.equal(await behind.count(), 0);
  } finally {
    await s2.close();
  }

  // The Sidebar layout keeps its own background, with the picture still chosen for the grid.
  const s3 = await openApp({ state: { ...installed, settings: { libraryBackground: picture, library: { layout: "sidebar" } } } });
  try {
    await s3.page.locator(".library-split").waitFor();
    assert.equal(await s3.page.locator(".library-background").count(), 0);
  } finally {
    await s3.close();
  }

  // A file moved or deleted since: Settings says so, and the library goes on without it.
  const s4 = await openApp({ state: { ...installed, settings: { libraryBackground: "/home/player/Pictures/missing.png" } } });
  try {
    await s4.page.locator(".library-grid").waitFor();
    assert.equal(await s4.page.locator(".library-background").count(), 0);
    await s4.page.locator(".topbar button[aria-label='Settings']").click();
    await s4.page.locator(".settings-nav button", { hasText: "Library" }).first().click();
    await s4.page.getByText("Quiver couldn't show that picture.", { exact: false }).waitFor();
  } finally {
    await s4.close();
  }
});

test("Settings' list stays on the screen as the page scrolls, in a window with just room for it", async () => {
  const s2 = await openApp({ state: installed, height: 345 });
  try {
    await s2.page.locator(".topbar").waitFor();
    await s2.page.locator(".topbar button[aria-label='Settings']").click();
    await s2.page.locator(".settings-nav").waitFor();
    for (const section of ["general", "updates"]) {
      await s2.page.locator(`.settings-nav [data-section="${section}"]`).click();
      await pause(100);
      const over = await s2.app.execute(() => {
        const main = document.querySelector(".shell > main");
        const list = document.querySelector(".settings-nav");
        main.scrollTop = main.scrollHeight;
        return [list.getBoundingClientRect().top - main.getBoundingClientRect().top, main.clientHeight - list.offsetHeight];
      });
      assert.ok(over[1] >= 0, "the window has room for all of it");
      assert.ok(over[0] >= 0, `${section}: ${over[0]}px from the top`);
    }
  } finally {
    await s2.close();
  }
});

test("Installed and Not installed are only filters: not tabs that can disagree with them", async () => {
  const tabs = await app.execute(() => [...document.querySelectorAll(".playlists button, .library-show option")].map((b) => b.innerText || b.label));
  assert.ok(tabs.some((name) => name.trim() === "All"));
  assert.ok(!tabs.some((name) => /installed/i.test(name)), tabs.join());
});

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

test("an app's page and its dialogs", async () => {
  assert.equal(await appPage(), "page: Test Port");
  await everything("the app's page");
  await section("Media");
  await everything("its Media");
  // Its setup, Mods among it, on its own tab.
  await section("On this computer");
  await everything("its On this computer tab");
  assert.equal(await open("button", { text: "Mods" }), "dialog: Mods for Test Port");
  await everything("Mods");
  assert.equal(await back(), "page: Test Port");
  await section("Overview");
  assert.equal(await open("button", { text: "Uninstall" }), "dialog: Uninstall Test Port");
  assert.match(await reach.focused(app), /Cancel/, "Uninstall opens on Cancel");
  await everything("Uninstall");
  assert.equal(await back(), "page: Test Port");
  assert.equal(await open("button", { text: "Test Port (original)" }), "page: Test Port (original)");
  await everything("the game's page");
  assert.equal(await back(), "page: Test Port");
  assert.equal(await back(), "main");
});

test("Downloads, and every section of Settings", async () => {
  assert.equal(await open('button[aria-label="Downloads"]'), "page: Downloads");
  await everything("Downloads");
  assert.equal(await back(), "main");
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  const sections = await app.execute(() => [...document.querySelectorAll('nav[aria-label="Settings sections"] button')].map((b) => b.innerText.trim()));
  for (const name of sections) {
    await open('nav[aria-label="Settings sections"] button', { text: name });
    await everything(`Settings › ${name}`);
  }
});

test("a controller rebinds a control with its own buttons", async () => {
  const binding = 'button[aria-label="Confirm / Select: controller"]';
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  await open('nav[aria-label="Settings sections"] button', { text: "Controllers" });
  await open(binding);
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "Press a button…");
  await reach.press(app, "West");
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "X");
  // X is Confirm now: it rebinds it back to A.
  await app.execute((b) => document.querySelector(b).focus(), binding);
  await reach.press(app, "West");
  await reach.press(app, "South");
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "A");
  // Swap A and B, with the controller: A swaps them, and then it's B that confirms, so B swaps them back.
  const cells = () => app.execute(() => ["Confirm / Select", "Cancel / Back"].map((a) => document.querySelector(`button[aria-label="${a}: controller"]`).innerText));
  await app.execute(() => [...document.querySelectorAll(".controls label.toggle")].find((l) => l.innerText.startsWith("Swap ")).querySelector("input").focus());
  await reach.press(app, "South");
  await s.until(async () => (await cells()).join() === "B,A");
  await reach.press(app, "East");
  await s.until(async () => (await cells()).join() === "A,B");
});

test("Up goes through a long page to its top before the top bar", async () => {
  await appPage();
  await section("Player feedback");
  await everything("its Player feedback");
  // From the bottom, Up alone.
  for (let i = 0; i < 30; i++) await reach.press(app, "DPadDown");
  const ups = [];
  for (let i = 0; i < 40; i++) {
    await reach.press(app, "DPadUp");
    ups.push(await reach.focused(app));
    if (await app.execute(() => Boolean(document.activeElement?.closest(".topbar")))) break;
  }
  assert.ok(ups.includes('button "Library" in Test Port'), `up gets to the page's Library link: ${ups.join(", ")}`);
  assert.ok(await app.execute(() => Boolean(document.activeElement?.closest(".topbar"))), "and then the top bar");
  await s.until(async () => (await app.execute(() => document.querySelector(".shell > main").scrollTop)) === 0);
  await section("Releases");
  await everything("its Releases");
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

test("Add an app, in the library, which B closes, and B on a dialog puts the focus back on what opened it", async () => {
  await home();
  await open("button", { text: "Library" });
  assert.equal(await open("button", { text: "Add an app" }), "dialog: Add an app");
  await everything("Add an app");
  assert.equal(await back(), "main");
  for (const [opener, dialog] of [["Add an app", "dialog: Add an app"], ["Sign in", "dialog: Sign in"]]) {
    assert.equal(await open("button", { text: opener }), dialog);
    await reach.press(app, "DPadDown");
    assert.equal(await back(), "main");
    assert.match(await reach.focused(app), new RegExp(`"${opener}"`));
  }
});

test("a page opened with A has the focus in it, and a button that changes as it's pressed keeps it near", async () => {
  assert.equal(await appPage(), "page: Test Port");
  assert.equal(await reach.focused(app), 'button "Library" in Test Port');
  // The app's setup is on its On this computer tab.
  await page.locator("[aria-label=\"App sections\"] button", { hasText: "On this computer" }).click();
  await open("button", { text: "Change version" });
  assert.doesNotMatch(await reach.focused(app), /^body/);
});

test("a long README: the d-pad reads through it, the right stick and Page Down scroll it, and A on what's out of sight shows it", async () => {
  await home();
  await open("button", { text: "Browse" });
  assert.equal(await open('article[data-slug="test-remake"] .card-open'), "page: Test Remake");
  const y = () => app.execute(() => document.querySelector(".shell > main").scrollTop);
  const inSight = () => app.execute(() => window.__reach.inSight(document.activeElement));
  const view = await app.execute(() => document.documentElement.clientHeight);
  // Down a press at a time: a step at most, through the text rather than past it, to the link at its end.
  let steps = 0;
  for (let i = 0; i < 40 && !(await reach.focused(app)).includes("the notes"); i++) {
    // At the fold, Read more: A opens the rest, and the d-pad reads on from there.
    if ((await reach.focused(app)).startsWith('button "Read more"')) await reach.press(app, "South");
    const before = await y();
    await reach.press(app, "DPadDown");
    const by = (await y()) - before;
    assert.ok(by < view * 0.6, `a step, not a jump (${by}px of ${view})`);
    if (by > 0 && !(await reach.focused(app)).includes("the notes")) steps++;
  }
  assert.equal(await reach.focused(app), 'a "the notes" in Test Remake');
  assert.ok(await inSight());
  assert.ok(steps >= 2, `it stepped through the text (${steps} steps)`);
  // The right stick held: a smooth scroll for as long as it is, which stops when it's let go.
  const hold = (key, down) => app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
  const bottom = await y();
  await hold("RightStickUp", true);
  await pause(500);
  await hold("RightStickUp", false);
  // Where it stopped, once the page has heard it let go (events come after the call that sends them, as Tauri's do).
  let glided = await y();
  await s.until(async () => glided === (glided = await y()));
  assert.ok(glided < bottom - 100, `it scrolled up (${bottom} to ${glided})`);
  await pause(300);
  assert.equal(await y(), glided);
  // A on the link, now out of sight: back into view, not opened unseen.
  assert.equal(await inSight(), false);
  await reach.press(app, "South");
  await s.until(inSight);
  assert.equal(await where(), "page: Test Remake");
  // Page Down: most of a screen.
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, 0));
  await page.keyboard.press("PageDown");
  await s.until(async () => (await y()) > view * 0.5);
});

test("with a controller connected, the bottom says what its buttons do, as it labels them, and Settings hides it", async () => {
  // As gamepad.rs says a controller has come (and which), so it runs the same with none plugged in.
  const send = (event, payload) => app.execute((event, payload) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event, payload }), event, payload);
  const xbox = { kind: "xbox", south: "A", east: "B", west: "X", north: "Y" };
  const hints = () => app.execute(() => [...document.querySelectorAll(".pad-hints > span")].map((s) => s.innerText.replace(/\s+/g, " ").trim()));
  await send("pads", ["Test pad"]);
  await send("pad-labels", xbox);
  await home();
  await open("button", { text: "Library" });
  await s.until(async () => (await hints()).length > 0);
  // On a library card: A plays it, Y opens its menu.
  await app.execute(() => document.querySelector(".library-tile .card-open").focus());
  await s.until(async () => (await hints()).join(", ").startsWith("A Play, Y Menu"));
  // It doesn't cover the page: the d-pad still gets everywhere, every move in sight above it.
  await everything("the library, with the hints");
  // In the Sidebar layout: only beside the sidebar, which runs to the window's bottom.
  const layout = async (name) => {
    await open('button[aria-label="View options"]');
    await app.execute((name) => [...document.querySelectorAll("button")].find((b) => b.innerText.trim() === name).focus(), name);
    await reach.press(app, "South");
    await reach.press(app, "East");
  };
  await layout("Sidebar");
  const at = () =>
    app.execute(() => {
      const side = document.querySelector(".library-sidebar").getBoundingClientRect();
      const top = document.querySelector(".topbar").getBoundingClientRect().height;
      return { bar: document.querySelector(".pad-hints").getBoundingClientRect().left, side: side.right, tall: side.height, room: document.documentElement.clientHeight - top };
    });
  await s.until(async () => {
    const { bar, side } = await at();
    return Math.abs(bar - side) < 2;
  });
  // As tall as the window under the top bar: not cut short for the hints.
  const { tall, room } = await at();
  assert.ok(Math.abs(tall - room) < 2, `the sidebar runs to the bottom (${tall} of ${room})`);
  // Its foot's strip and the hints beside it: their tops one line, once the page has scrolled the sidebar up under the top bar.
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, document.querySelector(".shell > main").scrollHeight));
  const tops = () => app.execute(() => [".library-sidebar .sidebar-foot", ".pad-hints"].map((s) => document.querySelector(s).getBoundingClientRect().top));
  await s.until(async () => {
    const [foot, hints] = await tops();
    return Math.abs(foot - hints) < 1.5;
  }, 5000).catch(async () => assert.fail(`the foot's top and the hints' top: ${(await tops()).join(" and ")}`));
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, 0));
  await everything("the sidebar layout, with the hints");
  await layout("Grid");
  // On a page: B goes back.
  assert.equal(await appPage(), "page: Test Port");
  await s.until(async () => (await hints()).includes("B Back"));
  // A PlayStation pad's labels.
  await send("pad-labels", { kind: "playstation", south: "Cross", east: "Circle", west: "Square", north: "Triangle" });
  await s.until(async () => (await hints()).some((h) => h.startsWith("✕ ")) && (await hints()).includes("○ Back"));
  // Settings › Controllers: its switch, with the controller, hides it and shows it again.
  await home();
  await open('button[aria-label="Settings"]');
  await open('nav[aria-label="Settings sections"] button', { text: "Controllers" });
  const showing = () => app.execute(() => Boolean(document.querySelector(".pad-hints")));
  await app.execute(() => [...document.querySelectorAll(".controls label.toggle")].find((l) => l.innerText.includes("Show what the buttons do")).querySelector("input").focus());
  await reach.press(app, "South");
  await s.until(async () => !(await showing()));
  await reach.press(app, "South");
  await s.until(showing);
  // And none connected, none shown.
  await send("pads", []);
  await s.until(async () => !(await app.execute(() => Boolean(document.querySelector(".pad-hints")))));
});

test("after a mouse click, and back in front of other windows, a controller's focus has its ring", async () => {
  await home();
  const ringed = () => app.execute(() => document.activeElement?.matches(":focus-visible") ?? false);
  // A click with the mouse, then the d-pad: the browser alone would leave the ring off.
  await page.getByRole("button", { name: "Browse", exact: true }).click();
  await reach.press(app, "DPadDown");
  assert.ok(await ringed(), "the d-pad's focus shows after a click");
  // Back in front of other windows, as the controller left it: the ring the browser dropped comes back. A script's
  // focus after a ringed one keeps the ring in WebView2 and WebKitGTK alike, so this may not drop it; either way it shows after.
  await app.execute(() => {
    const at = document.activeElement;
    at.blur();
    at.focus();
  });
  const dropped = !(await ringed());
  await app.execute(() => window.dispatchEvent(new FocusEvent("focus")));
  assert.ok(await ringed(), dropped ? "the window's focus brings it back" : "the window's focus keeps it");
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

test("A on a dropdown on a page opens its options, and B closes them without going back a page", async () => {
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  await open('nav[aria-label="Settings sections"] button', { text: "General" });
  assert.doesNotMatch(await reach.pressOn(app, ".settings-pane select", "South"), /^not reached/);
  assert.ok((await page.locator(".select-picker").count()) > 0, "A opens the list");
  await reach.press(app, "East");
  assert.ok(!(await page.locator(".select-picker").count()), "B closes the list");
  assert.equal(await where(), "page: Settings", "and the page stays");
  assert.match(await reach.focused(app), /^select /);
});

test("a game's card: Y lists its ports, B and Get there put the focus back on the card, Cancel keeps it on the port's line, and A plays the port played or got last", async () => {
  // Test Port's game, with Test Remake too: one card for both, as View options' One card for each game has them.
  const game = await openApp({
    state: { ...state, settings: { library: { byGame: true } }, library: [...state.library, { id: "entry_test-remake", slug: "test-remake", addedAt: Date.now() - 86400_000 }] },
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
      // The mock's files are marked for no computer: which one.
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

test("outside TV mode, the shoulder buttons go between Library and Browse, as they do between TV mode's tabs", async () => {
  const showing = () => app.execute(() => document.querySelector('.topbar [data-tabs] [aria-current="page"]')?.innerText.trim() ?? "");
  assert.match(await showing(), /^Library/);
  await reach.press(app, "RightTrigger");
  await s.until(async () => /^Browse/.test(await showing()));
  await reach.press(app, "LeftTrigger");
  await s.until(async () => /^Library/.test(await showing()));
});

test("moving by a controller, nothing on a card eases, and a shelf glides along rather than jumping a card at a time", async () => {
  // WebKitGTK gives what eases a layer of its own while it does, and the cards it overlaps too, which on a Steam Deck
  // showed black for a frame, or another card's art.
  const easing = () =>
    app.execute(() => {
      // Without reach.js's sheet that stills everything for the other tests, which would hide what this looks for.
      const still = [...document.querySelectorAll("style")].find((s) => s.textContent.includes("transition: none !important"));
      if (still) still.disabled = true;
      const found = [];
      for (const el of document.querySelectorAll(".entry-card, .entry-card *")) {
        const style = getComputedStyle(el);
        const moving = style.transitionDuration.split(",").some((d) => parseFloat(d) > 0) || style.animationName !== "none";
        if (moving) found.push(`${el.className || el.tagName} in ${el.closest(".entry-card")?.getAttribute("data-slug")}`);
      }
      if (still) still.disabled = false;
      return [...new Set(found)];
    });
  await reach.press(app, "DPadRight");
  assert.deepEqual(await easing(), [], "the library's cards");

  await reach.press(app, "RightTrigger");
  await page.locator(".shelf-row article .card-open").first().waitFor();
  assert.doesNotMatch(await reach.pressOn(app, ".shelf-row article .card-open", "DPadRight"), /^not reached/);
  assert.deepEqual(await easing(), [], "Browse's cards");

  // A shelf of the mock catalog's (short ones) made a card a row, its first card focused, then Right: the row eases
  // on to the next, frame by frame.
  const steps = await app.execute(async () => {
    const el = [...document.querySelectorAll(".shelf-row")].find((r) => r.querySelectorAll(".card-open").length > 1);
    // A card the row wide: what Shelf fits it to again, as its height changes.
    el.style.setProperty("--shelf-card", `${el.clientWidth}px`);
    el.style.setProperty("--shelf-fit", `${el.clientWidth - 16}px`);
    await new Promise(requestAnimationFrame);
    el.querySelector(".card-open").focus();
    await new Promise(requestAnimationFrame);
    // With the motion reach.js turns off for the other tests, so the scroll eases.
    const still = window.matchMedia;
    window.matchMedia = (query) => (/prefers-reduced-motion/.test(query) ? { matches: false, media: query } : still(query));
    const seen = [el.scrollLeft];
    await window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "DPadRight", down: true } });
    await window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "DPadRight", down: false } });
    for (let i = 0; i < 40; i++) {
      await new Promise(requestAnimationFrame);
      seen.push(el.scrollLeft);
    }
    window.matchMedia = still;
    return { seen, snap: getComputedStyle(el).scrollSnapType };
  });
  const { seen } = steps;
  const [from, to] = [seen[0], seen.at(-1)];
  assert.ok(to > from, `the row scrolled on (${seen.join(", ")})`);
  assert.ok(new Set(seen.filter((x) => x > from && x < to)).size >= 2, `on its way, not all at once (${seen.join(", ")})`);
  assert.equal(steps.snap, "none", "and doesn't snap under it");
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

test("apps installed here but not in the library: each card is one stop, A opens its page, Y adds it, and none has Uninstall", async () => {
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
    // Y: Add, into the library, the card going from the section to it.
    const slug = await l.execute(() => document.activeElement.closest("article").getAttribute("data-slug"));
    await reach.press(l, "North");
    await loose.until(() => l.execute((s) => !document.querySelector(`.loose article[data-slug="${s}"]`) && Boolean(document.querySelector(`article[data-slug="${s}"]:not(.loose article)`)), slug));
  } finally {
    await loose.close();
  }
});

test("the first start's setup: each step all reached by the d-pad, the focus on what goes on, A going on and B back, never out", async () => {
  const first = await openApp({ state: { settings: { setupDone: false } } });
  try {
    const { app: setup } = first;
    await reach.install(setup);
    const step = () => setup.execute(() => document.querySelector('[role="dialog"][aria-label="Set up Quiver Launcher"] h2')?.innerText ?? null);
    const goesTo = async (title) => {
      for (let i = 0; i < 30 && (await step()) !== title; i++) await pause(100);
      assert.equal(await step(), title);
    };
    await goesTo("Welcome to Quiver Launcher!");
    assert.match(await reach.focused(setup), /"Next"/, "A goes on from where the focus lands");
    // B on the first step stays: Skip setup is the way out.
    await reach.press(setup, "East");
    await pause(200);
    assert.equal(await step(), "Welcome to Quiver Launcher!");
    // Usage data is a step only in a build that sends it (a PostHog token), which CI's web job isn't.
    const total = Number(await setup.execute(() => document.querySelector(".setup-count")?.innerText.match(/(\d+)\s*$/)?.[1]));
    const titles = ["Welcome to Quiver Launcher!", "Coming from Quiver Launcher 3?", "Where apps install", "Usage data", "Sign in to sync your library"].filter((t) => total === 5 || t !== "Usage data");
    assert.equal(titles.length, total);
    for (const [i, title] of titles.entries()) {
      await goesTo(title);
      await everything(`setup: ${title}`);
      if (i === 1) {
        // B goes back a step, and A on Next comes back to this one.
        await reach.press(setup, "East");
        await goesTo(titles[0]);
        await reach.pressOn(setup, ".setup-next", "South");
        await goesTo(title);
      }
      if (i < titles.length - 1) assert.doesNotMatch(await reach.pressOn(setup, ".setup-next", "South"), /^not reached/);
    }
    assert.doesNotMatch(await reach.pressOn(setup, ".setup-next", "South"), /^not reached/);
    for (let i = 0; i < 30 && (await step()) !== null; i++) await pause(100);
    assert.equal(await step(), null, "Start using Quiver closes it");
  } finally {
    await first.close();
  }
});
