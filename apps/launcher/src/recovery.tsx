/**
 * Library snapshots: the launcher keeps earlier copies of the library, so a
 * damaged or emptied one can be put back. When a saved file can't be read,
 * or is gone though snapshots show a library, the launcher opens here
 * instead of starting over it; Settings can restore one any time.
 */
import { useEffect, useState } from "react";
import { ArchiveRestore } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import { native, type Snapshot, type StateProblem } from "./native";

// A whole sentence for each file, put in the language when shown.
const DAMAGED: Record<string, MessageDescriptor> = {
  library: msg`The file with your library is damaged.`,
  installs: msg`The file with your list of installed apps is damaged.`,
  settings: msg`The file with your settings is damaged.`,
  collections: msg`The file with your playlists is damaged.`,
};
const MISSING: Record<string, MessageDescriptor> = {
  library: msg`The file with your library is missing.`,
  installs: msg`The file with your list of installed apps is missing.`,
  settings: msg`The file with your settings is missing.`,
  collections: msg`The file with your playlists is missing.`,
};

const when = (at: number) => new Date(at).toLocaleString(i18n.locale, { dateStyle: "medium", timeStyle: "short" });
const apps = (n: number) => plural(n, { one: "# app", other: "# apps" });

/** Restores a snapshot, then starts the launcher again on it. */
const restore = (id: string) => native.restoreSnapshot(id).then(() => window.location.reload());

function SnapshotList({ snapshots, onRestore }: { snapshots: Snapshot[]; onRestore: (id: string) => void }) {
  return (
    <ul className="snapshots" aria-label={t`Backups`}>
      {snapshots.map((s) => (
        <li key={s.id}>
          <span>
            {when(s.at)} · {apps(s.apps)}
          </span>
          <button onClick={() => onRestore(s.id)}>
            <Trans>Restore</Trans>
          </button>
        </li>
      ))}
    </ul>
  );
}

/** Shown instead of the launcher while a saved file can't be used. */
export function Recovery({ problems }: { problems: StateProblem[] }) {
  const [snapshots, setSnapshots] = useState<Snapshot[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => void native.listSnapshots().then(setSnapshots, () => setSnapshots([])), []);
  const fail = (e: unknown) => setError(String(e));
  return (
    <main className="empty recovery" role="alert" aria-label={t`Library problem`}>
      <h2>
        <Trans>Quiver couldn&apos;t open your library</Trans>
      </h2>
      {problems.map((p) => {
        const sentence = (p.problem === "damaged" ? DAMAGED : MISSING)[p.name];
        return (
          <p key={p.name}>
            {sentence ? i18n._(sentence) : p.problem === "damaged" ? t`A file Quiver keeps is damaged.` : t`A file Quiver keeps is missing.`}
          </p>
        );
      })}
      {snapshots?.length ? (
        <>
          <p>
            <Trans>Restore a backup Quiver kept. The newest is first.</Trans>
          </p>
          <SnapshotList snapshots={snapshots.slice(0, 10)} onRestore={(id) => restore(id).catch(fail)} />
        </>
      ) : (
        snapshots && (
          <p>
            <Trans>There are no backups to restore.</Trans>
          </p>
        )
      )}
      <button onClick={() => native.startEmptyLibrary().then(() => window.location.reload(), fail)}>
        <Trans>Continue without restoring</Trans>
      </button>
      <p className="muted small">
        <Trans>Apps installed on this computer stay in your library. A damaged file is kept beside the new one, so nothing is deleted.</Trans>
      </p>
      {error && <p className="job-error">{error}</p>}
    </main>
  );
}

/** Settings: restore a backup of the library. */
export function Snapshots() {
  const [snapshots, setSnapshots] = useState<Snapshot[] | null>(null);
  const [asking, setAsking] = useState<Snapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  if (!snapshots)
    return (
      <button onClick={() => native.listSnapshots().then(setSnapshots, (e) => setError(String(e)))}>
        <ArchiveRestore size={15} aria-hidden="true" /> <Trans>Choose a backup…</Trans>
      </button>
    );
  const taken = asking ? when(asking.at) : "";
  const count = asking?.apps ?? 0;
  return (
    <div className="snapshot-picker">
      {asking ? (
        <div className="confirm" role="group" aria-label={t`Restore backup`}>
          <p>
            {t`Replace your library with the one from ${taken} (${plural(count, { one: "# app", other: "# apps" })})? Your library as it is now is kept as a backup too.`}
          </p>
          <div className="row">
            <button className="primary" onClick={() => restore(asking.id).catch((e) => setError(String(e)))}>
              <Trans>Restore</Trans>
            </button>
            <button onClick={() => setAsking(null)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        </div>
      ) : snapshots.length ? (
        <SnapshotList snapshots={snapshots.slice(0, 10)} onRestore={(id) => setAsking(snapshots.find((s) => s.id === id) ?? null)} />
      ) : (
        <p className="muted">
          <Trans>There are no backups yet.</Trans>
        </p>
      )}
      {error && <p className="job-error">{error}</p>}
    </div>
  );
}
