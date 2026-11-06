/**
 * An installed app's page with a controller: its sections and dialogs, and where the focus goes on it. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { app, appPage, back, everything, installed, open, page, s, section, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("Play finding no program in an app's folder lets the player choose it, and starts it", async () => {
  const own = await openApp({ state: installed });
  try {
    await own.page.evaluate(() => {
      const { invoke } = window.__TAURI_INTERNALS__;
      let failed = false;
      window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
        if (cmd === "launch" && !failed) {
          failed = true;
          return Promise.reject("Couldn't find a program to start in this app's folder.");
        }
        if (cmd === "file_kinds") return Promise.resolve(["bat", "pak"]);
        if (cmd === "pick_executable") return Promise.resolve("bin/start.bat");
        return invoke(cmd, args);
      };
    });
    await own.page.locator('article[data-slug="test-port"] .card-open').first().click();
    await own.page.getByRole("button", { name: "Play", exact: true }).first().click();
    const choose = own.page.locator(".job-error").getByRole("button", { name: "Choose its program…" });
    await choose.click();
    const started = () => own.app.execute(() => window.__calls.some((c) => c.cmd === "launch" && c.args.launch?.executable === "bin/start.bat"));
    await own.until(started);
    assert.equal(await own.page.locator(".job-error").count(), 0, "the error is gone");
  } finally {
    await own.close();
  }
});

test("an app's page and its dialogs", async () => {
  assert.equal(await appPage(), "page: Test Port");
  await everything("the app's page");
  await section("Media");
  await everything("its Media");
  // Its setup, Mods among it, on its own tab.
  await section("On this computer");
  await everything("its On this computer tab");
  assert.equal(await open("button", { text: "Mods" }), "dialog: Mods for Test Port");
  await everything("Mods");
  assert.equal(await back(), "page: Test Port");
  await section("Overview");
  assert.equal(await open("button", { text: "Uninstall" }), "dialog: Uninstall Test Port");
  assert.match(await reach.focused(app), /Cancel/, "Uninstall opens on Cancel");
  await everything("Uninstall");
  assert.equal(await back(), "page: Test Port");
  assert.equal(await open("button", { text: "Test Port (original)" }), "page: Test Port (original)");
  await everything("the game's page");
  assert.equal(await back(), "page: Test Port");
  assert.equal(await back(), "main");
});

test("Up goes through a long page to its top before the top bar", async () => {
  await appPage();
  await section("Player feedback");
  await everything("its Player feedback");
  // From the bottom, Up alone.
  for (let i = 0; i < 30; i++) await reach.press(app, "DPadDown");
  const ups = [];
  for (let i = 0; i < 40; i++) {
    await reach.press(app, "DPadUp");
    ups.push(await reach.focused(app));
    if (await app.execute(() => Boolean(document.activeElement?.closest(".topbar")))) break;
  }
  assert.ok(ups.includes('button "Library" in Test Port'), `up gets to the page's Library link: ${ups.join(", ")}`);
  assert.ok(await app.execute(() => Boolean(document.activeElement?.closest(".topbar"))), "and then the top bar");
  await s.until(async () => (await app.execute(() => document.querySelector(".shell > main").scrollTop)) === 0);
  await section("Releases");
  await everything("its Releases");
});

test("a page opened with A has the focus in it, and a button that changes as it's pressed keeps it near", async () => {
  assert.equal(await appPage(), "page: Test Port");
  assert.equal(await reach.focused(app), 'button "Library" in Test Port');
  // The app's setup is on its On this computer tab.
  await page.locator("[aria-label=\"App sections\"] button", { hasText: "On this computer" }).click();
  await open("button", { text: "Change version" });
  assert.doesNotMatch(await reach.focused(app), /^body/);
});
