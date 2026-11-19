/**
 * Which download?: files the site put on no platform (a PS5 or Switch build) aren't files for this computer, so
 * several of them ask as files none of which is marked for it; and Show all files offers the release's others, the
 * ones a maintainer didn't tick too, which ask before they install.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

const ID = "entry_test-port";
const files = (...list) =>
  list.map(([filename, os]) => ({ id: filename, url: `https://example.com/${filename}`, filename, os, architecture: "unknown", format: "zip", checksum: `sha256:${"a".repeat(64)}` }));
// As BattleShip-Remix v2.0.1-port.12 reached the launcher: the Windows build missing, the PS5 and Switch ones unplaced.
const RELEASE = {
  id: "release_1.0.0",
  version: "1.0.0",
  releasedAt: Date.UTC(2026, 0, 1),
  prerelease: false,
  assets: files(["Port-ps5.zip", "unknown"], ["Port-switch.zip", "unknown"], ["Port-macos.zip", "macos"], ["Port-android.apk", "android"]),
  // The Windows build, which no maintainer ticked: the site sends it apart, with the checksum it saw unverified.
  otherAssets: files(["Port-windows-x64.zip", "windows"]).map(({ checksum, ...a }) => ({ ...a, unverifiedChecksum: checksum })),
};
// Only the Windows build that wasn't ticked is one Windows can install: Show all files is open from the start.
const ONLY_UNTICKED = { ...RELEASE, assets: files(["Port-android.apk", "android"]) };
// As Darkwatch: Darker Edition v0.8.1 reached the launcher: the Linux build ticked, the Windows one not, its virus
// scan not finished.
const SCAN_PENDING = {
  ...RELEASE,
  assets: files(["Port-linux-x64.tar.gz", "linux"]),
  otherAssets: files(["Port-windows-x64.zip", "windows"]).map(({ checksum, ...a }) => ({
    ...a,
    unverifiedChecksum: checksum,
    scan: { verdict: "pending", url: "https://www.virustotal.com/gui/file/aaaa" },
  })),
};

const open = (path, release = RELEASE, signIn = false) =>
  openApp({
    state: { library: [{ id: ID, slug: "test-port", addedAt: 1 }], installs: {} },
    config: { os: "windows", arch: "x64" },
    prepare: (api) => api.setReleases("test-port", [release]),
  }).then(async (s) => {
    if (signIn) {
      await s.page.locator(".account button").click();
      const dialog = s.page.locator('[role="dialog"]');
      await dialog.locator('form > button[type="button"]').last().click();
      await dialog.locator('input[autocomplete="username"]').fill("files");
      await dialog.locator('input[autocomplete="new-password"]').fill("a password for the test");
      await dialog.locator("button.primary").click();
      await s.page.locator(".account-button").waitFor();
    }
    await s.page.locator("article[data-slug]").first().click({ button: "right" });
    await s.page.locator('[role="menu"] [role="menuitem"]').filter({ hasText: "Install" }).first().click();
    await s.page.locator(".choose").waitFor();
    if (path) await s.page.locator(".choose").screenshot({ path });
    return s;
  });
const installedFile = async (s) => {
  await s.page.waitForFunction(() => window.__calls.some((c) => c.cmd === "install"));
  return s.page.evaluate(() => window.__calls.find((c) => c.cmd === "install").args.request.filename);
};

after(stop);

test("files on no platform ask as none marked for this computer, and Show all files offers the others", async () => {
  const s = await open(process.env.SHOT_DIR && `${process.env.SHOT_DIR}/which-download.png`);
  try {
    const dialog = s.page.locator(".choose");
    assert.match(await dialog.innerText(), /None of .* files are marked for your computer/);
    const choices = dialog.locator(".choices button");
    assert.equal(await choices.count(), 2);
    await dialog.getByRole("button", { name: "Show all files" }).click();
    // The Android app isn't one Windows can install; the Windows build no maintainer ticked is there, said so.
    assert.equal(await choices.count(), 4);
    assert.match(await choices.filter({ hasText: "Port-windows-x64.zip" }).innerText(), /Not approved/);
    assert.equal(await dialog.getByText("Port-android.apk").count(), 0);
    if (process.env.SHOT_DIR) await dialog.screenshot({ path: `${process.env.SHOT_DIR}/which-download-all.png` });
    await choices.filter({ hasText: "Port-macos.zip" }).click();
    assert.equal(await installedFile(s), "Port-macos.zip");
  } finally {
    await s.close();
  }
});

test("a file no maintainer ticked asks before it installs, and is offered at once when it's all there is", async () => {
  const s = await open(process.env.SHOT_DIR && `${process.env.SHOT_DIR}/which-download-unticked.png`, ONLY_UNTICKED);
  try {
    const dialog = s.page.locator(".choose");
    assert.equal(await dialog.getByRole("button", { name: "Show all files" }).count(), 0);
    await dialog.locator(".choices button").filter({ hasText: "Port-windows-x64.zip" }).click();
    const prompt = s.page.locator(".unverified-prompt");
    await prompt.waitFor();
    assert.match(await prompt.innerText(), /Install a file Quiver doesn't offer\?/);
    if (process.env.SHOT_DIR) await prompt.screenshot({ path: `${process.env.SHOT_DIR}/which-download-ask.png` });
    await prompt.getByRole("button", { name: "Install anyway" }).click();
    assert.equal(await installedFile(s), "Port-windows-x64.zip");
  } finally {
    await s.close();
  }
});

test("a file Quiver doesn't offer whose virus scan isn't finished says so, and so does the question before it installs", async () => {
  const s = await open(process.env.SHOT_DIR && `${process.env.SHOT_DIR}/which-download-scan-pending.png`, SCAN_PENDING);
  try {
    const dialog = s.page.locator(".choose");
    await dialog.getByRole("button", { name: "Show all files" }).click();
    const file = dialog.locator(".choices button").filter({ hasText: "Port-windows-x64.zip" });
    assert.match(await file.innerText(), /Windows · Virus scan not finished/);
    assert.doesNotMatch(await file.innerText(), /Not approved/);
    if (process.env.SHOT_DIR) await dialog.screenshot({ path: `${process.env.SHOT_DIR}/which-download-scan-pending-all.png` });
    await file.click();
    const prompt = s.page.locator(".unverified-prompt");
    await prompt.waitFor();
    assert.match(await prompt.innerText(), /Quiver doesn't offer Port-windows-x64\.zip for .*, and its virus scan hasn't finished\./);
    assert.match(await prompt.innerText(), /VirusTotal: still scanning its files\./);
    if (process.env.SHOT_DIR) await prompt.screenshot({ path: `${process.env.SHOT_DIR}/which-download-scan-pending-ask.png` });
    await prompt.getByRole("button", { name: "Install anyway" }).click();
    assert.equal(await installedFile(s), "Port-windows-x64.zip");
  } finally {
    await s.close();
  }
});

test("the maintainers can be told, in a click, which file is the build for which system", async () => {
  const s = await open(undefined, RELEASE, true);
  try {
    const dialog = s.page.locator(".choose");
    await dialog.getByRole("button", { name: "Show all files" }).click();
    await dialog.getByRole("button", { name: "Tell the maintainers which file is for which system" }).click();
    // The file no maintainer ticked, for this computer's system, to start with.
    const form = dialog.locator(".suggest-platform");
    assert.equal(await form.locator("select").first().inputValue(), "Port-windows-x64.zip");
    assert.equal(await form.locator("select").last().inputValue(), "windows");
    if (process.env.SHOT_DIR) await dialog.screenshot({ path: `${process.env.SHOT_DIR}/which-download-suggest.png` });
    await form.getByRole("button", { name: "Send" }).click();
    await dialog.getByText("Sent. A maintainer will take a look.").waitFor();
    const [sent] = s.api.corrections;
    assert.equal(sent.topic, "downloads");
    assert.equal(sent.entryId, ID);
    assert.match(sent.body, /^Port-windows-x64\.zip in v1\.0\.0 is the Windows build, but it isn't offered\. Please tick it and set its platform to Windows\./);
    // The picker is still open, to pick from.
    assert.equal(await dialog.count(), 1);
  } finally {
    await s.close();
  }
});
