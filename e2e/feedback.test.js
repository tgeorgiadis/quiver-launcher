/**
 * Player feedback on an app's page, as on the website: anyone sees what
 * players said, the releases and the project's details; sharing how it ran
 * asks a signed-out player to sign in first, then saves, and saving again
 * updates it.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { launch } from "./session.js";

let s, app, api, until;

before(async () => {
  s = await launch();
  ({ app, api, until } = s);
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => app.$(".page-layer:not([hidden]) .app-page");
/** A tab of the open page, by its label. */
const tab = async (name) => {
  const tabs = await (await page()).$$("button[role=tab]");
  for (const t of tabs) if ((await t.getText()).startsWith(name)) return t;
  throw new Error(`no ${name} tab`);
};
const rows = () =>
  app.execute(() =>
    [...document.querySelectorAll(".page-layer:not([hidden]) .feedback article.review")].map((r) => ({
      author: r.querySelector(".review-author strong").textContent,
      said: r.querySelector(".result-badge").textContent.trim(),
      meta: r.querySelector(".review-author small").textContent,
    })),
  );

test("anyone sees what players said, the releases and the project's details", async () => {
  await (await (await s.card("test-port")).$(".card-open")).click();
  const score = await (await page()).$("button.score-link");
  assert.match(await score.getText(), /Feedback from 3 players/);
  // The score line goes to Player feedback.
  await score.click();
  await until(async () => (await (await tab("Player feedback")).getAttribute("aria-selected")) === "true");
  assert.match(await (await tab("Player feedback")).getText(), /3/);
  await until(async () => (await rows()).length === 2);
  assert.deepEqual(
    (await rows()).map((r) => [r.author, r.said]),
    [
      ["ada", "Runs well"],
      ["lin", "Runs with issues"],
    ],
  );
  assert.match((await rows())[0].meta, /Linux · tested on v1\.0\.0/);
  assert.ok(api.convexQueries().some((q) => q.path === "reviews:list" && q.args.slug === "test-port"));
  // Releases, and the project's details beside every tab.
  await (await tab("Releases")).click();
  await until(() => app.execute(() => document.querySelector(".page-layer:not([hidden]) .release-summary strong")?.textContent === "1.0.0"));
  const details = await (await page()).$("section[aria-label='Project details']");
  assert.match(await details.getText(), /MADE BY\s+Quiver Tester/i);
  assert.match(await details.getText(), /No AI use found/);
});

test("sharing how it ran signs in first, then the form opens and saves", async () => {
  await (await tab("Player feedback")).click();
  assert.match(await (await (await page()).$(".share-prompt")).getText(), /Sign in to share how it runs for you/);
  await (await (await page()).$("button*=Share how it ran")).click();
  const signIn = await app.$("[role=dialog][aria-label='Sign in']");
  await signIn.waitForDisplayed({ timeout: 10000 });
  await (await signIn.$("button=Create an account")).click();
  await (await signIn.$("input[placeholder=Username]")).setValue("player");
  await (await signIn.$("input[placeholder=Password]")).setValue("correct horse battery");
  await (await signIn.$("form button.primary")).click();
  // Signed in, the form it asked for opens.
  await (await (await page()).$("h3=How did it go?")).waitForDisplayed({ timeout: 10000 });
  await (await (await page()).$("button*=It ran, but I had issues")).click();
  await (await (await page()).$("textarea")).setValue("Needs the controller set up first.");
  await (await (await page()).$("button=Share")).click();
  await (await (await page()).$("p*=You've shared how it ran")).waitForDisplayed({ timeout: 10000 });
  const os = process.platform === "win32" ? "windows" : "linux";
  assert.deepEqual(api.reviews.at(-1), { entryId: "entry_test-port", result: "issues", body: "Needs the controller set up first.", platform: os, user: "player" });
  // Theirs is in the list, marked as theirs.
  await until(async () => (await rows()).some((r) => r.author === "playerYou"));
  assert.equal((await rows()).find((r) => r.author === "playerYou").said, "Runs with issues");
});

test("saving again updates the player's feedback instead of adding more", async () => {
  await (await (await page()).$("button*=Update your notes")).click();
  await (await (await page()).$("h3=Update your notes")).waitForDisplayed({ timeout: 10000 });
  // What they said before is there to change.
  await until(async () => (await (await (await page()).$("textarea")).getValue()) === "Needs the controller set up first.");
  await (await (await page()).$("button*=It worked well")).click();
  await (await (await page()).$("button=Update notes")).click();
  await (await (await page()).$("p*=You've shared how it ran")).waitForDisplayed({ timeout: 10000 });
  await until(async () => (await rows()).find((r) => r.author === "playerYou")?.said === "Runs well");
  assert.equal((await rows()).filter((r) => r.author === "playerYou").length, 1);
  await app.keys("Escape");
});
