/**
 * Apps the player adds themselves: from a GitLab repository (picking one of
 * several games in a release), a program already on this computer, and a
 * folder they fill. Each opens its folder, and only a downloaded one is
 * ever deleted.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { chmodSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const windows = process.platform === "win32";
// A program of the player's own, somewhere outside the launcher, that leaves a mark where it starts.
const games = mkdtempSync(join(tmpdir(), "quiver-own-"));
const program = join(games, windows ? "Mini Game.cmd" : "Mini Game.sh");
writeFileSync(program, windows ? "@echo played> launched.txt\r\n" : "#!/bin/sh\necho played > launched.txt\n");
if (!windows) chmodSync(program, 0o755);
const opened = join(games, "opened.log");

let s, app, api, apps, until;

before(async () => {
  s = await launch({ env: { QUIVER_PICK: program, QUIVER_OPENED: opened } });
  ({ app, api, apps, until } = s);
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => app.$(".page-layer:not([hidden]) .app-page");
const heading = () => page().then((p) => p.$("h1")).then((h) => h.getText()).catch(() => "");
const dialog = () => app.$("[role=dialog][aria-label='Add an app']");
const addAn = async (way) => {
  await (await app.$("button*=Add an app")).click();
  await (await (await dialog()).$(`button*=${way}`)).click();
};
/** Folders the launcher was asked to show, newest last. */
const shown = () => (existsSync(opened) ? readFileSync(opened, "utf8").trim().split("\n").map((l) => l.replace(/^\d+ /, "")) : []);
const library = () => JSON.parse(readFileSync(join(s.data, "library.json"), "utf8"));

test("a GitLab repository with several games in a release: the player picks theirs, and it installs", async () => {
  await addAn("From GitHub or GitLab");
  await (await app.$("input[aria-label=Repository]")).setValue("https://gitlab.com/someone/collection/-/releases");
  await (await app.$("button=Look it up")).click();
  const files = await app.$("fieldset.pick-file");
  await files.waitForDisplayed({ timeout: 10000 });
  const os = windows ? "windows" : "linux";
  const choices = await app.execute(() => [...document.querySelectorAll("fieldset.pick-file input")].map((i) => i.value));
  assert.deepEqual(choices, [`GameA-v1.0-${os}-x64.zip`, `GameB-v1.0-${os}-x64.zip`]);
  await (await app.$(`input[value="GameB-v1.0-${os}-x64.zip"]`)).click();
  const name = await app.$("[role=dialog] input[aria-label=Name]");
  await name.setValue("Game B");
  await (await app.$("button=Add and get")).click();
  await until(async () => (await heading()) === "Game B");
  await until(() => existsSync(join(apps, "collection", ".quiver-version")), 30000);
  assert.deepEqual(api.downloads().slice(-1), [`gameb-v1.0-${os}-x64.zip`]);
  // It stays on Game B in later releases, and it's marked as one Quiver hasn't checked.
  const item = library().find((i) => i.id === "gitlab:someone/collection");
  assert.deepEqual(item.custom, { provider: "gitlab", repository: "someone/collection", name: "Game B", assetFilter: "gameb" });
  assert.match(await (await (await page()).$(".verified")).getText(), /not checked by Quiver/);
  // Open folder shows where it's installed.
  await (await (await page()).$("button*=Open folder")).click();
  await until(() => shown().at(-1) === join(apps, "collection"));
  await app.keys("Escape");
});

test("a program already on this computer starts where it is, and removing it leaves it there", async () => {
  await addAn("A program on this computer");
  await (await app.$("button=Choose the program…")).click();
  await (await app.$(`p=${program}`)).waitForDisplayed({ timeout: 10000 });
  assert.equal(await (await app.$("[role=dialog] input[aria-label=Name]")).getValue(), "Mini Game");
  await (await (await dialog()).$("button=Add")).click();
  await until(async () => (await heading()) === "Mini Game");
  await (await (await page()).$("button*=Play")).click();
  await until(() => existsSync(join(games, "launched.txt")));
  await (await (await page()).$("button*=Open folder")).click();
  await until(() => shown().at(-1)?.replace(/[\\/]$/, "") === games);
  // It's on this computer only: not in an account's library, and not in the apps folder.
  const item = library().find((i) => i.local);
  assert.deepEqual(item.local, { kind: "program", path: program, name: "Mini Game" });
  assert.equal(item.account, undefined);
  await (await (await page()).$("button*=Remove from library")).click();
  await until(() => !library().some((i) => i.local));
  assert.ok(existsSync(program), "the player's program is still there");
});

test("a folder to fill, with the game's artwork matched by name, plays once a program is in it", async () => {
  await addAn("A folder you fill yourself");
  await (await app.$("[role=dialog] input[aria-label=Name]")).setValue("Tampered");
  // The game it plays is matched by name and picked, for its artwork and consoles.
  const match = await app.$('.art-match button[data-game="tampered-port"]');
  await match.waitForDisplayed({ timeout: 10000 });
  await until(async () => (await match.getAttribute("aria-pressed")) === "true");
  await (await app.$("button=Make the folder")).click();
  await until(async () => (await heading()) === "Tampered");
  const folder = join(apps, "Tampered");
  assert.ok(existsSync(folder));
  await until(() => shown().at(-1) === folder);
  assert.match(await (await (await page()).$(".eyebrow")).getText(), /SUPER NINTENDO ENTERTAINMENT SYSTEM/);
  assert.equal(await (await (await page()).$(".based-on .game-chip")).getText(), "Tampered Port (original)");
  // Empty, it has nothing to start yet.
  await (await (await page()).$("button*=Play")).click();
  const alert = await (await page()).$("[role=alert]");
  await alert.waitForDisplayed({ timeout: 10000 });
  assert.match(await alert.getText(), /Couldn't find a program to start/);
  await (await alert.$("button=Dismiss")).click();
  // The player puts the app in its folder, and Play starts it there.
  mkdirSync(join(folder, "bin"));
  if (windows) copyFileSync(join(process.env.SystemRoot ?? "C:\\Windows", "System32", "hostname.exe"), join(folder, "bin", "game.exe"));
  else copyFileSync(program, join(folder, "bin", "game.sh"));
  await (await (await page()).$("button*=Play")).click();
  if (windows) {
    // hostname.exe leaves no mark; starting it without a problem is enough here (Linux checks the mark).
    await new Promise((r) => setTimeout(r, 2000));
    assert.ok(!(await (await page()).$("[role=alert]").isExisting()), "Play started the program");
  } else {
    await until(() => existsSync(join(folder, "bin", "launched.txt")));
  }
  await (await (await page()).$("button*=Remove from library")).click();
  await until(() => !library().some((i) => i.local));
  assert.ok(existsSync(join(folder, "bin")), "the player's files are still there");
});
