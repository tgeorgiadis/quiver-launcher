/**
 * Opens the app once before the test files start, for Vite to bundle the launcher's dependencies into its cache. Left to
 * the test files, each one's server does it at once, in the same folder, and their first loads take too long to wait for.
 */
import { openApp, stop } from "./app.js";

const s = await openApp();
await s.close();
await stop();
process.exit();
