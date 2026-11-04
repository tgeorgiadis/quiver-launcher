import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { msg } from "@lingui/core/macro";
import type { TelemetryContext } from "./telemetry";

const sentry = vi.hoisted(() => ({ init: vi.fn(), captureException: vi.fn() }));
vi.mock("@sentry/browser", () => sentry);

const HOST = "https://quiverlauncher.com";
const context: TelemetryContext = {
  host: HOST,
  version: "4.0.0",
  os: "windows",
  arch: "x64",
  home: "C:\\Users\\Ada Lovelace",
  user: "ada",
  appsDir: "D:\\Quiver\\apps",
};

/** What the website says about asking first; a rejection stands for not reaching it. */
let consent: () => Promise<Response>;
const fetched = vi.fn();
const usageCalls = () => fetched.mock.calls.filter(([url]) => String(url).endsWith("/api/launcher/usage"));
const consentCalls = () => fetched.mock.calls.filter(([url]) => String(url).endsWith("/api/launcher/consent"));
const sentBodies = () => usageCalls().map(([, init]) => JSON.parse(init.body as string));
const sentEvents = () => sentBodies().flatMap((body) => body.events);

beforeEach(() => {
  vi.resetModules();
  vi.stubEnv("VITE_USAGE_DATA", "1");
  vi.stubEnv("VITE_SENTRY_DSN", "https://key@sentry.test/1");
  vi.stubEnv("DEV", false);
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "Date"] });
  const stored = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => stored.get(key) ?? null,
    setItem: (key: string, value: string) => stored.set(key, value),
    removeItem: (key: string) => stored.delete(key),
  });
  vi.stubGlobal("window", new EventTarget());
  fetched.mockReset();
  sentry.init.mockReset();
  sentry.captureException.mockReset();
  consent = async () => Response.json({ askFirst: false });
  fetched.mockImplementation((url: string) => (url.endsWith("/consent") ? consent() : Promise.resolve(new Response(null, { status: 204 }))));
  vi.stubGlobal("fetch", fetched);
});
afterEach(async () => {
  vi.useRealTimers();
  // Sentry loading from a test that didn't wait for it doesn't land in the next one, however slowly it loads.
  await vi.dynamicImportSettled();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
});

/** Lets the consent lookup and Sentry's import finish. */
const settle = async () => {
  vi.useRealTimers();
  await vi.dynamicImportSettled();
  await new Promise((r) => setTimeout(r, 20));
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "Date"] });
};
/** Past the wait for more events. */
const sendWaiting = () => vi.advanceTimersByTime(10_000);

/** A fresh copy of the module, started signed out unless told otherwise. */
async function started(options: { account?: Parameters<typeof import("./telemetry").setTelemetryAccount>[0]; host?: string } = {}) {
  const telemetry = await import("./telemetry");
  telemetry.startTelemetry({ ...context, host: options.host ?? context.host });
  telemetry.setTelemetryAccount(options.account === undefined ? null : options.account);
  await settle();
  return telemetry;
}
const beforeSend = (event: object) => sentry.init.mock.calls[0][0].beforeSend(event);

test("a build not asked to send sends and asks nothing", async () => {
  vi.stubEnv("VITE_USAGE_DATA", "");
  const telemetry = await started();
  telemetry.track("screen_viewed", { screen: "library" });
  sendWaiting();
  expect(fetched).not.toHaveBeenCalled();
  expect(sentry.init).not.toHaveBeenCalled();
  expect(telemetry.telemetryChoice()).toEqual({ available: false, enabled: false, signedIn: false, ask: false });
});

test("pnpm dev sends nothing unless VITE_USAGE_DEV=1", async () => {
  vi.stubEnv("DEV", true);
  let telemetry = await started();
  expect(telemetry.telemetryAvailable()).toBe(false);
  expect(fetched).not.toHaveBeenCalled();

  vi.resetModules();
  vi.stubEnv("VITE_USAGE_DEV", "1");
  telemetry = await started();
  expect(telemetry.telemetryAvailable()).toBe(true);
  expect(consentCalls()).toHaveLength(1);
});

