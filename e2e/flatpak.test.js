/**
 * Releases that are a Flatpak bundle, or one in a zip: installed for the
 * player with flatpak, played with `flatpak run`, and uninstalled with the
 * app. A stand-in `flatpak` on PATH records what it was asked to do.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const bin = mkdtempSync(join(tmpdir(), "quiver-bin-"));
const log = join(mkdtempSync(join(tmpdir(), "quiver-flatpak-")), "log");
let s;

before(async () => {
  // Installs read the bundle's ref, as flatpak does; everything is logged.
  writeFileSync(
    join(bin, "flatpak"),
    `#!/bin/sh
echo "$*" >> "${log}"
case "$1" in install) for last; do :; done; grep -aq "app/" "$last" || exit 1 ;; esac
exit 0
`,
    { mode: 0o755 },
  );
  s = await launch({ env: { PATH: `${bin}:${process.env.PATH}` } });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => s.app.$(".page-layer:not([hidden]) .app-page");
const ran = () => (existsSync(log) ? readFileSync(log, "utf8") : "");

const addFromGitHub = async (repository, file) => {
  await (await s.app.$("button*=Library")).click();
  await (await s.app.$("button*=Add an app")).click();
  await (await (await s.app.$("[role=dialog][aria-label='Add an app']")).$("button*=From GitHub")).click();
  await (await s.app.$("input[aria-label=Repository]")).setValue(repository);
  await (await s.app.$("button=Look it up")).click();
  await (await s.app.$(`p*=Downloads ${file}`)).waitForDisplayed({ timeout: 10000 });
  await (await s.app.$("button=Add and get")).click();
};

for (const [repository, file, id] of [
  ["someone/flat-game", "FlatGame-x86_64.flatpak", "com.example.FlatGame"],
  ["someone/zipped-flat", "ZippedFlat-linux.zip", "com.example.ZippedFlat"],
])
  test(`${file} installs with flatpak, plays with flatpak run, and uninstalls`, async () => {
    await addFromGitHub(repository, file);
    const folder = join(s.apps, repository.split("/")[1]);
    await s.until(() => existsSync(join(folder, ".quiver-flatpak")), 30000);
    assert.equal(readFileSync(join(folder, ".quiver-flatpak"), "utf8"), id);
    assert.match(ran(), new RegExp(`^install --user --noninteractive -y --bundle .*\\.flatpak$`, "m"));
    await (await (await page()).$("button*=Play")).click();
    await s.until(() => ran().includes(`run ${id}`));
    await (await (await page()).$("button=Uninstall and remove")).click();
    await (await (await s.app.$("[role=dialog]")).$("button=Uninstall and remove")).click();
    await s.until(() => ran().includes(`uninstall --user --noninteractive -y ${id}`));
    assert.ok(!existsSync(folder));
  });

test("without Flatpak on the computer, the install says so", async () => {
  await s.close();
  s = await launch({ env: { PATH: "/usr/bin:/bin" } });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  await addFromGitHub("someone/flat-game", "FlatGame-x86_64.flatpak");
  await (await s.app.$("p*=Flatpak isn't installed on this computer")).waitForDisplayed({ timeout: 30000 });
});
