# Quiver Launcher 3.4.4

## Library protection and recovery

- Protect the critical `apps.json` library file with fail-closed reads: corrupt, incomplete, inaccessible, or malformed data is never treated as an empty library and is never overwritten during startup.
- Keep immutable, verified library snapshots in `Backups/apps` before changes, with recovery instructions for restoring a saved library.
- Bound library and update backup storage with automatic retention limits while always keeping the newest complete snapshot.
- Back up `apps.json` and `settings.json` in a dated `Backups/updates` folder before desktop or Android launcher updates. Update handoff stops if either backup cannot be completed.

## Library and App Catalog

- Preserve your scroll position when returning to Quiver after using another window, even with a controller connected or keyboard navigation active. This applies to the Library, catalog sources, and catalog app lists and grids. Keyboard and controller navigation still bring the selected item into view.
- Able to choose a release before installing a repository app using **Versions → Change Version**. Download the version you want without installing the latest release first.
- Add a mouse-wheel scroll speed setting with 1×, 2×, 3× and 5× options for the Library and App Catalog.

## Update checks

- Make **Retry** recheck only apps whose checks failed, were rate limited, or did not finish. Successful results are retained, and each retry narrows to the remaining failures. Quiver's own update check is also skipped when it already succeeded; **Check for Updates** button still runs a full check.
- Show app names and failure reasons in the update-check details, including unfinished checks. Add repository warning indicators to Library entries, with guidance for fixing repository and access problems.
- Improve keyboard and controller navigation through the update-check controls and expandable details.

## Android

- Open the navigation drawer by swiping in from the left edge, with an opening animation and Android gesture handling to help the swipe reach Quiver.

## Linux and installation fixes

- Default new Linux catalog platform filters to both Linux and Windows so apps usable through Wine or Proton are included. Existing filter choices are preserved.
- Avoid scanning Wine/Proton prefixes or following directory links while looking for game executables. Improve detection of native Linux executables and exclude launcher metadata and shared libraries.
- Allow an incomplete installation to be downloaded again even when its saved version matches the requested release. Leftover version metadata no longer causes the download to be skipped when the application files are missing.
