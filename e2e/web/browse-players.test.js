/**
 * Browse's Players say filter: the apps whose players say they run well, have
 * issues or don't run, as their cards sum it up, or that no one has rated.
 * The site has no such filter, so Browse picks them out of its pages itself.
 */
import { after, before, beforeEach, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

let s, page;

before(async () => {
  s = await openApp({ state: { settings: {} } });
  ({ page } = s);
});

beforeEach(async () => {
  await page.reload();
  await page.getByRole("button", { name: "Browse", exact: true }).click();
  await card("tampered-port").first().waitFor();
  await page.getByRole("button", { name: /^Filters/ }).click();
});

after(async () => {
  await s?.close();
  await stop();
});

const card = (slug) => page.locator(`article[data-slug="${slug}"]`);
const players = (say) => page.getByLabel("Players say", { exact: true }).selectOption(say);
const slugs = () => page.locator("article[data-slug]").evaluateAll((cards) => [...new Set(cards.map((c) => c.dataset.slug))].sort());
const empty = () => page.locator(".empty", { hasText: "Nothing matches that search and those filters." });

test("Players say shows only the apps players say that of, and its pill takes it off", async () => {
  await players("broken");
  await card("tampered-port").first().waitFor({ state: "detached" });
  assert.deepEqual(await slugs(), ["test-remake"]);

  await players("runs");
  await card("test-remake").first().waitFor({ state: "detached" });
  assert.deepEqual(await slugs(), ["tampered-port", "test-port"]);

  // Closed, its pill stays, and removing it shows every app again.
  await page.getByRole("button", { name: /^Filters/ }).click();
  await page.getByRole("button", { name: "Remove filter Runs well" }).click();
  await card("test-remake").first().waitFor();
});

test("Players say with no app of that kind says nothing matches", async () => {
  await players("unrated");
  await empty().waitFor();
  assert.equal(await page.locator("article[data-slug]").count(), 0);
});

test("Players say isn't sent to the site, which has no such filter", async () => {
  await players("issues");
  await empty().waitFor();
  const last = s.api.catalogQueries().filter((q) => q.path === "catalog:list").at(-1);
  assert.ok(last && !JSON.stringify(last.args).includes("players"), JSON.stringify(last?.args));
});
