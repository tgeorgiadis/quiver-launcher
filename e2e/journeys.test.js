/**
 * User journeys through the real app (Tauri + WebDriver). Build first with
 * `pnpm --filter @quiver/e2e build`; Linux needs tauri-driver,
 * WebKitWebDriver and a display (xvfb-run).
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { existsSync, mkdtempSync, readFileSync } from "node:fs";
import { homedir, tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { remote } from "webdriverio";
import { startMockApi } from "./mock-api.js";

const binary = resolve(import.meta.dirname, "../apps/launcher/src-tauri/target/debug/quiver-launcher");
const data = mkdtempSync(join(tmpdir(), "quiver-data-"));
const apps = join(data, "apps");
let api, driver, app;

before(async () => {
  api = await startMockApi();
  driver = spawn(join(homedir(), ".cargo/bin/tauri-driver"), [], {
    env: { ...process.env, QUIVER_API: api.api, QUIVER_DATA: data },
    stdio: "inherit",
  });
  await new Promise((r) => setTimeout(r, 1500));
  app = await remote({
    hostname: "127.0.0.1",
    port: 4444,
    logLevel: "error",
    capabilities: { "wdio:enforceWebDriverClassic": true, "tauri:options": { application: binary } },
  });
});

after(async () => {
  await app?.deleteSession();
  driver?.kill();
  api?.close();
});

const card = (name) => app.$(`article[data-slug="${name}"]`);
const until = (check, timeout = 20000) => app.waitUntil(check, { timeout, interval: 200 });

test("a new player gets a port in one click and plays it", async () => {
  // An empty library starts in the catalog, showing ports for this computer.
  const port = await card("test-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();

  // Get adds it to the library and installs the verified release.
  await (await port.$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  const folder = join(apps, "test-port");
  assert.equal(readFileSync(join(folder, ".quiver-version"), "utf8"), "1.0.0");
  assert.ok(existsSync(join(folder, "data/level1.dat")), "the wrapper folder is flattened");
  assert.ok(existsSync(join(folder, "portable.txt")), "the catalog's filesToAdd are created");
  assert.match(await (await port.$(".cover-badge")).getText(), /in library/i);

  // It's in the library, and Play starts it.
  await (await app.$("button*=Library")).click();
  const owned = await card("test-port");
  await (await owned.$("button*=Play")).click();
  await until(() => existsSync(join(folder, "launched.txt")));
});

test("a download that doesn't match Quiver's checksum is refused", async () => {
  await (await app.$("button=Browse")).click();
  const port = await card("tampered-port");
  await port.waitForDisplayed({ timeout: 20000 });
  await (await port.$("button=Get")).click();
  const alert = await port.$('[role="alert"]');
  await alert.waitForDisplayed({ timeout: 30000 });
  assert.match(await alert.getText(), /doesn't match the file Quiver checked/);
  assert.ok(!existsSync(join(apps, "tampered-port")), "nothing was installed");
});

test("search narrows the catalog", async () => {
  await (await app.$("input[placeholder^=Search]")).setValue("tampered");
  await until(async () => !(await (await card("test-port")).isExisting()));
  assert.ok(await (await card("tampered-port")).isExisting());
});

test("removing an app uninstalls it and takes it out of the library", async () => {
  await (await app.$("button*=Library")).click();
  await (await (await card("test-port")).$(".card-open")).click();
  await (await app.$("button*=Uninstall and remove")).click();
  await until(() => !existsSync(join(apps, "test-port")));
  await until(async () => !(await (await card("test-port")).isExisting()));
});
