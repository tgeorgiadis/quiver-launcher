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

test("online, an app's page shows the site's README and keeps it; offline, the copy kept, with its pictures", async () => {
  const library = [{ id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 }];
  const installs = { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [] } };
  const online = await openApp({ state: { settings: { cache: { mode: "installed" } }, library, installs } });
  try {
    const { page } = online;
    await page.locator('article[data-slug="test-port"] .card-open').first().click();
    await page.locator(".readme .markdown").first().waitFor();
    await online.until(() => page.evaluate(() => (window.__cache.saved ?? []).length > 0));
    const [saved] = await page.evaluate(() => window.__cache.saved);
    assert.equal(saved.app, "entry_test-port");
    assert.ok(saved.readme.markdown.length > 0);
    assert.equal(await page.locator(".readme-kept").count(), 0, "online, the site's own, never the copy");
  } finally {
    await online.close();
  }
  const offline = await openApp({ state: { settings: { cache: { mode: "installed" } }, library, installs } });
  try {
    const { page } = offline;
    const picture = "https://raw.example/test-port/main/shot.png";
    const data = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    await page.evaluate(([picture, data]) => {
      window.__cache.readmes = { "entry_test-port": { markdown: "# Kept copy\n\n![shot](shot.png)", rawBase: "https://raw.example/test-port/main/", keptAt: Date.UTC(2026, 10, 20), images: { [picture]: data } } };
    }, [picture, data]);
    await page.locator('article[data-slug="test-port"]').first().waitFor();
    await page.route(`${offline.api.api}/**`, (route) => route.abort("internetdisconnected"));
    await page.locator('article[data-slug="test-port"] .card-open').first().click();
    await page.locator(".readme-kept").waitFor();
    assert.match(await page.locator(".readme-kept").textContent(), /can't be reached/);
    assert.match(await page.locator(".readme .markdown").first().textContent(), /Kept copy/);
    assert.equal(await page.locator(".readme .markdown img").first().getAttribute("src"), data);
  } finally {
    await offline.close();
  }
});
