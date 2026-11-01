/**
 * The parts of an app's page beyond Get and Play: its repository, README and
 * releases, the project's details, shortcuts, picking another version, and
 * the player's tags.
 */
import { useEffect, useLayoutEffect, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { ArrowUpRight, Ban, Check, ChevronDown, ChevronRight, CircleX, Download, ExternalLink, Github, Gitlab, History, Monitor, Package, Pin, ShieldAlert, ShieldCheck, ShieldX, ThumbsUp, TriangleAlert, X } from "lucide-react";
import { FaSteam } from "react-icons/fa6";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { ApiError, type AiUse, type Asset, type Checking, type Detail, type Entry, type HistoryRelease, type Os, type Readme as ReadmeText, type Release, type ReleaseScan, type Withdrawn } from "@quiverlauncher/api";
import { OS_NAMES, ReleaseBadge, alertTitles, fullDate, platformList, relativeTime } from "@quiverlauncher/ui";
import { Markdown } from "@quiverlauncher/ui/markdown";
import { i18n } from "@quiverlauncher/i18n";
import { pulledVerified, useLauncher, type Install, type Settings } from "./store";
import { bare, saidOf, unverifiedFile, unverifiedIn, versionLabel, versionList, withdrawnAsBlocked, type Said, type Version } from "./versions";
import { native, type ShortcutRequest } from "./native";
import { hasBuildFor, runsHere } from "./assets";
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

/** The catalog's app for one the player added from its repository, once the catalog lists it; else null. */
export function useListed(entry: Entry): Entry | null {
  const { listedAs } = useLauncher();
  const custom = isCustom(entry.id);
  const [listed, setListed] = useState<Entry | null>(null);
  useEffect(() => {
    setListed(null);
    if (!custom) return;
    let live = true;
    listedAs(entry.id).then(
      (e) => live && setListed(e),
      () => {},
    );
    return () => void (live = false);
  }, [entry.id, custom]); // eslint-disable-line react-hooks/exhaustive-deps
  return listed;
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

/** Releases the Releases tab shows at first, and how many more each time. */
const HISTORY_SHOWN = 10;
/**
 * Asked a hundred at a time: the site keeps that one address for every
 * launcher and clears it when the history changes, so it rarely reaches
 * the site's database. Any other size is worked out again each time.
 */
const HISTORY_PAGE = 100;

/**
 * Every release of the app's repository, newest first, verified or not, as
 * the website's Releases tab lists them: 10, then 10 more at a time. A site
 * that doesn't list them all yet gives its verified ones.
 */
export function useReleaseHistory(entry: Entry, on: boolean) {
  const { client } = useLauncher();
  const [all, setAll] = useState<HistoryRelease[] | undefined>();
  const [shown, setShown] = useState(HISTORY_SHOWN);
  const [next, setNext] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  function load(cursor: string | null) {
    setLoading(true);
    client
      .releaseHistory(entry.slug, HISTORY_PAGE, cursor)
      .catch((e: unknown) => {
        if (!(e instanceof ApiError && e.status === 404)) throw e;
        return client.releases(entry.slug, HISTORY_PAGE, cursor).then((page) => ({ ...page, items: page.items.map(asHistory) }));
      })
      .then(
        (page) => {
          setAll((list) => (cursor ? [...(list ?? []), ...page.items] : page.items));
          setNext(page.isDone ? null : page.nextCursor);
          setLoading(false);
        },
        () => (setAll((list) => list ?? []), setLoading(false)),
      );
  }
  useEffect(() => {
    setShown(HISTORY_SHOWN);
    if (on) load(null);
  }, [client, entry.slug, on]); // eslint-disable-line react-hooks/exhaustive-deps
  const items = all?.slice(0, shown);
  return {
    items,
    more: (all?.length ?? 0) > shown || Boolean(next),
    loading,
    loadMore: () => {
      setShown((n) => n + HISTORY_SHOWN);
      if (all && shown + HISTORY_SHOWN > all.length && next) load(next);
    },
  };
}
export type ReleaseHistory = ReturnType<typeof useReleaseHistory>;

/** A project's repository: its host's mark (named for a screen reader), then its path, onto another line only after a slash. */
export function RepositoryLink({ source }: { source: Source }) {
  const [host, Mark] = source.provider === "github" ? ["GitHub", Github] : ["GitLab", Gitlab];
  const parts = source.repository.split("/");
  return (
    <a className="repo-link" href={source.url} title={`${host}: ${source.repository}`} aria-label={`${host}: ${source.repository}`}>
      <Mark size={14} aria-hidden="true" />
      {/* Each part whole (a name's hyphens aren't where it breaks), its slash at the end of the line before. */}
      <span>
        {parts.map((part, i) => (
          <span key={i} className="repo-part">
            {part}
            {i < parts.length - 1 && "/"}
          </span>
        ))}
      </span>
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
        <h3>
          <Trans>About this project</Trans>
        </h3>
        <p className="detail-description">{entry.description}</p>
      </section>
    );
  if (!readme) return null;
  return (
    <section className="readme">
      <h3>README</h3>
      <div className="readme-body">
        <Markdown text={readme.markdown} rawBase={readme.rawBase} htmlBase={readme.htmlBase} alertTitles={alertTitles()} />
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
  // Each follows "Version 1.2 is".
  if (checking.checkEndsAt === undefined)
    return checking.needsReview
      ? t({ message: "unverified · waiting for a maintainer", comment: "Follows “Version 1.2 is”" })
      : t({ message: "unverified · being checked", comment: "Follows “Version 1.2 is”" });
  const hours = Math.ceil((checking.checkEndsAt - now) / 3_600_000);
  return hours <= 0
    ? t({ message: "unverified · verified shortly", comment: "Follows “Version 1.2 is”" })
    : plural(hours, { one: "unverified · verified in about # hour", other: "unverified · verified in about # hours" });
}

/** The newer release being checked, shown only when the app has an approved one to install meanwhile. */
export const checkingOf = (detail: Detail | null | undefined, releases: Releases) => (releases.items?.length ? detail?.checking : undefined);

/**
 * One release, in release order with the others, as on the website: a
 * verified one links its files; an unverified one stands out in orange and
 * says why, with its files under a warning; a blocked one stands out in red,
 * says why and links nothing.
 */
function ReleaseCard({
  release,
  open,
  onToggle,
  host,
  installed,
  said,
  onInstall,
  runsHere,
}: {
  /** A withdrawn one is blocked (withdrawnAsBlocked), with why. */
  release: HistoryRelease & { withdrawn?: Withdrawn };
  open: boolean;
  onToggle: () => void;
  host: string;
  /** The version installed on this computer. */
  installed: boolean;
  said?: Said;
  /** Installs this release, asking first as Change version does: `asset`, the file the player chose. Unset when it can't install now. */
  onInstall?: (asset?: string) => void;
  /** Whether this computer can install a file: those it can install it in place of opening it on GitHub. */
  runsHere: (asset: Asset) => boolean;
}) {
  const { state, scan, withdrawn, version } = release;
  const files = release.assets.length;
  // A blocked release the site lists no files for (one from before blocked installs) can't install, as in Change version.
  const install = onInstall && !(state === "blocked" && files === 0) ? onInstall : undefined;
  const hours = release.checkEndsAt !== undefined ? hoursFrom(release.checkEndsAt) : 0;
  const engines = scan?.engines ?? t({ message: "flagged", comment: "What VirusTotal said of a release's files, after “VirusTotal:”" });
  return (
    <article className={`panel release release-${state}${open ? " open" : ""}`}>
      <button type="button" className="release-summary" aria-expanded={open} onClick={onToggle}>
        <span>
          <strong>
            {release.version}
            {release.prerelease && (
              <span className="prerelease-tag">
                <Trans comment="A release its developer marked as not yet stable">Pre-release</Trans>
              </span>
            )}
          </strong>
          <small className="muted">
            {fullDate(release.releasedAt)} · {plural(files, { one: "# file", other: "# files" })}
          </small>
        </span>
        {/* Everything right of the name, together: on a narrow screen it goes under the name as one. */}
        <span className="release-side">
          <span className="release-tags">
            {said && <SaidLine said={said} />}
            {withdrawn && (
              <span className="release-tag withdrawn" title={withdrawn.reason}>
                <Ban size={13} aria-hidden="true" /> <Trans comment="A release that was taken back">Withdrawn</Trans>
              </span>
            )}
            {installed && (
              <span className="release-tag installed">
                <Check size={13} strokeWidth={3} aria-hidden="true" /> <Trans context="release" comment="This release is the one on this computer">Installed</Trans>
              </span>
            )}
          </span>
          {/* One status each: Withdrawn says it all, in place of Blocked. */}
          {!withdrawn && <ReleaseBadge state={state} />}
          <span className="release-chevron" aria-hidden="true">
            <ChevronDown size={20} />
          </span>
        </span>
      </button>
      {(state === "blocked" || (state === "unverified" && open)) && (
        <div className="release-why">
          {release.reasons.map((r) => (
            <p key={r}>{r}</p>
          ))}
          {release.checkEndsAt !== undefined && (
            <>
              <p>
                {plural(hours, {
                  one: "It's in its 48-hour waiting period and will be verified in about # hour.",
                  other: "It's in its 48-hour waiting period and will be verified in about # hours.",
                })}
              </p>
              <p className="muted">
                <Trans>Quiver waits until each file has been out for 48 hours, so a hacked or malicious update can be caught before it reaches you.</Trans>
              </p>
            </>
          )}
          {state === "blocked" && (
            <p>
              <strong>
                {installed
                  ? t`You have it installed. If you're happy to accept the risk, you can keep using it; otherwise, remove it and install a verified release instead.`
                  : t`If you installed it and are happy to accept the risk, you can keep using it; otherwise, remove it and install a verified release instead.`}
              </strong>
            </p>
          )}
          {scan?.url && (scan.verdict === "flagged" || scan.verdict === "warning") && (
            <a className="release-hold-link" href={scan.url}>
              VirusTotal: {engines}
              <ArrowUpRight size={13} aria-hidden="true" />
            </a>
          )}
        </div>
      )}
      {open && (
        <div className="release-details">
          {release.notes.trim() ? (
            <div className="release-notes">
              <Markdown text={release.notes} alertTitles={alertTitles()} />
            </div>
          ) : (
            <p className="muted release-notes">
              <Trans>No release notes provided.</Trans>
            </p>
          )}
          {state === "blocked" ? (
            <p className="release-files-note">
              {withdrawn
                ? t`This release was withdrawn, so it doesn't link its files. Installing it asks you to accept the risk first.`
                : t`Quiver blocked this release, so it doesn't link its files. Installing it asks you to accept the risk first.`}
            </p>
          ) : (
            state === "unverified" && (
              <p className="release-files-note">
                <Trans>Quiver hasn&apos;t verified these files. Installing one asks you to confirm first.</Trans>
              </p>
            )
          )}
          {install && (
            <button type="button" className={`release-install ${state}`} onClick={() => install()}>
              <Download size={16} aria-hidden="true" /> {installLabel(version)}
            </button>
          )}
          {state !== "blocked" && (
            <div className="download-list">
              {release.assets.map((a) => {
                // A file that isn't verified with its release, marked as on the website, so a player sees which one waits.
                const why = unverifiedFile(a, release);
                const changed = why === "changed" ? changedFile(a.checkEndsAt) : undefined;
                const body = (
                  <>
                    <Download size={18} />
                    <span>
                      <strong>
                        {a.os === "unknown" ? t`Platform unspecified` : (OS_NAMES[a.os] ?? a.os)} · {a.architecture}
                      </strong>
                      <small>{a.filename}</small>
                      {changed && <small className="file-why">{changed}</small>}
                    </span>
                    {changed && (
                      <span className="file-mark">
                        <ReleaseBadge state="unverified" title={changed} />
                      </span>
                    )}
                    {why === "unchecked" && (
                      <span className="file-mark unchecked">{t({ message: "Not checked by Quiver", comment: "A release's file Quiver has no checksum for, though it has for the release's other files" })}</span>
                    )}
                  </>
                );
                // One this computer runs installs, as the player chose it; any other opens on GitHub or GitLab.
                return install && runsHere(a) ? (
                  <button type="button" className="download" key={a.id} onClick={() => install(a.filename)}>
                    {body}
                    <span className="download-go">
                      <Trans comment="Button that installs this version">Install</Trans>
                    </span>
                  </button>
                ) : (
                  <a className="download" key={a.id} href={a.url}>
                    {body}
                    <ExternalLink size={14} />
                  </a>
                );
              })}
            </div>
          )}
          {state !== "verified" && release.upstreamUrl && (
            <a className="release-hold-link" href={release.upstreamUrl}>
              <Trans>
                View {version} on {host}
              </Trans>
              <ArrowUpRight size={13} aria-hidden="true" />
            </a>
          )}
        </div>
      )}
    </article>
  );
}

/** "Install v1.2", as the button that takes an app off a withdrawn release says it. */
function installLabel(release: string) {
  const version = versionLabel(release);
  return t`Install ${version}`;
}

/**
 * Why a file of a verified release isn't verified with it, as the website says it: it changed on GitHub since, and
 * when it's verified by itself, unless the site's automatic verification is paused (no `checkEndsAt`).
 */
function changedFile(checkEndsAt?: number) {
  if (checkEndsAt === undefined) return t`Changed on GitHub after Quiver verified this release.`;
  // "in 31 hours", as the player's language says it.
  const when = new Intl.RelativeTimeFormat(i18n.locale, { numeric: "auto" }).format(hoursFrom(checkEndsAt), "hour");
  return t({ message: `Changed on GitHub after Quiver verified this release. Verified by itself ${when}`, comment: "{when} reads “in 31 hours”" });
}

/**
 * Every release of the app in release order, verified or not, as on the
 * website's Releases tab. Each but the installed one installs from here, or
 * one of its files does, asking first as Change version does.
 */
export function ReleasesTab({
  entry,
  detail,
  history,
  checking,
  openVersion,
}: {
  entry: Entry;
  detail: Detail | null | undefined;
  history: ReleaseHistory;
  /** A newer release the site is still checking, said over the list. */
  checking?: Checking;
  /** A version whose notes start open too, as the one just installed (Downloads' See changelog). */
  openVersion?: string;
}) {
  const [toggled, setToggled] = useState<Set<string>>(new Set());
  const { items } = history;
  const { client, installs, jobs, catalog, config } = useLauncher();
  const install = installs[entry.id];
  // A release the player chose to install, with the file they chose, in Change version's dialog for its questions.
  const [chosen, setChosen] = useState<{ version: string; asset?: string } | null>(null);
  const job = jobs[entry.id];
  // The player's own files are never downloaded over, and one download at a time.
  const canInstall = !isLocal(entry.id) && !install?.local && !(job && !("error" in job));
  const fits = (a: Asset) => runsHere(a, config.os, config.arch);
  // What players said of each version, from the newest feedback: the first page, the one address the site keeps for every launcher.
  const [said, setSaid] = useState<Map<string, Said>>(new Map());
  useEffect(() => {
    let live = true;
    client.reviews(entry.slug, null, 10).then(
      (page) => live && setSaid(saidOf(page.items)),
      () => undefined,
    );
    return () => void (live = false);
  }, [client, entry.slug]);
  const host = detail?.project.provider === "gitlab" ? "GitLab" : "GitHub";
  // A withdrawn release is blocked, whatever the site still says of it.
  const releases = items && withdrawnAsBlocked<HistoryRelease & { withdrawn?: Withdrawn }>(items, (r) => r.version, detail?.withdrawn);
  const newestVerified = releases?.find((r) => r.state === "verified");
  // Put in the sentence by name, as it was on the page's top.
  const checkingVersion = checking?.version;
  return (
    <section className="release-section">
      {/* As on the website: a newer release the site is still checking. */}
      {checking && (
        <p className="release-checking-line">
          <ShieldAlert size={14} aria-hidden="true" />
          <span>
            <Trans>
              Version {checkingVersion} is <CheckingStatus checking={checking} />
            </Trans>
          </span>
        </p>
      )}
      {newestVerified?.prerelease && (
        <div className="prerelease-notice" role="note">
          <Trans>
            <strong>The newest verified release is a pre-release build.</strong> The project hasn&apos;t published a newer stable release with downloads, so Quiver
            Launcher installs this one. Expect bugs and unfinished features.
          </Trans>
        </div>
      )}
      {releases === undefined ? (
        <div className="more" role="status" aria-label={t`Loading releases`}>
          <span className="spinner" />
        </div>
      ) : releases.length === 0 ? (
        <div className="empty compact panel">
          <Package size={28} />
          <h3>
            <Trans>Releases are being cataloged</Trans>
          </h3>
          <p>
            <Trans>There are no releases yet. Check the upstream project for downloads.</Trans>
          </p>
        </div>
      ) : (
        <>
          <p className="muted release-intro">
            <Trans>Quiver Launcher only updates to verified releases. It installs an unverified or blocked one only if you pick it and confirm. Verified means each of a release&apos;s files has been out for 48 hours, so there was time for a bad upload to be spotted, and a Quiver maintainer checked any file VirusTotal flagged. Quiver Launcher checks every download against the file Quiver verified. If a file is replaced after that, the new one waits its own 48 hours.</Trans>
          </p>
          {releases.map((release) => {
            // The newest verified release starts open; the others fold.
            const open = (release === newestVerified || (openVersion !== undefined && bare(release.version) === bare(openVersion))) !== toggled.has(release.releaseId);
            // A fresh set each time: React may run this twice, so it mustn't change the one it's given.
            const flip = () =>
              setToggled((t) => {
                const next = new Set(t);
                if (!next.delete(release.releaseId)) next.add(release.releaseId);
                return next;
              });
            const installed = Boolean(install && !install.local && bare(install.version) === bare(release.version));
            const facts = { installed, said: said.get(bare(release.version)) };
            const onInstall = canInstall && !installed ? (asset?: string) => setChosen({ version: release.version, ...(asset ? { asset } : {}) }) : undefined;
            return <ReleaseCard key={release.releaseId} release={release} open={open} onToggle={flip} host={host} onInstall={onInstall} runsHere={fits} {...facts} />;
          })}
        </>
      )}
      {chosen && <VersionsDialog entry={catalog[entry.id] ?? entry} start={chosen} onClose={() => setChosen(null)} />}
      {history.more && (
        <button className="button secondary" disabled={history.loading} onClick={history.loadMore}>
          <Trans>Show older releases</Trans>
        </button>
      )}
    </section>
  );
}

/** A version's feedback, in its title bar: how many said it runs well, and how many had issues or found it broken. */
function SaidLine({ said }: { said: Said }) {
  // How many players said each: "3 runs well", "1 has issues", "2 broken".
  const { runs, issues, broken } = said;
  const parts: [keyof Said, string, ReactNode][] = [
    ["runs", plural(runs, { one: "# runs well", other: "# runs well" }), <ThumbsUp size={13} aria-hidden="true" />],
    ["issues", plural(issues, { one: "# has issues", other: "# have issues" }), <TriangleAlert size={13} aria-hidden="true" />],
    ["broken", plural(broken, { one: "# broken", other: "# broken" }), <CircleX size={13} aria-hidden="true" />],
  ];
  const total = runs + issues + broken;
  return (
    <span
      className="release-said"
      title={plural(total, {
        one: "From the newest player feedback: # player tested this version",
        other: "From the newest player feedback: # players tested this version",
      })}
    >
      {parts
        .filter(([key]) => said[key])
        .map(([key, words, icon]) => (
          <span key={key} className={`said-${key}`}>
            {icon} {words}
          </span>
        ))}
    </span>
  );
}

const DEVELOPER_SAYS: Record<string, MessageDescriptor> = { none: msg`No AI used`, assisted: msg`Some AI assistance`, generated: msg`Mostly AI-generated` };
/** Where the AI use row's finding came from, each a whole sentence: {date} is when it was checked. */
const AI_SOURCES: Record<AiUse["source"], MessageDescriptor> = {
  developer: msg({ message: "Based on the developer's answer, checked {date}" }),
  readme: msg({ message: "Based on the project's README, checked {date}" }),
  signals: msg({ message: "Based on repository signals, checked {date}" }),
  admin: msg({ message: "Based on a moderator, checked {date}" }),
};

/** How much AI wrote it, as the website's AI use row says it: the finding, where it came from, and what the developer says. */
function aiFindings(ai?: AiUse) {
  const label = !ai || ai.level === "none"
    ? ai?.source === "readme" || (ai?.developerAnswer === "none" && ai.answeredBy === "developer") ? t`Developer states no AI` : t`No AI use found`
    : ai.level === "assisted" ? t`AI-assisted` : t`Mostly AI-generated`;
  const answer = ai?.developerAnswer && ai.developerAnswer !== "unknown" && ai.source !== "developer" ? ai.developerAnswer : undefined;
  const says = answer && answer !== "credited" ? i18n._(DEVELOPER_SAYS[answer]) : "";
  return {
    label,
    from: ai ? i18n._({ ...AI_SOURCES[ai.source], values: { date: fullDate(ai.checkedAt) } }) : t`Not checked yet`,
    developer: answer && (answer === "credited" ? t`Developer wasn't sure` : t`Developer says: ${says}`),
  };
}

/**
 * The AI use's finding alone, and the way to the rest (AiEvidence), which shows under all the facts: down this narrow
 * column it ran a few words a line, far past the others. Not checked yet, it says so here.
 */
function AiUseSummary({ ai, evidence, onEvidence }: { ai?: AiUse; evidence: boolean; onEvidence: () => void }) {
  const { label, from } = aiFindings(ai);
  return (
    <div className="ai-use">
      <strong>{label}</strong>
      {ai ? (
        <button type="button" className="ai-evidence-toggle" aria-expanded={evidence} aria-controls="ai-evidence" onClick={onEvidence}>
          <ChevronRight size={14} aria-hidden="true" />
          <Trans>Evidence</Trans>
        </button>
      ) : (
        <span className="muted"> · {from}</span>
      )}
    </div>
  );
}

/** Where the AI use's finding came from, what the developer says, and the evidence, across the panel. */
function AiEvidence({ ai }: { ai: AiUse }) {
  const { from, developer } = aiFindings(ai);
  return (
    <div className="ai-evidence" id="ai-evidence">
      <p className="muted">{developer ? `${from} · ${developer}` : from}</p>
      {ai.evidence.length > 0 && (
        <ul>
          {ai.evidence.map((e, i) => (
            <li key={i}>{e.url ? <a href={e.url}>{e.detail}</a> : e.detail}</li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Who made it, where it runs, its latest release and its AI use, as on the website's Project details. */
/**
 * The app's facts at the top of its page's Overview: who made it, where it runs and its repository, its latest
 * release and its use of AI. It folds away to a line, and stays folded for every app once the player folds it.
 */
export function ProjectDetails({ entry, detail, source }: { entry: Entry; detail: Detail | null | undefined; source: Source | null | undefined }) {
  const { settings, setSettings } = useLauncher();
  const open = !settings.projectDetailsClosed;
  const latest = entry.verified
    ? { version: entry.verified.version, at: entry.verified.releasedAt }
    : entry.lastReleaseAt
      ? { version: entry.lastReleaseVersion, at: entry.lastReleaseAt }
      : undefined;
  const stale = latest && Date.now() - latest.at > 365 * 24 * 60 * 60 * 1000;
  const developer = detail?.entry.developer ?? entry.developer;
  const ai = detail?.project.aiUse;
  const [evidence, setEvidence] = useState(false);
  return (
    <section className="panel content-panel project-details" data-open={open || undefined} aria-label={t`Project details`}>
      <button type="button" className="eyebrow project-details-toggle" aria-expanded={open} onClick={() => setSettings({ ...settings, projectDetailsClosed: open || undefined })}>
        <Trans>PROJECT DETAILS</Trans>
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {open && (
        <dl>
          {developer && (
            <div>
              <dt>
                <Trans>Made by</Trans>
              </dt>
              <dd>{detail?.project.author || developer.name}</dd>
            </div>
          )}
          <div>
            <dt>
              <Trans>Supported platforms</Trans>
            </dt>
            <dd>{platformList(entry.supportedOS)}</dd>
          </div>
          {source && (
            <div>
              <dt>
                <Trans comment="Where an app's code is: a link to its repository">Source</Trans>
              </dt>
              <dd>
                <RepositoryLink source={source} />
              </dd>
            </div>
          )}
          <div>
            <dt>
              <Trans>Latest release</Trans>
            </dt>
            <dd>
              {latest ? (
                <span className={stale ? "release-age stale" : ""} title={fullDate(latest.at)}>
                  {latest.version ? `${latest.version} · ` : ""}
                  {relativeTime(latest.at)}
                </span>
              ) : (
                t`No releases found`
              )}
            </dd>
          </div>
          <div>
            <dt>
              <Trans>AI use</Trans>
            </dt>
            <dd>{detail === undefined ? <span className="muted">…</span> : <AiUseSummary ai={ai} evidence={evidence} onEvidence={() => setEvidence(!evidence)} />}</dd>
          </div>
        </dl>
      )}
      {open && evidence && ai && <AiEvidence ai={ai} />}
    </section>
  );
}

/** A desktop shortcut and a Steam shortcut, both starting the game directly. */
export function Shortcuts({ entry }: { entry: Entry }) {
  const { installs, config, settings } = useLauncher();
  const install = installs[entry.id];
  const [message, setMessage] = useState<{ text: string; error?: boolean } | null>(null);
  if (!install || !shortcutsOn(config.os)) return null;
  const request = shortcutRequest(entry, install, settings);
  const run = (action: Promise<string>) =>
    action.then(
      (text) => setMessage({ text }),
      (e) => setMessage({ text: String(e), error: true }),
    );
  return (
    <div className="shortcuts">
      <button className="panel-action" onClick={() => run(native.createShortcut(request))}>
        <Monitor size={16} aria-hidden="true" /> <Trans>Desktop shortcut</Trans>
      </button>
      <button className="panel-action" onClick={() => run(native.addToSteam(request))}>
        <FaSteam size={15} aria-hidden="true" /> <Trans>Add to Steam</Trans>
      </button>
      {message && (
        <p className={message.error ? "job-error" : "muted"} role="status">
          {message.text}
        </p>
      )}
    </div>
  );
}

/** The title of a form opened in the app page's side panel, and its close button. */
export function PanelFormHead({ title, onClose }: { title: string; onClose: () => void }) {
  return (
    <div className="panel-form-head">
      <h4>{title}</h4>
      <button type="button" className="panel-close" aria-label={t({ message: `Close ${title}`, comment: "Close button of a form; {title} is the form's name" })} title={t`Close`} onClick={onClose}>
        <X size={16} aria-hidden="true" />
      </button>
    </div>
  );
}

/** Desktop and Steam shortcuts are made on Windows and Linux. */
export const shortcutsOn = (os: string) => os === "windows" || os === "linux";

/** Android keeps an app's files to itself: there's no folder to open, or program to choose. */
export const foldersOn = (os: string) => os !== "android" && os !== "ios";

/** What a desktop or Steam shortcut to an installed app starts, and its pictures; `settings` has the runner for Windows programs on Linux. */
export function shortcutRequest(entry: Entry, install: Install, settings: Settings): ShortcutRequest {
  const art = entry.libraryArt ?? {};
  return {
    name: entry.projectName,
    folder: install.folder,
    dir: install.dir,
    preferred: install.executables,
    wine: Boolean(install.wine),
    program: install.program,
    launch: { ...install.launch, windowsRunner: settings.windowsRunner },
    art: { icon: entry.artwork ?? art.logo, header: art.header, capsule: art.capsule, hero: art.hero, logo: art.logo },
  };
}

/** About how many hours from now, at least one, for a release's wait. */
export const hoursFrom = (at: number) => Math.max(1, Math.ceil((at - Date.now()) / 3_600_000));

/** What VirusTotal said, for a player deciding. */
export function scanText(scan: ReleaseScan) {
  // Each follows "VirusTotal:"; engines reads "1 of 70 engines".
  const { engines } = scan;
  if (scan.verdict === "clean") return t({ message: "no antivirus engine flags its files.", comment: "Follows “VirusTotal:”" });
  if (scan.verdict === "warning")
    return engines
      ? t({ message: `${engines} flag one of its files. A lone detection is often a false alarm.`, comment: "Follows “VirusTotal:”; {engines} reads “1 of 70 engines”" })
      : t({ message: "an engine flags one of its files. A lone detection is often a false alarm.", comment: "Follows “VirusTotal:”" });
  if (scan.verdict === "flagged")
    return engines
      ? t({ message: `${engines} flag one of its files.`, comment: "Follows “VirusTotal:”; {engines} reads “3 of 70 engines”" })
      : t({ message: "several engines flag one of its files.", comment: "Follows “VirusTotal:”" });
  if (scan.verdict === "pending") return t({ message: "still scanning its files.", comment: "Follows “VirusTotal:”" });
  return t({ message: "couldn't scan every file.", comment: "Follows “VirusTotal:”" });
}

/** Keeps the app on a version: pressed while it's pinned there. Its words show for keyboards and controllers. */
function PinButton({ pressed, onClick }: { pressed: boolean; onClick: () => void }) {
  return (
    <button type="button" className="pin-button" aria-pressed={pressed} title={pressed ? t`Pinned: click to unpin` : t`Always stay on this version`} onClick={onClick}>
      <Pin size={14} aria-hidden="true" />
      <span className="pin-label">
        <Trans>Always stay on this version</Trans>
      </span>
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
export function Versions({
  entry,
  source,
  onClose,
  start,
  getting,
}: {
  entry: Entry;
  source: Source | null | undefined;
  /** Set, the list opens at once in a dialog of its own, which this closes. */
  onClose?: () => void;
  /** A release the player chose already, on the Releases tab: it installs as if picked from the list, which shows only when it isn't there. `asset` is the file they chose. */
  start?: { version: string; asset?: string };
  /** Set, it's Get's own choices, from beside Get: what Get would install first, and the build to install where there's a choice. */
  getting?: boolean;
}) {
  const { client, github, gitlab, installs, get, jobs, setUpdates, catalog, config } = useLauncher();
  // On Linux, an app with a build for each: Windows' runs through Wine, which plays some apps better.
  const builds: Os[] = getting && config.os === "linux" && entry.supportedOS.includes("linux") && entry.supportedOS.includes("windows") ? ["linux", "windows"] : [];
  const [platform, setPlatform] = useState<Os | undefined>(builds[0]);
  const lacks = (release: Release) => Boolean(platform) && release.assets.length > 0 && !hasBuildFor(release.assets, config.os, config.arch, platform!);
  const [versions, setVersions] = useState<Version[] | string | null>(null);
  // The release from `start`, once it's found in the list: cancelling its question closes the dialog, as there's no list behind it.
  const [started, setStarted] = useState<Version | null>(null);
  // An unverified release the player chose, waiting for them to confirm; `pin` keeps the app on it after.
  const [asking, setAsking] = useState<{ version: Version; pin: boolean } | null>(null);
  // A blocked release the player chose: installed only once they tick that they understand.
  const [insisting, setInsisting] = useState<Version | null>(null);
  const [understood, setUnderstood] = useState(false);
  const install = installs[entry.id];
  const pinned = install?.updates === "pinned";
  // A custom app has nothing Quiver checks, so nothing to mark or warn about.
  const custom = isCustom(entry.id);
  const app = entry.projectName;
  // As a dialog, the list loads as soon as it knows where the app's releases are.
  useEffect(() => {
    if (onClose && source !== undefined && versions === null) void load();
  }, [source]); // eslint-disable-line react-hooks/exhaustive-deps
  // Before it paints: the list only flashes by otherwise.
  useLayoutEffect(() => {
    if (!start || started || !Array.isArray(versions)) return;
    const chosen = versions.find((v) => bare(v.release.version) === bare(start.version));
    if (!chosen) return;
    setStarted(chosen);
    pick(chosen);
  }, [versions]); // eslint-disable-line react-hooks/exhaustive-deps
  if (source === undefined || jobs[entry.id]) return null;
  async function load() {
    setVersions("loading");
    const upstream = async (count = 50) =>
      !source ? [] : source.provider === "github" ? github.releases(source.repository, count) : gitlab.releases(source.repository, count);
    const missing = (e: unknown) => {
      if (e instanceof ApiError && e.status === 404) return null;
      throw e;
    };
    try {
      // A withdrawn version is blocked here too: installed only when the player insists.
      const pulled = (list: Version[]) => withdrawnAsBlocked(list, (v) => v.release.version, catalog[entry.id]?.withdrawn);
      if (custom) return setVersions(versionList([], [], await upstream()));
      // The site's history marks every release of the repository itself, at the
      // one address Cloudflare keeps for every launcher. GitHub or GitLab only
      // adds the newest few, which the site may not have seen yet: GitHub
      // allows a network only 60 requests an hour, so the list doesn't wait on it.
      const [checked, history, newest] = await Promise.all([
        client.releases(entry.slug, 100).then((p) => p.items),
        client.releaseHistory(entry.slug, 100).then((p) => p.items, missing),
        upstream(3).catch((): Release[] => []),
      ]);
      if (history) return setVersions(pulled(versionList(checked, unverifiedIn(history), newest)));
      // A site that doesn't list the history yet: the releases it knows, and the rest from GitHub or GitLab.
      const [unverified, others] = await Promise.all([client.unverifiedReleases(entry.slug).catch(missing), upstream()]);
      setVersions(pulled(versionList(checked, unverified, others)));
    } catch (e) {
      setVersions(e instanceof Error ? e.message : String(e));
    }
  }
  function installVersion(version: Version, pin: boolean) {
    setVersions(null);
    const blocked = !custom && version.state === "blocked" ? (version.reasons[0] ?? "Quiver blocked this release.") : undefined;
    // By its version: a verified one installs from the effect that found it, before `started` is set.
    const asset = start && bare(version.release.version) === bare(start.version) ? start.asset : undefined;
    void get(entry, version.release, { pin, unverified: !custom && version.state !== "verified", ...(blocked ? { blocked } : {}), ...(asset ? { asset } : {}), ...(platform ? { platform } : {}) });
    onClose?.();
  }
  function pick(version: Version, pin = false) {
    if (version.state === "blocked" && !custom) return (setUnderstood(false), setInsisting(version));
    if (version.state === "unverified" && !custom) return setAsking({ version, pin });
    installVersion(version, pin);
  }
  if (insisting) {
    const close = () => (insisting === started ? onClose?.() : setInsisting(null));
    const { release, reasons, scan, withdrawn } = insisting;
    const keep = install && !install.local ? versionLabel(install.version) : undefined;
    // Withdrawn or blocked, the same insisting: only the words differ.
    const { version } = release;
    return (
      <Dialog label={withdrawn ? t`Install a withdrawn release` : t`Install a blocked release`} onClose={close}>
        <div className="detail-body unverified-prompt blocked-prompt">
          <h2>
            <ShieldX size={18} /> {withdrawn ? t`Install a withdrawn release?` : t`Install a blocked release?`}
          </h2>
          <p>
            {withdrawn ? t`${version} was withdrawn, so it's not meant to be installed.` : t`Quiver blocked ${version}, so it's not meant to be installed.`}{" "}
            <Trans>Quiver Launcher never updates to it. Install it only if you know why you need this exact version and accept the risk.</Trans>
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
                  <Trans>See the report</Trans>
                </a>
              )}
            </p>
          )}
          <p>
            <Trans>It installs the files Quiver saw when it came out, and refuses any that changed since.</Trans>
          </p>
          <label className="blocked-consent">
            <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />{" "}
            {withdrawn ? t`I understand this release was withdrawn, and I accept the risk` : t`I understand Quiver blocked this release`}
          </label>
          <div className="row">
            {/* Keeping what they have is the easy choice: it's first, and where a controller starts. */}
            <button className="primary" autoFocus onClick={close}>
              {keep ? t`Keep ${keep}` : t`Cancel`}
            </button>
            <button
              className="danger"
              disabled={!understood}
              onClick={() => {
                close();
                installVersion(insisting, false);
              }}
            >
              <Trans>Install {version} anyway</Trans>
            </button>
          </div>
        </div>
      </Dialog>
    );
  }
  if (asking) {
    const close = () => (asking.version === started ? onClose?.() : setAsking(null));
    const { version, pin } = asking;
    const host = source?.provider === "gitlab" ? "GitLab" : "GitHub";
    const waits = version.checkEndsAt !== undefined;
    const hours = waits ? hoursFrom(version.checkEndsAt!) : 0;
    const release = version.release.version;
    return (
      <Dialog label={t`Install a release before it's verified`} onClose={close}>
        <div className="detail-body unverified-prompt">
          <h2>
            <ShieldAlert size={18} /> <Trans>Install {release} before it&apos;s verified?</Trans>
          </h2>
          <p>
            {version.known
              ? t`Quiver hasn't verified this release. It installs the files Quiver saw when it came out, and refuses any that changed since.`
              : t`Quiver hasn't checked this release's files. It downloads straight from ${host}, as the developer published it.`}
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
                  <Trans>See the report</Trans>
                </a>
              )}
            </p>
          )}
          {waits && (
            <p className="wait-line">
              <ShieldCheck size={14} aria-hidden="true" />{" "}
              {plural(hours, {
                one: "If you wait, it's verified in about # hour and offered as an update then.",
                other: "If you wait, it's verified in about # hours and offered as an update then.",
              })}
            </p>
          )}
          {pin && (
            <p>
              <Trans>{app} then stays on this version until you unpin it.</Trans>
            </p>
          )}
          <div className="row">
            {/* Waiting is the easy choice: it's first, and where a controller starts. */}
            <button className="primary" autoFocus onClick={close}>
              {waits ? t({ message: "Wait", comment: "Button: waits for the release to be verified instead of installing it now" }) : t`Cancel`}
            </button>
            <button
              onClick={() => {
                close();
                installVersion(version, pin);
              }}
            >
              <Trans>Install anyway</Trans>
            </button>
          </div>
        </div>
      </Dialog>
    );
  }
  // Get's own choices: the build, then what Get installs of it (its newest verified release), over the other versions.
  const newest = entry.verified && !pulledVerified(catalog[entry.id] ?? entry) ? entry.verified.version : undefined;
  const newestRelease = Array.isArray(versions) ? versions.find((v) => newest && bare(v.release.version) === bare(newest))?.release : undefined;
  const buildName = (os: Os) => (os === "windows" ? t`Windows (Wine)` : (OS_NAMES[os] ?? os));
  const build = platform && buildName(platform);
  const missing = build && t`No ${build} build`;
  const newestLabel = versionLabel(newest);
  const head = getting && (
    <div className="get-choices">
      <PanelFormHead title={t`Get ${app}`} onClose={onClose!} />
      {builds.length > 0 && (
        <div className="segmented" role="group" aria-label={t({ message: "Build", comment: "Which platform's build of an app to install, as Linux or Windows through Wine" })}>
          {builds.map((os) => (
            <button key={os} type="button" aria-pressed={os === platform} onClick={() => setPlatform(os)}>
              {buildName(os)}
            </button>
          ))}
        </div>
      )}
      {newestRelease && lacks(newestRelease) ? (
        <p className="muted">{missing}</p>
      ) : (
        <button
          className="primary"
          autoFocus
          onClick={() => {
            onClose?.();
            void get(entry, undefined, platform ? { platform } : undefined);
          }}
        >
          <Download size={15} aria-hidden="true" /> {newest ? t`Get ${newestLabel}` : t({ message: "Get", comment: "Button that installs an app" })}
        </button>
      )}
    </div>
  );
  // As a dialog: the list, or how it's loading, in one.
  const shown = (body: ReactNode) =>
    onClose ? (
      <Dialog label={getting ? t`Get ${app}` : t`Versions`} onClose={onClose}>
        <div className="detail-body versions-dialog">
          {head}
          {body}
        </div>
      </Dialog>
    ) : (
      body
    );
  if (versions === null)
    return onClose ? null : (
      <button className="panel-action opens" onClick={load}>
        <History size={16} aria-hidden="true" /> <Trans>Change version</Trans>
      </button>
    );
  if (typeof versions === "string")
    return shown(versions === "loading" ? <span className="spinner" /> : <p className="job-error">{versions}</p>);
  return shown(
    <section className="panel-form versions" aria-label={t`Versions`}>
      {getting ? (
        <h3 className="versions-heading">
          <Trans comment="Over the list of an app's versions, under Get's own button for its newest">Other versions</Trans>
        </h3>
      ) : (
        <PanelFormHead title={onClose ? t`${app} versions` : t`Versions`} onClose={onClose ?? (() => setVersions(null))} />
      )}
      {versions.length === 0 && (
        <p className="muted">
          <Trans>No releases found.</Trans>
        </p>
      )}
      <ul>
        {versions.map((version) => {
          const { release, state, reasons } = version;
          // A site from before blocked installs lists no files for one.
          const unavailable = !custom && state === "blocked" && release.assets.length === 0;
          const why =
            custom || state !== "blocked" ? "" : [...(version.withdrawn ? [t({ message: "Withdrawn:", comment: "Before why a release was withdrawn" })] : []), ...reasons, ...(unavailable ? [t`Quiver won't install it.`] : [])].join(" ");
          // One row each: the badge as its icon (its word for screen readers and on hover), the version with when under it, its buttons, and why.
          return (
            <li key={release.id + release.version} className={`version ${custom ? "verified" : state}`}>
              {!custom && <ReleaseBadge state={state} title={reasons.join(" ") || undefined} />}
              <strong>{release.version}</strong>
              {(release.releasedAt || release.prerelease) && (
                <span className="version-date muted">
                  {release.releasedAt ? <span>{fullDate(release.releasedAt)}</span> : null}
                  {release.prerelease && (
              <span className="prerelease-tag">
                <Trans comment="A release its developer marked as not yet stable">Pre-release</Trans>
              </span>
            )}
                </span>
              )}
              {install && bare(install.version) === bare(release.version) ? (
                <span className="version-actions">
                  <span className="installed">
                    <Trans context="release" comment="This version is the one on this computer">Installed</Trans>
                  </span>
                  {state !== "blocked" && <PinButton pressed={pinned} onClick={() => setUpdates(entry.id, pinned ? undefined : "pinned")} />}
                </span>
              ) : unavailable ? null : lacks(release) ? (
                <span className="version-actions muted">{missing}</span>
              ) : (
                <span className="version-actions">
                  <button className={state === "verified" || custom ? undefined : `quiet ${state}`} onClick={() => pick(version)}>
                    <Trans comment="Button that installs this version">Install</Trans>
                  </button>
                  {(state !== "blocked" || custom) && <PinButton pressed={false} onClick={() => pick(version, true)} />}
                </span>
              )}
              {why && <span className="version-why">{why}</span>}
            </li>
          );
        })}
      </ul>
    </section>,
  );
}

/**
 * The app's versions in a dialog of their own, from a library card's Install button or its menu's Change version;
 * from the Releases tab, the one release the player chose (`start`).
 */
export function VersionsDialog({ entry, onClose, start, getting }: { entry: Entry; onClose: () => void; start?: { version: string; asset?: string }; getting?: boolean }) {
  const { source } = useAppDetail(entry);
  // Over the page, not in the card, which a lifted (transformed) card would hold it inside.
  return createPortal(<Versions entry={entry} source={source} onClose={onClose} start={start} getting={getting} />, document.body);
}

/**
 * The consoles a library app is for, as the library groups and filters it:
 * the catalog's (or its game's, for an app the player added), unless the
 * player picked their own. The catalog's consoles by maker, to pick from.
 */
function useConsolePicks(id: string) {
  const { library, catalog, consoles, setConsoles } = useLauncher();
  const own = library.find((i) => i.id === id)?.overrides?.consoles;
  const chosen = own ?? catalog[id]?.consoles ?? [];
  const brands = [...new Set(consoles.map((c) => c.brand))].map((brand) => ({
    brand,
    label: brand === "OtherPlatforms" ? t`Other platforms` : brand,
    consoles: consoles.filter((c) => c.brand === brand),
  }));
  return {
    chosen,
    brands,
    /** Picked by the player rather than the catalog's. */
    own: Boolean(own),
    toggle: (console: string) => setConsoles(id, chosen.includes(console) ? chosen.filter((c) => c !== console) : [...chosen, console]),
    reset: () => setConsoles(id, undefined),
  };
}

/** The consoles the app is for, to add to or take away from; the library's sections and filters go by them. */
export function Consoles({ id }: { id: string }) {
  const { consoleNames } = useLauncher();
  const { chosen, brands, own, toggle, reset } = useConsolePicks(id);
  return (
    <section className="tags" aria-label={t`Consoles`}>
      <div className="tag-field">
        <span className="field-label">
          <Trans>Consoles</Trans>
        </span>
        <div className="chips">
          {chosen.map((c) => {
            const name = consoleNames[c] ?? c.toUpperCase();
            return (
              <button type="button" key={c} className="chip on" aria-label={t`Remove console ${name}`} onClick={() => toggle(c)}>
                {name} ×
              </button>
            );
          })}
          {/* Picking one adds it; the choice goes back to the prompt. */}
          <select className="add-console" aria-label={t`Add a console`} value="" onChange={(e) => e.target.value && toggle(e.target.value)}>
            <option value="">{t`Add a console…`}</option>
            {brands.map(({ brand, label, consoles }) => (
              <optgroup key={brand} label={label}>
                {consoles
                  .filter((c) => !chosen.includes(c.id))
                  .map((c) => (
                    <option key={c.id} value={c.id}>
                      {consoleNames[c.id] ?? c.name}
                    </option>
                  ))}
              </optgroup>
            ))}
          </select>
        </div>
        {own && (
          <button type="button" className="link-button" onClick={reset}>
            <Trans>Use the catalog&apos;s consoles</Trans>
          </button>
        )}
      </div>
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
    <section className="tags" aria-label={t`Playlists and tags`}>
      <div className="tag-field">
        <span className="field-label">
          <Trans>Playlists</Trans>
        </span>
        <div className="chips">
          <PlaylistChoices id={id} playlists={collections.filter((c) => !c.follows)} />
        </div>
      </div>
      <form
        className="tag-field"
        onSubmit={(e) => {
          e.preventDefault();
          const tag = input.trim().slice(0, 50);
          if (tag && !has(tag) && tags.length < 20) setTags(id, [...tags, tag]);
          setInput("");
        }}
      >
        <span className="field-label">
          <Trans>Your tags</Trans>
        </span>
        <div className="chips">
          {tags.map((tag) => (
            <button type="button" key={tag} className="chip on" aria-label={t`Remove tag ${tag}`} onClick={() => toggle(tag)}>
              {tag} ×
            </button>
          ))}
          <input aria-label={t`Add a tag`} placeholder={t`Add a tag…`} value={input} onChange={(e) => setInput(e.target.value)} />
        </div>
      </form>
    </section>
  );
}
