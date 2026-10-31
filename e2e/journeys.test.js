/**
 * User journeys through the real app (Tauri + WebDriver). Build first with
 * `pnpm --filter @quiver/e2e build`; Linux needs tauri-driver,
 * WebKitWebDriver and a display (xvfb-run).
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, card, until;

before(async () => {
  // These journeys come back to Browse for apps in the library, so Browse shows them (browse-owned.test.js covers leaving them out).
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ browseShowsLibrary: true }));
  s = await launch({ data });
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
  // How it updates is on its page's On this computer tab.
  const updates = async (choice) => {
    await (await (await card("test-port")).$(".card-open")).click();
    await (await (await app.$(".app-page")).$("button*=On this computer")).click();
    await (await (await app.$(".app-page")).$(".//label[span[text()='Updates']]/select")).selectByVisibleText(choice);
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
  await (await page.$("button*=On this computer")).click();
  await (await page.$("button=Change version")).click();
  const row = (version) => app.$(`//section[@aria-label="Versions"]//li[strong[text()="${version}"]]`);
  await (await (await row("v1.0.0")).$("button=Install")).click();
  await (await (await app.$('//section[@role="dialog"]')).$("button=Install anyway")).click();
  await until(() => readFileSync(join(apps, "test-port", ".quiver-version"), "utf8") === "v1.0.0", 30000);
  assert.equal(await (await page.$(".//label[span[text()='Updates']]/select")).getValue(), "auto");
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
  // The site hears of it, once, to read that release again.
  await until(() => api.downloadProblems().length > 0);
  const [{ file, ...report }, ...more] = api.downloadProblems();
  assert.deepEqual(report, { slug: "tampered-port", version: "1.0.0", problem: "mismatch" });
  assert.match(file, /^tampered-port-(linux|windows)\.zip$/);
  assert.equal(more.length, 0);
});

test("arrow keys and controllers move between cards", async () => {
  await (await app.$("input[placeholder^=Search]")).click();
  const focused = () => app.execute(() => document.activeElement?.closest("article")?.dataset.slug);
  // Down from the search box goes into the first shelf (its See all, then its cards); the sort and Filters are beside the search.
  await app.keys("ArrowDown");
  assert.equal(await app.execute(() => document.activeElement?.closest("section.shelf")?.getAttribute("aria-label")), "Players say it runs well");
  // Along the shelf, card to card.
  await app.execute(() => document.querySelector('section.shelf article[data-slug="test-port"] .card-open').focus());
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

test("Uninstall on an app's page deletes its files and keeps it in the library", async () => {
  await (await app.$("button*=Library")).click();
  await (await (await card("test-port")).$(".card-open")).click();
  await (await (await app.$(".app-actions")).$("button*=Uninstall")).click();
  // It asks first, with Cancel focused; Escape (a controller's B) says no.
  const prompt = await app.$('[role=dialog][aria-label="Uninstall Test Port"]');
  await prompt.waitForDisplayed();
  assert.equal(await app.execute(() => document.activeElement?.textContent), "Cancel");
  await app.keys("Escape");
  await prompt.waitForDisplayed({ reverse: true });
  assert.ok(existsSync(join(apps, "test-port")), "nothing was deleted");
  assert.ok(await (await app.$(".page-layer:not([hidden]) .app-page")).isExisting(), "Escape closed the prompt, not the page");
  await (await (await app.$(".app-actions")).$("button*=Uninstall")).click();
  await (await prompt.$("button=Uninstall")).click();
  await until(() => !existsSync(join(apps, "test-port")));
  // Still in the library, ready to get again from the same page.
  const page = await app.$(".app-page");
  await (await page.$("button*=Get")).waitForDisplayed();
  await (await page.$("button*=On this computer")).click();
  assert.ok(await (await page.$("button*=Remove from library")).isExisting());
  await (await page.$("button*=Get")).click();
  await (await page.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
});

test("removing an app uninstalls it and takes it out of the library", async () => {
  await (await app.$("button*=Library")).click();
  await (await (await card("test-port")).$(".card-open")).click();
  await (await (await app.$(".app-page")).$("button*=On this computer")).click();
  await (await app.$("button*=Uninstall and remove")).click();
  await (await (await app.$("[role=dialog]")).$("button=Uninstall and remove")).click();
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
  await (await app.$("button*=Library")).click();
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
  await s.settings("General");
  // By its label: TV mode's dropdown comes before it now.
  await (await app.$("//section[contains(@class, 'settings-page')]//label[contains(., 'Interface size')]/select")).selectByVisibleText("Large");
  await s.closeSettings();
  assert.equal(await app.execute(() => document.documentElement.style.zoom), "1.15");
  await until(() => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).scale === 1.15);
});
