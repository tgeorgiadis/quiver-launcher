/**
 * The library's sidebar (the Sidebar layout): its apps under headings, as
 * they're sorted. Recently played or added groups them by when: Recent, this
 * year's months, then earlier years.
 */
import { msg, plural, t } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";

const DAY = 24 * 60 * 60 * 1000;
/** As recent as "Recent" goes. */
export const RECENT_DAYS = 14;

/** The sidebar's narrowest and widest, in the page's pixels; whatever it's dragged to leaves the app's page at least `SIDEBAR_PANE_MIN`. */
export const SIDEBAR_WIDTHS = [220, 640] as const;
export const SIDEBAR_PANE_MIN = 560;

/** The sidebar's width dragged to `width`, beside a page `total` wide in all. */
export function sidebarWidth(width: number, total: number): number {
  const [min, max] = SIDEBAR_WIDTHS;
  return Math.round(Math.max(min, Math.min(width, max, total - SIDEBAR_PANE_MIN)));
}

export type SidebarSize = "fit" | "narrow" | "wide" | "wider";
/**
 * Widths to pick in the View menu, for a controller or the keys (the edge is
 * for a mouse): "fit" is the width that fits the window, as a double click on
 * the edge puts back. The page beside it keeps its least all the same.
 */
export const SIDEBAR_SIZES: [SidebarSize, MessageDescriptor, number | undefined][] = [
  ["fit", msg({ message: "Fit the window", comment: "Library sidebar width" }), undefined],
  ["narrow", msg({ message: "Narrow", comment: "Library sidebar width" }), 240],
  ["wide", msg({ message: "Wide", comment: "Library sidebar width" }), 420],
  ["wider", msg({ message: "Extra wide", comment: "Library sidebar width" }), 560],
];

/** The heading an app played (or added) at `time` goes under. */
export function whenHeading(time: number | undefined, now = Date.now(), never = t`Not played yet`): string {
  if (!time) return never;
  if (now - time < RECENT_DAYS * DAY) return t({ message: "Recent", comment: "Library sidebar heading: apps played or added in the last two weeks" });
  const date = new Date(time);
  if (date.getFullYear() === new Date(now).getFullYear()) return date.toLocaleString(i18n.locale, { month: "long" });
  return String(date.getFullYear());
}

/** Already sorted apps under headings by the time `at` gives each, in the order they come. */
export function byWhen(entries: Entry[], at: (entry: Entry) => number | undefined, now = Date.now(), never?: string): [string, Entry[]][] {
  const groups = new Map<string, Entry[]>();
  for (const e of entries) {
    const heading = whenHeading(at(e), now, never);
    groups.set(heading, [...(groups.get(heading) ?? []), e]);
  }
  return [...groups];
}

/** "Today", "Yesterday", "Sep 3", or "Sep 3, 2024" for another year. */
export function dayLabel(time: number, now = Date.now()): string {
  const day = (t: number) => new Date(t).toDateString();
  if (day(time) === day(now)) return t`Today`;
  if (day(time) === day(now - DAY)) return t`Yesterday`;
  const date = new Date(time);
  return date.toLocaleDateString(i18n.locale, { month: "short", day: "numeric", ...(date.getFullYear() === new Date(now).getFullYear() ? {} : { year: "numeric" }) });
}

/** Play time as it reads: "Under a minute", "25 minutes", "1.5 hours", "438.9 hours". */
export function playTimeLabel(seconds: number): string {
  const minutes = Math.floor(seconds / 60);
  if (minutes < 1) return t`Under a minute`;
  if (minutes < 60) return plural(minutes, { one: "# minute", other: "# minutes" });
  const hours = Math.floor((seconds / 3600) * 10) / 10;
  return plural(hours, { one: "# hour", other: "# hours" });
}
