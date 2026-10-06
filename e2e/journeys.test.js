/**
 * User journeys through the real app (Tauri + WebDriver). Build first with
 * `pnpm --filter @quiver/e2e build`; Linux needs tauri-driver,
 * WebKitWebDriver and a display (xvfb-run).
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, card, until;

before(async () => {
  s = await launch();
  ({ app, api, apps, card, until } = s);
});

after(() => s?.close());

test("a new player gets a port in one click and plays it", async () => {
  // An empty library starts in the catalog, showing ports for this computer.
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();

  // Get adds it to the library and installs the verified release.
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  const folder = join(apps, "test-port");
  assert.equal(readFileSync(join(folder, ".quiver-version"), "utf8"), "1.0.0");
  assert.ok(existsSync(join(folder, "data/level1.dat")), "the wrapper folder is flattened");
  assert.ok(existsSync(join(folder, "portable.txt")), "the catalog's filesToAdd are created");
  assert.match(await (await port.$(".cover-badge")).getText(), /in library/i);

  // It's in the library, and Play starts it.
  await (await app.$("button*=Library")).click();
  const owned = await card("test-port");
  await (await owned.$("button*=Play")).click();
  await until(() => existsSync(join(folder, "launched.txt")));
});

test("a new verified release is offered as an update and installs", async () => {
  api.release("1.1.0");
  await (await app.$("button=Browse")).click();
  const port = await card("test-port");
  await (await port.$("button*=Update to v1.1.0")).click();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "1.1.0");
  assert.ok(existsSync(join(apps, "test-port", "launched.txt")), "files the app wrote are kept");
  api.release("1.0.0");
});

test("an app can stay on its version, or update by itself", async () => {
  const updates = async (choice) => {
    await (await (await card("test-port")).$(".card-open")).click();
    await (await (await app.$(".app-page")).$("select")).selectByVisibleText(choice);
    await app.keys("Escape");
  };
  const reload = async () => {
    await (await app.$("button*=Library")).click();
    await (await app.$("button=Browse")).click();
  };
  await updates("Always stay on v1.1.0");
  api.release("1.2.0");
  await reload();
  await (await (await card("test-port")).$("button*=Play")).waitForDisplayed();
  assert.ok(!(await (await card("test-port")).$("button*=Update").isExisting()), "no update offered");
  await updates("Install automatically");
  await reload();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "1.2.0", 30000);

  // Going back a version: Auto Update doesn't put the newer one straight back, but the next release still comes.
  await (await (await card("test-port")).$(".card-open")).click();
  const page = await app.$(".app-page");
  await (await page.$("button=Change version")).click();
  const row = (version) => app.$(`//section[@aria-label="Versions"]//li[strong[text()="${version}"]]`);
  await (await (await row("v1.0.0")).$("button=Install")).click();
  await (await (await app.$('//section[@role="dialog"]')).$("button=Install anyway")).click();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "v1.0.0", 30000);
  assert.equal(await (await page.$("select")).getValue(), "auto");
  await (await page.$("p*=put v1.2.0 back")).waitForDisplayed({ timeout: 10000 });
  await app.keys("Escape");
  await reload();
  await (await (await card("test-port")).$("button=Update to v1.2.0")).waitForDisplayed({ timeout: 10000 });
  assert.equal(readFileSync(join(apps, "test-port", ".quiver-version"), "utf8"), "v1.0.0");
  api.release("1.3.0");
  await reload();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "1.3.0", 30000);
  api.release("1.0.0");
});

test("a download that doesn't match Quiver's checksum is refused", async () => {
  await (await app.$("button=Browse")).click();
  const port = await card("tampered-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  const alert = await port.$('[role="alert"]');
  await alert.waitForDisplayed({ timeout: 30000 });
  assert.match(await alert.getText(), /doesn't match the file Quiver checked/);
  assert.ok(!existsSync(join(apps, "tampered-port")), "nothing was installed");
});

test("arrow keys and controllers move between cards", async () => {
  await (await app.$("input[placeholder^=Search]")).click();
  const focused = () => app.execute(() => document.activeElement?.closest("article")?.dataset.slug);
  // Down from the search box reaches the first card; the sort and Filters are beside the search.
  await app.keys("ArrowDown");
  assert.equal(await focused(), "test-port");
  await app.keys("ArrowRight");
  assert.equal(await focused(), "tampered-port");
  // A controller's d-pad, as the Rust side reports it.
  const pad = (key, down) =>
    app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
  await pad("DPadLeft", true);
  await pad("DPadLeft", false);
  await until(async () => (await focused()) === "test-port");
});

test("search narrows the catalog", async () => {
  await (await app.$("input[placeholder^=Search]")).setValue("tampered");
  await until(async () => !(await (await card("test-port")).isExisting()));
  assert.ok(await (await card("tampered-port")).isExisting());
});

test("removing an app uninstalls it and takes it out of the library", async () => {
  await (await app.$("button*=Library")).click();
  await (await (await card("test-port")).$(".card-open")).click();
  await (await app.$("button*=Uninstall and remove")).click();
  await until(() => !existsSync(join(apps, "test-port")));
  await until(async () => !(await (await card("test-port")).isExisting()));
});

test("an app the catalog doesn't list is added from its GitHub repository", async () => {
  const fromRepository = async (address) => {
    await (await app.$("button*=Add an app")).click();
    await (await app.$("button*=From GitHub or GitLab")).click();
    await (await app.$("input[aria-label=Repository]")).setValue(address);
    await (await app.$("button=Look it up")).click();
  };
  await (await app.$("button=Browse")).click();
  // A repository the catalog lists opens the catalog's app instead.
  await fromRepository("https://github.com/quiver/test-port");
  await (await app.$("h1=Test Port")).waitForDisplayed({ timeout: 10000 });
  await app.keys("Escape");
  await fromRepository("someone/homebrew");
  await (await app.$("p*=Downloads Homebrew-")).waitForDisplayed({ timeout: 10000 });
  await (await app.$("button=Add and get")).click();
  await (await (await app.$(".app-page")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  assert.equal(readFileSync(join(apps, "homebrew", ".quiver-version"), "utf8"), "v2.0");
  await app.keys("Escape");
});

test("settings make the interface larger, and it stays that way", async () => {
  await (await app.$("button[aria-label=Settings]")).click();
  await (await app.$('[aria-label="Settings"] select')).selectByVisibleText("Large");
  await (await app.$("button=Done")).click();
  assert.equal(await app.execute(() => document.documentElement.style.zoom), "1.15");
  await until(() => existsSync(join(s.data, "settings.json")));
  assert.equal(JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).scale, 1.15);
});
