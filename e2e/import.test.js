/** A Quiver Launcher 3 player imports their library. */
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
        // Its repository moved since: the catalog entry 3.x knew it as still finds it.
        { name: "Test Port", repository: "quiver/test-port-old-name", catalogEntryId: "entry_test-port", folderName: "TestPort" },
        // Its repository is gone (GitHub says 404): it comes over all the same, as a custom app.
        { name: "Homebrew Thing", repository: "someone/not-in-catalog", folderName: "Homebrew" },
        // Nothing says where it's from: the one left out.
        { name: "Mystery App", folderName: "Mystery" },
      ],
    }),
  );
  s = await launch({ env: { QUIVER_V3_DATA: old } });
});

after(() => s?.close());

test("a 3.x library is added in one click, keeping installed apps, all but an app 3.x doesn't say where it's from", async () => {
  const { app, card, until } = s;
  await s.usageNoticeSeen();
  await (await app.$("button=Add 3 apps")).click();
  await (await app.$("p*=except these, as Quiver Launcher 3 doesn't know where they come from: Mystery App.")).waitForDisplayed({ timeout: 20000 });
  await (await app.$("article*=Homebrew Thing")).waitForExist();
  const port = await card("test-port");
  await (await port.$("button*=Play")).click();
  await until(() => existsSync(join(installed, "launched.txt")));
  assert.ok(!existsSync(join(s.apps, "TestPort")), "nothing was downloaded again");
});
