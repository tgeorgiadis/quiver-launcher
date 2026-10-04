/**
 * Anonymous usage data: on by default as on the website, said so once, off in
 * a click (here or on the account), and never a file, folder or user name.
 * Events go to the mock API's stand-in for PostHog (QUIVER_POSTHOG_HOST).
 */
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { homedir, userInfo } from "node:os";
import { join } from "node:path";
import { launch } from "./session.js";
import { startMockApi } from "./mock-api.js";

let api, s;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const events = (name) => api.telemetry().filter((e) => e.event === name);
const notice = () => s.app.$('[aria-label="Usage data"]');
const usageSwitch = async () => (await s.app.$("label*=Send anonymous usage data")).$("input");
const openSettings = async () => {
  await (await s.app.$("button[aria-label=Settings]")).click();
  await (await usageSwitch()).waitForExist({ timeout: 10000 });
};
const signIn = async (create) => {
  await (await s.app.$("button=Sign in")).click();
  if (create) await (await s.app.$("button=Create an account")).click();
  await (await s.app.$("input[placeholder=Username]")).setValue("player");
  await (await s.app.$("input[placeholder=Password]")).setValue("correct horse battery");
  await (await s.app.$("form button.primary")).click();
  await (await s.app.$("button=Sign out")).waitForDisplayed({ timeout: 10000 });
};
/** Moves around the launcher: what would send screens if usage data were on. */
const wander = async () => {
  await (await s.app.$("button=Browse")).click();
  await (await (await s.card("test-port")).$(".card-open")).click();
  await (await s.app.$(".app-page")).waitForDisplayed();
  await s.app.keys("Escape");
  await (await s.app.$("button*=Library")).click();
  await (await s.app.$("button=Browse")).click();
};
/** Nothing more arrives, even after PostHog's queue would have sent it. */
const assertQuiet = async () => {
  // Anything captured before turning it off has gone by now.
  await sleep(5000);
  const before = api.telemetry().length;
  await wander();
  await sleep(6000);
  assert.deepEqual(api.telemetry().slice(before).map((e) => e.event), []);
};

before(async () => {
  api = await startMockApi();
});

after(async () => {
  await s?.close();
  api?.close();
});

test("usage data is on at first, says so once, and every event says it's the launcher", async () => {
  s = await launch({ api });
  const bar = await notice();
  await bar.waitForDisplayed({ timeout: 20000 });
  assert.match(await bar.getText(), /Quiver Launcher sends anonymous usage data to help improve it\. You can turn this off in Settings\./);
  await s.until(() => events("launcher_started").length > 0 && events("screen_viewed").length > 0, 30000);
  const [started] = events("launcher_started");
  assert.equal(started.properties.first_run, true);
  for (const event of [started, ...events("screen_viewed")]) {
    assert.equal(event.properties.app, "launcher");
    assert.match(event.properties.launcher_version, /^\d+\.\d+\.\d+/);
    assert.equal(event.properties.os, process.platform === "win32" ? "windows" : process.platform === "darwin" ? "macos" : "linux");
    assert.ok(event.properties.arch);
  }
  // A new player starts in the catalog.
  assert.deepEqual(events("screen_viewed")[0].properties.screen, "browse");
  // Only the launcher's own events: nothing captured by itself.
  assert.ok(!api.telemetry().some((e) => ["$autocapture", "$pageview", "$pageleave", "$rageclick"].includes(e.event)));

  await (await bar.$("button=OK")).click();
  await bar.waitForExist({ reverse: true });
  const data = s.data;
  await s.close();
  s = await launch({ api, data });
  await (await s.card("test-port")).waitForDisplayed({ timeout: 20000 });
  await s.until(() => events("launcher_started").some((e) => e.properties.first_run === false), 30000);
  assert.equal(await (await notice()).isExisting(), false, "the notice shows once");
});

