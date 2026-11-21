/**
 * The download cache's own fetches (cache.ts): for Every latest, Hoarder and Selected titles, the release files the
 * cache should have and doesn't yet, downloaded one at a time in the background. A run goes through the apps, asks
 * the catalog for their releases, and fetches what's missing while it fits in the cache's limit; it never pushes out
 * what's kept to make room. It runs a little after the launcher starts, then every few hours, and again when the
 * player changes what's kept; Settings › Cache shows it and can start it now or stop it.
 */
import { useEffect, useRef, useSyncExternalStore } from "react";
import { cacheAsk, requestCache, subscribeCacheAsk } from "./cache-signal";
import type { Client, Entry, Os, Release } from "@quiverlauncher/api";
import { cacheNames, cacheOf, cacheTargets, sourceArchive, fetches, fits, installedTargets, protectedFiles, sameVersion, type Cache, type CacheTarget, type CacheUse } from "./cache";
import { native, type CacheStatus, type InstallRequest } from "./native";
import { catalogOs, useLauncher } from "./store";
import { readmeImages } from "@quiverlauncher/ui/markdown";
import { isOwn } from "./custom";

/** How a run is going, for Settings › Cache. */
export type FetchState = {
  running: boolean;
  /** The app whose releases are being read, or the file being downloaded. */
  current?: { name: string; version?: string; filename?: string; received?: number; total?: number | null };
  /** Apps looked at, of how many (unknown while the catalog is still being listed). */
  apps: number;
  ofApps?: number;
  fetched: number;
  /** Files that didn't download, or weren't the file their checksum names. */
  failed: number;
  /** Files left out as they wouldn't fit in the limit. */
  noRoom: number;
  /** When the last run finished. */
  finishedAt?: number;
  /** The files fetched, being fetched or that failed, newest first, across runs (the last FILES_SHOWN): Downloads lists them. */
  files: CacheFile[];
};

/**
 * A file the cache fetched, or tried to: its app (for its picture and name), version and file as published, so a file
 * an antivirus stopped can be told, and its release reported or taken out of the catalog.
 */
export type CacheFile = {
  /** Its checksum, which tells it from any other. */
  key: string;
  entry: Entry;
  version: string;
  filename: string;
  state: "downloading" | "kept" | "failed";
  /** Why it failed: taken away as it came, as an antivirus does ("blocked"), not the file its checksum names, gone from its host, or anything else. */
  why?: "blocked" | "mismatch" | "missing" | "failed";
  received?: number;
  total?: number | null;
  at: number;
};

/** How many of the cache's files Downloads lists. */
export const FILES_SHOWN = 100;

const whyOf = (e: unknown): CacheFile["why"] => (e === "blocked" || e === "mismatch" || e === "missing" ? e : "failed");

export type FetchDeps = {
  client: Pick<Client, "apps" | "releases" | "app">;
  native: { cacheHas: (dir: string, checksums: string[]) => Promise<boolean[]>; cacheStatus: (dir: string) => Promise<CacheStatus>; cacheFetch: (request: InstallRequest) => Promise<void> };
  /** Selected titles: an app's entry, as the launcher knows it or the catalog has it; none when it's gone. */
  entryOf: (app: { id: string; slug: string }) => Promise<Entry | undefined>;
  here: { os: Os; arch: string };
  /** The checksums of the versions installed now, never rotated out. */
  protect: () => string[];
  /** The catalog as asked for: this computer's apps, unless every system's are kept. */
  catalogOs?: Os;
  /** An app's repository and its host, for its source code (Keep source code); none when the catalog can't say. */
  projectOf?: (slug: string) => Promise<{ provider: string; repository?: string; host?: string } | undefined>;
  /** Keeps an app's README as the site has it now, unless kept as it is already. */
  keepReadme?: (entry: Pick<Entry, "id" | "slug">) => Promise<unknown>;
  /** Installed only: the catalog's apps installed here, their version and the installed file's checksum. */
  installed?: () => { id: string; slug: string; version: string; checksum?: string }[];
};

