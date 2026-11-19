/**
 * A launch that can't get as far as the launcher itself: a message and a way on, never a blank window.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

test("saved state that can't be loaded shows the problem and a way to reload, and it's written to the log", async () => {
  const s = await openApp({ state: { stateFailure: "The data folder can't be read." }, ready: '[role="alert"]' });
  try {
    const alert = s.page.locator('[role="alert"]');
    assert.match(await alert.innerText(), /Quiver Launcher hit a problem/);
    assert.match(await alert.innerText(), /The data folder can't be read\./);
    await alert.getByRole("button", { name: "Reload" }).waitFor();
    const logged = await s.page.evaluate(() => window.__calls.filter((c) => c.cmd === "log_error").map((c) => c.args.message));
    assert.ok(logged.some((m) => m.includes("The data folder can't be read.")), "in quiver.log");
  } finally {
    await s.close();
  }
});

test("a damaged saved file still opens on Recovery", async () => {
  const s = await openApp({ state: { stateProblems: [{ name: "library", problem: "damaged" }] }, ready: ".recovery" });
  try {
    assert.match(await s.page.locator(".recovery").innerText(), /The file with your library is damaged\./);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
