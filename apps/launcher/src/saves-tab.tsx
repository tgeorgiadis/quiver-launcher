/**
 * An installed app's Saves tab: where its saves are kept and the backups Quiver has made of them (saves.rs), with
 * Back up now, Restore, and the player's own choice of folder when Quiver has the wrong place. Backups are made by
 * themselves after playing, before an update and before uninstalling (store.tsx).
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import { Archive, FolderOpen, FolderSearch, RotateCcw, Trash2, Undo2 } from "lucide-react";
import { i18n } from "@lingui/core";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { sizeLabel } from "./downloads";
import { native, type SaveBackup, type SavesInfo } from "./native";
import { onSavesChanged, savesOf } from "./saves";
import { useLauncher } from "./store";

const NOTHING: SavesInfo = { places: [], from: "none", backups: [] };

/** Why a backup was made, as its row says. */
function reasonLabel(reason: string) {
  if (reason === "played") return t({ message: "After playing", comment: "Why a save backup was made" });
  if (reason === "update") return t({ message: "Before an update", comment: "Why a save backup was made" });
  if (reason === "uninstall") return t({ message: "Before uninstalling", comment: "Why a save backup was made" });
  if (reason === "restore") return t({ message: "Before a restore", comment: "Why a save backup was made: it holds what was there before another backup was put back" });
  return t({ message: "Made by you", comment: "Why a save backup was made: the player pressed Back up now" });
}

const when = (at: number) => new Date(at).toLocaleString(i18n.locale, { dateStyle: "medium", timeStyle: "short" });

