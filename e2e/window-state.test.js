/**
 * The window comes back at the size and place it had when the launcher was
 * last open, kept in the launcher's data.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const saved = () => (existsSync(join(data, "window.json")) ? JSON.parse(readFileSync(join(data, "window.json"), "utf8")) : null);

test("the window's size and place are kept for next time", async () => {
  let s = await launch({ data });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    await s.app.setWindowRect(140, 90, 1000, 640);
    await s.until(() => saved()?.rect.width === 1000 && saved()?.rect.x === 140);
    assert.equal(saved().maximized, false);
  } finally {
    await s.close();
  }

  s = await launch({ data });
  try {
    await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
    const rect = await s.app.getWindowRect();
    assert.deepEqual([rect.x, rect.y, rect.width], [140, 90, 1000]);
    assert.ok(Math.abs(rect.height - 640) <= 40, `about 640 high, with any title bar: ${rect.height}`);
  } finally {
    await s.close();
  }
});
