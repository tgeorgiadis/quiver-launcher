/**
 * Installation directories for consoles (Settings › Apps folder): a console's
 * folder typed or browsed for, kept only as a whole path outside every
 * installed app's folder, and a new install of an app for that console going
 * in a folder of its own in it; one for more than one console going in their
 * folder; and Move all apps taking along a console's folder in the apps
 * folder, but not one elsewhere. In the launcher's web side on its own
 * (app.js); the Rust side's checks are install.rs's and apps_folder.rs's tests.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { openApp, stop } from "./app.js";

// Test Port (N64) installed in the apps folder; Test Remake (N64 and SNES) in the library, not installed.
const state = {
  library: ["test-port", "test-remake"].map((slug, i) => ({ id: `entry_${slug}`, slug, addedAt: Date.now() - i * 60_000 })),
  installs: { "entry_test-port": { version: "1.0.0", folder: "test-port", executables: [], lastPlayed: Date.now() - 3600_000 } },
};
let s, page;

/**
 * What the stand-in doesn't answer: the folder picker (the folder a test sets); the Rust side's check of a console's
 * folder, as apps_folder.rs makes it, a whole path not in an installed app's folder; and moving the apps folder, what's
 * in it going to the same place in the new one.
 */
const standIns = (page) =>
  page.evaluate((appsDir) => {
    const invoke = window.__TAURI_INTERNALS__.invoke;
    const inApps = (paths) => paths.filter((p) => p.startsWith(`${appsDir}/`));
    window.__TAURI_INTERNALS__.invoke = async (cmd, args) => {
      if (cmd === "pick_folder") return window.__picked ?? null;
      if (cmd === "console_folder_refusal") {
        window.__calls.push({ cmd, args });
        if (!args.to.startsWith("/")) return `${args.to} isn't a whole path.`;
        const inside = [...args.folders.map((f) => `${appsDir}/${f}`), ...args.dirs].find((a) => args.to === a || args.to.startsWith(`${a}/`));
        return inside ? `That folder is in ${inside.split("/").pop()}'s folder.` : null;
      }
      // Made if it isn't there; the apps folder itself is none of a console's own.
      if (cmd === "prepare_console_folder") return args.to !== appsDir;
      if (cmd === "apps_folder_refusal") return null;
      if (cmd === "apps_folder_holds") return inApps(args.dirs);
      if (cmd === "move_apps_folder") {
        window.__calls.push({ cmd, args });
        const moved = Object.fromEntries(inApps([...args.dirs, ...args.consoles]).map((p) => [p, args.to + p.slice(appsDir.length)]));
        return { appsDir: args.to, moved, appsFolderNow: args.consoles.filter((c) => c === args.to) };
      }
      return invoke(cmd, args);
    };
  }, "/home/player/Games/Quiver");

before(async () => {
  s = await openApp({ state });
  page = s.page;
  await standIns(page);
});

after(async () => {
  await s?.close();
  await stop();
});

const section = () => page.locator(".console-folders");
const box = (name) => section().getByLabel(name, { exact: true });
const saved = () => page.evaluate(() => window.__files.settings.consoleFolders ?? {});
/** A folder typed in a console's box, and Enter pressed. */
const type = async (name, text) => {
  await box(name).fill(text);
  await box(name).press("Enter");
};
const until = (check) => s.until(check);

