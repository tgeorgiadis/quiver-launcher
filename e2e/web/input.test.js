/**
 * What a controller brings to every screen: the hints along the bottom, the focus's ring, and the shoulder buttons. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { app, appPage, everything, home, open, page, s, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("with a controller connected, the bottom says what its buttons do, as it labels them, and Settings hides it", async () => {
  // As gamepad.rs says a controller has come (and which), so it runs the same with none plugged in.
  const send = (event, payload) => app.execute((event, payload) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event, payload }), event, payload);
  const xbox = { kind: "xbox", south: "A", east: "B", west: "X", north: "Y" };
  const hints = () => app.execute(() => [...document.querySelectorAll(".pad-hints > span")].map((s) => s.innerText.replace(/\s+/g, " ").trim()));
  await send("pads", ["Test pad"]);
  await send("pad-labels", xbox);
  await home();
  await open("button", { text: "Library" });
  await s.until(async () => (await hints()).length > 0);
  // On a library card: A plays it, Y opens its menu.
  await app.execute(() => document.querySelector(".library-tile .card-open").focus());
  await s.until(async () => (await hints()).join(", ").startsWith("A Play, Y Menu"));
  // On a catalog card: A opens it, Y opens its menu, X gets it, with that button showing X.
  await open("button", { text: "Browse" });
  await app.execute(() => document.querySelector('article[data-slug="tampered-port"] .card-open').focus());
  await s.until(async () => (await hints()).join(", ").startsWith("A Open, Y Menu, X Get"));
  await s.until(() => app.execute(() => document.querySelector('article[data-slug="tampered-port"] .card-action .primary')?.getAttribute("data-pad-glyph") === "X"));
  await open("button", { text: "Library" });
  // It doesn't cover the page: the d-pad still gets everywhere, every move in sight above it.
  await everything("the library, with the hints");
  // In the Sidebar layout: only beside the sidebar, which runs to the window's bottom.
  const layout = async (name) => {
    await open('button[aria-label="View options"]');
    await app.execute((name) => [...document.querySelectorAll("button")].find((b) => b.innerText.trim() === name).focus(), name);
    await reach.press(app, "South");
    await reach.press(app, "East");
  };
  await layout("Sidebar");
  const at = () =>
    app.execute(() => {
      const side = document.querySelector(".library-sidebar").getBoundingClientRect();
      const top = document.querySelector(".topbar").getBoundingClientRect().height;
      return { bar: document.querySelector(".pad-hints").getBoundingClientRect().left, side: side.right, tall: side.height, room: document.documentElement.clientHeight - top };
    });
  await s.until(async () => {
    const { bar, side } = await at();
    return Math.abs(bar - side) < 2;
  });
  // As tall as the window under the top bar: not cut short for the hints.
  const { tall, room } = await at();
  assert.ok(Math.abs(tall - room) < 2, `the sidebar runs to the bottom (${tall} of ${room})`);
  // Its foot's strip and the hints beside it: their tops one line, once the page has scrolled the sidebar up under the top bar.
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, document.querySelector(".shell > main").scrollHeight));
  const tops = () => app.execute(() => [".library-sidebar .sidebar-foot", ".pad-hints"].map((s) => document.querySelector(s).getBoundingClientRect().top));
  await s.until(async () => {
    const [foot, hints] = await tops();
    return Math.abs(foot - hints) < 1.5;
  }, 5000).catch(async () => assert.fail(`the foot's top and the hints' top: ${(await tops()).join(" and ")}`));
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, 0));
  await everything("the sidebar layout, with the hints");
  await layout("Grid");
  // On a page: B goes back.
  assert.equal(await appPage(), "page: Test Port");
  await s.until(async () => (await hints()).includes("B Back"));
  // A PlayStation pad's labels.
  await send("pad-labels", { kind: "playstation", south: "Cross", east: "Circle", west: "Square", north: "Triangle" });
  await s.until(async () => (await hints()).some((h) => h.startsWith("✕ ")) && (await hints()).includes("○ Back"));
  // Settings › Controllers: its switch, with the controller, hides it and shows it again.
  await home();
  await open('button[aria-label="Settings"]');
  await open('nav[aria-label="Settings sections"] button', { text: "Controllers" });
  const showing = () => app.execute(() => Boolean(document.querySelector(".pad-hints")));
  await app.execute(() => [...document.querySelectorAll(".controls label.toggle")].find((l) => l.innerText.includes("Show what the buttons do")).querySelector("input").focus());
  await reach.press(app, "South");
  await s.until(async () => !(await showing()));
  await reach.press(app, "South");
  await s.until(showing);
  // And none connected, none shown.
  await send("pads", []);
  await s.until(async () => !(await app.execute(() => Boolean(document.querySelector(".pad-hints")))));
});

test("after a mouse click, and back in front of other windows, a controller's focus has its ring", async () => {
  await home();
  const ringed = () => app.execute(() => document.activeElement?.matches(":focus-visible") ?? false);
  // A click with the mouse, then the d-pad: the browser alone would leave the ring off.
  await page.getByRole("button", { name: "Browse", exact: true }).click();
  await reach.press(app, "DPadDown");
  assert.ok(await ringed(), "the d-pad's focus shows after a click");
  // Back in front of other windows, as the controller left it: the ring the browser dropped comes back. A script's
  // focus after a ringed one keeps the ring in WebView2 and WebKitGTK alike, so this may not drop it; either way it shows after.
  await app.execute(() => {
    const at = document.activeElement;
    at.blur();
    at.focus();
  });
  const dropped = !(await ringed());
  await app.execute(() => window.dispatchEvent(new FocusEvent("focus")));
  assert.ok(await ringed(), dropped ? "the window's focus brings it back" : "the window's focus keeps it");
});

test("outside TV mode, the shoulder buttons go between Library and Browse, as they do between TV mode's tabs", async () => {
  const showing = () => app.execute(() => document.querySelector('.topbar [data-tabs] [aria-current="page"]')?.innerText.trim() ?? "");
  assert.match(await showing(), /^Library/);
  await reach.press(app, "RightTrigger");
  await s.until(async () => /^Browse/.test(await showing()));
  await reach.press(app, "LeftTrigger");
  await s.until(async () => /^Library/.test(await showing()));
});
