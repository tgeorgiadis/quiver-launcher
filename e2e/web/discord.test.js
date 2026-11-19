/**
 * Discord's Rich Presence (discord.rs): what the page tells the Rust side, which talks to Discord.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { installed } from "./controller.js";

const calls = (page, cmd) => page.evaluate((cmd) => window.__calls.filter((c) => c.cmd === cmd).map((c) => c.args), cmd);

test("Discord shows the game played by its name, and Settings turns it off", async () => {
  const own = await openApp({ state: installed });
  try {
    await own.until(async () => (await calls(own.page, "set_discord_presence")).length > 0);
    assert.deepEqual((await calls(own.page, "set_discord_presence")).at(-1), { enabled: true, texts: { idle: "Browsing games", button: "Get Quiver Launcher" } });

    await own.page.locator('article[data-slug="test-port"] .card-open').first().click();
    await own.page.getByRole("button", { name: "Play", exact: true }).first().click();
    await own.until(async () => (await calls(own.page, "launch")).length > 0);
    assert.equal((await calls(own.page, "launch"))[0].presence.name, "Test Port");

    await own.page.locator(".topbar button[aria-label='Settings']").click();
    const toggle = own.page.getByLabel("Show on Discord what I'm playing, or that I'm in Quiver Launcher");
    assert.equal(await toggle.isChecked(), true);
    await toggle.uncheck();
    await own.until(async () => (await calls(own.page, "set_discord_presence")).at(-1).enabled === false);
    assert.equal((await own.page.evaluate(() => window.__files.settings)).discordPresenceOff, true);
  } finally {
    await own.close();
  }
});
