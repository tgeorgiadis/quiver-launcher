/**
 * The end-to-end tests as `node --test` runs them, but stopping once a few have failed. A failing test waits out its
 * timeouts (20 seconds and more), so a change that broke many ran on to the CI job's time limit, which GitHub shows as
 * cancelled, not failed, and the run never got as far as saying what failed. Stopping, it ends red in a minute or two
 * with the first failures to look at; all of them are a run in WSL away (wsl.sh).
 *
 *   node run.js [--concurrency=2] [--max-failures=3] [files…]    no files: every one but live's and phone's
 */
import { readdirSync } from "node:fs";
import { resolve } from "node:path";
import { run } from "node:test";
import { spec } from "node:test/reporters";
import { parseArgs } from "node:util";

const { values, positionals } = parseArgs({
  allowPositionals: true,
  options: { concurrency: { type: "string", default: "1" }, "max-failures": { type: "string", default: "0" } },
});
const files = (positionals.length ? positionals : readdirSync(import.meta.dirname).filter((f) => f.endsWith(".test.js") && !/^(live|phone)\./.test(f))).map(
  (f) => resolve(import.meta.dirname, f),
);
const maxFailures = Number(values["max-failures"]);

const stop = new AbortController();
let failed = 0;
const tests = run({ files, concurrency: Number(values.concurrency), forceExit: true, signal: stop.signal });
tests.on("test:fail", ({ nesting }) => {
  process.exitCode = 1;
  // A file's own tests, not their subtests (each fails its test too), nor the tests stopping cancels.
  if (nesting !== 0 || stop.signal.aborted) return;
  if (maxFailures && ++failed >= maxFailures) {
    console.log(`\nStopping: ${failed} tests failed, so the rest aren't run.\n`);
    // The test files' processes are ended, and each stops the apps it started (session.js).
    stop.abort();
  }
});
tests.compose(new spec()).pipe(process.stdout);