export function SavesTab({ entry }: { entry: Entry }) {
  const { installs, catalog } = useLauncher();
  const install = installs[entry.id];
  const known = catalog[entry.id] ?? entry;
  // Named afresh only when something it's named by changes, so the tab doesn't read its backups again on every render.
  const key = JSON.stringify(install ? savesOf(entry.id, install, known) : null);
  const of = useMemo(() => (install ? savesOf(entry.id, install, known) : null), [key]); // eslint-disable-line react-hooks/exhaustive-deps
  const [info, setInfo] = useState<SavesInfo | null>(null);
  const [busy, setBusy] = useState(false);
  /** The backup Restore asks about before it goes ahead. */
  const [asking, setAsking] = useState<string | null>(null);
  const [said, setSaid] = useState<{ text: string; error?: true } | null>(null);
  const failed = (e: unknown) => setSaid({ text: String(e), error: true });

  const load = useCallback(() => {
    if (!of) return;
    native.savesInfo(of).then((i) => setInfo(i ?? NOTHING), failed);
  }, [of]);
  useEffect(() => {
    load();
    return onSavesChanged((id) => id === entry.id && load());
  }, [load, entry.id]);
  if (!of || !info) return null;

  /** One thing at a time: its buttons wait, and what it says replaces the last message. */
  const run = (work: () => Promise<string | null>) => {
    setBusy(true);
    setSaid(null);
    setAsking(null);
    work()
      .then((text) => text && setSaid({ text }), failed)
      .finally(() => (setBusy(false), load()));
  };
  const backUp = () =>
    run(async () => ((await native.savesBackup(of, "manual")) ? t({ message: "Backed up.", comment: "Said after a save backup was made" }) : t`Nothing changed since the last backup.`));
  const restore = (backup: SaveBackup) =>
    run(async () => {
      await native.savesRestore(of, backup.name);
      return t({ message: "Restored.", comment: "Said after a save backup was put back" });
    });
  const remove = (backup: SaveBackup) =>
    run(async () => {
      await native.savesRemove(of.id, backup.name);
      return null;
    });
  const choose = () =>
    run(async () => {
      const path = await native.pickFolder(t`Choose where this app saves`, info.places.find((p) => p.exists)?.path || install?.dir);
      if (!path) return null;
      await native.savesChoose(of, path);
      return null;
    });
  const unchoose = () =>
    run(async () => {
      await native.savesChoose(of);
      return null;
    });

  const source =
    info.from === "you"
      ? t`You chose this folder.`
      : info.from === "catalog"
        ? t`From Quiver's catalog.`
        : info.from === "found"
          ? t`Quiver saw the app save here.`
          : t`Quiver doesn't know where this app saves yet. Play it once, or choose the folder.`;

  return (
    <section className="app-panel computer-panel saves-panel" aria-label={t({ message: "Saves", comment: "Tab of an installed app's page: its save backups" })}>
      <div className="panel-group">
        <h4>
          <Trans>Where saves are kept</Trans>
        </h4>
        {info.places.map((place) => (
          <div className="save-row" key={place.template}>
            <span className="save-main">
              <span className="save-path">{place.path || place.template}</span>
              {!place.exists && (
                <span className="save-note">
                  <Trans>Not there yet. It&apos;s made when the app first saves.</Trans>
                </span>
              )}
            </span>
            {place.exists && (
              <button className="save-button" onClick={() => void native.savesOpen(of, place.template).catch(failed)}>
                <FolderOpen size={14} aria-hidden="true" /> <Trans>Open folder</Trans>
              </button>
            )}
          </div>
        ))}
        <p className="muted save-source">{source}</p>
        <button className="panel-action opens" disabled={busy} onClick={choose}>
          <FolderSearch size={16} aria-hidden="true" /> <Trans>Choose folder</Trans>
        </button>
        {info.from === "you" && (
          <button className="panel-action" disabled={busy} onClick={unchoose}>
            <Undo2 size={16} aria-hidden="true" /> <Trans>Use Quiver&apos;s choice</Trans>
          </button>
        )}
      </div>
      <div className="panel-group">
        <h4>
          <Trans>Backups</Trans>
        </h4>
        <p className="muted save-source">
          <Trans>Quiver backs up your saves after you play, before an update and before uninstalling. It keeps the newest 10 on this computer.</Trans>
        </p>
        <button className="panel-action" disabled={busy} onClick={backUp}>
          <Archive size={16} aria-hidden="true" /> <Trans>Back up now</Trans>
        </button>
        {said && (
          <p className={said.error ? "save-said error" : "save-said"} role={said.error ? "alert" : "status"}>
            {said.text}
          </p>
        )}
        {info.backups.map((backup) => {
          const files = plural(backup.files, { one: "# file", other: "# files" });
          const version = backup.version ?? "";
          return (
            <div className="save-row" key={backup.name}>
              <span className="save-main">
                <span>{when(backup.at)}</span>
                <span className="save-note">
                  {[reasonLabel(backup.reason), files, sizeLabel(backup.size), version && t({ message: `Version ${version}`, comment: "The app's version when a save backup was made" })].filter(Boolean).join(" · ")}
                </span>
                {asking === backup.name && (
                  <span className="save-note">
                    <Trans>Restore this backup? Your saves now are backed up first.</Trans>
                  </span>
                )}
              </span>
              {asking === backup.name ? (
                <>
                  <button className="save-button primary" disabled={busy} onClick={() => restore(backup)}>
                    <Trans>Restore</Trans>
                  </button>
                  <button className="save-button" disabled={busy} onClick={() => setAsking(null)}>
                    <Trans>Cancel</Trans>
                  </button>
                </>
              ) : (
                <>
                  <button className="save-button" disabled={busy} onClick={() => setAsking(backup.name)}>
                    <RotateCcw size={14} aria-hidden="true" /> <Trans>Restore</Trans>
                  </button>
                  <button className="save-button" disabled={busy} aria-label={t`Remove this backup`} title={t`Remove`} onClick={() => remove(backup)}>
                    <Trash2 size={14} aria-hidden="true" />
                  </button>
                </>
              )}
            </div>
          );
        })}
        {!info.backups.length && (
          <p className="muted save-source">
            <Trans>No backups yet.</Trans>
          </p>
        )}
      </div>
    </section>
  );
}
