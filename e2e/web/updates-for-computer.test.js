/**
 * An app's newest verified release may only have another system's build: what a player is offered, and what installs,
 * is the newest with one for their computer. As Wario Land 4's, whose Linux release came just after its Windows one.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const ID = "entry_test-port";
const when = (y, m) => Date.UTC(y, m, 1);
const release = (version, releasedAt, ...files) => ({
  id: `release_${version}`,
  version,
  releasedAt,
  prerelease: false,
  assets: files.map(([filename, os]) => ({ id: filename, url: `https://example.com/${filename}`, filename, os, architecture: "x64", format: "zip", checksum: `sha256:${"a".repeat(64)}` })),
});
// The mock's verified release is 1.0.0, of January 2026.
const LINUX = release("1.0.0", when(2026, 0), ["Port-1.0.0.AppImage", "linux"]);
const WINDOWS = release("0.5.0", when(2025, 11), ["Port-0.5.0-Windows.zip", "windows"]);
const OLDER = release("0.4.0", when(2025, 10), ["Port-0.4.0-Windows.zip", "windows"]);
const onWindows = { os: "windows", arch: "x64" };
const onLinux = { os: "linux", arch: "x64" };
const UPDATE = ".tile-update, .tile-icon-update";
const installed = (version, more = {}) => ({ [ID]: { version, releasedAt: version === "0.5.0" ? WINDOWS.releasedAt : OLDER.releasedAt, folder: "test-port", executables: [], ...more } });

const open = (releases, installs, config = onWindows) =>
  openApp({ state: { library: [{ id: ID, slug: "test-port", addedAt: 1 }], installs }, config, prepare: (api) => api.setReleases("test-port", releases) });
const looked = (s) => s.until(() => s.api.catalogQueries().some((q) => q.path === "catalog:releases"));
const menu = async (s) => {
  await s.page.locator("article[data-slug]").first().click({ button: "right" });
  return s.page.locator('[role="menu"] [role="menuitem"]');
};
const installedFile = async (s) => {
  await s.page.waitForFunction(() => window.__calls.some((c) => c.cmd === "install"));
  return s.page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request.filename);
};

after(stop);

test("a Windows player is offered the newest Windows release, and it installs without asking", async () => {
  const s = await open([LINUX, WINDOWS, OLDER], installed("0.4.0"));
  try {
    await s.page.locator(UPDATE).first().waitFor({ state: "attached" });
    const items = await menu(s);
    await items.filter({ hasText: "Update to v0.5.0" }).click();
    assert.equal(await installedFile(s), "Port-0.5.0-Windows.zip");
    assert.equal(await s.page.locator(".choose").count(), 0);
  } finally {
    await s.close();
  }
});

test("a Windows player on the newest Windows release has no update", async () => {
  const s = await open([LINUX, WINDOWS, OLDER], installed("0.5.0"));
  try {
    await looked(s);
    await s.page.waitForTimeout(300);
    const items = await menu(s);
    await items.first().waitFor();
    assert.equal(await items.filter({ hasText: "Update to" }).count(), 0);
    assert.equal(await s.page.locator(UPDATE).count(), 0);
  } finally {
    await s.close();
  }
});

test("Get on Windows installs the newest Windows release", async () => {
  const s = await open([LINUX, WINDOWS, OLDER], {});
  try {
    const items = await menu(s);
    await items.filter({ hasText: "Get" }).first().click();
    assert.equal(await installedFile(s), "Port-0.5.0-Windows.zip");
    assert.equal(await s.page.locator(".choose").count(), 0);
  } finally {
    await s.close();
  }
});

test("Get's own choices name the release it installs on this computer", async () => {
  const s = await open([LINUX, WINDOWS, OLDER], {});
  try {
    await s.page.locator("article[data-slug]").first().hover();
    await s.page.locator(".tile-get-more").first().click();
    const get = s.page.locator(".get-choices button.primary");
    await get.filter({ hasText: "Get v0.5.0" }).waitFor();
    // Linux's release says so, and can still be installed; Windows' say nothing.
    const row = (version) => s.page.locator(".versions li", { hasText: version });
    assert.equal(await row("0.5.0").locator(".other-system-tag").count(), 0);
    assert.equal(await row("1.0.0").locator(".other-system-tag").innerText(), "Linux only");
    await row("1.0.0").getByRole("button", { name: "Install" }).waitFor();
    await get.click();
    assert.equal(await installedFile(s), "Port-0.5.0-Windows.zip");
  } finally {
    await s.close();
  }
});

test("Skip this update stops offering it, and the app's page can still install it", async () => {
  const s = await open([LINUX, WINDOWS, OLDER], installed("0.4.0"));
  try {
    await s.page.locator(UPDATE).first().waitFor({ state: "attached" });
    let items = await menu(s);
    await items.filter({ hasText: "Skip this update" }).click();
    await s.page.locator(UPDATE).first().waitFor({ state: "detached" });
    items = await menu(s);
    await items.first().waitFor();
    assert.equal(await items.filter({ hasText: "Update to" }).count(), 0);
    await s.page.keyboard.press("Escape");
    await s.page.locator("article[data-slug]").first().click();
    await s.page.getByText("You skipped v0.5.0.").waitFor();
    await s.page.locator(".release-pinned-line button", { hasText: "Update to v0.5.0" }).click();
    assert.equal(await installedFile(s), "Port-0.5.0-Windows.zip");
  } finally {
    await s.close();
  }
});

test("Auto Update never stops to ask, with no build for this computer or several to pick from", async () => {
  const two = release("0.5.0", when(2025, 11), ["Port-0.5.0-Windows-A.zip", "windows"], ["Port-0.5.0-Windows-B.zip", "windows"]);
  for (const releases of [[LINUX, OLDER], [LINUX, two, OLDER]]) {
    const s = await open(releases, installed("0.4.0", { updates: "auto" }));
    try {
      await looked(s);
      await s.page.waitForTimeout(500);
      assert.equal(await s.page.locator(".choose").count(), 0);
      assert.equal(await s.page.evaluate(() => window.__calls.filter((c) => c.cmd === "install").length), 0);
    } finally {
      await s.close();
    }
  }
});

test("an update keeps Linux to the build installed: Windows' through Wine, or its own", async () => {
  const wine = await open([LINUX, WINDOWS, OLDER], installed("0.4.0", { wine: true }), onLinux);
  try {
    const items = await menu(wine);
    await items.filter({ hasText: "Update to v0.5.0" }).click();
    assert.equal(await installedFile(wine), "Port-0.5.0-Windows.zip");
  } finally {
    await wine.close();
  }
  const windowsNewest = release("1.0.0", when(2026, 0), ["Port-1.0.0-Windows.zip", "windows"]);
  const native = await open([windowsNewest, release("0.5.0", when(2025, 11), ["Port-0.5.0.AppImage", "linux"])], installed("0.4.0"), onLinux);
  try {
    const items = await menu(native);
    await items.filter({ hasText: "Update to v0.5.0" }).click();
    assert.equal(await installedFile(native), "Port-0.5.0.AppImage");
  } finally {
    await native.close();
  }
});
