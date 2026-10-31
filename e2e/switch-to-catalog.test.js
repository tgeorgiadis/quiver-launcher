/**
 * An app the player added from its repository, before the catalog listed it,
 * switches to the catalog's app for it: its files, folder and play time stay,
 * and its updates come from the releases Quiver checks.
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

let s, app, until;
const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
// Added from github.com/quiver/test-port into a folder named after the repository, and played for an hour.
const folder = join(data, "apps", "Test-Port");

before(async () => {
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, ".quiver-version"), "v1.0.0");
  writeFileSync(join(folder, "save.dat"), "the player's save");
  const custom = { provider: "github", repository: "quiver/Test-Port", name: "My Test Port" };
  writeFileSync(join(data, "library.json"), JSON.stringify([{ id: "github:quiver/test-port", slug: "", addedAt: 1, custom, overrides: { tags: ["favourites"] } }]));
  writeFileSync(join(data, "installs.json"), JSON.stringify({ "github:quiver/test-port": { version: "v1.0.0", releasedAt: 1, releaseId: "v1.0.0", folder: "Test-Port", executables: [], playTime: 3600, updates: "auto" } }));
  writeFileSync(join(data, "catalog.json"), JSON.stringify({}));
  s = await launch({ data });
  ({ app, until } = s);
});

after(() => s?.close());

const library = () => JSON.parse(readFileSync(join(data, "library.json"), "utf8"));
const installs = () => JSON.parse(readFileSync(join(data, "installs.json"), "utf8"));

test("the app's page offers the catalog's app, and switching keeps its files and play time", async () => {
  await (await app.$("button*=Library")).click();
  const card = await app.$(".card-open");
  await card.waitForDisplayed({ timeout: 20000 });
  await card.click();
  const line = await app.$("p*=catalog lists this app now");
  await line.waitForDisplayed({ timeout: 20000 });
  await (await line.$("button=Switch to the catalog's")).click();
  // The page is the catalog's app now.
  await until(async () => (await (await app.$(".app-page h1")).getText()) === "Test Port");
  assert.ok(!(await (await app.$("p*=catalog lists this app now")).isExisting()));
  await until(() => library().some((i) => i.id === "entry_test-port"));
  const items = library();
  assert.ok(!items.some((i) => i.id === "github:quiver/test-port"));
  assert.deepEqual(items.find((i) => i.id === "entry_test-port").overrides, { tags: ["favourites"] });
  await until(() => Boolean(installs()["entry_test-port"]));
  const install = installs()["entry_test-port"];
  assert.equal(install.folder, "Test-Port");
  assert.equal(install.playTime, 3600);
  assert.equal(install.updates, "auto");
  assert.equal(installs()["github:quiver/test-port"], undefined);
  assert.equal(readFileSync(join(folder, "save.dat"), "utf8"), "the player's save");
});
