/**
 * The quiverlauncher.com catalog API (https://api.quiverlauncher.com/api/v1),
 * typed after the site's public DTOs. Unknown fields are ignored.
 */
export type Os = "windows" | "linux" | "macos" | "android" | "ios" | "unknown";
export type Architecture = "x64" | "x86" | "arm64" | "arm" | "universal" | "unknown";
export type ProjectType = "port" | "tool" | "emulator" | "game";

/** How an app is installed: from the catalog entry, or a release's override. */
export type InstallSettings = {
  folderName: string;
  filesToAdd: string[];
  releaseAssetFilter?: string;
  preferredExecutables?: Partial<Record<"windows" | "linux" | "macos", string[]>>;
};

export type LibraryArt = { capsule?: string; header?: string; hero?: string; logo?: string };

export type Verified = {
  version: string;
  releasedAt: number;
  prerelease: boolean;
  /** Every file has a SHA-256 to check downloads against. */
  pinned: boolean;
  rolling?: boolean;
};

/** Which version of a game an app is based on. */
export type BasedOn = {
  /** Console id of the version, such as "ps1" (see facets). */
  console?: string;
  /** The edition, when it matters: "Director's Cut". */
  edition?: string;
};

export type Entry = {
  id: string;
  catalogId?: string;
  slug: string;
  name: string;
  description: string;
  projectName: string;
  games: { id: string; slug: string; title: string }[];
  /** Original consoles of the games it's based on, as console ids (see facets). */
  consoles?: string[];
  /** The version of its game it's based on: the PlayStation version, a Director's Cut. */
  basedOn?: BasedOn;
  libraryArt?: LibraryArt;
  artwork?: string;
  tags: string[];
  launcher: InstallSettings;
  projectType: ProjectType;
  supportedOS: Os[];
  recommended: number;
  reviewCount: number;
  reportIssues: number;
  reportBroken: number;
  addedAt: number;
  /** The newest release upstream, approved or not. */
  lastReleaseAt?: number;
  lastReleaseVersion?: string;
  aiLevel?: "none" | "assisted" | "generated";
  verified?: Verified;
  developer?: { key: string; name: string };
};

/** An original game, which one or more apps play. */
export type Game = {
  id: string;
  slug: string;
  /** The name most players know it by. */
  title: string;
  /** Where the title is the name, such as "US". */
  titleRegion?: string;
  /** Other names it goes by, with their region if any: "King's Field II (Japan)". */
  alternateTitles?: string[];
  /** When it first came out. */
  year?: number;
  description: string;
  artwork?: string;
  libraryArt?: LibraryArt;
  /** Console ids (see facets). */
  originalSystems: string[];
};

/** A game whose title (or another of its names) matches a search, and how many apps play it. */
export type GameMatch = {
  slug: string;
  title: string;
  /** The other name the search matched, when its title didn't. */
  matched?: string;
  art?: string;
  apps: number;
};

/** A game's page: the game and every app that plays it, in no order. */
export type GameDetail = { game: Game; entries: Entry[] };

export type Asset = {
  id: string;
  url: string;
  filename: string;
  os: Os;
  architecture: Architecture;
  format: string;
  size?: number;
  /** `sha256:<hex>` */
  checksum?: string;
};

export type Release = {
  /** The entry's release (entryReleases id), which a review names. */
  id: string;
  version: string;
  releasedAt: number;
  prerelease: boolean;
  notes?: string;
  assets: Asset[];
  installationOverride?: InstallSettings;
};

export type Withdrawn = { version: string; reason: string; at: number };

/** How much AI wrote an app, and how the site knows. */
export type AiUse = {
  level: "none" | "assisted" | "generated";
  source: "developer" | "readme" | "signals" | "admin";
  developerAnswer?: "none" | "assisted" | "generated" | "unknown" | "credited";
  answeredBy?: string;
  answerUrl?: string;
  evidence: { kind: string; detail: string; url?: string }[];
  checkedAt: number;
};

/** A newer release the site is checking before it's offered. */
export type Checking = {
  version: string;
  releasedAt: number;
  prerelease: boolean;
  firstSeenAt: number;
  /** When the wait ends; none while a maintainer has to look at it. */
  checkEndsAt?: number;
  needsReview: boolean;
  /** Why it waits, worded for players. */
  reasons: string[];
  earlyAccess: boolean;
  upstreamUrl?: string;
};

/** VirusTotal's verdict on a release, from its most worrying file. */
export type ReleaseScan = {
  verdict: "clean" | "warning" | "flagged" | "pending" | "missing";
  /** "1 of 70 engines" call it malicious, for a warning or flag. */
  engines?: string;
  /** The report of the file the verdict is from. */
  url?: string;
};

