/**
 * Mods for an installed app, from the Thunderstore community and GameBanana
 * game its catalog entry names: browsing and searching, installing with a
 * requirement, choosing one of a mod's files, updating, and uninstalling
 * only what a mod put in the mods folder.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const app = join(data, "apps", "test-port");
const mods = join(app, "mods");
let s;

before(async () => {
  s = await launch({ data });
});

after(() => s?.close());

const dialog = () => s.app.$('[role="dialog"][aria-label="Mods for Test Port"]');
const press = async (button) => {
  await button.waitForDisplayed();
  await button.scrollIntoView({ block: "center" });
  await button.click();
};
const row = async (name) => (await dialog()).$(`li[aria-label="${name}"]`);
const inRow = async (name, button) => (await row(name)).$(`button=${button}`);
const shown = async (name) => (await row(name)).isExisting();
const file = (path) => join(mods, path);
const read = (path) => (existsSync(file(path)) ? readFileSync(file(path), "utf8") : null);
const record = () => JSON.parse(readFileSync(join(app, ".quiver-mods.json"), "utf8")).mods;

test("browse, then install a mod with the mod it needs", async () => {
  const game = await s.card("test-port");
  await game.waitForDisplayed({ timeout: 20000 });
  await (await game.$("button=Get")).click();
  await (await game.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  // A file the player put there themselves.
  mkdirSync(mods, { recursive: true });
  writeFileSync(file("mine.nrm"), "mine");
  await (await game.$(".card-open")).click();
  // On the app's On this computer tab, with the rest of its setup.
  await press(await (await s.app.$(".app-page")).$("button*=On this computer"));
  await press(await (await s.app.$(".app-page")).$("button*=Mods"));
  await (await dialog()).waitForDisplayed();
  await (await (await dialog()).$('ul[aria-label="Mods to browse"]')).waitForDisplayed({ timeout: 20000 });

  // Both sources are listed; NSFW mods only when asked for.
  for (const name of ["Big Head", "Mod Core", "Greener Bushes", "Recolor"]) assert.ok(await shown(name), `${name} is listed`);
  assert.equal(await shown("Spicy"), false);
  await press(await (await dialog()).$("label*=Include NSFW"));
  await s.until(() => shown("Spicy"));
  await press(await (await dialog()).$("label*=Include NSFW"));
  await s.until(async () => !(await shown("Spicy")));

  await (await (await dialog()).$('input[aria-label="Search mods"]')).setValue("big");
  await s.until(async () => !(await shown("Mod Core")));
  assert.ok(await shown("Big Head"));

  await press(await inRow("Big Head", "Install"));
  const prompt = await (await dialog()).$('[aria-label="Mod requirements"]');
  await prompt.waitForDisplayed();
  assert.match(await prompt.getText(), /Big Head needs Mod Core, which isn't installed yet/);
  await press(await prompt.$("button=Install both"));
  await s.until(() => read("BigHead.nrm") === "big head 1.1.0" && read("ModCore.nrm") === "core", 20000);
  await (await (await row("Big Head")).$("span=Installed")).waitForDisplayed();
  assert.equal(read("textures/head.png"), "head");
  // Thunderstore's own files stay out of the game's folder.
  assert.equal(read("manifest.json"), null);
  assert.equal(read("README.md"), null);
  assert.deepEqual(record().map((m) => [m.key, m.version]).sort(), [
    ["thunderstore:Modder-Big_Head", "1.1.0"],
    ["thunderstore:Modder-Mod_Core", "1.0.0"],
  ]);
});

test("a GameBanana mod with several files asks which one", async () => {
  // Typed away, so the page hears it.
  await (await (await dialog()).$('input[aria-label="Search mods"]')).click();
  await s.app.keys(["Control", "a"]);
  await s.app.keys("Backspace");
  await s.until(() => shown("Recolor"));
  await press(await (await dialog()).$("button.chip=GameBanana"));
  await s.until(async () => !(await shown("Big Head")));

  await press(await inRow("Recolor", "Install"));
  const picker = await (await dialog()).$('[aria-label="Choose a file"]');
  await picker.waitForDisplayed({ timeout: 20000 });
  assert.match(await picker.getText(), /The blue one/);
  await press(await picker.$("button*=recolor-blue.zip"));
  await s.until(() => read("blue.pak") === "blue", 20000);
  assert.equal(read("red.pak"), null);

  // One file installs straight away, out of the folder it came in.
  await press(await inRow("Greener Bushes", "Install"));
  await s.until(() => read("bushes.pak") === "bushes", 20000);
  assert.ok(s.api.modDownloads.includes("recolor-blue.zip") && !s.api.modDownloads.includes("recolor-red.zip"));
});

test("update all, then uninstall only what a mod put there", async () => {
  s.api.publishMod({ name: "Big_Head", version: "1.2.0", dependencies: ["Modder-Mod_Core-1.0.0"], files: { "BigHead.nrm": "big head 1.2.0" } });
  await press(await (await dialog()).$("button=Refresh"));
  await press(await (await dialog()).$('button[role="tab"]*=Installed'));
  const list = await (await dialog()).$('ul[aria-label="Installed mods"]');
  await list.waitForDisplayed();
  await (await (await row("Big Head")).$("span=Update available")).waitForDisplayed({ timeout: 20000 });
  assert.match(await (await dialog()).getText(), /1 mod has an update/);

  await press(await (await dialog()).$("button=Update all"));
  await s.until(() => read("BigHead.nrm") === "big head 1.2.0", 20000);
  // The file the old version had and this one doesn't is gone.
  await s.until(() => read("textures/head.png") === null);
  await (await (await row("Big Head")).$("span=Installed")).waitForDisplayed();

  await press(await inRow("Mod Core", "Uninstall"));
  await s.until(() => read("ModCore.nrm") === null);
  await s.until(async () => !(await shown("Mod Core")));
  assert.equal(read("mine.nrm"), "mine");
  assert.equal(read("BigHead.nrm"), "big head 1.2.0");
  assert.deepEqual(record().map((m) => m.key).sort(), ["gamebanana:101", "gamebanana:102", "thunderstore:Modder-Big_Head"]);
});
