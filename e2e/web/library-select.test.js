/**
 * Selecting many library apps and changing them together (library-ticking.tsx): Ctrl+click and Shift+click, Select
 * in an app's menu, the bar's playlists, tags, hiding and uninstalling, the List layout, and a controller.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = () => ({
  settings: {},
  library: [
    { id: "entry_test-port", slug: "test-port", addedAt: 3, overrides: { tags: ["Favourites"] } },
    { id: "entry_tampered-port", slug: "tampered-port", addedAt: 2 },
    { id: "entry_test-remake", slug: "test-remake", addedAt: 1 },
  ],
  installs: {
    "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [] },
    "entry_tampered-port": { version: "1.0.0", folder: "tampered-port", executables: [] },
  },
  collections: [{ key: "pl1", name: "Weekend", tags: [], consoles: [], apps: ["entry_test-port"], order: 0 }],
});

after(() => stop());

const card = (page, slug) => page.locator(`article[data-slug='${slug}']`);
const ticked = (page) => page.$$eval("[data-tick][data-ticked]", (els) => els.map((el) => el.dataset.slug ?? el.dataset.tick));
const bar = (page) => page.getByRole("toolbar", { name: "Selected apps" });

async function open(options = {}, ready = "article[data-slug='test-port']") {
  const s = await openApp({ state: state(), ...options });
  await s.page.locator(ready).waitFor();
  return s;
}

test("Ctrl+click selects, Shift+click selects a range, and a click then selects instead of opening", async () => {
  const s = await open();
  const { page } = s;
  try {
    // The order the library shows them: from the first to the last card, once all three are there (the catalog's come
    // in after the first, and a range taken from fewer would miss them).
    await s.until(async () => (await page.locator("article[data-tick]").count()) === state().library.length);
    const slugs = await page.$$eval("article[data-tick]", (els) => els.map((el) => el.dataset.slug));
    await card(page, slugs[0]).locator(".card-open").click({ modifiers: ["Control"] });
    await bar(page).waitFor();
    assert.match(await bar(page).innerText(), /1 selected/);
    await card(page, slugs.at(-1)).locator(".card-open").click({ modifiers: ["Shift"] });
    await s.until(async () => (await ticked(page)).length === slugs.length);
    assert.deepEqual((await ticked(page)).sort(), [...slugs].sort());
    // A plain click now unselects, and no app page opens.
    await card(page, slugs.at(-1)).locator(".card-open").click();
    await s.until(async () => (await ticked(page)).length === slugs.length - 1);
    assert.equal(await page.locator(".page-layer:not([hidden])").count(), 0, "no app's page opened");
    await page.keyboard.press("Escape");
    await bar(page).waitFor({ state: "detached" });
    assert.deepEqual(await ticked(page), []);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("Select in an app's menu starts selecting, and the bar puts them all on a playlist and a tag, then hides them", async () => {
  const s = await open();
  const { page } = s;
  try {
    await card(page, "tampered-port").locator(".card-open").click({ button: "right" });
    await page.getByRole("menuitem", { name: "Select", exact: true }).click();
    await bar(page).waitFor();
    await card(page, "test-port").locator(".card-open").click();
    assert.match(await bar(page).innerText(), /2 selected/);

    // One of the two is on Weekend: half ticked; a click puts both on it.
    await bar(page).getByRole("button", { name: "Add to playlist" }).click();
    const weekend = page.getByRole("menuitemcheckbox", { name: "Weekend" });
    assert.equal(await weekend.getAttribute("aria-checked"), "mixed");
    await weekend.click();
    assert.equal(await weekend.getAttribute("aria-checked"), "true");
    await page.keyboard.press("Escape");

    await bar(page).getByRole("button", { name: "Tags" }).click();
    const favourites = page.getByRole("menuitemcheckbox", { name: "Favourites" });
    assert.equal(await favourites.getAttribute("aria-checked"), "mixed");
    await page.getByRole("textbox", { name: "Add a tag" }).fill("Co-op");
    await page.keyboard.press("Enter");
    assert.equal(await page.getByRole("menuitemcheckbox", { name: "Co-op" }).getAttribute("aria-checked"), "true");
    await page.keyboard.press("Escape");
    assert.ok(await bar(page).isVisible(), "Escape closed the menu, not the selection");

    await bar(page).getByRole("button", { name: "Hide from library" }).click();
    await bar(page).waitFor({ state: "detached" });
    await card(page, "test-port").waitFor({ state: "detached" });
    await card(page, "tampered-port").waitFor({ state: "detached" });
    assert.equal(await card(page, "test-remake").count(), 1);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("Undo on the toast after hiding shows them again, still selected", async () => {
  const s = await open();
  const { page } = s;
  try {
    await card(page, "test-port").locator(".card-open").click({ modifiers: ["Control"] });
    await card(page, "tampered-port").locator(".card-open").click();
    await bar(page).getByRole("button", { name: "Hide from library" }).click();
    await card(page, "test-port").waitFor({ state: "detached" });
    const toast = page.locator(".toast");
    assert.match(await toast.innerText(), /2 apps hidden from your library/);
    await toast.getByRole("button", { name: "Undo" }).click();
    await card(page, "test-port").waitFor();
    await bar(page).waitFor();
    assert.deepEqual((await ticked(page)).sort(), ["tampered-port", "test-port"]);
    assert.equal(await page.locator(".toast").count(), 0);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("hidden apps' tags can be filtered on, and the filter shows hidden apps on a playlist too", async () => {
  const hiddenPort = { ...state(), settings: { hidden: ["entry_test-port"] } };
  const s = await open({ state: hiddenPort }, "article[data-slug='tampered-port']");
  const { page } = s;
  try {
    assert.equal(await card(page, "test-port").count(), 0);
    await page.getByRole("button", { name: "Weekend" }).click();
    await page.getByRole("button", { name: "Filters" }).click();
    // The hidden app's own tag is one to pick.
    const tags = await page.getByRole("combobox", { name: "Tag" }).locator("option").allInnerTexts();
    assert.ok(tags.includes("Favourites"), tags.join(", "));
    // Weekend's one app is hidden: with the filter it shows, marked.
    await page.getByRole("combobox", { name: "Hidden apps" }).selectOption("show");
    await card(page, "test-port").waitFor();
    assert.equal(await card(page, "test-port").locator(".hidden-mark").count(), 1);
    assert.equal(await card(page, "tampered-port").count(), 0, "still only the playlist's");
    await page.getByRole("combobox", { name: "Hidden apps" }).selectOption("only");
    assert.equal(await card(page, "test-port").count(), 1);
    // Saving these as a playlist isn't offered by the hidden apps' choice alone.
    assert.equal(await page.locator("#library-filters .save-playlist").count(), 0);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("Uninstall asks once for every app selected, naming them", async () => {
  const s = await open();
  const { page } = s;
  try {
    await page.keyboard.down("Control");
    await card(page, "test-port").locator(".card-open").click();
    await card(page, "tampered-port").locator(".card-open").click();
    await card(page, "test-remake").locator(".card-open").click();
    await page.keyboard.up("Control");
    await bar(page).getByRole("button", { name: "More" }).click();
    await page.getByRole("menuitem", { name: "Uninstall 2 apps…" }).click();
    const dialog = page.getByRole("dialog", { name: "Uninstall apps" });
    await dialog.waitFor();
    assert.match(await dialog.innerText(), /Uninstall 2 apps\?/);
    assert.match(await dialog.innerText(), /Test Port/);
    assert.match(await dialog.innerText(), /Tampered Port/);
    assert.doesNotMatch(await dialog.innerText(), /Test Remake/, "not the one that isn't installed");
    await dialog.getByRole("button", { name: "Cancel" }).click();
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("in the List layout, rows select the same way, and a controller's A selects while B stops", async () => {
  const s = await open({ state: { ...state(), settings: { library: { layout: "sidebar" } } } }, ".sidebar-row[data-slug='test-port']");
  const { page } = s;
  try {
    const row = (slug) => page.locator(`.sidebar-row[data-slug='${slug}']`);
    await row("test-port").waitFor();
    await row("test-port").click({ modifiers: ["Control"] });
    await row("test-remake").click();
    assert.deepEqual((await ticked(page)).sort(), ["test-port", "test-remake"]);
    await page.keyboard.press("Escape");
    await bar(page).waitFor({ state: "detached" });

    // A controller: Select from the menu (Y), then A on another row, B to stop.
    await s.emit("pads", ["Xbox Controller"]);
    await row("tampered-port").focus();
    await s.pad("North", true);
    await s.pad("North", false);
    await page.getByRole("menuitem", { name: "Select", exact: true }).waitFor();
    await page.getByRole("menuitem", { name: "Select", exact: true }).focus();
    await s.pad("South", true);
    await s.pad("South", false);
    await bar(page).waitFor();
    await row("test-port").focus();
    await s.pad("South", true);
    await s.pad("South", false);
    await s.until(async () => (await ticked(page)).length === 2);
    await s.pad("East", true);
    await s.pad("East", false);
    await bar(page).waitFor({ state: "detached" });
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
