/**
 * A player's own name for an app in their library: from its card's menu (Manage, Rename…) and from its name on its
 * page, without a visit to its Manage tab; and the menu's way straight to that tab (All options…).
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = {
  settings: {},
  library: [{ id: "entry_test-port", slug: "test-port", addedAt: 1 }],
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [] } },
};
let s, page;

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await page.locator("article[data-slug]").first().waitFor();
  s.errors.length = 0;
});

after(async () => {
  await s?.close();
  await stop();
});

const card = () => page.locator("article[data-slug='test-port']");
/** The card's menu, opened with a right-click, at `text` under Manage. */
const manage = async (text) => {
  await card().click({ button: "right" });
  await page.getByRole("menuitem", { name: "Manage", exact: true }).click();
  await page.getByRole("menu", { name: "Manage" }).getByRole("menuitem", { name: text, exact: true }).click();
};
const dialog = (name) => page.getByRole("dialog", { name });

test("Rename…, under Manage in a card's menu, gives the app the player's name without opening its page", async () => {
  await manage("Rename…");
  const field = dialog("Rename Test Port").getByRole("textbox", { name: "Name" });
  assert.equal(await field.inputValue(), "Test Port", "its name now fills the field");
  await field.fill("  My favourite port ");
  await dialog("Rename Test Port").getByRole("button", { name: "Save" }).click();
  await dialog("Rename Test Port").waitFor({ state: "detached" });
  await card().getByRole("button", { name: "My favourite port", exact: true }).waitFor();
  assert.equal(await page.locator(".app-page").count(), 0, "the library is still what shows");
  // The catalog's name comes back from the same prompt.
  await manage("Rename…");
  await dialog("Rename My favourite port").getByRole("button", { name: "Use the catalog's name" }).click();
  await card().getByRole("button", { name: "Test Port", exact: true }).waitFor();
  assert.deepEqual(s.errors, []);
});

test("an app's name on its page renames it: the pencil beside it, or a double click", async () => {
  await card().getByRole("button", { name: "Test Port", exact: true }).click();
  const title = page.locator(".app-page .app-title");
  await title.getByRole("button", { name: "Rename Test Port" }).click();
  await dialog("Rename Test Port").getByRole("textbox", { name: "Name" }).fill("Port of mine");
  await page.keyboard.press("Enter");
  await title.locator("h1", { hasText: "Port of mine" }).waitFor();
  await title.locator("h1").dblclick();
  const field = dialog("Rename Port of mine").getByRole("textbox", { name: "Name" });
  // Emptied, it's the catalog's again.
  await field.fill("");
  await page.keyboard.press("Enter");
  await title.locator("h1", { hasText: "Test Port" }).waitFor();
  assert.equal(await dialog("Rename Port of mine").getByRole("button", { name: "Use the catalog's name" }).count(), 0);
  assert.deepEqual(s.errors, []);
});

test("All options…, last under Manage, opens the app's page at its Manage tab", async () => {
  await manage("All options…");
  await page.locator(".app-page .computer-panel").waitFor();
  assert.equal(await page.getByRole("tab", { name: "Manage" }).getAttribute("aria-selected"), "true");
  assert.deepEqual(s.errors, []);
});
