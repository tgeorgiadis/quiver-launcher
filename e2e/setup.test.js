/**
 * The first start's setup (setup.tsx): a step at a time, a Quiver Launcher 3
 * library imported on the way, and only once.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

/** A portable 3.x folder with Test Port in its Apps folder, or wherever `apps` says. */
function oldLibrary(dir, { apps = join(dir, "Apps"), settings = {} } = {}) {
  const installed = join(apps, "TestPort");
  mkdirSync(installed, { recursive: true });
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(installed, "port.sh"), '#!/bin/sh\ncd "$(dirname "$0")" && echo played > launched.txt\n', { mode: 0o755 });
  writeFileSync(join(dir, "settings.json"), JSON.stringify(settings));
  writeFileSync(join(dir, "apps.json"), JSON.stringify({ Apps: [{ Name: "Test Port", Repository: "quiver/test-port", FolderName: "TestPort" }] }));
  return installed;
}

const setup = (s) => s.app.$('[role=dialog][aria-label="Set up Quiver Launcher"]');
const heading = async (s) => (await (await setup(s)).$("h2")).getText();
const press = async (s, name) => (await (await setup(s)).$(`button=${name}`)).click();
/** On to the step called `name`. */
const onTo = async (s, name) => s.until(async () => (await heading(s)) === name);

test("the first start asks a step at a time, importing a 3.x library on the way, and never again", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const folder = join(home, "Downloads", "QuiverLauncher-linux");
  const installed = oldLibrary(folder);
  let s = await launch({ setup: true, env: { HOME: home } });
  try {
    await (await setup(s)).waitForDisplayed({ timeout: 20000 });
    assert.equal(await heading(s), "Welcome to Quiver Launcher!");
    // Escape, a controller's B, never leaves it from the first step.
    await s.app.keys("Escape");
    assert.equal(await heading(s), "Welcome to Quiver Launcher!");
    await press(s, "Next");
    await onTo(s, "Coming from Quiver Launcher 3?");
    const dialog = await setup(s);
    await s.until(async () => (await (await dialog.$(".old-library-folder")).getText()) === `Found in ${folder}`);
    assert.match(await (await dialog.$(".old-library-apps")).getText(), new RegExp(`Test Port\\s+Installed in ${join(folder, "Apps", "TestPort")}`));
    // Escape goes back a step.
    await s.app.keys("Escape");
    await onTo(s, "Welcome to Quiver Launcher!");
    await press(s, "Next");
    await onTo(s, "Coming from Quiver Launcher 3?");
    await press(s, "Add 1 app");
    // All of it came, so it goes on by itself, and the next step says so.
    await s.until(async () => (await heading(s)) === "Where apps install", 20000);
    assert.ok(await (await (await setup(s)).$("p*=All done: your Quiver Launcher 3 apps are in your library")).isDisplayed());
    await press(s, "Next");
    await onTo(s, "Usage data");
    await press(s, "Next");
    await onTo(s, "Sign in to sync your library");
    await press(s, "Start using Quiver");
    await (await setup(s)).waitForDisplayed({ reverse: true });
    // A new player starts in Browse; with their apps imported, it's their library that shows.
    await s.until(() => s.app.execute(() => /Library/.test(document.querySelector("button[aria-current=page]")?.textContent ?? "")));
    // Each step it went through was answered: no banner asks again.
    assert.ok(!(await (await s.app.$("[aria-label='Usage data']")).isExisting()));
    assert.ok(!(await (await s.app.$(".old-library-notice")).isExisting()));
    const play = async () => (await s.card("test-port")).$("button*=Play");
    await s.until(() => play().then((b) => b.isDisplayed()).catch(() => false));
    await (await play()).click();
    await s.until(() => existsSync(join(installed, "launched.txt")));
    assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
  } finally {
    await s.close();
  }
  s = await launch({ setup: true, data: s.data, env: { HOME: home } });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    assert.ok(!(await (await setup(s)).isExisting()), "the setup is only for the first start");
    // Settings runs it again. The library has its apps now, so the Quiver Launcher 3 step asks rather than offering one.
    await s.settings("General");
    await (await (await s.app.$("section.settings-page")).$("button=Run setup again")).click();
    await onTo(s, "Welcome to Quiver Launcher!");
    await press(s, "Next");
    await onTo(s, "Coming from Quiver Launcher 3?");
    await (await (await setup(s)).$("p*=Show Quiver where your Quiver Launcher 3 folder is")).waitForDisplayed();
    await press(s, "Skip setup");
    await (await setup(s)).waitForDisplayed({ reverse: true });
    assert.ok(await (await s.app.$("section.settings-page")).isDisplayed(), "back in Settings");
  } finally {
    await s.close();
  }
});

test("Skip setup leaves what it didn't ask to the banners", async () => {
  const s = await launch({ setup: true, env: { HOME: mkdtempSync(join(tmpdir(), "quiver-home-")) } });
  try {
    await (await setup(s)).waitForDisplayed({ timeout: 20000 });
    await press(s, "Skip setup");
    await (await setup(s)).waitForDisplayed({ reverse: true });
    await (await s.app.$("[aria-label='Usage data']")).waitForDisplayed();
  } finally {
    await s.close();
  }
});

test("a 3.x library whose installs moved is matched to its Apps folder where it is now", async () => {
  const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
  const folder = join(home, "Downloads", "QuiverLauncher-linux");
  // 3.x's settings name an Apps folder that's gone; its installs are somewhere else now.
  const moved = join(home, "Games");
  oldLibrary(folder, { apps: moved, settings: { AppsPath: "/media/old-card/Apps" } });
  const s = await launch({ setup: true, env: { HOME: home, QUIVER_PICK_FOLDER: moved } });
  try {
    await (await setup(s)).waitForDisplayed({ timeout: 20000 });
    await press(s, "Next");
    await onTo(s, "Coming from Quiver Launcher 3?");
    const dialog = await setup(s);
    await (await dialog.$("p*=None of these show as installed")).waitForDisplayed({ timeout: 20000 });
    assert.match(await (await dialog.$(".old-library-apps")).getText(), /Not installed/);
    await press(s, "Choose the Apps folder…");
    await s.until(async () => (await (await dialog.$(".old-library-apps")).getText()).includes(`Installed in ${join(moved, "TestPort")}`));
  } finally {
    await s.close();
  }
});
