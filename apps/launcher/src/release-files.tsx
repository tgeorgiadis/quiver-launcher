/**
 * The player's release files filter for an app, as Quiver Launcher 3's release asset filter: which of a release's
 * files Quiver installs it from, for a repository that ships several apps or builds. Shown against its newest
 * release's files, so the player sees what it keeps before saving.
 */
import { useEffect, useState } from "react";
import { Check, FileSearch, X } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Asset, Entry, Release } from "@quiverlauncher/api";
import { AUXILIARY, parseFileFilter, wantedFiles } from "./assets";
import { PanelFormHead, type Source } from "./detail";
import { isCustom } from "./custom";
import { useLauncher } from "./store";

/** The newest release's files, auxiliary ones (checksums, sources) aside: those a maintainer didn't tick too. */
function useNewestFiles(entry: Entry, source: Source | null | undefined, on: boolean) {
  const { client, providers } = useLauncher();
  const [files, setFiles] = useState<{ version: string; assets: Asset[] } | null | undefined>();
  useEffect(() => {
    if (!on || files !== undefined) return;
    const newest: Promise<Release | undefined> = isCustom(entry.id)
      ? source
        ? providers.releases(source, 5).then((list) => list.find((r) => !r.prerelease) ?? list[0])
        : Promise.resolve(undefined)
      : client.releases(entry.slug, 1, null, true).then((p) => p.items[0]);
    newest.then(
      (r) => setFiles(r ? { version: r.version, assets: [...r.assets, ...(r.otherAssets ?? [])].filter((a) => !AUXILIARY.test(a.filename)) } : null),
      () => setFiles(null),
    );
  }, [on, source]); // eslint-disable-line react-hooks/exhaustive-deps
  return files;
}

export function ReleaseFiles({ entry, source }: { entry: Entry; source: Source | null | undefined }) {
  const { library, setReleaseFiles } = useLauncher();
  const saved = library.find((i) => i.id === entry.id)?.overrides?.releaseFiles ?? "";
  const [open, setOpen] = useState(false);
  const [text, setText] = useState(saved);
  const files = useNewestFiles(entry, source, open);
  if (!open)
    return (
      <button className="panel-action opens" onClick={() => (setText(saved), setOpen(true))}>
        <FileSearch size={16} aria-hidden="true" /> <Trans comment="Button: which of a release's files the app installs from">Release files</Trans>
        {saved && <span className="release-files-current">{saved}</span>}
      </button>
    );
  const { bad } = parseFileFilter(text);
  const invalid = bad.join(", ");
  const kept = files ? new Set(wantedFiles(files.assets, text).map((a) => a.id)) : new Set<string>();
  const none = Boolean(files?.assets.length) && kept.size === 0;
  const version = files?.version ?? "";
  return (
    <form
      className="panel-form release-files"
      aria-label={t`Release files`}
      onSubmit={(e) => {
        e.preventDefault();
        if (bad.length) return;
        setReleaseFiles(entry.id, text);
        setOpen(false);
      }}
    >
      <PanelFormHead title={t`Release files`} onClose={() => setOpen(false)} />
      <p className="muted small">
        <Trans>
          Quiver installs and updates this app only from the release files that match. Useful when one repository ships several apps or builds.
        </Trans>
      </p>
      <label className="panel-field">
        <span className="field-label">
          <Trans>Filter</Trans>
        </span>
        <input
          aria-label={t`Filter`}
          spellCheck={false}
          autoComplete="off"
          placeholder={t`e.g. EXIT1, *portable*x64*, !setup`}
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
      </label>
      <ul className="release-files-help muted small">
        <li>
          <Trans>
            Text matches anywhere in the name: <code>EXIT1</code>
          </Trans>
        </li>
        <li>
          <Trans>
            <code>*</code> is any text and <code>?</code> one character, over the whole name: <code>*portable*x64*</code>
          </Trans>
        </li>
        <li>
          <Trans>
            <code>/…/</code> is a regular expression: <code>/^game-(win|linux)/</code>
          </Trans>
        </li>
        <li>
          <Trans>
            Separate several with commas; <code>!</code> in front leaves files out: <code>!setup</code>
          </Trans>
        </li>
      </ul>
      {bad.length > 0 && (
        <p className="job-error">
          <Trans>Not a valid pattern: {invalid}</Trans>
        </p>
      )}
      <div className="release-files-preview" aria-live="polite">
        {files === undefined ? (
          <p className="muted small">
            <Trans>Loading the newest release…</Trans>
          </p>
        ) : files === null || !files.assets.length ? (
          <p className="muted small">
            <Trans>No release files to show yet.</Trans>
          </p>
        ) : (
          <>
            <p className="field-label">
              <Trans>Files of {version}</Trans>
            </p>
            <ul>
              {files.assets.map((a) => {
                const on = kept.has(a.id);
                return (
                  <li key={a.id} className={on ? "kept" : "left-out"}>
                    {on ? <Check size={14} aria-label={t`Matches`} /> : <X size={14} aria-label={t`Left out`} />}
                    <span>{a.filename}</span>
                  </li>
                );
              })}
            </ul>
            {none && (
              <p className="job-error">
                <Trans>None of these files match, so this release couldn&apos;t be installed.</Trans>
              </p>
            )}
          </>
        )}
      </div>
      {saved && (
        <button type="button" className="link-button" onClick={() => (setReleaseFiles(entry.id, ""), setText(""), setOpen(false))}>
          <Trans>Use all files again</Trans>
        </button>
      )}
      <div className="row">
        <button className="primary" disabled={bad.length > 0}>
          <Trans>Save</Trans>
        </button>
        <button type="button" onClick={() => setOpen(false)}>
          <Trans>Cancel</Trans>
        </button>
      </div>
    </form>
  );
}
