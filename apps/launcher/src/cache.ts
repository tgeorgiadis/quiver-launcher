/**
 * The download cache (cache.rs): release files kept to install again without downloading them, and as a backup of what
 * was published. Off unless the player turns it on in Settings › Cache, where they pick what it keeps:
 *
 * - Installed only: the file of each app's installed version, as it's installed; an app's newer one takes its place.
 * - Selected titles: the apps the player marks on their pages (Manage), their newest release, kept up to date.
 * - Every latest: the newest release of every app in the catalog.
 * - Hoarder: every release of every app in the catalog, to archive them.
 *
 * Whatever it keeps fits in the player's limit, rotating out what was least recently used, but never the file of a
 * version installed now. Only files with a checksum are kept, and a kept file is checked against it whenever it's used.
 */
import { msg } from "@lingui/core/macro";
import type { MessageDescriptor } from "@lingui/core";
import type { Asset, Entry, Os, Release } from "@quiverlauncher/api";
import { bestAssets, releaseFiles } from "./assets";
import type { CachedFile } from "./native";

export type CacheMode = "none" | "installed" | "selected" | "latest" | "hoarder";

/** As settings.json keeps it. */
export type CacheSettings = {
  mode?: CacheMode;
  /** The cache folder; unset, `cache` in the data folder (`Config.cacheDir`). */
  dir?: string;
  /** The most it keeps, in GB; unset, DEFAULT_LIMIT_GB; null, no limit. */
  limitGb?: number | null;
  /** Every latest, Hoarder and Selected titles keep each release's files for every system, not only this computer's. */
  allPlatforms?: boolean;
  /** The source code of the release kept of each app (its host's archive of the tag), its latest alone. */
  sources?: boolean;
  /** Selected titles: the apps, by id, and their catalog slug to look them up by when they aren't in the library. */
  apps?: { id: string; slug: string }[];
};

export const DEFAULT_LIMIT_GB = 50;
/** The limits offered, in GB; null is Unlimited. */
export const LIMITS_GB: (number | null)[] = [5, 10, 25, 50, 100, 250, 500, 1000, null];
const GB = 1024 ** 3;

// Names put in the language when shown (i18n._), not here, where the module loads before any is picked.
export const CACHE_MODES: { mode: CacheMode; name: MessageDescriptor; about: MessageDescriptor; warning?: MessageDescriptor }[] = [
  {
    mode: "none",
    name: msg({ message: "Off", comment: "Download cache mode: nothing is kept" }),
    about: msg`Nothing is kept. Each install downloads its files.`,
  },
  {
    mode: "installed",
    name: msg({ message: "Installed only", comment: "Download cache mode" }),
    about: msg`Keeps the file of each app as you install it. An app's newer version takes the place of the one before.`,
  },
  {
    mode: "selected",
    name: msg({ message: "Selected titles", comment: "Download cache mode" }),
    about: msg`Keeps the newest release of the apps you choose, from Manage on each app's page.`,
  },
  {
    mode: "latest",
    name: msg({ message: "Every latest", comment: "Download cache mode: the newest release of every app in the catalog" }),
    about: msg`Keeps the newest release of every app in the catalog, downloading them in the background.`,
    warning: msg`This downloads every app in the catalog, which can take a lot of storage and bandwidth.`,
  },
  {
    mode: "hoarder",
    name: msg({ message: "Hoarder", comment: "Download cache mode: every release of every app, to archive them" }),
    about: msg`Keeps every release of every app in the catalog, to archive them.`,
    warning: msg`For archiving only. Every version of every app can need terabytes of storage and a very long time to download.`,
  },
];

/** The settings as they apply, defaults filled in: `limit` in bytes, none for no limit. */
export function cacheOf(saved: CacheSettings | undefined, defaultDir: string) {
  const mode = CACHE_MODES.some((m) => m.mode === saved?.mode) ? saved!.mode! : "none";
  const gb = saved?.limitGb === undefined ? DEFAULT_LIMIT_GB : saved.limitGb;
  return {
    mode,
    on: mode !== "none",
    dir: saved?.dir?.trim() || defaultDir,
    limit: gb === null || !(gb > 0) ? undefined : Math.round(gb * GB),
    limitGb: gb,
    allPlatforms: Boolean(saved?.allPlatforms),
    sources: Boolean(saved?.sources),
    apps: saved?.apps ?? [],
  };
}
export type Cache = ReturnType<typeof cacheOf>;

