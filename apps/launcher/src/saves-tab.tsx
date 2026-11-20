/**
 * An installed app's Saves tab: where its saves are kept and the backups Quiver has made of them (saves.rs), with
 * Back up now, Restore, and the player's own choice of folder when Quiver has the wrong place. Backups are made by
 * themselves after playing, before an update and before uninstalling (store.tsx).
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import { Archive, CircleCheck, CloudDownload, CloudUpload, FolderInput, FolderOpen, FolderSearch, FolderSync, RotateCcw, Trash2, Undo2 } from "lucide-react";
import { i18n } from "@lingui/core";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { sizeLabel } from "./downloads";
import { native, type SaveBackup, type SavesFolder, type SavesInfo, type SavesLook } from "./native";
import { restoreFromFolder } from "./sync-folder";
import { onSavesChanged, reportSaveWarning, savesOf } from "./saves";
import { telemetryChoice } from "./telemetry";
import { fetchCopy, lastSent, restoreBackup, sendBackup, type CloudCopy, type CloudMine } from "./cloud-saves";
import { useAccount } from "./account";
import { isLocal } from "./custom";
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

/** Why a backup looks like more than saves, in words: the strongest sign. */
export function lookText(look: SavesLook) {
  if (look.reasons.includes("tooBig")) return t`This folder is too big to back up, so it likely holds more than saves, such as the game's own files.`;
  const size = sizeLabel(look.bytes);
  if (look.reasons.includes("big")) return t`The files in this app's newest backup add up to ${size} before it's zipped. Saves are usually under 1 MB, so it may hold files that aren't saves.`;
  const files = look.files;
  if (look.reasons.includes("many")) return t`This app's newest backup has ${files} files. Saves are usually a few files, so it may hold files that aren't saves.`;
  return t`This app's newest backup is mostly pictures, so it may hold screenshots or textures rather than saves.`;
}

export const SYSTEM_NAMES: Record<string, string> = { windows: "Windows", linux: "Linux", macos: "macOS" };

export const when = (at: number) => new Date(at).toLocaleString(i18n.locale, { dateStyle: "medium", timeStyle: "short" });

