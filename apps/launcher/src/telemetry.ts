/**
 * Usage data and errors: named events go to quiverlauncher.com, which passes
 * them on to Google Analytics (the website's worker/launcherUsage.ts), and
 * errors go to Sentry. As on the website (its src/analytics.tsx), it's on unless
 * the player turns it off, in Settings or on their Quiver account, and either
 * one off is off. Where the website asks first (the EU, UK and Switzerland),
 * nothing is sent until the player says yes, nor if that can't be found out.
 * Neither is linked to a Quiver account.
 *
 * Only the events below are sent, never clicks or text on the screen, and
 * nothing that looks like a file or folder, or the computer's user name, ever
 * leaves (`scrub`). Without VITE_USAGE_DATA=1 at build time nothing is sent at
 * all, and `pnpm dev` sends nothing unless VITE_USAGE_DEV=1 is set too.
 *
 * Errors from before the player's choices are known wait in memory until they
 * are. If the launcher can't get that far (`reportStartupFailure`), they go only
 * where usage data was allowed the last time it ran.
 */
import { useSyncExternalStore } from "react";
import type { ErrorEvent } from "@sentry/browser";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";

/** The website, which keeps the secret Google Analytics asks for. */
export const DEFAULT_HOST = "https://quiverlauncher.com";

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

/** The signed-in account's setting, as the website's users.me has it. */
export type TelemetryAccount = { optOut: boolean };

export type Properties = Record<string, unknown>;

type Sentry = typeof import("@sentry/browser");
/** What's sent with an error, besides the error. */
type ErrorScope = { extra?: Properties; tags?: Record<string, string>; fingerprint?: string[] };

const ID_KEY = "quiver-usage-id";
/** Set while usage data is allowed on this computer, for a launch that fails before it can find out again. */
const ALLOWED_KEY = "quiver-usage-allowed";
/** Errors kept until it's known whether they may be sent. */
const MAX_HELD_ERRORS = 20;
/** Google Analytics' limit for one request. */
const MAX_BATCH = 25;
const SEND_AFTER_MS = 10_000;
/** The longest time between events counted as using the launcher. */
const MAX_ENGAGEMENT_MS = 30 * 60_000;
/** Sentry's parts that report by themselves, whatever beforeSend says, or keep what the player did. */
const LEFT_OUT = new Set(["BrowserSession", "Breadcrumbs", "Console"]);

const env = () => import.meta.env;

/** Whether this build sends anything at all: it's asked to, and isn't `pnpm dev` (unless asked). */
export function telemetryAvailable() {
  if (env().VITE_USAGE_DATA !== "1") return false;
  return !env().DEV || env().VITE_USAGE_DEV === "1";
}

let context: TelemetryContext | null = null;
/** This computer's choice (Settings). */
let localOff = false;
/** The player said yes on this computer (Settings). */
let consented = false;
/** Whether the player must say yes first; undefined until the website has answered. */
let askFirst: boolean | undefined;
let lookingUp = false;
/** The signed-in account's choice; null signed out. */
let accountOff: boolean | null = null;
/** False until known: nothing starts before the account's choice can be followed. */
let accountKnown = false;
let errors: Promise<Sentry | null> | null = null;
/** Sentry is running, with its own handlers for uncaught errors. */
let sentryRunning = false;
/** This launch failed before it could learn the choices, and the last one was allowed to send. */
let lastResort = false;
/** Errors from before it could send. */
let heldErrors: [unknown, ErrorScope][] = [];
let clientId: string | null = null;
let sessionId: string | null = null;
let lastEventAt = 0;
let outbox: { name: string; params: Properties }[] = [];
let sendTimer: ReturnType<typeof setTimeout> | null = null;
let listeningForClose = false;
/** Screens already sent, each with its app or step (`trackScreen`). */
const screens = new Set<string>();
/** Events from before it could send (the start, the first screen), sent once it can. */
let queue: [string, Properties | undefined][] = [];
const listeners = new Set<() => void>();

