/**
 * What the tests with a controller share. Everywhere with a controller: on each screen, dialog and menu, the d-pad
 * reaches every control the player can see, from where the focus lands (or the first press puts it), and B leaves it.
 * Screens are opened the same way: the control that opens one is moved to with the d-pad and pressed with A (or Y,
 * Options, or X, Quick action). Presses go in as the Rust side sends a controller's ("pad" events), so this is
 * spatial.ts as a controller drives it: in the launcher's web side on its own (app.js), Test Port installed and played.
 *
 * Each test file runs in a process of its own, so each has its own `s`, `app` and `page` here.
 */
import { after, before, beforeEach } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import * as reach from "../reach.js";

export const state = {
  // A card for each app, as the library starts: a game's card has a test of its own, in library-menus.test.js.
  settings: {},
  library: [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }],
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000, playTime: 5400 } },
};
export const installed = { library: state.library, installs: state.installs };
export let s, app, page;

/** For a test file: one app for its tests to share, as `s`, `app` and `page`. */
export function useSharedApp() {
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
}

/** The topmost dialog, else the page showing, else the library or catalog. */
export const where = () =>
  app.execute(() => {
    const dialog = [...document.querySelectorAll('[role="dialog"]')].at(-1);
    if (dialog) return `dialog: ${dialog.getAttribute("aria-label")}`;
    const page = [...document.querySelectorAll(".page-layer")].find((l) => !l.hidden);
    return page ? `page: ${page.querySelector("h1, h2")?.innerText ?? "?"}` : "main";
  });
export const pause = (ms) => new Promise((r) => setTimeout(r, ms));
/** Once the screen has changed from `from` (and the focus has been put back), what it is. */
export const changed = async (from) => {
  for (let i = 0; i < 30 && (await where()) === from; i++) await pause(100);
  await pause(200);
  return where();
};
/** Moves to the control (as the d-pad reaches it) and presses `key` on it; the screen it leads to. */
export const open = async (selector, { key = "South", text } = {}) => {
  const from = await where();
  const pressed = await reach.pressOn(app, selector, key, text ? { text } : undefined);
  assert.doesNotMatch(pressed, /^not reached/, `the d-pad gets to ${selector} ${text ?? ""}`);
  return changed(from);
};
/** B, and the screen it goes back to. */
export const back = async () => {
  const from = await where();
  await reach.press(app, "East");
  return changed(from);
};
/** B until the library or catalog is all that shows. */
export const home = async () => {
  for (let i = 0; i < 5 && (await where()) !== "main"; i++) await back();
};
// The corner's Back to top button is a pointer's: R3 (and Home) do it.
const BACK_TO_TOP = 'button[aria-label="Back to top"]';
// The sidebar's edge is a pointer's (and Tab's): the d-pad goes past it, and View options set its width.
const SIDEBAR_EDGE = ".sidebar-edge";
// A card's only button (Get, Play), or Get and its arrow: X on the card presses Get, so the d-pad goes card to card
// past them. The arrow's choices are on the app's page too. A Browse card's menu (card-menu.tsx) is beside them, with
// no button of its own.
const CARD_MAIN = ".card-action > .primary:only-of-type, .card-action .get-split > .primary";
/** Asserts the d-pad reaches everything showing (but Back to top, the sidebar's edge, a card's only button and `ignore`), each with all of its focus ring showing. */
export const everything = async (name, ignore) => {
  const { missed, reached, cut, outOfSight, unmarked } = await reach.explore(app, { ignore: [BACK_TO_TOP, SIDEBAR_EDGE, CARD_MAIN, ignore].filter(Boolean).join(", ") });
  assert.deepEqual(unmarked, [], `${name}: these look no different with the focus on them`);
  assert.deepEqual(outOfSight, [], `${name}: moves that leave the focus out of sight`);
  assert.deepEqual(missed, [], `${name}: the d-pad doesn't get to these (of ${reached + missed.length})`);
  assert.deepEqual(cut, [], `${name}: these focus rings are cut off`);
};
/** An installed app's page: Y on its library card, then App page, its menu's last item. */
export const appPage = async () => {
  await home();
  await open("button", { text: "Library" });
  assert.equal(await open(".library-tile .card-open", { key: "North" }), "main");
  for (let i = 0; i < 5; i++) await reach.press(app, "DPadDown");
  assert.match(await reach.focused(app), /App page/);
  await reach.press(app, "South");
  return changed("main");
};
export const section = (name) => open('[aria-label="App sections"] button', { text: name });

/** From the card, Y and the d-pad: the menu's item named `text`, in a submenu of Manage when `manage` says so. */
export const toMenuItem = async (a, text, { manage = false } = {}) => {
  await reach.pressOn(a, ".library-tile .card-open", "North");
  const move = (name) => a.execute((name) => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].find((b) => b.innerText.trim() === name)?.focus(), name);
  await move(manage ? "Manage" : text);
  if (manage) {
    await reach.press(a, "DPadRight");
    await move(text);
  }
};
export const focusIs = (a) => a.execute(() => document.activeElement?.getAttribute("aria-label") || document.activeElement?.innerText.trim());

export const focusedSelector = (a) => a.execute(() => { const el = document.activeElement; return el?.matches(".continue-play") ? "play" : el?.matches(".library-bar .search input") ? "search" : el?.matches(".card-open") ? "card" : el?.closest(".topbar") ? "bar" : el === document.body ? "nothing" : el?.className || el?.tagName; });
