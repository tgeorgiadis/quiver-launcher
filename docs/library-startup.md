# Local-first library startup

Startup loads apps.json, local installation state, and cached artwork before making online release requests. This allows the Library to populate even when the network is unavailable or a release request is paused. Startup makes no catalog requests; the App Catalog loads when it is opened.

Local library validation never rewrites an existing `apps.json`. An unreadable or
malformed library stops loading and reports the error instead of becoming an empty
library. Successful reads preserve the original file in `Backups/apps`; see
[library protection and recovery](library-recovery.md).

Release enrichment then operates on the existing library app instances. They do not reread and replace the library collection. Failed online checks retain the saved library and cached evidence. Startup release requests receive the launcher session cancellation token.

The rendered startup regression uses an isolated profile containing 125 apps. All HTTP requests are blocked until the test verifies the library; the requests then fail with HTTP 503. The loaded app instances and saved entries must remain intact.
