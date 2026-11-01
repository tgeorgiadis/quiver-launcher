/**
 * A library card's menu: behind its ⋯ button, a right-click, the Menu key, a
 * long press and a controller's Options button, with what the app's page has:
 * its main action first, then "Play with" its programs, "Add to" its playlists and "Manage" beside it.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, card, until, tap;
// A home folder with a desktop, so shortcuts land somewhere we can look.
const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
const opened = join(home, "opened.log");

before(async () => {
  mkdirSync(join(home, "Desktop"));
  s = await launch({ env: { HOME: home, QUIVER_OPENED: opened } });
  ({ app, api, apps, card, until, tap } = s);
});

after(() => s?.close());

const menu = () => app.$('[role="menu"][aria-label="Test Port menu"]');
const items = () => app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].map((b) => b.textContent));
const pick = async (text) => (await (await menu()).$(`button=${text}`)).click();
/** Picks an item in the Manage submenu, opening it first. */
const manage = async (text) => {
  await pick("Manage");
  await pick(text);
};
/** A library card's ⋯ button, which shows with the card's other buttons while the pointer is on it. */
const menuButton = async () => {
  const port = await card("test-port");
  await (await port.$(".card-open")).moveTo();
  return port.$("button[aria-label='Test Port menu']");
};
const library = async () => {
  await (await app.$("button=Browse")).click();
  await (await app.$("button*=Library")).click();
};

test("the ⋯ button on a library card has the app's actions", async () => {
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  await (await app.$("button*=Library")).click();
  await (await menuButton()).click();
  await (await menu()).waitForDisplayed();
  assert.deepEqual(await items(), ["Play", "Play with", "Add to", "Manage", "App page"]);
  await pick("Manage");
  assert.deepEqual(await items(), ["Play", "Play with", "Add to", "Manage", "Open folder", "Rename folder…", "Desktop shortcut", "Add to Steam", "Change version…", "Hide from library", "Uninstall", "Uninstall and remove", "App page"]);
  await pick("Open folder");
  await until(() => existsSync(opened) && readFileSync(opened, "utf8").includes(join(apps, "test-port")));
  await (await menu()).waitForDisplayed({ reverse: true });
});

test("the app's page puts it under the consoles picked, and back under the catalog's; the menu leaves them be", async () => {
  await (await app.$("main > div:not(.page-layer) select[aria-label='Group by']")).selectByVisibleText("Group by console");
  // Grouped, every section starts folded away.
  await (await app.$("main > div:not(.page-layer) button[aria-label='Expand all sections']")).click();
  const sectionOf = () => app.execute(() => document.querySelector("article[data-slug='test-port']")?.closest(".console-section")?.getAttribute("aria-label"));
  await until(async () => (await sectionOf()) === "Nintendo 64");
  // Its consoles are on its page's On this computer tab.
  const openConsoles = async () => {
    await (await (await card("test-port")).$(".card-open")).click();
    await (await (await app.$(".app-page")).$("button*=On this computer")).click();
  };
  await openConsoles();
  const consoles = await app.$(".app-page section[aria-label=Consoles]");
  await (await consoles.$("select[aria-label='Add a console']")).selectByVisibleText("Super Nintendo Entertainment System");
  await (await consoles.$("button[aria-label='Remove console Nintendo 64']")).click();
  await (await consoles.$("button[aria-label='Remove console Super Nintendo Entertainment System']")).waitForDisplayed();
  await app.keys("Escape");
  await until(async () => (await sectionOf()) === "Super Nintendo Entertainment System");
  await openConsoles();
  await (await consoles.$("button=Use the catalog's consoles")).click();
  await (await consoles.$("button[aria-label='Remove console Nintendo 64']")).waitForDisplayed();
  await app.keys("Escape");
  await until(async () => (await sectionOf()) === "Nintendo 64");
  await (await app.$("main > div:not(.page-layer) select[aria-label='Group by']")).selectByVisibleText("No groups");
});

test("a right-click opens it where the pointer is, ready for the keyboard", async () => {
  await (await (await card("test-port")).$(".card-open")).click({ button: "right" });
  await (await menu()).waitForDisplayed();
  assert.equal(await app.execute(() => document.activeElement?.textContent), "Play");
  assert.ok(!(await (await app.$(".app-page")).isExisting()), "the app's page didn't open");
  await manage("Desktop shortcut");
  await until(() => existsSync(join(home, "Desktop", "Test Port.desktop")));
  await (await app.$('.banner[role="status"]')).waitForDisplayed();
  await (await (await app.$('.banner[role="status"]')).$("button=OK")).click();
});

test("a right-click elsewhere shows no browser menu, but a text field keeps its own", async () => {
  // Whether the browser would show its menu: nothing cancelled the event.
  const browserMenu = (tag) =>
    app.execute((tag) => {
      const el = document.body.appendChild(document.createElement(tag));
      const shown = el.dispatchEvent(new MouseEvent("contextmenu", { bubbles: true, cancelable: true, button: 2 }));
      el.remove();
      return shown;
    }, tag);
  assert.equal(await browserMenu("div"), false);
  assert.equal(await browserMenu("input"), true);
  assert.equal(await browserMenu("textarea"), true);
});