test("Settings: a console's folder typed, browsed for, refused, or emptied", async () => {
  await page.getByRole("button", { name: "Settings" }).click();
  await page.getByRole("button", { name: "Apps folder" }).click();
  // Folded away until opened, then each maker too.
  assert.equal(await section().locator(".console-makers").count(), 0);
  await section().getByRole("button", { name: /Installation directories for consoles/ }).click();
  await section().getByRole("button", { name: "Nintendo" }).click();
  assert.equal(await box("Nintendo 64").getAttribute("placeholder"), "Apps folder");

  // Typed: kept without the separator at its end.
  await type("Nintendo 64", "/home/player/Games/N64/");
  await until(async () => (await saved()).n64 === "/home/player/Games/N64");
  // Browsed for: the picker's folder, in the box too.
  await page.evaluate(() => (window.__picked = "/home/player/Games/SNES"));
  await section().getByRole("button", { name: "Browse for a folder for Super Nintendo Entertainment System" }).click();
  await until(async () => (await saved()).snes === "/home/player/Games/SNES");
  assert.equal(await box("Super Nintendo Entertainment System").inputValue(), "/home/player/Games/SNES");
  assert.match(await section().getByRole("button", { name: /Installation directories for consoles/ }).innerText(), /2 folders/);

  // Not a whole path, or in Test Port's folder: said so by the box, which keeps what was typed; the folder before stays.
  for (const [typed, problem] of [
    ["~/Games/SNES", /isn't a whole path/],
    ["/home/player/Games/Quiver/test-port/SNES", /test-port's folder/],
  ]) {
    await type("Super Nintendo Entertainment System", typed);
    await section().locator(".console-folder-problem", { hasText: problem }).waitFor();
    assert.equal(await box("Super Nintendo Entertainment System").getAttribute("aria-invalid"), "true");
    assert.equal(await box("Super Nintendo Entertainment System").inputValue(), typed);
    assert.equal((await saved()).snes, "/home/player/Games/SNES");
  }

  // The apps folder itself: taken as none, the box emptied without a word, as it's where the console's apps go anyway.
  await type("Super Nintendo Entertainment System", "/home/player/Games/Quiver");
  await until(async () => !(await saved()).snes);
  await until(async () => (await box("Super Nintendo Entertainment System").inputValue()) === "");
  assert.equal(await section().locator(".console-folder-problem").count(), 0);

  // Emptied: the apps folder again.
  await type("Super Nintendo Entertainment System", "/home/player/Games/SNES");
  await until(async () => (await saved()).snes === "/home/player/Games/SNES");
  await type("Super Nintendo Entertainment System", "");
  await until(async () => !(await saved()).snes);
  assert.equal(await section().locator(".console-folder-problem").count(), 0);
  assert.deepEqual(await saved(), { n64: "/home/player/Games/N64" });
});

test("a new install of an app for more than one console goes in a folder of its own in theirs; one installed before stays where it is", async () => {
  // In the apps folder, so moving it takes it along (the next test).
  await section().getByRole("button", { name: "Apps for multiple consoles" }).click();
  await type("Multiple consoles", "/home/player/Games/Quiver/Multi");
  await until(async () => (await saved())["*"] === "/home/player/Games/Quiver/Multi");
  await page.locator(".topbar nav button", { hasText: "Library" }).click();
  const card = page.locator("article", { hasText: "Test Remake" }).first();
  await card.hover();
  await card.getByRole("button", { name: /^Get/ }).first().click();
  // The mock's files aren't marked for this computer: it asks which, as for any app.
  await page.getByRole("button", { name: /Linux · x64/ }).click();
  await until(() => page.evaluate(() => window.__installs.has("entry_test-remake")));
  const request = await page.evaluate(() => window.__calls.findLast((c) => c.cmd === "install").args.request);
  // Not N64's folder, though it has one: the folder for apps for more than one console.
  assert.equal(request.parent, "/home/player/Games/Quiver/Multi");
  assert.equal(request.dir, undefined);
  // The Rust side answers with where it went: the app's own folder in that one.
  await page.evaluate(() => window.__installs.get("entry_test-remake").resolve({ dir: "/home/player/Games/Quiver/Multi/test-remake", version: "1.0.0", installer: null, restart: false }));
  await until(() => page.evaluate(() => Boolean(window.__files.installs["entry_test-remake"])));
  assert.equal(await page.evaluate(() => window.__files.installs["entry_test-remake"].dir), "/home/player/Games/Quiver/Multi/test-remake");
  // Test Port, installed before there was a folder for N64, stays in the apps folder.
  assert.equal(await page.evaluate(() => window.__files.installs["entry_test-port"].dir), undefined);
  assert.deepEqual(s.errors, []);
});

test("Move all apps takes along a console's folder in the apps folder, and its apps; one elsewhere stays where it is", async () => {
  // N64's folder in the apps folder, with Test Remake in it; SNES's on a card, with Tampered Port in it.
  const moving = await openApp({
    state: {
      library: state.library.concat({ id: "entry_tampered-port", slug: "tampered-port", addedAt: Date.now() }),
      installs: {
        ...state.installs,
        "entry_test-remake": { version: "1.0.0", folder: "test-remake", dir: "/home/player/Games/Quiver/N64/test-remake", executables: [] },
        "entry_tampered-port": { version: "1.0.0", folder: "tampered-port", dir: "/media/card/SNES/tampered-port", executables: [] },
      },
      settings: { consoleFolders: { n64: "/home/player/Games/Quiver/N64", snes: "/media/card/SNES" } },
    },
  });
  const p = moving.page;
  try {
    await standIns(p);
    await p.getByRole("button", { name: "Settings" }).click();
    await p.getByRole("button", { name: "Apps folder" }).click();
    await p.evaluate(() => (window.__picked = "/mnt/games"));
    await p.locator(".apps-folder").getByRole("button", { name: "Change…" }).click();
    // Test Port and Test Remake go; Tampered Port, on the card, is said to stay.
    await p.getByText("Move 2 installed apps to /mnt/games?", { exact: false }).waitFor();
    await p.getByText("1 app installed outside your apps folder, such as in a console's folder elsewhere, stays where it is.").waitFor();
    await p.getByRole("button", { name: "Move apps" }).click();
    await p.getByText("Moved 2 apps.", { exact: false }).waitFor();
    const call = await p.evaluate(() => window.__calls.findLast((c) => c.cmd === "move_apps_folder").args);
    assert.deepEqual(call.folders, ["test-port"]);
    // N64's folder, and Test Remake in it, are where they were in the new apps folder; SNES's and Tampered Port stay.
    const files = () => p.evaluate(() => window.__files);
    await moving.until(async () => (await files()).settings.consoleFolders.n64 === "/mnt/games/N64");
    assert.equal((await files()).settings.consoleFolders.snes, "/media/card/SNES");
    assert.equal((await files()).installs["entry_test-remake"].dir, "/mnt/games/N64/test-remake");
    assert.equal((await files()).installs["entry_tampered-port"].dir, "/media/card/SNES/tampered-port");
    assert.deepEqual(moving.errors, []);
  } finally {
    await moving.close();
  }
});