/**
 * A release of an app's repository Quiver hasn't verified for it. An
 * "unverified" one installs only when a player picks it and confirms,
 * against the checksums the site pinned when it first saw it; a "blocked"
 * one (withdrawn, taken down, held, stopped, a file replaced, flagged) only
 * when a player insists after a stronger warning. Never an update.
 */
export type UnverifiedRelease = {
  releaseId: string;
  version: string;
  releasedAt: number;
  prerelease: boolean;
  state: "unverified" | "blocked";
  /** Why it isn't verified, or why it's blocked, worded for players. */
  reasons: string[];
  /** When it's verified by itself if nothing changes. */
  checkEndsAt?: number;
  scan?: ReleaseScan;
  /** With the checksums pinned when Quiver first saw them (none from a site older than blocked installs, when blocked). */
  assets: Asset[];
  installationOverride?: InstallSettings;
  upstreamUrl?: string;
};

/**
 * Any release of an app's repository, as the app page lists them: newest
 * first, each verified, unverified or blocked, with why. Only a verified
 * one is ever an update.
 */
export type HistoryRelease = {
  releaseId: string;
  version: string;
  releasedAt: number;
  notes: string;
  prerelease: boolean;
  state: "verified" | "unverified" | "blocked";
  /** Why it isn't verified, or why it's blocked, worded for players; empty when verified. */
  reasons: string[];
  /** When it's verified by itself if nothing changes. */
  checkEndsAt?: number;
  scan?: ReleaseScan;
  /** With the checksums Quiver pinned. */
  assets: Asset[];
  installationOverride?: InstallSettings;
  upstreamUrl?: string;
};

export type Detail = {
  entry: Entry;
  project: { name: string; description: string; repository?: string; provider: string; website?: string; author?: string; aiUse?: AiUse };
  withdrawn: Withdrawn[];
  checking?: Checking;
};

/** A list a player shared from their library, public at quiverlauncher.com/lists/<slug> (sharedLists.get). */
export type SharedList = {
  slug: string;
  name: string;
  description?: string;
  owner: { name: string; avatar?: string };
  items: ({ kind: "entry"; entry: Entry } | { kind: "custom"; custom: { provider: "github" | "gitlab"; repository: string; name: string }; url: string })[];
  /** Apps on it that left the catalog. */
  unavailable: number;
  createdAt: number;
  updatedAt: number;
};

