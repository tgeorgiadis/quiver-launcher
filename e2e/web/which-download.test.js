/**
 * Which download?: files the site put on no platform (a PS5 or Switch build) aren't files for this computer, so
 * several of them ask as files none of which is marked for it; and Show all files offers the release's others.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const ID = "entry_test-port";
const files = (...list) =>
  list.map(([filename, os]) => ({ id: filename, url: `https://example.com/${filename}`, filename, os, architecture: "unknown", format: "zip", checksum: `sha256:${"a".repeat(64)}` }));
// As BattleShip-Remix v2.0.1-port.12 reached the launcher: the Windows build missing, the PS5 and Switch ones unplaced.
const RELEASE = {
  id: "release_1.0.0",
  version: "1.0.0",
  releasedAt: Date.UTC(2026, 0, 1),
  prerelease: false,
  assets: files(["Port-ps5.zip", "unknown"], ["Port-switch.zip", "unknown"], ["Port-macos.zip", "macos"], ["Port-android.apk", "android"]),
};

const open = (path) =>
  openApp({
    state: { library: [{ id: ID, slug: "test-port", addedAt: 1 }], installs: {} },
    config: { os: "windows", arch: "x64" },
    prepare: (api) => api.setReleases("test-port", [RELEASE]),
  }).then(async (s) => {
    await s.page.locator("article[data-slug]").first().click({ button: "right" });
    await s.page.locator('[role="menu"] [role="menuitem"]').filter({ hasText: "Install" }).first().click();
    await s.page.locator(".choose").waitFor();
    if (path) await s.page.locator(".choose").screenshot({ path });
    return s;
  });
const installedFile = async (s) => {
  await s.page.waitForFunction(() => window.__calls.some((c) => c.cmd === "install"));
  return s.page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request.filename);
};

after(stop);

test("files on no platform ask as none marked for this computer, and Show all files offers the others", async () => {
  const s = await open(process.env.SHOT_DIR && `${process.env.SHOT_DIR}/which-download.png`);
  try {
    const dialog = s.page.locator(".choose");
    assert.match(await dialog.innerText(), /None of .* files are marked for your computer/);
    const choices = dialog.locator(".choices button");
    assert.equal(await choices.count(), 2);
    await dialog.getByRole("button", { name: "Show all files" }).click();
    // The Android app isn't one Windows can install.
    assert.equal(await choices.count(), 3);
    assert.equal(await dialog.getByText("Port-android.apk").count(), 0);
    if (process.env.SHOT_DIR) await dialog.screenshot({ path: `${process.env.SHOT_DIR}/which-download-all.png` });
    await choices.filter({ hasText: "Port-macos.zip" }).click();
    assert.equal(await installedFile(s), "Port-macos.zip");
  } finally {
    await s.close();
  }
});
