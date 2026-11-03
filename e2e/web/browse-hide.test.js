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

test("a right-click puts the focus on the menu's main action, Get", async () => {
  await card("tampered-port").first().click({ button: "right" });
  await item("Read the README").waitFor();
  assert.equal(await page.evaluate(() => document.activeElement.innerText.trim()), "Get");
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

test("Only hidden apps asks for the hidden apps alone, not for page after page of the catalog", async () => {
  await inMenu("tampered-port", "Hide from Browse");
  await filters();
  const before = s.api.catalogQueries().filter((q) => q.path === "catalog:list").length;
  await hiddenView("only");
  await card("tampered-port").waitFor();
  await page.evaluate(() => new Promise((r) => setTimeout(r, 500)));
  assert.equal(s.api.catalogQueries().filter((q) => q.path === "catalog:list").length, before);
  assert.equal(await page.getByRole("button", { name: "Show more" }).count(), 0);
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

test("the menu and the app's page add it to the library without downloading it", async () => {
  await card("tampered-port").first().click({ button: "right" });
  const items = await menu().getByRole("menuitem").allInnerTexts();
  assert.deepEqual(items.slice(0, 2), ["Get", "Add to library"]);
  await item("Add to library").click();
  await page.getByText("Tampered Port is in your library.").waitFor();
  // Its card stays for now, as Get's does, and isn't offered again.
  await card("tampered-port").first().click({ button: "right" });
  await item("Read the README").waitFor();
  assert.equal(await item("Add to library").count(), 0);
  await page.keyboard.press("Escape");

  await card("test-remake").locator(".card-open").first().click();
  const add = page.locator(".page-layer:not([hidden]) .app-actions").getByRole("button", { name: "Add to library" });
  await add.click();
  await add.waitFor({ state: "detached" });
  await page.locator(".page-layer:not([hidden]) .app-actions").getByRole("button", { name: "Get", exact: true }).waitFor();

  // In the library, ready to get from there, with nothing downloaded.
  await page.locator(".topbar button", { hasText: "Library" }).first().click();
  for (const slug of ["tampered-port", "test-remake"]) await page.locator(`.library-tile[data-slug="${slug}"] .tile-get`).first().waitFor({ state: "attached" });
  assert.equal(await page.locator(".job, .job-error").count(), 0);
});

test("the menu opens the app's page at its whole README, Back returns to the same place in Browse, and it opens the page at its feedback", async () => {
  const main = () => page.evaluate(() => document.querySelector(".shell > main").scrollTop);
  await card("test-remake").first().scrollIntoViewIfNeeded();
  await page.evaluate(() => document.querySelector(".shell > main").scrollBy(0, 60));
  await card("test-remake").first().click({ button: "right" });
  const y = await main();
  await item("Read the README").click();
  const readme = page.locator(".page-layer:not([hidden]) .readme");
  await readme.waitFor();
  await page.waitForFunction(() => document.activeElement?.closest(".readme"));
  assert.equal(await page.locator('[role="dialog"]').count(), 0);
  assert.equal(await readme.locator(".clipped").count(), 0, "not folded");
  const top = await readme.evaluate((el) => el.getBoundingClientRect().top - document.querySelector(".shell > main").getBoundingClientRect().top);
  assert.ok(Math.abs(top) < 40, `the README is at the top (${top}px)`);

  await page.keyboard.press("Escape");
  await readme.waitFor({ state: "detached" });
  assert.equal(await main(), y, "Browse is where it was");

  await inMenu("tampered-port", "Share feedback…");
  await page.getByRole("tab", { name: /Player feedback/, selected: true }).waitFor();
});
