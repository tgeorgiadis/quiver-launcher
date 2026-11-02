/**
 * Installation directories for consoles (Settings › Apps folder): a console's
 * folder typed or browsed for, kept only as a whole path outside every
 * installed app's folder, and a new install of an app for that console going
 * in a folder of its own in it. In the launcher's web side on its own
 * (app.js); the Rust side's checks are install.rs's and apps_folder.rs's tests.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

// Test Port (N64) installed in the apps folder; Test Remake (N64) in the library, not installed.
const state = {
  library: ["test-port", "test-remake"].map((slug, i) => ({ id: `entry_${slug}`, slug, addedAt: Date.now() - i * 60_000 })),
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000 } },
};
let s, page;

before(async () => {
  s = await openApp({ state });
  page = s.page;
  // What the stand-in doesn't answer: the folder picker (the folder a test sets), and the Rust side's check of a
  // console's folder, as apps_folder.rs makes it: a whole path, not in an installed app's folder.
  await page.evaluate((appsDir) => {
    const invoke = window.__TAURI_INTERNALS__.invoke;
    window.__TAURI_INTERNALS__.invoke = async (cmd, args) => {
      if (cmd === "pick_folder") return window.__picked ?? null;
      if (cmd === "console_folder_refusal") {
        window.__calls.push({ cmd, args });
        if (!args.to.startsWith("/")) return `${args.to} isn't a whole path.`;
        const inside = [...args.folders.map((f) => `${appsDir}/${f}`), ...args.dirs].find((a) => args.to === a || args.to.startsWith(`${a}/`));
        return inside ? `That folder is in ${inside.split("/").pop()}'s folder.` : null;
      }
      return invoke(cmd, args);
    };
  }, "/home/player/Games/Quiver");
});

after(async () => {
  await s?.close();
  await stop();
});

const section = () => page.locator(".console-folders");
const box = (name) => section().getByLabel(name, { exact: true });
const saved = () => page.evaluate(() => window.__files.settings.consoleFolders ?? {});
/** A folder typed in a console's box, and Enter pressed. */
const type = async (name, text) => {
  await box(name).fill(text);
  await box(name).press("Enter");
};
const until = (check) => s.until(check);

test("Settings: a console's folder typed, browsed for, refused, or emptied", async () => {
  await page.getByRole("button", { name: "Settings" }).click();
  await page.getByRole("button", { name: "Apps folder" }).click();
  // Folded away until opened, then each maker too.
  assert.equal(await section().locator(".console-makers").count(), 0);
  await section().getByRole("button", { name: /Installation directories for consoles/ }).click();
  await section().getByRole("button", { name: "Nintendo" }).click();
  assert.equal(await box("Nintendo 64").getAttribute("placeholder"), "Apps folder");

  // Typed: kept without the separator at its end.
  await type("Nintendo 64", "/home/player/Games/N64/");
  await until(async () => (await saved()).n64 === "/home/player/Games/N64");
  // Browsed for: the picker's folder, in the box too.
  await page.evaluate(() => (window.__picked = "/home/player/Games/SNES"));
  await section().getByRole("button", { name: "Browse for a folder for Super Nintendo Entertainment System" }).click();
  await until(async () => (await saved()).snes === "/home/player/Games/SNES");
  assert.equal(await box("Super Nintendo Entertainment System").inputValue(), "/home/player/Games/SNES");
  assert.match(await section().getByRole("button", { name: /Installation directories for consoles/ }).innerText(), /2 folders/);

  // Not a whole path, or in Test Port's folder: said so by the box, which keeps what was typed; the folder before stays.
  for (const [typed, problem] of [
    ["~/Games/SNES", /isn't a whole path/],
    ["/home/player/Games/Quiver/test-port/SNES", /test-port's folder/],
  ]) {
    await type("Super Nintendo Entertainment System", typed);
    await section().locator(".console-folder-problem", { hasText: problem }).waitFor();
    assert.equal(await box("Super Nintendo Entertainment System").getAttribute("aria-invalid"), "true");
    assert.equal(await box("Super Nintendo Entertainment System").inputValue(), typed);
    assert.equal((await saved()).snes, "/home/player/Games/SNES");
  }

  // Emptied: the apps folder again.
  await type("Super Nintendo Entertainment System", "");
  await until(async () => !(await saved()).snes);
  assert.equal(await section().locator(".console-folder-problem").count(), 0);
  assert.deepEqual(await saved(), { n64: "/home/player/Games/N64" });
});

test("a new install of an app for a console with a folder goes in a folder of its own in it; one installed before stays where it is", async () => {
  await page.locator(".topbar nav button", { hasText: "Library" }).click();
  const card = page.locator("article", { hasText: "Test Remake" }).first();
  await card.hover();
  await card.getByRole("button", { name: /^Get/ }).first().click();
  // The mock's files aren't marked for this computer: it asks which, as for any app.
  await page.getByRole("button", { name: /Linux · x64/ }).click();
  await until(() => page.evaluate(() => window.__installs.has("entry_test-remake")));
  const request = await page.evaluate(() => window.__calls.findLast((c) => c.cmd === "install").args.request);
  assert.equal(request.parent, "/home/player/Games/N64");
  assert.equal(request.dir, undefined);
  // The Rust side answers with where it went: the app's own folder in the console's.
  await page.evaluate(() => window.__installs.get("entry_test-remake").resolve({ dir: "/home/player/Games/N64/test-remake", version: "1.0.0", installer: null, restart: false }));
  await until(() => page.evaluate(() => Boolean(window.__files.installs["entry_test-remake"])));
  assert.equal(await page.evaluate(() => window.__files.installs["entry_test-remake"].dir), "/home/player/Games/N64/test-remake");
  // Test Port, installed before there was a folder for N64, stays in the apps folder.
  assert.equal(await page.evaluate(() => window.__files.installs["entry_test-port"].dir), undefined);
  assert.deepEqual(s.errors, []);
});
