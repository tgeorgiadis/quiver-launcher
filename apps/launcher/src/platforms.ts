/**
 * Which platforms' apps the library shows on this device: this device's own
 * at first (Linux's and Windows', which Linux runs through Wine), or the ones
 * the player picked. Kept in settings.json with the other preferences for this
 * device, not synced: the library is synced, and a phone has no use for the
 * Windows apps added on a computer.
 */
import type { Entry, Os } from "@quiverlauncher/api";

/** The platforms the library can be narrowed to, in the order they're offered. */
export const PLATFORMS = ["windows", "macos", "linux", "android"] as const satisfies Os[];
export type Platform = (typeof PLATFORMS)[number];

const isPlatform = (os: unknown): os is Platform => PLATFORMS.includes(os as Platform);

/** What a device shows until the player picks: its own apps, and on Linux Windows' too. Another device, every platform's. */
export function defaultPlatforms(os: string): Platform[] {
  if (os === "linux") return ["windows", "linux"];
  return isPlatform(os) ? [os] : [...PLATFORMS];
}

/** The saved choice, in the usual order; none saved, or none left of it, is this device's default. */
export function platformsOf(saved: unknown, os: string): Platform[] {
  const picked = PLATFORMS.filter((p) => Array.isArray(saved) && saved.includes(p));
  return picked.length ? picked : defaultPlatforms(os);
}

/** Saved only when it isn't the default, so a device that never picked follows a better default later. */
export function savedPlatforms(picked: Platform[], os: string): Platform[] | undefined {
  const list = PLATFORMS.filter((p) => picked.includes(p));
  const fallback = defaultPlatforms(os);
  return list.length && (list.length !== fallback.length || list.some((p) => !fallback.includes(p))) ? list : undefined;
}

/** Whether an app has a release for one of the platforms; one whose platforms aren't known yet always does. */
export function forPlatforms(entry: Pick<Entry, "supportedOS">, platforms: readonly Platform[]): boolean {
  const known = entry.supportedOS.filter((o) => o !== "unknown");
  return !known.length || entry.supportedOS.includes("unknown") || known.some((o) => platforms.includes(o as Platform));
}
