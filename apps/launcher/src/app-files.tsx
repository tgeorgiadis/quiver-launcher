/**
 * Add files to folder: the empty files in an installed app's folder, which some
 * apps look for to change how they run. `portable.txt`, which has many keep their
 * saves and settings there, gets a checkbox; any other is a name to type. What's
 * ticked comes from the folder itself, so it's right for an install from Quiver
 * Launcher 3 or a file the player made by hand. On an app's On this computer tab,
 * in its menu's Manage (as a dialog), with Settings' default for new installs.
 */
import { useEffect, useState, type ReactNode } from "react";
import { FilePlus } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { PanelFormHead } from "./detail";
import { native } from "./native";
import { PORTABLE_FILE, uniqueFiles, useLauncher } from "./store";

const isPortable = (name: string) => name.toLowerCase() === PORTABLE_FILE;
const same = (a: string[], b: string[]) => a.length === b.length && a.every((n) => b.some((m) => m.toLowerCase() === n.toLowerCase()));

/** The form: `head` over it, closing when saved or `onClose` says so. */
function AddFilesForm({ entry, head, className, onClose }: { entry: Entry; head: ReactNode; className: string; onClose: () => void }) {
  const { installs, catalog, setFiles, setNotice } = useLauncher();
  const install = installs[entry.id];
  // What the folder has as it opens, which is what taking a file off the list removes.
  const [present, setPresent] = useState<string[] | null>(null);
  // The form's choices as it opened, to tell whether saving changes anything.
  const [opened, setOpened] = useState<string[]>([]);
  const [portable, setPortable] = useState(false);
  const [others, setOthers] = useState<string[]>([]);
  const [input, setInput] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const listed = install?.files ?? (catalog[entry.id] ?? entry).launcher?.filesToAdd ?? [];
  useEffect(() => {
    if (!install) return;
    native.appFiles(install.folder, install.dir, uniqueFiles([PORTABLE_FILE, ...listed])).then(
      (found) => {
        const files = found ?? [];
        setPresent(files);
        const there = files.some(isPortable);
        const named = uniqueFiles([...listed, ...files]).filter((n) => !isPortable(n));
        setPortable(there);
        setOthers(named);
        setOpened(there ? [PORTABLE_FILE, ...named] : named);
      },
      (e) => (setPresent([]), setError(String(e))),
    );
    // What's there as it opens.
  }, [entry.id]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!install) return null;

  // Typed names, comma-separated as Quiver Launcher 3 took them; portable.txt typed is its checkbox.
  const typed = (list: string[]) => {
    const names = uniqueFiles(input.split(","));
    return { others: uniqueFiles([...list, ...names.filter((n) => !isPortable(n))]), portable: names.some(isPortable) };
  };
  const addTyped = () => {
    const next = typed(others);
    setOthers(next.others);
    if (next.portable) setPortable(true);
    setInput("");
  };
  const wasPortable = Boolean(present?.some(isPortable));
  const played = Boolean(install.lastPlayed || install.playTime);
  const save = () => {
    const next = typed(others);
    const files = uniqueFiles([...(portable || next.portable ? [PORTABLE_FILE] : []), ...next.others]);
    // Nothing changed: it goes on following the catalog, if it was.
    if (same(files, opened)) return onClose();
    const remove = (present ?? []).filter((n) => !files.some((f) => f.toLowerCase() === n.toLowerCase()));
    setBusy(true);
    setError(null);
    native.setAppFiles(entry.id, install.folder, install.dir, files, remove).then(
      (stayed) => {
        setFiles(entry.id, files);
        onClose();
        const names = (stayed ?? []).join(", ");
        if (names) setNotice(t`${names} has something in it, so it stays in the folder.`);
      },
      (e) => (setBusy(false), setError(String(e))),
    );
  };
  return (
    <form
      className={`add-files ${className}`}
      aria-label={t`Add files to folder`}
      onSubmit={(e) => {
        e.preventDefault();
        if (present) save();
      }}
    >
      {head}
      <label className="toggle">
        <input type="checkbox" checked={portable} disabled={!present} onChange={(e) => setPortable(e.target.checked)} />
        <span>
          <Trans comment="Adding this file to an app's folder has the app keep its saves there">
            Keep this app&apos;s data in its folder (<code>portable.txt</code>)
          </Trans>
        </span>
      </label>
      {present && portable !== wasPortable && played && (
        <p className="add-files-warning" role="status">
          <Trans>This app may stop finding its saves and settings, because it will look for them in a different place. Nothing is deleted.</Trans>
        </p>
      )}
      <div className="tag-field">
        <span className="field-label">
          <Trans comment="Files to add to an app's folder, other than portable.txt">Other files</Trans>
        </span>
        <div className="chips">
          {others.map((name) => (
            <button type="button" key={name} className="chip on" aria-label={t`Remove ${name}`} onClick={() => setOthers(others.filter((n) => n !== name))}>
              {name} ×
            </button>
          ))}
          <input
            aria-label={t`Other files`}
            placeholder={t`File name, like config.ini`}
            maxLength={255}
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => {
              if (e.key !== "Enter") return;
              e.preventDefault();
              addTyped();
            }}
          />
        </div>
        <span className="muted small">
          <Trans>Creates an empty file with each name. Separate several names with commas. Some apps look for these files to change how they run.</Trans>
        </span>
      </div>
      {error && (
        <p className="job-error" role="status">
          {error}
        </p>
      )}
      <div className="row">
        <button className="primary" disabled={busy || !present}>
          <Trans comment="Saves the files for an app's folder">Save</Trans>
        </button>
        <button type="button" onClick={onClose}>
          <Trans>Cancel</Trans>
        </button>
      </div>
    </form>
  );
}

/** On an app's On this computer tab: a button that opens the form in place, as Launch options does. */
export function AddFilesButton({ entry }: { entry: Entry }) {
  const [open, setOpen] = useState(false);
  if (!open)
    return (
      <button className="panel-action opens" onClick={() => setOpen(true)}>
        <FilePlus size={16} aria-hidden="true" /> <Trans>Add files to folder</Trans>
      </button>
    );
  return <AddFilesForm entry={entry} className="panel-form" head={<PanelFormHead title={t`Add files to folder`} onClose={() => setOpen(false)} />} onClose={() => setOpen(false)} />;
}

/** The same form as a dialog's body, for an app's menu; `Dialog` is App.tsx's, around it. */
export function AddFilesDialogBody({ entry, onClose }: { entry: Entry; onClose: () => void }) {
  return (
    <AddFilesForm
      entry={entry}
      className="detail-body"
      onClose={onClose}
      head={
        <h2>
          <FilePlus size={18} /> <Trans>Add files to folder</Trans>
        </h2>
      }
    />
  );
}

/** Settings' default for new installs. */
export function PortableDefaultSetting() {
  const { config, settings, setSettings } = useLauncher();
  if (config.os === "android" || config.os === "ios") return null;
  return (
    <section className="settings-section" aria-label={t`New installs`}>
      <label className="toggle">
        <input type="checkbox" checked={Boolean(settings.portableByDefault)} onChange={(e) => setSettings({ ...settings, portableByDefault: e.target.checked || undefined })} />
        <span>
          <Trans>
            Add <code>portable.txt</code> to new installs
          </Trans>
        </span>
      </label>
      <p className="muted">
        <Trans>Apps that support it keep their saves and settings in their own folder. Apps you&apos;ve already installed don&apos;t change.</Trans>
      </p>
    </section>
  );
}
