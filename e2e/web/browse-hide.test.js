/**
 * Hiding apps from Browse with the mouse: a card's menu takes one out, the
 * Hidden apps filter shows it again, and Browse never leaves a blank page
 * behind it. The menu's other items lead to the app's README and feedback.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = {
  settings: {},
  library: [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }],
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000, playTime: 5400 } },
};
let s, page;

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await page.getByRole("button", { name: "Browse", exact: true }).click();
  await card("tampered-port").first().waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

const card = (slug) => page.locator(`article[data-slug="${slug}"]`);
const menu = () => page.locator('[role="menu"]');
const item = (name) => menu().getByRole("menuitem", { name, exact: true });
const inMenu = async (slug, name) => {
  await card(slug).first().click({ button: "right" });
  await item(name).click();
};
const hiddenView = (view) => page.getByLabel("Hidden apps", { exact: true }).selectOption(view);
const filters = () => page.getByRole("button", { name: /^Filters/ }).click();
const empty = () => page.locator(".empty", { hasText: "Nothing matches that search and those filters." });

test("a card's menu hides an app, Only hidden apps shows it, and Show in Browse puts it back", async () => {
  await card("tampered-port").first().click({ button: "right" });
  const items = await menu().getByRole("menuitem").allInnerTexts();
  for (const name of ["Read the README", "Share feedback…", "Hide from Browse"]) assert.ok(items.includes(name), items.join());
  await item("Hide from Browse").click();
  await card("tampered-port").first().waitFor({ state: "detached" });

  await page.locator(".owned-note", { hasText: "1 app here is hidden." }).waitFor();
  await filters();
  await hiddenView("only");
  await card("tampered-port").locator(".cover-badge", { hasText: /hidden/i }).waitFor();
  assert.equal(await card("test-remake").count(), 0, "only the hidden app shows");

  // The last one back: to the rest of Browse, not a view of nothing, and with nothing to choose from any more.
  await inMenu("tampered-port", "Show in Browse");
  await card("test-remake").first().waitFor();
  await card("tampered-port").first().waitFor();
  assert.equal(await page.getByLabel("Hidden apps", { exact: true }).count(), 0);
  assert.equal(await empty().count(), 0);
});

test("a right-click's first item doesn't look picked", async () => {
  await card("tampered-port").first().click({ button: "right" });
  await item("Read the README").waitFor();
  // Not over the items, whose hover would look the same.
  await page.mouse.move(2, 2);
  await page.waitForTimeout(100);
  const focused = await page.evaluate(() => ({ role: document.activeElement.getAttribute("role"), background: getComputedStyle(document.activeElement).backgroundColor }));
  assert.equal(focused.role, "menuitem");
  assert.equal(focused.background, "rgba(0, 0, 0, 0)");
});

test("Only hidden apps says so when none is in the search", async () => {
  await inMenu("tampered-port", "Hide from Browse");
  await filters();
  await hiddenView("only");
  await card("tampered-port").waitFor();
  await page.getByLabel("Search the catalog").fill("Test Remake");
  await empty().waitFor();
  assert.equal(await page.locator("article[data-slug]").count(), 0);
});

test("Only hidden apps doesn't offer to hide the library's apps, which it shows whichever way that is", async () => {
  await page.locator(".owned-note button", { hasText: "Show it" }).first().click();
  await page.locator(".owned-note", { hasText: "Apps in your library are shown here too." }).waitFor();
  await inMenu("tampered-port", "Hide from Browse");
  await filters();
  await hiddenView("only");
  await card("tampered-port").waitFor();
  assert.equal(await page.locator(".owned-note", { hasText: "Apps in your library are shown here too." }).count(), 0);
});

test("the menu reads the README over Browse, and opens the app's page at its feedback", async () => {
  await inMenu("tampered-port", "Read the README");
  const dialog = page.locator('[role="dialog"][aria-label="README"]');
  await dialog.waitFor();
  await page.keyboard.press("Escape");
  await dialog.waitFor({ state: "detached" });
  assert.equal(await card("tampered-port").count() > 0, true, "Browse is still there");

  await inMenu("tampered-port", "Share feedback…");
  await page.getByRole("tab", { name: /Player feedback/, selected: true }).waitFor();
});