/** The apps a run goes through: the installed or selected ones (`only` of them, when asked for), or every app in the catalog, a page at a time. */
async function* appsOf(cache: Cache, deps: FetchDeps, count: (n: number) => void, only?: string[]): AsyncGenerator<Entry> {
  if (cache.mode === "selected" || cache.mode === "installed") {
    const apps = (cache.mode === "installed" ? (deps.installed?.() ?? []) : cache.apps).filter((a) => !only || only.includes(a.id));
    count(apps.length);
    for (const app of apps) {
      const entry = await deps.entryOf(app).catch(() => undefined);
      if (entry) yield entry;
    }
    return;
  }
  let cursor: string | null = null;
  do {
    const page = await deps.client.apps({ sort: "name", limit: 100, cursor, ...(deps.catalogOs && !cache.allPlatforms && { os: deps.catalogOs }) });
    yield* page.items;
    cursor = page.isDone ? null : page.nextCursor;
  } while (cursor);
}

/** The release of an installed version, looked for a page at a time; none when the catalog no longer has it. */
async function releaseOf(deps: FetchDeps, slug: string, version: string): Promise<Release | undefined> {
  let cursor: string | null = null;
  do {
    const page = await deps.client.releases(slug, 100, cursor, true);
    const found = page.items.find((r) => sameVersion(r.version, version));
    if (found) return found;
    cursor = page.isDone ? null : page.nextCursor;
  } while (cursor);
  return undefined;
}

/** An app's releases, newest first: every one for Hoarder, else the newest few. */
async function releasesOf(cache: Cache, deps: FetchDeps, slug: string): Promise<Release[]> {
  if (cache.mode !== "hoarder") return (await deps.client.releases(slug, 5)).items;
  const all: Release[] = [];
  let cursor: string | null = null;
  do {
    const page = await deps.client.releases(slug, 100, cursor);
    all.push(...page.items);
    cursor = page.isDone ? null : page.nextCursor;
  } while (cursor);
  return all;
}

/** A target as a request to the cache alone (cache.rs's fetch_to_cache). */
export function fetchRequest(target: CacheTarget, cache: Cache, protect: string[]): InstallRequest {
  const use: CacheUse = {
    dir: cache.dir,
    keep: true,
    app: target.app,
    version: target.version,
    os: target.asset.os,
    slug: target.slug,
    // Installed only keeps an app's installed version alone.
    ...(cache.mode === "installed" && { onlyThis: true }),
    ...(cache.limit !== undefined && { limit: cache.limit }),
    protect,
  };
  return { id: target.app, url: target.asset.url, filename: target.asset.filename, checksum: target.asset.checksum, folder: target.slug, filesToAdd: [], version: target.version, cache: use };
}

/** A release's source code as a request to the cache alone: no checksum, kept by its own, the app's latest alone. */
export function sourceRequest(entry: Pick<Entry, "id" | "slug">, version: string, archive: { url: string; filename: string }, cache: Cache, protect: string[]): InstallRequest {
  const use: CacheUse = { dir: cache.dir, keep: true, app: entry.id, version, slug: entry.slug, source: true, ...(cache.limit !== undefined && { limit: cache.limit }), protect };
  return { id: entry.id, url: archive.url, filename: archive.filename, folder: entry.slug, filesToAdd: [], version, cache: use };
}

/**
 * One run: every file the cache should keep and doesn't, fetched while it fits. `stopped` ends it between files (the
 * file under way is stopped with cacheFetchCancel). Resolves to how it went.
 */
