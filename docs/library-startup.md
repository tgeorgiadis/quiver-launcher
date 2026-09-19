# Local-first library startup

Startup loads apps.json, local installation state, cached artwork, and cached catalog counts before making online catalog or release requests. This allows the Library and source cards to populate even when the network is unavailable or a release request is paused.

Local library validation never rewrites an existing `apps.json`. An unreadable or
malformed library stops loading and reports the error instead of becoming an empty
library. Successful reads preserve the original file in `Backups/apps`; see
[library protection and recovery](library-recovery.md).

Online catalog refresh and release enrichment then operate on the existing library app instances. They do not reread and replace the library collection. Failed online checks retain the saved library and cached evidence. Startup release requests receive the launcher session cancellation token.

The rendered startup regression uses an isolated profile containing 125 apps and a cached source. All HTTP requests are blocked until the test verifies the library and source counts; the requests then fail with HTTP 503. The loaded app instances and saved entries must remain intact.
