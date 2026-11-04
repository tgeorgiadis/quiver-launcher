/**
 * Browse with a controller: the catalog, its cards and their menus. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { app, back, everything, home, open, page, s, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("the catalog, its filters, and Sign in, which B closes", async () => {
  assert.equal(await open("button", { text: "Browse" }), "main");
  await everything("Browse");
  await open("button", { text: "Filters" });
  await everything("Browse with its filters");
  assert.equal(await open("button", { text: "Sign in" }), "dialog: Sign in");
  await everything("Sign in");
  assert.equal(await back(), "main");
  // Along the top bar: Right through each of its controls in turn, Left back, never into the page under it.
  const inBar = () => app.execute(() => [...document.querySelectorAll(".topbar button:not(.brand)")].filter((b) => b.getClientRects().length).map((b) => b.getAttribute("aria-label") || b.innerText.replace(/\s+/g, " ").trim()));
  const bar = await inBar();
  await app.execute(() => document.querySelector(".topbar button:not(.brand)").focus());
  const along = [(await reach.focused(app)).match(/"(.*)"/)[1]];
  for (let i = 1; i < bar.length; i++) await reach.press(app, "DPadRight"), along.push((await reach.focused(app)).match(/"(.*)"/)[1]);
  assert.deepEqual(along, bar, "Right goes along the top bar");
  for (let i = bar.length - 2; i >= 0; i--) {
    await reach.press(app, "DPadLeft");
    assert.equal((await reach.focused(app)).match(/"(.*)"/)[1], bar[i], "Left goes back along it");
  }
  // A card's focus: a ring round its box art, not its button's ring cut off inside the card.
  const look = await app.execute(() => {
    const button = document.querySelector("article[data-slug] .card-open");
    button.focus();
    return { outline: getComputedStyle(button).outlineStyle, art: getComputedStyle(button.closest("article").querySelector(".card-cover")).boxShadow };
  });
  assert.equal(look.outline, "none");
  assert.match(look.art, /0px 0px 0px 1px/);
});

test("A opens a catalog card, Y opens its menu with Get first, and X gets the app", async () => {
  await open("button", { text: "Browse" });
  // Down from a card skips its Get, which X on the card presses.
  await app.execute(() => document.querySelector('article[data-slug="tampered-port"] .card-open').focus());
  await reach.press(app, "DPadDown");
  assert.doesNotMatch(await reach.focused(app), /"Get"/);
  assert.equal(await open('article[data-slug="tampered-port"] .card-open'), "page: Tampered Port");
  assert.equal(await back(), "main");
  await reach.pressOn(app, 'article[data-slug="tampered-port"] .card-open', "North");
  await s.until(() => app.execute(() => Boolean(document.querySelector('[role="menu"] .game-menu-main'))));
  const items = await app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].map((b) => b.innerText.trim()));
  assert.equal(items[0], "Get");
  assert.ok(items.includes("Hide from Browse"), items.join());
  assert.equal(await app.execute(() => document.activeElement.innerText.trim()), "Get", "the focus is on Get");
  await reach.press(app, "East");
  await s.until(() => app.execute(() => !document.querySelector('[role="menu"]')));
  // It has two downloads for this computer: X asks which.
  const file = (filename) => ({ id: filename, url: `https://example.com/${filename}`, filename, os: "linux", architecture: "x64", format: "zip", checksum: `sha256:${"a".repeat(64)}` });
  s.api.setReleases("tampered-port", [{ id: "two-files", version: "1.0.0", releasedAt: Date.UTC(2026, 0, 1), prerelease: false, assets: [file("port-linux-a.zip"), file("port-linux-b.zip")] }]);
  try {
    assert.equal(await open('article[data-slug="tampered-port"] .card-open', { key: "West" }), "dialog: Choose a download");
    await everything("Choose a download");
    // A on one starts it (what it installs is the real app's to show).
    assert.doesNotMatch(await reach.pressOn(app, '[role="dialog"] button', "South", { text: "linux" }), /^not reached/);
    await s.until(() => app.execute(() => window.__calls.some((c) => c.cmd === "install")));
  } finally {
    s.api.setReleases("tampered-port", undefined);
  }
});

test("a Browse card's Read the README: the page opens at it, and the d-pad reads on from there", async () => {
  await home();
  await open("button", { text: "Browse" });
  await reach.pressOn(app, 'article[data-slug="test-remake"] .card-open', "North");
  await s.until(() => app.execute(() => Boolean(document.querySelector('[role="menu"] .game-menu-main'))));
  await app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].find((b) => b.innerText.trim() === "Read the README").focus());
  await reach.press(app, "South");
  await s.until(() => app.execute(() => Boolean(document.activeElement?.closest(".readme"))));
  const y = () => app.execute(() => document.querySelector(".shell > main").scrollTop);
  const start = await y();
  assert.ok(start > 0, "scrolled to it");
  await reach.press(app, "DPadDown");
  assert.ok((await y()) >= start, "on down the page, not back to the top");
  assert.ok(await app.execute(() => document.querySelector(".page-layer:not([hidden]) .readme").contains(document.activeElement)), "still in the README");
});

test("moving by a controller, nothing on a card eases, and a shelf glides along rather than jumping a card at a time", async () => {
  // WebKitGTK gives what eases a layer of its own while it does, and the cards it overlaps too, which on a Steam Deck
  // showed black for a frame, or another card's art.
  const easing = () =>
    app.execute(() => {
      // Without reach.js's sheet that stills everything for the other tests, which would hide what this looks for.
      const still = [...document.querySelectorAll("style")].find((s) => s.textContent.includes("transition: none !important"));
      if (still) still.disabled = true;
      const found = [];
      for (const el of document.querySelectorAll(".entry-card, .entry-card *")) {
        const style = getComputedStyle(el);
        const moving = style.transitionDuration.split(",").some((d) => parseFloat(d) > 0) || style.animationName !== "none";
        if (moving) found.push(`${el.className || el.tagName} in ${el.closest(".entry-card")?.getAttribute("data-slug")}`);
      }
      if (still) still.disabled = false;
      return [...new Set(found)];
    });
  await reach.press(app, "DPadRight");
  assert.deepEqual(await easing(), [], "the library's cards");

  await reach.press(app, "RightTrigger");
  await page.locator(".shelf-row article .card-open").first().waitFor();
  assert.doesNotMatch(await reach.pressOn(app, ".shelf-row article .card-open", "DPadRight"), /^not reached/);
  assert.deepEqual(await easing(), [], "Browse's cards");

  // A shelf of the mock catalog's (short ones) made a card a row, its first card focused, then Right: the row eases
  // on to the next, frame by frame.
  const steps = await app.execute(async () => {
    const el = [...document.querySelectorAll(".shelf-row")].find((r) => r.querySelectorAll(".card-open").length > 1);
    // A card the row wide: what Shelf fits it to again, as its height changes.
    el.style.setProperty("--shelf-card", `${el.clientWidth}px`);
    el.style.setProperty("--shelf-fit", `${el.clientWidth - 16}px`);
    await new Promise(requestAnimationFrame);
    el.querySelector(".card-open").focus();
    await new Promise(requestAnimationFrame);
    // With the motion reach.js turns off for the other tests, so the scroll eases.
    const still = window.matchMedia;
    window.matchMedia = (query) => (/prefers-reduced-motion/.test(query) ? { matches: false, media: query } : still(query));
    const seen = [el.scrollLeft];
    await window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "DPadRight", down: true } });
    await window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key: "DPadRight", down: false } });
    for (let i = 0; i < 40; i++) {
      await new Promise(requestAnimationFrame);
      seen.push(el.scrollLeft);
    }
    window.matchMedia = still;
    return { seen, snap: getComputedStyle(el).scrollSnapType };
  });
  const { seen } = steps;
  const [from, to] = [seen[0], seen.at(-1)];
  assert.ok(to > from, `the row scrolled on (${seen.join(", ")})`);
  assert.ok(new Set(seen.filter((x) => x > from && x < to)).size >= 2, `on its way, not all at once (${seen.join(", ")})`);
  assert.equal(steps.snap, "none", "and doesn't snap under it");
});

test("in Steam's desktop layout, where X isn't Quiver's, a catalog card offers its menu and doesn't mark Get with X", async () => {
  await s.emit("pads", ["Steam Deck"]);
  await s.emit("pad-labels", { kind: "steam", south: "A", east: "B", west: "X", north: "Y", desktop: true });
  await page.evaluate(() => new Promise((r) => setTimeout(r)));
  await open("button", { text: "Browse" });
  const CARD = page.locator('article[data-slug="tampered-port"]').first();
  await CARD.locator(".card-open").focus();
  const hints = () => app.execute(() => [...document.querySelectorAll(".pad-hints > span")].map((h) => h.innerText.replace(/\s+/g, " ").trim()).join(", "));
  await s.until(async () => (await hints()).startsWith("A Open, Y Menu"));
  assert.doesNotMatch(await hints(), /\bX\b/);
  assert.equal(await CARD.locator("[data-pad-glyph]").count(), 0);
});
