import { ApiError } from "../errors";
import { inferPlatform } from "../platform";
import type { RateLimit, Readme, Release } from "../index";
import { refusal } from "./refusal";
import type { RepositoryProvider, RepositoryRef } from "./types";

/** Without a token GitHub allows a network 60 requests an hour (a token, 5,000). */
const githubError = (response: Response, token: boolean) => refusal(response, token, "GitHub", "x-ratelimit");

/** A release as GitHub's API gives it. */
type RawRelease = {
  tag_name: string;
  draft: boolean;
  prerelease: boolean;
  published_at: string;
  body?: string;
  assets: { id: number; name: string; browser_download_url: string; size: number; digest?: string }[];
};

const fromGithub = (r: RawRelease): Release => ({
  id: r.tag_name,
  version: r.tag_name,
  releasedAt: Date.parse(r.published_at) || 0,
  prerelease: r.prerelease,
  notes: r.body,
  assets: r.assets.map((a) => ({
    id: String(a.id),
    url: a.browser_download_url,
    filename: a.name,
    ...inferPlatform(a.name),
    format: a.name.split(".").pop() ?? "unknown",
    size: a.size,
    ...(/^sha256:[0-9a-f]{64}$/i.test(a.digest ?? "") ? { checksum: a.digest!.toLowerCase() } : {}),
  })),
});

/**
 * Releases straight from GitHub, for custom apps the catalog doesn't list.
 * Nobody has checked these; GitHub's own SHA-256 digest still guards the
 * download when it has one. With the player's token, GitHub counts requests
 * against it rather than the network.
 */
export function createGithub(base = "https://api.github.com", token?: string, fetchImpl: typeof globalThis.fetch = globalThis.fetch) {
  const auth: Record<string, string> = token?.trim() ? { Authorization: `Bearer ${token.trim()}` } : {};
  const fail = (response: Response) => githubError(response, Boolean(auth.Authorization));
  const host = (() => {
    try {
      return new URL(base).host;
    } catch {
      return "github.com";
    }
  })();
  return {
    /**
     * Every release, newest first, except drafts; up to `count` of them, stable
     * ones first. When those are all pre-releases and there are more, the
     * repository's latest release is added in front, so a run of betas doesn't
     * hide the stable release players should get.
     */
    async releases(repository: string, count = 10): Promise<Release[]> {
      const headers = { Accept: "application/vnd.github+json", ...auth };
      const response = await fetchImpl(`${base}/repos/${repository}/releases?per_page=${count}`, { headers });
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on GitHub.");
      if (!response.ok) throw fail(response);
      const raw: RawRelease[] = await response.json();
      if (raw.length >= count && raw.every((r) => r.prerelease || r.draft)) {
        // A 404 when the repository has no stable release at all.
        const latest = await fetchImpl(`${base}/repos/${repository}/releases/latest`, { headers });
        if (latest.ok) raw.unshift(await latest.json());
      }
      return raw
        .filter((r) => !r.draft && r.tag_name)
        // Stable releases first: the first one is what a player gets.
        .sort((a, b) => Number(a.prerelease) - Number(b.prerelease))
        .map(fromGithub);
    },
    /** The repository's README, or null when it has none. */
    async readme(repository: string): Promise<Readme | null> {
      const response = await fetchImpl(`${base}/repos/${repository}/readme`, { headers: { Accept: "application/vnd.github.raw", ...auth } });
      if (response.status === 404) return null;
      if (!response.ok) throw fail(response);
      // github.com serves relative images from raw.githubusercontent.com; a GitHub Enterprise serves them from itself.
      const rawBase = host === "api.github.com" ? `https://raw.githubusercontent.com/${repository}/HEAD/` : `https://${host}/${repository}/raw/HEAD/`;
      return {
        markdown: await response.text(),
        rawBase,
        htmlBase: `https://${host === "api.github.com" ? "github.com" : host}/${repository}/blob/HEAD/`,
      };
    },
    /** The hourly limit, for the token if there is one; asking doesn't count against it. A 401 ApiError when GitHub refuses the token. */
    async rateLimit(): Promise<RateLimit> {
      const response = await fetchImpl(`${base}/rate_limit`, { headers: { Accept: "application/vnd.github+json", ...auth } });
      if (!response.ok) throw fail(response);
      const { resources } = (await response.json()) as { resources: { core: { limit: number; remaining: number; reset: number } } };
      return { limit: resources.core.limit, remaining: resources.core.remaining, resetAt: resources.core.reset * 1000 };
    },
  };
}

export type Github = ReturnType<typeof createGithub>;

export const githubProvider: RepositoryProvider = {
  id: "github",
  name: "GitHub",
  defaultHost: "github.com",
  hosts: ["github.com"],
  anyHost: true,
  apiBase: (host) => (host === "github.com" ? "https://api.github.com" : `https://${host}/api/v3`),
  releases: (ref, ctx, count) => createGithub(githubProvider.apiBase(hostOf(ref)), ctx.token, ctx.fetch).releases(ref.repository, count),
  readme: (ref, ctx) => createGithub(githubProvider.apiBase(hostOf(ref)), ctx.token, ctx.fetch).readme(ref.repository),
  rateLimit: (ref, ctx) => createGithub(githubProvider.apiBase(hostOf(ref)), ctx.token, ctx.fetch).rateLimit(),
  webUrl: (ref) => `https://${hostOf(ref)}/${ref.repository}`,
  token: {
    auth: "bearer",
    label: "GitHub token",
    createUrl: (ref) => `https://${hostOf(ref)}/settings/tokens/new?description=Quiver+Launcher`,
  },
};

/** A ref's host: its own, or GitHub's. */
function hostOf(ref: RepositoryRef): string {
  return ref.host ?? githubProvider.defaultHost!;
}
