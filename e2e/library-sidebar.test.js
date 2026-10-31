/**
 * The library's Sidebar layout: the library as a list of names down the
 * left, under headings by when each was played, with a search, Recently
 * played and Installed only buttons and a filters panel whose filters save as
 * a playlist; the app picked in it open beside it with the player's own play:
 * when they last played it and for how long.
 * Only where the window is wide enough: narrower, the library is a grid.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let api, s;

before(async () => {
  api = await startMockApi();
  s = await launch({ api });
  // Three apps in the library: Tampered Port's download is refused, so it stays not installed.
  for (const slug of ["test-port", "test-remake", "tampered-port"]) {
    const card = await s.card(slug);
    await card.waitForDisplayed({ timeout: 20000 });
    await (await card.$("button=Get")).click();
    await s.until(async () => (await card.$("button*=Play").isExisting()) || (await card.$('[role="alert"]').isExisting()), 30000);
  }
  await (await s.app.$("button*=Library")).click();
  await (await s.card("test-port")).waitForDisplayed();
});

after(async () => {
  await s?.close();
  api?.close();
});

const settings = () => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8"));
/** The sidebar's headings and the apps under each, by slug, as shown. */
const listed = () =>
  s.app.execute(() =>
    [...document.querySelectorAll(".sidebar-group")].map((g) => [
      g.querySelector(".sidebar-heading-name")?.textContent ?? "",
      [...g.querySelectorAll(".sidebar-row")].map((r) => r.dataset.slug),
    ]),
  );
const slugs = async () => (await listed()).flatMap(([, apps]) => apps);
/** The open app's name, beside the sidebar. */
const open = () => s.app.execute(() => document.querySelector(".library-pane .app-page h1")?.textContent);
const topbarPadding = () => s.app.execute(() => getComputedStyle(document.querySelector(".topbar")).paddingLeft);
const row = (slug) => s.app.$(`.sidebar-row[data-slug="${slug}"]`);
/**
 * View options' panel is under its button: when the sidebar's width moves the button, the panel follows it a frame
 * later (Popover in library.tsx), and a click before then lands where it was, outside it, closing it.
 */
const panelFollowed = () =>
  s.app.execute(() => {
    const button = document.querySelector(".library-sidebar button[aria-label='View options']")?.getBoundingClientRect();
    const panel = document.querySelector(".view-panel")?.getBoundingClientRect();
    return Boolean(button && panel && Math.abs(panel.left - button.left) < 1);
  });

