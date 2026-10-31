/**
 * A GitHub token in Settings: one GitHub refuses isn't saved, a good one
 * raises the limit Settings shows and goes with every request to GitHub,
 * and removing it stops that.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s;

before(async () => {
  s = await launch();
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const section = () => s.app.$('section.settings-page section[aria-label="GitHub token"]');
const status = async () => (await (await section()).$("p[role=status]")).getText();
// With QUIVER_DATA set the keychain isn't used: secrets go to account.json, and never to settings.json.
const read = (name) => {
  try {
    return JSON.parse(readFileSync(join(s.data, name), "utf8"));
  } catch {
    return {};
  }
};
const saved = () => {
  assert.equal(read("settings.json").githubToken, undefined);
  return read("account.json").githubToken;
};
const enter = async (token) => {
  await (await (await section()).$("input[aria-label='GitHub token']")).setValue(token);
  await (await (await section()).$("button=Save token")).click();
};

test("a token GitHub accepts is saved and used; one it refuses isn't", async () => {
  const { app, api, until } = s;
  await s.settings("Advanced");
  await until(async () => /^59 of 60 requests left this hour/.test(await status()));

  await enter("bad-token");
  await until(async () => /GitHub didn't accept that token, so it wasn't saved/.test(await (await section()).getText()));
  assert.equal(saved(), undefined);

  await enter("ghp_good");
  await until(async () => /Saved\. Requests to GitHub use your token now\./.test(await (await section()).getText()));
  await until(() => saved() === "ghp_good");
  // Grouped as the system's language has it: "4,999", "4 999" or "4999".
  await until(async () => /^4\D?999 of 5\D?000 requests left this hour/.test(await status()));

  // An app added from GitHub asks with the token.
  await app.keys("Escape");
  await (await app.$("button*=Library")).click();
  await (await app.$("button*=Add an app")).click();
  await (await (await app.$("[role=dialog][aria-label='Add an app']")).$("button*=From GitHub or GitLab")).click();
  await (await app.$("input[aria-label=Repository]")).setValue("someone/homebrew");
  await (await app.$("button=Look it up")).click();
  await until(() => api.githubRequests().some((r) => r.path === "/github/repos/someone/homebrew/releases"));
  assert.equal(api.githubRequests().findLast((r) => r.path === "/github/repos/someone/homebrew/releases").token, "ghp_good");
  await app.keys("Escape");

  // Removed, nothing is sent.
  await s.settings("Advanced");
  await (await (await section()).$("button=Remove token")).click();
  await until(() => saved() === undefined);
  await until(async () => /^59 of 60 requests left this hour/.test(await status()));
  assert.equal(api.githubRequests().at(-1).token, null);
});
