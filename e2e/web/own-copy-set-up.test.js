/**
 * Play on a game that needs the player's own copy (own-copy-setup.tsx), when the player has set it up themselves:
 * a copy already where the game reads it is checked and kept, and "I've already set it up" stops the asking, till
 * "Ask me again" on the app's page.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import { installed } from "./controller.js";

after(stop);

const ROM = { id: "rom", label: "Game ROM", destination: "baserom.z64", extensions: ["z64"], accepted: [{ algo: "sha1", value: "ab".repeat(20), label: "US 1.0" }] };
const withRom = (api) => api.setEntry("test-port", { launcher: { folderName: "test-port", setupFiles: [ROM] } });

/** The app, with the Rust side's launches counted, and its copy calls answered by `answers`. */
async function open(state, answers = {}) {
  const own = await openApp({ state: { ...installed, ...state }, prepare: withRom });
  await own.page.evaluate((answers) => {
    const { invoke } = window.__TAURI_INTERNALS__;
    window.launches = [];
    window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
      if (cmd === "launch") return window.launches.push(args), Promise.resolve();
      if (cmd in answers) return Promise.resolve(answers[cmd]);
      return invoke(cmd, args);
    };
  }, answers);
  await own.page.locator('article[data-slug="test-port"] .card-open').first().click();
  return own;
}
/** Play on the app's page: "Set up and play" while the game still needs its copy. */
const playButton = (page) => page.getByRole("button", { name: /^(Set up and play|Play)$/ }).first();
const launches = (own) => own.page.evaluate(() => window.launches.length);

test("I've already set it up stops Play asking for the copy, till Ask me again", async () => {
  const own = await open({});
  const { page } = own;
  try {
    const play = playButton(page);
    assert.equal(await play.innerText(), "Set up and play");
    await play.click();
    const dialog = page.getByRole("dialog", { name: "Set up your copy for Test Port" });
    await dialog.waitFor();
    await dialog.getByRole("button", { name: "I've already set it up, don't ask again" }).click();
    await own.until(async () => (await launches(own)) === 1);
    await dialog.waitFor({ state: "detached" });
    // Remembered: Play starts it at once, and the chip says the player set it up.
    assert.equal(await play.innerText(), "Play");
    await play.click();
    await own.until(async () => (await launches(own)) === 2);
    assert.equal(await dialog.count(), 0);
    const chip = page.locator(".own-copy-chip");
    assert.match(await chip.innerText(), /Set up by you/);
    assert.match(await chip.getAttribute("class"), /\bready\b/);
    // Asked for again from the chip.
    await chip.click();
    await dialog.getByText("You set it up yourself, so Quiver doesn't ask for it when you play.").waitFor();
    await dialog.getByRole("button", { name: "Ask me again" }).click();
    await play.click();
    await dialog.getByRole("button", { name: "I've already set it up, don't ask again" }).waitFor();
    assert.equal(await launches(own), 2);
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});

test("a copy the player put where the game reads it is checked and kept, and the game starts", async () => {
  const at = "/home/player/Games/Quiver/test-port/baserom.z64";
  const own = await open({ copyInPlace: at }, { check_copy: { ok: true, verified: true, dump: "US 1.0", size: 8 }, place_copy: at });
  const { page } = own;
  try {
    await playButton(page).click();
    await own.until(async () => (await launches(own)) === 1);
    assert.match(await page.locator(".own-copy-chip").innerText(), /US 1\.0/);
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});

test("one there that isn't a copy the game takes says so, and offers I've already set it up", async () => {
  const at = "/home/player/Games/Quiver/test-port/baserom.z64";
  const problem = "This isn't a copy this game takes. It may be another region or version, or a changed or damaged copy.";
  const own = await open({ copyInPlace: at }, { check_copy: { ok: false, verified: false, problem, size: 8, sha1: "cd".repeat(20) } });
  const { page } = own;
  try {
    await playButton(page).click();
    const dialog = page.getByRole("dialog", { name: "Set up your copy for Test Port" });
    await dialog.getByText(`There's already a file where Test Port reads it. ${problem}`).waitFor();
    await dialog.getByText("If Test Port plays fine with it, choose I've already set it up below, and Quiver won't ask again.").waitFor();
    await dialog.getByRole("button", { name: "I've already set it up, don't ask again" }).click();
    await own.until(async () => (await launches(own)) === 1);
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});
