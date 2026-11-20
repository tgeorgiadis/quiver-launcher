/**
 * Settings › Apps folder, with new apps in a folder for their console: each console folder named another way or put
 * anywhere (console-folders.tsx), the apps already in it following when the player says so, and a new install going
 * where the player put its console's folder.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";
import * as reach from "../reach.js";

const APPS = "/home/player/Games/Quiver";
const N64 = `${APPS}/Ports/N64`;
const state = (more = {}) => ({
  settings: { groupByConsole: true, ...more },
  library: [
    { id: "entry_test-port", slug: "test-port", addedAt: Date.now() - 86400_000 },
    { id: "entry_tampered-port", slug: "tampered-port", addedAt: Date.now() - 86400_000 },
  ],
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", dir: `${N64}/test-port`, inApps: true, group: "Ports/N64", executables: [] } },
});

after(stop);

/** Settings › Apps folder, its console folders open. */
async function consoleFolders(page) {
  await page.locator(".topbar button[aria-label='Settings']").click();
  await page.locator('.settings-nav [data-section="apps"]').click();
  const section = page.locator("details.console-folders");
  await section.locator("summary").click();
  return section;
}

const saved = (page) => page.evaluate(() => window.__files.settings.consoleFolders ?? null);

test("a system folder put on another drive: saved whole, and its apps follow when asked", async () => {
  const s = await openApp({ state: state() });
  const { page } = s;
  try {
    const section = await consoleFolders(page);
    // Only consoles the player's installed apps are for: Nintendo 64's, with its app, its usual place shown till
    // another is typed.
    const box = section.getByRole("textbox", { name: "Nintendo 64", exact: true });
    assert.equal(await box.getAttribute("placeholder"), N64);
    assert.match(await section.locator(".console-folder", { hasText: "Nintendo 64" }).innerText(), /1 app/);
    assert.equal(await section.locator(".console-folder").count(), 1);
    // A controller gets to Browse… (which asks the system's picker) and its box.
    await reach.install(s.app);
    assert.doesNotMatch(await reach.pressOn(s.app, ".console-folder button", "South", { text: "Browse…" }), /^not reached/);
    await s.until(() => page.evaluate(() => window.__calls.some((c) => c.cmd === "pick_folder")));
    assert.doesNotMatch(await reach.pressOn(s.app, ".console-folder input", "South"), /^not reached/);

    // A name alone isn't a place: said so, and nothing saved.
    await box.fill("N64 games");
    await box.press("Enter");
    await section.getByText("Type the folder's whole path").waitFor();
    assert.equal(await saved(page), null);

    await box.fill("/mnt/card/N64/");
    await box.press("Enter");
    await s.until(async () => JSON.stringify(await saved(page)) === JSON.stringify({ "port:n64": "/mnt/card/N64" }));
    const left = section.locator(".console-folder-left");
    await left.getByText(`1 app is still in ${N64}.`).waitFor();
    await left.getByRole("button", { name: "Move it there" }).click();
    await section.getByText("Moved 1 app.").waitFor();
    const move = await page.evaluate(() => window.__calls.find((c) => c.cmd === "move_app").args);
    assert.equal(move.to, "/mnt/card/N64");
    // Elsewhere, it's the player's folder for those apps alone: moving the apps folder leaves it be.
    assert.equal(move.inApps, false);
    assert.equal(move.group, null);
    const install = await page.evaluate(() => window.__files.installs["entry_test-port"]);
    assert.equal(install.dir, "/mnt/card/N64\\test-port");
    assert.equal(install.inApps, undefined);
    assert.equal(install.group, undefined);
    // The old folder goes once it's empty.
    assert.ok(await page.evaluate((from) => window.__calls.some((c) => c.cmd === "remove_empty_console_folder" && c.args.path === from), N64));

    // Reset: back to its usual place, and its app can come back too.
    await section.getByRole("button", { name: "Put Nintendo 64's folder back in the usual place" }).click();
    await s.until(async () => (await saved(page)) === null);
    await left.getByText("1 app is still in /mnt/card/N64.").waitFor();
    await left.getByRole("button", { name: "Leave them" }).click();
    await left.waitFor({ state: "detached" });
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("a system folder renamed in the apps folder is saved as its place there, and never in an app's own folder", async () => {
  const s = await openApp({ state: state() });
  const { page } = s;
  try {
    const section = await consoleFolders(page);
    const box = section.getByRole("textbox", { name: "Nintendo 64", exact: true });
    await box.fill(`${N64}/test-port/N64`);
    await box.press("Enter");
    await section.getByText("an app's own folder").waitFor();
    assert.equal(await box.getAttribute("aria-invalid"), "true");
    await box.fill(APPS);
    await box.press("Enter");
    await section.getByText("That's the apps folder itself").waitFor();

    await box.fill(`${APPS}/Nintendo 64`);
    await box.press("Enter");
    await s.until(async () => (await saved(page))?.["port:n64"] === "Nintendo 64");
    await section.locator(".console-folder-left").getByRole("button", { name: "Move it there" }).click();
    await section.getByText("Moved 1 app.").waitFor();
    const move = await page.evaluate(() => window.__calls.find((c) => c.cmd === "move_app").args);
    // In the apps folder, it goes with it when that moves.
    assert.equal(move.to, `${APPS}/Nintendo 64`);
    assert.equal(move.inApps, true);
    assert.equal(move.group, "Nintendo 64");
    assert.equal(await page.evaluate(() => window.__files.installs["entry_test-port"].group), "Nintendo 64");
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("another system's folder set before any of its apps is installed, and a new install goes there", async () => {
  const s = await openApp({ state: state() });
  const { page } = s;
  try {
    const section = await consoleFolders(page);
    await section.locator("select.console-folder-add").selectOption({ label: "Super Nintendo Entertainment System" });
    const box = section.getByRole("textbox", { name: "Super Nintendo Entertainment System", exact: true });
    await s.until(() => box.evaluate((b) => b === document.activeElement));
    await box.fill("/mnt/card/SNES");
    await box.press("Enter");
    await s.until(async () => (await saved(page))?.["port:snes"] === "/mnt/card/SNES");
    // Nothing was in it: nothing to move.
    assert.equal(await section.locator(".console-folder-left").count(), 0);

    await page.locator(".topbar button[aria-label='Settings']").click();
    await page.keyboard.press("Escape");
    await page.locator('article[data-slug="tampered-port"]').click({ button: "right" });
    await page.locator('[role="menu"] [role="menuitem"]', { hasText: /^Install/ }).first().click();
    await s.until(() => page.evaluate(() => window.__calls.some((c) => c.cmd === "install")));
    const request = await page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request);
    assert.equal(request.into, "/mnt/card/SNES");
    await page.evaluate(() => window.__installs.get("entry_tampered-port").resolve({ dir: "/mnt/card/SNES/tampered-port", version: "1.0.0" }));
    await s.until(() => page.evaluate(() => window.__files.installs["entry_tampered-port"]?.dir === "/mnt/card/SNES/tampered-port"));
    const install = await page.evaluate(() => window.__files.installs["entry_tampered-port"]);
    assert.equal(install.inApps, undefined);
    assert.equal(install.group, undefined);
  } finally {
    await s.close();
  }
});

test("system folders named in full: an app already in one by abbreviation can move into its new name", async () => {
  const s = await openApp({ state: state() });
  const { page } = s;
  try {
    await consoleFolders(page);
    const naming = page.getByLabel("Name system folders");
    assert.equal(await naming.inputValue(), "short");
    assert.equal(await page.locator(".console-folder-path").first().getAttribute("placeholder"), N64);
    await naming.selectOption("full");
    await s.until(() => page.evaluate(() => window.__files.settings.systemFolderNames === "full"));
    assert.equal(await page.locator(".console-folder-path").first().getAttribute("placeholder"), `${APPS}/Ports/Nintendo 64`);
    await page.getByRole("button", { name: "Move 1 installed app into its system folder" }).click();
    await page.getByText("Moved 1 app into system folders.").waitFor();
    const move = await page.evaluate(() => window.__calls.find((c) => c.cmd === "move_app").args);
    assert.equal(move.to, `${APPS}/Ports/Nintendo 64`);
    assert.equal(move.group, "Ports/Nintendo 64");
    // Back to abbreviations: Nintendo 64's app can go back too.
    await naming.selectOption("short");
    await page.getByRole("button", { name: "Move 1 installed app into its system folder" }).waitFor();
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
