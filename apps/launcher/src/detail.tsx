/**
 * The parts of an app's page beyond Get and Play: its repository and README,
 * shortcuts, picking another version, and the player's tags.
 */
import { useEffect, useState } from "react";
import { ExternalLink, Monitor, ShieldCheck } from "lucide-react";
import type { Entry, Readme as ReadmeText, Release } from "@quiver/api";
import { Markdown, fullDate } from "@quiver/ui";
import { useLauncher } from "./store";
import { native } from "./native";
import { isCustom } from "./custom";

export type Source = { provider: "github" | "gitlab"; repository: string; url: string };

const sourceOf = (provider: string, repository?: string): Source | null =>
  repository && (provider === "github" || provider === "gitlab")
    ? { provider, repository, url: `https://${provider}.com/${repository}` }
    : null;

/** Where the app's code lives; undefined while loading, null if it isn't on GitHub or GitLab. */
export function useSource(entry: Entry): Source | null | undefined {
  const { client, library } = useLauncher();
  const custom = library.find((i) => i.id === entry.id)?.custom;
  const [source, setSource] = useState<Source | null | undefined>(() =>
    custom ? sourceOf(custom.provider, custom.repository) : isCustom(entry.id) ? sourceOf("github", entry.id.slice(7)) : undefined,
  );
  useEffect(() => {
    if (isCustom(entry.id)) return;
    let live = true;
    client.app(entry.slug).then(
      (d) => live && setSource(sourceOf(d.project.provider, d.project.repository)),
      () => live && setSource(null),
    );
    return () => void (live = false);
  }, [client, entry.id, entry.slug]);
  return source;
}

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
    if (source === undefined) return;
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

/** The player's own tags on an app, and the hand-picked collections it's in. */
export function Tags({ id }: { id: string }) {
  const { library, collections, setTags } = useLauncher();
  const tags = library.find((i) => i.id === id)?.overrides?.tags ?? [];
  const [input, setInput] = useState("");
  const has = (tag: string) => tags.some((t) => t.toLowerCase() === tag.toLowerCase());
  const toggle = (tag: string) => setTags(id, has(tag) ? tags.filter((t) => t.toLowerCase() !== tag.toLowerCase()) : [...tags, tag]);
  const picked = collections.filter((c) => c.tags.length === 1 && !c.consoles.length && !c.installed);
  return (
    <section className="tags" aria-label="Collections and tags">
      {picked.length > 0 && (
        <div className="chips">
          <span className="muted">Collections</span>
          {picked.map((c) => (
            <button key={c.key} className={`chip${has(c.tags[0]) ? " on" : ""}`} aria-pressed={has(c.tags[0])} onClick={() => toggle(c.tags[0])}>
              {c.name}
            </button>
          ))}
        </div>
      )}
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
