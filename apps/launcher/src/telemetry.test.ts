import { afterEach, beforeEach, expect, test, vi } from "vitest";
import type { CaptureResult } from "posthog-js";
import type { TelemetryContext } from "./telemetry";

const posthog = vi.hoisted(() => ({
  init: vi.fn(),
  register: vi.fn(),
  identify: vi.fn(),
  reset: vi.fn(),
  capture: vi.fn(),
  captureException: vi.fn(),
}));
vi.mock("posthog-js/no-external", () => ({ default: posthog }));
vi.mock("posthog-js/dist/exception-autocapture", () => ({}));

const context: TelemetryContext = {
  host: "https://us.i.posthog.com",
  version: "4.0.0",
  os: "windows",
  arch: "x64",
  home: "C:\\Users\\Ada Lovelace",
  user: "ada",
  appsDir: "D:\\Quiver\\apps",
};

beforeEach(() => {
  vi.resetModules();
  vi.stubEnv("VITE_POSTHOG_PROJECT_TOKEN", "phc_project");
  vi.stubEnv("DEV", false);
  Object.values(posthog).forEach((f) => f.mockClear());
});
afterEach(() => vi.unstubAllEnvs());

/** A fresh copy of the module, started signed out unless told otherwise. */
async function started(options: { account?: Parameters<typeof import("./telemetry").setTelemetryAccount>[0]; host?: string } = {}) {
  const telemetry = await import("./telemetry");
  telemetry.startTelemetry({ ...context, host: options.host ?? context.host });
  telemetry.setTelemetryAccount(options.account === undefined ? null : options.account);
  return telemetry;
}
const config = () => posthog.init.mock.calls[0][1];
const beforeSend = (event: Partial<CaptureResult>) => config().before_send({ uuid: "1", event: "e", properties: {}, ...event });
const settle = () => new Promise((r) => setTimeout(r, 10));

test("without a project token nothing loads or sends", async () => {
  vi.stubEnv("VITE_POSTHOG_PROJECT_TOKEN", "");
  const telemetry = await started();
  telemetry.track("screen_viewed", { screen: "library" });
  await settle();
  expect(posthog.init).not.toHaveBeenCalled();
  expect(telemetry.telemetryChoice()).toEqual({ available: false, enabled: false, signedIn: false });
});

test("pnpm dev sends nothing unless VITE_POSTHOG_DEV=1", async () => {
  vi.stubEnv("DEV", true);
  let telemetry = await started();
  await settle();
  expect(telemetry.telemetryAvailable()).toBe(false);
  expect(posthog.init).not.toHaveBeenCalled();

  vi.resetModules();
  vi.stubEnv("VITE_POSTHOG_DEV", "1");
  telemetry = await started();
  await vi.waitFor(() => expect(posthog.init).toHaveBeenCalled());
  expect(telemetry.telemetryAvailable()).toBe(true);
});

test("a test token never goes to PostHog itself, only to a stand-in", async () => {
  vi.stubEnv("VITE_POSTHOG_PROJECT_TOKEN", "phc_test");
  await started();
  await settle();
  expect(posthog.init).not.toHaveBeenCalled();
  vi.resetModules();
  await started({ host: "http://127.0.0.1:5000/posthog" });
  await vi.waitFor(() => expect(posthog.init).toHaveBeenCalled());
  expect(config()).toMatchObject({ api_host: "http://127.0.0.1:5000/posthog", disable_compression: true });
});

test("explicit events only, no replays or remote scripts, and every event says it's the launcher", async () => {
  await started();
  await vi.waitFor(() => expect(posthog.register).toHaveBeenCalled());
  expect(posthog.init.mock.calls[0][0]).toBe("phc_project");
  expect(config()).toMatchObject({
    api_host: "https://us.i.posthog.com",
    ui_host: "https://us.posthog.com",
    defaults: "2026-05-30",
    persistence: "localStorage",
    person_profiles: "identified_only",
    autocapture: false,
    capture_pageview: false,
    capture_pageleave: false,
    disable_session_recording: true,
    disable_surveys: true,
    disable_external_dependency_loading: true,
    disable_compression: false,
    opt_out_useragent_filter: true,
    capture_exceptions: { capture_unhandled_errors: true, capture_unhandled_rejections: true },
  });
  expect(posthog.register).toHaveBeenCalledWith({ app: "launcher", launcher_version: "4.0.0", os: "windows", arch: "x64" });
});

