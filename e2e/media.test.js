/**
 * An app's Media tab, as on the website: its YouTube videos in the order
 * staff set, filtered by kind, each a thumbnail until it's played, and a
 * link that opens the video on YouTube in the browser.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { chmodSync, existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, until;
// A browser that notes the pages it's asked to open.
const dir = mkdtempSync(join(tmpdir(), "quiver-browser-"));
const opened = join(dir, "opened.log");
const browser = join(dir, "browser.sh");
writeFileSync(browser, `#!/bin/sh\necho "$1" >> "${opened}"\n`);
chmodSync(browser, 0o755);

before(async () => {
  s = await launch({ env: { QUIVER_BROWSER: browser } });
  ({ app, until } = s);
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => app.$(".page-layer:not([hidden]) .app-page");
const tabs = () => app.execute(() => [...document.querySelectorAll(".page-layer:not([hidden]) .app-page [role=tab]")].map((t) => t.textContent.trim()));
const videos = () =>
  app.execute(() =>
    [...document.querySelectorAll(".page-layer:not([hidden]) .media-card")].map((card) => ({
      kind: card.querySelector(".media-kind").textContent,
      title: card.querySelector(".media-title")?.textContent ?? null,
      thumbnail: card.querySelector(".media-play img")?.getAttribute("src") ?? null,
      player: card.querySelector("iframe")?.getAttribute("src") ?? null,
    })),
  );
const openApp = async (slug) => {
  await (await app.$("button=Browse")).click();
  await (await (await s.card(slug)).$(".card-open")).click();
  await (await page()).waitForDisplayed();
};

test("an app with videos has a Media tab after Overview, with how many", async () => {
  await openApp("test-port");
  await until(async () => (await tabs()).length === 4);
  assert.deepEqual((await tabs()).slice(0, 2), ["Overview", "Media3"]);
});

test("the videos show in the order staff set, as thumbnails, and filter by kind", async () => {
  for (const t of await (await page()).$$("button[role=tab]")) if ((await t.getText()).startsWith("Media")) await t.click();
  await until(async () => (await videos()).length === 3);
  assert.deepEqual(await videos(), [
    { kind: "Trailer", title: "Announcement trailer", thumbnail: "https://i.ytimg.com/vi/aqz-KE-bpKQ/hqdefault.jpg", player: null },
    { kind: "Gameplay", title: "First level at 60 fps", thumbnail: "https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg", player: null },
    { kind: "Gameplay", title: null, thumbnail: "https://i.ytimg.com/vi/M7lc1UVf-VE/hqdefault.jpg", player: null },
  ]);
  // The first is shown large, the rest two to a row.
  const widths = await app.execute(() => [...document.querySelectorAll(".page-layer:not([hidden]) .media-card")].map((c) => Math.round(c.getBoundingClientRect().width)));
  assert.ok(widths[0] > widths[1] * 1.8, `first is large: ${widths}`);
  assert.equal(widths[1], widths[2]);

  const filters = () => app.execute(() => [...document.querySelectorAll(".media-filters button")].map((b) => [b.textContent, b.getAttribute("aria-pressed")]));
  assert.deepEqual(await filters(), [["All3", "true"], ["Trailer1", "false"], ["Gameplay2", "false"]]);
  await (await (await app.$(".media-filters")).$("button*=Gameplay")).click();
  await until(async () => (await videos()).length === 2);
  assert.deepEqual((await videos()).map((v) => v.kind), ["Gameplay", "Gameplay"]);
  await (await (await app.$(".media-filters")).$("button*=All")).click();
  await until(async () => (await videos()).length === 3);
});

test("playing a video loads YouTube's player in its place, through the website's page for it, one at a time", async () => {
  await (await app.$('button[aria-label="Play First level at 60 fps"]')).click();
  await until(async () => (await videos())[1].player !== null);
  assert.equal((await videos())[1].player, "https://quiverlauncher.com/embed/youtube/jNQXAC9IVRw?start=90");
  assert.equal((await videos())[0].player, null);
  await (await app.$('button[aria-label="Play Announcement trailer"]')).click();
  await until(async () => (await videos())[0].player !== null);
  assert.equal((await videos())[1].player, null, "the other stopped");
  // A video with no title is named by its kind.
  assert.ok(await (await app.$('button[aria-label="Play Gameplay video"]')).isExisting());
});

test("a video's YouTube link opens it on YouTube in the browser, at its start time", async () => {
  const links = await (await page()).$$(".media-youtube");
  await links[1].click();
  await until(() => existsSync(opened) && readFileSync(opened, "utf8").includes("youtube.com"));
  assert.match(readFileSync(opened, "utf8"), /https:\/\/www\.youtube\.com\/watch\?v=jNQXAC9IVRw&t=90s/);
});

test("an app without videos has no Media tab", async () => {
  await openApp("tampered-port");
  await until(async () => (await tabs()).includes("Overview"));
  assert.ok(!(await tabs()).some((t) => t.startsWith("Media")), JSON.stringify(await tabs()));
});
