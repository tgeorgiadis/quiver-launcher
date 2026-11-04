/**
 * A GitHub repository's files, for an app the player added from it: with their GitHub token saved, through GitHub's
 * API and with the token, which a private repository's files need; without one, by their browser links as before.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const library = [{ id: "github:someone/homebrew", slug: "", addedAt: 1, custom: { provider: "github", repository: "someone/homebrew", name: "Homebrew" } }];
const opened = [];

after(async () => {
  await Promise.all(opened.map((s) => s.close()));
  await stop();
});

/** What the launcher asks the Rust side to download when Get is pressed on Homebrew's card. */
async function downloads(secrets) {
  const s = await openApp({ state: { settings: {}, library, secrets } });
  opened.push(s);
  const { page } = s;
  const card = page.locator("article").filter({ has: page.getByRole("button", { name: "Homebrew", exact: true }) });
  await card.hover();
  await card.locator(".tile-get").click();
  await s.until(() => page.evaluate(() => window.__calls.some((c) => c.cmd === "install")));
  return { request: await page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request), github: s.api.github };
}

test("with a token saved, a GitHub app's file comes through GitHub's API, with the token", async () => {
  const { request, github } = await downloads({ githubToken: "secret" });
  assert.equal(request.url, `${github}/repos/someone/homebrew/releases/assets/1`);
  assert.equal(request.githubToken, "secret");
});

test("without one, by its browser link, and no token goes with it", async () => {
  const { request } = await downloads(undefined);
  assert.match(request.url, /\/files\/homebrew-linux\.zip$/);
  assert.equal(request.githubToken, undefined);
});