test("nothing starts until the account's choice is known; events from before are sent once it is", async () => {
  const telemetry = await import("./telemetry");
  telemetry.startTelemetry(context);
  telemetry.track("launcher_started", { first_run: true });
  await settle();
  expect(posthog.init).not.toHaveBeenCalled();
  telemetry.setTelemetryAccount(null);
  await vi.waitFor(() => expect(posthog.capture).toHaveBeenCalledWith("launcher_started", { first_run: true }));
  telemetry.track("screen_viewed", { screen: "browse" });
  await vi.waitFor(() => expect(posthog.capture).toHaveBeenCalledWith("screen_viewed", { screen: "browse" }));
});

test("turning it off forgets the person and drops events; turning it on sends again", async () => {
  const telemetry = await started();
  await vi.waitFor(() => expect(posthog.init).toHaveBeenCalled());
  telemetry.setTelemetryEnabled(false);
  expect(telemetry.telemetryChoice().enabled).toBe(false);
  await vi.waitFor(() => expect(posthog.reset).toHaveBeenCalled());
  // What every event says about the launcher survives the reset.
  expect(posthog.register).toHaveBeenCalledTimes(2);
  expect(beforeSend({ event: "$exception" })).toBeNull();
  telemetry.track("screen_viewed", { screen: "library" });
  telemetry.captureError(new Error("boom"));
  await settle();
  expect(posthog.capture).not.toHaveBeenCalled();
  expect(posthog.captureException).not.toHaveBeenCalled();

  telemetry.setTelemetryEnabled(true);
  telemetry.track("screen_viewed", { screen: "library" });
  await vi.waitFor(() => expect(posthog.capture).toHaveBeenCalledTimes(1));
  expect(beforeSend({ event: "screen_viewed" })).not.toBeNull();
});

test("this computer's choice off means nothing loads at all", async () => {
  const telemetry = await import("./telemetry");
  telemetry.setTelemetryLocal(false);
  telemetry.startTelemetry(context);
  telemetry.setTelemetryAccount(null);
  telemetry.track("launcher_started");
  await settle();
  expect(posthog.init).not.toHaveBeenCalled();
});

test("signed in, the account's choice wins; signed out, this computer's", async () => {
  const telemetry = await started({ account: { id: "u1", name: "MagicTurtle", optOut: true } });
  expect(telemetry.telemetryChoice()).toEqual({ available: true, enabled: false, signedIn: true });
  await settle();
  expect(posthog.init).not.toHaveBeenCalled();
  expect(posthog.identify).not.toHaveBeenCalled();

  telemetry.setTelemetryAccount(null);
  expect(telemetry.telemetryChoice()).toEqual({ available: true, enabled: true, signedIn: false });

  // Off here, on for the account: the account's choice is followed while signed in.
  telemetry.setTelemetryLocal(false);
  expect(telemetry.telemetryChoice().enabled).toBe(false);
  telemetry.setTelemetryAccount({ id: "u1", optOut: false });
  expect(telemetry.telemetryChoice().enabled).toBe(true);
  await vi.waitFor(() => expect(posthog.identify).toHaveBeenCalledWith("u1", {}, undefined));
});

test("signed in, events join the account's person, with its name, role and provider; signing out forgets it", async () => {
  const telemetry = await started({
    account: { id: "u1", name: "MagicTurtle", role: "member", provider: "discord", createdAt: Date.UTC(2026, 8, 30), optOut: false },
  });
  await vi.waitFor(() => expect(posthog.identify).toHaveBeenCalledTimes(1));
  expect(posthog.identify).toHaveBeenCalledWith(
    "u1",
    { name: "MagicTurtle", role: "member", provider: "discord" },
    { created_at: "2026-09-30T00:00:00.000Z" },
  );
  // The same account again doesn't identify again.
  telemetry.setTelemetryAccount({ id: "u1", name: "MagicTurtle", role: "member", provider: "discord", createdAt: Date.UTC(2026, 8, 30), optOut: false });
  telemetry.setTelemetryAccount(null);
  await vi.waitFor(() => expect(posthog.reset).toHaveBeenCalledTimes(1));
  expect(posthog.identify).toHaveBeenCalledTimes(1);
});

