/**
 * Settings and Downloads with a controller, and the library's background Settings chooses. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { openApp } from "./app.js";
import { app, back, everything, installed, open, page, pause, s, where, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("into Settings' list from the section beside it, the d-pad lands on the section open", async () => {
  await page.locator(".topbar button[aria-label='Settings']").click();
  await page.locator(".settings-nav").waitFor();
  await page.locator('.settings-nav [data-section="updates"]').click();
  await pause(100);
  await reach.pressOn(app, ".settings-pane button, .settings-pane input, .settings-pane select", "DPadLeft");
  assert.equal(await app.execute(() => document.activeElement?.getAttribute("data-section")), "updates");
});

test("the player's picture shows behind the library's grid and nowhere else, and Settings chooses it", async () => {
  const picture = "/home/player/Pictures/wall.png";
  const s2 = await openApp({ state: { ...installed, pickBackground: picture } });
  const { page: p, app: a } = s2;
  const behind = p.locator(".library-background img");
  const settingsOpen = async (name) => {
    await p.locator(".topbar button[aria-label='Settings']").click();
    await p.locator(".settings-nav button", { hasText: name }).first().click();
  };
  try {
    await p.locator(".library-grid").waitFor();
    assert.equal(await behind.count(), 0, "none until the player chooses one");

    await settingsOpen("Library");
    const setting = p.locator('section[aria-label="Library background"]');
    await setting.waitFor();
    await reach.install(a);
    assert.doesNotMatch(await reach.pressOn(a, 'section[aria-label="Library background"] button', "South", { text: "Choose a picture…" }), /^not reached/);
    await setting.getByText("wall.png").waitFor();
    const preview = setting.locator(".background-preview img");
    await preview.waitFor();
    await setting.locator('input[type="range"]').fill("60");
    await s2.until(() => preview.evaluate((i) => i.style.opacity === "0.6"));

    await s2.page.locator(".topbar nav button", { hasText: "Library" }).click();
    await behind.waitFor({ state: "attached" });
    assert.equal(await behind.evaluate((i) => i.style.opacity), "0.6");
    const placed = await behind.evaluate((i) => {
      const bar = document.querySelector(".topbar").getBoundingClientRect();
      const card = document.querySelector(".library-tile").getBoundingClientRect();
      const box = i.getBoundingClientRect();
      return { wide: box.width === innerWidth, tall: box.height === innerHeight, shown: i.naturalWidth > 0, overBar: document.elementFromPoint(bar.x + bar.width / 2, bar.y + bar.height / 2) === i, overCard: document.elementFromPoint(card.x + card.width / 2, card.y + card.height / 2) === i };
    });
    assert.deepEqual(placed, { wide: true, tall: true, shown: true, overBar: false, overCard: false });
    const scroller = p.locator(".shell > main");
    await scroller.evaluate((m) => m.scrollTo(0, 200));
    assert.equal(await behind.evaluate((i) => i.getBoundingClientRect().y), 0, "stays put as the cards scroll");

    await p.locator(".topbar nav button", { hasText: "Browse" }).click();
    await behind.waitFor({ state: "detached" });
    await p.locator(".topbar nav button", { hasText: "Library" }).click();
    await behind.waitFor({ state: "attached" });
    await p.locator(".library-tile").first().click();
    await p.locator(".app-page").first().waitFor();
    assert.equal(await behind.isVisible(), false, "not behind an app's page");
    await reach.press(a, "East");
    await behind.waitFor();

    await settingsOpen("Library");
    await setting.locator("button", { hasText: "Remove" }).click();
    await setting.locator("button", { hasText: "Choose a picture…" }).waitFor();
    await p.locator(".topbar nav button", { hasText: "Library" }).click();
    await p.locator(".library-grid").waitFor();
    assert.equal(await behind.count(), 0);
  } finally {
    await s2.close();
  }

  // The Sidebar layout keeps its own background, with the picture still chosen for the grid.
  const s3 = await openApp({ state: { ...installed, settings: { libraryBackground: picture, library: { layout: "sidebar" } } } });
  try {
    await s3.page.locator(".library-split").waitFor();
    assert.equal(await s3.page.locator(".library-background").count(), 0);
  } finally {
    await s3.close();
  }

  // A file moved or deleted since: Settings says so, and the library goes on without it.
  const s4 = await openApp({ state: { ...installed, settings: { libraryBackground: "/home/player/Pictures/missing.png" } } });
  try {
    await s4.page.locator(".library-grid").waitFor();
    assert.equal(await s4.page.locator(".library-background").count(), 0);
    await s4.page.locator(".topbar button[aria-label='Settings']").click();
    await s4.page.locator(".settings-nav button", { hasText: "Library" }).first().click();
    await s4.page.getByText("Quiver couldn't show that picture.", { exact: false }).waitFor();
  } finally {
    await s4.close();
  }
});

test("Settings' list stays on the screen as the page scrolls, in a window with just room for it", async () => {
  const s2 = await openApp({ state: installed, height: 345 });
  try {
    await s2.page.locator(".topbar").waitFor();
    await s2.page.locator(".topbar button[aria-label='Settings']").click();
    await s2.page.locator(".settings-nav").waitFor();
    for (const section of ["general", "updates"]) {
      await s2.page.locator(`.settings-nav [data-section="${section}"]`).click();
      await pause(100);
      const over = await s2.app.execute(() => {
        const main = document.querySelector(".shell > main");
        const list = document.querySelector(".settings-nav");
        main.scrollTop = main.scrollHeight;
        return [list.getBoundingClientRect().top - main.getBoundingClientRect().top, main.clientHeight - list.offsetHeight];
      });
      assert.ok(over[1] >= 0, "the window has room for all of it");
      assert.ok(over[0] >= 0, `${section}: ${over[0]}px from the top`);
    }
  } finally {
    await s2.close();
  }
});

test("Downloads, and every section of Settings", async () => {
  assert.equal(await open('button[aria-label="Downloads"]'), "page: Downloads");
  await everything("Downloads");
  assert.equal(await back(), "main");
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  const sections = await app.execute(() => [...document.querySelectorAll('nav[aria-label="Settings sections"] button')].map((b) => b.innerText.trim()));
  for (const name of sections) {
    await open('nav[aria-label="Settings sections"] button', { text: name });
    await everything(`Settings › ${name}`);
  }
});

test("a controller rebinds a control with its own buttons", async () => {
  const binding = 'button[aria-label="Confirm / Select: controller"]';
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  await open('nav[aria-label="Settings sections"] button', { text: "Controllers" });
  await open(binding);
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "Press a button…");
  await reach.press(app, "West");
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "X");
  // X is Confirm now: it rebinds it back to A.
  await app.execute((b) => document.querySelector(b).focus(), binding);
  await reach.press(app, "West");
  await reach.press(app, "South");
  assert.equal(await app.execute((b) => document.querySelector(b).innerText, binding), "A");
  // Swap A and B, with the controller: A swaps them, and then it's B that confirms, so B swaps them back.
  const cells = () => app.execute(() => ["Confirm / Select", "Cancel / Back"].map((a) => document.querySelector(`button[aria-label="${a}: controller"]`).innerText));
  await app.execute(() => [...document.querySelectorAll(".controls label.toggle")].find((l) => l.innerText.startsWith("Swap ")).querySelector("input").focus());
  await reach.press(app, "South");
  await s.until(async () => (await cells()).join() === "B,A");
  await reach.press(app, "East");
  await s.until(async () => (await cells()).join() === "A,B");
});

test("A on a dropdown on a page opens its options, and B closes them without going back a page", async () => {
  assert.equal(await open('button[aria-label="Settings"]'), "page: Settings");
  await open('nav[aria-label="Settings sections"] button', { text: "General" });
  assert.doesNotMatch(await reach.pressOn(app, ".settings-pane select", "South"), /^not reached/);
  assert.ok((await page.locator(".select-picker").count()) > 0, "A opens the list");
  await reach.press(app, "East");
  assert.ok(!(await page.locator(".select-picker").count()), "B closes the list");
  assert.equal(await where(), "page: Settings", "and the page stays");
  assert.match(await reach.focused(app), /^select /);
});