/** Off if this computer or the signed-in account says so: an opt-out never comes undone by itself. */
const off = () => localOff || accountOff === true;
/** No yes is needed here, or the player gave it. */
const cleared = () => consented || askFirst === false;
const allowed = () => telemetryAvailable() && context !== null && !off() && (lastResort || (accountKnown && cleared()));

/** A random identifier for this computer, kept until usage data is turned off. Nothing else says who it is. */
function usageId() {
  if (clientId) return clientId;
  try {
    clientId = localStorage.getItem(ID_KEY);
  } catch {
    // Without storage it lasts until the launcher closes.
  }
  if (!clientId) {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    clientId = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
    try {
      localStorage.setItem(ID_KEY, clientId);
    } catch {
      // As above.
    }
  }
  return clientId;
}

function sendNow() {
  if (sendTimer) clearTimeout(sendTimer);
  sendTimer = null;
  if (!outbox.length || !context || !allowed()) return;
  const events = outbox;
  outbox = [];
  // As plain text, which needs no preflight; keepalive lets the last ones outlive the window.
  void fetch(`${context.host}/api/launcher/usage`, {
    method: "POST",
    body: JSON.stringify({ client_id: usageId(), events }),
    keepalive: true,
  }).catch(() => {});
}

function send(name: string, properties: Properties | undefined, ctx: TelemetryContext) {
  const now = Date.now();
  sessionId ??= String(Math.floor(now / 1000));
  const engaged = lastEventAt ? Math.min(now - lastEventAt, MAX_ENGAGEMENT_MS) : 0;
  lastEventAt = now;
  const params = scrubValue({
    app: "launcher",
    launcher_version: ctx.version,
    os: ctx.os,
    arch: ctx.arch,
    ...properties,
    session_id: sessionId,
    // Google Analytics only counts someone as active with some time spent.
    engagement_time_msec: Math.max(engaged, 1),
  }) as Properties;
  outbox.push({ name, params });
  if (outbox.length >= MAX_BATCH) sendNow();
  else sendTimer ??= setTimeout(sendNow, SEND_AFTER_MS);
}

/** Sentry, once it may send; only where events really go, so tests and smoke runs report nothing. */
function startErrors(ctx: TelemetryContext) {
  const dsn = (env().VITE_SENTRY_DSN as string | undefined)?.trim();
  if (errors || !dsn || ctx.host !== DEFAULT_HOST) return;
  errors = import("@sentry/browser")
    .then((sentry) => {
      sentry.init({
        dsn,
        release: `quiver-launcher@${ctx.version}`,
        environment: env().MODE,
        sendClientReports: false,
        integrations: (defaults) => defaults.filter(({ name }) => !LEFT_OUT.has(name)),
        // Turning it off after it loaded drops everything, and browser noise goes too; what's left is scrubbed.
        beforeSend: (event) => (!allowed() || isNoise(event) ? null : scrubEvent(event)),
      });
      sentryRunning = true;
      return sentry;
    })
    .catch(() => null);
}

/** Starts sending, if it may now, with what was waiting. */
function start() {
  const ctx = context;
  if (!ctx || !allowed()) return;
  if (!lastResort) {
    try {
      localStorage.setItem(ALLOWED_KEY, "1");
    } catch {
      // Then a launch that fails early reports nothing.
    }
  }
  startErrors(ctx);
  const held = heldErrors;
  heldErrors = [];
  for (const [error, scope] of held) report(error, scope);
  if (!listeningForClose) {
    listeningForClose = true;
    window.addEventListener("pagehide", sendNow);
  }
  const waiting = queue;
  queue = [];
  for (const [event, properties] of waiting) send(event, properties, ctx);
}

/** A fresh identifier, and nothing left to send. */
function forget() {
  queue = [];
  outbox = [];
  heldErrors = [];
  if (sendTimer) clearTimeout(sendTimer);
  sendTimer = null;
  screens.clear();
  clientId = null;
  sessionId = null;
  lastEventAt = 0;
  try {
    localStorage.removeItem(ID_KEY);
    localStorage.removeItem(ALLOWED_KEY);
  } catch {
    // Nothing stored, then.
  }
}

