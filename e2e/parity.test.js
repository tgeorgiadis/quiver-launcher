/**
 * Journeys for what Quiver Launcher 3 could do: catalog filters, an app's
 * README, repository and versions, shortcuts, library playlists and bindings.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, card, until;
// A home folder with a desktop and a Steam account, so shortcuts land somewhere we can look.
const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
const steamConfig = join(home, ".steam/steam/userdata/42/config");

before(async () => {
  mkdirSync(join(home, "Desktop"));
  mkdirSync(steamConfig, { recursive: true });
  s = await launch({ env: { HOME: home } });
  ({ app, api, apps, card, until } = s);
});

after(() => s?.close());

const dialog = () => app.$('[role="dialog"]');
const page = () => app.$(".app-page");
const open = async (slug) => (await (await card(slug)).$(".card-open")).click();

test("the catalog filters by console and sorts as the website does", async () => {
  await (await card("tampered-port")).waitForDisplayed({ timeout: 20000 });
  await (await app.$("select[aria-label=Console]")).selectByVisibleText("Nintendo 64");
  await until(async () => !(await (await card("tampered-port")).isExisting()));
  assert.equal(api.lastQuery().get("console"), "n64");
  await (await app.$("select[aria-label=Sort]")).selectByVisibleText("Top rated");
  await until(() => api.lastQuery().get("sort") === "rating");
  await (await app.$("select[aria-label=Console]")).selectByVisibleText("All consoles");
  await (await card("tampered-port")).waitForDisplayed();
});

test("cards show what the website's cards show", async () => {
  const port = await card("test-port");
  assert.equal(await (await port.$(".based-on .game-chip")).getText(), "Test Port (original)");
  assert.match(await (await port.$(".card-bottom")).getText(), /Mostly runs/);
});

test("an app opens as a full page, with its README and a link to its repository", async () => {
  await (await (await card("test-port")).$("button=Get")).click();
  await (await (await card("test-port")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  await open("test-port");
  assert.equal(await (await page()).$("h1").then((h) => h.getText()), "Test Port");
  assert.equal(await (await card("test-port")).isDisplayed(), false);
  // Back returns to the catalog as it was.
  await (await (await page()).$("button.back-link")).click();
  await (await card("test-port")).waitForDisplayed();
  await open("test-port");
  await (await (await page()).$(".readme")).waitForDisplayed({ timeout: 10000 });
  assert.match(await (await (await page()).$(".readme")).getText(), /A test port/);
  const link = await (await page()).$("a.repo-link");
  assert.match(await link.getText(), /quiver\/test-port/);
  assert.equal(await link.getAttribute("href"), "https://github.com/quiver/test-port");
});

test("any release can be installed, and the ones Quiver verified say so", async () => {
  await (await (await page()).$("button=Change version")).click();
  const row = (version) => app.$(`//section[@aria-label="Versions"]//li[strong[text()="${version}"]]`);
  await (await row("v0.9.0")).waitForDisplayed({ timeout: 10000 });
  assert.match(await (await row("1.0.0")).getText(), /Verified[\s\S]*Installed/);
  assert.match(await (await row("v0.9.0")).getText(), /Not verified/);
  await (await (await row("v0.9.0")).$("button=Install")).click();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "v0.9.0", 30000);
  // It stays on the version picked.
  await until(async () => (await (await (await page()).$("select")).getValue()) === "pinned");
});

test("a desktop shortcut and a Steam shortcut start the game", async () => {
  await (await (await page()).$("button*=Desktop shortcut")).click();
  await (await app.$(".shortcuts p")).waitForDisplayed({ timeout: 10000 });
  assert.match(await (await app.$(".shortcuts p")).getText(), /is on your desktop/);
  const desktop = readFileSync(join(home, "Desktop", "Test Port.desktop"), "utf8");
  assert.match(desktop, /Exec=".*test-port.*port\.sh"/);
  await (await (await page()).$("button=Add to Steam")).click();
  await (await app.$("p*=is in Steam")).waitForDisplayed({ timeout: 10000 });
  const vdf = readFileSync(join(steamConfig, "shortcuts.vdf"));
  assert.ok(vdf.includes("Test Port") && vdf.includes("port.sh") && vdf.includes("QuiverLauncher"));
  await app.keys("Escape");
});

test("library playlists: installed or not, a hand-picked collection, and sections by console", async () => {
  // Tampered Port stays in the library, not installed (its download is refused).
  await (await (await card("tampered-port")).$("button=Get")).click();
  await (await (await card("tampered-port")).$('[role="alert"]')).waitForDisplayed({ timeout: 30000 });
  await (await app.$("button*=Library")).click();
  const playlist = async (name) => (await (await app.$("nav[aria-label=Playlists]")).$(`button=${name}`)).click();
  const shown = () => app.execute(() => [...document.querySelectorAll("main article")].map((e) => e.dataset.slug).sort());
  await playlist("Installed");
  assert.deepEqual(await shown(), ["test-port"]);
  await playlist("Not installed");
  assert.deepEqual(await shown(), ["tampered-port"]);
  await (await app.$("button[aria-label='Add a playlist']")).click();
  await (await (await app.$("[role=menu]")).$("button=New playlist")).click();
  await (await app.$("input[aria-label='Playlist name']")).setValue("Favourites");
  await (await (await dialog()).$("button=Save")).click();
  await (await app.$("p*=No apps in this playlist yet.")).waitForDisplayed();
  await playlist("All");
  await open("test-port");
  await (await (await page()).$("button.chip=Favourites")).click();
  await app.keys("Escape");
  await playlist("Favourites");
  await until(async () => JSON.stringify(await shown()) === '["test-port"]');
  await playlist("All");
  // Sections by console are in the View menu.
  await (await app.$("button[aria-label='View options']")).click();
  await (await app.$("label*=Group by console")).click();
  await app.keys("Escape");
  await (await app.$("h2=Nintendo 64")).waitForDisplayed();
  await (await app.$("h2=Super Nintendo Entertainment System")).waitForDisplayed();
  const saved = JSON.parse(readFileSync(join(s.data, "collections.json"), "utf8"));
  assert.deepEqual(saved.map((c) => [c.name, c.apps]), [["Favourites", ["entry_test-port"]]]);
});

test("settings show controllers and rebind keys and buttons", async () => {
  await (await app.$("button[aria-label=Settings]")).click();
  await (await app.$("p*=No controllers detected")).waitForDisplayed();
  await (await app.$("button[aria-label='Options: keyboard']")).click();
  await app.keys("p");
  await until(async () => (await (await app.$("button[aria-label='Options: keyboard']")).getText()) === "P");
  await (await app.$("button[aria-label='Confirm / Select: controller']")).click();
  await app.execute(() => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "West", down: true } }));
  await until(async () => (await (await app.$("button[aria-label='Confirm / Select: controller']")).getText()) === "X");
  await until(() => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).pad?.confirm?.[0] === "West");
  assert.deepEqual(JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).keys.options, ["P"]);
  await (await app.$("button=Done")).click();
});
