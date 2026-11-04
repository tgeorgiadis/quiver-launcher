/**
 * Download archive, in a library app's menu and on its page: it saves the file where the player picks, dated as its
 * release, and installs nothing. Browse's cards don't have it.
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

/** The launcher's answers: the folder the player picks (null, Cancel), and saving there. */
const picksFolder = (folder) =>
  page.evaluate((folder) => {
    const { invoke } = window.__TAURI_INTERNALS__;
    window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
      if (cmd === "pick_folder") return Promise.resolve(folder);
      if (cmd === "save_release_file") {
        window.__calls.push({ cmd, args });
        return Promise.resolve(`${args.request.to}/${args.request.filename}`);
      }
      return invoke(cmd, args);
    };
  }, folder);
const calls = (cmd) => page.evaluate((cmd) => window.__calls.filter((c) => c.cmd === cmd).map((c) => c.args), cmd);
const fromMenu = async () => {
  await page.locator("article[data-slug]").first().click({ button: "right" });
  await page.locator('[role="menu"] [role="menuitem"]', { hasText: "Manage" }).first().click();
  await page.locator('.game-submenu [role="menuitem"]', { hasText: "Download archive…" }).click();
};

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
});

beforeEach(async () => {
  s.api.setReleases("test-port", undefined);
  await page.reload();
  await page.locator("article[data-slug]").first().waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

test("the menu saves this computer's archive where the player picks, dated as its release, and installs nothing", async () => {
  await picksFolder("/home/player/Archive");
  await fromMenu();
  await page.getByText("Saved /home/player/Archive/test-port-linux.zip").waitFor();
  const [{ request }] = await calls("save_release_file");
  assert.equal(request.id, ID);
  assert.equal(request.to, "/home/player/Archive");
  assert.equal(request.filename, "test-port-linux.zip");
  assert.ok(request.releasedAt > 0);
  assert.ok(request.checksum?.startsWith("sha256:"));
  assert.deepEqual(await calls("install"), []);
});

test("cancelling the folder saves nothing", async () => {
  await picksFolder(null);
  await fromMenu();
  await page.locator('[role="menu"]').waitFor({ state: "detached" });
  await page.waitForTimeout(200);
  assert.deepEqual(await calls("save_release_file"), []);
});

test("an app's page has it as one of its small actions", async () => {
  await picksFolder("/home/player/Archive");
  await page.locator("article[data-slug] .card-open").first().click();
  await page.locator('[aria-label="App sections"] button', { hasText: "On this computer" }).click();
  const action = page.locator(".panel-action", { hasText: "Download archive…" }).first();
  await action.click();
  await page.getByText("Saved /home/player/Archive/test-port-linux.zip").waitFor();
  assert.equal(await page.locator(".app-actions", { hasText: "Download archive" }).count(), 0, "not among the big buttons");
});

test("Browse's cards don't offer it", async () => {
  await page.getByRole("button", { name: "Browse", exact: true }).click();
  const card = page.locator('article[data-slug="tampered-port"]').first();
  await card.click({ button: "right" });
  await page.locator('[role="menu"] [role="menuitem"]', { hasText: "Hide from Browse" }).waitFor();
  assert.equal(await page.locator('[role="menuitem"]', { hasText: "Download archive" }).count(), 0);
});

/** Test Port's one release, with this file for Linux. */
const releases = (file) => [{ id: "release_2", version: "2.0.0", prerelease: false, releasedAt: 1767225600000, installationOverride: null, assets: [{ id: "asset_2", url: `${s.api.api}/files/${file.filename}`, os: "linux", architecture: "x64", format: "", size: 3, ...file }] }];

for (const filename of ["TestPort.7z", "TestPort.tar.gz"]) {
  test(`${filename} saves too, not only a ZIP`, async () => {
    s.api.setReleases("test-port", releases({ filename, checksum: `sha256:${"a".repeat(64)}` }));
    await picksFolder("/home/player/Archive");
    await fromMenu();
    await page.getByText(`Saved /home/player/Archive/${filename}`).waitFor();
  });
}

test("a file Quiver hasn't verified asks about saving it, not installing it", async () => {
  s.api.setReleases("test-port", releases({ filename: "TestPort.zip", unverifiedChecksum: `sha256:${"a".repeat(64)}` }));
  await picksFolder("/home/player/Archive");
  await fromMenu();
  const prompt = page.locator(".unverified-prompt");
  await prompt.getByRole("heading", { name: "Save TestPort.zip before it's verified?" }).waitFor();
  assert.match(await prompt.innerText(), /It saves the file Quiver saw/);
  assert.equal(await prompt.getByRole("button", { name: "Install anyway" }).count(), 0);
  await prompt.getByRole("button", { name: "Save anyway" }).click();
  await page.getByText("Saved /home/player/Archive/TestPort.zip").waitFor();
});

test("a private repository's archive comes through GitHub's API, with the player's token", async () => {
  const library = [{ id: "github:someone/homebrew", slug: "", addedAt: 1, custom: { provider: "github", repository: "someone/homebrew", name: "Homebrew" } }];
  const own = await openApp({ state: { settings: {}, library, secrets: { githubToken: "secret" } } });
  try {
    const saved = own.page;
    await saved.evaluate(() => {
      const { invoke } = window.__TAURI_INTERNALS__;
      window.__TAURI_INTERNALS__.invoke = (cmd, args) => {
        if (cmd === "pick_folder") return Promise.resolve("/a");
        if (cmd === "save_release_file") {
          window.__calls.push({ cmd, args });
          return Promise.resolve(`/a/${args.request.filename}`);
        }
        return invoke(cmd, args);
      };
    });
    await saved.locator("article").first().click({ button: "right" });
    await saved.locator('[role="menu"] [role="menuitem"]', { hasText: "Manage" }).first().click();
    await saved.locator('.game-submenu [role="menuitem"]', { hasText: "Download archive…" }).click();
    await saved.getByText("Saved /a/homebrew-linux.zip").waitFor();
    const { request } = await saved.evaluate(() => window.__calls.find((c) => c.cmd === "save_release_file").args);
    assert.equal(request.url, `${own.api.github}/repos/someone/homebrew/releases/assets/1`);
    assert.equal(request.githubToken, "secret");
  } finally {
    await own.close();
  }
});
