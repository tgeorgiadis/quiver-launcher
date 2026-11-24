/**
 * A game the launcher started closed with an error right after starting, or crashed (the Rust side's game_log.rs
 * says so with "app-failed"): what a report of it says. The dialog
 * is game-report-prompt.tsx.
 */
import type { Os } from "@quiverlauncher/api";

/** How a game went wrong, as game_log.rs tells it. */
export type GameFailure = {
  /** The app's id; null for one started without one. */
  id: string | null;
  /** The install's folder name. */
  folder: string;
  /** It closed with an error right after starting, or the system ended it. */
  kind: "early" | "crashed";
  /** What it closed with: its exit code, or on Windows the crash's status. */
  code: number | null;
  /** What kind of crash, in a few plain English words. */
  what: string | null;
  /** The last line of its output that says what went wrong. */
  line: string | null;
  /** When the page heard of it: tells one failure of an app from its next. */
  at?: number;
};

/** What a report says, besides the game's own output. */
export type ReportDetails = {
  /** The app's name, as the catalog or the player has it. */
  name: string;
  /** The installed version. */
  version: string;
  /** Quiver Launcher's version. */
  launcher: string;
  /** The system, as a person would say it: "Windows 11 (x86_64)". */
  system: string;
  /** Started through Wine or Proton. */
  wine: boolean;
  failure: GameFailure;
  /** The end of the game's output, with paths and names already taken out. */
  log: string;
};

/** The most of the game's output a report carries. */
export const LOG_IN_REPORT = 6000;

/** A Windows crash's status the way Windows writes it (0xC0000005); any other exit code as it is. */
export function codeText(code: number, os: Os) {
  return os === "windows" && code < 0 ? `0x${(code >>> 0).toString(16).toUpperCase()}` : String(code);
}

/** What happened, in one English line: what groups the same failure together. */
export function failureLine(failure: GameFailure, os: Os) {
  const code = failure.code === null ? null : codeText(failure.code, os);
  if (failure.kind === "crashed") return `Crashed${failure.what ? `: ${failure.what}` : ""}${code ? ` (${code})` : ""}`;
  return `Closed right after starting${code ? ` (exit code ${code})` : ""}`;
}

/** The last `max` characters of the output, from the start of a line. */
export function endOf(log: string, max = LOG_IN_REPORT) {
  if (max <= 0) return "";
  if (log.length <= max) return log;
  const cut = log.slice(-max);
  const line = cut.indexOf("\n");
  return line >= 0 && line < cut.length - 1 ? cut.slice(line + 1) : cut;
}

/** The report as text, in English as its readers are Quiver staff or an app's developer: for the player to copy. */
export function reportText({ name, version, launcher, system, wine, failure, log }: ReportDetails, os: Os, logMax = LOG_IN_REPORT) {
  const lines = [
    `**${name}** ${version}`.trim(),
    "",
    `- What happened: ${failureLine(failure, os)}`,
    ...(failure.line ? [`- Last message: ${failure.line}`] : []),
    `- System: ${system}${wine ? ", through Wine or Proton" : ""}`,
    `- Started from Quiver Launcher ${launcher}`,
  ];
  const output = endOf(log, logMax).trim();
  if (output) lines.push("", "The end of the game's output:", "", "```", output.replaceAll("```", "'''"), "```");
  return lines.join("\n");
}