test("the switch flipped while signed in stands for the account until its setting comes back", async () => {
  const telemetry = await started({ account: { id: "u1", optOut: false } });
  telemetry.setTelemetryEnabled(false);
  expect(telemetry.telemetryChoice()).toEqual({ available: true, enabled: false, signedIn: true });
  await vi.waitFor(() => expect(posthog.reset).toHaveBeenCalled());
  // Signed out afterwards, this computer remembers it too.
  telemetry.setTelemetryAccount(null);
  expect(telemetry.telemetryChoice().enabled).toBe(false);
});

test("paths, the home and apps folders and the user name never leave, in properties or errors", async () => {
  await started();
  await vi.waitFor(() => expect(posthog.init).toHaveBeenCalled());
  const sent = beforeSend({
    event: "$exception",
    properties: {
      reason: "Couldn't start C:\\Users\\Ada Lovelace\\Games\\port.exe: Access is denied.",
      drive: "D:/Quiver/apps/test-port/port.exe",
      posix: "No such file: /home/ada/games/port.sh",
      mac: "at /Users/ada/Library/x (line 2)",
      tilde: "~/Games/port.sh",
      share: "\\\\nas\\games\\port.exe",
      spaces: "Couldn't start C:\\Program Files\\Game\\game.exe",
      who: "Made by Ada",
      slug: "test-port",
      web: "https://github.com/Users/x and http://tauri.localhost/assets/index.js",
      nested: { list: ["/tmp/quiver-data-abc/apps", 3, null] },
      $exception_list: [
        {
          type: "Error",
          value: "ENOENT: no such file or directory, open 'C:\\Users\\Ada Lovelace\\AppData\\x.json'",
          stacktrace: {
            frames: [
              { filename: "file:///C:/Users/Ada%20Lovelace/app.js", lineno: 1 },
              { filename: "http://tauri.localhost/assets/index-abc.js", function: "go", lineno: 2 },
            ],
          },
        },
      ],
      $set: { name: "ada" },
    },
  })!;
  const p = sent.properties;
  expect(p.reason).toBe("Couldn't start <path> Access is denied.");
  expect(p.drive).toBe("<path>");
  expect(p.posix).toBe("No such file: <path>");
  expect(p.mac).toBe("at <path> (line 2)");
  expect(p.tilde).toBe("<path>");
  expect(p.share).toBe("<path>");
  expect(p.spaces).toBe("Couldn't start <path> <path>");
  expect(p.who).toBe("Made by <user>");
  expect(p.slug).toBe("test-port");
  expect(p.web).toBe("https://github.com/Users/x and http://tauri.localhost/assets/index.js");
  expect(p.nested).toEqual({ list: ["<path>", 3, null] });
  expect(p.$exception_list[0].value).toBe("ENOENT: no such file or directory, open '<path>'");
  expect(p.$exception_list[0].stacktrace.frames[0].filename).toBe("<path>");
  expect(p.$exception_list[0].stacktrace.frames[1]).toEqual({ filename: "http://tauri.localhost/assets/index-abc.js", function: "go", lineno: 2 });
  // Their Quiver name is theirs to send, as on the website.
  expect(p.$set).toEqual({ name: "ada" });
  const { $set: _, ...rest } = p;
  expect(JSON.stringify(rest)).not.toMatch(/ada|lovelace|quiver[\\/]apps/i);
});

test("a failure's reason is short and safe", async () => {
  const telemetry = await started();
  expect(telemetry.reasonOf(new Error(`Couldn't start /home/ada/x.sh: ${"x".repeat(200)}`))).toHaveLength(120);
  expect(telemetry.reasonOf("This release has no download for your computer.")).toBe("This release has no download for your computer.");
});

test("errors caught by an error boundary go to error tracking", async () => {
  const telemetry = await started();
  const error = new Error("page failed");
  telemetry.captureError(error, { boundary: "page" });
  await vi.waitFor(() => expect(posthog.captureException).toHaveBeenCalledWith(error, { boundary: "page" }));
});
