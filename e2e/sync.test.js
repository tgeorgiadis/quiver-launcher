/** Optional sign-in: two computers, one account, one library. */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let api, a, b;
const titles = async (s) => {
  await (await s.app.$("button*=Library")).click();
  return s.app.execute(() => [...document.querySelectorAll("article")].filter((e) => !e.closest(".loose")).map((e) => e.dataset.slug).sort());
};
const signIn = async (s, create) => {
  await (await s.app.$("button=Sign in")).click();
  if (create) await (await s.app.$("button=Create an account")).click();
  await (await s.app.$("input[placeholder=Username]")).setValue("player");
  await (await s.app.$("input[placeholder=Password]")).setValue("correct horse battery");
  await (await s.app.$(`form button.primary`)).click();
  await (await s.app.$("button=Sign out")).waitForDisplayed({ timeout: 10000 });
};

before(async () => {
  api = await startMockApi();
});

after(async () => {
  await a?.close();
  await b?.close();
  api?.close();
});

test("a library built signed out is still there after a restart", async () => {
  a = await launch({ api });
  await (await (await a.card("test-port")).$("button=Get")).click();
  await (await (await a.card("test-port")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  const data = a.data;
  await a.close();
  a = await launch({ api, data });
  assert.deepEqual(await titles(a), ["test-port"]);
});

test("signing in on two computers combines their libraries on both", async () => {
  b = await launch({ api, port: 4454 });
  await (await (await b.card("tampered-port")).$("button=Get")).click();
  await (await (await b.card("tampered-port")).$('[role="alert"]')).waitForDisplayed({ timeout: 30000 });
  await signIn(a, true);
  await signIn(b, false);
  await a.until(async () => (await titles(a)).length === 2);
  await b.until(async () => (await titles(b)).length === 2);
  assert.deepEqual(await titles(b), ["tampered-port", "test-port"]);
});

test("renaming an app on one computer renames it on the other", async () => {
  await (await (await a.card("test-port")).$(".card-open")).click();
  await (await a.app.$("button=Change name or artwork")).click();
  await (await a.app.$("input[aria-label=Name]")).setValue("My Port");
  await (await a.app.$("button=Save")).click();
  await (await a.app.$("h1=My Port")).waitForDisplayed();
  await a.app.keys("Escape");
  await titles(b);
  await b.until(async () => (await (await (await b.card("test-port")).$(".card-open")).getAttribute("aria-label")) === "My Port");
});

test("a collection made on one computer shows on the other", async () => {
  await (await a.app.$("button[aria-label='Add a playlist']")).click();
  await (await (await a.app.$("[role=menu]")).$("button=New playlist")).click();
  await (await a.app.$("input[aria-label='Playlist name']")).setValue("Co-op");
  await (await a.app.$("button=Save")).click();
  await titles(b);
  await (await (await b.app.$("nav[aria-label=Playlists]")).$("button=Co-op")).waitForDisplayed({ timeout: 10000 });
  await (await (await a.app.$("nav[aria-label=Playlists]")).$("button=All")).click();
});

test("removing an app on one computer removes it on the other, keeping installed files", async () => {
  // B installs test-port; A then removes both apps.
  await (await (await b.card("test-port")).$("button=Get")).click();
  await (await (await b.card("test-port")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  for (const slug of ["tampered-port", "test-port"]) {
    await (await (await a.card(slug)).$(".card-open")).click();
    await (await a.app.$(`button=${slug === "test-port" ? "Uninstall and remove" : "Remove from library"}`)).click();
  }
  await b.until(async () => (await titles(b)).length === 0);
  await (await b.app.$("h2=Installed, not in your library")).waitForDisplayed();
  assert.ok(existsSync(join(b.apps, "test-port")), "sync never deletes files");
  await (await (await b.card("test-port")).$("button=Add")).click();
  await a.until(async () => (await titles(a)).includes("test-port"));
});

test("a signed-in player reviews the release they installed", async () => {
  await (await (await b.card("test-port")).$(".card-open")).click();
  await (await b.app.$("button*=Player feedback")).click();
  await (await b.app.$("button*=Share how it ran")).click();
  // The release they have is the one they say they tested.
  await (await b.app.$("h3=How did it go?")).waitForDisplayed({ timeout: 10000 });
  await (await b.app.$("textarea")).setValue("Smooth at 60 fps");
  await (await b.app.$("button=Share")).click();
  await (await b.app.$("p*=You've shared how it ran")).waitForDisplayed({ timeout: 10000 });
  assert.deepEqual(api.reviews.at(-1), {
    entryId: "entry_test-port",
    result: "runs",
    body: "Smooth at 60 fps",
    platform: process.platform === "win32" ? "windows" : "linux",
    entryReleaseId: "release_test-port_1.0.0",
    user: "player",
  });
  await b.app.keys("Escape");
});

test("Browse opens with the filters saved on the website, keeping this computer's platform", async () => {
  const browse = async () => {
    await (await a.app.$("button*=Library")).click();
    await (await a.app.$("button*=Browse")).click();
  };
  const os = process.platform === "win32" ? "windows" : "linux";
  api.setCatalogDefaults("player", { projectTypes: ["port"], console: "n64", sort: "rating", platforms: ["android"] });
  await a.until(async () => (await browse(), api.lastQuery().get("console") === "n64"));
  assert.deepEqual(Object.fromEntries(api.lastQuery()), { os, projectType: "port", console: "n64", sort: "rating" });
  assert.equal(await (await a.app.$(".browse-tools .tool-count")).getText(), "2");
  // Several kinds of project at once, which only the website can save.
  api.setCatalogDefaults("player", { projectTypes: ["port", "game"] });
  await a.until(() => api.lastQuery().get("projectTypes") === "port,game");
  // Removing one here wins over the saved filters until Browse opens again.
  await (await a.app.$("button[aria-label='Remove filter Port, Standalone game']")).click();
  await a.until(() => !api.lastQuery().has("projectTypes"));
  api.setCatalogDefaults("player", undefined);
  await browse();
  await a.until(() => [...api.lastQuery().keys()].join() === "os,sort");
});

test("signing out keeps the apps installed here in the library, and signing back in carries on", async () => {
  await (await b.app.$("button=Sign out")).click();
  await (await b.app.$("p*=Apps installed on this computer stay in your library")).waitForDisplayed();
  assert.deepEqual(await titles(b), ["test-port"]);
  assert.equal(await (await b.app.$("h2=Installed, not in your library")).isExisting(), false);
  assert.ok(existsSync(join(b.apps, "test-port")), "installed files stay");
  await (await (await b.card("test-port")).$(".card-open")).click();
  await (await b.app.$("button*=Player feedback")).click();
  await (await b.app.$("p=Sign in to share how it runs for you.")).waitForDisplayed();
  await b.app.keys("Escape");
  await signIn(b, false);
  await b.until(async () => (await titles(b)).includes("test-port"));
  await (await (await b.card("test-port")).$("button*=Play")).waitForDisplayed();
  await (await b.app.$("button=Sign out")).click();
});

test("signing in with GitHub happens in the browser and comes back to the app", async () => {
  await (await b.app.$("button=Sign in")).click();
  await (await b.app.$("button=Continue with GitHub")).click();
  try {
    await (await b.app.$("span=octocat")).waitForDisplayed({ timeout: 10000 });
  } catch (e) {
    console.log("BODY", await b.app.execute(() => document.body.innerText.slice(0, 1500)));
    throw e;
  }
  // The browser tab ends on the website's page, not the launcher's loopback address.
  await b.until(async () => api.signedInPages.length > 0);
  assert.deepEqual(api.signedInPages, [""]);
});
