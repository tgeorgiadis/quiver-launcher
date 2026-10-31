/**
 * Library backups: a damaged or missing library opens on the backups Quiver
 * kept rather than starting over it, and Settings can restore one.
 */
import { after, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const library = join(data, "library.json");
let s;

after(() => s?.close());

const problem = () => s.app.$('[role="alert"][aria-label="Library problem"]');
const restart = async () => {
  await s.close();
  s = await launch({ data });
};
/** Whether Test Port is in the Library tab. */
const inLibrary = async () => {
  await (await s.app.$("button*=Library")).click();
  await (await s.app.$("main")).waitForDisplayed();
  return (await s.card("test-port")).isExisting();
};
// Looked up afresh until it shows: the library redraws as it comes back.
const playable = () => s.until(async () => (await (await s.card("test-port")).$("button*=Play")).isDisplayed().catch(() => false));

test("a damaged library opens on its snapshots, and one puts it back", async () => {
  s = await launch({ data });
  const port = await s.card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  await playable();
  // A snapshot is taken as soon as an app is installed.
  const backups = join(data, "backups");
  await s.until(() => existsSync(backups) && readdirSync(backups).some((b) => existsSync(join(backups, b, "installs.json")) && readFileSync(join(backups, b, "installs.json"), "utf8").includes("test-port")));

  await s.close();
  writeFileSync(library, '[{"id": "entry_test-port", "slu');
  s = await launch({ data });
  await (await problem()).waitForDisplayed({ timeout: 20000 });
  assert.match(await (await problem()).getText(), /The file with your library is damaged/);
  const newest = await (await problem()).$("ul[aria-label=Backups] li");
  assert.match(await newest.getText(), /1 app/);
  await (await newest.$("button=Restore")).click();
  await playable();
  assert.equal(JSON.parse(readFileSync(library, "utf8")).length, 1);
  assert.ok(readdirSync(data).some((f) => f.startsWith("library.damaged-")), "the damaged file is kept aside");
});

test("a missing library can be left behind, and then the launcher starts as usual", async () => {
  await s.close();
  rmSync(library);
  s = await launch({ data });
  await (await problem()).waitForDisplayed({ timeout: 20000 });
  assert.match(await (await problem()).getText(), /The file with your library is missing/);
  await (await (await problem()).$("button=Continue without restoring")).click();
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  assert.deepEqual(JSON.parse(readFileSync(library, "utf8")), []);
  await restart();
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  assert.ok(!(await (await problem()).isExisting()));
  // Installed, so still in the library.
  assert.ok(await inLibrary());
});

test("Settings restores a backup", async () => {
  assert.deepEqual(JSON.parse(readFileSync(library, "utf8")), []);
  const settings = await s.settings("Library");
  await (await settings.$("button=Choose a backup…")).click();
  const withPort = await settings.$("li*=1 app");
  await withPort.waitForDisplayed();
  await (await withPort.$("button=Restore")).click();
  const confirm = await s.app.$('[role="group"][aria-label="Restore backup"]');
  assert.match(await confirm.getText(), /Replace your library with the one from .* \(1 app\)/);
  await (await confirm.$("button=Restore")).click();
  await s.until(() => JSON.parse(readFileSync(library, "utf8")).length === 1);
  await playable();
  // Restoring kept the library as it was too.
  assert.ok(readdirSync(join(data, "backups")).length >= 3);
});