export async function runCacheFetch(cache: Cache, deps: FetchDeps, report: (s: FetchState) => void, stopped: () => boolean, before: CacheFile[] = [], only?: string[]): Promise<FetchState> {
  const state: FetchState = { running: true, apps: 0, fetched: 0, failed: 0, noRoom: 0, files: before.filter((f) => f.state !== "downloading") };
  const show = (more: Partial<FetchState> = {}) => report({ ...state, ...more });
  /** The file at the top of the list as it is now, the oldest dropping off. */
  const note = (file: CacheFile) => (state.files = [file, ...state.files.filter((f) => f.key !== file.key)].slice(0, FILES_SHOWN));
  show();
  const status = await deps.native.cacheStatus(cache.dir);
  let used = status.bytes;
  // The apps' versions whose source code is kept already.
  const sources = new Set(status.entries.filter((f) => f.source).flatMap((f) => f.apps.map((o) => `${o.app}@${o.version}`)));
  const installed = new Map((deps.installed?.() ?? []).map((i) => [i.id, i]));
  for await (const entry of appsOf(cache, deps, (n) => (state.ofApps = n), only)) {
    if (stopped()) break;
    state.apps++;
    state.current = { name: entry.projectName };
    show();
    const install = installed.get(entry.id);
    let targets: CacheTarget[];
    // The release whose source code is kept: the installed one, else the newest.
    let latest: Release | undefined;
    if (cache.mode === "installed") {
      latest = install && (await releaseOf(deps, entry.slug, install.version).catch(() => undefined));
      targets = latest && install ? installedTargets(entry, latest, install, cache, deps.here) : [];
    } else {
      const releases = await releasesOf(cache, deps, entry.slug).catch(() => []);
      latest = releases[0];
      targets = cacheTargets(entry, releases, cache, deps.here);
    }
    // Its README, as the site has it now, for its page while the site can't be reached.
    if (!stopped()) await deps.keepReadme?.(entry).catch(() => {});
    // Keep source code: the release's, as its host archives its tag, unless it's kept already.
    if (cache.sources && latest && !sources.has(`${entry.id}@${latest.version}`) && !stopped()) {
      const project = await deps.projectOf?.(entry.slug).catch(() => undefined);
      const archive = project && sourceArchive(project, latest.version);
      if (archive) {
        const file: CacheFile = { key: `source:${entry.id}@${latest.version}`, entry, version: latest.version, filename: archive.filename, state: "downloading", received: 0, total: null, at: Date.now() };
        state.current = { name: entry.projectName, version: latest.version, filename: archive.filename, received: 0, total: null };
        note(file);
        show();
        try {
          await deps.native.cacheFetch(sourceRequest(entry, latest.version, archive, cache, deps.protect()));
          state.fetched++;
          sources.add(`${entry.id}@${latest.version}`);
          note({ ...file, state: "kept", at: Date.now() });
        } catch (e) {
          if (e === "cancelled") {
            state.files = state.files.filter((f) => f.key !== file.key);
            break;
          }
          state.failed++;
          note({ ...file, state: "failed", why: whyOf(e), at: Date.now() });
        }
      }
    }
    if (!targets.length) continue;
    const held = await deps.native.cacheHas(cache.dir, targets.map((t) => t.asset.checksum)).catch(() => targets.map(() => false));
    for (const [i, target] of targets.entries()) {
      if (stopped()) break;
      if (held[i]) continue;
      if (!fits(target.asset, used, cache.limit)) {
        state.noRoom++;
        continue;
      }
      state.current = { name: entry.projectName, version: target.version, filename: target.asset.filename, received: 0, total: target.asset.size ?? null };
      const file: CacheFile = { key: target.asset.checksum, entry, version: target.version, filename: target.asset.filename, state: "downloading", received: 0, total: target.asset.size ?? null, at: Date.now() };
      note(file);
      show();
      try {
        await deps.native.cacheFetch(fetchRequest(target, cache, deps.protect()));
        state.fetched++;
        used += target.asset.size ?? 0;
        note({ ...file, state: "kept", received: file.total ?? undefined, at: Date.now() });
      } catch (e) {
        if (e === "cancelled") {
          state.files = state.files.filter((f) => f.key !== file.key);
          break;
        }
        state.failed++;
        note({ ...file, state: "failed", why: whyOf(e), at: Date.now() });
      }
    }
  }
  state.running = false;
  state.current = undefined;
  state.finishedAt = Date.now();
  show();
  return state;
}

// The run's state, for every page that shows it.
let current: FetchState = { running: false, apps: 0, fetched: 0, failed: 0, noRoom: 0, files: [] };
const listeners = new Set<() => void>();
const changed = () => listeners.forEach((l) => l());
const subscribe = (l: () => void) => (listeners.add(l), () => void listeners.delete(l));
export const setFetchState = (s: FetchState) => ((current = s), changed());
/** The run's progress, as Settings › Cache shows it. */
export const useFetchState = () => useSyncExternalStore(subscribe, () => current);
/** Empties the list of what the cache downloaded (Downloads' Clear list); a file under way stays. */
export const clearCacheFiles = () => setFetchState({ ...current, files: current.files.filter((f) => f.state === "downloading") });
/** Starts a run now (Check now): Installed only's only way to run. */
export const fetchNow = () => requestCache();
const useAsked = () => useSyncExternalStore(subscribeCacheAsk, cacheAsk);

