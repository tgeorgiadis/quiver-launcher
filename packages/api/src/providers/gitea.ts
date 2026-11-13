import { ApiError } from "../errors";
import { inferPlatform } from "../platform";
import type { RateLimit, Readme, Release } from "../index";
import { refusal } from "./refusal";
import type { RepositoryProvider, RepositoryRef } from "./types";

/** Gitea, Forgejo and Codeberg share this API; Gogs is close enough for releases. */
type RawRelease = {
  tag_name: string;
  name?: string;
  body?: string;
  draft?: boolean;
  prerelease?: boolean;
  created_at?: string;
  published_at?: string;
  assets?: { id: number; name: string; size?: number; browser_download_url: string }[];
};

const time = (value?: string): number => {
  const at = Date.parse(value ?? "") || 0;
  return at > 0 ? at : 0;
};

const fromGitea = (r: RawRelease): Release => ({
  id: r.tag_name,
  version: r.tag_name,
  releasedAt: time(r.published_at) || time(r.created_at),
  prerelease: Boolean(r.prerelease),
  notes: r.body,
  assets: (r.assets ?? []).map((a) => ({
    id: String(a.id),
    url: a.browser_download_url,
    filename: a.name,
    ...inferPlatform(a.name),
    format: a.name.split(".").pop() ?? "unknown",
    ...(a.size ? { size: a.size } : {}),
  })),
});

const splitRepository = (repository: string): [string, string] => {
  const [owner = "", name = ""] = repository.split("/");
  return [owner, name];
};

const giteaError = (response: Response, token: boolean, host: string) => refusal(response, token, host, "x-ratelimit");

/**
 * Releases from a Gitea or Forgejo instance (codeberg.org among them). Its
 * API follows GitHub's release shape, but it publishes no per-file checksum,
 * so these installs are unchecked like GitLab's.
 */
export const giteaProvider: RepositoryProvider = {
  id: "gitea",
  name: "Gitea / Forgejo",
  hosts: ["codeberg.org", "gitea.com", "git.ryujinx.app", "git.eden-emu.dev", "try.gitea.io"],
  anyHost: true,
  apiBase: (host) => `https://${host}/api/v1`,
  async releases(ref, ctx, count = 30) {
    const host = hostOf(ref);
    const base = giteaProvider.apiBase(host);
    const [owner, name] = splitRepository(ref.repository);
    const headers = { Accept: "application/json", ...authOf(ctx.token) };
    const fail = (response: Response) => giteaError(response, Boolean(ctx.token?.trim()), host);
    const perPage = Math.min(count, 50);
    const raw: RawRelease[] = [];
    for (let page = 1; raw.length < count; page++) {
      const response = await ctx.fetch(
        `${base}/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/releases?limit=${perPage}&page=${page}`,
        { headers },
      );
      if (response.status === 404) throw new ApiError(404, `That repository wasn't found on ${host}.`);
      if (!response.ok) throw fail(response);
      const batch = (await response.json()) as RawRelease[];
      if (!Array.isArray(batch) || batch.length === 0) break;
      raw.push(...batch);
      if (batch.length < perPage) break;
    }
    if (raw.length >= count && raw.every((r) => r.prerelease || r.draft)) {
      // A run of betas shouldn't hide the stable release players should get.
      const latest = await ctx.fetch(`${base}/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/releases/latest`, { headers });
      if (latest.ok) raw.unshift((await latest.json()) as RawRelease);
    }
    return raw
      .filter((r) => !r.draft && r.tag_name)
      // Stable releases first: the first one is what a player gets.
      .sort((a, b) => Number(Boolean(a.prerelease)) - Number(Boolean(b.prerelease)))
      .slice(0, count)
      .map(fromGitea);
  },
  async readme(ref, ctx) {
    const host = hostOf(ref);
    const base = giteaProvider.apiBase(host);
    const [owner, name] = splitRepository(ref.repository);
    const headers = { Accept: "text/plain", ...authOf(ctx.token) };
    const fail = (response: Response) => giteaError(response, Boolean(ctx.token?.trim()), host);
    // Gitea's raw endpoint serves the file as it is; try the names projects use.
    for (const file of ["README.md", "README.rst", "README.txt", "README", "readme.md"]) {
      const response = await ctx.fetch(`${base}/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}/raw/${encodeURIComponent(file)}`, { headers });
      if (response.ok) {
        return {
          markdown: await response.text(),
          rawBase: `https://${host}/${ref.repository}/raw/HEAD/`,
          htmlBase: `https://${host}/${ref.repository}/src/HEAD/`,
        };
      }
      if (response.status !== 404) throw fail(response);
    }
    return null;
  },
  async rateLimit(ref, ctx) {
    const host = hostOf(ref);
    const base = giteaProvider.apiBase(host);
    const [owner, name] = splitRepository(ref.repository);
    const response = await ctx.fetch(`${base}/repos/${encodeURIComponent(owner)}/${encodeURIComponent(name)}`, {
      headers: { Accept: "application/json", ...authOf(ctx.token) },
    });
    if (!response.ok) return null;
    // Codeberg and some Forgejo versions use RateLimit; older Gitea uses X-RateLimit.
    const number = (...names: string[]) => {
      for (const name of names) {
        const value = Number(response.headers.get(name));
        if (Number.isFinite(value) && value > 0) return value;
      }
      return undefined;
    };
    const limit = number("x-ratelimit-limit", "ratelimit-limit");
    const remaining = number("x-ratelimit-remaining", "ratelimit-remaining");
    const reset = number("x-ratelimit-reset", "ratelimit-reset");
    return limit && remaining !== undefined && reset ? { limit, remaining, resetAt: reset * 1000 } : null;
  },
  webUrl: (ref) => `https://${hostOf(ref)}/${ref.repository}`,
  token: {
    auth: "token",
    label: "Gitea token",
    createUrl: (ref) => `https://${hostOf(ref)}/user/settings/applications`,
  },
};

function hostOf(ref: RepositoryRef): string {
  if (!ref.host) throw new Error("A Gitea repository needs its host.");
  return ref.host;
}

function authOf(token?: string): Record<string, string> {
  return token?.trim() ? { Authorization: `token ${token.trim()}` } : {};
}

/** Gitea's own public instance, for tests and the known-host list. */
export const GITEA_HOSTS = giteaProvider.hosts;
