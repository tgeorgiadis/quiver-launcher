/**
 * Settings › Cache (cache.ts): whether the download cache keeps anything and what, how much room it has, where it is,
 * what's in it now, and, for the modes that download by themselves, how that's going.
 */
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { Archive, Ban, Download, FolderOpen, HardDrive, Layers, ListChecks, TriangleAlert, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import { Field } from "@quiverlauncher/ui/controls";
import { CACHE_MODES, LIMITS_GB, cacheNames, cacheOf, orphans, fetches, isSelected, protectedFiles, withSelected, type CacheMode, type CacheSettings } from "./cache";
import { isOwn } from "./custom";
import { fetchNow, useFetchState } from "./cache-fetch";
import { sizeLabel } from "./downloads";
import { native, type CachedFile, type CacheStatus } from "./native";
import type { Entry } from "@quiverlauncher/api";
import { Artwork, cardSources, coverOf } from "@quiverlauncher/ui";
import { useLauncher } from "./store";
import { pageArea } from "./spatial";
import { boxZoom } from "./zoom";

const ICONS: Record<CacheMode, ReactNode> = {
  none: <Ban size={22} aria-hidden="true" />,
  installed: <HardDrive size={22} aria-hidden="true" />,
  selected: <ListChecks size={22} aria-hidden="true" />,
  latest: <Layers size={22} aria-hidden="true" />,
  hoarder: <Archive size={22} aria-hidden="true" />,
};

const GB = 1024 ** 3;

/** A limit as it reads: "50 GB", "1 TB". */
function limitName(gb: number) {
  const tb = gb / 1000;
  return gb >= 1000 ? t({ message: `${tb} TB`, comment: "A storage size in terabytes" }) : t({ message: `${gb} GB`, comment: "A storage size in gigabytes" });
}

/** `onOpen`: an app's page, from its row among the selected titles. */
export function CacheSettingsSection({ onOpen }: { onOpen: (entry: Entry) => void }) {
  const { settings, setSettings, config, installs, catalog } = useLauncher();
  const cache = cacheOf(settings.cache, config.cacheDir ?? "");
  const [status, setStatus] = useState<CacheStatus | null>(null);
  const [said, setSaid] = useState<{ text: string; error?: true } | null>(null);
  const [clearing, setClearing] = useState(false);
  const [orphaning, setOrphaning] = useState(false);
  const fetch = useFetchState();
  const set = (change: (c: CacheSettings) => CacheSettings) => setSettings((s) => ({ ...s, cache: change(s.cache ?? {}) }));

  const load = useCallback(() => {
    if (!cache.dir) return;
    native.cacheStatus(cache.dir, cacheNames(catalog, cache.apps)).then(setStatus, () => setStatus(null));
  }, [cache.dir]); // eslint-disable-line react-hooks/exhaustive-deps
  // Read again as files come in, and when a run ends.
  useEffect(load, [load, fetch.fetched, fetch.running]);

  const setLimit = (gb: number | null) => {
    set((c) => ({ ...c, limitGb: gb }));
    // Lowered: rotated down to it now, never the installed versions' files.
    void native.cacheTrim(cache.dir, gb === null ? null : Math.round(gb * GB), protectedFiles(installs)).then(load, load);
  };
  const choose = async () => {
    setSaid(null);
    const picked = await native.pickFolder(t`Choose a folder for the download cache`, cache.dir);
    if (!picked) return;
    set((c) => ({ ...c, dir: picked }));
    setSaid({ text: t`New files go in this folder. Files already in the old one stay there.` });
  };
  const clear = async () => {
    setClearing(false);
    await native.cacheRemove(cache.dir).then(
      () => setSaid({ text: t`The cache was cleared.` }),
      () => setSaid({ text: t`Couldn't clear the cache. Is its drive connected?`, error: true }),
    );
    load();
  };

  // What Clear orphans would take: files of apps no longer selected, or no longer installed.
  const loose = orphans(status?.entries ?? [], cache, Object.keys(installs), protectedFiles(installs));
  const size = sizeLabel(loose.reduce((n, f) => n + f.size, 0));
  const clearOrphans = async () => {
    setOrphaning(false);
    const count = loose.length;
    await native.cacheRemove(cache.dir, loose.map((f) => f.hash)).then(
      () => setSaid({ text: plural(count, { one: "# orphaned file was removed.", other: "# orphaned files were removed." }) }),
      () => setSaid({ text: t`Couldn't remove the orphaned files. Is the cache's drive connected?`, error: true }),
    );
    load();
  };
  const used = status?.bytes ?? 0;
  const [usedLabel, limitLabel] = [sizeLabel(used), sizeLabel(cache.limit ?? 0)];
  const mode = CACHE_MODES.find((m) => m.mode === cache.mode)!;
  return (
    <section className="settings-section download-cache" aria-label={t`Download cache`}>
      <h3>
        <Trans comment="Heading in Settings: a folder of downloaded release files, kept to install again and as a backup">Download cache</Trans>
      </h3>
      <p className="muted">
        <Trans>
          Keep the files Quiver downloads, to install them again without downloading, even offline, and as a backup of what was released. Each file is checked against the catalog before it's
          installed.
        </Trans>
      </p>
      <div className="update-choices" role="radiogroup" aria-label={t`What the download cache keeps`}>
        {CACHE_MODES.map((m) => (
          <button
            key={m.mode}
            type="button"
            role="radio"
            aria-checked={cache.mode === m.mode}
            className="add-choice update-choice"
            onClick={() => set((c) => ({ ...c, mode: m.mode }))}
          >
            {ICONS[m.mode]}
            <span>
              <strong>{i18n._(m.name)}</strong>
              <small>{i18n._(m.about)}</small>
            </span>
          </button>
        ))}
      </div>
      {mode.warning && (
        <p className="cache-warning" role="note">
          <TriangleAlert size={16} aria-hidden="true" /> {i18n._(mode.warning)}
        </p>
      )}
      {cache.on && (
        <>
          <label className="toggle">
            <input type="checkbox" checked={cache.allPlatforms} onChange={(e) => set((c) => ({ ...c, allPlatforms: e.target.checked || undefined }))} />
            <Trans comment="Download cache setting: keep a release's builds for Windows, Linux, macOS and Android, not only this computer's">Keep every system's files</Trans>
          </label>
          <Field
            label={<Trans comment="Download cache setting: the most storage it uses">Storage limit</Trans>}
            hint={<Trans>When it's full, the files used longest ago make room. The files of the versions you have installed always stay.</Trans>}
          >
            <select value={cache.limitGb === null ? "unlimited" : String(cache.limitGb)} onChange={(e) => setLimit(e.target.value === "unlimited" ? null : Number(e.target.value))}>
              {(LIMITS_GB.includes(cache.limitGb) ? LIMITS_GB : [...LIMITS_GB, cache.limitGb]).map((gb) =>
                gb === null ? (
                  <option key="unlimited" value="unlimited">
                    {t({ message: "Unlimited", comment: "Download cache storage limit: none" })}
                  </option>
                ) : (
                  <option key={gb} value={gb}>
                    {limitName(gb)}
                  </option>
                ),
              )}
            </select>
          </Field>
          <div className="cache-usage">
            {cache.limit !== undefined && (
              <div className="storage-bar" role="meter" aria-label={t`Download cache used`} aria-valuemin={0} aria-valuemax={cache.limit} aria-valuenow={used}>
                <span style={{ width: `${Math.min(100, (used / cache.limit) * 100)}%` }} />
              </div>
            )}
            <p className="save-note">
              {status
                ? [
                    plural(status.files, { one: "# file", other: "# files" }),
                    cache.limit !== undefined ? t`${usedLabel} of ${limitLabel} used` : t`${usedLabel} used`,
                  ].join(" · ")
                : t`Nothing kept yet.`}
            </p>
          </div>
          {(fetches(cache) || cache.mode === "installed") && <FetchProgress manual={cache.mode === "installed"} />}
        </>
      )}
      <div className="save-row cache-folder">
        <span className="save-main">
          <span className="save-path">{cache.dir}</span>
          <span className="save-note">{settings.cache?.dir ? t`Your own folder` : t`The default folder, in Quiver's data folder`}</span>
        </span>
        <button type="button" className="save-button" onClick={() => void native.cacheOpen().catch((e) => setSaid({ text: String(e), error: true }))}>
          <FolderOpen size={14} aria-hidden="true" /> <Trans>Open</Trans>
        </button>
        <button type="button" className="save-button" onClick={() => void choose()}>
          <Trans>Change…</Trans>
        </button>
        {settings.cache?.dir && (
          <button type="button" className="save-button" onClick={() => set(({ dir: _, ...c }) => c)}>
            <Trans>Use default</Trans>
          </button>
        )}
      </div>
      {loose.length > 0 &&
        (orphaning ? (
          <div className="cache-clear">
            <span>
              {cache.mode === "selected"
                ? plural(loose.length, {
                    one: `Remove # file (${size}) of apps that aren't among your selected titles?`,
                    other: `Remove # files (${size}) of apps that aren't among your selected titles?`,
                  })
                : plural(loose.length, {
                    one: `Remove # file (${size}) of apps that aren't installed?`,
                    other: `Remove # files (${size}) of apps that aren't installed?`,
                  })}
            </span>
            <button type="button" className="danger" onClick={() => void clearOrphans()}>
              <Trans comment="Button: removes the cache's files of apps it no longer keeps">Clear orphans</Trans>
            </button>
            <button type="button" onClick={() => setOrphaning(false)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        ) : (
          <button type="button" className="text-button" onClick={() => (setClearing(false), setOrphaning(true))}>
            <Trans comment="Button: asks first, then removes the cache's files of apps it no longer keeps">Clear orphans…</Trans>
          </button>
        ))}
      {Boolean(status?.files) &&
        (clearing ? (
          <div className="cache-clear">
            <span>{t`Remove every file in the download cache? Installed apps aren't affected.`}</span>
            <button type="button" className="danger" onClick={() => void clear()}>
              <Trans>Clear cache</Trans>
            </button>
            <button type="button" onClick={() => setClearing(false)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        ) : (
          <button type="button" className="text-button" onClick={() => (setOrphaning(false), setClearing(true))}>
            <Trans>Clear cache…</Trans>
          </button>
        ))}
      {said && <p className={said.error ? "save-said error" : "save-said"}>{said.text}</p>}
      {cache.mode === "selected" && (
        <SelectedTitles
          apps={cache.apps}
          kept={status?.entries ?? []}
          onOpen={onOpen}
          onFolder={(id) => void native.cacheOpenApp(id).catch((e) => setSaid({ text: String(e), error: true }))}
          onRemove={(app) => set((c) => withSelected(c, app, false))}
        />
      )}
    </section>
  );
}

/** For the modes that download by themselves: what's under way, or how the last run went, and Check now or Stop. */
function FetchProgress({ manual }: { manual: boolean }) {
  const s = useFetchState();
  const c = s.current;
  const of = s.ofApps;
  const [name, version, looked, finished] = [c?.name ?? "", c?.version ?? "", s.apps, s.finishedAt ? new Date(s.finishedAt).toLocaleString(i18n.locale) : ""];
  const shown = c?.filename ? (c.total ? `${sizeLabel(c.received ?? 0)} / ${sizeLabel(c.total)}` : sizeLabel(c.received ?? 0)) : undefined;
  return (
    <div className="save-row cache-fetch" aria-live="polite">
      <Download size={16} aria-hidden="true" />
      <span className="save-main">
        {s.running ? (
          <>
            <span>
              {c?.filename
                ? t({ message: `Downloading ${name} ${version}`, comment: "Download cache: the app and version it's downloading by itself now" })
                : c
                  ? t({ message: `Looking at ${name}`, comment: "Download cache: the app whose releases it's reading" })
                  : t`Starting…`}
            </span>
            <span className="save-note">
              {[of ? t`App ${looked} of ${of}` : plural(s.apps, { one: "# app looked at", other: "# apps looked at" }), plural(s.fetched, { one: "# file kept", other: "# files kept" }), shown]
                .filter(Boolean)
                .join(" · ")}
            </span>
          </>
        ) : s.finishedAt ? (
          <>
            <span>{s.fetched ? plural(s.fetched, { one: "# new file kept", other: "# new files kept" }) : t`Up to date`}</span>
            <span className="save-note">
              {[
                t`Checked ${finished}`,
                s.failed ? plural(s.failed, { one: "# file couldn't be downloaded", other: "# files couldn't be downloaded" }) : "",
                s.noRoom ? plural(s.noRoom, { one: "# file didn't fit in the storage limit", other: "# files didn't fit in the storage limit" }) : "",
              ]
                .filter(Boolean)
                .join(" · ")}
            </span>
          </>
        ) : (
          <span>
            {manual
              ? t`Apps you install from now on are kept as they install. Check now keeps the version you have installed of every app.`
              : t`Downloads start shortly after Quiver opens, then every 6 hours.`}
          </span>
        )}
      </span>
      {s.running ? (
        <button type="button" className="save-button" onClick={() => void native.cacheFetchCancel().catch(() => {})}>
          <X size={14} aria-hidden="true" /> <Trans comment="Stops the download cache's current download">Stop</Trans>
        </button>
      ) : (
        <button type="button" className="save-button" onClick={fetchNow}>
          <Trans>Check now</Trans>
        </button>
      )}
    </div>
  );
}

/**
 * Selected titles' apps, at the foot of the page: each with its picture, as Downloads has them, and how much of the
 * cache its files take, the largest first, in a box that scrolls past a few. Each can be taken off here; they're
 * added from Manage on an app's page or its menu.
 */
function SelectedTitles({
  apps,
  kept,
  onOpen,
  onFolder,
  onRemove,
}: {
  apps: { id: string; slug: string }[];
  kept: CachedFile[];
  onOpen: (entry: Entry) => void;
  onFolder: (id: string) => void;
  onRemove: (app: { id: string; slug: string }) => void;
}) {
  const { catalog, client } = useLauncher();
  // The catalog's details of the ones that aren't in the library, for their pictures and names.
  const [looked, setLooked] = useState<Record<string, Entry | null>>({});
  useEffect(() => {
    for (const app of apps) {
      if (catalog[app.id] || app.id in looked) continue;
      setLooked((l) => ({ ...l, [app.id]: null }));
      client.app(app.slug).then(
        (d) => setLooked((l) => ({ ...l, [app.id]: d.entry })),
        () => {},
      );
    }
  }, [apps.map((a) => a.id).join()]); // eslint-disable-line react-hooks/exhaustive-deps
  const list = useRef<HTMLUListElement>(null);
  useFittedList(list, apps.length);
  const rows = apps
    .map((app) => {
      const files = kept.filter((f) => f.apps.some((o) => o.app === app.id));
      return { app, entry: catalog[app.id] ?? looked[app.id] ?? undefined, bytes: files.reduce((n, f) => n + f.size, 0), files: files.length };
    })
    .sort((a, b) => b.bytes - a.bytes || (a.entry?.projectName ?? a.app.slug).localeCompare(b.entry?.projectName ?? b.app.slug));
  return (
    <div className="cache-titles-box">
      <h4>
        <Trans comment="Heading in Settings › Cache over the apps it keeps">Selected titles</Trans> <span className="download-count">({apps.length})</span>
      </h4>
      {!apps.length ? (
        <p className="muted">
          <Trans>No titles chosen yet. Open an app's page, then Manage, and turn on Keep in download cache.</Trans>
        </p>
      ) : (
        <ul ref={list} className="cache-titles" aria-label={t`Titles kept in the download cache`}>
          {rows.map(({ app, entry, bytes, files }) => {
            const shown = entry?.projectName ?? app.slug;
            const size = sizeLabel(bytes);
            const art = entry && coverOf(entry);
            return (
              <li key={app.id} className="cache-title">
                {/* The whole row opens the app's page; its two buttons sit over it, at its end. */}
                <button type="button" className="cache-title-open" disabled={!entry} onClick={() => entry && onOpen(entry)}>
                  <span className="download-thumb">
                    <Artwork src={art} sources={art ? cardSources(art, "100px") : undefined} name={shown} className={entry?.libraryArt?.header || entry?.libraryArt?.capsule ? "cover-photo" : ""} />
                  </span>
                  <span className="download-names">
                    <span className="download-name">{shown}</span>
                    <span className="download-detail">
                      {files ? [size, plural(files, { one: "# file", other: "# files" })].join(" · ") : t({ message: "Not kept yet", comment: "Settings › Cache: a selected app none of whose files are in the cache yet" })}
                    </span>
                  </span>
                </button>
                <div className="cache-title-actions">
                  <button type="button" className="save-button" aria-label={t`Open ${shown}'s folder in the download cache`} title={t`Open folder`} disabled={!files} onClick={() => onFolder(app.id)}>
                    <FolderOpen size={14} aria-hidden="true" />
                  </button>
                  <button type="button" className="save-button" aria-label={t`Stop keeping ${shown}`} onClick={() => onRemove(app)}>
                    <X size={14} aria-hidden="true" />
                  </button>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

/** The fewest rows the list shows, however short the window: below that, the page scrolls instead. */
const FEWEST_ROWS = 3;
/** A little room left under the list besides the page's own padding. */
const SLACK = 8;

/** What the page has under an element: what follows it, and its boxes' padding, borders and margins to the page's foot. */
function spaceUnder(el: HTMLElement, area: HTMLElement) {
  let under = 0;
  for (let n: HTMLElement | null = el; n && n !== area; n = n.parentElement) {
    const style = getComputedStyle(n);
    under += parseFloat(style.marginBottom) || 0;
    if (n !== el) under += (parseFloat(style.paddingBottom) || 0) + (parseFloat(style.borderBottomWidth) || 0);
    for (let next = n.nextElementSibling; next; next = next.nextElementSibling) under += (next as HTMLElement).offsetHeight || 0;
  }
  return under + (parseFloat(getComputedStyle(area).paddingBottom) || 0);
}

/**
 * Fits a list of rows to the window: as tall as the room from where it starts down to the window's foot, and no less
 * than FEWEST_ROWS, then down to a whole number of rows, so none shows cut off; the rest scroll inside it. Measured
 * again as the window or the list changes size, in the page's pixels (the interface size zooms them: zoom.ts).
 */
function useFittedList(list: React.RefObject<HTMLUListElement | null>, count: number) {
  useLayoutEffect(() => {
    const el = list.current;
    if (!el) return;
    const fit = () => {
      const first = el.firstElementChild as HTMLElement | null;
      if (!first) return;
      const area = pageArea();
      const z = boxZoom();
      const row = first.offsetHeight;
      const gap = parseFloat(getComputedStyle(el).rowGap) || 0;
      // Where the list starts in the page, as if scrolled to the top, and the room from there to the window's foot.
      const top = (el.getBoundingClientRect().top - area.getBoundingClientRect().top) / z + area.scrollTop;
      const room = area.clientHeight - top - spaceUnder(el, area) - SLACK;
      const fits = Math.max(FEWEST_ROWS, Math.floor((room + gap) / (row + gap)));
      const height = fits >= count ? "" : `${fits * row + (fits - 1) * gap}px`;
      if (el.style.maxHeight !== height) el.style.maxHeight = height;
    };
    fit();
    window.addEventListener("resize", fit);
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(fit);
    observer?.observe(pageArea());
    if (el.firstElementChild) observer?.observe(el.firstElementChild);
    return () => {
      window.removeEventListener("resize", fit);
      observer?.disconnect();
    };
  }, [list, count]);
}

/**
 * On an app's Manage tab, while the cache keeps Selected titles: whether it keeps this one, the newest release of it
 * downloaded in the background. Not for an app the player added, whose releases the catalog doesn't list.
 */
export function CacheChoice({ entry }: { entry: { id: string; slug: string } }) {
  const { settings, setSettings, config } = useLauncher();
  const cache = cacheOf(settings.cache, config.cacheDir ?? "");
  if (cache.mode !== "selected" || isOwn(entry.id) || !entry.slug) return null;
  const on = isSelected(cache, entry.id);
  return (
    <label className="toggle panel-toggle">
      <input type="checkbox" checked={on} onChange={(e) => setSettings((s) => ({ ...s, cache: withSelected(s.cache, entry, e.target.checked) }))} />
      <Trans comment="Toggle on an app's Manage tab: the download cache keeps this app's newest release">Keep in download cache</Trans>
    </label>
  );
}