test("Shift+F10 and a controller's Options button open it on the focused card", async () => {
  const focusCard = () => app.execute(() => document.querySelector('article[data-slug="test-port"] .card-open').focus());
  await focusCard();
  await app.keys(["Shift", "F10"]);
  await (await menu()).waitForDisplayed();
  // Escape closes only the menu, and focus goes back to the card.
  await app.keys("Escape");
  await (await menu()).waitForDisplayed({ reverse: true });
  assert.ok(await app.execute(() => document.activeElement?.matches('article[data-slug="test-port"] .card-open')));
  await focusCard();
  await tap("North");
  await (await menu()).waitForDisplayed();
  // The controller moves through the menu and presses with A; right goes into a submenu, and B comes back out of it.
  const press = tap;
  const focused = (text) => until(async () => (await app.execute(() => document.activeElement?.textContent)) === text);
  await press("DPadDown");
  await focused("Play with");
  await press("DPadDown");
  await focused("Add to");
  await press("DPadDown");
  await focused("Manage");
  await press("DPadRight");
  await focused("Open folder");
  await press("East");
  await focused("Manage");
  assert.ok(await (await menu()).isDisplayed(), "B closed only the submenu");
  await press("South");
  await focused("Open folder");
  await press("DPadLeft");
  await focused("Manage");
  await press("DPadDown");
  await focused("App page");
  await press("South");
  await (await (await app.$(".app-page")).$("h1=Test Port")).waitForDisplayed();
  await app.keys("Escape");
  await (await card("test-port")).waitForDisplayed();
});

test("a long press on a touch screen opens it, and the tap doesn't open the app", async () => {
  await app.execute(async () => {
    const open = document.querySelector('article[data-slug="test-port"] .card-open');
    const r = open.getBoundingClientRect();
    const at = { clientX: r.left + 20, clientY: r.top + 20, pointerType: "touch", bubbles: true, isPrimary: true };
    open.dispatchEvent(new PointerEvent("pointerdown", at));
    await new Promise((done) => setTimeout(done, 700));
    open.dispatchEvent(new PointerEvent("pointerup", at));
    open.dispatchEvent(new MouseEvent("mousedown", { ...at, bubbles: true }));
    open.click();
  });
  await (await menu()).waitForDisplayed();
  assert.ok(!(await (await app.$(".app-page")).isDisplayed()), "the app's page didn't open");
  // A short tap is still a tap.
  await app.keys("Escape");
  await app.execute(async () => {
    const open = document.querySelector('article[data-slug="test-port"] .card-open');
    open.dispatchEvent(new PointerEvent("pointerdown", { pointerType: "touch", bubbles: true }));
    open.dispatchEvent(new PointerEvent("pointerup", { pointerType: "touch", bubbles: true }));
    open.click();
  });
  await (await (await app.$(".app-page")).$("h1=Test Port")).waitForDisplayed();
  assert.ok(!(await (await menu()).isExisting()));
  await app.keys("Escape");
});

test("Add to puts the app on the player's playlists, and a new one starts with it", async () => {
  await (await menuButton()).click();
  await pick("Add to");
  // Lowercase: WebKit's driver types capitals without Shift here, so "Favourites" would be saved as "favourites".
  await (await (await menu()).$("input[aria-label='New playlist']")).setValue("favourites");
  await app.keys("Enter");
  const favourites = async () => (await menu()).$("button=favourites");
  await until(async () => (await (await favourites()).getAttribute("aria-checked")) === "true");
  await (await favourites()).click();
  await until(async () => (await (await favourites()).getAttribute("aria-checked")) === "false");
  await (await favourites()).click();
  await until(async () => (await (await favourites()).getAttribute("aria-checked")) === "true");
  // Escape closes the submenu, then the menu.
  await app.keys("Escape");
  await app.keys("Escape");
  await (await menu()).waitForDisplayed({ reverse: true });
  await s.showTab("favourites");
  await (await card("test-port")).waitForDisplayed();
  await s.showTab("All");
});

test("hiding from the menu moves the app to Hidden, and showing it brings it back", async () => {
  await (await menuButton()).click();
  await manage("Hide from library");
  await until(async () => !(await (await card("test-port")).isExisting()));
  await s.showTab("Hidden (1)");
  await (await menuButton()).click();
  await manage("Show in library");
  await s.showTab("All");
  await (await card("test-port")).waitForDisplayed();
});

test("an update is in the menu when there is one", async () => {
  api.release("1.1.0");
  await library();
  await (await menuButton()).click();
  await pick("Update to v1.1.0");
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "1.1.0", 30000);
  api.release("1.0.0");
});

test("Uninstall deletes the files and keeps the app in the library to get again", async () => {
  await (await menuButton()).click();
  await manage("Uninstall");
  await (await (await app.$("[role=dialog]")).$("button=Uninstall")).click();
  await until(() => !existsSync(join(apps, "test-port")));
  const port = await card("test-port");
  await until(async () => (await (await port.$(".card-status")).getProperty("textContent")) === "Not installed");
  await (await menuButton()).click();
  assert.deepEqual(await items(), ["Get", "Add to", "Manage", "App page"]);
  await pick("Manage");
  assert.deepEqual(await items(), ["Get", "Add to", "Manage", "Choose a version…", "Hide from library", "Remove from library", "App page"]);
  await pick("Get");
  await (await port.$(".card-open")).moveTo();
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
});

test("Uninstall and remove takes it out of the library", async () => {
  await (await menuButton()).click();
  await manage("Uninstall and remove");
  await (await (await app.$("[role=dialog]")).$("button=Uninstall and remove")).click();
  await until(() => !existsSync(join(apps, "test-port")));
  await until(async () => !(await (await card("test-port")).isExisting()));
});
