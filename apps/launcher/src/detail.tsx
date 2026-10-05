/**
 * The parts of an app's page beyond Get and Play: its repository, README and
 * releases, the project's details, shortcuts, picking another version, and
 * the player's tags.
 */
import { useEffect, useState } from "react";
import { ArrowUpRight, ChevronDown, Download, ExternalLink, Monitor, Package, Pin, ShieldAlert, ShieldCheck, ShieldX } from "lucide-react";
import { ApiError, type AiUse, type Checking, type Detail, type Entry, type HistoryRelease, type Readme as ReadmeText, type Release, type ReleaseScan } from "@quiverlauncher/api";
import { OS_NAMES, ReleaseBadge, fullDate, platformList, relativeTime } from "@quiverlauncher/ui";
import { Markdown } from "@quiverlauncher/ui/markdown";
import { useLauncher } from "./store";
import { bare, versionLabel, versionList, type Version } from "./versions";
import { native } from "./native";
import { isCustom, isLocal } from "./custom";
import { Dialog, PlaylistChoices } from "./library";

export type Source = { provider: "github" | "gitlab"; repository: string; url: string };

const sourceOf = (provider: string, repository?: string): Source | null =>
  repository && (provider === "github" || provider === "gitlab")
    ? { provider, repository, url: `https://${provider}.com/${repository}` }
    : null;

/**
 * The app's page on the site, and where its code lives. Each is undefined
 * while loading, null when there's none (apps the player added have no page).
 */
export function useAppDetail(entry: Entry): { source: Source | null | undefined; detail: Detail | null | undefined } {
  const { client, library } = useLauncher();
  const custom = library.find((i) => i.id === entry.id)?.custom;
  const own = isCustom(entry.id) || isLocal(entry.id);
  const [detail, setDetail] = useState<Detail | null | undefined>(own ? null : undefined);
  useEffect(() => {
    if (own) return;
    let live = true;
    client.app(entry.slug).then(
      (d) => live && setDetail(d),
      () => live && setDetail(null),
    );
    return () => void (live = false);
  }, [client, entry.slug, own]);
  const source = custom
    ? sourceOf(custom.provider, custom.repository)
    : isCustom(entry.id)
      ? sourceOf(entry.id.slice(0, entry.id.indexOf(":")), entry.id.slice(entry.id.indexOf(":") + 1))
      : detail === undefined
        ? undefined
        : detail && sourceOf(detail.project.provider, detail.project.repository);
  return { source, detail };
}

/** The releases the site approved, newest first: 5, then 10 more at a time. */
export function useReleases(entry: Entry, on: boolean) {
  const { client } = useLauncher();
  const [items, setItems] = useState<Release[] | undefined>();
  const [next, setNext] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  function load(cursor: string | null, count: number) {
    setLoading(true);
    client.releases(entry.slug, count, cursor).then(
      (page) => {
        setItems((list) => (cursor ? [...(list ?? []), ...page.items] : page.items));
        setNext(page.isDone ? null : page.nextCursor);
        setLoading(false);
      },
      () => (setItems((list) => list ?? []), setLoading(false)),
    );
  }
  useEffect(() => {
    if (on) load(null, 5);
  }, [client, entry.slug, on]); // eslint-disable-line react-hooks/exhaustive-deps
  return { items, more: Boolean(next), loading, loadMore: () => load(next, 10) };
}
export type Releases = ReturnType<typeof useReleases>;

/** A verified release as the full list has it, for a site that doesn't list every release yet. */
const asHistory = (r: Release): HistoryRelease => ({
  releaseId: r.id,
  version: r.version,
  releasedAt: r.releasedAt,
  notes: r.notes ?? "",
  prerelease: r.prerelease,
  state: "verified",
  reasons: [],
  assets: r.assets,
  ...(r.installationOverride ? { installationOverride: r.installationOverride } : {}),
});