test("the Sidebar layout lists the library down the left, the first app open beside it", async () => {
  await (await s.app.$("button[aria-label='View options']")).click();
  await (await (await s.app.$(".view-panel [aria-label='Layout']")).$("button=Sidebar")).click();
  await s.app.keys("Escape");
  await (await s.app.$(".library-sidebar")).waitForDisplayed();
  await s.until(() => settings().library?.layout === "sidebar");
  // None played yet: one heading, by name.
  assert.deepEqual(await listed(), [["Not played yet", ["tampered-port", "test-port", "test-remake"]]]);
  await s.until(async () => (await open()) === "Tampered Port");
  assert.equal(await (await row("tampered-port")).getAttribute("aria-current"), "true");
  // Not installed (its download refused): dimmed, saying so. No way back from a page that isn't over anything.
  assert.equal(await (await row("tampered-port")).getAttribute("data-installed"), "false");
  assert.match(await (await row("tampered-port")).$(".sidebar-line").getText(), /Tampered Port \(original\)\s+– Couldn't install/);
  assert.equal(await (await row("test-port")).getAttribute("data-installed"), "true");
  assert.equal(await (await row("test-port")).getAttribute("data-state"), "ready");
  assert.ok(!(await s.app.$(".library-pane .back-link").isExisting()));
  // The top bar's logo and buttons go to the window's edges, as the sidebar does; Settings is a gear.
  assert.equal(await topbarPadding(), "14px");
  assert.ok(await s.app.$("button[aria-label=Settings] svg.lucide-settings").isExisting());
  // The buttons at its foot have their icon and words in the middle.
  const offCentre = await s.app.execute(() =>
    [...document.querySelectorAll(".sidebar-foot button")].map((b) => {
      const box = b.getBoundingClientRect();
      // Its icon, words and labels, each as wide as it shows (a long label is cut short).
      const parts = [...b.childNodes].map((n) => {
        if (n.nodeType === Node.ELEMENT_NODE) return n.getBoundingClientRect();
        const range = document.createRange();
        range.selectNodeContents(n);
        return range.getBoundingClientRect();
      }).filter((r) => r.width);
      const [left, right] = [Math.min(...parts.map((r) => r.left)), Math.max(...parts.map((r) => r.right))];
      return Math.round(Math.abs((left + right) / 2 - (box.left + box.right) / 2));
    }),
  );
  assert.ok(offCentre.length && offCentre.every((d) => d <= 1), `off centre by ${offCentre}`);
  // No grid, tabs or Continue playing.
  assert.ok(!(await s.app.$(".library-grid").isExisting()) && !(await s.app.$(".library-bar").isExisting()));
});

test("picking an app opens its page; playing it puts it under Recent with when it was played", async () => {
  await (await row("test-port")).click();
  await s.until(async () => (await open()) === "Test Port");
  const stats = () => s.app.execute(() => Object.fromEntries([...document.querySelectorAll(".library-pane .play-stats > div")].map((d) => [d.querySelector("dt").textContent, d.querySelector("dd").textContent])));
  // When it was added is the sidebar's to say, not the page's.
  assert.deepEqual(await stats(), { "Last played": "Never", Installed: "v1.0.0" });
  await (await (await s.app.$(".library-pane .app-actions")).$("button*=Play")).click();
  await s.until(async () => (await stats())["Last played"] === "Today");
  // Its play time once it closes: the test app closes straight away.
  await s.until(async () => (await stats())["Play time"] === "Under a minute");
  const labels = await s.app.execute(() => [...document.querySelectorAll(".library-pane .play-stats dt")].map((d) => d.textContent));
  assert.deepEqual(labels, ["Last played", "Play time", "Installed"]);
  await s.until(async () => JSON.stringify(await listed()) === JSON.stringify([["Recent", ["test-port"]], ["Not played yet", ["tampered-port", "test-remake"]]]));
  // The page stays on the app picked.
  assert.equal(await open(), "Test Port");
  // A heading folds its apps away.
  await (await s.app.$(".sidebar-heading*=Not played yet")).click();
  await s.until(async () => (await slugs()).join() === "test-port");
  await (await s.app.$(".sidebar-heading*=Not played yet")).click();
  await s.until(async () => (await slugs()).length === 3);
});

test("a row's menu opens on a right-click or a controller's Options button, as a library card's does", async () => {
  const menu = () => s.app.$('[role="menu"][aria-label="Test Remake menu"]');
  const items = () => s.app.execute(() => [...document.querySelectorAll('[role="menu"] [role="menuitem"]')].map((b) => b.textContent));
  const focused = () => s.app.execute(() => document.activeElement?.dataset.slug ?? document.activeElement?.textContent);
  const press = async (key) => {
    for (const down of [true, false]) await s.pad(key, down);
  };
  await (await row("test-remake")).click({ button: "right" });
  await (await menu()).waitForDisplayed();
  assert.deepEqual(await items(), ["Play", "Play with", "Add to", "Manage", "App page"]);
  assert.equal(await focused(), "Play");
  // A right-click doesn't pick the app, and Escape gives the row its focus back.
  assert.equal(await open(), "Test Port");
  await s.app.keys("Escape");
  await (await menu()).waitForDisplayed({ reverse: true });
  assert.equal(await focused(), "test-remake");
  // Options on a controller opens it on the focused row; App page picks the app.
  await press("North");
  await (await menu()).waitForDisplayed();
  await s.until(async () => (await focused()) === "Play");
  await press("DPadDown");
  await press("DPadDown");
  await press("DPadDown");
  await press("DPadRight");
  await s.until(async () => (await focused()) === "Open folder");
  await press("DPadLeft");
  await press("DPadDown");
  await s.until(async () => (await focused()) === "App page");
  await press("South");
  await s.until(async () => (await open()) === "Test Remake");
  await (await menu()).waitForDisplayed({ reverse: true });
  assert.equal(await focused(), "test-remake");
});

test("Recently played, Installed only and the search narrow and order the list", async () => {
  const recent = await s.app.$("button[aria-label='Recently played first']");
  assert.equal(await recent.getAttribute("aria-pressed"), "true");
  await recent.click();
  // A to Z, under no headings.
  await s.until(async () => JSON.stringify(await listed()) === JSON.stringify([["", ["tampered-port", "test-port", "test-remake"]]]));
  // A to Z by the name shown on top: the game's.
  await s.until(() => settings().librarySort === "game");
  await recent.click();
  await s.until(() => settings().librarySort === undefined);
  await (await s.app.$("button[aria-label='Installed only']")).click();
  await s.until(async () => (await slugs()).sort().join() === "test-port,test-remake");
  await (await s.app.$("button[aria-label='Installed only']")).click();
  await s.until(async () => (await slugs()).length === 3);
  await (await s.app.$(".library-sidebar input[aria-label='Search your library']")).setValue("remake");
  await s.until(async () => (await slugs()).join() === "test-remake");
  // The page follows: the app open left the list.
  await s.until(async () => (await open()) === "Test Remake");
  await (await s.app.$(".library-sidebar button[aria-label='Clear search']")).click();
  await s.until(async () => (await slugs()).length === 3);
});

test("Left and Right move through the search's text, and Right out of it past its end", async () => {
  const search = await s.app.$(".library-sidebar input[aria-label='Search your library']");
  const focused = () => s.app.execute(() => (document.activeElement?.getAttribute("aria-label") ?? document.activeElement?.tagName));
  const caret = () => s.app.execute((el) => [el.selectionStart, el.selectionEnd], search);
  await search.setValue("te");
  // Inside the text, the caret moves and the focus stays.
  await s.app.keys("ArrowLeft");
  assert.deepEqual(await caret(), [1, 1]);
  await s.app.keys("ArrowRight");
  assert.deepEqual(await caret(), [2, 2]);
  assert.equal(await focused(), "Search your library");
  // At its end, Right goes to what's right of the field.
  await s.app.keys("ArrowRight");
  await s.until(async () => (await focused()) !== "Search your library");
  await (await s.app.$(".library-sidebar button[aria-label='Clear search']")).click();
  await s.until(async () => (await slugs()).length === 3);
});

test("each app shows its game's name over its own; View options flip them or show just one", async () => {
  const names = (slug) =>
    s.app.execute((slug) => {
      const r = document.querySelector(`.sidebar-row[data-slug="${slug}"]`);
      return [r.querySelector(".sidebar-name").textContent, r.querySelector(".sidebar-other")?.textContent ?? null];
    }, slug);
  assert.deepEqual(await names("test-port"), ["Test Port (original)", "Test Port"]);
  assert.equal(await (await row("test-port")).getAttribute("data-lines"), "2");
  await (await s.app.$(".library-sidebar button[aria-label='View options']")).click();
  const panel = await s.app.$(".view-panel");
  await (await (await panel.$("[aria-label='Name on top']")).$("button=App")).click();
  await s.until(async () => JSON.stringify(await names("test-port")) === JSON.stringify(["Test Port", "Test Port (original)"]));
  await (await panel.$("label*=Show both names")).click();
  await s.until(async () => JSON.stringify(await names("test-port")) === JSON.stringify(["Test Port", null]));
  assert.equal(await (await row("test-port")).getAttribute("data-lines"), "1");
  // One name: the game's, when picked.
  await (await (await panel.$("[aria-label='Name']")).$("button=Game")).click();
  await s.until(async () => JSON.stringify(await names("test-port")) === JSON.stringify(["Test Port (original)", null]));
  await s.until(() => settings().library?.bothNames === false);
  await (await panel.$("label*=Show both names")).click();
  await s.until(() => settings().library?.bothNames === undefined);
  await s.app.keys("Escape");
});

test("the filters panel's filters save as a playlist, which the sidebar then shows", async () => {
  await (await s.app.$(".library-sidebar button[aria-label=Filters]")).click();
  const panel = await s.app.$("[aria-label='Library filters']");
  await panel.waitForDisplayed();
  assert.equal(await (await panel.$("button*=Save as playlist")).getAttribute("disabled"), "true", "nothing to save yet");
  await (await panel.$("select[aria-label=Console]")).selectByVisibleText("Super Nintendo Entertainment System");
  await s.until(async () => (await slugs()).join() === "tampered-port");
  await (await panel.$("button*=Save as playlist")).click();
  const dialog = await s.app.$("[role=dialog][aria-label='New playlist']");
  // All lowercase: typed without Shift, which in this file came out of WebKit's driver lowercase ("SNES" saved as "snes").
  await (await dialog.$("input[aria-label='Playlist name']")).setValue("retro picks");
  await (await dialog.$("button=Save")).click();
  const show = await s.app.$(".library-sidebar select[aria-label=Show]");
  await s.until(() => s.app.execute((el) => el.selectedOptions[0]?.textContent, show).then((name) => name === "retro picks"));
  assert.deepEqual(await slugs(), ["tampered-port"]);
  assert.ok(!(await s.app.$("[aria-label='Library filters']").isExisting()), "the filters went into the playlist");
  await s.until(async () => (await open()) === "Tampered Port");
  await show.selectByVisibleText("All");
  await s.until(async () => (await slugs()).length === 3);
});

test("the sidebar's edge drags it wider or narrower, and the width stays", async () => {
  const width = () => s.app.execute(() => document.querySelector(".library-sidebar").offsetWidth);
  const pane = () => s.app.execute(() => document.querySelector(".library-pane").offsetWidth);
  const edge = await s.app.$(".sidebar-edge[role=separator]");
  // The driver can hold a mouse button down after the select the last test picked from: a press then comes as a
  // pointermove, not a pointerdown, so nothing starts dragging. Let go of everything first.
  await s.app.releaseActions();
  const [before, beside] = [await width(), await pane()];
  // What's at the edge's middle, where the drag starts, for CI's log if it doesn't move.
  const atEdge = await s.app.execute(() => {
    const r = document.querySelector(".sidebar-edge").getBoundingClientRect();
    const top = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
    return { rect: [r.left, r.top, r.width, r.height].map(Math.round), top: top?.outerHTML.slice(0, 140), window: [innerWidth, innerHeight], scrollY };
  });
  // Which pointer events reach the edge and the window during the drag, for the same log.
  await s.app.execute(() => {
    window.__drag = [];
    const edge = document.querySelector(".sidebar-edge");
    for (const type of ["pointerdown", "pointermove", "pointerup", "lostpointercapture", "mousedown"]) {
      edge.addEventListener(type, (e) => window.__drag.push(`edge ${type} ${e.button ?? ""}/${e.buttons ?? ""} ${Math.round(e.clientX)}`), true);
      window.addEventListener(type, (e) => window.__drag.length < 40 && window.__drag.push(`window ${type} ${e.target?.className ?? e.target?.nodeName}`), true);
    }
  });
  await s.app.action("pointer").move({ origin: edge }).down().move({ origin: "pointer", x: 120, y: 0, duration: 200 }).up().perform();
  try {
    await s.until(async () => (await width()) === before + 120);
  } catch (e) {
    console.log(`sidebar edge: ${JSON.stringify(atEdge)}; width ${before} -> ${await width()}; saved ${settings().sidebarWidth}; events ${JSON.stringify(await s.app.execute(() => window.__drag))}`);
    throw e;
  }
  // The app's page gives up the room.
  assert.equal(await pane(), beside - 120);
  await s.until(() => settings().sidebarWidth === before + 120);
  // Its line shows only under the pointer (or keyboard focus): gone once the pointer is off it.
  const line = () => s.app.execute(() => getComputedStyle(document.querySelector(".sidebar-edge"), "::after").backgroundColor);
  await s.app.action("pointer").move({ origin: await s.app.$(".library-pane h1"), duration: 50 }).perform();
  await s.until(async () => (await line()) === "rgba(0, 0, 0, 0)");
  // No wider than its widest.
  await s.app.action("pointer").move({ origin: edge }).down().move({ origin: "pointer", x: 600, y: 0, duration: 200 }).up().perform();
  await s.until(() => settings().sidebarWidth === 640);
  // The arrow keys move it too.
  await edge.click();
  await s.app.keys("ArrowLeft");
  await s.until(() => settings().sidebarWidth === 624);
  assert.equal(await width(), 624);
  // A double click fits it to the window again.
  await edge.doubleClick();
  await s.until(() => settings().sidebarWidth === undefined);
  await s.until(async () => (await width()) === before);
});

test("Right from the sidebar goes past its edge to the app's page; View options set its width", async () => {
  const width = () => s.app.execute(() => document.querySelector(".library-sidebar").offsetWidth);
  const before = await width();
  await (await row("test-port")).click();
  await s.app.keys("ArrowRight");
  // One press: the edge is for the mouse (and Tab), not a stop on the way.
  await s.until(() => s.app.execute(() => !!document.activeElement?.closest(".library-pane")));
  assert.equal(await width(), before, "the edge didn't move");
  // Its widths, for a controller or the keys.
  await (await s.app.$(".library-sidebar button[aria-label='View options']")).click();
  const widths = await s.app.$(".view-panel [aria-label='Sidebar width']");
  await (await widths.$("button=Wide")).click();
  await s.until(() => settings().sidebarWidth === 420);
  await s.until(async () => (await width()) === 420);
  assert.equal(await (await widths.$("button=Wide")).getAttribute("aria-pressed"), "true");
  // A busy CI runner clicked Fit the window before the panel had followed its button over, and missed it.
  await s.until(panelFollowed);
  await (await widths.$("button=Fit the window")).click();
  await s.until(() => settings().sidebarWidth === undefined);
  await s.until(async () => (await width()) === before);
  // Enter ticks and unticks a tickbox, as Confirm does (a Steam Deck's A is Enter in its desktop).
  const both = await (await (await s.app.$(".view-panel")).$("label*=Show both names")).$("input");
  const ticked = await both.isSelected();
  await s.app.execute((el) => el.focus(), both);
  await s.app.keys("Enter");
  await s.until(async () => (await both.isSelected()) === !ticked);
  await s.app.keys("Enter");
  await s.until(async () => (await both.isSelected()) === ticked);
  await s.app.keys("Escape");
});

test("a window too narrow for it shows the grid, and the sidebar comes back when it's wide again", async () => {
  const { x, y } = await s.app.getWindowRect();
  await s.app.setWindowRect(x, y, 820, 700);
  await s.until(async () => (await s.app.$(".library-grid").isExisting()) && !(await s.app.$(".library-sidebar").isExisting()));
  // The top bar stays edge to edge, as in every layout.
  assert.equal(await topbarPadding(), "14px");
  // The grid is the only layout that fits, so there's no choice to make.
  await (await s.app.$("button[aria-label='View options']")).click();
  await (await s.app.$(".view-panel")).waitForDisplayed();
  assert.ok(!(await s.app.$(".view-panel [aria-label='Layout']").isExisting()));
  await s.app.keys("Escape");
  // Still the player's choice.
  assert.equal(settings().library?.layout, "sidebar");
  await s.app.setWindowRect(x, y, 1280, 800);
  await (await s.app.$(".library-sidebar")).waitForDisplayed();
  // Saved a moment after it stops changing; the next test restarts the app, which would come back narrow.
  await s.until(() => JSON.parse(readFileSync(join(s.data, "window.json"), "utf8")).rect.width === 1280);
});

test("apps installed here but not in the library add from their page, or all at once", async () => {
  // Test Port and Test Remake taken out of the library behind the launcher's back, as another account's would be.
  const data = s.data;
  await s.close();
  const file = join(data, "library.json");
  writeFileSync(file, JSON.stringify(JSON.parse(readFileSync(file, "utf8")).filter((i) => i.slug === "tampered-port")));
  s = await launch({ api, data });
  await s.until(async () => JSON.stringify((await listed()).at(-1)) === JSON.stringify(["Installed, not in your library", ["test-port", "test-remake"]]));
  // One from its page.
  await (await row("test-remake")).click();
  await s.until(async () => (await open()) === "Test Remake");
  await (await (await s.app.$(".library-pane .app-actions")).$("button*=Add to library")).click();
  await s.until(async () => JSON.stringify((await listed()).at(-1)) === JSON.stringify(["Installed, not in your library", ["test-port"]]));
  assert.ok(!(await (await s.app.$(".library-pane .app-actions")).$("button*=Add to library").isExisting()));
  // The rest at once.
  await (await s.app.$(".sidebar-group[aria-label='Installed, not in your library'] button.sidebar-group-action")).click();
  await s.until(async () => (await slugs()).sort().join() === "tampered-port,test-port,test-remake");
  assert.ok(!(await listed()).some(([heading]) => heading === "Installed, not in your library"));
  await s.until(() => JSON.parse(readFileSync(file, "utf8")).length === 3);
});
