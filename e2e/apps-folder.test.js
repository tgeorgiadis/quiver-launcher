/**
 * The apps folder can move: Settings asks first, moves every installed app
 * with its files, and apps play and install there from then on, after a
 * restart too.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const games = join(mkdtempSync(join(tmpdir(), "quiver-drive-")), "Games");
let s;

before(async () => {
  s = await launch({ env: { QUIVER_PICK_FOLDER: games } });
});

after(() => s?.close());

const settings = () => s.app.$("section.settings-page");
const openSettings = () => s.settings("Apps folder");
const folderText = async () => (await (await settings()).$("section[aria-label='Apps folder'] p")).getText();

test("moving the apps folder takes installed apps and their saves along", async () => {
  const { app, card, apps, until } = s;
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  writeFileSync(join(apps, "test-port", "save.dat"), "my save");

  await openSettings();
  assert.equal(await folderText(), `Apps are installed in ${apps}`);
  await (await (await settings()).$("button=Change…")).click();
  const confirm = await app.$('[role="group"][aria-label="Move apps"]');
  await confirm.waitForDisplayed({ timeout: 10000 });
  assert.match(await confirm.getText(), new RegExp(`Move 1 installed app to ${games.replace(/[/\\]/g, ".")}`));
  await (await confirm.$("button=Move apps")).click();
  await (await (await settings()).$("p*=Moved 1 app")).waitForDisplayed({ timeout: 20000 });
  assert.equal(await folderText(), `Apps are installed in ${games}`);
  assert.equal(readFileSync(join(games, "test-port", "save.dat"), "utf8"), "my save");
  assert.equal(readFileSync(join(games, "test-port", ".quiver-version"), "utf8"), "1.0.0");
  assert.ok(!existsSync(join(apps, "test-port")), "the old copy is gone");
  await s.closeSettings();

  // It plays from there, and the next app installs there.
  await (await (await card("test-port")).$("button*=Play")).click();
  await until(() => existsSync(join(games, "test-port", "launched.txt")));
  await (await (await card("tampered-port")).$("button=Get")).waitForDisplayed();
  await (await app.$("button*=Library")).click();
  await (await app.$("button=Browse")).click();
  const remake = await card("test-remake");
  await (await remake.$("button=Get")).click();
  await (await remake.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  assert.ok(existsSync(join(games, "test-remake", ".quiver-version")));
});

test("the new folder is kept after a restart", async () => {
  await s.close();
  s = await launch({ data: s.data, env: { QUIVER_PICK_FOLDER: games } });
  await openSettings();
  assert.equal(await folderText(), `Apps are installed in ${games}`);
  await s.closeSettings();
  await (await s.app.$("button*=Library")).click();
  await (await (await s.card("test-port")).$("button*=Play")).click();
  // Played again: the file it writes is rewritten.
  await s.until(() => readFileSync(join(games, "test-port", "launched.txt"), "utf8").includes("played"));
});

test("a folder inside the apps folder, or one with an app's folder name taken, is refused", async () => {
  await s.close();
  const taken = mkdtempSync(join(tmpdir(), "quiver-taken-"));
  mkdirSync(join(taken, "test-port"));
  s = await launch({ data: s.data, env: { QUIVER_PICK_FOLDER: taken } });
  await openSettings();
  await (await (await settings()).$("button=Change…")).click();
  await (await (await settings()).$("p*=already has a folder named test-port")).waitForDisplayed({ timeout: 10000 });
  await s.closeSettings();
  await s.close();
  s = await launch({ data: s.data, env: { QUIVER_PICK_FOLDER: join(games, "test-port") } });
  await openSettings();
  await (await (await settings()).$("button=Change…")).click();
  await (await (await settings()).$("p=Choose a folder outside the apps folder.")).waitForDisplayed({ timeout: 10000 });
  assert.ok(existsSync(join(games, "test-port", ".quiver-version")), "nothing moved");
});
