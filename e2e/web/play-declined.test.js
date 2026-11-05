/**
 * Play on a Windows program that needs administrator rights, when the player says no at Windows' prompt.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { installed } from "./controller.js";

test("saying no to administrator rights for a program isn't an error; its failing to start is", async () => {
  const own = await openApp({ state: installed });
  try {
    await own.page.evaluate(() => {
      const { invoke } = window.__TAURI_INTERNALS__;
      const answers = ["cancelled", "Couldn't start port.exe: it broke."];
      window.__TAURI_INTERNALS__.invoke = (cmd, args) => (cmd === "launch" ? Promise.reject(answers.shift()) : invoke(cmd, args));
    });
    await own.page.locator('article[data-slug="test-port"] .card-open').first().click();
    const play = own.page.getByRole("button", { name: "Play", exact: true }).first();
    await play.click();
    await own.page.evaluate(() => new Promise((r) => setTimeout(r, 100)));
    assert.equal(await own.page.locator(".job-error").count(), 0, "no error for the no");
    await play.click();
    await own.page.locator(".job-error").first().waitFor();
  } finally {
    await own.close();
  }
});