/**
 * Every release of the app's repository, newest first, verified or not, as
 * the website's Releases tab lists them: 10, then 10 more at a time. A site
 * that doesn't list them all yet gives its verified ones.
 */
export function useReleaseHistory(entry: Entry, on: boolean) {
  const { client } = useLauncher();
  const [items, setItems] = useState<HistoryRelease[] | undefined>();
  const [next, setNext] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  function load(cursor: string | null) {
    setLoading(true);
    client
      .releaseHistory(entry.slug, 10, cursor)
      .catch((e: unknown) => {
        if (!(e instanceof ApiError && e.status === 404)) throw e;
        return client.releases(entry.slug, 10, cursor).then((page) => ({ ...page, items: page.items.map(asHistory) }));
      })
      .then(
        (page) => {
          setItems((list) => (cursor ? [...(list ?? []), ...page.items] : page.items));
          setNext(page.isDone ? null : page.nextCursor);
          setLoading(false);
        },
        () => (setItems((list) => list ?? []), setLoading(false)),
      );
  }
  useEffect(() => {
    if (on) load(null);
  }, [client, entry.slug, on]); // eslint-disable-line react-hooks/exhaustive-deps
  return { items, more: Boolean(next), loading, loadMore: () => load(next) };
}
export type ReleaseHistory = ReturnType<typeof useReleaseHistory>;

export function RepositoryLink({ source }: { source: Source }) {
  return (
    <a className="repo-link" href={source.url}>
      <ExternalLink size={14} /> {source.provider === "github" ? "GitHub" : "GitLab"}: {source.repository}
    </a>
  );
}

/** The project's README: the site's copy, else straight from GitHub. */
export function Readme({ entry, source }: { entry: Entry; source: Source | null | undefined }) {
  const { client, github } = useLauncher();
  const [readme, setReadme] = useState<ReadmeText | null | undefined>();
  useEffect(() => {
    if (source === undefined || isLocal(entry.id)) return;
    let live = true;
    const upstream = () => (source?.provider === "github" ? github.readme(source.repository) : Promise.resolve(null));
    (isCustom(entry.id) ? upstream() : client.readme(entry.slug).then((r) => r ?? upstream()))
      .catch(() => upstream())
      .then(
        (r) => live && setReadme(r),
        () => live && setReadme(null),
      );
    return () => void (live = false);
  }, [client, github, entry.id, entry.slug, source]);
  // No README: what the catalog says about it, as on the website.
  if (readme === null && entry.description.trim() && !isLocal(entry.id))
    return (
      <section className="readme">
        <h3>About this project</h3>
        <p className="detail-description">{entry.description}</p>
      </section>
    );
  if (!readme) return null;
  return (
    <section className="readme">
      <h3>README</h3>
      <div className="readme-body">
        <Markdown text={readme.markdown} rawBase={readme.rawBase} htmlBase={readme.htmlBase} />
      </div>
    </section>
  );
}

/** Where a newer, unverified release stands, as the website words it; kept current each minute. */
export function CheckingStatus({ checking }: { checking: Checking }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(timer);
  }, []);
  if (checking.checkEndsAt === undefined) return checking.needsReview ? "unverified · waiting for a maintainer" : "unverified · being checked";
  const hours = Math.ceil((checking.checkEndsAt - now) / 3_600_000);
  return hours <= 0 ? "unverified · verified shortly" : `unverified · verified in about ${hours} ${hours === 1 ? "hour" : "hours"}`;
}

/** The newer release being checked, shown only when the app has an approved one to install meanwhile. */
export const checkingOf = (detail: Detail | null | undefined, releases: Releases) => (releases.items?.length ? detail?.checking : undefined);

/**
 * One release, in release order with the others, as on the website: a
 * verified one links its files; an unverified one stands out in orange and
 * says why, with its files under a warning; a blocked one stands out in red,
 * says why and links nothing.
 */