export function SavesTab({ entry }: { entry: Entry }) {
  const { installs, catalog, settings, cloudSavesFor, cloudCatchUp, confirmSavePlaces, client } = useLauncher();
  const account = useAccount();
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
  /** The account's copies of this app's saves; null signed out, or while the account can't be reached. */
  const [cloud, setCloud] = useState<CloudMine | null>(null);
  /** The cloud copy Restore asks about. */
  const [askingCloud, setAskingCloud] = useState<string | null>(null);
  /** What the cloud part says after something was done there. */
  const [cloudSaid, setCloudSaid] = useState<{ text: string; error?: true } | null>(null);
  const api = cloudSavesFor(entry.id);
  /** The player's sync folder, and its backups of this app (sync-folder.ts); null while they're read. */
  const folder = settings.syncFolder;
  const [kept, setKept] = useState<SavesFolder | null>(null);
  /** The folder's backup Restore asks about. */
  const [askingKept, setAskingKept] = useState<string | null>(null);
  const [keptSaid, setKeptSaid] = useState<{ text: string; error?: true } | null>(null);
  /** Shows the player a folder a backup names that this computer hasn't kept the app's saves in, before anything goes there. */
  const confirm = confirmSavePlaces(known.projectName);

  const load = useCallback(() => {
    if (!of) return;
    native.savesInfo(of).then((i) => setInfo(i ?? NOTHING), failed);
    if (api)
      api.mine(entry.id).then(setCloud, () => setCloud(null));
    else setCloud(null);
    if (folder) native.savesFolderState(of, folder).then(setKept, () => setKept(null));
    else setKept(null);
  }, [of, Boolean(api), entry.id, folder]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    load();
    return onSavesChanged((id) => id === entry.id && load());
  }, [load, entry.id]);
  if (!of || !info) return null;

  /** One thing at a time: its buttons wait, and what it says replaces the last message. */
  const run = (work: () => Promise<string | null>) => {
    setBusy(true);
    setSaid(null);
    setCloudSaid(null);
    setAsking(null);
    setAskingCloud(null);
    setAskingKept(null);
    setKeptSaid(null);
    work()
      .then((text) => text && setSaid({ text }), failed)
      .finally(() => (setBusy(false), load()));
  };
  const cloudOn = Boolean(api && cloud?.on && cloud.allowed && !info.cloudOff);
  const backUp = () =>
    run(async () => {
      const made = await native.savesBackup(of, "manual");
      // Sent to the account too; how that went shows in the cloud part.
      if (made && api && cloudOn) await sendBackup(of, entry.id, made, { api, native, manual: true });
      // And kept in the sync folder; how that went shows in its part.
      if (made && folder) await native.savesFolderPut(of, folder, made.name).catch((e) => setKeptSaid({ text: String(e), error: true }));
      return made ? t({ message: "Backed up.", comment: "Said after a save backup was made" }) : t`Nothing changed since the last backup.`;
    });
  /** Something done with the account's copies: what it says goes in the cloud part. */
  const runCloud = (work: () => Promise<string | null>) =>
    run(() =>
      work().then(
        (text) => (text && setCloudSaid({ text }), null),
        (e) => (setCloudSaid({ text: typeof e === "string" ? e : t`Couldn't reach your cloud saves. Check your connection and try again.`, error: true }), null),
      ),
    );
  const restoreCloud = (copy: CloudCopy) =>
    runCloud(async () => {
      if (!api) return null;
      if (!(await fetchCopy(of, copy, { api, native, confirm }))) return null;
      return t({ message: "Restored.", comment: "Said after a save backup was put back" });
    });
  const removeCloud = (copy: CloudCopy) =>
    runCloud(async () => {
      await api?.remove({ id: copy.id });
      cloudCatchUp();
      return null;
    });
  const setCloudOff = (off: boolean) =>
    runCloud(async () => {
      await native.savesCloudOff(of, off);
      return null;
    });
  /** Sends the newest backup now: after switching cloud saves on, or once there's room again. */
  const sendNewest = () =>
    runCloud(async () => {
      const newest = info.backups.find((b) => b.reason !== "restore") ?? info.backups[0];
      if (!api || !newest) return null;
      const how = await sendBackup(of, entry.id, newest, { api, native, manual: true });
      return how === "sent" ? t`Sent to your cloud saves.` : null;
    });
  /** Something done with the sync folder's backups: what it says goes in its part. */
  const runKept = (work: () => Promise<string | null>) =>
    run(() =>
      work().then(
        (text) => (text && setKeptSaid({ text }), null),
        (e) => (setKeptSaid({ text: String(e), error: true }), null),
      ),
    );
  const restoreKept = (backup: SaveBackup) =>
    runKept(async () => {
      if (!folder || !(await restoreFromFolder(of, folder, backup, { native, confirm }))) return null;
      return t({ message: "Restored.", comment: "Said after a save backup was put back" });
    });
  /** Copies the newest backup in now: after choosing the folder, or once its drive is back. */
  const putNewest = () =>
    runKept(async () => {
      const newest = info.backups.find((b) => b.reason !== "restore") ?? info.backups[0];
      if (!folder || !newest) return null;
      await native.savesFolderPut(of, folder, newest.name);
      return t`Copied to your sync folder.`;
    });
  const restore = (backup: SaveBackup) =>
    run(async () => {
      if (!(await restoreBackup(of, backup.name, { native, confirm }))) return null;
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
  /** The player says the places hold their saves: the note goes, and staff are told, as for a warning. */
  const fine = () =>
    run(async () => {
      await native.savesFine(of);
      if (known.slug && !isLocal(entry.id) && telemetryChoice().enabled)
        void reportSaveWarning(of, known, { warning: native.savesWarning, warned: native.savesWarned, send: (app, report) => client.saveWarning(app, report) }).catch(() => {});
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

  /** The account's side: what's kept there for this app, and the switch for it. */
  function cloudPart() {
    const heading = (
      <h4>
        <Trans comment="Heading on an app's Saves tab: the copies of its saves kept with the player's account">Cloud saves</Trans>
      </h4>
    );
    const why = isLocal(entry.id) || !known.slug
      ? t`Cloud saves are for apps from Quiver's catalog. This app's backups stay on this computer.`
      : !account.user
        ? t`Sign in to keep your saves with your Quiver account, so they follow you to another computer.`
        : settings.cloudSavesOff
          ? t`Cloud saves are switched off in Settings. Backups stay on this computer.`
          : !cloud
            ? t`Couldn't reach your cloud saves. Backups stay on this computer for now.`
            : cloud.why === "paused"
              ? t`Cloud saves are paused for now, so nothing new is being sent. Backups stay on this computer.`
              : cloud.why === "suspended"
                ? t`Quiver has stopped this account adding to its cloud saves. Backups stay on this computer.`
                : !cloud.on
                  ? t`Cloud saves aren't available yet. Backups stay on this computer.`
              : !cloud.allowed
                ? t`Quiver has switched cloud saves off for this app. Its backups stay on this computer.`
                : null;
    if (why || !cloud)
      return (
        <div className="panel-group cloud-saves">
          {heading}
          <p className="muted save-source">{why}</p>
        </div>
      );
    const used = sizeLabel(cloud.used);
    const limit = sizeLabel(cloud.limit);
    const keep = cloud.keep;
    const max = sizeLabel(cloud.maxCopy);
    const sent = lastSent(entry.id);
    const newest = info!.backups.find((b) => b.reason !== "restore") ?? info!.backups[0];
    const unsent = !info!.cloudOff && newest && !cloud.copies.some((c) => c.hash === newest.hash);
    return (
      <div className="panel-group cloud-saves">
        {heading}
        <label className="toggle">
          <input type="checkbox" checked={!info!.cloudOff} disabled={busy} onChange={(e) => setCloudOff(!e.target.checked)} />
          <Trans>Keep this app&apos;s saves with my Quiver account</Trans>
        </label>
        <p className="muted save-source">
          {t`Each backup is also sent to your account, which keeps the newest ${keep} of each app. ${used} of ${limit} used.`}
        </p>
        {!info!.cloudOff && sent === "full" && (
          <p className="save-said error" role="status">
            {t`Your cloud saves are full (${limit}). The newest backup is on this computer only. Remove copies here or from another app to make room.`}
          </p>
        )}
        {!info!.cloudOff && sent === "big" && (
          <p className="save-said error" role="status">
            {t`These saves are too big for cloud saves (${max}). They're backed up on this computer only.`}
          </p>
        )}
        {unsent && sent !== "big" && (
          <button className="panel-action" disabled={busy} onClick={sendNewest}>
            <CloudUpload size={16} aria-hidden="true" /> <Trans>Send the newest backup now</Trans>
          </button>
        )}
        {cloudSaid && (
          <p className={cloudSaid.error ? "save-said error" : "save-said"} role={cloudSaid.error ? "alert" : "status"}>
            {cloudSaid.text}
          </p>
        )}
        {cloud.copies.map((copy) => {
          const files = plural(copy.files, { one: "# file", other: "# files" });
          const version = copy.version ?? "";
          const device = copy.device;
          return (
            <div className="save-row cloud-row" key={copy.id}>
              <span className="save-main">
                <span>{when(copy.madeAt)}</span>
                <span className="save-note">
                  {[t({ message: `From ${device}`, comment: "Which computer a cloud save was made on" }), SYSTEM_NAMES[copy.os] ?? copy.os, files, sizeLabel(copy.size), version && t({ message: `Version ${version}`, comment: "The app's version when a save backup was made" })]
                    .filter(Boolean)
                    .join(" · ")}
                </span>
                {askingCloud === copy.id && (
                  <span className="save-note">
                    <Trans>Restore this copy? Your saves now are backed up first.</Trans>
                  </span>
                )}
              </span>
              {askingCloud === copy.id ? (
                <>
                  <button className="save-button primary" disabled={busy} onClick={() => restoreCloud(copy)}>
                    <Trans>Restore</Trans>
                  </button>
                  <button className="save-button" disabled={busy} onClick={() => setAskingCloud(null)}>
                    <Trans>Cancel</Trans>
                  </button>
                </>
              ) : (
                <>
                  <button className="save-button" disabled={busy} onClick={() => setAskingCloud(copy.id)}>
                    <CloudDownload size={14} aria-hidden="true" /> <Trans>Restore</Trans>
                  </button>
                  <button className="save-button" disabled={busy} aria-label={t`Remove this copy from your account`} title={t`Remove`} onClick={() => removeCloud(copy)}>
                    <Trash2 size={14} aria-hidden="true" />
                  </button>
                </>
              )}
            </div>
          );
        })}
        {!cloud.copies.length && (
          <p className="muted save-source">
            <Trans>Nothing in your account for this app yet.</Trans>
          </p>
        )}
      </div>
    );
  }

  /** The sync folder's side: its backups of this app, from this computer and the player's others. */
  function folderPart() {
    const heading = (
      <h4>
        <Trans comment="Heading on an app's Saves tab: the backups of its saves in the player's own synced folder">Sync folder</Trans>
      </h4>
    );
    if (!folder)
      return (
        <div className="panel-group sync-saves">
          {heading}
          <p className="muted save-source">
            <Trans>Backups can also go in a folder of your own, such as one in Dropbox, OneDrive or Syncthing, so your saves follow you to your other computers without an account. Choose it in Settings, under Cloud saves.</Trans>
          </p>
        </div>
      );
    if (!kept) return null;
    const newest = info!.backups.find((b) => b.reason !== "restore") ?? info!.backups[0];
    const missing = kept.there && newest && !kept.copies.some((c) => c.hash === newest.hash);
    return (
      <div className="panel-group sync-saves">
        {heading}
        <p className="muted save-source">
          <FolderSync size={13} aria-hidden="true" /> {t`Each backup is also kept in ${folder}, which holds the newest 5 of this app from all your computers.`}
        </p>
        {!kept.there && (
          <p className="save-said error" role="status">
            <Trans>Quiver can&apos;t find this folder right now. If it&apos;s on a drive, plug it in. Backups carry on on this computer.</Trans>
          </p>
        )}
        {missing && (
          <button className="panel-action" disabled={busy} onClick={putNewest}>
            <FolderInput size={16} aria-hidden="true" /> <Trans>Copy the newest backup there now</Trans>
          </button>
        )}
        {keptSaid && (
          <p className={keptSaid.error ? "save-said error" : "save-said"} role={keptSaid.error ? "alert" : "status"}>
            {keptSaid.text}
          </p>
        )}
        {kept.copies.map((copy) => {
          const files = plural(copy.files, { one: "# file", other: "# files" });
          const version = copy.version ?? "";
          const device = copy.device;
          return (
            <div className="save-row cloud-row" key={copy.name}>
              <span className="save-main">
                <span>{when(copy.at)}</span>
                <span className="save-note">
                  {[t({ message: `From ${device}`, comment: "Which computer a cloud save was made on" }), SYSTEM_NAMES[copy.os] ?? copy.os, files, sizeLabel(copy.size), version && t({ message: `Version ${version}`, comment: "The app's version when a save backup was made" })]
                    .filter(Boolean)
                    .join(" · ")}
                </span>
                {copy.hash === kept.hash && (
                  <span className="save-note">
                    <Trans>These are the saves on this computer now.</Trans>
                  </span>
                )}
                {askingKept === copy.name && (
                  <span className="save-note">
                    <Trans>Restore this backup? Your saves now are backed up first.</Trans>
                  </span>
                )}
              </span>
              {askingKept === copy.name ? (
                <>
                  <button className="save-button primary" disabled={busy} onClick={() => restoreKept(copy)}>
                    <Trans>Restore</Trans>
                  </button>
                  <button className="save-button" disabled={busy} onClick={() => setAskingKept(null)}>
                    <Trans>Cancel</Trans>
                  </button>
                </>
              ) : (
                <button className="save-button" disabled={busy} onClick={() => setAskingKept(copy.name)}>
                  <RotateCcw size={14} aria-hidden="true" /> <Trans>Restore</Trans>
                </button>
              )}
            </div>
          );
        })}
        {kept.there && !kept.copies.length && (
          <p className="muted save-source">
            <Trans>Nothing in your sync folder for this app yet.</Trans>
          </p>
        )}
      </div>
    );
  }

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
        {info.look && (
          <div className="save-look" role="note" aria-label={t`These may not be only saves`}>
            <p className="save-said">{lookText(info.look)}</p>
            <p className="save-note">
              <Trans>Open the folder to check. If the app saves somewhere else, choose that folder.</Trans>
            </p>
          </div>
        )}
        <button className="panel-action opens" disabled={busy} onClick={choose}>
          <FolderSearch size={16} aria-hidden="true" /> <Trans>Choose folder</Trans>
        </button>
        {info.look && !info.look.reasons.includes("tooBig") && (
          <button className="panel-action" disabled={busy} onClick={fine}>
            <CircleCheck size={16} aria-hidden="true" /> <Trans>These are my saves</Trans>
          </button>
        )}
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
      {cloudPart()}
      {folderPart()}
    </section>
  );
}
