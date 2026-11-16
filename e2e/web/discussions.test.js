/**
 * Discussions (discussions.tsx) with a controller: the top bar's Community page and an app's Discussions tab. The
 * stand-in account has no live site, so both say discussions aren't available; what's tested is that they open, the
 * d-pad reaches all of them and B leaves them.
 */
import { test } from "node:test";
import assert from "node:assert/strict";
import { appPage, back, everything, open, page, s, section, useSharedApp } from "./controller.js";

useSharedApp();

test("the Community page opens from the top bar, and an app's page has a Discussions tab", async () => {
  assert.equal(await open("button.community-button"), "page: Community");
  for (const name of ["Recent", "General"]) assert.equal(await page.getByRole("tab", { name }).count(), 1, `the ${name} tab`);
  // Help with Quiver Launcher and Requests went: launcher help is on Discord and GitHub Issues.
  assert.equal(await page.getByRole("tab", { name: "Requests" }).count(), 0);
  assert.equal(await page.getByText("Discussions aren't available right now.").count(), 1);
  await everything("Community");
  assert.equal(await back(), "main");
  assert.equal(await appPage(), "page: Test Port");
  await section("Discussions");
  await everything("its Discussions");
  assert.deepEqual(s.errors, []);
});
