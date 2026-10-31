/**
 * Quiver Launcher updating itself: offered when a newer release is out,
 * refused when its signature doesn't match, and installed (an AppImage here)
 * with the saved files backed up first. A copy that can't update itself is
 * sent to the release page.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

const api = await startMockApi();
const dir = mkdtempSync(join(tmpdir(), "quiver-appimage-"));
const image = join(dir, "Quiver_Launcher.AppImage");
const updated = join(dir, "updated.txt");
const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
let s;

after(async () => {
  await s?.close();
  api.close();
});

const banner = () => s.app.$('[role="status"][aria-label="Launcher update"]');
const checkInSettings = async () => {
  await s.settings("Updates");
  const section = await s.app.$('section[aria-label="Quiver Launcher"]');
  await (await section.$("button=Check for updates")).click();
  return section;
};
const closeSettings = () => s.closeSettings();

test("nothing newer: no banner, and Settings says so", async () => {
  writeFileSync(image, "#!/bin/sh\necho old\n", { mode: 0o755 });
  s = await launch({ api, data, env: { APPIMAGE: image, QUIVER_UPDATED: updated } });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  // The update's banner would wait behind it (the data folder keeps the answer for the copy started later).
  await s.usageNoticeSeen();
  assert.ok(!(await (await banner()).isExisting()));
  const section = await checkInSettings();
  assert.match(await section.getText(), /Version 4\.\d+\.\d+/);
  await (await section.$("p=You have the newest version.")).waitForDisplayed({ timeout: 10000 });
  await closeSettings();
});

test("an update whose signature doesn't match isn't installed", async () => {
  api.publishLauncher({ version: "9.0.0", tampered: true });
  await checkInSettings();
  await closeSettings();
  await (await banner()).waitForDisplayed({ timeout: 10000 });
  assert.match(await (await banner()).getText(), /Quiver Launcher 9\.0\.0 is out\./);
  await (await (await banner()).$("button=Update now")).click();
  await (await (await banner()).$("span*=signature doesn't match")).waitForDisplayed({ timeout: 10000 });
  assert.equal(readFileSync(image, "utf8"), "#!/bin/sh\necho old\n");
  assert.ok(!existsSync(updated));
  // What's new opens the release page.
  await (await (await banner()).$("button=What's new")).click();
  await s.until(() => api.releasePages().includes("v9.0.0"));
});

test("a signed update backs up the library, replaces the AppImage and starts it", async () => {
  api.publishLauncher({ version: "9.0.1" });
  // Not now hides it until the next check.
  await (await (await banner()).$("button=Not now")).click();
  assert.ok(!(await (await banner()).isExisting()));
  await checkInSettings();
  await closeSettings();
  await (await banner()).waitForDisplayed({ timeout: 10000 });
  assert.match(await (await banner()).getText(), /Quiver Launcher 9\.0\.1 is out\./);
  await (await (await banner()).$("button=Update now")).click();
  // This copy closes and the new one starts.
  await s.until(() => existsSync(updated), 30000);
  assert.equal(readFileSync(updated, "utf8").trim(), "9.0.1");
  assert.match(readFileSync(image, "utf8"), /9\.0\.1/);
  const backups = readdirSync(join(data, "backups", "updates"));
  assert.equal(backups.length, 1);
  assert.match(backups[0], /-to-9\.0\.1$/);
  const saved = readdirSync(join(data, "backups", "updates", backups[0]));
  assert.ok(saved.includes("settings.json"), `backed up: ${saved}`);
  await s.close();
  s = null;
});

test("a copy that can't update itself opens the release page", async () => {
  // The first copy, if the test before stopped before closing it.
  await s?.close();
  s = await launch({ api, data });
  await (await banner()).waitForDisplayed({ timeout: 20000 });
  assert.match(await (await banner()).getText(), /9\.0\.1 is out\. Update Quiver Launcher the way you installed it/);
  await (await (await banner()).$("button=Open the release page")).click();
  await s.until(() => api.releasePages().includes("v9.0.1"));
});