/** Asks the website whether the player must say yes first; if it can't say, they must. Not once it's off or answered. */
function lookUpAskFirst() {
  if (askFirst !== undefined || lookingUp || consented || off() || !context || !telemetryAvailable()) return;
  lookingUp = true;
  void fetch(`${context.host}/api/launcher/consent`)
    .then((response) => (response.ok ? response.json() : null))
    .then((answer: { askFirst?: unknown } | null) => answer?.askFirst === false)
    .catch(() => false)
    .then((free) => {
      askFirst = !free;
      lookingUp = false;
      changed(off());
    });
}

/** Acts on a change of choice and tells the switches. */
function changed(wasOff: boolean) {
  if (off() && !wasOff) forget();
  lookUpAskFirst();
  start();
  snapshot = telemetryChoice();
  listeners.forEach((listener) => listener());
}

/** Starts sending (if it may), once the launcher knows where to and what it is. */
export function startTelemetry(next: TelemetryContext) {
  context = next;
  changed(off());
}

/**
 * The launcher stopped before it could learn this launch's choices (the settings
 * or account never loaded), so what's held would wait forever. It goes if usage
 * data was allowed the last time the launcher ran, and that hasn't been undone:
 * turning it off clears that here, and a first launch has nothing to go by.
 */
export function reportStartupFailure(next: TelemetryContext) {
  context ??= next;
  if (!allowed()) {
    try {
      lastResort = localStorage.getItem(ALLOWED_KEY) === "1";
    } catch {
      return;
    }
  }
  start();
}

/** The apps folder moved: it's taken out of what's sent too. */
export function setTelemetryAppsDir(appsDir: string) {
  if (context) context = { ...context, appsDir };
}

/** This computer's choices, from Settings: on or off, and whether the player has said yes. */
export function setTelemetryLocal(enabled: boolean, consent = false) {
  const was = off();
  localOff = !enabled;
  consented = consent;
  changed(was);
}

/** The signed-in account (null signed out; undefined while it loads), whose choice counts too while signed in. */
export function setTelemetryAccount(next: TelemetryAccount | null | undefined) {
  if (next === undefined) return;
  const was = off();
  accountKnown = true;
  accountOff = next ? next.optOut : null;
  changed(was);
}

/**
 * The player turning it off or back on: this computer remembers it, and while
 * someone is signed in it stands for their account until the saved setting
 * (users.setAnalytics) comes back. If saving it fails, this computer's off
 * still holds. Turning it on is the yes the player may have been asked for.
 */
export function setTelemetryEnabled(enabled: boolean) {
  const was = off();
  localOff = !enabled;
  consented = enabled;
  if (accountOff !== null) accountOff = !enabled;
  changed(was);
}

/**
 * Whether this build sends anything, whether it's on, whether the choice is the
 * account's, and whether the player must be asked first.
 */
export function telemetryChoice() {
  const available = telemetryAvailable();
  return {
    available,
    enabled: available && !off() && cleared(),
    signedIn: accountOff !== null,
    ask: available && !off() && !consented && askFirst === true,
  };
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
  if (!allowed() || !context) {
    if (queue.length < 50) queue.push([event, properties]);
    return;
  }
  send(event, properties, context);
}

/** A screen shown, once per launch (and again for a fresh identifier): coming back to it says nothing new. */
export function trackScreen(properties: Properties) {
  if (!telemetryAvailable() || off()) return;
  const key = JSON.stringify(properties);
  if (screens.has(key)) return;
  screens.add(key);
  track("screen_viewed", properties);
}

/**
 * Errors that aren't the launcher's: the browser's warning that a resize took
 * more than one frame to settle, harmless and in nearly every window.
 */
