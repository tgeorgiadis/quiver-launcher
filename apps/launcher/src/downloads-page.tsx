/**
 * Downloads, like Steam's: what's downloading now, the updates waiting, and
 * what finished since the launcher started. One page in every layout,
 * reached from the Downloads button in the top bar, the strip above the
 * library while something is happening, and the Sidebar layout's foot.
 */
import { useState, type ReactNode } from "react";
import { ArrowLeft, Check, Copy, Download, HardDrive, Play, ScrollText, TriangleAlert, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";
import { Artwork, cardSources, coverOf } from "@quiverlauncher/ui";
import { updateFor, useLauncher } from "./store";
import { downloadsLine, downloadsOf, sizeLabel, type Downloads } from "./downloads";
import { LauncherNotes, useLauncherUpdate } from "./update";
import { namesOf } from "./library-tile";
import { withOverrides } from "./library";
import { dayLabel } from "./sidebar";
import { versionLabel } from "./versions";
import { native } from "./native";
import { ChangelogDialog } from "./detail";
import { isCustom, isLocal } from "./custom";
import { cacheOf, fetches } from "./cache";
import { clearCacheFiles, fetchNow, useFetchState, type CacheFile } from "./cache-fetch";

/** What's downloading and waiting, and the apps by the player's names for them. */
function useDownloads() {
  const { library, catalog, installs, jobs, settings } = useLauncher();
  const launcher = useLauncherUpdate();
  const downloads = downloadsOf(library, jobs, installs, catalog, settings.hidden);
  const entryOf = (id: string): Entry | undefined => catalog[id] && withOverrides(catalog[id], library.find((i) => i.id === id)?.overrides);
  // What downloads is the app, so it goes by its own name, not its game's.
  const name = (id: string) => entryOf(id)?.projectName ?? t`An app`;
  // Things waiting on the player: updates, downloads that didn't finish, and Quiver Launcher's own update.
  const waiting = downloads.updates.length + downloads.failed.length + (launcher.available ? 1 : 0);
  return { downloads, jobs, entryOf, name, waiting, launcher };
}

/** The top bar's Downloads button: a ring while something downloads, and a count of what's waiting. */
export function DownloadsButton({ open, onOpen }: { open: boolean; onOpen: () => void }) {
  const { downloads, waiting } = useDownloads();
  const busy = downloads.active.length > 0;
  const active = downloads.active.length;
  const percent = downloads.percent;
  const said = [
    busy
      ? percent === null
        ? plural(active, { one: "# downloading", other: "# downloading" })
        : plural(active, { one: `# downloading, ${percent}%`, other: `# downloading, ${percent}%` })
      : "",
    waiting ? plural(waiting, { one: "# waiting", other: "# waiting" }) : "",
  ].filter(Boolean);
  const list = said.join(", ");
  // The ring's circumference: 2πr for r = 15.
  const around = 94.25;
  return (
    <button
      className={`icon downloads-button${open ? " active" : ""}`}
      aria-label={said.length ? t`Downloads: ${list}` : t`Downloads`}
      title={t`Downloads`}
      aria-current={open ? "page" : undefined}
      data-busy={busy || undefined}
      onClick={onOpen}
    >
      {busy && (
        <svg className="downloads-ring" viewBox="0 0 34 34" aria-hidden="true">
          <circle cx="17" cy="17" r="15" />
          <circle
            className={downloads.percent === null ? "spin" : ""}
            cx="17"
            cy="17"
            r="15"
            strokeDasharray={around}
            strokeDashoffset={downloads.percent === null ? around * 0.75 : around * (1 - downloads.percent / 100)}
          />
        </svg>
      )}
      <Download size={18} />
      {waiting > 0 && (
        <span className="downloads-count" aria-hidden="true">
          {waiting > 99 ? "99+" : waiting}
        </span>
      )}
    </button>
  );
}

/**
 * Above the library and Browse while something downloads or has an update;
 * the updates can be put away until another one comes. A download that
 * didn't finish says so on its card, and counts on the Downloads button.
 */
export function DownloadsStrip({
  onOpen,
  active,
  offerUpdates = true,
}: {
  onOpen: () => void;
  /** Shown only while something's downloading (over the other banners, App.tsx), or only while nothing is. */
  active: boolean;
  /** Says how many updates wait and offers them: on the Library page, not Browse, which is for finding apps. */
  offerUpdates?: boolean;
}) {
  const { downloads, jobs, name } = useDownloads();
  const { catalog, get } = useLauncher();
  const [putAway, setPutAway] = useState("");
  if (active !== downloads.active.length > 0) return null;
  const key = downloads.updates.join(" ");
  const updates = offerUpdates && Boolean(key) && key !== putAway;
  const lines = downloadsLine({ ...downloads, failed: [] }, jobs, name, updates);
  if (!lines.length) return null;
  return (
    <div className="banner downloads-strip" role="status" aria-label={t`Downloads`}>
      <p>{lines.join(" ")}</p>
      {updates && (
        <button className="primary" onClick={() => downloads.updates.forEach((id) => void get(catalog[id]))}>
          {downloads.updates.length === 1 ? t({ message: "Update", comment: "Button: update the one app with an update" }) : t`Update all`}
        </button>
      )}
      {/* On a phone these two are icons, so the line and its buttons share one row. */}
      <button onClick={onOpen}>
        <Download size={15} className="strip-icon" aria-hidden="true" />
        <span className="strip-label">
          <Trans>See downloads</Trans>
        </span>
      </button>
      {updates && !downloads.active.length && (
        <button onClick={() => setPutAway(key)}>
          <X size={15} className="strip-icon" aria-hidden="true" />
          <span className="strip-label">
            <Trans>Not now</Trans>
          </span>
        </button>
      )}
    </div>
  );
}

/** At the foot of the Sidebar layout, as in Steam: the way to Downloads, and how they're going. */
export function SidebarDownloads({ onOpen }: { onOpen: () => void }) {
  const { downloads, jobs, name } = useDownloads();
  const [line] = downloadsLine(downloads, jobs, name);
  return (
    <button type="button" className="sidebar-add sidebar-downloads" data-busy={downloads.active.length > 0 || undefined} title={line} onClick={onOpen}>
      <Download size={15} />
      <span className="sidebar-downloads-label">{line ? line.replace(/\.$/, "") : t`Downloads`}</span>
    </button>
  );
}

/** The time a download finished: "Today 4:58 AM". */
function finishedAt(at: number) {
  const day = dayLabel(at);
  const time = new Date(at).toLocaleTimeString(i18n.locale, { hour: "numeric", minute: "2-digit" });
  return t({ message: `${day} ${time}`, comment: "When a download finished: a day (Today, Yesterday, Sep 3) and a time" });
}

export function DownloadsPage({
  back,
  onBack,
  onOpen,
  action,
}: {
  back: string;
  onBack: () => void;
  /** An app's page; with `at`, at its Releases with that version's notes open. */
  onOpen: (entry: Entry, at?: { releases: string }) => void;
  /** An app's usual button, for a download: its progress and Cancel, or what went wrong. */
  action: (entry: Entry) => ReactNode;
}) {
  const { config, installs, catalog, finished, clearFinished, get, play } = useLauncher();
  const { downloads, jobs, entryOf, launcher } = useDownloads();
  const now: string[] = [...downloads.active, ...downloads.failed];
  // One downloading again shows under Downloading only.
  const done = finished.filter((f) => entryOf(f.id) && installs[f.id] && !now.includes(f.id));
  const updateCount = downloads.updates.length;
  const newest = launcher.available?.version;
  const yours = config.version;
  /** `button` in place of the app's usual one: Update for one waiting, Play for one that's done. */
  const row = (id: string, detail: ReactNode, button?: ReactNode) => {
    const entry = entryOf(id);
    return entry && <DownloadRow key={id} entry={entry} detail={detail} onOpen={() => onOpen(catalog[id] ?? entry)} action={button ?? action(catalog[id] ?? entry)} />;
  };
  const [changelog, setChangelog] = useState<{ id: string; to: string; have?: string } | null>(null);
  const reading = changelog && entryOf(changelog.id);
  // Quiver Launcher's own update: its notes, as the banner's What's new shows them.
  const [launcherNotes, setLauncherNotes] = useState(false);
  /** What's new, over this page. An app the player added has no history on the site, so its own page, as for all its releases. */
  const seeChangelog = (entry: Entry, to: string, have?: string) => {
    if (isCustom(entry.id) || isLocal(entry.id)) onOpen(catalog[entry.id] ?? entry, { releases: to });
    else setChangelog({ id: entry.id, to, ...(have ? { have } : {}) });
  };
  return (
    <section className="downloads-page" aria-label={t`Downloads`}>
      <button className="back-link" onClick={onBack}>
        <ArrowLeft size={15} /> {back}
      </button>
      <h1>
        <Trans>Downloads</Trans>
      </h1>
      <DownloadSection title={t`Downloading`} count={now.length}>
        {now.length ? (
          <ul>
            {now.map((id) => {
              const job = jobs[id];
              const going = job && !("error" in job) ? job : undefined;
              const received = going ? sizeLabel(going.received) : "";
              const size = going?.total ? sizeLabel(going.total) : undefined;
              // Why it stopped, under its name, where there's room for it: the row's buttons are beside it.
              const failed = job && "error" in job ? job.error : "";
              const detail = !going
                ? failed
                  ? (
                    <span className="download-failed" title={failed}>
                      <TriangleAlert size={14} aria-hidden="true" /> {failed}
                    </span>
                  )
                  : t`Didn't finish`
                : going.phase === "installing"
                  ? t`Installing`
                  : size
                    ? t`${received} of ${size}`
                    : going.received
                      ? t`${received} so far`
                      : t`Starting`;
              return row(id, detail);
            })}
          </ul>
        ) : (
          <p className="muted">
            <Trans>Nothing is downloading.</Trans>
          </p>
        )}
      </DownloadSection>
      <DownloadSection
        title={t`Updates available`}
        count={downloads.updates.length + (launcher.available ? 1 : 0)}
        tools={
          downloads.updates.length > 1 && (
            <button className="primary" onClick={() => downloads.updates.forEach((id) => void get(catalog[id]))}>
              <Trans>Update all ({updateCount})</Trans>
            </button>
          )
        }
      >
        {downloads.updates.length || launcher.available ? (
          <ul>
            {launcher.available && (
              <li className="download-row" data-slug="quiver-launcher">
                <span className="download-thumb launcher-thumb">
                  <img src="/quiver-icon-96.png" alt="" width="40" height="40" />
                </span>
                <div className="download-names">
                  <span className="download-name">Quiver Launcher</span>
                  <span className="download-detail">
                    <Trans>{newest} is out. You have {yours}.</Trans>
                  </span>
                  {launcher.error && <span className="job-error download-detail">{launcher.error}</span>}
                </div>
                <div className="download-action">
                  {launcher.installing ? (
                    <div className="progress" aria-label={t`Updating Quiver Launcher`}>
                      <div className="bar" style={{ width: `${launcher.installing.total ? Math.floor((launcher.installing.done / launcher.installing.total) * 100) : 5}%` }} />
                      <span>
                        <Trans>Updating…</Trans>
                      </span>
                    </div>
                  ) : (
                    <span className="download-buttons">
                      <button onClick={() => setLauncherNotes(true)}>
                        <ScrollText size={15} /> <Trans>See changelog</Trans>
                      </button>
                      {launcher.available.manual ? (
                        <button className="primary wide" onClick={() => void native.openUrl(launcher.available!.url)}>
                          <Trans>Open the release page</Trans>
                        </button>
                      ) : (
                        <button className="primary wide" onClick={() => void launcher.install(launcher.available!.version)}>
                          {launcher.error ? t`Try again` : t`Update and restart`}
                        </button>
                      )}
                    </span>
                  )}
                </div>
              </li>
            )}
            {downloads.updates.map((id) => {
              const out = versionLabel(updateFor(catalog[id], installs[id])?.version);
              const have = versionLabel(installs[id]?.version);
              const entry = entryOf(id);
              // Update only: Play comes once it's updated, under Recently finished. What's new in it first, if wanted.
              return row(
                id,
                t`${out} is out. You have ${have}.`,
                <span className="download-buttons">
                  {entry && catalog[id]?.verified && (
                    <button onClick={() => seeChangelog(entry, catalog[id].verified!.version, installs[id]?.version)}>
                      <ScrollText size={15} /> <Trans>See changelog</Trans>
                    </button>
                  )}
                  {/* With more than one update, Update all above is the page's one filled button. */}
                  <button className={downloads.updates.length > 1 ? "wide" : "primary wide"} onClick={() => void get(catalog[id])}>
                    <Download size={15} /> <Trans>Update to {out}</Trans>
                  </button>
                </span>,
              );
            })}
          </ul>
        ) : (
          <p className="muted">
            <Trans>Everything is up to date.</Trans>
          </p>
        )}
      </DownloadSection>
      <DownloadSection
        title={t`Recently finished`}
        count={done.length}
        tools={
          done.length > 0 && (
            <button onClick={clearFinished}>
              <Trans>Clear all</Trans>
            </button>
          )
        }
      >
        {done.length ? (
          <ul>
            {done.map((f) => {
              const version = versionLabel(f.version);
              const when = finishedAt(f.at);
              const entry = entryOf(f.id);
              return row(
                f.id,
                f.update ? t`Updated to ${version} · ${when}` : t`Installed ${version} · ${when}`,
                <span className="download-buttons">
                  {/* An update: what's new in it. */}
                  {f.update && entry && (
                    <button onClick={() => seeChangelog(entry, f.version)}>
                      <ScrollText size={15} /> <Trans>See changelog</Trans>
                    </button>
                  )}
                  <button className="wide" onClick={() => void play(f.id)}>
                    <Play size={15} /> <Trans comment="Button: start the app">Play</Trans>
                  </button>
                </span>,
              );
            })}
          </ul>
        ) : (
          <p className="muted">
            <Trans>Downloads that finish while Quiver Launcher is open show here.</Trans>
          </p>
        )}
      </DownloadSection>
      <CacheDownloads onOpen={(entry) => onOpen(catalog[entry.id] ?? entry)} />
      {launcherNotes && launcher.available && <LauncherNotes update={launcher.available} onClose={() => setLauncherNotes(false)} />}
      {changelog && reading && (
        <ChangelogDialog
          entry={catalog[changelog.id] ?? reading}
          to={changelog.to}
          have={changelog.have}
          onUpdate={downloads.updates.includes(changelog.id) && updateFor(catalog[changelog.id], installs[changelog.id])?.version === changelog.to ? () => (setChangelog(null), void get(catalog[changelog.id])) : undefined}
          onAll={() => (setChangelog(null), onOpen(catalog[changelog.id] ?? reading, { releases: changelog.to }))}
          onClose={() => setChangelog(null)}
        />
      )}
    </section>
  );
}

function DownloadSection({ title, count, tools, children }: { title: string; count: number; tools?: ReactNode; children: ReactNode }) {
  return (
    <section className="download-section" aria-label={title}>
      <header>
        <h2>
          {title} <span className="download-count">({count})</span>
        </h2>
        {tools}
      </header>
      {children}
    </section>
  );
}

function DownloadRow({ entry, detail, action, onOpen }: { entry: Entry; detail: ReactNode; action: ReactNode; onOpen: () => void }) {
  const [name, other] = namesOf(entry, "app");
  return (
    <li className="download-row" data-slug={entry.slug}>
      {/* The picture and the name open the app's page: the picture for a controller too, left of the row's buttons. */}
      <button type="button" className="download-thumb" aria-label={entry.projectName} onClick={onOpen}>
        <Artwork src={coverOf(entry)} sources={coverOf(entry) ? cardSources(coverOf(entry)!, "100px") : undefined} name={entry.projectName} className={entry.libraryArt?.header || entry.libraryArt?.capsule ? "cover-photo" : ""} />
      </button>
      <div className="download-names">
        {/* For a mouse: a controller has the picture for the app's page, not its name as a second stop beside it. */}
        <button type="button" className="download-name" tabIndex={-1} onClick={onOpen}>
          {name}
        </button>
        {other && <span className="download-other">{other}</span>}
        <span className="download-detail">{detail}</span>
      </div>
      <div className="download-action">{action}</div>
    </li>
  );
}

/**
 * The download cache's own downloads (cache-fetch.ts), apart from the apps being installed: the one under way, then
 * what it kept or couldn't, each by its app, version and file as published. One an antivirus took away says so, and
 * its details copy for a report, so the release can be checked and taken out of the catalog.
 */
function CacheDownloads({ onOpen }: { onOpen: (entry: Entry) => void }) {
  const { settings, config } = useLauncher();
  const cache = cacheOf(settings.cache, config.cacheDir ?? "");
  const run = useFetchState();
  const [copied, setCopied] = useState<string | null>(null);
  if (!fetches(cache) && !run.files.length) return null;
  const copy = (f: CacheFile) => {
    const name = f.entry.projectName;
    const page = `https://quiverlauncher.com/apps/${f.entry.slug}`;
    void navigator.clipboard?.writeText(`${name} ${f.version}: ${f.filename} (${whyLabel(f)})\n${page}`).then(() => setCopied(f.key));
  };
  return (
    <DownloadSection
      title={t({ message: "Download cache", comment: "Section of Downloads: the files the download cache downloads by itself" })}
      count={run.files.length}
      tools={
        <span className="download-buttons">
          {run.files.length > 0 && !run.running && (
            <button onClick={clearCacheFiles}>
              <Trans>Clear list</Trans>
            </button>
          )}
          {fetches(cache) &&
            (run.running ? (
              <button onClick={() => void native.cacheFetchCancel().catch(() => {})}>
                <X size={15} /> <Trans comment="Stops the download cache's current download">Stop</Trans>
              </button>
            ) : (
              <button onClick={fetchNow}>
                <HardDrive size={15} /> <Trans>Check now</Trans>
              </button>
            ))}
        </span>
      }
    >
      {run.files.length ? (
        <ul>
          {run.files.map((f) => {
            const version = versionLabel(f.version);
            const file = f.filename;
            const when = finishedAt(f.at);
            const percent = f.total ? Math.min(100, Math.floor(((f.received ?? 0) / f.total) * 100)) : null;
            const received = sizeLabel(f.received ?? 0);
            const size = f.total ? sizeLabel(f.total) : undefined;
            const detail =
              f.state === "downloading" ? (
                size ? t`${version} · ${file} · ${received} of ${size}` : t`${version} · ${file}`
              ) : f.state === "kept" ? (
                t({ message: `Kept ${version} · ${file} · ${when}`, comment: "Downloads, Download cache: a file it downloaded and kept, and when" })
              ) : (
                <span className="download-failed" title={`${whyLabel(f)}: ${file}`}>
                  <TriangleAlert size={14} aria-hidden="true" /> {whyLabel(f)}: {version} · {file}
                </span>
              );
            const action =
              f.state === "downloading" ? (
                <div className="row progress-row">
                  <div className="progress" aria-label={t`Downloading`}>
                    <div className="bar" style={{ width: `${percent ?? 5}%` }} />
                    <span>{percent === null ? t`Downloading…` : t`Downloading ${percent}%`}</span>
                  </div>
                  <button className="icon cancel-download" aria-label={t`Stop the download cache`} title={t`Stop`} onClick={() => void native.cacheFetchCancel().catch(() => {})}>
                    <X size={15} />
                  </button>
                </div>
              ) : f.state === "failed" ? (
                <span className="download-buttons">
                  <button onClick={() => copy(f)}>
                    {copied === f.key ? <Check size={15} /> : <Copy size={15} />} {copied === f.key ? t`Copied` : t`Copy details`}
                  </button>
                </span>
              ) : null;
            return <DownloadRow key={f.key} entry={f.entry} detail={detail} onOpen={() => onOpen(f.entry)} action={action} />;
          })}
        </ul>
      ) : (
        <p className="muted">
          {run.running ? <Trans>Looking for files to keep…</Trans> : <Trans>The files the download cache downloads by itself show here.</Trans>}
        </p>
      )}
    </DownloadSection>
  );
}

/** Why a cache download failed, as it reads. */
function whyLabel(f: CacheFile) {
  if (f.why === "blocked") return t({ message: "Removed by your antivirus", comment: "Downloads, Download cache: the file went as it downloaded, as antivirus software (Windows Defender) removes a file it flags" });
  if (f.why === "mismatch") return t`Not the file Quiver checked`;
  if (f.why === "missing") return t`Gone from where it was published`;
  return t`Couldn't download`;
}
