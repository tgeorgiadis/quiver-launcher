/**
 * The library's Playlists filter (issue 506): the apps on none of the player's playlists, still to sort, or the ones on
 * one, picked for it or matching its filters. On All, Updates and Hidden, not on a playlist's own tab; not saved with a
 * playlist.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = (settings = {}) => ({
  settings,
  library: [
    { id: "entry_test-port", slug: "test-port", addedAt: 3 },
    { id: "entry_tampered-port", slug: "tampered-port", addedAt: 2, overrides: { tags: ["Retro"] } },
    { id: "entry_test-remake", slug: "test-remake", addedAt: 1 },
  ],
  installs: {
    "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [] },
    "entry_tampered-port": { version: "1.0.0", folder: "tampered-port", executables: [] },
  },
  collections: [
    // One picked by hand, one by its filters.
    { key: "pl1", name: "Weekend", tags: [], consoles: [], apps: ["entry_test-port"], order: 0 },
    { key: "pl2", name: "Retro", tags: ["Retro"], consoles: [], order: 1 },
  ],
});

after(() => stop());

for (const [layout, settings, row] of [
  ["Grid", {}, "article"],
  ["List", { library: { layout: "sidebar" } }, ".sidebar-row"],
]) {
  const slugs = (page) => page.$$eval(`${row}[data-slug]`, (els) => els.map((el) => el.dataset.slug).sort());
  const app = (page, slug) => page.locator(`${row}[data-slug='${slug}']`);
  test(`${layout}: Not in a playlist shows the apps on none, In a playlist the rest, and only off a playlist's tab`, async () => {
    const s = await openApp({ state: state(settings) });
    const { page } = s;
    try {
      const filter = page.getByRole("combobox", { name: "Playlists" });
      await app(page, "test-remake").waitFor();
      await page.getByRole("button", { name: "Filters" }).click();
      await filter.selectOption("none");
      await app(page, "test-port").waitFor({ state: "detached" });
      assert.deepEqual(await slugs(page), ["test-remake"]);
      // A playlist of what's on none would be on one: not offered.
      assert.equal(await page.locator(".save-playlist:not([disabled])").count(), 0);
      await filter.selectOption("some");
      await app(page, "test-port").waitFor();
      assert.deepEqual(await slugs(page), ["tampered-port", "test-port"]);
      // A playlist's own tab hasn't the choice, and shows its apps whatever it was.
      if (layout === "Grid") await page.getByRole("button", { name: "Weekend", exact: true }).first().click();
      else await page.locator(".library-sidebar select").first().selectOption({ label: "Weekend" });
      await app(page, "tampered-port").waitFor({ state: "detached" });
      assert.equal(await filter.count(), 0);
      assert.deepEqual(await slugs(page), ["test-port"]);
      assert.deepEqual(s.errors, []);
    } finally {
      await s.close();
    }
  });
}

test("without playlists of the player's own there's no choice to make", async () => {
  const s = await openApp({ state: { ...state(), collections: [] } });
  const { page } = s;
  try {
    await page.locator("article[data-slug='test-remake']").waitFor();
    await page.getByRole("button", { name: "Filters" }).click();
    await page.getByRole("combobox", { name: "Installed" }).waitFor();
    assert.equal(await page.getByRole("combobox", { name: "Playlists" }).count(), 0);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