const NOISE = /^ResizeObserver loop (completed with undelivered notifications|limit exceeded)/;
function isNoise(event: ErrorEvent) {
  const list = event.exception?.values;
  return Array.isArray(list) && list.length > 0 && list.every((e) => typeof e?.value === "string" && NOISE.test(e.value));
}

function report(error: unknown, scope: ErrorScope) {
  if (!telemetryAvailable() || off()) return;
  if (!allowed()) {
    if (heldErrors.length < MAX_HELD_ERRORS) heldErrors.push([error, scope]);
    return;
  }
  void errors?.then((sentry) => sentry?.captureException(error, scope));
}

/** Reports an error caught on the way (an error boundary's) to error tracking. */
export function captureError(error: unknown, properties?: Properties) {
  report(error, { extra: properties });
}

/** An uncaught error: Sentry's own handlers report it once it's running, so only those from before that come here. */
export function captureUncaught(error: unknown) {
  if (!sentryRunning) captureError(error, { handled: "before_start" });
}

/**
 * Failures that aren't the launcher's to fix: the connection, a service that's
 * down or limiting requests, a full disk. Usage data still counts them.
 */
const EXPECTED = new RegExp(
  [
    "check your connection",
    "error sending request",
    "timed? ?out",
    "connection (closed|reset|refused|aborted)",
    "dns error",
    "error decoding response body",
    "failed to fetch",
    "networkerror",
    "HTTP status (client|server) error \\((403|429|5\\d\\d)",
    "no space left",
    "not enough space",
    "os error (28|112)\\b",
  ].join("|"),
  "i",
);

/** The player said no, or it's a failure the launcher already understands and words for them (`Problem`). */
const expected = (error: unknown) => error === "cancelled" || error instanceof Problem || EXPECTED.test(messageOf(error));

/**
 * A failure the player was told about, which no error handler sees: `step` says
 * what was being done. The Rust side's come as plain text, all from the same
 * line here, so they're told apart by what they say.
 */
export function captureFailure(step: string, error: unknown, properties?: Properties) {
  if (expected(error)) return;
  if (error instanceof Error) return report(error, { tags: { failure: step }, extra: properties });
  report(new Error(messageOf(error)), { tags: { failure: step }, extra: properties, fingerprint: ["failure", step, reasonOf(error)] });
}

/**
 * A failure the player reads in their language and usage data gets in English, its placeholders unfilled
 * ("{version} can't be installed again…"), so the same failure counts as one whatever language it showed in.
 */
export class Problem extends Error {
  readonly english: string;
  constructor(said: MessageDescriptor) {
    super(i18n._(said));
    this.english = said.message ?? this.message;
  }
}

const messageOf = (error: unknown) => (error instanceof Problem ? error.english : error instanceof Error ? error.message : String(error));

/** A short, safe reason for a failure: the error's message without paths or names, at most 120 characters. */
export function reasonOf(error: unknown) {
  return scrub(messageOf(error)).slice(0, 120);
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
export function scrub(text: string) {
  let out = text;
  for (const folder of [context?.appsDir, context?.home]) {
    if (folder && folder.length > 2) out = out.replace(new RegExp(escape(folder), "gi"), PATH);
  }
  for (const pattern of PATHS) out = out.replace(pattern, PATH);
  out = out.replace(/(<path>)+/g, PATH);
  const user = context?.user;
  if (user && user.length >= 2)
    out = out.replace(new RegExp(`(?<![\\p{L}\\p{N}_-])${escape(user)}(?![\\p{L}\\p{N}_-])`, "giu"), "<user>");
  return out;
}

function scrubValue(value: unknown, depth = 0): unknown {
  if (typeof value === "string") return scrub(value);
  if (depth > 10 || value === null || typeof value !== "object") return value;
  if (Array.isArray(value)) return value.map((v) => scrubValue(v, depth + 1));
  return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, scrubValue(v, depth + 1)]));
}

/** An error report with every string scrubbed: its messages, stack frames and anything added to it. */
export function scrubEvent<T>(event: T) {
  return scrubValue(event) as T;
}
