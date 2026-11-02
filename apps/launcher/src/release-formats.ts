/**
 * Release formats for Browse's catalog filter: which installable files a
 * project's releases ship, from the site's release assets (not tags, not the
 * install on this computer). Flatpak, AppImage, archive and Windows .exe.
 */
import type { Asset, Client, Entry } from "@quiverlauncher/api";
import { installFormat } from "./assets";
import { native } from "./native";

/** Checksums, signatures, symbols and source archives: never an installable app (assets.ts). */
const AUXILIARY =
  /\.(json|sha\d*|md5|sig|asc|minisig|pdb|txt)$|(^|[._-])(checksums?|sha\d*sums?|source|src|debug|symbols)([._-]|$)/i;

/** The formats Browse can filter by, in the order the filter lists them. */
export const RELEASE_FORMATS = ["flatpak", "appimage", "archive", "exe"] as const;
export type ReleaseFormat = (typeof RELEASE_FORMATS)[number];

export const isReleaseFormat = (value: string): value is ReleaseFormat =>
  (RELEASE_FORMATS as readonly string[]).includes(value);

/**
 * The installable release formats among these assets. Every platform's files
 * count (Browse's filter is about what the project ships, not what runs here).
 * Source archives and checksums are left out, as installs leave them out.
 */
export function formatsOf(assets: Asset[]): ReleaseFormat[] {
  const found = new Set<ReleaseFormat>();
  for (const asset of assets) {
    if (AUXILIARY.test(asset.filename)) continue;
    const kind = installFormat(asset.filename);
    if (kind === "flatpak") found.add("flatpak");
    else if (kind === "appimage") found.add("appimage");
    else if (kind === "archive") found.add("archive");
    else if (/\.exe$/i.test(asset.filename)) found.add("exe");
  }
  return RELEASE_FORMATS.filter((f) => found.has(f));
}

/** Whether the app has any of the formats the player picked (OR). None picked matches everything. */
export function matchesFormats(have: readonly ReleaseFormat[] | undefined, want: readonly ReleaseFormat[]): boolean {
  if (!want.length) return true;
  return Boolean(have?.some((f) => want.includes(f)));
}

/** The release version that formats were last read for: the verified one, else the newest upstream the catalog knows. */
export const formatVersionOf = (entry: Pick<Entry, "verified" | "lastReleaseVersion">) =>
  entry.verified?.version ?? entry.lastReleaseVersion ?? "";

type CacheEntry = { formats: ReleaseFormat[]; version: string };
type Cache = Record<string, CacheEntry>;

let memory: Cache = {};
let loaded = false;
let writing: Promise<void> | null = null;

/** Loads the on-disk formats cache once; later calls reuse memory. */
export async function loadFormatCache(): Promise<void> {
  if (loaded) return;
  loaded = true;
  const saved = await native.readState<Cache>("releaseFormats").catch(() => null);
  if (saved && typeof saved === "object") memory = { ...saved, ...memory };
}

function saveFormatCache() {
  const snapshot = memory;
  writing = (writing ?? Promise.resolve())
    .then(() => native.writeState("releaseFormats", snapshot))
    .catch(() => {})
    .then(() => {
      writing = null;
    });
}

/** Formats already known for this app at its current catalog version, if any. */
export function cachedFormats(entry: Entry): ReleaseFormat[] | undefined {
  const hit = memory[entry.id];
  if (!hit) return undefined;
  const version = formatVersionOf(entry);
  return hit.version === version ? hit.formats : undefined;
}

function remember(entry: Entry, formats: ReleaseFormat[]) {
  memory[entry.id] = { formats, version: formatVersionOf(entry) };
}

/**
 * Resolves each app's release formats: the cache when it's for this version,
 * otherwise the site's newest approved release (one call per unknown app, a
 * few at a time). Persists what it learns so Browse doesn't ask again.
 */
export async function resolveFormats(client: Client, entries: Entry[], at = 6): Promise<Map<string, ReleaseFormat[]>> {
  await loadFormatCache();
  const out = new Map<string, ReleaseFormat[]>();
  const missing: Entry[] = [];
  for (const entry of entries) {
    const hit = cachedFormats(entry);
    if (hit) out.set(entry.id, hit);
    else missing.push(entry);
  }
  if (!missing.length) return out;

  let next = 0;
  let dirty = false;
  const worker = async () => {
    while (next < missing.length) {
      const entry = missing[next++];
      let formats: ReleaseFormat[] = [];
      try {
        const page = await client.releases(entry.slug, 1);
        formats = formatsOf(page.items[0]?.assets ?? []);
      } catch {
        formats = [];
      }
      remember(entry, formats);
      out.set(entry.id, formats);
      dirty = true;
    }
  };
  await Promise.all(Array.from({ length: Math.min(at, missing.length) }, worker));
  if (dirty) saveFormatCache();
  return out;
}
