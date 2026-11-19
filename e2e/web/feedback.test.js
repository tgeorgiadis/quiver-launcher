/**
 * Sharing feedback on an app: the form starts on the installed release, asks for one (or Not sure) when it can't
 * tell, and an "It ran, but I had issues" or "It didn't run" answer needs a note before it's shared.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

after(stop);

const ID = "entry_test-port";
const RELEASE = "release_test-port_1.0.0";

/** Test Port's Player feedback tab, signed in, with its form open. */
async function form(installs) {
  const s = await openApp({
    state: { library: [{ id: ID, slug: "test-port", addedAt: 1 }], installs },
    config: { os: "linux", osVersion: "steamos 3.7.13", pcSpecs: { cpu: "AMD Custom APU 0405", gpu: "AMD Custom GPU 0405", ramGb: 14.5 } },
  });
  const { page } = s;
  await page.locator('article[data-slug="test-port"] .card-open').first().click();
  await page.getByRole("tab", { name: /Player feedback/ }).click();
  await page.getByRole("button", { name: "Share how it ran" }).click();
  const dialog = page.locator('[role="dialog"]');
  await dialog.locator('form > button[type="button"]').last().click();
  await dialog.locator('input[autocomplete="username"]').fill("feedback");
  await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
  await dialog.locator("button.primary").click();
  const editor = page.locator(".review-editor");
  await editor.waitFor();
  // The releases listed, so the form can start on the installed one.
  await editor.locator(`select option[value="${RELEASE}"]`).waitFor({ state: "attached" });
  return { ...s, editor, release: editor.locator("select").nth(1), note: editor.locator("textarea") };
}
const share = (editor) => editor.getByRole("button", { name: "Share", exact: true }).click();
const invalid = (field) => field.evaluate((el) => !el.validity.valid);

test(`an "It didn't run" answer needs a note, and the installed release is the one it names`, async () => {
  const s = await form({ [ID]: { version: "1.0.0", releaseId: RELEASE, folder: "test-port", executables: [] } });
  try {
    const { editor, release, note } = s;
    assert.equal(await release.inputValue(), RELEASE, "the installed release, to start with");
    await editor.getByRole("button", { name: /It didn't run/ }).click();
    await share(editor);
    assert.ok(await invalid(note), "a note is asked for");
    assert.match(await note.getAttribute("placeholder"), /What went wrong/);
    assert.equal(s.api.reviews.length, 0, "nothing was shared");
    await note.fill("It closes at the title screen.");
    await share(editor);
    await editor.waitFor({ state: "detached" });
    const [sent] = s.api.reviews;
    assert.deepEqual(
      { result: sent.result, body: sent.body, platform: sent.platform, entryReleaseId: sent.entryReleaseId },
      { result: "broken", body: "It closes at the title screen.", platform: "linux", entryReleaseId: RELEASE },
    );
    // This computer's PC specs go with it, shown to other players.
    assert.deepEqual(sent.specs, { osVersion: "SteamOS 3.7.13", cpu: "AMD Custom APU 0405", gpu: "AMD Custom GPU 0405", ramGb: 14.5 });
    assert.equal(sent.specsPublic, true);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test(`not installed, the release is picked, or Not sure, before it's shared; an "It worked well" answer needs no note`, async () => {
  const s = await form({});
  try {
    const { editor, release, note } = s;
    assert.equal(await release.inputValue(), "", "no release picked for the player");
    await editor.getByRole("button", { name: /It worked well/ }).click();
    await share(editor);
    assert.ok(await invalid(release), "a release is asked for");
    assert.ok(!(await invalid(note)), "but no note");
    assert.equal(s.api.reviews.length, 0, "nothing was shared");
    await release.selectOption({ label: "Not sure" });
    await share(editor);
    await editor.waitFor({ state: "detached" });
    const [sent] = s.api.reviews;
    assert.equal(sent.result, "runs");
    assert.equal(sent.entryReleaseId, undefined, "Not sure names no release");
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});

test("PC specs can be kept from other players, and aren't sent for feedback on another platform", async () => {
  const s = await form({ [ID]: { version: "1.0.0", releaseId: RELEASE, folder: "test-port", executables: [] } });
  try {
    const { page, editor } = s;
    const box = editor.getByRole("checkbox", { name: "Show my PC specs to other players" });
    assert.ok(await box.isChecked(), "shown, to start with");
    await editor.locator(".review-specs").filter({ hasText: "SteamOS 3.7.13 · AMD Custom APU 0405 · AMD Custom GPU 0405 · 14.5 GB RAM" }).waitFor();
    await box.uncheck();
    await editor.getByRole("button", { name: "Share", exact: true }).click();
    await editor.waitFor({ state: "detached" });
    assert.equal(s.api.reviews[0].specsPublic, false);
    assert.ok(s.api.reviews[0].specs, "the developer still gets them");

    // On Windows, feedback says nothing of this Linux computer: no specs, and no box.
    await page.getByRole("button", { name: /Update your notes/ }).click();
    const again = page.locator(".review-editor");
    await again.locator("select").first().selectOption("windows");
    assert.equal(await again.getByRole("checkbox").count(), 0);
    await again.getByRole("button", { name: "Update notes" }).click();
    await again.waitFor({ state: "detached" });
    assert.equal(s.api.reviews.at(-1).specs, undefined);
    assert.deepEqual(s.errors, []);
  } finally {
    await s.close();
  }
});