/** What an install asks of the cache (cache.rs's CacheUse): none while it's off. */
export type CacheUse = { dir: string; keep: boolean; app: string; version: string; os?: Os; arch?: string; source?: boolean; slug?: string; onlyThis?: boolean; limit?: number; protect: string[] };

/**
 * For an app's install: always taken from the cache when its file is there; kept there as the mode says (any install
 * but in Selected titles, where only those apps'). Installed only keeps an app's latest installed file alone.
 */
export function cacheUse(cache: Cache, app: { id: string; version: string; os?: Os; arch?: string; slug?: string }, protect: string[]): CacheUse | undefined {
  if (!cache.on) return undefined;
  const keep = cache.mode !== "selected" || isSelected(cache, app.id);
  return {
    dir: cache.dir,
    keep,
    app: app.id,
    version: app.version,
    ...(app.os && { os: app.os }),
    ...(app.arch && { arch: app.arch }),
    // Its folder in the cache: `<slug>/<version>/<file>`.
    ...(app.slug && { slug: app.slug }),
    ...(cache.mode === "installed" && { onlyThis: true }),
    ...(cache.limit !== undefined && { limit: cache.limit }),
    protect,
  };
}

/** Whether Selected titles keeps this app. */
export const isSelected = (cache: Pick<Cache, "apps">, id: string) => cache.apps.some((a) => a.id === id);

/** Selected titles with this app on or off. */
export function withSelected(saved: CacheSettings | undefined, app: { id: string; slug: string }, on: boolean): CacheSettings {
  const others = (saved?.apps ?? []).filter((a) => a.id !== app.id);
  return { ...saved, apps: on ? [...others, { id: app.id, slug: app.slug }] : others };
}

/** Apps' ids to their catalog slugs, which name their folders in the cache: the library's, and the selected titles'. */
export const cacheNames = (catalog: Record<string, { slug: string }>, apps: { id: string; slug: string }[]) =>
  Object.fromEntries([...Object.entries(catalog).map(([id, e]) => [id, e.slug] as const), ...apps.map((a) => [a.id, a.slug] as const)].filter(([, slug]) => slug));

/** The files never rotated out: the versions installed now, by their checksums. */
export const protectedFiles = (installs: Record<string, { checksum?: string }>) =>
  [...new Set(Object.values(installs).flatMap((i) => (i.checksum ? [i.checksum] : [])))];

/** The modes that download files by themselves, rather than only keeping what's installed. */
export const fetches = (cache: Cache) => cache.mode === "latest" || cache.mode === "hoarder" || cache.mode === "selected";

/** A release file for the cache to fetch. */
export type CacheTarget = { app: string; slug: string; name: string; version: string; asset: Asset & { checksum: string } };

/**
 * The files the cache keeps of an app, from its releases newest first: the newest's, or every one's in Hoarder. Of a
 * release, this computer's file, as an install would pick it, or with `allPlatforms` its builds for every system. Only
 * files with a checksum, which the cache is checked against.
 */
export function cacheTargets(entry: Pick<Entry, "id" | "slug" | "projectName" | "launcher">, releases: Release[], cache: Cache, here: { os: Os; arch: string }): CacheTarget[] {
  const of = cache.mode === "hoarder" ? releases : releases.slice(0, 1);
  return of.flatMap((release) => {
    const filter = (release.installationOverride ?? entry.launcher)?.releaseAssetFilter;
    const files = cache.allPlatforms ? releaseFiles(release.assets) : bestAssets(release.assets, here.os, here.arch, filter).slice(0, 1);
    return files.flatMap((asset) =>
      asset.checksum ? [{ app: entry.id, slug: entry.slug, name: entry.projectName, version: release.version, asset: asset as Asset & { checksum: string } }] : [],
    );
  });
}

/**
 * Installed only's files of an installed app, from the release of its version: the file installed, by its checksum, or
 * with `allPlatforms` the release's builds for every system. An install Quiver has no checksum for keeps the file an
 * install would pick here.
 */
export function installedTargets(entry: Pick<Entry, "id" | "slug" | "projectName" | "launcher">, release: Release, installed: { checksum?: string }, cache: Cache, here: { os: Os; arch: string }): CacheTarget[] {
  const filter = (release.installationOverride ?? entry.launcher)?.releaseAssetFilter;
  const files = cache.allPlatforms
    ? releaseFiles(release.assets)
    : installed.checksum
      ? release.assets.filter((a) => a.checksum === installed.checksum)
      : bestAssets(release.assets, here.os, here.arch, filter).slice(0, 1);
  return files.flatMap((asset) =>
    asset.checksum ? [{ app: entry.id, slug: entry.slug, name: entry.projectName, version: release.version, asset: asset as Asset & { checksum: string } }] : [],
  );
}

