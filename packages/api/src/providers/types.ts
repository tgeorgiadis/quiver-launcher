/**
 * A repository a release comes from, and the host that serves it. `host` is
 * left out for a provider's own host (github.com, gitlab.com), so library
 * keys and stored apps from earlier versions keep their shape.
 */
import type { RateLimit, Readme, Release } from "../index";

export type RepositoryRef = {
  /** The adapter's id: "github", "gitlab", "gitea", "bitbucket", "feed". */
  provider: string;
  /** The web host, when it isn't the provider's default: "codeberg.org", "git.example.com". */
  host?: string;
  /** "owner/repo" (GitHub, Gitea, Bitbucket), or "group/subgroup/project" (GitLab). */
  repository: string;
};

/** A repository as the player pasted it, before an unknown host is detected. */
export type ParsedRepository = {
  /** "unknown" when the host isn't known yet; detectProvider names it. */
  provider: string;
  host: string;
  repository: string;
};

/** How a provider's token goes in a request. */
export type ProviderAuth = "bearer" | "token" | "private-token" | "basic";

/** What a provider needs for one call: the transport, and the token saved for its host. */
export type ProviderContext = {
  /** The browser's fetch, the Rust provider proxy, or a test's. */
  fetch: typeof globalThis.fetch;
  /** The player's token for this host, if they saved one. */
  token?: string;
};

/** Where the player makes a token, and how it's sent. */
export type ProviderTokenHelp = {
  createUrl(ref: RepositoryRef): string;
  auth: ProviderAuth;
  /** "GitHub token", "Gitea token". */
  label: string;
};

/** One release host's API. */
export type RepositoryProvider = {
  /** "github", "gitlab", "gitea", "bitbucket", "feed". */
  id: string;
  /** "GitHub", "GitLab", "Gitea / Forgejo", "Bitbucket", "Release feed". */
  name: string;
  /** The host a ref without one uses. */
  defaultHost?: string;
  /** Hosts this provider is known by. */
  hosts: string[];
  /** Whether the host can be any domain, as a self-hosted forge. */
  anyHost?: boolean;
  /** The API's address for a host. */
  apiBase(host: string): string;
  /** This provider's releases, newest first, stable ones first. */
  releases(ref: RepositoryRef, ctx: ProviderContext, count?: number): Promise<Release[]>;
  /** The repository's README, or null when it has none. */
  readme?(ref: RepositoryRef, ctx: ProviderContext): Promise<Readme | null>;
  /** The host's request limit, when it says. */
  rateLimit?(ref: RepositoryRef, ctx: ProviderContext): Promise<RateLimit | null>;
  /** The repository's page on the web. */
  webUrl(ref: RepositoryRef): string;
  /** How to make and send a token, for providers that take one. */
  token?: ProviderTokenHelp;
};

/** The provider a ref names, or undefined when the launcher has no adapter for it. */
export type ProviderRegistry = {
  all: RepositoryProvider[];
  byId(id: string): RepositoryProvider | undefined;
  /** The provider a host belongs to, from the known hosts; undefined when it isn't known. */
  forHost(host: string): RepositoryProvider | undefined;
  /** The host a ref uses: its own, or its provider's default. */
  hostOf(ref: RepositoryRef): string;
};

/** The release hosts, with the player's tokens, that the launcher asks for apps it added. */
export type Providers = {
  /** A repository's releases, newest first. */
  releases(ref: RepositoryRef, count?: number): Promise<Release[]>;
  /** A repository's README, or null when it has none or its host has no readme API. */
  readme(ref: RepositoryRef): Promise<Readme | null>;
  /** The host's request limit, when it reports one. */
  rateLimit(ref: RepositoryRef): Promise<RateLimit | null>;
  /** The repository's page on the web. */
  webUrl(ref: RepositoryRef): string;
  /** How to make and send a token for the ref's host, when the provider takes one. */
  tokenHelp(ref: RepositoryRef): ProviderTokenHelp | undefined;
  /** The provider a ref names, when the launcher has an adapter for it. */
  provider(ref: RepositoryRef): RepositoryProvider | undefined;
  /** Names an unknown host's provider ("gitea", "gitlab", "github"), or null when it can't tell. */
  detect(host: string): Promise<string | null>;
};
