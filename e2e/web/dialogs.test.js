/**
 * The dialogs that open over any page with a controller: the changelogs, Add an app, and the first start's setup. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { app, back, everything, home, installed, open, pause, state, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("Downloads' See changelog shows what's new over the page, read and closed with a controller, and leaves it there", async () => {
  const s2 = await openApp({ state: { library: state.library, installs: { "entry_test-port": { ...state.installs["entry_test-port"], version: "0.7.0" } } } });
  const { page: p, app: a } = s2;
  const notes = (version) => [`What's in ${version}.`, ...Array.from({ length: 14 }, (_, i) => `Change ${i + 1}, long enough to wrap onto a second line in a dialog this wide, as release notes do.`), `[More on ${version}](https://example.com/${version})`].join("\n\n");
  const release = (version, rest = {}) => ({ releaseId: `rel_${version}`, version, releasedAt: Date.parse(`20${version.slice(3, 4)}${version.slice(1, 2)}-01-01`), notes: notes(version), prerelease: false, state: "verified", reasons: [], assets: [], ...rest });
  const items = [release("v1.1.0", { state: "unverified" }), release("v1.0.0"), release("v0.9.0", { state: "unverified" }), release("v0.8.5", { state: "blocked" }), release("v0.8.0", { notes: "" }), release("v0.7.0"), release("v0.6.0")];
  const history = (route) => route.fulfill({ json: { items, nextCursor: null, isDone: true }, headers: { "access-control-allow-origin": "*" } });
  const seeChangelog = () => p.locator(".download-row button", { hasText: "See changelog" }).click();
  const dialog = p.locator('[role="dialog"].changelog');
  const focusText = () => a.execute(() => document.activeElement?.innerText?.trim());
  try {
    await p.route("**/release-history*", history);
    await p.locator(".downloads-button").click();
    await p.locator(".downloads-page").waitFor();
    await reach.install(a);

    assert.doesNotMatch(await reach.pressOn(a, ".download-row button", "South", { text: "See changelog" }), /^not reached/);
    await dialog.locator("article h3").first().waitFor();
    assert.deepEqual(await dialog.locator("article h3").evaluateAll((all) => all.map((h) => h.firstChild.textContent)), ["v1.0.0", "v0.9.0"], "the versions since the one installed");
    assert.equal(await p.locator(".app-page").count(), 0, "still on Downloads");
    assert.equal(await focusText(), "Close", "starting where A can't update");

    await reach.press(a, "DPadDown");
    await s2.until(() => dialog.evaluate((d) => d.scrollTop > 0));
    assert.equal(await focusText(), "Close", "the d-pad scrolls what has no control on the way");
    const { missed, outOfSight, cut } = await reach.explore(a, {});
    assert.deepEqual({ missed, outOfSight, cut }, { missed: [], outOfSight: [], cut: [] }, "the d-pad gets to the links in the notes and the buttons, each showing");

    await reach.press(a, "East");
    await dialog.waitFor({ state: "detached" });
    await p.locator(".downloads-page").waitFor();
    await s2.until(async () => (await focusText())?.includes("See changelog"));

    await seeChangelog();
    await dialog.waitFor();
    await p.keyboard.press("Escape");
    await dialog.waitFor({ state: "detached" });
    assert.equal(await p.locator(".downloads-page").isVisible(), true, "Escape closes it and not the page under it");

    await seeChangelog();
    await p.locator(".overlay").click({ position: { x: 5, y: 5 } });
    await dialog.waitFor({ state: "detached" });

    await seeChangelog();
    await dialog.locator(".changelog-other").click();
    await dialog.waitFor({ state: "detached" });
    await p.locator(".app-page .release-section").waitFor();
    await p.locator(".app-page .back-link").click();
    await p.locator(".downloads-page").waitFor();

    await p.unroute("**/release-history*");
    await p.route("**/release-history*", (route) => route.abort());
    await seeChangelog();
    await dialog.getByText("Couldn't load the release notes.").waitFor();
    await dialog.locator("button", { hasText: "Update to v1.0.0" }).click();
    await dialog.waitFor({ state: "detached" });
  } finally {
    await s2.close();
  }
});