/** The same version, written either way ("v1.2" and "1.2"). */
export const sameVersion = (a: string, b: string) => a.trim().replace(/^v/i, "") === b.trim().replace(/^v/i, "");

/**
 * The files no longer kept for anything, which Clear orphans takes away when the player asks: in Selected titles, those
 * of no app still selected; in Installed only, those of no app installed now. Never one an app still selected or
 * installed shares, nor a file of a version installed now (`protect`). None in the modes that keep the whole catalog.
 */
export function orphans<F extends { hash: string; apps: { app: string }[] }>(files: readonly F[], cache: Pick<Cache, "mode" | "apps">, installed: readonly string[], protect: readonly string[]): F[] {
  const kept = cache.mode === "selected" ? new Set(cache.apps.map((a) => a.id)) : cache.mode === "installed" ? new Set(installed) : null;
  if (!kept) return [];
  const safe = new Set(protect.map((c) => c.replace(/^sha256:/i, "").toLowerCase()));
  return files.filter((f) => !safe.has(f.hash) && !f.apps.some((o) => kept.has(o.app)));
}

/**
 * An app's releases as the cache has them, newest kept first: what an install goes by when quiverlauncher.com can't be
 * reached, each file checked against its SHA-256 as ever. Its builds alone, never its source code.
 */
export function cachedReleases(files: readonly CachedFile[], app: string): Release[] {
  const versions = new Map<string, { at: number; assets: Asset[] }>();
  for (const f of files) {
    if (f.source) continue;
    for (const o of f.apps.filter((o) => o.app === app)) {
      const v = versions.get(o.version) ?? { at: 0, assets: [] };
      v.at = Math.max(v.at, f.cachedAt);
      v.assets.push({ id: f.hash, url: "", filename: f.filename, os: (o.os ?? "unknown") as Os, architecture: (o.arch ?? "unknown") as Asset["architecture"], format: f.filename.split(".").slice(1).join(".") || "unknown", size: f.size, checksum: `sha256:${f.hash}` });
      versions.set(o.version, v);
    }
  }
  return [...versions.entries()].sort((a, b) => b[1].at - a[1].at).map(([version, v]) => ({ id: `cache:${version}`, version, releasedAt: v.at, prerelease: false, assets: v.assets }));
}

/**
 * A release's source code, as its host archives its tag: the address and the name it's kept by,
 * `<repository>-<tag>-source.zip`. None for a host with no such archive (a release feed) or no repository.
 */
export function sourceArchive(project: { provider: string; repository?: string; host?: string }, tag: string): { url: string; filename: string } | undefined {
  const repo = project.repository?.trim().replace(/^\/+|\/+$/g, "");
  if (!repo || !/^[^/\s]+\/[^\s]+$/.test(repo) || !tag.trim()) return undefined;
  const name = repo.split("/").pop()!;
  const ref = encodeURIComponent(tag.trim());
  const host = project.host?.trim().replace(/^https?:\/\//, "").replace(/\/+$/, "");
  const url =
    project.provider === "github"
      ? `https://${host || "github.com"}/${repo}/archive/refs/tags/${ref}.zip`
      : project.provider === "gitlab"
        ? `https://${host || "gitlab.com"}/${repo}/-/archive/${ref}/${name}-${ref}.zip`
        : project.provider === "gitea" && host
          ? `https://${host}/${repo}/archive/${ref}.zip`
          : project.provider === "bitbucket"
            ? `https://${host || "bitbucket.org"}/${repo}/get/${ref}.zip`
            : undefined;
  return url ? { url, filename: `${name}-${tag.trim().replace(/[^A-Za-z0-9._-]/g, "_")}-source.zip` } : undefined;
}

/** Whether the cache keeps an app's README: an app it keeps, by its mode. */
export function keepsReadme(cache: Pick<Cache, "mode" | "apps" | "on">, app: string, installed: boolean) {
  if (!cache.on) return false;
  if (cache.mode === "latest" || cache.mode === "hoarder") return true;
  return cache.mode === "installed" ? installed : isSelected(cache, app);
}

/** Whether a file fits in the room left, so a fetch never pushes out what's kept to make room for more. */
export const fits = (asset: Pick<Asset, "size">, used: number, limit?: number) => limit === undefined || used + (asset.size ?? 0) <= limit;
