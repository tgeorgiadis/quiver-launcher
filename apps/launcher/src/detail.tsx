/**
 * The parts of an app's page beyond Get and Play: its repository, README and
 * releases, the project's details, shortcuts, picking another version, and
 * the player's tags.
 */
import { useEffect, useState } from "react";
import { ArrowUpRight, ChevronDown, Download, ExternalLink, Monitor, Package, ShieldAlert, ShieldCheck } from "lucide-react";
import type { AiUse, Checking, Detail, Entry, Readme as ReadmeText, Release } from "@quiver/api";
import { Markdown, OS_NAMES, fullDate, relativeTime } from "@quiver/ui";
import { useLauncher } from "./store";
import { native } from "./native";
import { isCustom, isLocal } from "./custom";
import { ShelfChoices } from "./library";

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

/** Where a newer release stands while the site checks it, as the website words it; kept current each minute. */
export function CheckingStatus({ checking, short = false }: { checking: Checking; short?: boolean }) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 60_000);
    return () => clearInterval(timer);
  }, []);
  if (checking.checkEndsAt === undefined) return checking.needsReview ? "waiting for a maintainer's review." : "being checked.";
  const hours = Math.ceil((checking.checkEndsAt - now) / 3_600_000);
  const ready = hours <= 0 ? "ready shortly" : `ready in about ${hours} ${hours === 1 ? "hour" : "hours"}`;
  return short ? `being checked · ${ready}` : `being checked, ${ready}.`;
}

/** The newer release being checked, shown only when the app has an approved one to install meanwhile. */
export const checkingOf = (detail: Detail | null | undefined, releases: Releases) => (releases.items?.length ? detail?.checking : undefined);