test("installing and playing an app sends its slug, never a folder, a path or the user name", async () => {
  await (await (await s.card("test-port")).$("button=Get")).click();
  await (await (await s.card("test-port")).$("button*=Play")).waitForDisplayed({ timeout: 30000 });
  await (await (await s.card("test-port")).$("button*=Play")).click();
  await s.until(() => events("app_installed").length > 0 && (events("app_launched").length > 0 || events("app_launch_failed").length > 0), 30000);
  assert.deepEqual(
    { ...events("app_install_started")[0].properties, ...events("app_installed")[0].properties }.slug,
    "test-port",
  );
  assert.equal(events("app_installed")[0].properties.version, "1.0.0");
  assert.equal(events("app_installed")[0].properties.source, "catalog");
  // Settings shows the apps folder; showing it sends nothing about it.
  await (await s.app.$("button[aria-label=Settings]")).click();
  await (await s.app.$("button=Done")).click();
  await sleep(5000);

  const sent = api.telemetryBodies().join("\n");
  const json = (text) => JSON.stringify(text).slice(1, -1);
  for (const folder of [s.data, s.apps, homedir()]) {
    assert.ok(!sent.toLowerCase().includes(folder.toLowerCase()), `${folder} was sent`);
    assert.ok(!sent.toLowerCase().includes(json(folder).toLowerCase()), `${folder} was sent`);
  }
  assert.doesNotMatch(sent, /(?<![A-Za-z0-9])[A-Za-z]:(\\\\|\\|\/)/, "a Windows path was sent");
  assert.doesNotMatch(sent, /(?<![\w.:/-])\/(home|Users|tmp|root|var)\//, "a Linux or macOS path was sent");
  const user = userInfo().username;
  if (user.length >= 3)
    assert.doesNotMatch(sent, new RegExp(`(?<![\\p{L}\\p{N}_-])${user.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")}(?![\\p{L}\\p{N}_-])`, "iu"), "the user name was sent");
});

test("turning usage data off in Settings stops it, and it stays off", async () => {
  await openSettings();
  const toggle = await usageSwitch();
  assert.equal(await toggle.isSelected(), true);
  assert.match(
    await (await s.app.$('[role="dialog"][aria-label="Settings"]')).getText(),
    /Helps improve Quiver Launcher: which screens and features get used, and errors\. Never your files or folders\./,
  );
  await toggle.click();
  assert.equal(await toggle.isSelected(), false);
  await (await s.app.$("button=Done")).click();
  await assertQuiet();
  await s.until(() => JSON.parse(readFileSync(join(s.data, "settings.json"), "utf8")).telemetryOff === true);
});

test("the first-run notice's Turn off turns it off", async () => {
  await s.close();
  s = await launch({ api });
  const bar = await notice();
  await bar.waitForDisplayed({ timeout: 20000 });
  await (await bar.$("button=Turn off")).click();
  await bar.waitForExist({ reverse: true });
  await openSettings();
  assert.equal(await (await usageSwitch()).isSelected(), false);
  await (await s.app.$("button=Done")).click();
  await assertQuiet();
});

test("signed in, the account's setting is followed, and the switch saves to it", async () => {
  await s.close();
  s = await launch({ api });
  await (await notice()).waitForDisplayed({ timeout: 20000 });
  await signIn(true);
  // Events join the account's person, as on the website.
  await s.until(() => events("$identify").some((e) => e.properties.distinct_id === "user_player"), 30000);
  const identify = events("$identify").find((e) => e.properties.distinct_id === "user_player");
  assert.deepEqual(identify.$set ?? identify.properties.$set, { name: "player", provider: "password" });
  await s.until(() => events("signed_in").some((e) => e.properties.method === "password" && e.properties.created === true));

  await openSettings();
  assert.match(await (await s.app.$('[role="dialog"][aria-label="Settings"]')).getText(), /follows your Quiver account's setting/);
  await (await usageSwitch()).click();
  await s.until(() => api.analyticsCalls.some((c) => c.user === "player" && c.enabled === false));
  await (await s.app.$("button=Done")).click();
  await assertQuiet();

  // Signed out, this computer remembers it was turned off; signed back in, the account's setting (turned on elsewhere) wins.
  await (await s.app.$("button=Sign out")).click();
  await (await s.app.$("button=Sign in")).waitForDisplayed({ timeout: 10000 });
  await openSettings();
  assert.equal(await (await usageSwitch()).isSelected(), false);
  await (await s.app.$("button=Done")).click();
  api.setAnalytics("player", true);
  await signIn(false);
  await s.until(async () => {
    await openSettings();
    const on = await (await usageSwitch()).isSelected();
    await (await s.app.$("button=Done")).click();
    return on;
  });
  const before = api.telemetry().length;
  await wander();
  await s.until(() => api.telemetry().slice(before).some((e) => e.event === "screen_viewed"), 20000);
});
