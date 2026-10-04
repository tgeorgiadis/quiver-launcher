/**
 * Library filters and playlists: filter the library as the catalog filters,
 * keep a filter as a playlist, add an app to a playlist by hand, and share a
 * playlist that another player adds to their library.
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
const playlist = async (s, name) => (await s.app.$("nav[aria-label=Playlists]")).$(`button=${name}`);
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
  assert.match(await (await a.app.$("button*=Filters")).getText(), /Filters · 2/);
});

test("the filters are kept as a playlist", async () => {
  await (await a.app.$("button=Save as playlist")).click();
  const dialog = await a.app.$("[role=dialog][aria-label='New playlist']");
  await (await dialog.$("input[aria-label='Playlist name']")).setValue("N64 without AI");
  await (await dialog.$("button=Save")).click();
  await a.until(async () => (await (await playlist(a, "N64 without AI")).getAttribute("aria-pressed")) === "true");
  // The filters went into the playlist, and the playlist shows what they matched.
  assert.ok(!(await a.app.$("button=Save as playlist").isExisting()));
  assert.deepEqual(await shown(a), ["test-port"]);
  const saved = collections().find((c) => c.name === "N64 without AI");
  assert.deepEqual([saved.consoles, saved.ai], [["n64"], "no-generated"]);
});

test("an app is added to a playlist by hand, from its card", async () => {
  await (await playlist(a, "All")).click();
  await (await (await a.card("tampered-port")).$("button[aria-label='Add to playlist']")).click();
  assert.ok(await onTop(a, "[aria-label='Playlists for this app']"), "the card's playlist picker isn't clipped");
  await (await inMenu(a, "[aria-label='Playlists for this app']", "N64 without AI")).click();
  await (await playlist(a, "N64 without AI")).click();
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
  await (await a.app.$("button=Sign out")).waitForDisplayed({ timeout: 10000 });
  // The playlist's menu: Share.
  await (await playlist(a, "N64 without AI")).click();
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
  b = await launch({ api, port: 4446 });
  await (await b.app.$("button*=Library")).click();
  await (await b.app.$("button*=Add a shared playlist")).click();
  const follow = await b.app.$("[role=dialog][aria-label='Add a shared playlist']");
  await (await follow.$("input[aria-label='Shared playlist link']")).setValue(link);
  await (await follow.$("button=Look it up")).click();
  await (await follow.$("h2=N64 without AI")).waitForDisplayed({ timeout: 10000 });
  assert.match(await follow.getText(), /A playlist by sharer · 2 apps/);
  await (await follow.$("button=Add to my library")).click();
  await b.until(async () => (await (await playlist(b, "N64 without AI")).getAttribute("aria-pressed")) === "true");
  await b.until(async () => JSON.stringify(await shown(b)) === '["tampered-port","test-port"]');
  // Its apps offer Get; nothing downloads by itself.
  assert.ok(await (await b.card("test-port")).$("button=Get").isExisting());
  const appsDir = b.apps;
  assert.ok(!existsSync(appsDir) || readdirSync(appsDir).filter((d) => !d.startsWith(".")).length === 0, "nothing was installed");
  assert.match(await (await b.app.$(".playlist-note")).getText(), /A playlist by sharer/);
});
