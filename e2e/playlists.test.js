/**
 * Library filters and playlists: filter the library as the catalog filters,
 * keep a filter as a playlist, add an app to a playlist by hand, share a
 * playlist that another player adds to their library, and a playlist of words
 * in apps' names with one of its apps' icons.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, existsSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let api, a, b, link;

before(async () => {
  api = await startMockApi();
  a = await launch({ api });
  // Three apps in the library: Tampered Port's download is refused, so it stays not installed.
  for (const slug of ["test-port", "test-remake", "tampered-port"]) {
    const card = await a.card(slug);
    await card.waitForDisplayed({ timeout: 20000 });
    await (await card.$("button=Get")).click();
    await a.until(async () => (await card.$("button*=Play").isExisting()) || (await card.$('[role="alert"]').isExisting()), 30000);
  }
  await (await a.app.$("button*=Library")).click();
});

after(async () => {
  await a?.close();
  await b?.close();
  api?.close();
});

/** The library's cards, in order. */
const shown = (s) => s.app.execute(() => [...document.querySelectorAll("main > div:not(.page-layer) article")].map((e) => e.dataset.slug).sort());
const inMenu = async (s, menu, item) => (await s.app.$(menu)).$(`button=${item}`);
const collections = () => JSON.parse(readFileSync(join(a.data, "collections.json"), "utf8"));
/** Whether an open menu is really on top where it is: nothing clips or covers its middle. */
const onTop = (s, selector) =>
  s.app.execute((selector) => {
    const el = document.querySelector(selector);
    if (!el) return false;
    const r = el.getBoundingClientRect();
    return el.contains(document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2));
  }, selector);

test("the library filters by console and AI use, as the catalog does", async () => {
  await a.until(async () => (await shown(a)).length === 3);
  await (await a.app.$("button*=Filters")).click();
  await (await a.app.$("select[aria-label=Console]")).selectByVisibleText("Nintendo 64");
  await a.until(async () => JSON.stringify(await shown(a)) === '["test-port","test-remake"]');
  await (await a.app.$("select[aria-label='AI use']")).selectByVisibleText("Hide mostly AI-generated apps");
  await a.until(async () => JSON.stringify(await shown(a)) === '["test-port"]');
  // What's on shows as chips, each one removable.
  assert.ok(await a.app.$("button[aria-label='Remove filter Nintendo 64']").isExisting());
  assert.equal(await (await a.app.$(".library-tools .tool-count")).getText(), "2");
});

test("the filters are kept as a playlist", async () => {
  await (await a.app.$("button=Save as playlist")).click();
  const dialog = await a.app.$("[role=dialog][aria-label='New playlist']");
  await (await dialog.$("input[aria-label='Playlist name']")).setValue("N64 without AI");
  await (await dialog.$("button=Save")).click();
  await a.until(async () => (await a.openTab()) === "N64 without AI");
  // The filters went into the playlist, and the playlist shows what they matched.
  assert.ok(!(await a.app.$("button=Save as playlist").isExisting()));
  assert.deepEqual(await shown(a), ["test-port"]);
  const saved = collections().find((c) => c.name === "N64 without AI");
  assert.deepEqual([saved.consoles, saved.ai], [["n64"], "no-generated"]);
});

test("an app is added to a playlist by hand, from its card", async () => {
  await a.showTab("All");
  await (await (await a.card("tampered-port")).$("button[aria-label='Add to playlist']")).click();
  assert.ok(await onTop(a, "[aria-label='Playlists for this app']"), "the card's playlist picker isn't clipped");
  await (await inMenu(a, "[aria-label='Playlists for this app']", "N64 without AI")).click();
  await a.showTab("N64 without AI");
  await a.until(async () => JSON.stringify(await shown(a)) === '["tampered-port","test-port"]');
  assert.deepEqual(collections().find((c) => c.name === "N64 without AI").apps, ["entry_tampered-port"]);
});