/** The first run, a little after the launcher starts, out of the way of what it does first; then every few hours. */
const FIRST_AFTER = 60_000;
const EVERY = 6 * 60 * 60_000;

/**
 * Runs the cache's fetches while its mode is one that fetches: on start, every few hours, when what's kept changes,
 * and when asked. `cancel` stops the file under way.
 */
export function useCacheFetcher(cache: Cache, deps: FetchDeps, cancel: () => void) {
  const ask = useAsked();
  // The last ask a run was made for: Installed only runs once for each, never by itself.
  const answered = useRef(ask.count);
  const key = [cache.mode, cache.dir, cache.allPlatforms, cache.limit, cache.apps.map((a) => a.id).join(","), ask.count].join("|");
  useEffect(() => {
    const asked = ask.count !== answered.current;
    answered.current = ask.count;
    const manual = cache.mode === "installed" && asked;
    if (!fetches(cache) && !manual) return;
    let stop = false;
    let timer: ReturnType<typeof setTimeout>;
    // An ask for some apps alone is for the first run; the ones after go through everything.
    let only = asked ? ask.only : undefined;
    const run = async () => {
      await runCacheFetch(cache, deps, (s) => !stop && setFetchState(s), () => stop, current.files, only).catch(() => setFetchState({ ...current, running: false, finishedAt: Date.now() }));
      only = undefined;
      if (!stop && fetches(cache)) timer = setTimeout(run, EVERY);
    };
    // Asked for, or changed by the player: soon. Else after the launcher's start.
    timer = setTimeout(run, asked || current.finishedAt ? 2_000 : FIRST_AFTER);
    return () => {
      stop = true;
      clearTimeout(timer);
      cancel();
      if (current.running) setFetchState({ ...current, running: false, current: undefined, files: current.files.filter((f) => f.state !== "downloading") });
    };
  }, [key]); // eslint-disable-line react-hooks/exhaustive-deps
}

/** The download cache's background work, for the launcher's whole life: its fetches, and their progress as it comes. */
export function useDownloadCache() {
  const { client, config, settings, catalog, installs } = useLauncher();
  const cache = cacheOf(settings.cache, config.cacheDir ?? "");
  // Read when a run gets to them, not when it starts: what's installed and known changes while it goes.
  const now = useRef({ catalog, installs });
  now.current = { catalog, installs };
  useEffect(
    () =>
      void native.onCacheProgress((p) => {
        if (!current.running || !current.current?.filename) return;
        const total = p.total ?? current.current.total;
        setFetchState({
          ...current,
          current: { ...current.current, received: p.received, total },
          files: current.files.map((f) => (f.state === "downloading" ? { ...f, received: p.received, total } : f)),
        });
      }).catch(() => {}),
    [],
  );
  // As soon as the launcher starts, before its first fetch: what a launcher closed mid-download left cleared (an
  // antivirus can close it as it stops a file), then files an older build kept by hash moved to their apps' folders.
  useEffect(() => {
    if (!cache.on) return;
    void native
      .cacheClean(cache.dir)
      .catch(() => 0)
      .then(() => native.cacheStatus(cache.dir, cacheNames(now.current.catalog, cache.apps)))
      .catch(() => {});
  }, [cache.on, cache.dir]); // eslint-disable-line react-hooks/exhaustive-deps
  useCacheFetcher(
    cache,
    {
      client,
      native,
      here: { os: config.os, arch: config.arch },
      catalogOs: catalogOs(config.os),
      entryOf: async (app) => now.current.catalog[app.id] ?? (app.slug ? (await client.app(app.slug)).entry : undefined),
      protect: () => protectedFiles(now.current.installs),
      projectOf: async (slug) => (await client.app(slug)).project,
      keepReadme: async (entry) => {
        const readme = await client.readme(entry.slug);
        if (readme) await native.cacheReadmeSave(cache.dir, entry.id, entry.slug, readme, readmeImages(readme.markdown, readme));
      },
      // The catalog's apps installed here; never the player's own files, nor apps added from a repository.
      installed: () =>
        Object.entries(now.current.installs).flatMap(([id, i]) => {
          const slug = now.current.catalog[id]?.slug;
          return slug && !i.local && !isOwn(id) ? [{ id, slug, version: i.version, ...(i.checksum && { checksum: i.checksum }) }] : [];
        }),
    },
    () => void native.cacheFetchCancel().catch(() => {}),
  );
}
