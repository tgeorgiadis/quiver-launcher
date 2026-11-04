/**
 * The library's Managed by filter: the apps Quiver downloads and updates (from the catalog or a repository), or those
 * added from this computer, as 3.x's manually managed apps were. A playlist doesn't keep it.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const LOCAL = "local:mine";
const CUSTOM = "github:quiver/test-port";

const state = {
  settings: {},
  library: [
    { id: "entry_test-remake", slug: "test-remake", addedAt: 3 },
    { id: LOCAL, slug: "", addedAt: 2, local: { kind: "program", path: "/games/Test.exe", name: "Test" } },
    { id: CUSTOM, slug: "", addedAt: 1, custom: { provider: "github", repository: "quiver/test-port", name: "My Port" } },
  ],
  installs: { [LOCAL]: { local: true, version: "", dir: "/games/", folder: "Test.exe", executables: [], program: "/games/Test.exe" } },
};
let s, page;

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await card("Test Remake").waitFor();
  await page.getByRole("button", { name: /^Filters/ }).click();
});

after(async () => {
  await s?.close();
  await stop();
});

const card = (name) => page.locator("article").filter({ has: page.getByRole("button", { name, exact: true }) });
const managed = (by) => page.getByLabel("Managed by", { exact: true }).selectOption(by);
const names = async () => (await Promise.all(["Test Remake", "Test", "My Port"].map(async (n) => ((await card(n).count()) ? n : null)))).filter(Boolean);

test("Manually managed shows only the apps added from this computer, and Managed by Quiver the rest", async () => {
  await managed("manual");
  await card("Test Remake").waitFor({ state: "detached" });
  assert.deepEqual(await names(), ["Test"]);

  await managed("quiver");
  await card("Test Remake").waitFor();
  await card("Test").waitFor({ state: "detached" });
  assert.deepEqual(await names(), ["Test Remake", "My Port"]);

  await page.getByRole("button", { name: "Remove filter Managed by Quiver" }).click();
  await card("Test").waitFor();
});

test("Managed by alone doesn't offer to save a playlist, which wouldn't keep it", async () => {
  await managed("manual");
  await card("Test Remake").waitFor({ state: "detached" });
  assert.equal(await page.getByRole("button", { name: "Save as playlist" }).count(), 0);
});