/** The site's address for a shared list. */
export const listUrl = (slug: string) => `https://quiverlauncher.com/lists/${slug}`;
/** A shared list's slug from its address (or the slug itself); null if it isn't one. */
export function listSlug(input: string): string | null {
  const m = input.trim().match(/^(?:(?:https?:\/\/)?(?:www\.)?quiverlauncher\.com\/lists\/)?([a-z0-9-]{1,100})\/?(?:[?#].*)?$/i);
  return m ? m[1].toLowerCase() : null;
}

/** What a player said about how an app ran (reviews.list on the site). */
export type Feedback = {
  id: string;
  userId: string;
  author: string;
  avatar?: string;
  entryId: string;
  entryReleaseId?: string;
  /** The release they tested, as tagged ("v1.2.0"). */
  version?: string;
  result: "runs" | "issues" | "broken";
  body: string;
  platform?: Os;
  createdAt: number;
  updatedAt: number;
  /** Only on the player's own, while others can't see it. */
  underReview?: boolean;
  moderatorHidden?: boolean;
};

/** An app in the release status feed: enough to match a repository to its catalog entry. */
export type ReleaseStatus = { id: string; slug: string; provider: string; repository?: string };

export type Page<T> = { items: T[]; nextCursor: string | null; isDone: boolean };

export type Sort = "added" | "updated" | "rating" | "name";
export type AppQuery = {
  search?: string;
  os?: Os;
  projectType?: ProjectType;
  /** A console id, or "maker:Nintendo" for every console a maker made. */
  console?: string;
  sort?: Sort;
  ai?: "no-generated" | "no-ai";
  cursor?: string | null;
  limit?: number;
};

export type Console = { id: string; name: string; brand: string };
export type Facets = { total: number; consoles: Console[] };

/** A README in Markdown, and what its relative images and links resolve against. */
export type Readme = { markdown: string; rawBase?: string; htmlBase?: string };

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

export const DEFAULT_API = "https://api.quiverlauncher.com/api/v1";

/** The catalog, however it's reached: REST here, Convex in the launcher. */
export type Client = {
  apps(q?: AppQuery): Promise<Page<Entry>>;
  app(slug: string): Promise<Detail>;
  facets(): Promise<Facets>;
  /** Null when the site has no README for the app. */
  readme(slug: string): Promise<Readme | null>;
  releaseStatus(cursor?: string | null): Promise<Page<ReleaseStatus>>;
  /** Approved releases, newest first: the first is what a player gets. */
  releases(slug: string, limit?: number, cursor?: string | null): Promise<Page<Release>>;
  /** Releases Quiver hasn't verified for the app, newest first. A 404 ApiError from a site that doesn't list them yet. */
  unverifiedReleases(slug: string): Promise<UnverifiedRelease[]>;
  /** Every release of the app's repository, newest first, each with its state. A 404 ApiError from a site that doesn't list them yet. */
  releaseHistory(slug: string, limit?: number, cursor?: string | null): Promise<Page<HistoryRelease>>;
  /** What players said, newest first. */
  reviews(slug: string, cursor?: string | null, limit?: number): Promise<Page<Feedback>>;
  /** Up to four original games whose titles match a search of 2 or more characters. */
  matchingGames(search: string): Promise<GameMatch[]>;
  /** Null when there's no such game. */
  game(slug: string): Promise<GameDetail | null>;
  /** A shared list; null when there's none (or it stopped being shared). */
  sharedList(slug: string): Promise<SharedList | null>;
};

export function createClient(base: string = DEFAULT_API): Client {
  async function get<T>(path: string): Promise<T> {
    const response = await fetch(base + path);
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new ApiError(response.status, body?.error?.message ?? `The catalog answered ${response.status}`);
    }
    return response.json();
  }
  const app = (slug: string) => `/apps/${encodeURIComponent(slug)}`;
  const notFound = <T,>(e: unknown): T | null => {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  };
  return {
    apps(q: AppQuery = {}) {
      const params = new URLSearchParams({ limit: String(q.limit ?? 48) });
      if (q.search?.trim()) params.set("search", q.search.trim());
      if (q.os) params.set("os", q.os);
      if (q.projectType) params.set("projectType", q.projectType);
      if (q.console?.startsWith("maker:")) params.set("maker", q.console.slice(6));
      else if (q.console) params.set("console", q.console);
      if (q.sort) params.set("sort", q.sort);
      if (q.ai) params.set("ai", q.ai);
      if (q.cursor) params.set("cursor", q.cursor);
      return get<Page<Entry>>(`/apps?${params}`);
    },
    app: (slug: string) => get<Detail>(app(slug)),
    facets: () => get<Facets>("/facets"),
    readme: (slug: string) => get<Readme>(`${app(slug)}/readme`).catch(notFound<Readme>),
    releaseStatus: (cursor?: string | null) =>
      get<Page<ReleaseStatus>>(`/release-status?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
    releases: (slug: string, limit = 5, cursor?: string | null) =>
      get<Page<Release>>(`${app(slug)}/releases?limit=${limit}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
    unverifiedReleases: (slug: string) => get<{ items: UnverifiedRelease[] }>(`${app(slug)}/unverified-releases`).then((r) => r.items),
    releaseHistory: (slug: string, limit = 10, cursor?: string | null) =>
      get<Page<HistoryRelease>>(`${app(slug)}/release-history?limit=${limit}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
    reviews: (slug: string, cursor?: string | null, limit = 12) =>
      get<Page<Feedback>>(`${app(slug)}/reviews?limit=${limit}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
    // REST has no game search.
    matchingGames: () => Promise.resolve([]),
    game: (slug: string) => get<GameDetail>(`/games/${encodeURIComponent(slug)}`).catch(notFound<GameDetail>),
    sharedList: (slug: string) => get<SharedList>(`/lists/${encodeURIComponent(slug)}`).catch(notFound<SharedList>),
  };
}

/** Which computer a release file is for, from its name. */
export function inferPlatform(filename: string): { os: Os; architecture: Architecture } {
  const s = filename.toLowerCase();
  const os: Os = /windows|(?<!dar)win(?:32|64)|(?:^|[^a-z])win(?:[^a-z]|$)|\.exe$|\.msi$/.test(s)
    ? "windows"
    : /linux|appimage|flatpak|steamdeck|\.deb$|\.rpm$/.test(s)
      ? "linux"
      : /macos|darwin|osx|(?:^|[^a-z])mac(?:[^a-z]|$)|\.dmg$/.test(s)
        ? "macos"
        : /android|\.apk$/.test(s)
          ? "android"
          : /(?:^|[^a-z])ios(?:[^a-z]|$)|iphone|ipados|\.ipa$/.test(s)
            ? "ios"
            : "unknown";
  // "win64" and "linux64" name the architecture too; "darwin64" isn't Windows.
  const architecture: Architecture = /aarch64|arm64/.test(s)
    ? "arm64"
    : /x86[_-]64|amd64|x64|(?<!dar)win(?:dows)?[ _.-]?64|linux[ _.-]?64|(?:^|[^a-z0-9])64[ _-]?bit/.test(s)
      ? "x64"
      : /i[3-6]86|ia32|x86|(?<!dar)win(?:dows)?[ _.-]?32|linux[ _.-]?32|(?:^|[^a-z0-9])32[ _-]?bit/.test(s)
        ? "x86"
        : /universal/.test(s)
          ? "universal"
          : // "arm" only as its own word or armv7/armhf/armel, so "harmony" doesn't count.
            /(?:^|[^a-z])arm(?:v\d|hf|el|[^a-z]|$)/.test(s)
            ? "arm"
            : "unknown";
  return { os, architecture };
}

/** "owner/repo" from a GitHub address or the bare name; null if it isn't one. */
export function githubRepository(input: string): string | null {
  const m = input.trim().match(/^(?:https:\/\/github\.com\/)?([a-z0-9_][a-z0-9_.-]*)\/([a-z0-9_][a-z0-9_.-]*?)(?:\.git)?\/?$/i);
  return m && m[2] !== ".." ? `${m[1]}/${m[2]}` : null;
}

/**
 * A repository from what a player pastes: a github.com or gitlab.com address
 * (any page of it), or "owner/name" for GitHub. Null if it isn't one.
 */
export function parseRepository(input: string): { provider: "github" | "gitlab"; repository: string } | null {
  const text = input.trim().replace(/^(?:https?:\/\/)?(?:www\.)?/i, "");
  const lab = text.match(/^gitlab\.com\/(.+?)(?:\/-\/.*)?\/?$/i);
  if (lab) {
    const path = lab[1].replace(/\.git$/i, "");
    const parts = path.split("/");
    const ok = parts.length >= 2 && parts.every((p) => /^[a-z0-9_][a-z0-9_.-]*$/i.test(p) && p !== "..");
    return ok ? { provider: "gitlab", repository: path } : null;
  }
  const hub = text.match(/^github\.com\/([^/]+)\/([^/?#]+)/i);
  const repository = githubRepository(hub ? `${hub[1]}/${hub[2]}` : text);
  return repository ? { provider: "github", repository } : null;
}

/**
 * Releases straight from GitHub, for custom apps the catalog doesn't list.
 * Nobody has checked these; GitHub's own SHA-256 digest still guards the
 * download when it has one.
 */
export function createGithub(base = "https://api.github.com") {
  return {
    /** Every release, newest first, except drafts; up to `count` of them. */
    async releases(repository: string, count = 10): Promise<Release[]> {
      const response = await fetch(`${base}/repos/${repository}/releases?per_page=${count}`, {
        headers: { Accept: "application/vnd.github+json" },
      });
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on GitHub.");
      if (!response.ok) throw new ApiError(response.status, `GitHub answered ${response.status}. Try again later.`);
      type Raw = {
        tag_name: string;
        draft: boolean;
        prerelease: boolean;
        published_at: string;
        body?: string;
        assets: { id: number; name: string; browser_download_url: string; size: number; digest?: string }[];
      };
      const raw: Raw[] = await response.json();
      return raw
        .filter((r) => !r.draft && r.tag_name)
        // Stable releases first: the first one is what a player gets.
        .sort((a, b) => Number(a.prerelease) - Number(b.prerelease))
        .map((r) => ({
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
        }));
    },
    /** The repository's README, or null when it has none. */
    async readme(repository: string): Promise<Readme | null> {
      const response = await fetch(`${base}/repos/${repository}/readme`, { headers: { Accept: "application/vnd.github.raw" } });
      if (response.status === 404) return null;
      if (!response.ok) throw new ApiError(response.status, `GitHub answered ${response.status}. Try again later.`);
      return {
        markdown: await response.text(),
        rawBase: `https://raw.githubusercontent.com/${repository}/HEAD/`,
        htmlBase: `https://github.com/${repository}/blob/HEAD/`,
      };
    },
  };
}

export type Github = ReturnType<typeof createGithub>;

/** Releases on GitLab, for catalog apps hosted there. GitLab gives no checksums. */
export function createGitlab(base = "https://gitlab.com/api/v4") {
  return {
    async releases(repository: string, count = 30): Promise<Release[]> {
      const response = await fetch(`${base}/projects/${encodeURIComponent(repository)}/releases?per_page=${count}`);
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on GitLab.");
      if (!response.ok) throw new ApiError(response.status, `GitLab answered ${response.status}. Try again later.`);
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
  };
}
