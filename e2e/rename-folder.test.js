/**
 * Renaming an app's folder from its menu or its page: its files and Proton
 * prefix go along, Play starts it from there, and updates keep installing
 * into it. A name another folder has is refused, with nothing changed.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const apps = join(data, "apps");
let s;

before(async () => {
  s = await launch({ data });
});

after(() => s?.close());

const installs = () => JSON.parse(readFileSync(join(data, "installs.json"), "utf8"))["entry_test-port"];
const dialog = () => s.app.$(`[role="dialog"][aria-label="Rename Test Port's folder"]`);
const field = async () => (await dialog()).$("input[aria-label='Folder name']");
const page = () => s.app.$(".app-page");
/** Clicks a button in the side panel, scrolled clear of the usage data notice at the bottom. */
const press = async (button) => {
  await button.scrollIntoView({ block: "center" });
  await button.click();
};

test("Rename folder in the card's menu moves its files and Proton prefix, and Play starts it there", async () => {
  const { app, card, until } = s;
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  writeFileSync(join(apps, "test-port", "save.dat"), "saved");
  mkdirSync(join(data, "proton", "test-port", "pfx"), { recursive: true });

  await (await app.$("button*=Library")).click();
  const library = await card("test-port");
  await (await library.$(".card-open")).moveTo();
  await (await library.$("button[aria-label='Test Port menu']")).click();
  const menu = await app.$('[role="menu"][aria-label="Test Port menu"]');
  await (await menu.$("button=Manage")).click();
  await (await menu.$("button=Rename folder…")).click();
  await (await dialog()).waitForDisplayed();
  assert.equal(await (await field()).getValue(), "test-port");
  // What a folder can't be named is left out.
  await (await field()).setValue("Test: Port?");
  await (await (await dialog()).$("button=Rename")).click();
  await (await app.$("p*=folder is now Test Port")).waitForDisplayed({ timeout: 10000 });
  await (await dialog()).waitForDisplayed({ reverse: true });

  assert.equal(readFileSync(join(apps, "Test Port", "save.dat"), "utf8"), "saved");
  assert.ok(!existsSync(join(apps, "test-port")), "the old folder is gone");
  assert.ok(existsSync(join(data, "proton", "Test Port", "pfx")), "its Proton prefix went along");
  await until(() => installs().folder === "Test Port");

  await (await (await card("test-port")).$("button*=Play")).click();
  await until(() => existsSync(join(apps, "Test Port", "launched.txt")));
});

test("an update installs into the renamed folder", async () => {
  const { app, card, until, api } = s;
  api.release("1.1.0");
  // Browse fetches the catalog again as it opens; the update is on the app's library card.
  await (await app.$("button=Browse")).click();
  await (await app.$("button*=Library")).click();
  // Update, under Play (the corner icon for it is an icon card's, hidden on this one).
  await (await (await card("test-port")).$("button.tile-update")).click();
  await until(() => existsSync(join(apps, "Test Port", ".quiver-version")) && readFileSync(join(apps, "Test Port", ".quiver-version"), "utf8") === "1.1.0", 30000);
  api.release("1.0.0");
  await until(() => installs().version.endsWith("1.1.0"));
  assert.equal(installs().folder, "Test Port");
  assert.ok(!existsSync(join(apps, "test-port")), "nothing was installed under the old name");
});

test("on the app's page, a name another folder has is refused, and the folder stays as it was", async () => {
  const { app, card } = s;
  mkdirSync(join(apps, "Taken"));
  await (await (await card("test-port")).$(".card-open")).click();
  await press(await (await page()).$("button*=On this computer"));
  await press(await (await page()).$("button*=Rename folder"));
  await (await dialog()).waitForDisplayed();
  assert.equal(await (await field()).getValue(), "Test Port");
  await (await field()).setValue("Taken");
  await (await (await dialog()).$("button=Rename")).click();
  await (await (await dialog()).$("p*=already a folder named Taken")).waitForDisplayed({ timeout: 10000 });
  await (await (await dialog()).$("button=Cancel")).click();
  await (await dialog()).waitForDisplayed({ reverse: true });
  assert.ok(existsSync(join(apps, "Test Port", "save.dat")));
  assert.equal(installs().folder, "Test Port");
  await app.keys("Escape");
});

test("a drag from its text field out of the dialog leaves it open; a click outside closes it", async () => {
  const { app, card } = s;
  await (await (await card("test-port")).$(".card-open")).click();
  await press(await (await page()).$("button*=On this computer"));
  await press(await (await page()).$("button*=Rename folder"));
  await (await dialog()).waitForDisplayed();
  // The window's left edge, halfway down: the overlay, outside the dialog.
  const y = await app.execute(() => Math.round(innerHeight / 2));
  assert.ok(await app.execute((y) => document.elementFromPoint(4, y)?.classList.contains("overlay"), y));
  await app.action("pointer").move({ origin: await field() }).down().move({ x: 4, y, duration: 100 }).up().perform();
  await new Promise((r) => setTimeout(r, 300));
  assert.ok(await (await dialog()).isDisplayed(), "selecting the name with a drag doesn't close it");
  await app.action("pointer").move({ x: 4, y }).down().up().perform();
  await (await dialog()).waitForDisplayed({ reverse: true });
  await app.keys("Escape");
});
