/**
 * A game that closes with an error right after starting, or crashes: Quiver says so, shows what the game said last,
 * and offers to send Quiver a report where error reports are on.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { installed } from "./controller.js";

const LOG = "Loading assets\nusage: port ROM";

/** The app with the game's log as the Rust side would read it. */
async function open(log = LOG) {
  const own = await openApp({ state: installed });
  await own.page.evaluate((log) => {
    const { invoke } = window.__TAURI_INTERNALS__;
    window.__TAURI_INTERNALS__.invoke = (cmd, args) => (cmd === "game_log" ? Promise.resolve(log) : invoke(cmd, args));
  }, log);
  return own;
}
const id = Object.keys(installed.installs)[0];
const folder = installed.installs[id].folder;

test("a game that closes with an error right after starting is said, with what it said", async () => {
  const own = await open();
  try {
    await own.emit("app-failed", { id, folder, kind: "early", code: 2, what: null, line: "usage: port ROM" });
    const dialog = own.page.getByRole("dialog", { name: "Test Port closed right after starting" });
    await dialog.waitFor();
    await dialog.getByText("It stopped with an error (exit code 2) before it got going.").waitFor();
    assert.equal(await dialog.locator("pre").textContent(), LOG);
    // No error reports from a test build, so nothing to send; and nothing ever goes to the app's developer from here.
    assert.equal(await dialog.getByRole("button", { name: "Send report" }).count(), 0);
    assert.equal(await dialog.getByRole("button", { name: /Report on/ }).count(), 0);
    await dialog.getByRole("button", { name: "Copy details" }).waitFor();
    assert.equal(await own.page.evaluate(() => window.__calls.filter((c) => c.cmd === "open_url").length), 0);
    await dialog.getByRole("button", { name: "Close" }).click();
    await dialog.waitFor({ state: "detached" });
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});

test("a crash is said as one, and a game that isn't in the library says nothing", async () => {
  const own = await open();
  try {
    await own.emit("app-failed", { id: "not-in-the-library", folder: "Gone", kind: "crashed", code: null, what: "segmentation fault", line: null });
    await own.page.evaluate(() => new Promise((r) => setTimeout(r, 200)));
    assert.equal(await own.page.getByRole("dialog").count(), 0);
    await own.emit("app-failed", { id, folder, kind: "crashed", code: null, what: "segmentation fault", line: null });
    const dialog = own.page.getByRole("dialog", { name: "Test Port crashed" });
    await dialog.waitFor();
    await dialog.getByText("It stopped by itself while it was running.").waitFor();
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});

test("a game that asked for a key before closing shows its message, with no report to send", async () => {
  const own = await open("Put your game's ISO beside PLAY.bat as game.iso.\r\nPress any key to continue . . . \r\n");
  try {
    await own.emit("app-failed", { id, folder, kind: "early", code: 1, what: null, line: "Press any key to continue . . ." });
    const dialog = own.page.getByRole("dialog", { name: "Test Port closed with a message" });
    await dialog.waitFor();
    await dialog.getByText("It closed right after starting. This is what it said:").waitFor();
    assert.equal(await dialog.locator("pre").textContent(), "Put your game's ISO beside PLAY.bat as game.iso.");
    assert.equal(await dialog.getByText(/stopped with an error/).count(), 0);
    assert.equal(await dialog.getByRole("button", { name: "Send report" }).count(), 0);
    await dialog.getByRole("button", { name: "Copy details" }).waitFor();
    assert.deepEqual(own.errors, []);
  } finally {
    await own.close();
  }
});
