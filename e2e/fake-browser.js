#!/usr/bin/env node
// Stands in for the system browser during sign-in: follows the redirects.
// With FAKE_BROWSER_LOSE set to a file that isn't there yet, the first page is
// lost instead (left behind, or never shown), and the file made to say so.
import { existsSync, writeFileSync } from "node:fs";

const lose = process.env.FAKE_BROWSER_LOSE;
if (lose && !existsSync(lose)) writeFileSync(lose, process.argv[2]);
else await fetch(process.argv[2]);
