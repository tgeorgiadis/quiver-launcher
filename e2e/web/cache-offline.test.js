/**
 * The download cache while quiverlauncher.com can't be reached: an app in the library installs from the file the cache
 * keeps of it, checked against its checksum, with nothing to download.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";

test("offline, an app installs from the download cache's file of it", async () => {
  const hash = "c".repeat(64);
  const s = await openApp({
    state: {
      settings: { cache: { mode: "installed" } },
      library: [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }],
    },
  });
  try {
    const { page } = s;
    await page.evaluate((hash) => {
      window.__cache.files = [
        { hash, filename: "test-port-linux.zip", path: "test-port/1.0.0/test-port-linux.zip", size: 10, apps: [{ app: "entry_test-port", version: "1.0.0", os: "linux", arch: "x64", slug: "test-port" }], cachedAt: 1, usedAt: 1 },
      ];
    }, hash);
    await page.locator('article[data-slug="test-port"]').first().waitFor();
    // Offline from here: nothing from the catalog answers.
    await page.route(`${s.api.api}/**`, (route) => route.abort("internetdisconnected"));
    await page.locator('article[data-slug="test-port"] .card-open').first().click();
    await page.getByRole("button", { name: /^Install/ }).first().click();
    await s.until(() => page.evaluate(() => (window.__installRequests ?? []).length > 0));
    const [request] = await page.evaluate(() => window.__installRequests);
    assert.equal(request.id, "entry_test-port");
    assert.equal(request.version, "1.0.0");
    assert.equal(request.filename, "test-port-linux.zip");
    assert.equal(request.checksum, `sha256:${hash}`);
    // Nothing to download: the cache has it, and it's checked against that checksum there.
    assert.equal(request.url, "");
    assert.equal(request.cache.dir, "/home/player/.local/share/quiver/cache");
    assert.ok(!(await page.getByText("Couldn't reach quiverlauncher.com").count()), "no message that the site can't be reached");
  } finally {
    await s.close();
  }
});

test("offline with nothing in the cache, it says the site can't be reached, as ever", async () => {
  const s = await openApp({
    state: {
      settings: { cache: { mode: "installed" } },
      library: [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }],
    },
  });
  try {
    const { page } = s;
    await page.locator('article[data-slug="test-port"]').first().waitFor();
    await page.route(`${s.api.api}/**`, (route) => route.abort("internetdisconnected"));
    await page.locator('article[data-slug="test-port"] .card-open').first().click();
    await page.getByRole("button", { name: /^Install/ }).first().click();
    await page.getByText("Couldn't reach quiverlauncher.com").first().waitFor();
    assert.equal(await page.evaluate(() => (window.__installRequests ?? []).length), 0);
  } finally {
    await s.close();
  }
});
