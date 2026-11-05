/**
 * A keydown that names no key, which a real press never is.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { installed } from "./controller.js";

test("a keydown that names no key is ignored, even while a control waits for its key", async () => {
  const own = await openApp({ state: installed });
  try {
    await own.page.getByRole("button", { name: "Settings", exact: true }).first().click();
    await own.page.locator('nav[aria-label="Settings sections"] button', { hasText: "Controllers" }).click();
    const binding = own.page.locator('button[aria-label="Confirm / Select: keyboard"]');
    await binding.click();
    const waiting = await binding.innerText();
    // A plain event has no `key`, as the ones that crashed this had none. (Trusted ones can't be made here; this is the same path.)
    await own.page.evaluate(() => window.dispatchEvent(new Event("keydown")));
    await own.page.evaluate(() => new Promise((r) => setTimeout(r, 50)));
    assert.deepEqual(own.errors, [], "nothing threw");
    assert.equal(await binding.innerText(), waiting, "it's still waiting");
    await own.page.keyboard.press("k");
    await own.until(async () => (await binding.innerText()) !== waiting);
    assert.match(await binding.innerText(), /K/, "the real key binds");
  } finally {
    await own.close();
  }
});
