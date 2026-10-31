/**
 * Browse leaves out apps already in the library, as Quiver Launcher 3 does:
 * one just added stays with its "In library" badge, a note says how many are
 * left out, and they can be shown again (kept on this computer) or hidden.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const settings = () => JSON.parse(readFileSync(join(data, "settings.json"), "utf8"));

test("apps in the library stay out of Browse until the player shows them", async () => {
  let s = await launch({ data });
  try {
    const game = await s.card("test-port");
    await game.waitForDisplayed({ timeout: 20000 });
    await (await game.$("button=Get")).click();
    await (await game.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
    // Just added: still here, with its badge.
    assert.equal((await (await game.$(".cover-badge")).getText()).toLowerCase(), "in library");
  } finally {
    await s.close();
  }

  s = await launch({ data });
  try {
    // With apps in the library, the launcher opens on it.
    const browse = await s.app.$("button=Browse");
    await browse.waitForDisplayed({ timeout: 20000 });
    await browse.click();
    await (await s.card("tampered-port")).waitForDisplayed({ timeout: 20000 });
    assert.equal(await (await s.card("test-port")).isExisting(), false);
    const note = await s.app.$(".owned-note");
    assert.equal(await note.getText(), "1 app here is already in your library. Show it");
    await (await note.$("button=Show it")).click();
    await (await s.card("test-port")).waitForDisplayed();
    await s.until(() => settings().browseShowsLibrary === true);
    const shown = await s.app.$(".owned-note");
    assert.equal(await shown.getText(), "Apps in your library are shown here too. Hide them");
    await (await shown.$("button=Hide them")).click();
    await s.until(async () => !(await (await s.card("test-port")).isExisting()));
    await s.until(() => settings().browseShowsLibrary === undefined);
  } finally {
    await s.close();
  }
});
