/**
 * A player's own pictures and matches for an app, from its page's side panel: a picture for each place, by address or
 * from a file; the game an app they added plays; and moving one onto the catalog's app for it.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAEBgIApD5fRAAAAABJRU5ErkJggg==", "base64");
const LOCAL = "local:mine";
const CUSTOM = "github:quiver/test-port";

const state = {
  settings: {},
  pickArtwork: true,
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
  await page.route("https://art.example/**", (route) => route.fulfill({ contentType: "image/png", body: PNG }));
  await page.route("**/cdn-cgi/image/**", (route) => route.fulfill({ contentType: "image/png", body: PNG }));
});

beforeEach(async () => {
  await page.unroute(FEED);
  await page.reload();
  await page.locator(".topbar").waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

const card = (name) => page.locator("article").filter({ has: page.getByRole("button", { name, exact: true }) });
/** The side panel: its own tab on a catalog app's page. */
const toPanel = async () => {
  const tab = page.getByRole("tab", { name: /On this computer/ });
  await page.locator(".app-page h1").waitFor();
  if (await tab.count()) await tab.click();
  await page.locator(".app-panel").waitFor();
};
const openPage = async (name) => {
  await card(name).getByRole("button", { name, exact: true }).click();
  await toPanel();
};
const FEED = /\/api\/v1\/release-status/;

test("box art by address shows on the library's card, where the old field never reached", async () => {
  await openPage("Test Remake");
  await page.getByRole("button", { name: "Change name or artwork" }).click();
  await page.getByLabel("Box art address").fill("https://art.example/box.png");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.getByRole("button", { name: "Change name or artwork" }).waitFor();
  await page.locator(".back-link").click();
  await page.locator(`article img[src*="art.example/box.png"]`).first().waitFor();
});

test("a banner from a file shows behind the app's page, and an address it can't use is refused", async () => {
  await openPage("Test Remake");
  await page.getByRole("button", { name: "Change name or artwork" }).click();
  await page.getByLabel("Icon address").fill("http://not-https.example/a.png");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.getByText("Use an https:// image address.").waitFor();
  await page.getByLabel("Icon address").fill("");
  await page.getByRole("button", { name: "Choose a file for Banner" }).click();
  await page.locator('[data-slot="banner"]').getByText("Picture from this computer").waitFor();
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.locator('.backdrop-art[src^="data:image/png"]').waitFor();
});

test("an app the player added takes another game's art and its Based on", async () => {
  await openPage("Test");
  await page.getByRole("button", { name: "Change game" }).click();
  await page.getByLabel("Search for a game").fill("Test Port");
  await page.locator(".change-game .art-match button[data-game]").first().click();
  await page.waitForTimeout(100);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.locator(".game-chip").first().waitFor();
  // Picking none takes them away again.
  await page.getByRole("button", { name: "Change game" }).click();
  await page.locator(".change-game .art-match").getByRole("button", { name: "None", exact: true }).click();
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.waitForFunction(() => !document.querySelector(".game-chip"));
});

test("a repository the catalog lists is marked on its card, and its page offers the switch", async () => {
  await page.locator('article [data-copy="listed"]').first().waitFor();
  await openPage("My Port");
  await page.getByRole("button", { name: "Switch to the catalog's" }).waitFor();
});

test("a repository with several catalog apps asks which, rather than offering the last", async () => {
  await page.route(FEED, (route) =>
    route.fulfill({
      headers: { "Access-Control-Allow-Origin": "*" },
      json: {
        items: ["test-port", "tampered-port"].map((slug) => ({ id: `entry_${slug}`, slug, provider: "github", repository: "quiver/test-port" })),
        nextCursor: null,
        isDone: true,
      },
    }),
  );
  await page.reload();
  await openPage("My Port");
  await page.getByRole("button", { name: /^Switch to Test Port$/ }).waitFor();
  await page.getByRole("button", { name: /^Switch to Tampered Port$/i }).waitFor();
  assert.equal(await page.getByRole("button", { name: "Switch to the catalog's" }).count(), 0);
});

test("linking a local app keeps its files the player's own: no update, and removing it leaves them", async () => {
  await openPage("Test");
  await page.getByRole("button", { name: "Link to a catalog app" }).click();
  await page.getByLabel("Search the catalog").fill("Test Port");
  await page.locator(".link-catalog .pick-listed button", { hasText: "Test Port" }).first().click();
  await page.getByText("Its files stay yours").waitFor();
  await page.getByRole("button", { name: "Link", exact: true }).click();
  await page.getByRole("heading", { name: "Test Port", exact: true }).first().waitFor();
  await page.getByRole("button", { name: "Play", exact: true }).first().waitFor();
  assert.equal(await page.getByRole("button", { name: /^Update/ }).count(), 0);
  await toPanel();
  await page.getByText("Removing it from your library leaves its files where they are.").waitFor();
  assert.equal(await page.getByRole("button", { name: "Link to a catalog app" }).count(), 0);
  assert.equal(await page.getByRole("button", { name: "Uninstall and remove" }).count(), 0);
  assert.deepEqual(s.errors, []);
});
