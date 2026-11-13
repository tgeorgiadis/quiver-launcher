import type { RateLimit, Readme, Release } from "../index";
import { bitbucketProvider } from "./bitbucket";
import { feedProvider } from "./feed";
import { giteaProvider } from "./gitea";
import { githubProvider } from "./github";
import { gitlabProvider } from "./gitlab";
import type { ParsedRepository, ProviderTokenHelp, Providers, RepositoryProvider, RepositoryRef } from "./types";

/** Every release host the launcher knows, in the order its add-repository picker lists them. */
export const PROVIDERS: RepositoryProvider[] = [githubProvider, gitlabProvider, giteaProvider, bitbucketProvider, feedProvider];

const byId = new Map(PROVIDERS.map((p) => [p.id, p]));

/** Hosts that belong to a provider without asking. */
const knownHosts = new Map<string, string>();
for (const provider of PROVIDERS) for (const host of provider.hosts) knownHosts.set(host.toLowerCase(), provider.id);

/** A provider's default host, when it has one. */
export const defaultHost = (provider: string): string | undefined => byId.get(provider)?.defaultHost;

/** A ref's host: its own, or its provider's default. */
export function hostOf(ref: RepositoryRef): string {
  const host = ref.host ?? defaultHost(ref.provider);
  if (!host) throw new Error(`No host for a ${ref.provider} repository.`);
  return host;
}

/** The provider a ref names, when the launcher has an adapter for it. */
export const providerFor = (ref: RepositoryRef): RepositoryProvider | undefined => byId.get(ref.provider);

/** A provider by id, for the add-repository picker and tests. */
export const providerById = (id: string): RepositoryProvider | undefined => byId.get(id);

/** The provider a host belongs to, from the known hosts; undefined when it isn't known. */
export const providerForHost = (host: string): RepositoryProvider | undefined => {
  const id = knownHosts.get(host.toLowerCase());
  return id ? byId.get(id) : undefined;
};

/** GitLab's project paths can have subgroups; these are the pages after a project, not part of its name. */
const GITLAB_RESERVED = new Set([
  "-",
  "activity",
  "archive",
  "artifacts",
  "badges",
  "blob",
  "branches",
  "cluster",
  "commits",
  "compare",
  "container_registry",
  "contributors",
  "deployments",
  "designs",
  "discussions",
  "edit",
  "environments",
  "error_tracking",
  "feature_flags",
  "forks",
  "graphs",
  "hooks",
  "incidents",
  "issues",
  "iterations",
  "jobs",
  "labels",
  "merge_requests",
  "milestones",
  "network",
  "packages",
  "pipelines",
  "project_members",
  "raw",
  "registry",
  "releases",
  "remote_mirrors",
  "repository",
  "requirements",
  "resource_groups",
  "runners",
  "schedules",
  "security",
  "services",
  "settings",
  "snippets",
  "starrers",
  "tags",
  "terraform",
  "test_cases",
  "tree",
  "uploads",
  "vulnerabilities",
  "wikis",
]);

/** The path of a GitLab project: its group(s) and name, without the page after it. */
const gitlabPath = (path: string): string => {
  const parts = path.split("/").filter(Boolean);
  const end = parts.findIndex((part) => GITLAB_RESERVED.has(part.toLowerCase()));
  return (end < 0 ? parts : parts.slice(0, end)).join("/");
};

/** The first two parts of a GitHub, Gitea or Bitbucket path: owner and repository. */
const ownerAndRepo = (path: string): string => path.split("/").filter(Boolean).slice(0, 2).join("/");

/** A path without the `/-/` pages GitLab copies into a project address. */
const withoutGitlabPages = (path: string): string => path.replace(/\/-\/.*$/, "");

const isHost = (part: string): boolean => part.includes(".") || part.includes(":") || part.toLowerCase() === "localhost";

