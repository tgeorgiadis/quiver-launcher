/**
 * A portable Quiver Launcher 3 keeps its library beside itself, wherever the
 * player put it: found by itself in the usual places (Downloads here), and
 * anywhere else from Settings.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, renameSync, utimesSync, writeFileSync } from "node:fs";
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
    const banner = await s.app.$(".old-library-notice");
    await (await banner.$("strong=Bring your apps over from Quiver Launcher 3?")).waitForDisplayed({ timeout: 20000 });
    assert.match(await banner.getText(), /1 app, already installed\./);
    // Nothing looks moved, so there's no folder to ask about.
    assert.ok(!(await (await banner.$("button*=folder")).isExisting()));
    await bringOver(s, installed);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
  } finally {
    await s.close();
  }
});

test("Not now on the banner stands, and Settings can still bring the library over", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const folder = join(home, "Downloads", "QuiverLauncher-linux");
  const installed = portableLibrary(folder);
  let s = await launch({ env: { HOME: home } });
  try {
    await s.usageNoticeSeen();
    await (await (await s.app.$(".old-library-notice")).$("button=Not now")).click();
    await s.until(async () => !(await (await s.app.$(".old-library-notice")).isExisting()));
    await new Promise((r) => setTimeout(r, 1000));
  } finally {
    await s.close();
  }
  s = await launch({ data: s.data, env: { HOME: home, QUIVER_PICK_FOLDER: folder } });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    await new Promise((r) => setTimeout(r, 2000));
    assert.ok(!(await (await s.app.$(".old-library-notice")).isExisting()), "it isn't asked again");
    await s.settings("Library");
    const page = await s.app.$("section.settings-page");
    await (await page.$("button=Choose my Quiver Launcher 3 folder…")).click();
    await (await page.$("button=Add 1 app")).click();
    await (await page.$("p=Your apps are in your library.")).waitForDisplayed({ timeout: 20000 });
    await s.closeSettings();
    await playIt(s, installed);
  } finally {
    await s.close();
  }
});

test("a banner whose installs look moved asks for the folder, and one more folder finds them", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const folder = join(home, "Downloads", "QuiverLauncher-linux");
  portableLibrary(folder);
  const moved = join(home, "Games");
  renameSync(join(folder, "Apps"), moved);
  const s = await launch({ env: { HOME: home, QUIVER_PICK_FOLDER: moved } });
  try {
    await s.usageNoticeSeen();
    const banner = await s.app.$(".old-library-notice");
    await (await banner.$("button=Choose a folder…")).waitForDisplayed({ timeout: 20000 });
    assert.match(await banner.getText(), /1 app, not installed yet/);
    await (await banner.$("button=Choose a folder…")).click();
    await s.until(async () => /1 app, already installed/.test(await banner.getText()));
    assert.ok(!(await (await banner.$("button=Choose a folder…")).isExisting()));
    await bringOver(s, join(moved, "TestPort"));
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
    await bringOver(s, installed);
    assert.ok(!existsSync(join(left, "launched.txt")));
  } finally {
    await s.close();
  }
});

/**
 * A library brought over when its installs couldn't be seen, as the first betas left a custom Apps folder; they can be
 * now. Returns where Test Port is installed, and the running launcher's home and data to open them again.
 */
async function broughtOverWithoutInstalls() {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const copy = join(home, "Downloads", "QuiverLauncher-linux");
  const installed = portableLibrary(copy);
  const apps = join(copy, "Apps");
  renameSync(apps, `${apps}-away`);
  const s = await launch({ env: { HOME: home } });
  try {
    await s.usageNoticeSeen();
    await (await s.app.$("button=Add 1 app")).click();
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    // The library's saved before this copy stops.
    await s.until(async () => !(await (await s.app.$(".old-library-notice")).isExisting()));
    await new Promise((r) => setTimeout(r, 1000));
  } finally {
    await s.close();
  }
  renameSync(`${apps}-away`, apps);
  return { installed, home, data: s.data };
}

test("installs that couldn't be seen when a library came over are offered once, and played from where they are", async () => {
  const { installed, home, data } = await broughtOverWithoutInstalls();
  let s = await launch({ data, env: { HOME: home } });
  try {
    await (await s.app.$("p*=1 app in your library shows as not installed")).waitForDisplayed({ timeout: 20000 });
    await (await s.app.$("button=Use it")).click();
    await playIt(s, installed);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
    await new Promise((r) => setTimeout(r, 1000));
  } finally {
    await s.close();
  }
  s = await launch({ data, env: { HOME: home } });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    await new Promise((r) => setTimeout(r, 2000));
    assert.ok(!(await (await s.app.$(".old-library-notice")).isExisting()), "it isn't asked again");
  } finally {
    await s.close();
  }
});

