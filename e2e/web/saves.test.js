/**
 * An installed app's Saves tab: where its saves are, its backups, and what Back up now, Restore, Remove and Choose
 * folder ask of the launcher. And when backups are made by themselves: around playing, and before uninstalling.
 * Making and restoring the backups themselves is saves.rs's, with its own tests.
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
const place = { template: "app/saves", path: "/apps/test-port/saves", exists: true };
const backup = (name, reason) => ({ name: `${name}.zip`, at: name, size: 2048, files: 3, version: "1.0.0", reason, device: "pc" });
let s, page;

/** The launcher's answers about the app's saves. A command in `fails` is refused with those words. */
const savesAre = (info, fails = {}) =>
  page.evaluate(
    ([info, fails]) => {
      const { invoke } = window.__TAURI_INTERNALS__;
      window.__picked = "/games/saves";
      window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
        if (!cmd.startsWith("saves_") && cmd !== "pick_folder") return invoke(cmd, args);
        window.__calls.push({ cmd, args });
        if (fails[cmd]) return Promise.reject(fails[cmd]);
        if (cmd === "saves_info" || cmd === "saves_choose") return Promise.resolve(info);
        if (cmd === "saves_backup") return Promise.resolve(info.backups[0] ?? null);
        if (cmd === "saves_restore") return Promise.resolve(3);
        if (cmd === "pick_folder") return Promise.resolve(window.__picked);
        return Promise.resolve(null);
      };
    },
    [info, fails],
  );
const calls = (cmd) => page.evaluate((cmd) => window.__calls.filter((c) => c.cmd === cmd).map((c) => c.args), cmd);

async function openSaves() {
  await page.locator("article[data-slug] .card-open").first().click();
  await page.getByRole("tab", { name: "Saves" }).click();
  const panel = page.locator(".saves-panel");
  await panel.waitFor();
  return panel;
}

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

test("an app whose saves aren't known yet says so, and has no backups", async () => {
  const panel = await openSaves();
  await panel.getByText("Quiver doesn't know where this app saves yet. Play it once, or choose the folder.").waitFor();
  await panel.getByText("No backups yet.").waitFor();
  assert.equal(await panel.locator(".save-row").count(), 0);
});

test("it lists where the saves are and the backups, newest first, and backs up when asked", async () => {
  await savesAre({ places: [place], from: "found", backups: [backup(2000, "played"), backup(1000, "update")] });
  const panel = await openSaves();
  await panel.getByText("/apps/test-port/saves").waitFor();
  await panel.getByText("Quiver saw the app save here.").waitFor();
  const rows = panel.locator(".save-row .save-note");
  assert.match(await rows.nth(0).innerText(), /^After playing · 3 files · 2 KB · Version 1\.0\.0$/);
  assert.match(await rows.nth(1).innerText(), /^Before an update/);
  await panel.getByRole("button", { name: "Back up now" }).click();
  await panel.getByText("Backed up.").waitFor();
  const [args] = await calls("saves_backup");
  assert.equal(args.reason, "manual");
  assert.deepEqual({ id: args.of.id, folder: args.of.folder, version: args.of.version }, { id: ID, folder: "test-port", version: "1.0.0" });
  await panel.getByRole("button", { name: "Open folder" }).click();
  assert.equal((await calls("saves_open"))[0].template, "app/saves");
});

test("restoring asks first, then says it's done", async () => {
  await savesAre({ places: [place], from: "catalog", backups: [backup(2000, "manual")] });
  const panel = await openSaves();
  await panel.getByText("From Quiver's catalog.").waitFor();
  await panel.getByRole("button", { name: "Restore" }).click();
  await panel.getByText("Restore this backup? Your saves now are backed up first.").waitFor();
  assert.equal((await calls("saves_restore")).length, 0, "not before the player says so");
  await panel.getByRole("button", { name: "Cancel" }).click();
  await panel.getByRole("button", { name: "Restore" }).click();
  await panel.locator(".save-button.primary", { hasText: "Restore" }).click();
  await panel.getByText("Restored.").waitFor();
  assert.equal((await calls("saves_restore"))[0].name, "2000.zip");
  await panel.getByRole("button", { name: "Remove this backup" }).click();
  await page.waitForFunction(() => window.__calls.some((c) => c.cmd === "saves_remove"));
  assert.deepEqual((await calls("saves_remove"))[0], { id: ID, name: "2000.zip" });
});

test("what the launcher refuses is said in the tab", async () => {
  await savesAre({ places: [place], from: "found", backups: [backup(2000, "played")] }, { saves_backup: "Close the app first." });
  const panel = await openSaves();
  await panel.getByRole("button", { name: "Back up now" }).click();
  await panel.locator(".save-said.error", { hasText: "Close the app first." }).waitFor();
});

test("the player can choose the folder, and go back to Quiver's choice", async () => {
  await savesAre({ places: [{ template: "abs:/games/saves", path: "/games/saves", exists: true }], from: "you", backups: [] });
  const panel = await openSaves();
  await panel.getByText("You chose this folder.").waitFor();
  await panel.getByRole("button", { name: "Choose folder" }).click();
  await page.waitForFunction(() => window.__calls.some((c) => c.cmd === "saves_choose"));
  assert.equal((await calls("saves_choose"))[0].path, "/games/saves");
  await panel.getByRole("button", { name: "Use Quiver's choice" }).click();
  await page.waitForFunction(() => window.__calls.filter((c) => c.cmd === "saves_choose").length === 2);
  assert.equal((await calls("saves_choose"))[1].path, undefined);
});

test("playing notes the save places first, and closing backs the saves up", async () => {
  await savesAre({ places: [place], from: "found", backups: [backup(2000, "played")] });
  await page.locator("article[data-slug] .card-open").first().click();
  await page.getByRole("button", { name: "Play", exact: true }).first().click();
  await page.waitForFunction(() => window.__calls.some((c) => c.cmd === "launch"));
  const order = await page.evaluate(() => window.__calls.map((c) => c.cmd).filter((c) => c === "saves_watch" || c === "launch"));
  assert.deepEqual(order, ["saves_watch", "launch"]);
  const [watched] = await calls("saves_watch");
  assert.ok(watched.of.names.includes("test-port"));
  await s.emit("app-closed", { id: ID, seconds: 5 });
  await page.waitForFunction(() => window.__calls.some((c) => c.cmd === "saves_played"));
  assert.equal((await calls("saves_played"))[0].of.id, ID);
});

test("uninstalling backs the saves up first", async () => {
  await savesAre({ places: [place], from: "found", backups: [] });
  await page.locator("article[data-slug]").first().click({ button: "right" });
  await page.locator('[role="menu"] [role="menuitem"]', { hasText: "Manage" }).first().click();
  await page.locator('.game-submenu [role="menuitem"]', { hasText: /^Uninstall$/ }).click();
  await page.locator(".uninstall-prompt").getByRole("button", { name: /Uninstall/ }).click();
  await page.waitForFunction(() => window.__calls.some((c) => c.cmd === "uninstall"));
  const order = await page.evaluate(() => window.__calls.map((c) => c.cmd).filter((c) => c === "saves_backup" || c === "uninstall"));
  assert.deepEqual(order, ["saves_backup", "uninstall"]);
  assert.equal((await calls("saves_backup"))[0].reason, "uninstall");
});
