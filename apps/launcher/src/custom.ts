/**
 * Custom apps: a GitHub repository the catalog doesn't list. They show and
 * install like catalog apps, from a stand-in entry built from the repository
 * and its latest release on GitHub.
 */
import type { Entry, Os, Release } from "@quiver/api";
import type { CatalogEntry } from "./store";

export type CustomApp = { provider: "github" | "gitlab"; repository: string; name: string; assetFilter?: string };

export const customKey = (repository: string) => `github:${repository.toLowerCase()}`;
export const isCustom = (id: string) => id.startsWith("github:");

export function customEntry(id: string, app: CustomApp, latest?: Release): CatalogEntry {
  const oses = [...new Set(latest?.assets.map((a) => a.os) ?? [])] as Os[];
  return {
    id,
    slug: "",
    name: app.name,
    projectName: app.name,
    description: `Added from github.com/${app.repository}. Quiver hasn't checked this app.`,
    games: [],
    tags: [],
    launcher: { folderName: app.repository.split("/")[1], filesToAdd: [], releaseAssetFilter: app.assetFilter },
    projectType: "port",
    supportedOS: oses.length ? oses : ["unknown"],
    recommended: 0,
    reviewCount: 0,
    reportIssues: 0,
    reportBroken: 0,
    addedAt: 0,
    ...(latest
      ? {
          verified: {
            version: latest.version,
            releasedAt: latest.releasedAt,
            prerelease: latest.prerelease,
            pinned: latest.assets.length > 0 && latest.assets.every((a) => a.checksum),
          },
        }
      : {}),
  } satisfies Entry;
}