test("an app in the library can be pointed at the folder it's installed in, which has to hold an app", async () => {
  const { installed, home, data } = await broughtOverWithoutInstalls();
  const locate = async (s) => {
    const port = await s.card("test-port");
    await (await port.$(".card-open")).moveTo();
    await (await port.$("button[aria-label='Test Port menu']")).click();
    const menu = () => s.app.$('[role="menu"][aria-label="Test Port menu"]');
    await (await (await menu()).$("button=Manage")).click();
    await (await (await menu()).$("button=Locate existing install…")).click();
  };
  const notNow = async (s) => {
    await (await s.app.$("p*=1 app in your library shows as not installed")).waitForDisplayed({ timeout: 20000 });
    await (await (await s.app.$(".old-library-notice")).$("button=Not now")).click();
  };
  const empty = mkdtempSync(join(tmpdir(), "quiver-empty-"));
  let s = await launch({ data, env: { HOME: home, QUIVER_PICK_FOLDER: empty } });
  try {
    await notNow(s);
    await locate(s);
    await (await s.app.$("p=That folder doesn't have an installed app in it.")).waitForDisplayed({ timeout: 10000 });
    await new Promise((r) => setTimeout(r, 1000));
  } finally {
    await s.close();
  }
  s = await launch({ data, env: { HOME: home, QUIVER_PICK_FOLDER: installed } });
  try {
    await locate(s);
    await playIt(s, installed);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
  } finally {
    await s.close();
  }
});

test("an app in the library can be pointed at its folder from On this computer", async () => {
  const { installed, home, data } = await broughtOverWithoutInstalls();
  const s = await launch({ data, env: { HOME: home, QUIVER_PICK_FOLDER: installed } });
  try {
    await (await (await s.app.$(".old-library-notice")).$("button=Not now")).click();
    await (await (await s.card("test-port")).$(".card-open")).click();
    const page = await s.app.$(".app-page");
    await (await page.$("button*=On this computer")).click();
    await (await (await page.$(".computer-panel")).$("button*=Locate existing install")).click();
    await (await page.$("button*=Play")).waitForDisplayed({ timeout: 10000 });
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded");
  } finally {
    await s.close();
  }
});

test("an app that isn't in the library can be pointed at its folder from its page, and joins the library", async () => {
  const installed = portableLibrary(mkdtempSync(join(tmpdir(), "quiver-elsewhere-")));
  const s = await launch({ env: { QUIVER_PICK_FOLDER: installed } });
  try {
    await s.usageNoticeSeen();
    await (await (await s.card("test-port")).$(".card-open")).click();
    await (await (await s.app.$(".app-page")).$("button*=Locate existing install")).click();
    await s.until(async () => (await (await s.app.$(".app-page")).$("button*=Add to library").isExisting()) === false && (await (await s.app.$(".app-page")).$("button*=Open folder").isExisting()));
    await (await (await s.app.$(".app-page")).$("button*=Play")).click();
    await s.until(() => existsSync(join(installed, "launched.txt")));
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded");
  } finally {
    await s.close();
  }
});

test("an app whose folder moved offers to find it where it went, keeping its history", async () => {
  const { installed, home, data } = await broughtOverWithoutInstalls();
  const page = () => s.app.$(".app-page");
  let s = await launch({ data, env: { HOME: home, QUIVER_PICK_FOLDER: installed } });
  try {
    await (await s.app.$("button=Use it")).click();
    await (await (await s.card("test-port")).$(".card-open")).click();
    const moved = `${installed}-moved`;
    renameSync(installed, moved);
    await (await (await page()).$("button*=Play")).click();
    const alert = await (await page()).$("[role=alert]");
    await alert.waitForDisplayed({ timeout: 10000 });
    assert.match(await alert.getText(), /This app's folder isn't there any more/);
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded");
    // Back where the picker answers: the same path, as the test can only set it when the launcher starts.
    renameSync(moved, installed);
    await (await alert.$("button*=Locate existing install")).click();
    await s.until(async () => !(await (await page()).$("[role=alert]").isExisting()));
    await (await (await page()).$("button*=Play")).click();
    await s.until(() => existsSync(join(installed, "launched.txt")));
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
    await (await (await s.app.$("section.settings-page")).$("p*=1 app, already installed")).waitForDisplayed({ timeout: 10000 });
    assert.ok(!(await (await s.app.$(".old-library-question")).isExisting()));
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
