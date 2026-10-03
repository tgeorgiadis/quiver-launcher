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
  return s.app.execute(() => [...document.querySelectorAll("article")].map((e) => e.dataset.slug).sort());
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

test("removing an app on one computer removes it on the other, sparing installed copies", async () => {
  // B installs test-port; A then removes both apps.
  await (await (await b.card("test-port")).$("button=Get")).click();
  await (await (await b.card("test-port")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  for (const slug of ["tampered-port", "test-port"]) {
    await (await (await a.card(slug)).$(".card-open")).click();
    await (await a.app.$(`button=${slug === "test-port" ? "Uninstall and remove" : "Remove from library"}`)).click();
  }
  await b.until(async () => !(await titles(b)).includes("tampered-port"));
  const kept = await b.card("test-port");
  await (await kept.$("p=Removed on another device")).waitForDisplayed({ timeout: 10000 });
  assert.ok(existsSync(join(b.apps, "test-port")), "sync never deletes files");
  await (await kept.$("button=Keep")).click();
  await a.until(async () => (await titles(a)).includes("test-port"));
});

test("a signed-in player reviews the release they installed", async () => {
  await (await (await b.card("test-port")).$(".card-open")).click();
  await (await b.app.$("button=Runs well")).click();
  await (await b.app.$("textarea")).setValue("Smooth at 60 fps");
  await (await b.app.$("button=Post review")).click();
  await (await b.app.$("p*=your review is posted")).waitForDisplayed({ timeout: 10000 });
  assert.deepEqual(api.reviews.at(-1), {
    entryId: "entry_test-port",
    result: "runs",
    body: "Smooth at 60 fps",
    platform: "linux",
    entryReleaseId: "release_test-port_1.0.0",
    user: "player",
  });
  await b.app.keys("Escape");
});

test("signing out keeps the library, and reviewing asks to sign in", async () => {
  await (await b.app.$("button=Sign out")).click();
  assert.deepEqual(await titles(b), ["test-port"]);
  await (await (await b.card("test-port")).$(".card-open")).click();
  await (await b.app.$("button=Sign in to review")).waitForDisplayed();
});
