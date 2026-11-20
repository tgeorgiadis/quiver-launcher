/**
 * The player's own tags in the tags window: opened from an app's Tags menu on its Your tags tab, searched and sorted,
 * and each renamed or deleted on every app that has it.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const state = {
  settings: {},
  library: [
    { id: "entry_test-port", slug: "test-port", addedAt: 1, overrides: { tags: ["Favourites", "Co-op", "Racing"] } },
    { id: "entry_tampered-port", slug: "tampered-port", addedAt: 2, overrides: { tags: ["Favourites", "Speedrun"] } },
  ],
  installs: {},
};
let s, page;

before(async () => {
  s = await openApp({ state });
  ({ page } = s);
  await page.locator("article[data-slug='test-port']").waitFor();
});

after(async () => {
  await s?.close();
  await stop();
});

const tagWindow = () => page.getByRole("dialog", { name: "Manage tags" });
const listed = () => tagWindow().locator(".own-tags .tag-name strong").allInnerTexts();
const saved = () => s.app.execute(() => window.__calls.findLast((c) => c.cmd === "write_state" && c.args.name === "library")?.args.value.map((i) => [i.slug, i.overrides?.tags ?? []]));

test("an app's Tags menu opens the tags window on the player's own, searched and sorted", async () => {
  await page.locator("article[data-slug='test-port']").click({ button: "right" });
  await page.getByRole("menuitem", { name: "Tags", exact: true }).click();
  await page.getByRole("menuitem", { name: "Manage tags…" }).click();
  await tagWindow().waitFor();
  assert.equal(await tagWindow().getByRole("tab", { name: /^Your tags/ }).getAttribute("aria-selected"), "true");
  assert.deepEqual(await listed(), ["Co-op", "Favourites", "Racing", "Speedrun"]);
  assert.match(await tagWindow().locator("li", { hasText: "Favourites" }).innerText(), /2 apps/);
  await tagWindow().getByRole("combobox", { name: "Sort tags" }).selectOption("apps");
  assert.equal((await listed())[0], "Favourites", "the one on the most apps first");
  await tagWindow().getByRole("searchbox", { name: "Search tags" }).fill("run");
  assert.deepEqual(await listed(), ["Speedrun"]);
  await tagWindow().getByRole("searchbox", { name: "Search tags" }).fill("");
});

test("renames a tag on every app, and Escape leaves the rename, not the window", async () => {
  await tagWindow().getByRole("button", { name: "Rename Favourites" }).click();
  const name = tagWindow().getByRole("textbox", { name: "New name for Favourites" });
  await name.fill("Nope");
  await name.press("Escape");
  await tagWindow().getByRole("button", { name: "Rename Favourites" }).waitFor();
  await tagWindow().getByRole("button", { name: "Rename Favourites" }).click();
  await tagWindow().getByRole("textbox", { name: "New name for Favourites" }).fill("Loved");
  await tagWindow().getByRole("button", { name: "Save" }).click();
  await s.until(async () => JSON.stringify(await saved()).includes("Loved"));
  assert.deepEqual(await saved(), [
    ["test-port", ["Loved", "Co-op", "Racing"]],
    ["tampered-port", ["Loved", "Speedrun"]],
  ]);
  assert.ok((await listed()).includes("Loved"));
});

test("deletes a tag from every app, after asking, and the catalog tab is a press away", async () => {
  await tagWindow().getByRole("button", { name: "Delete Loved" }).click();
  await tagWindow().getByText("Remove “Loved” from 2 apps?").waitFor();
  await tagWindow().getByRole("group", { name: "Delete Loved" }).getByRole("button", { name: "Delete" }).click();
  await s.until(async () => !JSON.stringify(await saved()).includes("Loved"));
  assert.deepEqual(await listed(), ["Co-op", "Racing", "Speedrun"]);
  await tagWindow().getByRole("tab", { name: /^From the catalog/ }).click();
  await tagWindow().locator(".catalog-tag").first().waitFor();
  await page.keyboard.press("Escape");
  await tagWindow().waitFor({ state: "detached" });
  assert.match(await page.getByRole("region", { name: "Tags", exact: true }).locator(".tags-summary").innerText(), /3\s+of your tags/);
  assert.deepEqual(s.errors, []);
});