test("a playlist is shared, and another player adds it to their library", async () => {
  // Sharing needs an account.
  await (await a.app.$("button=Sign in")).click();
  await (await a.app.$("button=Create an account")).click();
  await (await a.app.$("input[placeholder=Username]")).setValue("sharer");
  await (await a.app.$("input[placeholder=Password]")).setValue("correct horse battery");
  await (await a.app.$("form button.primary")).click();
  await (await a.app.$(".account-button")).waitForDisplayed({ timeout: 10000 });
  // The playlist's menu: Share.
  await a.showTab("N64 without AI");
  await (await a.app.$("button[aria-label='N64 without AI options']")).click();
  assert.ok(await onTop(a, "[role=menu]"), "the playlist's menu isn't clipped by the tab row");
  await (await inMenu(a, "[role=menu]", "Share")).click();
  const share = await a.app.$("[role=dialog][aria-label='Share a playlist']");
  await (await share.$("button=Share")).click();
  const field = await share.$("input[aria-label='Shared playlist link']");
  await field.waitForDisplayed({ timeout: 10000 });
  link = await field.getValue();
  assert.match(link, /^https:\/\/quiverlauncher\.com\/lists\/n64-without-ai-[a-z0-9]+$/);
  const slug = link.split("/").pop();
  assert.deepEqual(api.lists.get(slug).apps.map((x) => x.entryId).sort(), ["entry_tampered-port", "entry_test-port"]);
  await (await share.$("button=Done")).click();

  // Another player, with an empty library, adds it from the link.
  b = await launch({ api });
  await (await b.app.$("button*=Library")).click();
  await (await b.app.$("button*=Add a shared playlist")).click();
  const follow = await b.app.$("[role=dialog][aria-label='Add a shared playlist']");
  await (await follow.$("input[aria-label='Shared playlist link']")).setValue(link);
  await (await follow.$("button=Look it up")).click();
  await (await follow.$("h2=N64 without AI")).waitForDisplayed({ timeout: 10000 });
  assert.match(await follow.getText(), /A playlist by sharer · 2 apps/);
  await (await follow.$("button=Add to my library")).click();
  await b.until(async () => (await b.openTab()) === "N64 without AI");
  await b.until(async () => JSON.stringify(await shown(b)) === '["tampered-port","test-port"]');
  // Its apps offer Get; nothing downloads by itself.
  assert.ok(await (await b.card("test-port")).$("button=Get").isExisting());
  const appsDir = b.apps;
  assert.ok(!existsSync(appsDir) || readdirSync(appsDir).filter((d) => !d.startsWith(".")).length === 0, "nothing was installed");
  assert.match(await (await b.app.$(".playlist-note")).getText(), /A playlist by sharer/);
});

test("a playlist of words collects the apps with them in their name or their game's, with one's icon on its tab", async () => {
  await a.showTab("All");
  await (await a.app.$("button[aria-label='Add a playlist']")).click();
  await (await (await a.app.$("[role=menu]")).$("button=New playlist")).click();
  const dialog = await a.app.$("[role=dialog][aria-label='New playlist']");
  await (await dialog.$("input[aria-label='Playlist name']")).setValue("Test Port games");
  await (await dialog.$("input[aria-label='Names with']")).setValue("test port");
  // Its icon is one of its apps': the first, or one picked.
  const icons = await dialog.$("[role=radiogroup][aria-label='Icon on its tab']");
  await a.until(async () => (await icons.$$("[role=radio]")).length === 2);
  const remake = await icons.$("[role=radio][aria-label='Test Remake']");
  await remake.click();
  assert.equal(await remake.getAttribute("aria-checked"), "true");
  await (await dialog.$("button=Save")).click();
  await a.until(async () => (await a.openTab()) === "Test Port games");
  // Test Remake is a way to play Test Port's game; Tampered Port's names don't say "test".
  await a.until(async () => JSON.stringify(await shown(a)) === '["test-port","test-remake"]');
  // On its tab, where the tabs show as a row (folded into the Show menu, a window this size has no room for them).
  const tabs = await a.app.$("nav[aria-label=Playlists]");
  if (await tabs.isExisting()) assert.ok(await (await tabs.$("button=Test Port games")).$(".playlist-icon").isExisting());
  const saved = collections().find((c) => c.name === "Test Port games");
  // No apps picked by hand: none, or an empty list once the account has synced it.
  assert.deepEqual([saved.search, saved.icon, saved.apps ?? []], ["test port", "entry_test-remake", []]);
});
