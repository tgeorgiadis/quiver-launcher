# Platform-aware catalog reviews

Pending catalog notifications use the device platform. The review page uses its selected catalog platform filters; All platforms includes every actionable entry. Search and tag filters do not acknowledge entries or alter notification counts.

A successful asset snapshot that does not match the platform and the app's asset-name filter excludes that entry from pending counts. A successful empty release also establishes no matching download. Missing metadata remains unverified and pending; a failed check never establishes incompatibility. Previously successful metadata remains usable through transient failures. Preferred releases and credential contexts use the existing catalog platform index, and Linux catalog support still requires native Linux assets.

Exclusions do not change ignored entries or the acknowledged catalog version. Automatic acknowledgement still requires zero actionable entries across all platforms. If a later release supports the device platform, the item becomes pending again even when the catalog version has not changed. Source cards show green “All reviewed” text when all entries are reviewed or only other-platform reviews remain. The status tooltip explains when completion applies to the device platform and other platforms still have apps to review.

The 2026-09-11 regression suite covers incompatible assets, auxiliary-only assets, successful empty releases, unknown metadata, All platforms, new compatible releases, asset filters, preferred releases and private credential isolation. Existing catalog filtering and review-action tests remain enabled.
