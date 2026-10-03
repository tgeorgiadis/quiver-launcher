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
  await (await a.app.$("h2=My Port")).waitForDisplayed();
  await a.app.keys("Escape");
  await titles(b);
  await b.until(async () => (await (await (await b.card("test-port")).$(".card-open")).getAttribute("aria-label")) === "My Port");
});

test("a collection made on one computer shows on the other", async () => {
  await (await a.app.$("button[aria-label='New collection']")).click();
  await (await a.app.$("input[aria-label='Collection name']")).setValue("Co-op");
  await (await a.app.$("button=Save")).click();
  await titles(b);
  await (await (await b.app.$("nav[aria-label=Shelves]")).$("button=Co-op")).waitForDisplayed({ timeout: 10000 });
  await (await (await a.app.$("nav[aria-label=Shelves]")).$("button=All")).click();
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

test("signing out takes the library along, and signing back in brings it back", async () => {
  await (await b.app.$("button=Sign out")).click();
  await (await b.app.$("h2=Installed, not in your library")).waitForDisplayed();
  assert.ok(existsSync(join(b.apps, "test-port")), "installed files stay");
  await (await (await b.card("test-port")).$(".card-open")).click();
  await (await b.app.$("button=Sign in to review")).waitForDisplayed();
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
});
