/**
 * What the Downloads page, its top-bar button, the strip above the library
 * and the Sidebar layout's foot say: what's downloading, what didn't finish,
 * and which apps have an update waiting.
 */
import { plural, t } from "@lingui/core/macro";
import type { Entry } from "@quiverlauncher/api";
import { i18n } from "@quiverlauncher/i18n";
import { hasUpdate, type Install, type Job, type LibraryItem } from "./store";
import type { Progress } from "./native";

export type Downloads = {
  /** Apps downloading or installing now, in the order they started. */
  active: string[];
  /** Apps whose download or install failed, until the player dismisses it or tries again; not ones that failed to start. */
  failed: string[];
  /** Installed apps with a newer verified release and nothing downloading; hidden apps left out, as the library's Updates does. */
  updates: string[];
  /** How far along the downloads are, 0 to 100, counting those whose size is known; null when none is. */
  percent: number | null;
};

export function downloadsOf(
  library: Pick<LibraryItem, "id">[],
  jobs: Record<string, Job>,
  installs: Record<string, Install>,
  catalog: Record<string, Entry>,
  hidden: string[] = [],
): Downloads {
  const running = Object.values(jobs).filter((j): j is Progress => !("error" in j));
  const active = running.map((j) => j.id);
  const failed = Object.entries(jobs).flatMap(([id, job]) => ("error" in job && job.download ? [id] : []));
  const skip = new Set(hidden);
  const updates = library.filter((i) => !skip.has(i.id) && !jobs[i.id] && hasUpdate(catalog[i.id], installs[i.id])).map((i) => i.id);
  // One that's installing has all its files.
  const sized = running.filter((j) => j.total);
  const total = sized.reduce((sum, j) => sum + j.total!, 0);
  const received = sized.reduce((sum, j) => sum + (j.phase === "installing" ? j.total! : Math.min(j.received, j.total!)), 0);
  return { active, failed, updates, percent: total ? Math.floor((received / total) * 100) : null };
}

/** What the strip and the Sidebar foot say, a sentence each: "Downloading Test Port, 45%.", "3 updates available." */
export function downloadsLine(d: Downloads, jobs: Record<string, Job>, name: (id: string) => string, updates = true): string[] {
  const lines: string[] = [];
  const percent = d.percent;
  const count = d.active.length;
  if (count === 1) {
    const job = jobs[d.active[0]];
    const app = name(d.active[0]);
    if (job && !("error" in job) && job.phase === "installing") lines.push(t`Installing ${app}.`);
    else lines.push(percent === null ? t`Downloading ${app}.` : t`Downloading ${app}, ${percent}%.`);
  } else if (count) {
    lines.push(
      percent === null
        ? t`Downloading ${plural(count, { one: "# app", other: "# apps" })}.`
        : t`Downloading ${plural(count, { one: "# app", other: "# apps" })}, ${percent}%.`,
    );
  }
  const failed = d.failed.length;
  if (failed === 1) {
    const app = name(d.failed[0]);
    lines.push(t`${app} didn't finish downloading.`);
  } else if (failed) lines.push(plural(failed, { one: "# download didn't finish.", other: "# downloads didn't finish." }));
  const waiting = d.updates.length;
  if (updates && waiting) lines.push(plural(waiting, { one: "# update available.", other: "# updates available." }));
  return lines;
}

/** A file's size as it reads: "820 KB", "12.3 MB", "1.4 GB". */
export function sizeLabel(bytes: number): string {
  const tenths = (n: number) => n.toLocaleString(i18n.locale, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
  if (bytes < 1024 * 1024) {
    const size = Math.max(1, Math.round(bytes / 1024));
    return t({ message: `${size} KB`, comment: "A file's size in kilobytes" });
  }
  if (bytes < 1024 ** 3) {
    const size = tenths(bytes / 1024 ** 2);
    return t({ message: `${size} MB`, comment: "A file's size in megabytes" });
  }
  const size = tenths(bytes / 1024 ** 3);
  return t({ message: `${size} GB`, comment: "A file's size in gigabytes" });
}
