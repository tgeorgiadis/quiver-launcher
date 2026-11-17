/**
 * Settings, Cloud saves, Sync folder: the folder of the player's own that save backups are also kept in
 * (sync-folder.ts), with what it holds for each app. Choosing it is all there is to set up; an app's own Saves tab
 * lists the folder's backups of that app, and restores them.
 */
import { useCallback, useEffect, useState } from "react";
import { FolderOpen, FolderSync } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Artwork, cardSources } from "@quiverlauncher/ui";
import { sizeLabel } from "./downloads";
import { native, type SyncFolderApp } from "./native";
import { onSavesChanged, savesOf } from "./saves";
import { seed } from "./sync-folder";
import { when } from "./saves-tab";
import { useLauncher } from "./store";

export function SyncFolderSettings() {
  const { settings, setSettings, installs, catalog } = useLauncher();
  const folder = settings.syncFolder;
  /** Null while it's read. */
  const [kept, setKept] = useState<{ there: boolean; apps: SyncFolderApp[] } | null>(null);
  const [busy, setBusy] = useState(false);
  const [said, setSaid] = useState<{ text: string; error?: true } | null>(null);
  /** Stop using asks first. */
  const [asking, setAsking] = useState(false);

  const load = useCallback(() => {
    if (!folder) return setKept(null);
    native.savesFolderSummary(folder).then(setKept, () => setKept({ there: false, apps: [] }));
  }, [folder]);
  useEffect(() => {
    load();
    return onSavesChanged(load);
  }, [load]);

  const choose = async () => {
    setSaid(null);
    setAsking(false);
    const picked = await native.pickFolder(t`Choose your sync folder`, folder);
    if (!picked) return;
    setBusy(true);
    try {
      const to = await native.savesFolderChoose(picked);
      const there = (await native.savesFolderSummary(to)).apps.length;
      setSettings((s) => ({ ...s, syncFolder: to }));
      // What this computer has goes in for the apps the folder has nothing of. The rest are asked about when they start.
      const apps = Object.entries(installs).map(([id, install]) => savesOf(id, install, catalog[id]));
      const put = await seed(apps, to, { native });
      setKept(await native.savesFolderSummary(to));
      setSaid({
        text: there
          ? plural(there, {
              one: "This folder already has saves of # app. Quiver offers them when you start it here.",
              other: "This folder already has saves of # apps. Quiver offers them when you start those apps here.",
            })
          : put
            ? plural(put, { one: "The saves of # app were copied in. New backups follow after you play.", other: "The saves of # apps were copied in. New backups follow after you play." })
            : t`Your save backups go here from now on, after you play.`,
      });
    } catch (e) {
      setSaid({ text: typeof e === "string" ? e : t`Couldn't use that folder. Choose another.`, error: true });
    }
    setBusy(false);
  };
  const stop = () => {
    setAsking(false);
    setSaid(null);
    setSettings((s) => ({ ...s, syncFolder: undefined }));
  };

  const count = kept?.apps.reduce((sum, app) => sum + app.copies, 0) ?? 0;
  const size = sizeLabel(kept?.apps.reduce((sum, app) => sum + app.size, 0) ?? 0);
  const apps = kept?.apps.length ?? 0;
  return (
    <section className="settings-section sync-folder" aria-label={t`Sync folder`}>
      <h3>
        <Trans comment="Heading in Settings: a folder of the player's own that save backups are also kept in">Sync folder</Trans>
      </h3>
      <p className="muted">
        <Trans>
          Keep your save backups in a folder of your own too, such as one inside Dropbox, OneDrive, Google Drive or Syncthing. Choose the same folder on your other computers, and Quiver offers your newest saves
          when you start an app there. It needs no Quiver account, and works for apps you added yourself.
        </Trans>
      </p>
      {!folder ? (
        <button disabled={busy} onClick={() => void choose()}>
          <FolderSync size={16} aria-hidden="true" /> <Trans>Choose folder</Trans>
        </button>
      ) : (
        <>
          <div className="save-row sync-folder-row">
            <span className="save-main">
              <span className="save-path">{folder}</span>
              {kept?.there && (
                <span className="save-note">
                  {apps
                    ? [plural(apps, { one: "# app", other: "# apps" }), plural(count, { one: "# backup", other: "# backups" }), size, t`the newest 5 of each app are kept`].join(" · ")
                    : t`Nothing here yet. Backups appear after you play.`}
                </span>
              )}
            </span>
            {kept?.there && (
              <button className="save-button" disabled={busy} onClick={() => void native.savesFolderOpen(folder).catch((e) => setSaid({ text: String(e), error: true }))}>
                <FolderOpen size={14} aria-hidden="true" /> <Trans>Open folder</Trans>
              </button>
            )}
            <button className="save-button" disabled={busy} onClick={() => void choose()}>
              <Trans comment="Button: choose another sync folder">Change</Trans>
            </button>
            <button className="save-button" disabled={busy} onClick={() => setAsking(true)}>
              <Trans comment="Button: no longer keep save backups in the sync folder">Stop using</Trans>
            </button>
          </div>
          {asking && (
            <div className="cloud-app-actions">
              <span className="save-note">
                <Trans>Stop keeping backups in this folder? The backups in it stay there, and the ones on this computer aren&apos;t touched.</Trans>
              </span>
              <button className="save-button danger" onClick={stop}>
                <Trans comment="Button: no longer keep save backups in the sync folder">Stop using</Trans>
              </button>
              <button className="save-button" onClick={() => setAsking(false)}>
                <Trans>Cancel</Trans>
              </button>
            </div>
          )}
          {kept && !kept.there && (
            <p className="save-said error" role="alert">
              <Trans>Quiver can&apos;t find this folder right now. If it&apos;s on a drive, plug it in. Backups carry on on this computer and go in the folder once it&apos;s back.</Trans>
            </p>
          )}
        </>
      )}
      {said && (
        <p className={said.error ? "save-said error" : "save-said"} role={said.error ? "alert" : "status"}>
          {said.text}
        </p>
      )}
      {folder && Boolean(apps) && (
        <ul className="cloud-apps">
          {kept!.apps.map((app) => {
            const entry = catalog[app.id];
            const name = entry?.projectName ?? app.label;
            const art = entry?.artwork ?? entry?.libraryArt?.logo;
            const date = when(app.newest.at);
            const device = app.newest.device;
            return (
              <li key={app.id} className="cloud-app sync-app-row">
                <span className="cloud-app-icon">
                  <Artwork src={art} sources={art ? cardSources(art, "36px") : undefined} name={name} />
                </span>
                <span className="save-main">
                  <span>{name}</span>
                  <span className="save-note">
                    {[plural(app.copies, { one: "# backup", other: "# backups" }), sizeLabel(app.size), t`Newest from ${device}, ${date}`, !installs[app.id] && t`not installed on this computer`].filter(Boolean).join(" · ")}
                  </span>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
