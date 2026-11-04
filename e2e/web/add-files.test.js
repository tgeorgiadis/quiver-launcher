/**
 * Add files to folder, on an app's page and in its menu: what it asks the launcher to do to the folder, what it
 * warns of, what uninstalling says, Settings' default for new installs, and the menu's Tags.
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
let s, page;

/** The launcher's answers about the folder: these files are in it, and what setting them says. */
const folderHas = (names, stayed = []) =>
  page.evaluate(
    ([names, stayed]) => {
      const { invoke } = window.__TAURI_INTERNALS__;
      window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
        if (cmd === "app_files") {
          window.__calls.push({ cmd, args });
          return Promise.resolve(args.names.filter((n) => names.includes(n)));
        }
        if (cmd === "set_app_files") {
          window.__calls.push({ cmd, args });
          return Promise.resolve(stayed);
        }
        return invoke(cmd, args);
      };
    },
    [names, stayed],
  );
const lastSet = () => page.evaluate(() => window.__calls.filter((c) => c.cmd === "set_app_files").at(-1)?.args);

/** The app's menu, open on a submenu. */
async function openSubmenu(name) {
  await page.locator("article[data-slug]").first().click({ button: "right" });
  await page.locator('[role="menu"] [role="menuitem"]', { hasText: name }).first().click();
}
const openFromMenu = async () => {
  await openSubmenu("Manage");
  await page.locator('.game-submenu [role="menuitem"]', { hasText: "Add files to folder…" }).click();
  return page.locator('[role="dialog"] form.add-files');
};

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

test("ticking portable.txt adds it, warning about the saves of an app already played", async () => {
  await folderHas([]);
  const form = await openFromMenu();
  const box = form.getByRole("checkbox");
  await box.waitFor();
  assert.equal(await box.isChecked(), false);
  assert.equal(await form.locator(".add-files-warning").count(), 0, "nothing to warn of yet");
  await box.check();
  await form.locator(".add-files-warning").waitFor();
  await form.getByRole("button", { name: "Save" }).click();
  await form.waitFor({ state: "detached" });
  const args = await lastSet();
  assert.deepEqual(args.add, ["portable.txt"]);
  assert.deepEqual(args.remove, []);
});

test("what's ticked comes from the folder, and unticking takes the file away", async () => {
  await folderHas(["portable.txt"]);
  const form = await openFromMenu();
  const box = form.getByRole("checkbox");
  await page.waitForFunction(() => document.querySelector("form.add-files input[type=checkbox]")?.checked);
  await box.uncheck();
  await form.getByRole("button", { name: "Save" }).click();
  await form.waitFor({ state: "detached" });
  const args = await lastSet();
  assert.deepEqual(args.add, []);
  assert.deepEqual(args.remove, ["portable.txt"]);
});

test("a file that isn't empty stays, and the player is told", async () => {
  await folderHas(["portable.txt"], ["portable.txt"]);
  const form = await openFromMenu();
  await page.waitForFunction(() => document.querySelector("form.add-files input[type=checkbox]")?.checked);
  await form.getByRole("checkbox").uncheck();
  await form.getByRole("button", { name: "Save" }).click();
  await page.getByText("portable.txt has something in it, so it stays in the folder.").waitFor();
});

test("other files are typed, comma-separated; portable.txt typed ticks its box", async () => {
  await folderHas([]);
  const form = await openFromMenu();
  const field = form.getByRole("textbox", { name: "Other files" });
  await field.fill("config.ini, PORTABLE.txt,config.ini");
  await field.press("Enter");
  assert.ok(await form.getByRole("checkbox").isChecked());
  assert.equal(await form.locator(".chip").count(), 1);
  assert.equal(await form.isVisible(), true, "Enter adds them; it doesn't save");
  await form.getByRole("button", { name: "Save" }).click();
  await form.waitFor({ state: "detached" });
  assert.deepEqual((await lastSet()).add, ["portable.txt", "config.ini"]);
});

