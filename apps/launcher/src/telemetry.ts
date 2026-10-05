/**
 * Anonymous usage data and errors, sent to PostHog as the website does (its
 * src/analytics.tsx): on unless the player turns it off, in Settings or on
 * their Quiver account. Signed in, the account's choice wins and events join
 * the same person as on the website; signed out, they stay anonymous.
 *
 * Only the events below are sent, never clicks or text on the screen, and
 * nothing that looks like a file or folder, or the computer's user name, ever
 * leaves (`scrub`). Without a project token (VITE_POSTHOG_PROJECT_TOKEN) at
 * build time nothing loads at all, and `pnpm dev` sends nothing unless
 * VITE_POSTHOG_DEV=1 is set too.
 */
import { useSyncExternalStore } from "react";
import type { PostHog, CaptureResult, Properties } from "posthog-js";

/** PostHog's US cloud, where the website's events go too. */
export const DEFAULT_HOST = "https://us.i.posthog.com";

/** What every event says about this copy of the launcher, and what none may contain. */
export type TelemetryContext = {
  host: string;
  version: string;
  os: string;
  arch: string;
  /** Folders and the OS user name, taken out of anything sent. */
  home?: string | null;
  user?: string | null;
  appsDir?: string | null;
};

/** The signed-in account, as the website's users.me has it. */
export type TelemetryAccount = { id: string; name?: string; role?: string; provider?: string; createdAt?: number; optOut: boolean };

type Client = Promise<PostHog | null>;

const env = () => import.meta.env;
const token = () => (env().VITE_POSTHOG_PROJECT_TOKEN as string | undefined)?.trim() || undefined;

/** Whether this build sends anything at all: it has a token, and isn't `pnpm dev` (unless asked). */
export function telemetryAvailable() {
  if (!token()) return false;
  return !env().DEV || env().VITE_POSTHOG_DEV === "1";
}

let context: TelemetryContext | null = null;
/** This computer's choice (Settings), used while signed out. */
let localOff = false;
/** The signed-in account's choice; null signed out. */
let accountOff: boolean | null = null;
/** undefined until known: nothing starts before the account's choice can be followed. */
let account: TelemetryAccount | null | undefined;
let client: Client | null = null;
let identified: string | null = null;
/** Events from before PostHog loaded (the start, the first screen), sent once it has. */
let queue: [string, Properties | undefined][] = [];
const listeners = new Set<() => void>();

const off = () => accountOff ?? localOff;

const superProperties = (c: TelemetryContext) => ({ app: "launcher", launcher_version: c.version, os: c.os, arch: c.arch });

/** A test token (phc_test…) only ever goes to a stand-in, never to PostHog itself. */
const isPostHogCloud = (host: string) => /(^|\.)posthog\.com$/i.test(hostname(host));
function hostname(url: string) {
  try {
    return new URL(url).hostname;
  } catch {
    return url;
  }
}

