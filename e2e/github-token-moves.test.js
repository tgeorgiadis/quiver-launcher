/**
 * A GitHub token an earlier version saved as plain text in settings.json
 * moves to where secrets are kept (account.json here, as QUIVER_DATA is set;
 * the keychain otherwise), and requests to GitHub still use it.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s;

after(() => s?.close());

test("a token left in settings.json moves out of it", async () => {
  const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
  writeFileSync(join(data, "settings.json"), JSON.stringify({ githubToken: "ghp_good" }));
  s = await launch({ data });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  const read = (name) => JSON.parse(readFileSync(join(data, name), "utf8"));
  assert.equal(read("account.json").githubToken, "ghp_good");
  assert.equal(read("settings.json").githubToken, undefined);
  await s.settings("Advanced");
  const section = await s.app.$('section.settings-page section[aria-label="GitHub token"]');
  await s.until(async () => /^4\D?999 of 5\D?000 requests left this hour/.test(await (await section.$("p[role=status]")).getText()));
});
