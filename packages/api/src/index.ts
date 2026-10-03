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

export type Entry = {
  id: string;
  catalogId?: string;
  slug: string;
  name: string;
  description: string;
  projectName: string;
  games: { id: string; slug: string; title: string }[];
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
  lastReleaseVersion?: string;
  verified?: Verified;
};

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

export type Detail = {
  entry: Entry;
  project: { name: string; description: string; repository?: string; provider: string; website?: string; author?: string };
  withdrawn: Withdrawn[];
};

/** An app in the release status feed: enough to match a repository to its catalog entry. */
export type ReleaseStatus = { id: string; slug: string; provider: string; repository?: string };

export type Page<T> = { items: T[]; nextCursor: string | null; isDone: boolean };

export type AppQuery = { search?: string; os?: Os; projectType?: ProjectType; cursor?: string | null; limit?: number };

export class ApiError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

export const DEFAULT_API = "https://api.quiverlauncher.com/api/v1";

export function createClient(base: string = DEFAULT_API) {
  async function get<T>(path: string): Promise<T> {
    const response = await fetch(base + path);
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new ApiError(response.status, body?.error?.message ?? `The catalog answered ${response.status}`);
    }
    return response.json();
  }
  const app = (slug: string) => `/apps/${encodeURIComponent(slug)}`;
  return {
    apps(q: AppQuery = {}) {
      const params = new URLSearchParams({ limit: String(q.limit ?? 48) });
      if (q.search?.trim()) params.set("search", q.search.trim());
      if (q.os) params.set("os", q.os);
      if (q.projectType) params.set("projectType", q.projectType);
      if (q.cursor) params.set("cursor", q.cursor);
      return get<Page<Entry>>(`/apps?${params}`);
    },
    app: (slug: string) => get<Detail>(app(slug)),
    releaseStatus: (cursor?: string | null) =>
      get<Page<ReleaseStatus>>(`/release-status?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`),
    /** Approved releases, newest first: the first is what a player gets. */
    releases: (slug: string) => get<Page<Release>>(`${app(slug)}/releases?limit=5`),
  };
}

export type Client = ReturnType<typeof createClient>;

/** Which computer a release file is for, from its name. */
export function inferPlatform(filename: string): { os: Os; architecture: Architecture } {
  const s = filename.toLowerCase();
  const os: Os = /windows|win32|win64|(?:^|[^a-z])win(?:[^a-z]|$)|\.exe$|\.msi$/.test(s)
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
  const architecture: Architecture = /aarch64|arm64/.test(s)
    ? "arm64"
    : /x86_64|amd64|x64/.test(s)
      ? "x64"
      : /i386|i686|win32|x86/.test(s)
        ? "x86"
        : /universal/.test(s)
          ? "universal"
          : /arm/.test(s)
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
 * Releases straight from GitHub, for custom apps the catalog doesn't list.
 * Nobody has checked these; GitHub's own SHA-256 digest still guards the
 * download when it has one.
 */
export function createGithub(base = "https://api.github.com") {
  return {
    async releases(repository: string): Promise<Release[]> {
      const response = await fetch(`${base}/repos/${repository}/releases?per_page=10`, {
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
  };
}

export type Github = ReturnType<typeof createGithub>;
