/**
 * The page of an app from Browse with a controller: its README, feedback box and versions. (controller.js has what these share.)
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { app, back, home, open, page, pause, s, where, useSharedApp } from "./controller.js";
import * as reach from "../reach.js";

useSharedApp();

test("a long README: the d-pad reads through it, the right stick and Page Down scroll it, and A on what's out of sight shows it", async () => {
  await home();
  await open("button", { text: "Browse" });
  assert.equal(await open('article[data-slug="test-remake"] .card-open'), "page: Test Remake");
  const y = () => app.execute(() => document.querySelector(".shell > main").scrollTop);
  const inSight = () => app.execute(() => window.__reach.inSight(document.activeElement));
  const view = await app.execute(() => document.documentElement.clientHeight);
  // Down a press at a time: a step at most, through the text rather than past it, to the link at its end.
  let steps = 0;
  for (let i = 0; i < 40 && !(await reach.focused(app)).includes("the notes"); i++) {
    // At the fold, Read more: A opens the rest, and the d-pad reads on from there.
    if ((await reach.focused(app)).startsWith('button "Read more"')) await reach.press(app, "South");
    const before = await y();
    await reach.press(app, "DPadDown");
    const by = (await y()) - before;
    assert.ok(by < view * 0.6, `a step, not a jump (${by}px of ${view})`);
    if (by > 0 && !(await reach.focused(app)).includes("the notes")) steps++;
  }
  assert.equal(await reach.focused(app), 'a "the notes" in Test Remake');
  assert.ok(await inSight());
  assert.ok(steps >= 2, `it stepped through the text (${steps} steps)`);
  // The right stick held: a smooth scroll for as long as it is, which stops when it's let go.
  const hold = (key, down) => app.execute((key, down) => window.__TAURI_INTERNALS__.invoke("plugin:event|emit", { event: "pad", payload: { key, down } }), key, down);
  const bottom = await y();
  await hold("RightStickUp", true);
  await pause(500);
  await hold("RightStickUp", false);
  // Where it stopped, once the page has heard it let go (events come after the call that sends them, as Tauri's do).
  let glided = await y();
  await s.until(async () => glided === (glided = await y()));
  assert.ok(glided < bottom - 100, `it scrolled up (${bottom} to ${glided})`);
  await pause(300);
  assert.equal(await y(), glided);
  // A on the link, now out of sight: back into view, not opened unseen.
  assert.equal(await inSight(), false);
  await reach.press(app, "South");
  await s.until(inSight);
  assert.equal(await where(), "page: Test Remake");
  // Page Down: most of a screen.
  await app.execute(() => document.querySelector(".shell > main").scrollTo(0, 0));
  await page.keyboard.press("PageDown");
  await s.until(async () => (await y()) > view * 0.5);
});

test("B closes an app page's feedback box, wherever the focus is, and only then goes back", async () => {
  await home();
  await open("button", { text: "Browse" });
  assert.equal(await open('article[data-slug="test-remake"] .card-open'), "page: Test Remake");
  const boxOpen = () => app.execute(() => Boolean(document.querySelector(".feedback-box .report-prompt")));
  const openBox = async () => {
    await app.execute(() => document.querySelector(".feedback-box-toggle").focus());
    await reach.press(app, "South");
    await s.until(boxOpen);
  };
  await openBox();
  await app.execute(() => document.querySelector(".report-answer").focus());
  await reach.press(app, "East");
  await s.until(async () => !(await boxOpen()));
  assert.equal(await where(), "page: Test Remake");
  assert.ok(await app.execute(() => document.activeElement.matches(".feedback-box-toggle")), "the focus is back on its button");

  await openBox();
  await app.execute(() => document.querySelector('[aria-label="App sections"] button').focus());
  await reach.press(app, "East");
  await s.until(async () => !(await boxOpen()));
  assert.equal(await where(), "page: Test Remake");
  assert.equal(await back(), "main");
});

test("Get's versions: one scrollbar, and the focus box stays on the focus as the list comes and as Get's button widens", async () => {
  const off = () =>
    app.execute(() => {
      const [box, el] = [document.querySelector(".pad-halo"), document.activeElement].map((n) => n.getBoundingClientRect());
      return [box.left + box.width / 2 - (el.left + el.width / 2), box.top + box.height / 2 - (el.top + el.height / 2), box.width - el.width];
    });
  const onFocus = () =>
    s.until(async () => {
      const [x, y, wider] = await off();
      return Math.abs(x) < 2 && Math.abs(y) < 2 && wider > 0 && wider < 12;
    }).catch(async () => assert.fail(`the focus box is off what has the focus by ${JSON.stringify(await off())}`));
  await s.emit("pads", ["Test pad"]);
  await home();
  await open("button", { text: "Browse" });
  assert.equal(await open('article[data-slug="test-remake"] .card-open'), "page: Test Remake");
  // The list a moment behind the dialog, which it then makes taller.
  let release;
  const held = new Promise((r) => (release = r));
  const hold = async (route) => {
    const request = route.request();
    if (request.url().includes("releases") || (request.postData() ?? "").includes("catalog:releases")) await held;
    await route.continue();
  };
  await page.route("**/*", hold);
  try {
    await reach.pressOn(app, ".app-actions .get-more", "South");
    await s.until(() => app.execute(() => document.activeElement?.closest('[role="dialog"]') && document.querySelector('[role="dialog"] .spinner')));
    await onFocus();
    const before = await app.execute(() => document.activeElement.getBoundingClientRect().top);
    release();
    await s.until(() => app.execute(() => Boolean(document.querySelector('[role="dialog"] .versions ul'))));
    assert.ok((await app.execute(() => document.activeElement.getBoundingClientRect().top)) < before, "the list moved the button");
    await onFocus();
  } finally {
    release();
    await page.unroute("**/*", hold);
  }
  // However long the list, only the list scrolls.
  await app.execute(() => {
    const list = document.querySelector('[role="dialog"] .versions ul');
    for (let i = 0; i < 40; i++) list.append(list.firstElementChild.cloneNode(true));
  });
  const [window_, body, list] = await app.execute(() =>
    ['[role="dialog"]', '[role="dialog"] .versions-dialog', '[role="dialog"] .versions ul'].map((q) => {
      const el = document.querySelector(q);
      return { scroll: el.scrollHeight, client: el.clientHeight, overflow: getComputedStyle(el).overflowY };
    }),
  );
  assert.equal(window_.overflow, "hidden", "the window has no scrollbar");
  assert.ok(body.client <= window_.client, `all of it fits in the window (${body.client} in ${window_.client})`);
  assert.ok(list.scroll > list.client, `the list scrolls (${JSON.stringify(list)})`);
  await s.page.reload();
  await s.emit("pads", []);
});

test("Back on the Get button: the focus box takes in the glyph it gains as the versions close", async () => {
  await s.emit("pads", ["Test pad"]);
  await s.emit("pad-labels", { kind: "xbox", south: "A", east: "B", west: "X", north: "Y" });
  await home();
  await open("button", { text: "Browse" });
  assert.equal(await open('article[data-slug="test-remake"] .card-open'), "page: Test Remake");
  await reach.pressOn(app, ".app-actions .get-more", "South");
  await s.until(() => app.execute(() => Boolean(document.activeElement?.closest('[role="dialog"]'))));
  await reach.press(app, "East");
  await s.until(() => app.execute(() => !document.querySelector('[role="dialog"]') && Boolean(document.querySelector(".app-actions [data-pad-glyph]"))));
  await s.until(() =>
    app.execute(() => {
      const [box, el] = [document.querySelector(".pad-halo"), document.activeElement].map((n) => n.getBoundingClientRect());
      return Math.abs(box.left + box.width / 2 - (el.left + el.width / 2)) < 2 && box.width - el.width < 12 && box.width - el.width > 0;
    }),
  );
  await s.emit("pads", []);
});