test("events go to the website in a batch, each saying it's the launcher, under a random id", async () => {
  const telemetry = await started();
  telemetry.track("launcher_started", { first_run: true });
  telemetry.track("screen_viewed", { screen: "browse" });
  expect(usageCalls()).toHaveLength(0);
  sendWaiting();
  expect(usageCalls()).toHaveLength(1);
  expect(usageCalls()[0][0]).toBe(`${HOST}/api/launcher/usage`);
  const [{ client_id: id, events }] = sentBodies();
  expect(id).toMatch(/^[0-9a-f]{32}$/);
  expect(events.map((e: { name: string }) => e.name)).toEqual(["launcher_started", "screen_viewed"]);
  expect(events[0].params).toMatchObject({ app: "launcher", launcher_version: "4.0.0", os: "windows", arch: "x64", first_run: true });
  expect(events[0].params.session_id).toMatch(/^\d+$/);
  expect(events[0].params.engagement_time_msec).toBeGreaterThan(0);
});

test("a full batch goes at once, and what's waiting goes when the window closes", async () => {
  const telemetry = await started();
  for (let i = 0; i < 25; i++) telemetry.track("e");
  expect(sentEvents()).toHaveLength(25);
  telemetry.track("last");
  (window as unknown as EventTarget).dispatchEvent(new Event("pagehide"));
  expect(sentEvents()).toHaveLength(26);
  expect(usageCalls()[1][1].keepalive).toBe(true);
});

test("the same id is kept across launches", async () => {
  let telemetry = await started();
  telemetry.track("e");
  sendWaiting();
  vi.resetModules();
  telemetry = await started();
  telemetry.track("e");
  sendWaiting();
  const [first, second] = sentBodies();
  expect(second.client_id).toBe(first.client_id);
});

test("nothing starts until the account's choice is known; events from before are sent once it is", async () => {
  const telemetry = await import("./telemetry");
  telemetry.startTelemetry(context);
  telemetry.track("launcher_started", { first_run: true });
  await settle();
  sendWaiting();
  expect(usageCalls()).toHaveLength(0);
  expect(sentry.init).not.toHaveBeenCalled();
  telemetry.setTelemetryAccount(null);
  sendWaiting();
  expect(sentEvents().map((e) => e.name)).toEqual(["launcher_started"]);
});

test("where it must ask first, nothing goes until the player says yes", async () => {
  consent = async () => Response.json({ askFirst: true });
  const telemetry = await started();
  telemetry.track("launcher_started");
  sendWaiting();
  await settle();
  expect(usageCalls()).toHaveLength(0);
  expect(sentry.init).not.toHaveBeenCalled();
  expect(telemetry.telemetryChoice()).toEqual({ available: true, enabled: false, signedIn: false, ask: true });

  telemetry.setTelemetryEnabled(true);
  sendWaiting();
  await settle();
  expect(sentEvents().map((e) => e.name)).toEqual(["launcher_started"]);
  expect(sentry.init).toHaveBeenCalled();
  expect(telemetry.telemetryChoice()).toEqual({ available: true, enabled: true, signedIn: false, ask: false });
});

test("saying no where it asks drops what was waiting and stops asking", async () => {
  consent = async () => Response.json({ askFirst: true });
  const telemetry = await started();
  telemetry.track("launcher_started");
  telemetry.setTelemetryEnabled(false);
  sendWaiting();
  expect(fetched.mock.calls.filter(([url]) => String(url).endsWith("/usage"))).toHaveLength(0);
  expect(telemetry.telemetryChoice().ask).toBe(false);
});

test.each([
  ["can't be reached", () => Promise.reject(new Error("offline"))],
  ["answers with an error", async () => new Response(null, { status: 500 })],
  ["answers with nonsense", async () => Response.json({})],
])("where the website %s, it asks", async (_, answer) => {
  consent = answer;
  const telemetry = await started();
  telemetry.track("launcher_started");
  sendWaiting();
  expect(usageCalls()).toHaveLength(0);
  expect(telemetry.telemetryChoice().ask).toBe(true);
});

test("a yes from before needs no lookup", async () => {
  const telemetry = await import("./telemetry");
  telemetry.setTelemetryLocal(true, true);
  telemetry.startTelemetry(context);
  telemetry.setTelemetryAccount(null);
  telemetry.track("launcher_started");
  sendWaiting();
  expect(consentCalls()).toHaveLength(0);
  expect(sentEvents().map((e) => e.name)).toEqual(["launcher_started"]);
});

test("turned off here, it contacts nothing at all", async () => {
  const telemetry = await import("./telemetry");
  telemetry.setTelemetryLocal(false);
  telemetry.startTelemetry(context);
  telemetry.setTelemetryAccount(null);
  telemetry.track("launcher_started");
  await settle();
  sendWaiting();
  expect(fetched).not.toHaveBeenCalled();
  expect(sentry.init).not.toHaveBeenCalled();
});

