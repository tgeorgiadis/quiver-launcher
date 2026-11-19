/**
 * The library's Managed by filter: the apps Quiver downloads and updates (from the catalog or a repository), or those
 * the player looks after, added from this computer as 3.x's manually managed apps were, or linked to a catalog app and
 * still played from their own files. A playlist keeps it, in the account too.
 */
import { after, afterEach, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const LOCAL = "local:mine";
const CUSTOM = "github:quiver/test-port";
const LINKED = "entry_test-port";

const state = {
  settings: {},
  library: [
    { id: "entry_test-remake", slug: "test-remake", addedAt: 4 },
    { id: LINKED, slug: "test-port", addedAt: 3 },
    { id: LOCAL, slug: "", addedAt: 2, local: { kind: "program", path: "/games/Test.exe", name: "Test" } },
    { id: CUSTOM, slug: "", addedAt: 1, custom: { provider: "github", repository: "quiver/test-port", name: "My Port" } },
  ],
  installs: {
    [LOCAL]: { local: true, version: "", dir: "/games/", folder: "Test.exe", executables: [], program: "/games/Test.exe" },
    [LINKED]: { local: true, version: "", dir: "/games/", folder: "Port.exe", executables: [], program: "/games/Port.exe" },
  },
};
let s, page;

beforeEach(async () => {
  s = await openApp({ state: structuredClone(state) });
  ({ page } = s);
  await card("Test Remake").waitFor();
  await page.getByRole("button", { name: /^Filters/ }).click();
});

afterEach(async () => {
  await s?.close();
});

after(stop);

const card = (name) => page.locator("article").filter({ has: page.getByRole("button", { name, exact: true }) });
const managed = (by) => page.getByLabel("Managed by", { exact: true }).selectOption(by);
const names = async () => (await Promise.all(["Test Remake", "Test Port", "Test", "My Port"].map(async (n) => ((await card(n).count()) ? n : null)))).filter(Boolean);
const dialog = () => page.locator('[role="dialog"]');

/** Saves the filters on now as a playlist, whose tab then opens. */
async function saveAs(name) {
  await page.getByRole("button", { name: "Save as playlist" }).click();
  await dialog().getByLabel("Playlist name").fill(name);
  await dialog().locator("button.primary").click();
  await dialog().waitFor({ state: "detached" });
  await page.locator(".playlist-tab").filter({ hasText: name }).waitFor();
}

test("Manually managed shows the apps the player looks after, and Managed by Quiver the rest", async () => {
  await managed("manual");
  await card("Test Remake").waitFor({ state: "detached" });
  assert.deepEqual(await names(), ["Test Port", "Test"]);

  await managed("quiver");
  await card("Test Remake").waitFor();
  await card("Test").waitFor({ state: "detached" });
  assert.deepEqual(await names(), ["Test Remake", "My Port"]);

  await page.getByRole("button", { name: "Remove filter Managed by Quiver" }).click();
  await card("Test").waitFor();
});

test("a playlist keeps Managed by alone, and its dialog can change it", async () => {
  await managed("manual");
  await card("Test Remake").waitFor({ state: "detached" });
  await saveAs("By hand");
  // Saving takes the filters off the library, so what's left is the playlist's own.
  await page.getByRole("button", { name: "Clear all" }).waitFor({ state: "detached" });
  await card("Test").waitFor();
  assert.deepEqual(await names(), ["Test Port", "Test"]);

  await page.locator(".playlist-tab").filter({ hasText: "By hand" }).locator("button").nth(1).click();
  await page.getByText("Edit", { exact: true }).click();
  const choice = dialog().getByLabel("Managed by", { exact: true });
  assert.equal(await choice.inputValue(), "manual");
  await choice.selectOption("quiver");
  await dialog().locator("button.primary").click();
  await card("Test Remake").waitFor();
  assert.deepEqual(await names(), ["Test Remake", "My Port"]);
});

test("the account keeps a playlist's Managed by, and clears it when it's taken off", async () => {
  const username = "managed-playlists";
  await page.locator(".account button").click();
  await dialog().waitFor();
  await dialog().locator('form > button[type="button"]').last().click();
  await dialog().locator('input[autocomplete="new-password"]').waitFor();
  await dialog().locator('input[autocomplete="username"]').fill(username);
  await dialog().locator('input[autocomplete="new-password"]').fill("a password for the test");
  await dialog().locator("button.primary").click();
  await page.locator(".account-button").waitFor();

  await managed("manual");
  await card("Test Remake").waitFor({ state: "detached" });
  await saveAs("By hand");
  await s.until(() => s.api.collections(username).some((c) => c.name === "By hand" && c.managed === "manual"));

  await page.locator(".playlist-tab").filter({ hasText: "By hand" }).locator("button").nth(1).click();
  await page.getByText("Edit", { exact: true }).click();
  await dialog().getByLabel("Managed by", { exact: true }).selectOption("");
  await dialog().locator("button.primary").click();
  await s.until(() => s.api.collections(username).some((c) => c.name === "By hand" && c.managed === undefined));
});
