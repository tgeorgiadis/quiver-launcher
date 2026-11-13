import { ApiError } from "../errors";
import type { Architecture, Os, Release } from "../index";
import type { RepositoryProvider, RepositoryRef } from "./types";

/** A release feed: one JSON file a project publishes, for hosts with no release API. */
type FeedRelease = {
  version: string;
  releasedAt?: string | number;
  prerelease?: boolean;
  notes?: string;
  assets: { name: string; url: string; size?: number; sha256?: string; os?: Os; architecture?: Architecture; format?: string }[];
};
type Feed = { releases?: FeedRelease[] };

const feedUrl = (ref: RepositoryRef) => `https://${hostOf(ref)}/${ref.repository}`;

/**
 * A JSON release feed, for a project that publishes its files itself:
 *
 * ```json
 * { "releases": [{ "version": "1.2.3", "releasedAt": "2026-01-01T00:00:00Z",
 *   "assets": [{ "name": "app-linux-x64.zip", "url": "https://…", "sha256": "…" }] }] }
 * ```
 */
export const feedProvider: RepositoryProvider = {
  id: "feed",
  name: "Release feed",
  anyHost: true,
  hosts: [],
  apiBase: () => "",
  async releases(ref, ctx) {
    const response = await ctx.fetch(feedUrl(ref), { headers: { Accept: "application/json" } });
    if (response.status === 404) throw new ApiError(404, "That release feed wasn't found.");
    if (!response.ok) throw new ApiError(response.status, `The release feed answered ${response.status}. Try again later.`);
    const feed = (await response.json().catch(() => null)) as Feed | null;
    if (!feed || !Array.isArray(feed.releases)) throw new ApiError(422, "That file isn't a release feed. It needs a releases list.");
    return feed.releases
      .filter((r) => r && typeof r.version === "string" && Array.isArray(r.assets))
      .map((r): Release => {
        const releasedAt = typeof r.releasedAt === "number" ? r.releasedAt : Date.parse(r.releasedAt ?? "") || 0;
        return {
          id: r.version,
          version: r.version,
          releasedAt,
          prerelease: Boolean(r.prerelease),
          notes: r.notes,
          assets: r.assets
            .filter((a) => a && typeof a.name === "string" && typeof a.url === "string")
            .map((a, i) => ({
              id: String(i),
              url: a.url,
              filename: a.name,
              os: a.os ?? "unknown",
              architecture: a.architecture ?? "unknown",
              format: a.format ?? a.name.split(".").pop() ?? "unknown",
              ...(a.size ? { size: a.size } : {}),
              ...(/^[0-9a-f]{64}$/i.test(a.sha256 ?? "") ? { checksum: `sha256:${a.sha256!.toLowerCase()}` } : {}),
            })),
        };
      })
      .filter((r) => r.assets.length > 0)
      .sort((a, b) => b.releasedAt - a.releasedAt || b.version.localeCompare(a.version, undefined, { numeric: true }));
  },
  webUrl: (ref) => `https://${hostOf(ref)}`,
};

function hostOf(ref: RepositoryRef): string {
  return ref.host ?? "";
}
