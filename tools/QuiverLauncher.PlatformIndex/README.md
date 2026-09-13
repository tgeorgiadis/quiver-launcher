# Community platform metadata publisher

Requires .NET 10. This command reads local catalog files and writes public browsing
metadata. Installation and update resolution never consume the output.

```sh
dotnet run --project tools/QuiverLauncher.PlatformIndex -- ../quiver-community-app-catalog/community-app-catalog ../quiver-community-app-catalog/platform-index.json
dotnet run --project tools/QuiverLauncher.PlatformIndex -- --validate ../quiver-community-app-catalog/platform-index.json
```

Use `GITHUB_TOKEN` (and optionally `GITLAB_TOKEN`) in the process environment.
Never put a token in catalog files or command-line arguments. The scheduled
publisher uses the catalog repository's scoped `github.token`.

`CatalogReleaseSelection` in Core is the shared selector used by this tool and
the launcher. Pins, Latest, stable/prerelease ordering and usable-asset fallback
must change there together. If a selection change makes old evidence unsafe,
bump `PublishedPlatformDocument.CurrentRevision` and the emitted entry revision;
old clients reject unsupported revisions while retaining their previous cache.

Entries are keyed by provider, normalized repository (case-insensitive only for
GitHub) and preferred release. Store asset names before app-specific filtering.
No download URLs, credentials, user preferences or review acknowledgements are
published. Empty successful results are distinct from failures.

Generation keeps the previous entry when an individual request fails. Cooldowns
are enforced by Core's shared request coordinator, so subsequent requests to a
limited provider do not keep hitting the API. Other providers can continue.
All-failed, cancelled, invalid-input or invalid-output runs return a nonzero exit
code without replacing the old file. Successful partial runs report failures in
`GITHUB_STEP_SUMMARY` and retain old successful entries. Writes are validated and
atomically renamed.

The catalog workflow must pin the launcher checkout to a tested full commit SHA.
Publish and validate its first index before distributing a consuming launcher.
Schedules are best-effort: clients retain stale success and refresh the shared
file first, never automatically fan out into covered community repositories.
