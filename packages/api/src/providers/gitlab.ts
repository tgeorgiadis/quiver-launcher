import { ApiError } from "../errors";
import { inferPlatform } from "../platform";
import type { RateLimit, Readme, Release } from "../index";
import { refusal } from "./refusal";
import type { RepositoryProvider, RepositoryRef } from "./types";

/** Without a token GitLab allows a network 500 requests a minute on gitlab.com (a token, 2,000). */
const gitlabError = (response: Response, token: boolean) => refusal(response, token, "GitLab", "ratelimit");

/**
 * What GitLab's answer says about the request limit, as the Rust side reads it.
 * The browser can't see GitLab's rate-limit headers (GitLab doesn't expose
 * them to pages), so the launcher asks the Rust side, which can.
 */
export type GitlabLimit = { status: number; limit?: number | null; remaining?: number | null; reset?: number | null; retryAfter?: number | null };
/** Reads a GitLab URL's status and rate-limit headers, the Rust side's way. */
export type GitlabLimitReader = (url: string, token: string | null) => Promise<GitlabLimit>;

/**
 * Releases on GitLab, for catalog apps hosted there. GitLab gives no checksums.
 * With the player's token, GitLab counts requests against it rather than the
 * network.
 */
export function createGitlab(
  base = "https://gitlab.com/api/v4",
  token?: string,
  readLimit?: GitlabLimitReader,
  fetchImpl: typeof globalThis.fetch = globalThis.fetch,
) {
  const auth: Record<string, string> = token?.trim() ? { "PRIVATE-TOKEN": token.trim() } : {};
  const fail = (response: Response) => gitlabError(response, Boolean(auth["PRIVATE-TOKEN"]));
  // `/user` checks a token; a project list is public, so it still answers without one.
  const limitUrl = `${base}/${auth["PRIVATE-TOKEN"] ? "user" : "projects?per_page=1"}`;
  /** GitLab's answer to `limitUrl` as the Rust side read it, headers and all. */
  const readByRust = async (reader: GitlabLimitReader) => {
    const { status, limit, remaining, reset, retryAfter } = await reader(limitUrl, auth["PRIVATE-TOKEN"] ?? null);
    const headers = new Headers();
    if (limit != null) headers.set("ratelimit-limit", String(limit));
    if (remaining != null) headers.set("ratelimit-remaining", String(remaining));
    if (reset != null) headers.set("ratelimit-reset", String(reset));
    if (retryAfter != null) headers.set("retry-after", String(retryAfter));
    return new Response(null, { status, headers });
  };
  /**
   * When the page can't tell why GitLab refused (it can't see the rate-limit
   * headers, and a refusal may come without CORS headers at all), the Rust side
   * asks again: its answer when GitLab is limiting requests, otherwise null.
   */
  const limited = async (): Promise<Response | null> => {
    if (!readLimit) return null;
    const response = await readByRust(readLimit).catch(() => null);
    return response && (response.status === 403 || response.status === 429) ? response : null;
  };
  const origin = (() => {
    try {
      return new URL(base).origin;
    } catch {
      return "https://gitlab.com";
    }
  })();
  return {
    async releases(repository: string, count = 30): Promise<Release[]> {
      let response: Response;
      try {
        response = await fetchImpl(`${base}/projects/${encodeURIComponent(repository)}/releases?per_page=${count}`, { headers: auth });
      } catch (e) {
        const refused = await limited();
        if (refused) throw fail(refused);
        throw e;
      }
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on GitLab.");
      if (response.status === 403 || response.status === 429) throw fail((await limited()) ?? response);
      if (!response.ok) throw fail(response);
      type Raw = {
        tag_name: string;
        released_at: string;
        upcoming_release?: boolean;
        description?: string;
        assets?: { links?: { id: number; name: string; url: string; direct_asset_url?: string; filepath?: string }[] };
      };
      const raw: Raw[] = await response.json();
      return raw.map((r) => ({
        id: r.tag_name,
        version: r.tag_name,
        releasedAt: Date.parse(r.released_at) || 0,
        prerelease: Boolean(r.upcoming_release),
        notes: r.description,
        assets: (r.assets?.links ?? []).map((a) => {
          // A link's name is its title ("Windows build"); the file's own name is in its path.
          const last = (s?: string) => (s ? decodeURIComponent(s.split(/[?#]/)[0].split("/").pop() ?? "") : "");
          const filename = [last(a.filepath), last(a.direct_asset_url), last(a.url)].find((f) => /\.[a-z0-9]{1,8}$/i.test(f)) ?? a.name;
          return {
            id: String(a.id),
            url: a.direct_asset_url ?? a.url,
            filename,
            ...inferPlatform(`${filename} ${a.name}`),
            format: filename.split(".").pop() ?? "unknown",
          };
        }),
      }));
    },
    /** The repository's README, or null when it has none. */
    async readme(repository: string): Promise<Readme | null> {
      const path = `${base}/projects/${encodeURIComponent(repository)}/repository/files/README.md/raw?ref=HEAD`;
      let response: Response;
      try {
        response = await fetchImpl(path, { headers: auth });
      } catch (e) {
        const refused = await limited();
        if (refused) throw fail(refused);
        throw e;
      }
      if (response.status === 404) return null;
      if (response.status === 403 || response.status === 429) throw fail((await limited()) ?? response);
      if (!response.ok) throw fail(response);
      return {
        markdown: await response.text(),
        rawBase: `${origin}/${repository}/-/raw/HEAD/`,
        htmlBase: `${origin}/${repository}/-/blob/HEAD/`,
      };
    },
    /**
     * How many requests GitLab allows, how many are left, and when it starts
     * again. Null when GitLab doesn't say, as some instances don't. Asking
     * with a token checks it: a 401 ApiError when GitLab refuses it. Unlike
     * GitHub's, asking counts as one request.
     */
    async rateLimit(): Promise<RateLimit | null> {
      // The browser can't read GitLab's rate-limit headers, so the Rust side does when it can.
      const response = readLimit ? await readByRust(readLimit) : await fetchImpl(limitUrl, { headers: auth });
      if (!response.ok) throw fail(response);
      const limit = Number(response.headers.get("ratelimit-limit"));
      const remaining = Number(response.headers.get("ratelimit-remaining"));
      const resetAt = Number(response.headers.get("ratelimit-reset")) * 1000;
      return limit > 0 && remaining >= 0 && resetAt > 0 ? { limit, remaining, resetAt } : null;
    },
  };
}

export type Gitlab = ReturnType<typeof createGitlab>;

export const gitlabProvider: RepositoryProvider = {
  id: "gitlab",
  name: "GitLab",
  defaultHost: "gitlab.com",
  hosts: ["gitlab.com"],
  anyHost: true,
  apiBase: (host) => `https://${host}/api/v4`,
  releases: (ref, ctx, count) => createGitlab(gitlabProvider.apiBase(hostOf(ref)), ctx.token, undefined, ctx.fetch).releases(ref.repository, count),
  readme: (ref, ctx) => createGitlab(gitlabProvider.apiBase(hostOf(ref)), ctx.token, undefined, ctx.fetch).readme(ref.repository),
  rateLimit: (ref, ctx) => createGitlab(gitlabProvider.apiBase(hostOf(ref)), ctx.token, undefined, ctx.fetch).rateLimit(),
  webUrl: (ref) => `https://${hostOf(ref)}/${ref.repository}`,
  token: {
    auth: "private-token",
    label: "GitLab token",
    createUrl: (ref) => `https://${hostOf(ref)}/-/user_settings/personal_access_tokens?name=Quiver+Launcher&scopes=read_api`,
  },
};

function hostOf(ref: RepositoryRef): string {
  return ref.host ?? gitlabProvider.defaultHost!;
}
