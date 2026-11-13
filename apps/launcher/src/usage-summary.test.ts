import { afterEach, beforeEach, expect, test, vi } from "vitest";
import type { CatalogEntry, Install, LibraryItem, Settings } from "./store";
import { appsDueToday, appsOnComputer, librarySummary, MOST_APPS_SENT, settingsSummary } from "./usage-summary";

const item = (id: string, slug = id): LibraryItem => ({ id, slug, addedAt: 1 });
const install = (more: Partial<Install> = {}) => ({ version: "v1", folder: "x", executables: [], ...more }) as Install;
const none: Record<string, CatalogEntry> = {};
/** What every event carries (telemetry.ts `send`), whose total Google Analytics limits to 25 properties. */
const CARRIED = 10;

beforeEach(() => {
  const stored = new Map<string, string>();
  vi.stubGlobal("localStorage", { getItem: (key: string) => stored.get(key) ?? null, setItem: (key: string, value: string) => void stored.set(key, value) });
});
afterEach(() => vi.unstubAllGlobals());

test("how the launcher is set up says what was chosen, never the files chosen", () => {
  const settings: Settings = {
    theme: "dark",
    language: "pt-BR",
    scale: 1.25,
    music: "C:\\Users\\ada\\song.mp3",
    libraryBackground: "/home/ada/art.png",
    windowsRunner: { kind: "proton", path: "/home/ada/proton" },
  };
  const summary = settingsSummary(settings, { gameMode: false }, true, { width: 2560, height: 1440, scale: 1.25 });
  expect(summary).toMatchObject({
    theme: "dark",
    language: "pt-BR",
    interface_scale: 1.25,
    background_music: true,
    background_image: true,
    windows_runner: "proton",
    github_token: true,
    screen: "2560x1440",
    display_scale: 1.25,
  });
  expect(JSON.stringify(summary)).not.toMatch(/ada|song|art\.png|\/home/);
  expect(Object.keys(summary).length + CARRIED).toBeLessThanOrEqual(25);
});

test("the library's counts", () => {
  const library = [item("a"), item("b"), item("github:o/r", "o/r"), item("local:x", "x")];
  const installs = { a: install({ updates: "auto" }), b: install({ updates: "pinned" }), "github:o/r": install() };
  const summary = librarySummary(library, installs, none, { hidden: ["a"] }, true);
  expect(summary).toEqual({ apps: 4, installed: 3, catalog_apps: 2, custom_apps: 1, local_apps: 1, updates_available: 0, auto_update: 1, pinned: 1, hidden: 1, signed_in: true });
  expect(Object.keys(summary).length + CARRIED).toBeLessThanOrEqual(25);
});

test("only catalog apps installed here are named, with their versions", () => {
  const library = [item("a"), item("not-installed"), item("github:o/secret", "o/secret"), item("local:secret", "secret")];
  const installs = { a: install({ version: "v2.1", updates: "auto" }), "github:o/secret": install(), "local:secret": install() };
  expect(appsOnComputer(library, installs, none)).toEqual([{ slug: "a", version: "v2.1", updates: "auto", update_available: false }]);
});

test("apps left to the player's default count as updating the way it says", () => {
  const library = [item("a"), item("b"), item("c")];
  const installs = { a: install(), b: install({ updates: "ask" }), c: install({ updates: "pinned" }) };
  expect(librarySummary(library, installs, none, { autoUpdates: true }, false)).toMatchObject({ auto_update: 1, pinned: 1 });
  expect(appsOnComputer(library, installs, none, { autoUpdates: true }).map((app) => app.updates)).toEqual(["auto", "notify", "pinned"]);
});

test("too many apps to count leaves out those played longest ago", () => {
  const library = Array.from({ length: MOST_APPS_SENT + 5 }, (_, i) => item(`app${i}`));
  const installs = Object.fromEntries(library.map((a, i) => [a.id, install({ lastPlayed: i })]));
  const apps = appsOnComputer(library, installs, none);
  expect(apps).toHaveLength(MOST_APPS_SENT);
  expect(apps[0].slug).toBe(`app${MOST_APPS_SENT + 4}`);
  expect(apps.map((a) => a.slug)).not.toContain("app0");
});

test("apps are counted once a day", () => {
  expect(appsDueToday(new Date("2026-11-06T10:00:00Z"))).toBe(true);
  expect(appsDueToday(new Date("2026-11-06T22:00:00Z"))).toBe(false);
  expect(appsDueToday(new Date("2026-11-07T00:01:00Z"))).toBe(true);
});

test("a screen that can't be read is left out", () => {
  expect(settingsSummary({}, {}, false, null)).toMatchObject({ screen: null, display_scale: null });
});