function start(): Client | null {
  if (client || !context || account === undefined || off() || !telemetryAvailable()) return client;
  const projectToken = token()!;
  const ctx = context;
  if (/^phc_test/i.test(projectToken) && isPostHogCloud(ctx.host)) return null;
  client = Promise.all([
    // The bundle that never loads scripts from elsewhere: nothing remote runs in the launcher's window.
    import("posthog-js/no-external"),
    // Error tracking's handlers, bundled in for the same reason.
    import("posthog-js/dist/exception-autocapture"),
  ])
    .then(([{ default: posthog }]) => {
      posthog.init(projectToken, {
        api_host: ctx.host,
        // Links in PostHog's toolbar still go to PostHog itself, as on the website.
        ui_host: "https://us.posthog.com",
        defaults: "2026-05-30",
        persistence: "localStorage",
        person_profiles: "identified_only",
        // Explicit events only: no clicks, text or pages captured by themselves.
        autocapture: false,
        rageclick: false,
        capture_pageview: false,
        capture_pageleave: false,
        capture_dead_clicks: false,
        capture_heatmaps: false,
        capture_performance: false,
        save_referrer: false,
        save_campaign_params: false,
        // Errors are tracked: uncaught ones and rejected promises.
        capture_exceptions: { capture_unhandled_errors: true, capture_unhandled_rejections: true, capture_console_errors: false },
        disable_session_recording: true,
        disable_surveys: true,
        disable_product_tours: true,
        disable_conversations: true,
        disable_web_experiments: true,
        disable_external_dependency_loading: true,
        // No feature flags or remote config: only events are sent.
        advanced_disable_flags: true,
        // PostHog drops events from a window that WebDriver controls (navigator.webdriver), which on Linux is how
        // the end-to-end tests drive the app. A desktop app isn't crawled by bots, so nothing real is lost by keeping them.
        opt_out_useragent_filter: true,
        // A stand-in (end-to-end tests) reads plain JSON.
        disable_compression: hostname(ctx.host) !== hostname(DEFAULT_HOST),
        // Turning it off after PostHog loaded drops everything; what's left is scrubbed.
        before_send: (event) => (!event || off() ? null : scrubEvent(event)),
      });
      posthog.register(superProperties(ctx));
      return posthog;
    })
    .catch(() => null);
  void client.then((posthog) => {
    if (!posthog) return;
    syncIdentity();
    const waiting = queue;
    queue = [];
    if (!off()) for (const [event, properties] of waiting) posthog.capture(event, properties);
  });
  return client;
}

/** A fresh anonymous identifier, keeping what every event says about the launcher. */
function forget() {
  identified = null;
  const ctx = context;
  void client?.then((posthog) => {
    posthog?.reset();
    if (ctx) posthog?.register(superProperties(ctx));
  });
}

/** Links events to the signed-in account, as the website does, and forgets it on sign-out or opting out. */
function syncIdentity() {
  const a = account;
  if (!a || off()) {
    if (identified) forget();
    return;
  }
  const current = client ?? start();
  if (!current) return;
  const props = Object.fromEntries(
    Object.entries({ name: a.name, role: a.role, provider: a.provider }).filter(([, v]) => v !== undefined),
  );
  // Again when the name or role changes, so the person stays current.
  const key = JSON.stringify([a.id, props]);
  if (identified === key) return;
  identified = key;
  void current.then((posthog) =>
    posthog?.identify(a.id, props, a.createdAt ? { created_at: new Date(a.createdAt).toISOString() } : undefined),
  );
}

/** Acts on a change of choice and tells the switches. */
function changed(wasOff: boolean) {
  if (!off()) start();
  else if (!wasOff) {
    queue = [];
    forget();
  }
  syncIdentity();
  snapshot = telemetryChoice();
  listeners.forEach((listener) => listener());
}

/** Starts sending (if it may), once the launcher knows where to and what it is. */
export function startTelemetry(next: TelemetryContext) {
  context = next;
  start();
  syncIdentity();
}

/** This computer's choice, from Settings. */
export function setTelemetryLocal(enabled: boolean) {
  const was = off();
  localOff = !enabled;
  changed(was);
}

/** The signed-in account (null signed out; undefined while it loads), whose choice wins while signed in. */
export function setTelemetryAccount(next: TelemetryAccount | null | undefined) {
  if (next === undefined) return;
  const was = off();
  account = next;
  accountOff = next ? next.optOut : null;
  changed(was);
}

/**
 * The player turning it off or back on: this computer remembers it for when
 * nobody's signed in, and while someone is it stands for their account until
 * the saved setting (users.setAnalytics) comes back.
 */
export function setTelemetryEnabled(enabled: boolean) {
  const was = off();
  localOff = !enabled;
  if (accountOff !== null) accountOff = !enabled;
  changed(was);
}

/** Whether this build sends anything, whether it's on, and whether the choice is the account's. */
export function telemetryChoice() {
  const available = telemetryAvailable();
  return { available, enabled: available && !off(), signedIn: accountOff !== null };
}

let snapshot = telemetryChoice();
/** The choice, re-rendering when it changes. */
export function useTelemetryChoice() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => snapshot,
  );
}