function ReleaseCard({ release, open, onToggle, host }: { release: HistoryRelease; open: boolean; onToggle: () => void; host: string }) {
  const { state, scan } = release;
  return (
    <article className={`panel release release-${state}${open ? " open" : ""}`}>
      <button type="button" className="release-summary" aria-expanded={open} onClick={onToggle}>
        <span>
          <strong>
            {release.version}
            {release.prerelease && <span className="prerelease-tag">Pre-release</span>}
          </strong>
          <small className="muted">
            {fullDate(release.releasedAt)} · {release.assets.length} {release.assets.length === 1 ? "file" : "files"}
          </small>
        </span>
        <ReleaseBadge state={state} />
        <ChevronDown size={16} />
      </button>
      {state !== "verified" && (
        <div className="release-why">
          {release.reasons.map((r) => (
            <p key={r}>{r}</p>
          ))}
          {release.checkEndsAt !== undefined && <p>Due to be verified in {hoursFrom(release.checkEndsAt)}.</p>}
          {state === "blocked" && (
            <p>
              <strong>If you installed it, remove it and install a verified release instead.</strong>
            </p>
          )}
          {scan?.url && (scan.verdict === "flagged" || scan.verdict === "warning") && (
            <a className="release-hold-link" href={scan.url}>
              VirusTotal: {scan.engines ?? "flagged"}
              <ArrowUpRight size={13} aria-hidden="true" />
            </a>
          )}
        </div>
      )}
      {open && (
        <div className="release-details">
          {release.notes.trim() ? (
            <div className="release-notes">
              <Markdown text={release.notes} />
            </div>
          ) : (
            <p className="muted release-notes">No release notes provided.</p>
          )}
          {state === "blocked" ? (
            <p className="release-files-note">Quiver blocked this release, so it doesn&apos;t link its files. Change version installs it only if you pick it and insist.</p>
          ) : (
            <>
              {state === "unverified" && (
                <p className="release-files-note">Quiver hasn&apos;t verified these files. Change version installs them only if you pick this release and confirm.</p>
              )}
              <div className="download-list">
                {release.assets.map((a) => (
                  <a className="download" key={a.id} href={a.url}>
                    <Download size={18} />
                    <span>
                      <strong>
                        {a.os === "unknown" ? "Platform unspecified" : (OS_NAMES[a.os] ?? a.os)} · {a.architecture}
                      </strong>
                      <small>{a.filename}</small>
                    </span>
                    <ExternalLink size={14} />
                  </a>
                ))}
              </div>
            </>
          )}
          {state !== "verified" && release.upstreamUrl && (
            <a className="release-hold-link" href={release.upstreamUrl}>
              View {release.version} on {host}
              <ArrowUpRight size={13} aria-hidden="true" />
            </a>
          )}
        </div>
      )}
    </article>
  );
}

/**
 * Every release of the app in release order, verified or not, as on the
 * website's Releases tab. Another version installs from Change version.
 */
export function ReleasesTab({ detail, history }: { detail: Detail | null | undefined; history: ReleaseHistory }) {
  const [toggled, setToggled] = useState<Set<string>>(new Set());
  const { items } = history;
  const host = detail?.project.provider === "gitlab" ? "GitLab" : "GitHub";
  const newestVerified = items?.find((r) => r.state === "verified");
  return (
    <section className="release-section">
      {newestVerified?.prerelease && (
        <div className="prerelease-notice" role="note">
          <strong>The newest verified release is a pre-release build.</strong> The project hasn't published a newer stable release with downloads, so Quiver Launcher
          installs this one. Expect bugs and unfinished features.
        </div>
      )}
      {items === undefined ? (
        <div className="more" role="status" aria-label="Loading releases">
          <span className="spinner" />
        </div>
      ) : items.length === 0 ? (
        <div className="empty compact panel">
          <Package size={28} />
          <h3>Releases are being cataloged</h3>
          <p>There are no releases yet. Check the upstream project for downloads.</p>
        </div>
      ) : (
        <>
          <p className="muted release-intro">Quiver Launcher only updates to verified releases. It installs an unverified or blocked one only if you pick it and confirm.</p>
          {items.map((release) => {
            // The newest verified release starts open; the others fold.
            const open = (release === newestVerified) !== toggled.has(release.releaseId);
            const flip = () => setToggled((t) => (t.delete(release.releaseId) ? new Set(t) : new Set(t).add(release.releaseId)));
            return <ReleaseCard key={release.releaseId} release={release} open={open} onToggle={flip} host={host} />;
          })}
        </>
      )}
      {history.more && (
        <button className="button secondary" disabled={history.loading} onClick={history.loadMore}>
          Show older releases
        </button>
      )}
    </section>
  );
}

