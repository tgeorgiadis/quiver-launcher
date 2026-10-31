/**
 * News from Quiver's owner, posted on quiverlauncher.com: the newest five,
 * read when the launcher starts and every few hours. The newest shows in a
 * banner above the list until it's dismissed, and all five are in the top
 * bar's Announcements, where each can be dismissed (and shown again) on this
 * computer.
 */
import { useEffect, useSyncExternalStore } from "react";
import type { Announcement, Client } from "@quiverlauncher/api";

/** How often a launcher left open asks again. Cloudflare answers from one copy, so it's cheap. */
export const RECHECK_MS = 3 * 60 * 60 * 1000;

let items: Announcement[] | null = null;
const listeners = new Set<() => void>();
const set = (next: Announcement[]) => {
  items = next;
  listeners.forEach((l) => l());
};

/** The newest announcements, newest first; null until the first answer. */
export const useAnnouncementList = () =>
  useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    () => items,
  );

/** Reads them when the launcher starts and every few hours; a failed read keeps what it had. */
export function useAnnouncementsCheck(client: Client) {
  useEffect(() => {
    const read = () => void client.announcements().then(set, () => {});
    read();
    const timer = setInterval(read, RECHECK_MS);
    return () => clearInterval(timer);
  }, [client]);
}

/** Split by whether they were dismissed here, each newest first. */
export function sorted(list: Announcement[], dismissed: string[] = []) {
  const gone = new Set(dismissed);
  return { shown: list.filter((a) => !gone.has(a.id)), dismissed: list.filter((a) => gone.has(a.id)) };
}

/** The banner's: the newest one, unless it was dismissed. Older ones stay in the top bar's list, never in the banner. */
export function bannerAnnouncement(list: Announcement[], dismissed: string[] = []): Announcement | null {
  const newest = list[0];
  return newest && !dismissed.includes(newest.id) ? newest : null;
}

/**
 * The dismissed ids still worth keeping: those of announcements the site still
 * lists. Older ones never come back, so the list stays at five at most.
 */
export function keptDismissals(dismissed: string[], list: Announcement[]): string[] {
  return dismissed.filter((id) => list.some((a) => a.id === id));
}