const clean = (input: string): string =>
  input
    .trim()
    .replace(/^git\+/i, "")
    .replace(/^(?:https?:\/\/)?(?:www\.)?/i, "")
    .replace(/[?#].*$/, "")
    .replace(/\/+$/, "");

/**
 * A repository from what a player pastes: a GitHub, GitLab, Gitea/Forgejo,
 * Bitbucket or other forge address (any page of it), or "owner/name" for
 * GitHub. An unknown host comes back as "unknown" for detectProvider to name;
 * `forced` picks the adapter when the player chose one themselves.
 */
export function parseRepository(input: string, forced?: string): ParsedRepository | null {
  if (forced === "feed") {
    // A feed is a file, not a repository: keep its path and query.
    const withHost = input
      .trim()
      .replace(/^git\+/i, "")
      .replace(/^(?:https?:\/\/)?(?:www\.)?/i, "");
    const slash = withHost.indexOf("/");
    if (slash <= 0) return null;
    const host = withHost.slice(0, slash).toLowerCase();
    const repository = withHost.slice(slash + 1).replace(/^\/+/, "");
    // A feed is a file on a host, not a bare "owner/repo".
    return host.includes(".") && repository ? { provider: "feed", host, repository } : null;
  }
  const text = clean(input);
  if (!text) return null;
  const slash = text.indexOf("/");
  const first = slash < 0 ? text : text.slice(0, slash);
  // A bare "owner.name/repo" is a GitHub owner, not a host: a host needs a known name, a port,
  // localhost, or the owner/repository path after it.
  const hasHost = slash > 0 && (knownHosts.has(first.toLowerCase()) || (isHost(first) && text.split("/").length >= 3));
  const host = (hasHost ? first : forced ? defaultHost(forced) : githubProvider.defaultHost)?.toLowerCase();
  if (!host) return null;
  const rawPath = hasHost ? text.slice(slash + 1) : text;
  const path = withoutGitlabPages(rawPath).replace(/^\/+|\/+$/g, "").replace(/\.git$/i, "");
  const provider = forced ?? knownHosts.get(host) ?? "unknown";
  if (!path || path.split("/").some((part) => part === "..")) return null;
  if (provider === "gitlab") {
    const repository = gitlabPath(path);
    return repository.includes("/") ? { provider, host, repository } : null;
  }
  if (provider === "github" || provider === "gitea" || provider === "bitbucket") {
    const repository = ownerAndRepo(path);
    return repository.includes("/") ? { provider, host, repository } : null;
  }
  // An unknown host: keep the path whole, as it may be a GitLab subgroup.
  return { provider: "unknown", host, repository: gitlabPath(path) || path };
}

/** "owner/repo" from a GitHub address or the bare name; null if it isn't one. */
export function githubRepository(input: string): string | null {
  const parsed = parseRepository(input);
  return parsed && parsed.provider === "github" && parsed.host === githubProvider.defaultHost ? parsed.repository : null;
}

/** The provider a ref's host uses, asking the host when it isn't one the launcher knows. */
const detected = new Map<string, string | null>();

export async function detectProvider(host: string, fetchImpl: typeof globalThis.fetch = globalThis.fetch): Promise<string | null> {
  const key = host.toLowerCase();
  const known = knownHosts.get(key);
  if (known) return known;
  if (detected.has(key)) return detected.get(key)!;
  const gitea = `https://${key}/api/v1/version`;
  const gitlab = `https://${key}/api/v4/projects?per_page=1`;
  const github = `https://${key}/api/v3/meta`;
  const probe = async (url: string, ok: (json: unknown, response: Response) => boolean) => {
    try {
      const response = await fetchImpl(url, { headers: { Accept: "application/json" } });
      if (response.ok) {
        const json = await response.json().catch(() => null);
        return ok(json, response) ? true : false;
      }
      // A GitLab that wants a token still names itself in its rate-limit header.
      return response.status === 401 && response.headers.get("ratelimit-name")?.includes("api") ? true : false;
    } catch {
      return false;
    }
  };
  const [isGitea, isGitlab] = await Promise.all([
    probe(gitea, (json) => typeof (json as { version?: unknown } | null)?.version === "string"),
    probe(gitlab, (json) => Array.isArray(json)),
  ]);
  if (isGitea) {
    detected.set(key, "gitea");
    return "gitea";
  }
  if (isGitlab) {
    detected.set(key, "gitlab");
    return "gitlab";
  }
  if (await probe(github, (json) => typeof (json as { installed_version?: unknown } | null)?.installed_version === "string")) {
    detected.set(key, "github");
    return "github";
  }
  detected.set(key, null);
  return null;
}

/**
 * The providers, with the player's tokens, that the launcher asks for the
 * apps it added. `tokens` is keyed by host; `fetch` is the browser's fetch,
 * the Rust provider proxy, or a test's.
 */
export function createProviders({ tokens, fetch: given }: { tokens: Record<string, string>; fetch?: typeof globalThis.fetch }): Providers {
  const fetchImpl = given ?? globalThis.fetch;
  const ctxFor = (ref: RepositoryRef) => ({ fetch: fetchImpl, token: tokens[hostOf(ref)] });
  const need = (ref: RepositoryRef) => {
    const provider = byId.get(ref.provider);
    if (!provider) throw new Error(`No provider for ${ref.provider}.`);
    return provider;
  };
  return {
    releases(ref, count) {
      return need(ref).releases(ref, ctxFor(ref), count);
    },
    async readme(ref) {
      const provider = need(ref);
      return provider.readme ? provider.readme(ref, ctxFor(ref)) : null;
    },
    async rateLimit(ref) {
      const provider = need(ref);
      return provider.rateLimit ? provider.rateLimit(ref, ctxFor(ref)) : null;
    },
    webUrl: (ref) => need(ref).webUrl(ref),
    tokenHelp: (ref): ProviderTokenHelp | undefined => need(ref).token,
    provider: (ref) => providerFor(ref),
    detect: (host) => detectProvider(host, fetchImpl),
  };
}

/** Everything a caller needs to turn a parsed ref into a stored one. */
export const toRef = (parsed: ParsedRepository): RepositoryRef => ({
  provider: parsed.provider,
  ...(parsed.host !== defaultHost(parsed.provider) ? { host: parsed.host } : {}),
  repository: parsed.repository,
});

/** The providers a player can pick by hand, for the add-repository flow. */
export const providerChoices = PROVIDERS.filter((p) => p.id !== "feed").map((p) => ({ id: p.id, name: p.name, anyHost: p.anyHost }));
