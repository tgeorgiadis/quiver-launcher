/**
 * Choosing a folder in the launcher's own browser (file-picker.tsx), as it
 * asks in Steam's Game Mode in place of the system's dialog; QUIVER_OWN_PICKER
 * asks it here.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
// A portable 3.x somewhere the launcher doesn't look by itself.
const old = join(home, "Downloads", "Old Stuff", "QuiverLauncher-linux");
let s;

before(async () => {
  mkdirSync(join(old, "Apps", "TestPort"), { recursive: true });
  writeFileSync(join(old, "Apps", "TestPort", "port.sh"), "#!/bin/sh\n", { mode: 0o755 });
  writeFileSync(join(old, "apps.json"), JSON.stringify({ Apps: [{ Name: "Test Port", Repository: "quiver/test-port", FolderName: "TestPort" }] }));
  s = await launch({ env: { HOME: home, QUIVER_OWN_PICKER: "1" } });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const picker = () => s.app.$('[role=dialog][aria-label="Choose your Quiver Launcher 3 folder"]');
const settings = () => s.app.$("section.settings-page");
const row = async (name) => (await (await picker()).$(".file-picker-list")).$(`button=${name}`);

test("a folder is chosen by going into it from a place, and Settings finds the library there", async () => {
  await s.settings("Library");
  await (await (await settings()).$("button=Choose my Quiver Launcher 3 folder…")).click();
  await (await picker()).waitForDisplayed({ timeout: 10000 });
  // It starts at home; Downloads is a place to start from.
  await (await (await (await picker()).$(".file-picker-places")).$("button=Downloads")).click();
  await (await row("Old Stuff")).click();
  await (await row("QuiverLauncher-linux")).click();
  await s.until(async () => (await (await (await picker()).$(".file-picker-path span")).getText()) === old);
  // Up goes back to the folder around it, and in again.
  await (await (await picker()).$("button*=Up")).click();
  await (await row("QuiverLauncher-linux")).click();
  await (await (await picker()).$("button=Choose this folder")).click();
  await (await picker()).waitForDisplayed({ reverse: true });
  await (await (await settings()).$("p*=That folder has 1 app, 1 installed")).waitForDisplayed({ timeout: 10000 });
});

test("Escape, a controller's B, cancels it and nothing is chosen", async () => {
  await (await (await settings()).$("button=Choose my Quiver Launcher 3 folder…")).click();
  await (await picker()).waitForDisplayed({ timeout: 10000 });
  await s.app.keys("Escape");
  await (await picker()).waitForDisplayed({ reverse: true });
  assert.ok(!(await (await (await settings()).$("p*=That folder doesn't have Quiver Launcher 3 in it")).isExisting()));
  assert.ok(await (await settings()).isDisplayed(), "still in Settings: the Escape was the picker's");
});
