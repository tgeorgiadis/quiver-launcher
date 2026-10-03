#!/usr/bin/env node
// Stands in for the system browser during sign-in: follows the redirects.
await fetch(process.argv[2]);
