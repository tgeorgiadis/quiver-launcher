/**
 * The player's playlists put in their own order: dragged with the mouse, or moved with Move up and Move down (left and
 * right along the top) from a right-click, the Menu key or a controller's Options button, and saved to the account in
 * one save.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import { installed } from "./controller.js";

after(stop);

const playlists = [
  { key: "a", name: "Alpha", tags: [], consoles: [], apps: ["entry_test-port"], order: 0 },
  { key: "b", name: "Bravo", tags: [], consoles: [], apps: [], order: 1 },
  { key: "c", name: "Charlie", tags: [], consoles: [], apps: [], order: 2 },
];

const panelNames = (p) => p.locator(".playlist-panel .panel-tabs").nth(1).locator(".panel-tab-name").allInnerTexts();
const tabNames = (p) => p.locator("nav.playlists .playlist-tab[data-playlist] > button").allInnerTexts().then((n) => n.map((x) => x.trim()));
const menuItems = (p) => p.getByRole("menu").getByRole("menuitem").allInnerTexts().then((n) => n.map((x) => x.trim()));

/** Drags one row onto another with the mouse, landing in its first or last quarter. */
async function drag(p, from, to, { axis = "y", end = false } = {}) {
  const a = await from.boundingBox();
  const b = await to.boundingBox();
  await p.mouse.move(a.x + a.width / 2, a.y + a.height / 2);
  await p.mouse.down();
  await p.mouse.move(a.x + a.width / 2 + 3, a.y + a.height / 2 + 3);
  const [x, y] = axis === "y" ? [b.x + b.width / 2, end ? b.y + b.height * 0.9 : b.y + b.height * 0.1] : [end ? b.x + b.width * 0.9 : b.x + b.width * 0.1, b.y + b.height / 2];
  await p.mouse.move(x, y, { steps: 8 });
  await p.mouse.up();
}

test("in the side panel: dragged, and moved up and down from its menu, which leaves out the way it can't go", async () => {
  const s = await openApp({ state: { ...installed, collections: playlists, settings: { library: { playlists: "side" } } } });
  try {
    const { page: p } = s;
    const row = (name) => p.locator(".playlist-panel .panel-tab", { hasText: name });
    await row("Alpha").waitFor();
    assert.deepEqual(await panelNames(p), ["Alpha", "Bravo", "Charlie"]);

    // Charlie dragged above Alpha, which stays open: the drop isn't a click on it.
    await drag(p, row("Charlie").locator("button").first(), row("Alpha"));
    assert.deepEqual(await panelNames(p), ["Charlie", "Alpha", "Bravo"]);
    assert.equal(await p.locator(".panel-tab.active .panel-tab-name").innerText(), "All");
    // And Charlie to the foot, past the last one.
    await drag(p, row("Charlie").locator("button").first(), row("Bravo"), { end: true });
    assert.deepEqual(await panelNames(p), ["Alpha", "Bravo", "Charlie"]);
    // Escape while dragging puts it back.
    const a = await row("Alpha").boundingBox();
    await p.mouse.move(a.x + 20, a.y + a.height / 2);
    await p.mouse.down();
    const c = await row("Charlie").boundingBox();
    await p.mouse.move(c.x + 20, c.y + c.height - 2, { steps: 8 });
    assert.equal(await p.locator(".panel-tab.drop-after").count(), 1, "a line where it lands");
    await p.keyboard.press("Escape");
    await p.mouse.up();
    assert.deepEqual(await panelNames(p), ["Alpha", "Bravo", "Charlie"]);

    // The first has no Move up, the last no Move down, the middle both.
    await row("Alpha").click({ button: "right" });
    assert.deepEqual(await menuItems(p), ["Move down", "Show at top of library"]);
    await p.keyboard.press("Escape");
    await row("Charlie").click({ button: "right" });
    assert.deepEqual(await menuItems(p), ["Move up", "Show at top of library"]);
    await p.keyboard.press("Escape");
    await row("Bravo").click({ button: "right" });
    assert.deepEqual(await menuItems(p), ["Move up", "Move down", "Show at top of library"]);
    await p.getByRole("menuitem", { name: "Move up" }).click();
    assert.deepEqual(await panelNames(p), ["Bravo", "Alpha", "Charlie"]);

    // From the keyboard: the Menu key on a focused playlist.
    await row("Bravo").locator("button").first().focus();
    await p.keyboard.press("ContextMenu");
    assert.deepEqual(await menuItems(p), ["Move down", "Show at top of library"]);
    await p.keyboard.press("Escape");

    // A controller's Options button on it (Y on an Xbox pad, North).
    await row("Bravo").locator("button").first().focus();
    await s.pad("North", true);
    await s.pad("North", false);
    await p.getByRole("menuitem", { name: "Move down" }).waitFor();
    await p.getByRole("menuitem", { name: "Move down" }).focus();
    await p.keyboard.press("Enter");
    assert.deepEqual(await panelNames(p), ["Alpha", "Bravo", "Charlie"]);

    // The open playlist's own menu has them too.
    await row("Charlie").locator("button").first().click();
    await row("Charlie").locator(".menu-button").click();
    assert.deepEqual(await menuItems(p), ["Edit", "Share", "Move up", "Delete"]);
    await p.keyboard.press("Escape");
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("along the top: dragged left and right, Move left and Move right, and the order kept on the account in one save", async () => {
  const s = await openApp({ state: { ...installed, collections: playlists } });
  try {
    const { page: p } = s;
    const tab = (name) => p.locator("nav.playlists .playlist-tab", { hasText: name });
    await tab("Alpha").waitFor();
    // Signed in, so the playlists go to the account.
    const username = "playlist-order";
    await p.locator(".account button").click();
    const dialog = p.locator('[role="dialog"]');
    await dialog.locator('form > button[type="button"]').last().click();
    await dialog.locator('input[autocomplete="username"]').fill(username);
    await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
    await dialog.locator("button.primary").click();
    await p.locator(".account-button").waitFor();
    await s.until(() => s.api.collections(username).length === 3);
    const saves = [];
    p.on("request", (r) => r.url().endsWith("/collections/save") && saves.push(JSON.parse(r.postData()).collections.map((c) => c.key)));

    assert.deepEqual(await tabNames(p), ["Alpha", "Bravo", "Charlie"]);
    await drag(p, tab("Alpha").locator("button").first(), tab("Charlie"), { axis: "x", end: true });
    assert.deepEqual(await tabNames(p), ["Bravo", "Charlie", "Alpha"]);
    // All three moved place, in one save.
    const byOrder = () => [...s.api.collections(username)].sort((x, y) => x.order - y.order).map((c) => c.name);
    await s.until(() => byOrder().join() === "Bravo,Charlie,Alpha");
    assert.deepEqual(saves, [["a", "b", "c"]]);

    await tab("Charlie").click({ button: "right" });
    assert.deepEqual(await menuItems(p), ["Move left", "Move right", "Show as side panel"]);
    await p.getByRole("menuitem", { name: "Move right" }).click();
    assert.deepEqual(await tabNames(p), ["Bravo", "Alpha", "Charlie"]);
    // Only the two that changed place.
    await s.until(() => byOrder().join() === "Bravo,Alpha,Charlie");
    assert.deepEqual(saves[1].sort(), ["a", "c"]);
    await tab("Bravo").click({ button: "right" });
    assert.deepEqual(await menuItems(p), ["Move right", "Show as side panel"]);
    await p.keyboard.press("Escape");
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
