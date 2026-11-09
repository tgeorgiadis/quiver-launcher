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

test("Report a problem, by the app's actions: signing in opens its dialog, B closes it to its button, and A sends one", async () => {
  assert.equal(await appPage(), "page: Test Port");
  // Nothing about corrections on the page until the player has sent one.
  assert.equal(await page.locator(".correction-prompt").count(), 0);
  // Signed out, the button signs in first, and the dialog opens after.
  assert.equal(await open("button", { text: "Report a problem" }), "dialog: Sign in");
  const dialog = page.locator('[role="dialog"]');
  await dialog.locator('form > button[type="button"]').last().click();
  await dialog.locator('input[autocomplete="username"]').fill("corrections");
  await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
  await dialog.locator("button.primary").click();
  const form = page.locator(".correction-dialog .correction-form");
  await form.waitFor();
  // The focus in it, on its first control: the topic.
  await s.until(async () => /^select .* in Report a problem$/.test(await reach.focused(app)));
  await everything("the Report a problem dialog");
  // B closes the dialog, not the page, and the focus goes back to its button.
  await form.locator("textarea").focus();
  assert.equal(await back(), "page: Test Port");
  assert.equal(await page.locator(".correction-dialog").count(), 0);
  await s.until(async () => /Report a problem/.test(await reach.focused(app)));
  await reach.press(app, "South");
  await form.locator("select").selectOption("launch");
  await page.getByText("To say how it ran, use Player feedback.").waitFor();
  await form.locator("textarea").fill("It starts the level editor, not the game.");
  await form.getByRole("button", { name: "Send" }).click();
  await page.getByText("Thanks! A maintainer will take a look.").waitFor();
  const [sent] = s.api.corrections;
  assert.deepEqual(
    { user: sent.user, entryId: sent.entryId, topic: sent.topic, body: sent.body, source: sent.source, installed: sent.client.installed },
    { user: "corrections", entryId: "entry_test-port", topic: "launch", body: "It starts the level editor, not the game.", source: "launcher", installed: "1.0.0" },
  );
  assert.ok(sent.client.version && sent.client.os, "with the launcher's version and system");
  // Done, which has the focus, closes it back to the button; the Overview now says where the report stands.
  await s.until(async () => /Done/.test(await reach.focused(app)));
  await reach.press(app, "South");
  assert.equal(await page.locator(".correction-dialog").count(), 0);
  await s.until(async () => /Report a problem/.test(await reach.focused(app)));
  await page.locator(".correction-prompt").getByText(/You reported a problem on/).waitFor();
});

test("a decided correction comes to the top bar's announcements, for a controller to read and dismiss", async () => {
  await page.locator(".account button").click();
  const dialog = page.locator('[role="dialog"]');
  await dialog.locator('form > button[type="button"]').last().click();
  await dialog.locator('input[autocomplete="username"]').fill("notices");
  await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
  await dialog.locator("button.primary").click();
  await page.locator(".account-button").waitFor();
  // A maintainer decides one the player sent, with a reply.
  s.api.corrections.push({ id: "correction_notice", user: "notices", entryId: "entry_test-port", topic: "launch", body: "It launches the wrong file.", status: "open", createdAt: Date.now() - 86400_000 });
  s.api.resolveCorrection("correction_notice", "dismissed", "That file is the game; the other one is a level editor.");
  // In the banners over the library (one shows at a time: another may be ahead of it).
  const banner = page.locator(".banner.correction-notice");
  await banner.waitFor({ state: "attached" });
  assert.match(await banner.textContent(), /left its page as it is.*level editor/);
  assert.equal(await open(".announcements-button"), "dialog: Announcements");
  await everything("Announcements with a correction's news");
  await reach.pressOn(app, ".correction-notice .announcement-dismiss", "South");
  await s.until(() => !s.api.corrections.find((c) => c.id === "correction_notice").unseen);
  assert.equal(await page.locator(".correction-notice").count(), 0, "gone from the dialog and the banner");
});