test("the launcher's What's new shows the release's notes in the launcher, with a way on to its page", async () => {
  const launcherUpdate = { version: "4.0.0-beta.2", notes: ["## Better", "The release's own words.", ...Array.from({ length: 40 }, (_, i) => `Change ${i + 1}.`)].join("\n\n"), url: "https://example.com/release", preview: true };
  const s2 = await openApp({ state: { ...installed, launcherUpdate } });
  const { page: p, app: a } = s2;
  const dialog = p.locator('[role="dialog"].changelog');
  const opened = () => a.execute(() => window.__opened);
  try {
    await p.locator(".launcher-update").waitFor();
    await reach.install(a);
    assert.doesNotMatch(await reach.pressOn(a, ".launcher-update button", "South", { text: "What's new" }), /^not reached/);
    await dialog.waitFor();
    assert.equal(await dialog.locator("h2").first().innerText(), "What's new in Quiver Launcher");
    await dialog.getByText("The release's own words.").waitFor();
    assert.deepEqual(await opened(), [], "nothing opens outside the launcher");
    assert.equal(await dialog.evaluate((d) => d.scrollTop), 0, "open at the notes' start");
    await reach.press(a, "DPadLeft");
    assert.equal(await a.execute(() => document.activeElement?.innerText?.trim()), "View v4.0.0-beta.2 on GitHub");
    assert.equal(await dialog.evaluate((d) => d.scrollTop), 0, "moving between the buttons doesn't scroll the notes");

    await dialog.locator("button", { hasText: "View v4.0.0-beta.2 on GitHub" }).click();
    assert.deepEqual(await opened(), [launcherUpdate.url]);

    await reach.press(a, "East");
    await dialog.waitFor({ state: "detached" });
    await p.locator(".launcher-update button", { hasText: "What's new" }).click();
    await dialog.locator("button", { hasText: "Update now" }).click();
    await dialog.waitFor({ state: "detached" });
    await s2.until(() => a.execute(() => window.__calls.some((c) => c.cmd === "install_update")));
  } finally {
    await s2.close();
  }
});

test("Add an app, in the library, which B closes, and B on a dialog puts the focus back on what opened it", async () => {
  await home();
  await open("button", { text: "Library" });
  assert.equal(await open("button", { text: "Add an app" }), "dialog: Add an app");
  await everything("Add an app");
  assert.equal(await back(), "main");
  for (const [opener, dialog] of [["Add an app", "dialog: Add an app"], ["Sign in", "dialog: Sign in"]]) {
    assert.equal(await open("button", { text: opener }), dialog);
    await reach.press(app, "DPadDown");
    assert.equal(await back(), "main");
    assert.match(await reach.focused(app), new RegExp(`"${opener}"`));
  }
});

test("the first start's setup: each step all reached by the d-pad, the focus on what goes on, A going on and B back, never out", async () => {
  const first = await openApp({ state: { settings: { setupDone: false } } });
  try {
    const { app: setup } = first;
    await reach.install(setup);
    const step = () => setup.execute(() => document.querySelector('[role="dialog"][aria-label="Set up Quiver Launcher"] h2')?.innerText ?? null);
    const goesTo = async (title) => {
      for (let i = 0; i < 30 && (await step()) !== title; i++) await pause(100);
      assert.equal(await step(), title);
    };
    await goesTo("Welcome to Quiver Launcher!");
    assert.match(await reach.focused(setup), /"Next"/, "A goes on from where the focus lands");
    // B on the first step stays: Skip setup is the way out.
    await reach.press(setup, "East");
    await pause(200);
    assert.equal(await step(), "Welcome to Quiver Launcher!");
    // Usage data is a step only in a build that sends it (VITE_USAGE_DATA), which CI's web job isn't.
    const total = Number(await setup.execute(() => document.querySelector(".setup-count")?.innerText.match(/(\d+)\s*$/)?.[1]));
    const titles = ["Welcome to Quiver Launcher!", "Coming from Quiver Launcher 3?", "Where apps install", "Usage data", "Sign in to sync your library"].filter((t) => total === 5 || t !== "Usage data");
    assert.equal(titles.length, total);
    for (const [i, title] of titles.entries()) {
      await goesTo(title);
      await everything(`setup: ${title}`);
      if (i === 1) {
        // B goes back a step, and A on Next comes back to this one.
        await reach.press(setup, "East");
        await goesTo(titles[0]);
        await reach.pressOn(setup, ".setup-next", "South");
        await goesTo(title);
      }
      if (i < titles.length - 1) assert.doesNotMatch(await reach.pressOn(setup, ".setup-next", "South"), /^not reached/);
    }
    assert.doesNotMatch(await reach.pressOn(setup, ".setup-next", "South"), /^not reached/);
    for (let i = 0; i < 30 && (await step()) !== null; i++) await pause(100);
    assert.equal(await step(), null, "Start using Quiver closes it");
  } finally {
    await first.close();
  }
});