const DEVELOPER_SAYS: Record<string, string> = { none: "No AI used", assisted: "Some AI assistance", generated: "Mostly AI-generated" };
const AI_SOURCES: Record<AiUse["source"], string> = {
  developer: "the developer's answer",
  readme: "the project's README",
  signals: "repository signals",
  admin: "a moderator",
};

/** How much AI wrote it, as the website's AI use row says it. */
function AiUseSummary({ ai }: { ai?: AiUse }) {
  const label = !ai || ai.level === "none"
    ? ai?.source === "readme" || (ai?.developerAnswer === "none" && ai.answeredBy === "developer") ? "Developer states no AI" : "No AI use found"
    : ai.level === "assisted" ? "AI-assisted" : "Mostly AI-generated";
  const answer = ai?.developerAnswer && ai.developerAnswer !== "unknown" && ai.source !== "developer" ? ai.developerAnswer : undefined;
  return (
    <div className="ai-use">
      <strong>{label}</strong>
      <span className="muted">{ai ? ` · Based on ${AI_SOURCES[ai.source]}, checked ${fullDate(ai.checkedAt)}` : " · Not checked yet"}</span>
      {answer && (
        <p className="muted">
          {answer === "credited" ? "Developer wasn't sure" : `Developer says: ${DEVELOPER_SAYS[answer]}`}
        </p>
      )}
      {ai && ai.evidence.length > 0 && (
        <details>
          <summary>Evidence</summary>
          <ul className="ai-evidence">
            {ai.evidence.map((e, i) => (
              <li key={i}>{e.url ? <a href={e.url}>{e.detail}</a> : e.detail}</li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}

/** Who made it, where it runs, its latest release and its AI use, as on the website's Project details. */
export function ProjectDetails({ entry, detail }: { entry: Entry; detail: Detail | null | undefined }) {
  const latest = entry.verified
    ? { version: entry.verified.version, at: entry.verified.releasedAt }
    : entry.lastReleaseAt
      ? { version: entry.lastReleaseVersion, at: entry.lastReleaseAt }
      : undefined;
  const stale = latest && Date.now() - latest.at > 365 * 24 * 60 * 60 * 1000;
  const developer = detail?.entry.developer ?? entry.developer;
  return (
    <section className="panel content-panel project-details" aria-label="Project details">
      <div className="eyebrow">PROJECT DETAILS</div>
      <dl>
        {developer && (
          <>
            <dt>Made by</dt>
            <dd>{detail?.project.author || developer.name}</dd>
          </>
        )}
        <dt>Supported platforms</dt>
        <dd>{platformList(entry.supportedOS)}</dd>
        <dt>Latest release</dt>
        <dd>
          {latest ? (
            <span className={stale ? "release-age stale" : ""} title={fullDate(latest.at)}>
              {latest.version ? `${latest.version} · ` : ""}
              {relativeTime(latest.at)}
            </span>
          ) : (
            "No releases found"
          )}
        </dd>
        <dt>AI use</dt>
        <dd>{detail === undefined ? <span className="muted">…</span> : <AiUseSummary ai={detail?.project.aiUse} />}</dd>
      </dl>
    </section>
  );
}

/** A desktop shortcut and a Steam shortcut, both starting the game directly. */
export function Shortcuts({ entry }: { entry: Entry }) {
  const { installs, config } = useLauncher();
  const install = installs[entry.id];
  const [message, setMessage] = useState<{ text: string; error?: boolean } | null>(null);
  if (!install || (config.os !== "windows" && config.os !== "linux")) return null;
  const art = entry.libraryArt ?? {};
  const request = {
    name: entry.projectName,
    folder: install.folder,
    dir: install.dir,
    preferred: install.executables,
    wine: Boolean(install.wine),
    program: install.program,
    art: { icon: entry.artwork ?? art.logo, header: art.header, capsule: art.capsule, hero: art.hero, logo: art.logo },
  };
  const run = (action: Promise<string>) =>
    action.then(
      (text) => setMessage({ text }),
      (e) => setMessage({ text: String(e), error: true }),
    );
  return (
    <div className="shortcuts">
      <div className="row">
        <button onClick={() => run(native.createShortcut(request))}>
          <Monitor size={15} /> Desktop shortcut
        </button>
        <button onClick={() => run(native.addToSteam(request))}>Add to Steam</button>
      </div>
      {message && (
        <p className={message.error ? "job-error" : "muted"} role="status">
          {message.text}
        </p>
      )}
    </div>
  );
}

/** "about 31 hours" from now, for a release's wait. */
const hoursFrom = (at: number) => {
  const hours = Math.max(1, Math.ceil((at - Date.now()) / 3_600_000));
  return `about ${hours} ${hours === 1 ? "hour" : "hours"}`;
};

/** What VirusTotal said, for a player deciding. */
function scanText(scan: ReleaseScan) {
  if (scan.verdict === "clean") return "no antivirus engine flags its files.";
  if (scan.verdict === "warning") return `${scan.engines ?? "an engine"} flag one of its files. A lone detection is often a false alarm.`;
  if (scan.verdict === "flagged") return `${scan.engines ?? "several engines"} flag one of its files.`;
  if (scan.verdict === "pending") return "still scanning its files.";
  return "couldn't scan every file.";
}

/** Keeps the app on a version: pressed while it's pinned there. Its words show for keyboards and controllers. */
function PinButton({ pressed, onClick }: { pressed: boolean; onClick: () => void }) {
  return (
    <button type="button" className="pin-button" aria-pressed={pressed} title={pressed ? "Pinned: click to unpin" : "Always stay on this version"} onClick={onClick}>
      <Pin size={14} aria-hidden="true" />
      <span className="pin-label">Always stay on this version</span>
    </button>
  );
}

/**
 * Every release of the app, verified or not. A verified one installs from
 * the site's checked files. For a catalog app, an unverified one asks
 * first, steering toward waiting for it to be verified; a blocked one asks
 * harder, and installs only once the player ticks that they understand.
 * Installing one only installs it: staying on it is the pin, which the
 * player sets on purpose (never on a blocked one).
 */
export function Versions({ entry, source }: { entry: Entry; source: Source | null | undefined }) {
  const { client, github, gitlab, installs, get, jobs, setUpdates } = useLauncher();
  const [versions, setVersions] = useState<Version[] | string | null>(null);
  // An unverified release the player chose, waiting for them to confirm; `pin` keeps the app on it after.
  const [asking, setAsking] = useState<{ version: Version; pin: boolean } | null>(null);
  // A blocked release the player chose: installed only once they tick that they understand.
  const [insisting, setInsisting] = useState<Version | null>(null);
  const [understood, setUnderstood] = useState(false);
  const install = installs[entry.id];
  const pinned = install?.updates === "pinned";
  // A custom app has nothing Quiver checks, so nothing to mark or warn about.
  const custom = isCustom(entry.id);
  if (source === undefined || jobs[entry.id]) return null;
  async function load() {
    setVersions("loading");
    try {
      const [checked, unverified, upstream] = await Promise.all([
        custom ? [] : client.releases(entry.slug, 100).then((p) => p.items),
        custom
          ? []
          : client.unverifiedReleases(entry.slug).catch((e: unknown) => {
              if (e instanceof ApiError && e.status === 404) return null;
              throw e;
            }),
        !source ? [] : source.provider === "github" ? github.releases(source.repository, 50) : gitlab.releases(source.repository, 50),
      ]);
      setVersions(versionList(checked, unverified, upstream));
    } catch (e) {
      setVersions(e instanceof Error ? e.message : String(e));
    }
  }
  function installVersion(version: Version, pin: boolean) {
    setVersions(null);
    const blocked = !custom && version.state === "blocked" ? (version.reasons[0] ?? "Quiver blocked this release.") : undefined;
    void get(entry, version.release, { pin, unverified: !custom && version.state !== "verified", ...(blocked ? { blocked } : {}) });
  }
  function pick(version: Version, pin = false) {
    if (version.state === "blocked" && !custom) return (setUnderstood(false), setInsisting(version));
    if (version.state === "unverified" && !custom) return setAsking({ version, pin });
    installVersion(version, pin);
  }
  if (insisting) {
    const close = () => setInsisting(null);
    const { release, reasons, scan } = insisting;
    const keep = install && !install.local ? versionLabel(install.version) : undefined;
    return (
      <Dialog label="Install a blocked release" onClose={close}>
        <div className="detail-body unverified-prompt blocked-prompt">
          <h2>
            <ShieldX size={18} /> Install a blocked release?
          </h2>
          <p>
            Quiver blocked {release.version}, so it&apos;s not meant to be installed. Quiver Launcher never updates to it. Install it only if you know why you need this
            exact version.
          </p>
          {reasons.length > 0 && (
            <ul className="unverified-reasons">
              {reasons.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          )}
          {scan && (
            <p className="scan-line">
              VirusTotal: {scanText(scan)}{" "}
              {scan.url && (
                <a href={scan.url} target="_blank" rel="noreferrer">
                  See the report
                </a>
              )}
            </p>
          )}
          <p>It installs the files Quiver saw when it came out, and refuses any that changed since.</p>
          <label className="blocked-consent">
            <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} /> I understand Quiver blocked this release
          </label>
          <div className="row">
            {/* Keeping what they have is the easy choice: it's first, and where a controller starts. */}
            <button className="primary" autoFocus onClick={close}>
              {keep ? `Keep ${keep}` : "Cancel"}
            </button>
            <button
              className="danger"
              disabled={!understood}
              onClick={() => {
                close();
                installVersion(insisting, false);
              }}
            >
              Install {release.version} anyway
            </button>
          </div>
        </div>
      </Dialog>
    );
  }
  if (asking) {
    const close = () => setAsking(null);
    const { version, pin } = asking;
    const host = source?.provider === "gitlab" ? "GitLab" : "GitHub";
    const waits = version.checkEndsAt !== undefined;
    return (
      <Dialog label="Install a release before it's verified" onClose={close}>
        <div className="detail-body unverified-prompt">
          <h2>
            <ShieldAlert size={18} /> Install {version.release.version} before it&apos;s verified?
          </h2>
          <p>
            {version.known
              ? `Quiver hasn't verified this release. It installs the files Quiver saw when it came out, and refuses any that changed since.`
              : `Quiver hasn't checked this release's files. It downloads straight from ${host}, as the developer published it.`}
          </p>
          {version.reasons.length > 0 && (
            <ul className="unverified-reasons">
              {version.reasons.map((r) => (
                <li key={r}>{r}</li>
              ))}
            </ul>
          )}
          {version.scan && (
            <p className="scan-line">
              VirusTotal: {scanText(version.scan)}{" "}
              {version.scan.url && (
                <a href={version.scan.url} target="_blank" rel="noreferrer">
                  See the report
                </a>
              )}
            </p>
          )}
          {waits && (
            <p className="wait-line">
              <ShieldCheck size={14} aria-hidden="true" /> If you wait, it&apos;s verified in {hoursFrom(version.checkEndsAt!)} and offered as an update then.
            </p>
          )}
          {pin && <p>{entry.projectName} then stays on this version until you unpin it.</p>}
          <div className="row">
            {/* Waiting is the easy choice: it's first, and where a controller starts. */}
            <button className="primary" autoFocus onClick={close}>
              {waits ? "Wait" : "Cancel"}
            </button>
            <button
              onClick={() => {
                close();
                installVersion(version, pin);
              }}
            >
              Install anyway
            </button>
          </div>
        </div>
      </Dialog>
    );
  }
  if (versions === null) return <button onClick={load}>Change version</button>;
  if (typeof versions === "string")
    return versions === "loading" ? <span className="spinner" /> : <p className="job-error">{versions}</p>;
  return (
    <section className="versions" aria-label="Versions">
      <h3>Versions</h3>
      {versions.length === 0 && <p className="muted">No releases found.</p>}
      <ul>
        {versions.map((version) => {
          const { release, state, reasons } = version;
          return (
            <li key={release.id + release.version} className={`version ${custom ? "verified" : state}`}>
              <strong>{release.version}</strong>
              <span className="muted">{release.releasedAt ? fullDate(release.releasedAt) : ""}</span>
              {!custom && <ReleaseBadge state={state} title={reasons.join(" ") || undefined} />}
              {release.prerelease && <span className="muted">Pre-release</span>}
              {install && bare(install.version) === bare(release.version) ? (
                <span className="version-actions">
                  <span className="installed">Installed</span>
                  {state !== "blocked" && <PinButton pressed={pinned} onClick={() => setUpdates(entry.id, pinned ? undefined : "pinned")} />}
                </span>
              ) : state === "blocked" && !custom && release.assets.length === 0 ? (
                // A site from before blocked installs lists no files for one.
                <span className="version-blocked">Quiver won&apos;t install it.</span>
              ) : (
                <span className="version-actions">
                  <button className={state === "verified" || custom ? undefined : `quiet ${state}`} onClick={() => pick(version)}>
                    Install
                  </button>
                  {(state !== "blocked" || custom) && <PinButton pressed={false} onClick={() => pick(version, true)} />}
                </span>
              )}
              {!custom && state !== "verified" && reasons.length > 0 && <span className="version-why">{reasons.join(" ")}</span>}
            </li>
          );
        })}
      </ul>
      <button onClick={() => setVersions(null)}>Close</button>
    </section>
  );
}

/** The player's playlists the app is on (or a new one), and their own tags on it. */
export function Tags({ id }: { id: string }) {
  const { library, collections, setTags } = useLauncher();
  const tags = library.find((i) => i.id === id)?.overrides?.tags ?? [];
  const [input, setInput] = useState("");
  const has = (tag: string) => tags.some((t) => t.toLowerCase() === tag.toLowerCase());
  const toggle = (tag: string) => setTags(id, has(tag) ? tags.filter((t) => t.toLowerCase() !== tag.toLowerCase()) : [...tags, tag]);
  return (
    <section className="tags" aria-label="Playlists and tags">
      <div className="chips">
        <span className="muted">Playlists</span>
        <PlaylistChoices id={id} playlists={collections.filter((c) => !c.follows)} />
      </div>
      <form
        className="chips"
        onSubmit={(e) => {
          e.preventDefault();
          const tag = input.trim().slice(0, 50);
          if (tag && !has(tag) && tags.length < 20) setTags(id, [...tags, tag]);
          setInput("");
        }}
      >
        <span className="muted">Your tags</span>
        {tags.map((t) => (
          <button type="button" key={t} className="chip on" aria-label={`Remove tag ${t}`} onClick={() => toggle(t)}>
            {t} ×
          </button>
        ))}
        <input aria-label="Add a tag" placeholder="Add a tag" value={input} onChange={(e) => setInput(e.target.value)} />
      </form>
    </section>
  );
}