test("saving with nothing changed leaves the folder alone", async () => {
  await folderHas([]);
  const before = await page.evaluate(() => window.__calls.filter((c) => c.cmd === "set_app_files").length);
  const form = await openFromMenu();
  await form.getByRole("button", { name: "Save" }).click();
  await form.waitFor({ state: "detached" });
  assert.equal(await page.evaluate(() => window.__calls.filter((c) => c.cmd === "set_app_files").length), before);
});

test("what the launcher refuses shows in the form, which stays open", async () => {
  await folderHas([]);
  await page.evaluate(() => {
    const { invoke } = window.__TAURI_INTERNALS__;
    window.__TAURI_INTERNALS__.invoke = (cmd, args) => (cmd === "set_app_files" ? Promise.reject('"a/b" isn\'t a valid file or folder name.') : invoke(cmd, args));
  });
  const form = await openFromMenu();
  await form.getByRole("textbox", { name: "Other files" }).fill("a/b");
  await form.getByRole("button", { name: "Save" }).click();
  await form.locator(".job-error", { hasText: "isn't a valid" }).waitFor();
});

test("it's a form on the app's On this computer tab too", async () => {
  await folderHas([]);
  await page.locator("article[data-slug] .card-open").first().click();
  await page.getByRole("tab", { name: "On this computer" }).click().catch(() => page.locator("button", { hasText: "On this computer" }).first().click());
  await page.locator(".panel-action", { hasText: "Add files to folder" }).click();
  await page.locator(".computer-panel form.add-files").getByRole("checkbox").waitFor();
});

test("uninstalling says the saves may go when portable.txt is there, and not otherwise", async () => {
  const warning = "This app may keep its saves in this folder.";
  const uninstall = async () => {
    await openSubmenu("Manage");
    await page.locator('.game-submenu [role="menuitem"]', { hasText: /^Uninstall$/ }).click();
    return page.locator(".uninstall-prompt");
  };
  await folderHas(["portable.txt"]);
  let prompt = await uninstall();
  await prompt.getByText(warning).waitFor();
  await prompt.getByRole("button", { name: "Cancel" }).click();
  await folderHas([]);
  prompt = await uninstall();
  await prompt.waitFor();
  await page.waitForTimeout(100);
  assert.equal(await prompt.getByText(warning).count(), 0);
});

test("the menu's Tags adds a typed tag and takes one off", async () => {
  await openSubmenu("Tags");
  const field = page.getByRole("textbox", { name: "Add a tag" });
  await field.fill("favourite");
  await field.press("Enter");
  const tag = page.locator('.game-submenu [role="menuitemcheckbox"]', { hasText: "favourite" });
  await tag.waitFor();
  assert.equal(await tag.getAttribute("aria-checked"), "true");
  await tag.click();
  await tag.waitFor({ state: "detached" });
});

test("an update keeps to the files the player chose, over the catalog's", async () => {
  const s2 = await openApp({ state: { ...state, installs: { [ID]: { ...state.installs[ID], version: "0.1.0", releasedAt: 1, files: ["mine.txt"] } } } });
  try {
    const p = s2.page;
    await p.locator("article[data-slug]").first().click({ button: "right" });
    await p.locator('[role="menu"] [role="menuitem"]', { hasText: "Update to" }).click();
    await p.waitForFunction(() => window.__calls.some((c) => c.cmd === "install"));
    const request = await p.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request);
    assert.deepEqual(request.filesToAdd, ["mine.txt"]);
  } finally {
    await s2.close();
  }
});

test("Settings' default for new installs is a checkbox with the rest of the apps folder's, and stays set", async () => {
  await page.locator(".topbar button[aria-label='Settings']").click();
  await page.locator('.settings-nav [data-section="apps"]').click();
  const box = page.getByRole("checkbox", { name: "Add portable.txt to new installs" });
  assert.equal(await box.isChecked(), false);
  await box.check();
  await page.getByText("Apps you've already installed don't change.").waitFor();
  assert.equal(await box.isChecked(), true);
  await box.uncheck();
});
