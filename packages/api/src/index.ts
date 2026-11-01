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
  /** Where its mods go, and the Thunderstore communities and GameBanana games they come from. */
  mods?: ModSettings;
};

/** A site to get mods from: "thunderstore" or "gamebanana" (the site keeps any it was given; the launcher skips ones it has no provider for). */
export type ModSource = { provider: string; sourceUrl: string };
export type ModSettings = {
  /** Inside the app's folder. */
  path: string;
  /** "folderPerMod" puts each mod in a folder of its own; anything else, their files go straight in. */
  layout?: string;
  sources: ModSource[];
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
  /**
   * In place of `checksum`: a file of a verified release that changed upstream
   * since, and isn't verified yet. The `sha256:<hex>` Quiver saw, which its
   * download must match; installed only once the player confirms.
   */
  unverifiedChecksum?: string;
  /** When that file is verified by itself, if nothing changes; none while the site's automatic publishing is off. */
  checkEndsAt?: number;
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
  /** YouTube videos for the app's Media tab, in the order staff set. Missing from a site that has none yet. */
  media?: MediaVideo[];
};

/** What a video is, in the order the site lists them. */
export const MEDIA_KINDS = ["trailer", "gameplay", "review", "other"] as const;
export type MediaKind = (typeof MEDIA_KINDS)[number];
export type MediaVideo = {
  /** The 11-character YouTube video id. */
  youtubeId: string;
  kind: MediaKind;
  title?: string;
  /** Where playback starts, in seconds. */
  start?: number;
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
/** Plain text, or a link with the text it shows. */
export type TextPart = { text: string; href?: string };

/** News from Quiver's owner for everyone using the launcher. An edited one keeps its id. */
export type Announcement = {
  id: string;
  /** As written, with links as [display text](https://example.com). */
  message: string;
  /** The message as text and links, ready to draw; only web addresses are links. */
  parts: TextPart[];
  publishedAt: number;
  editedAt?: number;
};

export type ReleaseStatus = { id: string; slug: string; provider: string; repository?: string };

export type Page<T> = { items: T[]; nextCursor: string | null; isDone: boolean };

export type Sort = "added" | "updated" | "rating" | "name";
export type AppQuery = {
  search?: string;
  os?: Os;
  projectType?: ProjectType;
  /** Apps of any of these kinds, such as a player's saved filters; replaces `projectType`. */
  projectTypes?: ProjectType[];
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

/**
 * A catalog download that failed: the release's version and the file's name as
 * the site listed them, and whether the file was gone (a 404) or wasn't the
 * file Quiver checked.
 */
export type DownloadProblem = { version: string; file: string; problem: "missing" | "mismatch" };

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

export const DEFAULT_API = "https://api.quiverlauncher.com/api/v1";
/**
 * The same API on the Convex deployment's own host. api.quiverlauncher.com is
 * a custom domain behind Cloudflare's cache in front of it; when a player
 * can't reach that, the deployment's host still answers, uncached.
 */
export const FALLBACK_API = "https://famous-wildebeest-660.convex.site/api/v1";

export type ClientOptions = {
  /** For tests. */
  fetch?: typeof globalThis.fetch;
  /** Called once, when the default address can't be reached and FALLBACK_API answered instead. */
  onFallback?: (error: unknown) => void;
  /** How long a call waits for an answer before giving up on it; 20 seconds unless a test says otherwise. */
  timeoutMs?: number;
};

/** The catalog API, the same one Cloudflare caches for every player. */
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
  /** Up to four original games whose titles match a search of 2 or more characters; none from a site without game search. */
  matchingGames(search: string): Promise<GameMatch[]>;
  /** Null when there's no such game. */
  game(slug: string): Promise<GameDetail | null>;
  /** A shared list; null when there's none (or it stopped being shared). */
  sharedList(slug: string): Promise<SharedList | null>;
  /** The newest announcements (at most five), newest first; none from a site without them. */
  announcements(): Promise<Announcement[]>;
  /**
   * Tells the site a download of one of an app's files failed, so it reads
   * that release back from its repository straight away. Once per failed
   * install, never retried. A 404 ApiError when the site doesn't offer that file.
   */
  downloadProblem(app: Pick<Entry, "slug" | "catalogId">, report: DownloadProblem): Promise<void>;
};

export function createClient(base: string = DEFAULT_API, options: ClientOptions = {}): Client {
  const fetch = options.fetch ?? ((input, init) => globalThis.fetch(input, init));
  const wait = options.timeoutMs ?? 20_000;
  /**
   * Gives up on a call the site hasn't answered in `wait`, so a connection that
   * stalls (an IPv6 route that goes nowhere, a VPN swallowing it) ends in an
   * error the player can retry instead of a catalog that never loads.
   */
  async function fetchOnce(url: string, init?: RequestInit): Promise<Response> {
    const stop = new AbortController();
    const timer = setTimeout(() => stop.abort(new DOMException("quiverlauncher.com took too long to answer", "TimeoutError")), wait);
    try {
      return await fetch(url, { ...init, signal: stop.signal });
    } finally {
      clearTimeout(timer);
    }
  }
  let origin = base;
  /**
   * When api.quiverlauncher.com gives no answer at all (no connection, a failed
   * TLS handshake, no answer in time, or the 499 its proxy sometimes sends) or
   * a server error, asks the deployment's own host instead, and keeps asking it
   * from then on, as Quiver Launcher 3 does. Only the default address does
   * this, so a test site never does.
   */
  async function request(path: string, init?: RequestInit): Promise<Response> {
    if (origin !== base) return fetchOnce(origin + path, init);
    let problem: unknown;
    let failed: Response | undefined;
    try {
      const response = await fetchOnce(base + path, init);
      if (response.status < 499) return response;
      problem = new ApiError(response.status, `The catalog answered ${response.status}`);
      // A server error keeps its message for the player, unless the other host answers.
      if (response.status !== 499) failed = response;
    } catch (e) {
      problem = e;
    }
    if (base === DEFAULT_API) {
      const other = await fetchOnce(FALLBACK_API + path, init).catch(() => null);
      if (other && other.status < 500) {
        // Calls already on their way get here too; only the first one tells.
        if (origin !== FALLBACK_API) options.onFallback?.(problem);
        origin = FALLBACK_API;
        return other;
      }
    }
    // The player's real problem is the first one.
    if (failed) return failed;
    throw problem;
  }
  /** The site's answer, or an ApiError with its message when it refused. */
  async function answer(response: Response): Promise<Response> {
    if (response.ok) return response;
    const body = await response.json().catch(() => null);
    throw new ApiError(response.status, body?.error?.message ?? `The catalog answered ${response.status}`);
  }
  async function get<T>(path: string): Promise<T> {
    return (await answer(await request(path))).json();
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
      const types = q.projectTypes ?? (q.projectType ? [q.projectType] : []);
      if (types.length > 1) params.set("projectTypes", types.join(","));
      else if (types[0]) params.set("projectType", types[0]);
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
    async matchingGames(search: string) {
      const text = search.trim().slice(0, 200);
      if (text.length < 2) return [];
      const { items } = await get<{ items: GameMatch[] }>(`/games?search=${encodeURIComponent(text)}`);
      // A site without game search lists every game instead, which aren't matches.
      return items.filter((g) => typeof g.apps === "number");
    },
    game: (slug: string) => get<GameDetail>(`/games/${encodeURIComponent(slug)}`).catch(notFound<GameDetail>),
    sharedList: (slug: string) => get<SharedList>(`/lists/${encodeURIComponent(slug)}`).catch(notFound<SharedList>),
    // One address for every player, so Cloudflare answers them all from one copy.
    announcements: () =>
      get<{ items: Announcement[] }>("/announcements").then(
        (r) => r.items,
        (e) => notFound<Announcement[]>(e) ?? [],
      ),
    async downloadProblem({ slug, catalogId }, report) {
      // By its catalog id, which doesn't change when the app is renamed.
      const path = `${catalogId ? `/catalog-entries/${encodeURIComponent(catalogId)}` : app(slug)}/download-problem`;
      await answer(await request(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(report) }));
    },
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
 * Why GitHub refused, worded for players. Without a token GitHub allows a
 * network 60 requests an hour (a token, 5,000), and its answer says when
 * that starts again.
 */
function githubError(response: Response, token: boolean): ApiError {
  const { status, headers } = response;
  if (status === 401 && token) return new ApiError(status, "GitHub didn't accept your GitHub token. Change or remove it in Settings.");
  if (status === 403 || status === 429) {
    const reset = Number(headers.get("x-ratelimit-reset"));
    if (headers.get("x-ratelimit-remaining") === "0" && reset > 0) {
      const at = new Date(reset * 1000).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
      return new ApiError(
        status,
        token
          ? `Your GitHub token's limit of requests is used up until ${at}. Try again then.`
          : `GitHub's limit of requests from this network is used up until ${at}. Try again then, or add a GitHub token in Settings to raise the limit.`,
      );
    }
    const wait = Number(headers.get("retry-after"));
    if (wait > 0) return new ApiError(status, `GitHub is limiting requests from this network. Try again in ${Math.ceil(wait / 60)} min.`);
  }
  return new ApiError(status, `GitHub answered ${status}. Try again later.`);
}

/** How many requests GitHub allows an hour, how many are left, and when it starts again. */
export type RateLimit = { limit: number; remaining: number; resetAt: number };

/**
 * Releases straight from GitHub, for custom apps the catalog doesn't list.
 * Nobody has checked these; GitHub's own SHA-256 digest still guards the
 * download when it has one. With the player's token, GitHub counts requests
 * against it rather than the network.
 */
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

export function createGithub(base = "https://api.github.com", token?: string) {
  const auth: Record<string, string> = token?.trim() ? { Authorization: `Bearer ${token.trim()}` } : {};
  const fail = (response: Response) => githubError(response, Boolean(auth.Authorization));
  return {
    /**
     * Every release, newest first, except drafts; up to `count` of them, stable
     * ones first. When those are all pre-releases and there are more, the
     * repository's latest release is added in front, so a run of betas doesn't
     * hide the stable release players should get.
     */
    async releases(repository: string, count = 10): Promise<Release[]> {
      const headers = { Accept: "application/vnd.github+json", ...auth };
      const response = await fetch(`${base}/repos/${repository}/releases?per_page=${count}`, { headers });
      if (response.status === 404) throw new ApiError(404, "That repository wasn't found on GitHub.");
      if (!response.ok) throw fail(response);
      const raw: RawRelease[] = await response.json();
      if (raw.length >= count && raw.every((r) => r.prerelease || r.draft)) {
        // A 404 when the repository has no stable release at all.
        const latest = await fetch(`${base}/repos/${repository}/releases/latest`, { headers });
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
      const response = await fetch(`${base}/repos/${repository}/readme`, { headers: { Accept: "application/vnd.github.raw", ...auth } });
      if (response.status === 404) return null;
      if (!response.ok) throw fail(response);
      return {
        markdown: await response.text(),
        rawBase: `https://raw.githubusercontent.com/${repository}/HEAD/`,
        htmlBase: `https://github.com/${repository}/blob/HEAD/`,
      };
    },
    /** The hourly limit, for the token if there is one; asking doesn't count against it. A 401 ApiError when GitHub refuses the token. */
    async rateLimit(): Promise<RateLimit> {
      const response = await fetch(`${base}/rate_limit`, { headers: { Accept: "application/vnd.github+json", ...auth } });
      if (!response.ok) throw fail(response);
      const { resources } = (await response.json()) as { resources: { core: { limit: number; remaining: number; reset: number } } };
      return { limit: resources.core.limit, remaining: resources.core.remaining, resetAt: resources.core.reset * 1000 };
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
