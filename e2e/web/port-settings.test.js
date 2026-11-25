/**
 * An installed port whose own setup file lists settings: a Settings tab with its rows, grouped as the file groups
 * them, advanced ones behind a button, and a change sent to the launcher to write. Reading and writing the port's
 * settings files is port_config.rs's, with its own tests; here the launcher's answers stand in.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const ID = "entry_test-port";
const state = {
  settings: {},
  library: [{ id: ID, slug: "test-port", addedAt: 1 }],
  installs: { [ID]: { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: 1000, playTime: 60 } },
};
const row = (id, label, more) => ({ id, label, help: null, group: null, advanced: false, enabled: true, needs: null, commandLine: false, choices: null, chosen: null, other: null, slider: null, ...more });
const VIEW = {
  settings: [
    row("fullscreen", "Fullscreen", { group: "Video", help: "Fill the screen.", choices: ["Off", "On"], chosen: 1 }),
    row("size", "Window size", { group: "Video", enabled: false, needs: ["Fullscreen", "Off"], choices: ["720p", "1080p"], chosen: null, other: "1600x900" }),
    row("sens", "Mouse sensitivity", { group: "Controls", slider: { min: 0.1, max: 5, step: 0.05, value: 1, text: "1.00" } }),
    row("log", "Logging", { group: "Debug", advanced: true, commandLine: true, choices: ["Game's option", "Verbose"], chosen: 0 }),
  ],
  unreadable: [],
};
let s, page;

/** The launcher's answers: `view` for the tab, and a change sets the chosen value. */
const settingsAre = (view) =>
  page.evaluate((view) => {
    const { invoke } = window.__TAURI_INTERNALS__;
    window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
      if (cmd !== "port_settings" && cmd !== "set_port_setting") return invoke(cmd, args);
      window.__calls.push({ cmd, args });
      if (cmd === "set_port_setting") {
        const s = view.settings.find((s) => s.id === args.id);
        if ("choice" in args.pick) s.chosen = args.pick.choice;
        else s.slider.value = args.pick.value;
      }
      // A new answer each time, as from the Rust side.
      return Promise.resolve(structuredClone(view));
    };
  }, view);
const calls = (cmd) => page.evaluate((cmd) => window.__calls.filter((c) => c.cmd === cmd).map((c) => c.args), cmd);
const openPage = () => page.locator("article[data-slug] .card-open").first().click();

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await page.locator("article[data-slug]").first().waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

test("a port without settings has no Settings tab", async () => {
  await settingsAre(null);
  await openPage();
  await page.getByRole("tab", { name: "Manage" }).waitFor();
  assert.equal(await page.getByRole("tab", { name: "Settings" }).count(), 0);
  assert.deepEqual(s.errors, []);
});

test("a port's settings show in groups, greyed out where they don't apply, with advanced ones hidden", async () => {
  await settingsAre(structuredClone(VIEW));
  await openPage();
  await page.getByRole("tab", { name: "Settings" }).click();
  const panel = page.locator(".port-settings");
  await panel.waitFor();
  assert.deepEqual(await panel.locator(".port-settings-group").allInnerTexts(), ["Video", "Controls"]);
  assert.equal(await panel.getByLabel("Fullscreen").inputValue(), "1");
  await panel.getByText("Fill the screen.").waitFor();
  const size = panel.getByLabel("Window size");
  assert.equal(await size.isDisabled(), true);
  assert.equal(await size.locator("option:checked").innerText(), "Something else (1600x900)");
  await panel.getByText("Only used when Fullscreen is Off.").waitFor();
  assert.equal(await panel.locator(".port-slider output").innerText(), "1.00");
  assert.equal(await panel.getByLabel("Logging").count(), 0);
  await panel.getByRole("button", { name: "Show advanced options" }).click();
  await panel.getByLabel("Logging").waitFor();
  await panel.getByText("Used when you press Play.").waitFor();
  assert.deepEqual(await panel.locator(".port-settings-group").allInnerTexts(), ["Video", "Controls", "Debug"]);
  const asked = await calls("port_settings");
  assert.deepEqual({ folder: asked[0].folder, wine: asked[0].wine }, { folder: "test-port", wine: false });
  assert.deepEqual(s.errors, []);
});

test("a change is sent to the launcher, and a slider once it stops", async () => {
  await settingsAre(structuredClone(VIEW));
  await openPage();
  await page.getByRole("tab", { name: "Settings" }).click();
  const panel = page.locator(".port-settings");
  await panel.getByLabel("Fullscreen").selectOption("0");
  await panel.locator(".port-slider input").fill("2.5");
  await page.waitForFunction(() => window.__calls.filter((c) => c.cmd === "set_port_setting").length === 2);
  const set = await calls("set_port_setting");
  assert.deepEqual(set.map((c) => [c.id, c.pick]), [["fullscreen", { choice: 0 }], ["sens", { value: 2.5 }]]);
  assert.equal(await panel.getByLabel("Fullscreen").inputValue(), "0");
  assert.deepEqual(s.errors, []);
});
