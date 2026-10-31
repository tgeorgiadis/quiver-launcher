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
// A card's only button (Get, Play): Y on the card presses it, so the d-pad goes card to card past it.
const CARD_MAIN = ".card-action > .primary:only-child";
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
  const inBar = () => app.execute(() => [...document.querySelectorAll(".topbar button")].filter((b) => b.getClientRects().length).map((b) => b.getAttribute("aria-label") || b.innerText.replace(/\s+/g, " ").trim()));
  const bar = await inBar();
  await app.execute(() => document.querySelector(".topbar button").focus());
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
    state: { ...state, settings: { scale: 1.15, library: { byGame: true } }, library: [...state.library, { id: "entry_test-remake", slug: "test-remake", addedAt: Date.now() - 86400_000 }] },
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
