/**
 * Releases packed as .tar.xz, .tar.bz2 and .tar.zst install and play, as
 * zips do: from repositories whose only file for this computer is one.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, api, apps, until;

before(async () => {
  s = await launch();
  ({ app, api, apps, until } = s);
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
});

after(() => s?.close());

const page = () => app.$(".page-layer:not([hidden]) .app-page");

for (const packing of ["xz", "bz2", "zst"])
  test(`a release packed as .tar.${packing} installs, keeps its program runnable, and plays`, async () => {
    // Added in the library: Browse's button is the catalog's submission form.
    await (await app.$("button*=Library")).click();
    await (await app.$("button*=Add an app")).click();
    await (await (await app.$("[role=dialog][aria-label='Add an app']")).$("button*=From GitHub")).click();
    await (await app.$("input[aria-label=Repository]")).setValue(`someone/packed-${packing}`);
    await (await app.$("button=Look it up")).click();
    await (await app.$(`p*=Downloads Packed-linux-x64.tar.${packing}`)).waitForDisplayed({ timeout: 10000 });
    await (await app.$("button=Add and get")).click();
    const folder = join(apps, `packed-${packing}`);
    await until(() => existsSync(join(folder, ".quiver-version")), 30000);
    assert.equal(readFileSync(join(folder, ".quiver-version"), "utf8"), "v1.0");
    assert.equal(readFileSync(join(folder, "data/level1.dat"), "utf8"), "level", "the wrapper folder is flattened");
    assert.deepEqual(api.downloads().slice(-1), [`packed-linux-x64.tar.${packing}`]);
    await (await (await page()).$("button*=Play")).click();
    await until(() => existsSync(join(folder, "launched.txt")));
    await app.keys("Escape");
  });