/** Records a product event; does nothing when it's off. */
export function track(event: string, properties?: Properties) {
  if (!telemetryAvailable() || off()) return;
  if (!client) {
    if (queue.length < 50) queue.push([event, properties]);
    return;
  }
  void client.then((posthog) => posthog?.capture(event, properties));
}

/** Reports an error caught on the way (an error boundary's) to error tracking. */
export function captureError(error: unknown, properties?: Properties) {
  if (!telemetryAvailable() || off()) return;
  void (client ?? start())?.then((posthog) => posthog?.captureException(error, properties));
}

/** A short, safe reason for a failure: the error's message without paths or names, at most 120 characters. */
export function reasonOf(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  return scrub(message).slice(0, 120);
}

const PATH = "<path>";
const escape = (text: string) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
/** Paths as Windows, Linux and macOS write them, wherever they appear in a string. */
const PATHS = [
  // file:///C:/Users/… and file:///home/…
  /\bfile:\/\/[^\s"'<>]*/gi,
  // C:\Users\… or D:/Games/… (not the "p:/" in "http://")
  /(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s"'<>|*?]*/g,
  // \\server\share\…
  /\\\\[^\s"'<>|*?\\]+\\[^\s"'<>|*?]*/g,
  // ~/… and /home/…, /Users/…, /tmp/… and the like (not the path of a web address)
  /(?<![\w.:/-])(?:~|\/(?:home|Users|root|tmp|var|private|mnt|media|opt|run|Volumes|usr|etc|srv|data|storage|sdcard|snap|nix|app|Applications|Library))(?:\/[^\s"'<>|,;)\]]*)?(?=$|[\s"'<>|,;)\]])/g,
  // What's left of a Windows path with spaces in it ("C:\Program Files\…"): anything with a backslash.
  /[^\s"'<>|]*\\[^\s"'<>|]*/g,
];

/** Takes paths, the home and apps folders, and the computer's user name out of a string. */
export function scrub(text: string, { keepName = false }: { keepName?: boolean } = {}) {
  let out = text;
  for (const folder of [context?.appsDir, context?.home]) {
    if (folder && folder.length > 2) out = out.replace(new RegExp(escape(folder), "gi"), PATH);
  }
  for (const pattern of PATHS) out = out.replace(pattern, PATH);
  out = out.replace(/(<path>)+/g, PATH);
  const user = context?.user;
  if (!keepName && user && user.length >= 2)
    out = out.replace(new RegExp(`(?<![\\p{L}\\p{N}_-])${escape(user)}(?![\\p{L}\\p{N}_-])`, "giu"), "<user>");
  return out;
}

function scrubValue(value: unknown, keepName: boolean, depth = 0): unknown {
  if (typeof value === "string") return scrub(value, { keepName });
  if (depth > 10 || value === null || typeof value !== "object") return value;
  if (Array.isArray(value)) return value.map((v) => scrubValue(v, false, depth + 1));
  return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, scrubValue(v, false, depth + 1)]));
}

/** The person's properties: their Quiver name is theirs to send, as on the website, even if it's their user name too. */
function scrubPerson(set: unknown) {
  if (!set || typeof set !== "object" || Array.isArray(set)) return scrubValue(set, false);
  return Object.fromEntries(Object.entries(set).map(([k, v]) => [k, scrubValue(v, k === "name")]));
}

/** An event with every string scrubbed: its properties, error messages and stack frames, and the person's. */
export function scrubEvent(event: CaptureResult): CaptureResult {
  const properties: Properties = {};
  for (const [key, value] of Object.entries(event.properties ?? {}))
    properties[key] = key === "$set" || key === "$set_once" ? scrubPerson(value) : scrubValue(value, false);
  return {
    ...event,
    properties,
    ...(event.$set ? { $set: scrubPerson(event.$set) as Properties } : {}),
    ...(event.$set_once ? { $set_once: scrubPerson(event.$set_once) as Properties } : {}),
  };
}
