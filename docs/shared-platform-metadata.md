# Shared catalog platform metadata

Community browsing loads `platformMetadataUrl` from the existing registry. The
published file is cached separately in `Cache/published-platforms`, including its
validator. Startup and review opening revalidate at most once per 15 minutes per
HTTP session; explicit catalog refresh bypasses that throttle. The initial review
payload is presented together with available metadata, even when the view has
already initialized its platform controls.

The most recent applicable success wins across published evidence, anonymous API
success and the current credential's API success. Authenticated API results never
cross credential contexts. Published entries include the release selection and
revision; app-specific asset filters are applied in the launcher. The normal
freshness threshold remains 24 hours. Stale successful published coverage remains
usable, and never automatically triggers per-repository requests. Uncovered
community targets are checked automatically after the opening shared-metadata
request completes or fails. Usable credential-appropriate API successes are also
reused. Custom catalogs keep automatic checking. Unknown and repository-free
entries remain visible under platform filters, labeled as checking or unverified.
Catalog payloads are displayed independently of a slow shared-metadata request.

Fallback work is owned by `LauncherSession`. The queue yields between targets,
prioritizes the current catalog, retains failures/cooldowns and allows two automatic
retries. Navigation detaches presentation without cancelling queued checks. Saving
a token explicitly changes the credential context and wakes affected work; editing
a token does not save settings or make requests. Shutdown cancels and drains work.

The review captures platform evidence for stable card membership. New information
updates compatibility labels in place, without resetting rows, selection, focus,
or scroll. A newly incompatible card stays in place until filters change or the
list is reopened. There is no Apply action. Bulk Add uses presented rows with
current verified evidence matching the selected platforms; unknown entries are
excluded even under All platforms. Individual Add remains a library action under
the existing rules. Add, Ignore, Hide and other direct actions still apply
immediately, using the existing notification/acknowledgement workflow.

One compact status row combines compatibility progress and actual hidden-review
counts. Unknown compatibility is not a filter exclusion. Technical errors, retry
timing and token settings are in the Details menu; stale usable evidence alone
does not show a warning. Failed checks retain visible unverified cards and never
establish incompatibility or acknowledge a catalog version. Rate-limited work
keeps the existing cooldown and bounded retry policy.

## Publisher rollout

- Generator: `tools/QuiverLauncher.PlatformIndex`, using Core's
  `CatalogReleaseSelection` shared with installs and updates.
- Published generator revision: `a77ef4f4409fd998c1e61c9e8ccf21f230dd6c75`
  on `codex/catalog-platform-publisher` in the launcher repository.
- Catalog rollout commit: `d09333e`; first scheduled-workflow publication:
  `b5e894e` in `quiver-community-app-catalog`.
- [Validated workflow run](https://github.com/tgeorgiadis/quiver-community-app-catalog/actions/runs/34575912920):
  125 targets validated, zero failures, using the repository-scoped Actions token.
- Runs on catalog edits, manual dispatch and every six hours at minute 17.
  GitHub scheduling and raw-file cache propagation are best-effort. Missing,
  malformed, unsupported or offline files keep previous successful metadata.
- The consumer changes remain in this checkout. No launcher release was published.

## Validation (Windows, September 11, 2026)

The non-slow suite passes 1,504 tests. The 62- and 150-app synthetic fresh-profile
tests assert one initial population and zero repository API requests. They include
the view's early filter initialization, public-cache/token isolation, pinned and
empty releases, conditional requests, invalid documents, explicit refresh,
session navigation, credential recovery, deferred bulk actions, and keyboard/
gamepad activation. Existing download/platform/release regressions also pass.

A fresh Windows build profile under `artifacts/shared-platform-validated` was
opened through the welcome dialog and Nintendo review. It displayed all 62 Windows
entries immediately with both tokens empty, no incomplete/deferred notice and no
repository API cache files. Only the shared platform file supplied platform
coverage. The validation window was closed afterwards; existing user profiles
were not modified.

Build/test logs live under `artifacts/shared-final-non-slow.log`,
`artifacts/shared-platform-validated.log` and `artifacts/shared-final-android.log`.
The final Windows build is in `artifacts/shared-platform-validated`; the signed
debug APK is in `QuiverLauncher.Android/bin/Debug/net10.0-android`.
