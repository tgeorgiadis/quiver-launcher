# Single-app Add

Single Add updates the existing button to **Added** before entering the session's FIFO catalog mutation queue. The row remains pending until persistence succeeds. The completed Add button then disappears, leaving Details and Remove under All; focus moves from Added to Details if the user has not moved away. Duplicate input is ignored; other rows can be added while a save is outstanding. Navigation does not cancel accepted saves, and the launcher session drains them before disposing services.

The single-app commit rereads the saved library inside the queue, validates instance/folder conflicts, then writes a same-directory temporary file, flushes it, and atomically replaces `apps.json`. A failure leaves the previous file intact. An unreadable or corrupt library fails the operation instead of being treated as empty. Bulk Add and other review mutations use the same queue.

After persistence, the comparison matching pass reuses unchanged definitions and recomputes only changed matches/conflicts. Existing row objects and action controls survive. Membership filters and notification counts then reflect the saved state. Source bookkeeping is coalesced at the end of queued additions and saved without broadcasting a settings change.

Only the new app receives filesystem preparation, a local status check and cached artwork loading. It is inserted into the existing library collection in sort order. Add does not wait for a library reload, unrelated artwork/status refresh, collection reset, or release/icon request. Preparation failures retain the saved app and are reported separately; downloads await required preparation.

Library artwork first reuses the catalog's URL-keyed image cache, retaining compatibility with the older per-app icon cache. Available platform metadata supplies an immediate version label without fabricating a downloadable release or clearing a preferred-version pin. After insertion, session-owned background work resolves the usable release through the existing release-selection policy and fills genuinely missing artwork through the shared image loader. These independent operations continue after catalog navigation and are cancelled and drained on shutdown. They never hold the Add queue open.

## Validation — September 11, 2026

The isolated desktop profile used a 150-app synthetic catalog and 500 existing library apps with local artwork. Input was exercised in the actual Windows launcher with the ordinary review view, strict Windows filtering, and the source badge. `CatalogAddDiagnostics.Completed` is an optional profiler event with timing values only; it writes nothing unless a profiler subscribes.

| Desktop sample | Button state | Atomic commit | Library preparation/insertion | Catalog/UI reconciliation | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| First measured Add | 1.5 ms | 14.3 ms | 61.6 ms | 369.4 ms | 451.8 ms |
| Subsequent Add | 0.1 ms | 11.7 ms | 10.3 ms | 368.9 ms | 391.9 ms |

Button-state timings measure binding notification, not monitor scanout. Visible Added state, retained focus, unchanged scroll position, persisted membership, badge updates, and duplicate Enter activation were also checked in the desktop UI. No physical controller was used.

Rendered headless regression fixtures cover all four combinations of 62/150 catalog apps and 100/500 library apps, with a decodable cached icon for every existing app. Measured feedback was 0.4–0.9 ms and total completion 20–34 ms. These smaller numbers exclude the full desktop shell and must not be presented as desktop timings.

Regression assertions cover pending membership/counts, duplicate and successive input, folder collisions, failed writes and subsequent successful writes, corrupt library reads, navigation/reopening, shutdown draining, preparation failures, stable row/control/app identity and focus, zero collection resets, no unrelated property changes, and no network requests. The non-slow suite passed 1,515 tests. Windows and Android builds were validated. All interactive validation used an isolated profile; existing user settings and caches were preserved.

Re-run the focused coverage with:

```powershell
dotnet test QuiverLauncher.Tests/QuiverLauncher.Tests.csproj --filter 'FullyQualifiedName~CatalogImmediateAddTests|FullyQualifiedName~CatalogAddPerformanceTests|FullyQualifiedName~CatalogIncrementalUpdateTests|FullyQualifiedName~CatalogReviewWorkspaceTests' --logger 'console;verbosity=detailed'
```

## Artwork and release enrichment regression

An isolated Windows profile was validated against the real Nintendo catalog: adding DK64 and opening Library displayed its existing banana artwork and `Latest: 1.0.2`. The legacy icon directory remained empty; the library reused the catalog cache. Tests additionally verify one image download across catalog and library, immediate version hints preserving pins, Add completion while enrichment is blocked, continuation after navigation, cancellation on shutdown, and usable-release selection when the newest release has no assets. The updated non-slow suite passed 1,518 tests, and Windows and Android builds passed. Run `CatalogAddEnrichmentTests` for the focused regression coverage.
