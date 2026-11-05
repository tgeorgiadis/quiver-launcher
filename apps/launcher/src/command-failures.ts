/**
 * Which of the Rust side's commands failed with what. Its errors are plain text, with no stack to say where they came
 * from, so one nothing caught ("No such file or directory (os error 2)") could be from any of them. An error report says
 * which (`telemetry.ts`). The most recent few are enough: it's reported as soon as nothing has caught it.
 */
const KEPT = 20;
const failed = new Map<string, string>();

export function noteFailure(command: string, error: unknown) {
  if (typeof error !== "string") return;
  failed.delete(error);
  failed.set(error, command);
  if (failed.size > KEPT) failed.delete(failed.keys().next().value as string);
}

export const failedCommand = (error: unknown) => (typeof error === "string" ? failed.get(error) : undefined);
