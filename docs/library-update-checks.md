# Library update checks

The top update button checks the launcher and installed library apps first. It reads installation state locally and updates the existing app objects, including installed apps hidden by search or display filters. It does not reload the library, resort the grid, refresh catalogs, or wait for downloads to finish.

Progress appears beneath the header with Cancel. A foreground pass has a 60-second deadline. Completed results remain available after cancellation, timeouts, or failures; incomplete checks show Retry and do not display an “up to date” badge. Repeated clicks share the current pass.

## Release requests

- Unpinned GitHub entries request the designated latest release first. The release list is fetched only when required for a pin or fallback (such as prerelease-only repositories or missing assets). Existing release-selection rules remain in use.
- Each pass shares raw endpoint responses for entries with the same repository and credentials. Release selection is applied separately for each entry.
- Requests remain serialized per provider. Interactive checks overtake queued background requests; an active request finishes or reaches its timeout first.
- Metadata requests have a 15-second timeout after acquiring the provider slot. Large download timeouts are unchanged.
- Conditional requests reuse credential-specific endpoint payloads. Scheduled installed-app checks allow six-hour cached evidence; manual checks revalidate immediately, subject to server rate limits.

## Background work

Uninstalled library entries refresh afterward with a 24-hour cache policy. Catalogs, mods, and artwork refresh separately from foreground completion. Automatic installations retain their existing opt-in policy and session-owned installation gate; automatic candidates are excluded from simultaneous manual-update prompts. Shutdown cancels queued metadata and secondary refreshes.

The desktop launcher retains its Velopack implementation. Cancelling the foreground wait suppresses late presentation; Velopack's native check may finish internally because its API does not provide the app-request cancellation mechanism.

## Diagnostics and verification

Trace output records foreground duration, app totals, successful checks, request counts during a pass, and secondary-stage timings. It does not include tokens or request authorization headers.

Controlled tests with 15 ms of latency per request measured:

| Apps | Previous requests / time | Latest-first requests / time |
| --- | --- | --- |
| 20 | 40 / 650 ms | 20 / 339 ms |
| 50 | 100 / 1,770 ms | 50 / 892 ms |
| 100 | 200 / 3,282 ms | 100 / 1,585 ms |

These measurements isolate release lookup behavior. They are not live GitHub performance guarantees and exclude previously blocking catalog refreshes, artwork, mods, and installations.
