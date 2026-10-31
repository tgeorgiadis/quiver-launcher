/**
 * A portable Quiver Launcher 3 keeps its library beside itself, wherever the
 * player put it: found by itself in the usual places (Downloads here), and
 * anywhere else from Settings.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, utimesSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

/**
 * A portable 3.x folder with Test Port installed in its Apps folder, as 3.x
 * writes it; without version.txt, as 3.x leaves an install it hasn't looked at.
 */
function portableLibrary(dir, { version = true } = {}) {
  const installed = join(dir, "Apps", "TestPort");
  mkdirSync(installed, { recursive: true });
  if (version) writeFileSync(join(installed, "version.txt"), "v1.0.0");
  writeFileSync(join(installed, "port.sh"), '#!/bin/sh\ncd "$(dirname "$0")" && echo played > launched.txt\n', { mode: 0o755 });
  writeFileSync(join(dir, "QuiverLauncher.AppImage"), "");
  writeFileSync(join(dir, "settings.json"), "{}");
  writeFileSync(join(dir, "apps.json"), JSON.stringify({ Apps: [{ Name: "Test Port", Repository: "quiver/test-port", FolderName: "TestPort" }] }));
  return installed;
}

const bringOver = async (s, installed) => {
  await (await s.app.$("button=Add 1 app")).click();
  await playIt(s, installed);
};

/** Plays Test Port where 3.x installed it. */
const playIt = async ({ card, until }, installed) => {
  // The library redraws as the apps come over, so look the card up afresh until Play shows.
  const play = async () => (await card("test-port")).$("button*=Play");
  await until(() => play().then((b) => b.isDisplayed()).catch(() => false));
  await (await play()).click();
  await until(() => existsSync(join(installed, "launched.txt")));
};

test("a portable 3.x in Downloads is found when the launcher starts", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const installed = portableLibrary(join(home, "Downloads", "QuiverLauncher-linux"));
  const s = await launch({ env: { HOME: home } });
  try {
    await s.usageNoticeSeen();
    await (await s.app.$("p*=Quiver Launcher 3 is on this computer, with 1 app")).waitForDisplayed({ timeout: 20000 });
    await bringOver(s, installed);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
  } finally {
    await s.close();
  }
});

test("a 3.x beside the launcher is the one found, over another elsewhere", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const left = portableLibrary(join(home, "Downloads", "QuiverLauncher-linux"));
  // The player put this launcher's AppImage in the folder of the 3.x they play from.
  const copy = join(mkdtempSync(join(tmpdir(), "quiver-elsewhere-")), "Quiver");
  const installed = portableLibrary(copy);
  // Older than the other, which would otherwise win as the one 3.x saved last.
  utimesSync(join(copy, "apps.json"), new Date(2025, 0, 1), new Date(2025, 0, 1));
  const s = await launch({ env: { HOME: home, APPIMAGE: join(copy, "QuiverLauncher_4.0.0_amd64.AppImage") } });
  try {
    await s.usageNoticeSeen();
    const where = await s.app.$(".old-library-notice .old-library-folder");
    await where.waitForDisplayed({ timeout: 20000 });
    assert.equal(await where.getText(), `Found in ${copy}`);
    await bringOver(s, installed);
    assert.ok(!existsSync(join(left, "launched.txt")));
  } finally {
    await s.close();
  }
});

test("the banner says where it found a 3.x, and another folder can be chosen instead", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const downloads = join(home, "Downloads", "QuiverLauncher-linux");
  const left = portableLibrary(downloads);
  const other = join(mkdtempSync(join(tmpdir(), "quiver-elsewhere-")), "Old Launcher");
  const installed = portableLibrary(other);
  const s = await launch({ env: { HOME: home, QUIVER_PICK_FOLDER: other } });
  try {
    await s.usageNoticeSeen();
    const where = () => s.app.$(".old-library-notice .old-library-folder");
    await (await where()).waitForDisplayed({ timeout: 20000 });
    assert.equal(await (await where()).getText(), `Found in ${downloads}`);
    await (await s.app.$("button=Choose a different folder…")).click();
    await s.until(async () => (await (await where()).getText()) === `Found in ${other}`);
    await bringOver(s, installed);
    assert.ok(!existsSync(join(left, "launched.txt")), "the library found first wasn't the one imported");
  } finally {
    await s.close();
  }
});

test("one anywhere else is found and imported in Settings, by its folder or the folder around it", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const elsewhere = mkdtempSync(join(tmpdir(), "quiver-elsewhere-"));
  const installed = portableLibrary(join(elsewhere, "Old Launcher"), { version: false });
  const empty = mkdtempSync(join(tmpdir(), "quiver-empty-"));
  const settings = async (s) => {
    await s.settings("Library");
    await (await s.app.$("button=Choose my Quiver Launcher 3 folder…")).click();
  };
  // Nothing in the usual places: no banner.
  let s = await launch({ env: { HOME: home, QUIVER_PICK_FOLDER: empty } });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    assert.ok(!(await (await s.app.$("button=Add 1 app")).isExisting()));
    await settings(s);
    await (await s.app.$("p*=That folder doesn't have Quiver Launcher 3 in it")).waitForDisplayed({ timeout: 10000 });
  } finally {
    await s.close();
  }
  s = await launch({ env: { HOME: home, QUIVER_PICK_FOLDER: elsewhere } });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    await s.usageNoticeSeen();
    await settings(s);
    // Settings says what it found and brings it over itself, with no banner asking as well.
    await (await (await s.app.$("section.settings-page")).$("p*=That folder has 1 app, 1 installed")).waitForDisplayed({ timeout: 10000 });
    assert.ok(!(await (await s.app.$("p*=Quiver Launcher 3 is on this computer")).isExisting()));
    await (await (await s.app.$("section.settings-page")).$("button=Add 1 app")).click();
    await (await (await s.app.$("section.settings-page")).$("p=Your apps are in your library.")).waitForDisplayed({ timeout: 20000 });
    await s.closeSettings();
    // Installed, though 3.x never wrote its version down.
    await playIt(s, installed);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
  } finally {
    await s.close();
  }
});
