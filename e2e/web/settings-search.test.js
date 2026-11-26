/**
 * Searching Settings (settings-search.ts): what's typed shows every section's matching settings at once, and hides the
 * rest; with the keys, a controller and a narrow window.
 */
import { test, after } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import * as reach from "../reach.js";

after(stop);

const pause = (ms) => new Promise((r) => setTimeout(r, ms));
/** The sections showing in the results, each with the settings shown in it (their names). */
const shown = (page) =>
  page.locator(".settings-results").evaluate((root) =>
    Object.fromEntries(
      [...root.querySelectorAll(".settings-search-group")]
        .filter((g) => g.checkVisibility())
        .map((g) => [g.dataset.section, [...g.querySelectorAll("label.toggle, .ql-field-label, h3")].filter((e) => e.checkVisibility()).map((e) => e.textContent.trim())]),
    ),
  );
const nav = (page) => page.locator(".settings-nav button").allInnerTexts();

async function settings(options) {
  const s = await openApp(options);
  await s.page.locator(".topbar button[aria-label='Settings']").click();
  await s.page.locator(".settings-search input").waitFor();
  return s;
}

test("typing shows the settings that match from every section, and the list only their sections", async () => {
  const s = await settings();
  const { page } = s;
  try {
    const field = page.locator(".settings-search input");
    await field.fill("theme");
    await pause(100);
    assert.deepEqual(await shown(page), { general: ["Theme"] });
    assert.deepEqual(await nav(page), ["General"]);

    // A menu's choices, extra words and words run together find their setting; a section's name finds all of it.
    await field.fill("dark mode");
    await pause(100);
    assert.deepEqual(await shown(page), { general: ["Theme"] });
    await field.fill("fullscreen");
    await pause(100);
    assert.deepEqual(await shown(page), { general: ["Full screen"] });
    await field.fill("controller swap");
    await pause(100);
    assert.deepEqual(await shown(page), { controllers: ["Swap A and B: Confirm on B, Back on A"] });
    await field.fill("token");
    await pause(100);
    assert.deepEqual(await shown(page), { advanced: ["GitHub token", "GitLab token", "Other repository tokens"] });

    await field.fill("xyzzy");
    await page.getByText("No settings match “xyzzy”.").waitFor();
    assert.deepEqual(await nav(page), []);

    // Cleared, the section open before is back, and every section in the list.
    await page.locator(".settings-search .search-clear").click();
    await page.locator('.settings-pane[aria-label="General"]').waitFor();
    assert.equal((await nav(page)).length >= 8, true);
    assert.equal(await page.locator("[data-search-hidden]").count(), 0);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("Ctrl+F goes to the search, Escape empties it, and the next Escape leaves Settings", async () => {
  const s = await settings();
  const { page } = s;
  try {
    await page.locator('.settings-nav [data-section="updates"]').click();
    await page.locator(".settings-pane button").first().focus();
    await page.keyboard.press("Control+f");
    await s.until(() => page.evaluate(() => document.activeElement?.closest(".settings-search") !== null));
    await page.keyboard.type("cache");
    await pause(100);
    assert.deepEqual(Object.keys(await shown(page)), ["cache"]);
    await page.keyboard.press("Escape");
    await page.locator('.settings-pane[aria-label="Updates"]').waitFor();
    assert.equal(await page.locator(".settings-search input").inputValue(), "");
    assert.equal(await page.locator(".settings-page").count(), 1, "still in Settings");
    await page.keyboard.press("Escape");
    await page.locator(".settings-page").waitFor({ state: "detached" });
  } finally {
    await s.close();
  }
});

test("with a controller, Down from the search goes into what it found, and B empties the search before leaving", async () => {
  const s = await settings();
  const { page, app } = s;
  const focus = () => app.execute(() => document.activeElement?.closest(".settings-results label, .settings-results .ql-field, .settings-nav button")?.textContent.trim() ?? document.activeElement?.tagName);
  try {
    await reach.install(app);
    // Nothing typed: the list's first section.
    await page.locator(".settings-search input").focus();
    await reach.press(app, "DPadDown");
    await pause(100);
    assert.equal(await focus(), "General");

    await page.locator(".settings-search input").fill("update");
    await pause(100);
    assert.deepEqual(await nav(page), ["Library", "Updates"]);
    await reach.press(app, "DPadDown");
    await pause(100);
    assert.equal(await focus(), "Show installed and update icons");
    await reach.press(app, "East");
    await pause(200);
    assert.equal(await page.locator(".settings-search input").inputValue(), "");
    assert.equal(await app.execute(() => document.activeElement?.closest(".settings-search") !== null), true, "back on the search");
    assert.equal(await page.locator(".settings-page").count(), 1, "still in Settings");
    await reach.press(app, "East");
    await page.locator(".settings-page").waitFor({ state: "detached" });
  } finally {
    await s.close();
  }
});

test("in a narrow window, the search is over the list, and the results take the list's place", async () => {
  const s = await settings({ width: 560, height: 800 });
  const { page } = s;
  try {
    await page.locator(".settings-nav").waitFor();
    await page.locator(".settings-search input").fill("language");
    await pause(100);
    assert.equal(await page.locator(".settings-nav").count(), 0);
    assert.deepEqual(await shown(page), { general: ["Language"] });
    await page.locator(".settings-search input").fill("");
    await page.locator(".settings-nav").waitFor();
  } finally {
    await s.close();
  }
});
