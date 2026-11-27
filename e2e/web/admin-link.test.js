/**
 * Edit in admin, on an app's page: only for an account that looks after the catalog on the website (a maintainer,
 * admin or the owner), and it opens the app's entry on the website's admin pages.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

/** Signs in from the top bar: with a new account, or one the site already has (made by `api.account`'s sign-up). */
const signIn = async (page, name, create = true) => {
  await page.locator(".account button").click();
  const dialog = page.locator('[role="dialog"]');
  if (create) await dialog.locator('form > button[type="button"]').last().click();
  await dialog.locator('input[autocomplete="username"]').fill(name);
  await dialog.locator(`input[autocomplete="${create ? "new-password" : "current-password"}"]`).fill("a password for the test");
  await dialog.locator("button.primary").click();
  await page.locator(".account-button").waitFor();
};

const openPage = async (page) => {
  await page.locator('article[data-slug="test-port"] .card-open').first().click();
  await page.locator(".app-page .app-actions").waitFor();
};

for (const role of ["maintainer", "admin", "owner"]) {
  test(`signed in as ${role === "owner" ? "the owner" : role === "admin" ? "an admin" : `a ${role}`}, an app's page opens its entry on the website's admin pages`, async () => {
    const own = await openApp();
    try {
      // Made staff on the website before signing in to the launcher.
      const made = await fetch(`${own.api.account}/signin`, { method: "POST", body: JSON.stringify({ username: `staff-${role}`, password: "a password for the test", create: true }) });
      assert.ok(made.ok);
      own.api.setRole(`staff-${role}`, role);
      await signIn(own.page, `staff-${role}`, false);
      await openPage(own.page);
      const edit = own.page.locator(".app-title").getByRole("button", { name: "Edit in admin" });
      assert.equal(await edit.getAttribute("title"), "Open Test Port in the catalog's admin pages on quiverlauncher.com");
      await edit.click();
      assert.deepEqual(await own.page.evaluate(() => window.__opened), ["https://quiverlauncher.com/admin/entries/entry_test-port/overview"]);
      assert.deepEqual(own.errors, []);
    } finally {
      await own.close();
    }
  });
}

test("signed out, or signed in as a player, an app's page has no Edit in admin", async () => {
  const own = await openApp();
  try {
    await openPage(own.page);
    assert.equal(await own.page.getByRole("button", { name: "Edit in admin" }).count(), 0, "signed out");
    await own.page.locator(".app-page .back-link").click();
    await signIn(own.page, "player");
    await openPage(own.page);
    // More (with Report a problem) is there for everyone; Edit in admin isn't.
    await own.page.locator(".app-actions").getByRole("button", { name: "More" }).waitFor();
    assert.equal(await own.page.getByRole("button", { name: "Edit in admin" }).count(), 0, "a player");
  } finally {
    await own.close();
  }
});