test("turning it off forgets the id and drops what's waiting and errors; on again sends under a new id", async () => {
  const telemetry = await started();
  telemetry.track("before");
  sendWaiting();
  telemetry.track("waiting");
  telemetry.setTelemetryEnabled(false);
  expect(localStorage.getItem("quiver-usage-id")).toBeNull();
  sendWaiting();
  expect(beforeSend({ message: "boom" })).toBeNull();
  telemetry.track("while_off");
  telemetry.captureError(new Error("boom"));
  sendWaiting();
  await settle();
  expect(sentEvents().map((e) => e.name)).toEqual(["before"]);
  expect(sentry.captureException).not.toHaveBeenCalled();

  telemetry.setTelemetryEnabled(true);
  telemetry.track("after");
  sendWaiting();
  const bodies = sentBodies();
  expect(bodies[1].events.map((e: { name: string }) => e.name)).toEqual(["after"]);
  expect(bodies[1].client_id).not.toBe(bodies[0].client_id);
  expect(beforeSend({ message: "boom" })).not.toBeNull();
});

test("off on the account or on this computer is off; signed out, this computer's choice alone", async () => {
  const telemetry = await started({ account: { optOut: true } });
  expect(telemetry.telemetryChoice()).toMatchObject({ enabled: false, signedIn: true });
  telemetry.track("while_off");
  sendWaiting();
  expect(usageCalls()).toHaveLength(0);

  telemetry.setTelemetryAccount(null);
  await settle();
  expect(telemetry.telemetryChoice()).toMatchObject({ enabled: true, signedIn: false });

  // Off here, on for the account (its saving failed, or it was turned off before signing in): still off.
  telemetry.setTelemetryLocal(false);
  telemetry.setTelemetryAccount({ optOut: false });
  expect(telemetry.telemetryChoice()).toMatchObject({ enabled: false, signedIn: true });

  // The switch turned on while signed in turns it on for both.
  telemetry.setTelemetryEnabled(true);
  expect(telemetry.telemetryChoice().enabled).toBe(true);
});

test("the switch flipped while signed in stands for the account until its setting comes back", async () => {
  const telemetry = await started({ account: { optOut: false } });
  telemetry.setTelemetryEnabled(false);
  expect(telemetry.telemetryChoice()).toMatchObject({ enabled: false, signedIn: true });
  // Signed out afterwards, this computer remembers it too.
  telemetry.setTelemetryAccount(null);
  expect(telemetry.telemetryChoice().enabled).toBe(false);
});

test("nothing about the account is sent", async () => {
  const telemetry = await started({ account: { optOut: false } });
  telemetry.track("e");
  sendWaiting();
  expect(Object.keys(sentBodies()[0])).toEqual(["client_id", "events"]);
});

test("Sentry reports nothing by itself and keeps nothing the player did", async () => {
  await started();
  const options = sentry.init.mock.calls[0][0];
  expect(options).toMatchObject({ release: "quiver-launcher@4.0.0", sendClientReports: false });
  expect(options.sendDefaultPii).toBeFalsy();
  expect(options.tracesSampleRate).toBeUndefined();
  expect(options.replaysSessionSampleRate).toBeUndefined();
  const names = ["BrowserSession", "Breadcrumbs", "Console", "GlobalHandlers", "Dedupe"].map((name) => ({ name }));
  expect(options.integrations(names).map((i: { name: string }) => i.name)).toEqual(["GlobalHandlers", "Dedupe"]);
});

test("anywhere but the website, as in tests and smoke runs, no errors are reported", async () => {
  await started({ host: "http://127.0.0.1:9" });
  expect(sentry.init).not.toHaveBeenCalled();
});

test("without a DSN no errors are reported, and events still go", async () => {
  vi.stubEnv("VITE_SENTRY_DSN", "");
  const telemetry = await started();
  telemetry.track("e");
  sendWaiting();
  expect(sentry.init).not.toHaveBeenCalled();
  expect(sentEvents()).toHaveLength(1);
});

test("paths, the home and apps folders and the user name never leave, in events", async () => {
  const telemetry = await started();
  telemetry.track("app_launch_failed", {
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
  });
  sendWaiting();
  const p = sentEvents()[0].params;
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
  expect(JSON.stringify(p)).not.toMatch(/ada|lovelace|quiver[\\/]apps/i);
});

