/**
 * Add an app, From a git repository, with a link to an app's page on quiverlauncher.com pasted in: the catalog is
 * asked for that app alone, and its page opens, as a repository the catalog lists does.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

/** Opens Add an app from the library and looks `input` up From a git repository. */
async function lookUp(page, input) {
  await page.locator("button", { hasText: "Library" }).first().click();
  await page.getByRole("button", { name: "Add an app" }).first().click();
  const dialog = page.locator('[role="dialog"]');
  await dialog.locator("button", { hasText: "From a git repository" }).click();
  await dialog.getByLabel("Repository").fill(input);
  await dialog.locator("button.primary").click();
  return dialog;
}

test("a link to an app's page on quiverlauncher.com opens that app's page", async () => {
  const s = await openApp();
  try {
    await lookUp(s.page, "https://quiverlauncher.com/apps/test-port/");
    await s.page.locator(".app-page .app-title", { hasText: "Test Port" }).waitFor();
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("a link to an app the catalog doesn't have says so", async () => {
  const s = await openApp();
  try {
    const dialog = await lookUp(s.page, "quiverlauncher.com/apps/no-such-app");
    await dialog.locator(".job-error", { hasText: "Quiver's catalog has no app at that address." }).waitFor();
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
