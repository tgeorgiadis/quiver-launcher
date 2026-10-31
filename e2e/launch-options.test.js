/**
 * Launch options on the app page: another of the app's programs, Steam-style
 * options (environment, a wrapper, arguments), Wine or a Proton, used by Play,
 * desktop shortcuts and Steam shortcuts, and kept through an update.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const home = mkdtempSync(join(tmpdir(), "quiver-home-"));
const bin = mkdtempSync(join(tmpdir(), "quiver-bin-"));
const log = join(mkdtempSync(join(tmpdir(), "quiver-ran-")), "log");
const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const steamConfig = join(home, ".steam/steam/userdata/42/config");
const proton = join(home, ".steam/steam/compatibilitytools.d/GE-Proton-Test");
const otherProton = join(mkdtempSync(join(tmpdir(), "quiver-proton-")), "Proton-Elsewhere");
const port = join(data, "apps", "test-port");
let s;

const script = (path, body) => writeFileSync(path, `#!/bin/sh\n${body}\n`, { mode: 0o755 });

before(async () => {
  mkdirSync(join(home, "Desktop"));
  mkdirSync(steamConfig, { recursive: true });
  for (const dir of [proton, otherProton]) mkdirSync(dir, { recursive: true });
  // Stand-ins that say how they were run.
  script(join(bin, "wine"), `echo "wine $*" >> "${log}"`);
  script(join(bin, "wrap"), `echo "wrap $*" >> "${log}"; exec "$@"`);
  script(join(proton, "proton"), `echo "ge-proton $* prefix=$STEAM_COMPAT_DATA_PATH" >> "${log}"`);
  script(join(otherProton, "proton"), `echo "other-proton $*" >> "${log}"`);
  s = await launch({
    data,
    env: { HOME: home, PATH: `${bin}:${process.env.PATH}`, QUIVER_PICK: join(port, "tools", "other.sh"), QUIVER_PICK_FOLDER: otherProton },
  });
});

after(() => s?.close());

const page = () => s.app.$(".app-page");
/** Clicks a button in the side panel, scrolled clear of the usage data notice at the bottom. */
const press = async (button) => {
  await button.scrollIntoView({ block: "center" });
  await button.click();
};
const form = () => s.app.$('form[aria-label="Launch options"]');
const ran = () => (existsSync(log) ? readFileSync(log, "utf8") : "");
const play = async () => {
  rmSync(log, { force: true });
  await press(await (await page()).$("button*=Play"));
  await s.until(() => ran() !== "");
  return ran();
};
const openForm = async () => {
  await press(await (await page()).$("button*=On this computer"));
  await press(await (await page()).$("button*=Launch options"));
  await (await form()).waitForDisplayed();
};
const save = async () => press(await (await form()).$("button=Save"));

test("another of the app's programs, with environment, a wrapper and arguments", async () => {
  const { card } = s;
  const game = await card("test-port");
  await game.waitForDisplayed({ timeout: 20000 });
  await (await game.$("button=Get")).click();
  await (await game.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  mkdirSync(join(port, "tools"));
  script(join(port, "tools", "other.sh"), `echo "other $* mode=$GAME_MODE" >> "${log}"`);
  await (await game.$(".card-open")).click();

  await openForm();
  await press(await (await form()).$("button=Choose program…"));
  await (await (await form()).$("code=tools/other.sh")).waitForDisplayed();
  await (await (await form()).$("input[aria-label='Launch flags']")).setValue('GAME_MODE=fast wrap %command% --level "two words"');
  await save();
  assert.equal(await play(), `wrap ${join(port, "tools", "other.sh")} --level two words\nother --level two words mode=fast\n`);
  assert.ok(await (await (await page()).$("button=Launch options (changed)")).isExisting());
  const installs = JSON.parse(readFileSync(join(data, "installs.json"), "utf8"));
  assert.deepEqual(installs["entry_test-port"].launch, { executable: "tools/other.sh", command: 'GAME_MODE=fast wrap %command% --level "two words"' });

  // Shortcuts start it the same way.
  await press(await (await page()).$("button*=On this computer"));
  await press(await (await page()).$("button*=Desktop shortcut"));
  await (await s.app.$("p*=is on your desktop")).waitForDisplayed({ timeout: 10000 });
  const desktop = readFileSync(join(home, "Desktop", "Test Port.desktop"), "utf8");
  assert.match(desktop, /\nExec=env GAME_MODE=fast "wrap" ".*\/test-port\/tools\/other\.sh" "--level" "two words"\n/);
  await press(await (await page()).$("button=Add to Steam"));
  await (await s.app.$("p*=is in Steam")).waitForDisplayed({ timeout: 10000 });
  const vdf = readFileSync(join(steamConfig, "shortcuts.vdf"), "latin1");
  assert.ok(vdf.includes("tools/other.sh"), "Steam starts the chosen program");
  assert.ok(vdf.includes('GAME_MODE=fast wrap %command% --level "two words"'), vdf);
});

test("Wine, a Proton Quiver found, and one chosen from a folder", async () => {
  const runWith = async (label) => {
    await openForm();
    const select = await (await form()).$("select[aria-label='Run with']");
    await select.selectByVisibleText(label);
  };
  await runWith("Wine");
  await save();
  assert.match(await play(), /^wine .*\/tools\/other\.sh --level two words\n$/m);

  await runWith("GE-Proton-Test");
  await save();
  const prefix = join(data, "proton", "test-port");
  assert.match(await play(), new RegExp(`^ge-proton run .*/tools/other\\.sh --level two words prefix=${prefix.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}\\n$`, "m"));
  assert.ok(existsSync(prefix), "each app gets its own Proton prefix");

  await runWith("Another Proton…");
  await (await (await form()).$("option=Proton-Elsewhere")).waitForExist();
  await save();
  assert.match(await play(), /^other-proton run .*\/tools\/other\.sh/m);
});

test("launch options stay through an update, and Reset goes back to Quiver's choice", async () => {
  const installs = () => JSON.parse(readFileSync(join(data, "installs.json"), "utf8"))["entry_test-port"];
  s.api.release("1.1.0");
  await s.app.keys("Escape");
  // Browse fetches the catalog again as it opens; the update is on the app's library card.
  await (await s.app.$("button*=Library")).click();
  await (await s.app.$("button=Browse")).click();
  await (await s.app.$("button*=Library")).click();
  // Update, under Play (the corner icon for it is an icon card's, hidden on this one).
  await (await (await s.card("test-port")).$("button.tile-update")).click();
  await s.until(() => readFileSync(join(port, ".quiver-version"), "utf8") === "1.1.0", 30000);
  s.api.release("1.0.0");
  await s.until(() => installs().version.endsWith("1.1.0"));
  assert.equal(installs().launch.executable, "tools/other.sh");
  await (await (await s.card("test-port")).$(".card-open")).click();
  assert.match(await play(), /^other-proton run .*\/tools\/other\.sh/m);

  // A chosen program that's gone: Play says so.
  rmSync(join(port, "tools"), { recursive: true });
  await press(await (await page()).$("button*=Play"));
  await (await s.app.$("p*=isn't in the app's folder any more")).waitForDisplayed({ timeout: 10000 });

  await openForm();
  await press(await (await form()).$("button=Reset"));
  await (await (await page()).$("button=Launch options")).waitForDisplayed();
  await s.until(() => installs().launch === undefined);
  await press(await (await page()).$("button=Dismiss"));
  await press(await (await page()).$("button*=Play"));
  await s.until(() => existsSync(join(port, "launched.txt")));
});
