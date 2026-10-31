/**
 * Steam's on-screen keyboard: under Steam or Gamescope, a text field brings it
 * up only while it's being edited (Confirm on a highlighted field, as in
 * Quiver Launcher 3), and leaving the field hides it again.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const bin = mkdtempSync(join(tmpdir(), "quiver-bin-"));
const log = join(mkdtempSync(join(tmpdir(), "quiver-opened-")), "log");
let s;

before(async () => {
  // Stands in for xdg-open, which hands steam:// links to Steam.
  writeFileSync(join(bin, "xdg-open"), `#!/bin/sh\necho "$1" >> "${log}"\n`, { mode: 0o755 });
  s = await launch({ env: { SteamDeck: "1", PATH: `${bin}:${process.env.PATH}` } });
});

after(() => s?.close());

const asked = () => (existsSync(log) ? readFileSync(log, "utf8").trim().split("\n") : []);
/** A controller button, pressed and let go, as the Rust side reports it. */
const pad = async (key) => {
  for (const down of [true, false])
    await s.app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
};
const focused = () => s.app.execute(() => document.activeElement?.getAttribute("placeholder") ?? document.activeElement?.closest("article")?.dataset.slug);
const pause = (ms) => new Promise((r) => setTimeout(r, ms));

test("Confirm on a text field brings up Steam's keyboard, and leaving the field hides it", async () => {
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  // Highlighted (focused) but not being edited: no keyboard yet.
  await s.app.execute(() => document.querySelector("input[placeholder^=Search]").focus());
  assert.match(await focused(), /^Search/);
  await pause(1000);
  assert.deepEqual(asked(), []);

  await pad("South");
  await s.until(() => asked().includes("steam://open/keyboard"));
  assert.match(await focused(), /^Search/, "the field keeps focus while it's edited");

  // Down, out of the field (into the first shelf, its See all).
  await pad("DPadDown");
  await s.until(() => s.app.execute(() => !document.activeElement?.matches("input[placeholder^=Search]")));
  await s.until(() => asked().includes("steam://close/keyboard"));
  assert.deepEqual(asked(), ["steam://open/keyboard", "steam://close/keyboard"]);
});

test("a mouse click in a text field doesn't ask for the keyboard", async () => {
  await (await s.app.$("input[placeholder^=Search]")).click();
  await (await s.app.$("input[placeholder^=Search]")).setValue("test");
  await pause(1000);
  assert.equal(asked().length, 2);
});

test("Steam's keyboard taking the window's focus doesn't hide it, and A asks for it again once it's closed", async () => {
  const before = asked().length;
  const field = "input[placeholder^=Search]";
  await s.app.execute((q) => document.querySelector(q).focus(), field);
  await pad("South");
  await s.until(() => asked().length === before + 1);
  // As the window losing focus to Steam's keyboard in Game Mode does: a blur, the field still the page's focus.
  await s.app.execute((q) => document.querySelector(q).dispatchEvent(new FocusEvent("blur")), field);
  await pause(1000);
  assert.deepEqual(asked().slice(before), ["steam://open/keyboard"]);
  // Closed in Steam, the field still focused: A brings it back.
  await pad("South");
  await s.until(() => asked().length === before + 2);
  assert.deepEqual(asked().slice(before), ["steam://open/keyboard", "steam://open/keyboard"]);
});

test("while Steam's keyboard is up, a dialog keeps above it, and the room goes once its field is left", async () => {
  const room = () => s.app.execute(() => document.documentElement.style.getPropertyValue("--steam-keyboard"));
  await (await s.app.$("button*=Library")).click();
  await (await s.app.$("button*=Add a shared playlist")).click();
  const field = "input[aria-label='Shared playlist link']";
  await (await s.app.$(field)).waitForDisplayed();
  await s.app.execute((q) => document.querySelector(q).focus(), field);
  await pad("South");
  await s.until(async () => (await room()) !== "");
  // The dialog ends above the part of the window the keyboard covers.
  await s.until(() =>
    s.app.execute(() => document.querySelector("[role=dialog]").getBoundingClientRect().bottom <= document.documentElement.clientHeight * 0.55 + 1),
  );
  await pad("East");
  await s.until(async () => !(await s.app.$("[role=dialog]").isExisting()));
  await s.until(async () => (await room()) === "");
});