test("paths, folders and the user name never leave, in error reports", async () => {
  await started();
  const sent = beforeSend({
    exception: {
      values: [
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
    },
    extra: { note: "ada was here" },
  });
  const [value] = sent.exception.values;
  expect(value.value).toBe("ENOENT: no such file or directory, open '<path>'");
  expect(value.stacktrace.frames[0].filename).toBe("<path>");
  expect(value.stacktrace.frames[1]).toEqual({ filename: "http://tauri.localhost/assets/index-abc.js", function: "go", lineno: 2 });
  expect(sent.extra.note).toBe("<user> was here");
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
  await settle();
  expect(sentry.captureException).toHaveBeenCalledWith(error, { extra: { boundary: "page" } });
});

test("errors from before it could send wait for the choices, and go once they're known", async () => {
  const telemetry = await import("./telemetry");
  const error = new Error("too early");
  telemetry.captureError(error, { handled: "error_boundary" });
  telemetry.startTelemetry(context);
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
  telemetry.setTelemetryAccount(null);
  await settle();
  expect(sentry.captureException).toHaveBeenCalledTimes(1);
  expect(sentry.captureException).toHaveBeenCalledWith(error, { extra: { handled: "error_boundary" } });
});

test("errors held where it asks first go only on a yes, and a no drops them", async () => {
  consent = async () => Response.json({ askFirst: true });
  let telemetry = await started();
  telemetry.captureError(new Error("held"));
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
  telemetry.setTelemetryEnabled(true);
  await settle();
  expect(sentry.captureException).toHaveBeenCalledTimes(1);

  vi.resetModules();
  sentry.captureException.mockReset();
  telemetry = await started();
  telemetry.captureError(new Error("held"));
  telemetry.setTelemetryEnabled(false);
  telemetry.setTelemetryEnabled(true);
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
});

test("only so many errors are held", async () => {
  const telemetry = await import("./telemetry");
  for (let i = 0; i < 30; i++) telemetry.captureError(new Error(String(i)));
  telemetry.startTelemetry(context);
  telemetry.setTelemetryAccount(null);
  await settle();
  expect(sentry.captureException).toHaveBeenCalledTimes(20);
});

test("uncaught errors are held only until Sentry's own handlers are running", async () => {
  const telemetry = await import("./telemetry");
  const early = new Error("early");
  telemetry.captureUncaught(early);
  telemetry.startTelemetry(context);
  telemetry.setTelemetryAccount(null);
  await settle();
  expect(sentry.captureException).toHaveBeenCalledWith(early, { extra: { handled: "before_start" } });
  telemetry.captureUncaught(new Error("late"));
  await settle();
  expect(sentry.captureException).toHaveBeenCalledTimes(1);
});

/** A launch that stops before the choices can be known, after one that did or didn't allow it. */
async function failedStart(lastLaunch: () => void) {
  lastLaunch();
  vi.resetModules();
  sentry.init.mockReset();
  const telemetry = await import("./telemetry");
  telemetry.captureError(new Error("no start"));
  telemetry.reportStartupFailure(context);
  await settle();
  return telemetry;
}

test("a launch that can't start reports what's held if the last one was allowed to", async () => {
  const before = await started();
  await failedStart(() => before.track("e"));
  expect(sentry.captureException).toHaveBeenCalledTimes(1);
  expect(beforeSend({ message: "boom" })).not.toBeNull();
});

test("a first launch that can't start reports nothing", async () => {
  const telemetry = await failedStart(() => {});
  expect(sentry.init).not.toHaveBeenCalled();
  expect(sentry.captureException).not.toHaveBeenCalled();
  expect(telemetry.telemetryChoice().enabled).toBe(false);
});

test("a launch that can't start reports nothing after a launch that wasn't allowed to, or was turned off", async () => {
  consent = async () => Response.json({ askFirst: true });
  await started();
  await failedStart(() => {});
  expect(sentry.captureException).not.toHaveBeenCalled();

  vi.resetModules();
  consent = async () => Response.json({ askFirst: false });
  const on = await started();
  await failedStart(() => on.setTelemetryEnabled(false));
  expect(sentry.captureException).not.toHaveBeenCalled();

  vi.resetModules();
  const account = await started({ account: { optOut: true } });
  await failedStart(() => account.track("e"));
  expect(sentry.captureException).not.toHaveBeenCalled();
});

test("a failure from the Rust side is told apart by what it says, with what was being done", async () => {
  const telemetry = await started();
  telemetry.captureFailure("install", "Couldn't unpack C:\\Users\\Ada Lovelace\\x.zip", { slug: "test-port" });
  await settle();
  const [error, scope] = sentry.captureException.mock.calls[0];
  expect(error).toBeInstanceOf(Error);
  expect(error.message).toBe("Couldn't unpack C:\\Users\\Ada Lovelace\\x.zip");
  expect(scope).toEqual({ tags: { failure: "install" }, extra: { slug: "test-port" }, fingerprint: ["failure", "install", "Couldn't unpack <path>"] });
});

test("a failure that's a real error keeps it, and is grouped by its stack", async () => {
  const telemetry = await started();
  const error = new TypeError("x is undefined");
  telemetry.captureFailure("launch", error);
  await settle();
  expect(sentry.captureException).toHaveBeenCalledWith(error, { tags: { failure: "launch" }, extra: undefined });
});

test.each([
  ["cancelled"],
  ["The download failed. Check your connection and try again."],
  ["The download stopped: error decoding response body"],
  ["Couldn't check for updates: error sending request for url (https://api.github.com/x)"],
  ["Couldn't check for updates: HTTP status client error (403 Forbidden) for url (https://api.github.com/x)"],
  ["Couldn't download the update: HTTP status server error (503 Service Unavailable) for url (https://x)"],
  ["operation timed out"],
  ["No space left on device (os error 28)"],
  ["There is not enough space on the disk. (os error 112)"],
  [new TypeError("Failed to fetch")],
])("%s isn't the launcher's to fix", async (failure) => {
  const telemetry = await started();
  telemetry.captureFailure("install", failure);
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
});

test("a failure already worded for the player isn't reported; others that look close are", async () => {
  const telemetry = await started();
  telemetry.captureFailure("install", new telemetry.Problem(msg`This release has no download for your computer.`));
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
  telemetry.captureFailure("install", "HTTP status client error (404 Not Found) for url (https://x)");
  telemetry.captureFailure("launch", "Access is denied. (os error 5)");
  await settle();
  expect(sentry.captureException).toHaveBeenCalledTimes(2);
});

test("failures while it's off are dropped", async () => {
  const telemetry = await started();
  telemetry.setTelemetryEnabled(false);
  telemetry.captureFailure("install", "Couldn't unpack it");
  telemetry.setTelemetryEnabled(true);
  await settle();
  expect(sentry.captureException).not.toHaveBeenCalled();
});

test("each screen is sent once per launch, and again after turning it off and on", async () => {
  const telemetry = await started();
  telemetry.trackScreen({ screen: "browse" });
  telemetry.trackScreen({ screen: "app", slug: "test-port" });
  telemetry.trackScreen({ screen: "browse" });
  telemetry.trackScreen({ screen: "app", slug: "other-port" });
  telemetry.trackScreen({ screen: "app", slug: "test-port" });
  sendWaiting();
  expect(sentEvents().map((e) => e.params.screen + (e.params.slug ?? ""))).toEqual(["browse", "apptest-port", "appother-port"]);

  telemetry.setTelemetryEnabled(false);
  telemetry.setTelemetryEnabled(true);
  telemetry.trackScreen({ screen: "browse" });
  sendWaiting();
  expect(sentEvents()).toHaveLength(4);
});

test("the browser's harmless ResizeObserver warning isn't sent; other errors are", async () => {
  await started();
  const error = (...values: string[]) => beforeSend({ exception: { values: values.map((value) => ({ type: "Error", value })) } });
  expect(error("ResizeObserver loop completed with undelivered notifications.")).toBeNull();
  expect(error("ResizeObserver loop limit exceeded")).toBeNull();
  expect(error("Cannot read properties of undefined (reading 'length')")).not.toBeNull();
  expect(error("ResizeObserver loop limit exceeded", "Failed to fetch")).not.toBeNull();
});

test("a problem reaches usage data in English, whatever language it showed in", async () => {
  const telemetry = await started();
  const { i18n } = await import("@quiverlauncher/i18n");
  const version = "v2";
  const said = msg`${version} is gone`;
  i18n.loadAndActivate({ locale: "pt-BR", messages: { [said.id]: "{version} sumiu" } });
  try {
    const problem = new telemetry.Problem(said);
    expect(problem.message).toBe("v2 sumiu");
    expect(telemetry.reasonOf(problem)).toBe("{version} is gone");
  } finally {
    i18n.loadAndActivate({ locale: "en", messages: {} });
  }
});