/** The releases the site approved, as on the website's Releases tab; another version installs from On this computer. */
export function ReleasesTab({ detail, releases }: { detail: Detail | null | undefined; releases: Releases }) {
  const [toggled, setToggled] = useState<Set<string>>(new Set());
  const { items } = releases;
  const checking = checkingOf(detail, releases);
  return (
    <section className="release-section">
      {detail?.withdrawn.map((w) => (
        <div key={w.version + w.at} className="release-withdrawn-notice" role="note">
          <ShieldAlert size={18} aria-hidden="true" />
          <p>
            <strong>Version {w.version} was withdrawn</strong> on {fullDate(w.at)}: {/[.!?]$/.test(w.reason) ? w.reason : `${w.reason}.`} If you
            installed it, remove it and install a version listed here instead.
          </p>
        </div>
      ))}
      {items === undefined ? (
        <div className="more" role="status" aria-label="Loading releases">
          <span className="spinner" />
        </div>
      ) : items.length === 0 ? (
        <div className="empty compact panel">
          <Package size={28} />
          <h3>Releases are being cataloged</h3>
          <p>There are no approved builds yet. Check the upstream project for downloads.</p>
        </div>
      ) : (
        <>
          {checking && (
            <div className="release-hold-notice" role="note">
              <ShieldCheck size={18} aria-hidden="true" />
              <div>
                <p>
                  <strong>
                    Version {checking.version} is out and <CheckingStatus checking={checking} />
                  </strong>{" "}
                  Quiver checks new releases before Quiver Launcher offers them, so a compromised or malicious update can't reach you straight away. Until
                  then, Quiver Launcher installs {items[0].version}.
                </p>
                {checking.checkEndsAt === undefined && checking.reasons.length > 0 && <p>{checking.reasons.join(" ")}</p>}
                {checking.upstreamUrl && (
                  <a className="release-hold-link" href={checking.upstreamUrl}>
                    View {checking.version} on {detail?.project.provider === "gitlab" ? "GitLab" : "GitHub"}
                    <ArrowUpRight size={13} aria-hidden="true" />
                  </a>
                )}
              </div>
            </div>
          )}
          {items[0].prerelease && (
            <div className="prerelease-notice" role="note">
              <strong>This is a pre-release build.</strong> The project hasn't published a newer stable release with downloads, so Quiver Launcher installs this one. Expect bugs and unfinished features.
            </div>
          )}
          {items.map((release, i) => {
            const open = (i === 0) !== toggled.has(release.id);
            const flip = () => setToggled((t) => (t.delete(release.id) ? new Set(t) : new Set(t).add(release.id)));
            return (
              <article key={release.id} className={`panel release${open ? " open" : ""}`}>
                <button type="button" className="release-summary" aria-expanded={open} onClick={flip}>
                  <span>
                    <strong>
                      {release.version}
                      {release.prerelease && <span className="prerelease-tag">Pre-release</span>}
                    </strong>
                    <small className="muted">
                      {fullDate(release.releasedAt)} · {release.assets.length} {release.assets.length === 1 ? "file" : "files"}
                    </small>
                  </span>
                  <ChevronDown size={16} />
                </button>
                {open && (
                  <div className="release-details">
                    {release.notes?.trim() ? (
                      <div className="release-notes">
                        <Markdown text={release.notes} />
                      </div>
                    ) : (
                      <p className="muted release-notes">No release notes provided.</p>
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
                  </div>
                )}
              </article>
            );
          })}
        </>
      )}
      {releases.more && (
        <button className="button secondary" disabled={releases.loading} onClick={releases.loadMore}>
          Show older releases
        </button>
      )}
    </section>
  );
}

const PLATFORMS = ["windows", "linux", "macos", "android", "ios"] as const;
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
  const platforms = PLATFORMS.filter((p) => entry.supportedOS.includes(p)).map((p) => OS_NAMES[p]);
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
        <dd>{platforms.length ? platforms.join(", ") : "Not confirmed yet"}</dd>
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

type Version = { release: Release; verified: boolean };
const bare = (v: string) => v.trim().replace(/^v/i, "");

/**
 * Every release on GitHub or GitLab, marking the ones quiverlauncher.com
 * verified. A verified one installs from the site's checked files.
 */
export function Versions({ entry, source }: { entry: Entry; source: Source | null | undefined }) {
  const { client, github, gitlab, installs, get, jobs } = useLauncher();
  const [versions, setVersions] = useState<Version[] | string | null>(null);
  const install = installs[entry.id];
  if (source === undefined || jobs[entry.id]) return null;
  async function load() {
    setVersions("loading");
    try {
      const [checked, upstream] = await Promise.all([
        isCustom(entry.id) ? [] : client.releases(entry.slug, 100).then((p) => p.items),
        !source ? [] : source.provider === "github" ? github.releases(source.repository, 50) : gitlab.releases(source.repository, 50),
      ]);
      const verified = new Map(checked.map((r) => [bare(r.version), r]));
      const list: Version[] = upstream.map((r) => {
        const mine = verified.get(bare(r.version));
        verified.delete(bare(r.version));
        return mine ? { release: mine, verified: true } : { release: r, verified: false };
      });
      list.push(...[...verified.values()].map((release) => ({ release, verified: true })));
      setVersions(list.sort((a, b) => b.release.releasedAt - a.release.releasedAt));
    } catch (e) {
      setVersions(e instanceof Error ? e.message : String(e));
    }
  }
  if (versions === null) return <button onClick={load}>Change version</button>;
  if (typeof versions === "string")
    return versions === "loading" ? <span className="spinner" /> : <p className="job-error">{versions}</p>;
  return (
    <section className="versions" aria-label="Versions">
      <h3>Versions</h3>
      {versions.length === 0 && <p className="muted">No releases found.</p>}
      <ul>
        {versions.map(({ release, verified }) => (
          <li key={release.id + release.version}>
            <strong>{release.version}</strong>
            <span className="muted">{release.releasedAt ? fullDate(release.releasedAt) : ""}</span>
            {verified ? (
              <span className="verified" title="Quiver checked this release's files">
                <ShieldCheck size={13} /> Verified
              </span>
            ) : (
              <span className="muted" title="Quiver hasn't checked this release">
                Not verified
              </span>
            )}
            {release.prerelease && <span className="muted">Pre-release</span>}
            {install && bare(install.version) === bare(release.version) ? (
              <span className="installed">Installed</span>
            ) : (
              <button onClick={() => (setVersions(null), void get(entry, release))}>Install</button>
            )}
          </li>
        ))}
      </ul>
      <button onClick={() => setVersions(null)}>Close</button>
    </section>
  );
}

/** The player's shelves the app is on (or a new one), and their own tags on it. */
export function Tags({ id }: { id: string }) {
  const { library, collections, setTags } = useLauncher();
  const tags = library.find((i) => i.id === id)?.overrides?.tags ?? [];
  const [input, setInput] = useState("");
  const has = (tag: string) => tags.some((t) => t.toLowerCase() === tag.toLowerCase());
  const toggle = (tag: string) => setTags(id, has(tag) ? tags.filter((t) => t.toLowerCase() !== tag.toLowerCase()) : [...tags, tag]);
  return (
    <section className="tags" aria-label="Shelves and tags">
      <div className="chips">
        <span className="muted">Shelves</span>
        <ShelfChoices id={id} shelves={collections.filter((c) => !c.follows)} />
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
