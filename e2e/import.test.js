/** A Quiver Launcher 3 player brings their library over. */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";

const old = mkdtempSync(join(tmpdir(), "quiver-v3-"));
const installed = join(old, "Apps", "TestPort");
let s;

before(async () => {
  mkdirSync(installed, { recursive: true });
  writeFileSync(join(installed, "version.txt"), "v1.0.0");
  writeFileSync(join(installed, "port.sh"), '#!/bin/sh\ncd "$(dirname "$0")" && echo played > launched.txt\n', { mode: 0o755 });
  writeFileSync(
    join(old, "apps.json"),
    JSON.stringify({
      apps: [
        { name: "Test Port", repository: "quiver/test-port", folderName: "TestPort" },
        { name: "Homebrew Thing", repository: "someone/not-in-catalog", folderName: "Homebrew" },
      ],
    }),
  );
  s = await launch({ QUIVER_V3_DATA: old });
});

after(() => s?.close());

test("a 3.x library comes over in one click, keeping installed apps", async () => {
  const { app, card, until } = s;
  await (await app.$("button=Bring them over")).click();
  await (await app.$("p*=Homebrew Thing")).waitForDisplayed({ timeout: 20000 });
  const port = await card("test-port");
  await (await port.$("button*=Play")).click();
  await until(() => existsSync(join(installed, "launched.txt")));
  assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
});
