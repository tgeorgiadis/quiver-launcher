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

/** What the launcher asks the Rust side to download when Homebrew is installed from its card's menu. */
async function downloads(secrets) {
  const s = await openApp({ state: { settings: {}, library, secrets } });
  opened.push(s);
  const { page } = s;
  // GitHub's API, as a custom app's releases are listed: one release, with a file for every computer.
  await page.route("https://api.github.com/repos/someone/homebrew/releases?**", (route) =>
    route.fulfill({
      headers: { "Access-Control-Allow-Origin": "*" },
      json: [
        {
          tag_name: "v2.0",
          draft: false,
          prerelease: false,
          published_at: "2026-02-01T00:00:00Z",
          assets: ["linux", "windows", "macos"].map((os, i) => ({ id: i + 1, name: `Homebrew-${os}.zip`, browser_download_url: `https://github.com/someone/homebrew/releases/download/v2.0/Homebrew-${os}.zip`, size: 5 })),
        },
      ],
    }),
  );
  await page.reload();
  await page.locator(".topbar").waitFor();
  const card = page.locator("article").filter({ hasText: "Homebrew" }).first();
  await card.locator(".tile-get").waitFor({ state: "attached" });
  await card.hover();
  await card.locator(".tile-get").click();
  await s.until(() => page.evaluate(() => window.__calls.some((c) => c.cmd === "install")));
  return page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request);
}

test("with a token saved, a GitHub app's file comes through GitHub's API, with the token, and its browser link to fall back on", async () => {
  const request = await downloads({ githubToken: "secret" });
  assert.match(request.url, /^https:\/\/api\.github\.com\/repos\/someone\/homebrew\/releases\/assets\/\d+$/);
  assert.equal(request.githubToken, "secret");
  assert.match(request.fallbackUrl, /^https:\/\/github\.com\/someone\/homebrew\/releases\/download\/v2\.0\/Homebrew-\w+\.zip$/);
});

test("without one, by its browser link, and no token goes with it", async () => {
  const request = await downloads(undefined);
  assert.match(request.url, /^https:\/\/github\.com\/someone\/homebrew\/releases\/download\/v2\.0\/Homebrew-\w+\.zip$/);
  assert.equal(request.githubToken, undefined);
  assert.equal(request.fallbackUrl, undefined);
});
