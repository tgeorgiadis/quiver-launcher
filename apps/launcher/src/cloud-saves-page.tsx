/**
 * Settings, Cloud saves: everything the account keeps, across every app. A bar of the room used, then each app that
 * has copies, biggest first; an app opens to its copies, which can be removed, and for an app installed here restored,
 * with a way to send its saves now and to open its save folder. An app's own Saves tab (saves-tab.tsx) has the same
 * copies beside its backups; this is where to see what they add up to and to make room.
 */
import { useCallback, useEffect, useState } from "react";
import { ChevronDown, ChevronRight, CloudDownload, CloudUpload, FolderOpen, Trash2 } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { sizeLabel } from "./downloads";
import { native } from "./native";
import { onSavesChanged, savesChanged, savesOf } from "./saves";
import { fetchCopy, sendBackup, type CloudCopy, type CloudUsage } from "./cloud-saves";
import { SYSTEM_NAMES, when } from "./saves-tab";
import { useAccount } from "./account";
import { useLauncher } from "./store";

type App = CloudUsage["apps"][number];

export function CloudSavesSettings() {
  const { settings, setSettings, installs, catalog } = useLauncher();
  const account = useAccount();
  const api = account.cloudSaves;
  /** Undefined while it loads; null when the account can't be reached. */
  const [usage, setUsage] = useState<CloudUsage | null | undefined>(undefined);
  const [open, setOpen] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  /** The copy Restore asks about, or the app Remove all asks about. */
  const [asking, setAsking] = useState<string | null>(null);
  /** What was last said, under the app it's about. */
  const [said, setSaid] = useState<{ app: string; text: string; error?: true } | null>(null);

  const load = useCallback(() => {
    if (!api) return setUsage(undefined);
    // A site from before this page lists each app's copies as a number: taken as out of reach.
    api.usage().then((u) => setUsage(u && u.apps.every((app) => Array.isArray(app.copies)) ? u : null), () => setUsage(null));
  }, [Boolean(api)]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    load();
    return onSavesChanged(load);
  }, [load]);

  /** One thing at a time: the buttons wait, and what it says goes under its app. */
  const run = (app: App, work: () => Promise<string | null>) => {
    setBusy(true);
    setSaid(null);
    setAsking(null);
    work()
      .then(
        (text) => text && setSaid({ app: app.entryId, text }),
        (e) => setSaid({ app: app.entryId, text: typeof e === "string" ? e : t`Couldn't reach your cloud saves. Check your connection and try again.`, error: true }),
      )
      .finally(() => (setBusy(false), load()));
  };
  /** The app as installed here, for what needs its files; null when it isn't. */
  const here = (app: App) => {
    const install = installs[app.entryId];
    return install ? savesOf(app.entryId, install, catalog[app.entryId]) : null;
  };
  const restore = (app: App, copy: CloudCopy) =>
    run(app, async () => {
      const of = here(app);
      if (!api || !of) return null;
      await fetchCopy(of, copy, { api, native });
      savesChanged(app.entryId);
      return t({ message: "Restored.", comment: "Said after a save backup was put back" });
    });
  const remove = (app: App, what: { id: string } | { entryId: string }) =>
    run(app, async () => {
      await api?.remove(what);
      return null;
    });
  const sendNow = (app: App) =>
    run(app, async () => {
      const of = here(app);
      if (!api || !of) return null;
      if ((await native.savesCloud(of)).off) return t`Cloud saves are switched off for this app on its Saves tab.`;
      const made = await native.savesBackup(of, "manual");
      const backups = (await native.savesInfo(of))?.backups ?? [];
      const newest = made ?? backups.find((b) => b.reason !== "restore") ?? backups[0];
      if (!newest) return t`This app has no saves on this computer yet.`;
      const how = await sendBackup(of, app.entryId, newest, { api, native });
      savesChanged(app.entryId);
      const limit = usage ? sizeLabel(usage.limit) : "";
      if (how === "sent") return t`Sent to your cloud saves.`;
      if (how === "full") return Promise.reject(t`Your cloud saves are full (${limit}). Remove copies to make room.`);
      if (how === "big") return Promise.reject(t`These saves are too big for cloud saves.`);
      return Promise.reject(t`Couldn't send them just now. Try again soon.`);
    });
  const openFolder = (app: App) =>
    run(app, async () => {
      const of = here(app);
      const place = of && (await native.savesInfo(of))?.places.find((p) => p.exists);
      if (!of || !place) return t`This app has no saves on this computer yet.`;
      await native.savesOpen(of, place.template);
      return null;
    });

  const toggle = (
    <div className="telemetry-setting">
      <label className="toggle">
        <input type="checkbox" checked={!settings.cloudSavesOff} onChange={(e) => setSettings({ ...settings, cloudSavesOff: e.target.checked ? undefined : true })} />
        <Trans>Cloud saves</Trans>
      </label>
      <p className="muted">
        <Trans>While you&apos;re signed in, backups of your saves are also kept with your Quiver account, so they follow you to another computer. Each app&apos;s Saves tab has its own switch.</Trans>
      </p>
    </div>
  );
  if (!account.user || !api)
    return (
      <>
        {toggle}
        <p className="muted">
          <Trans>Sign in to keep your saves with your Quiver account, so they follow you to another computer.</Trans>
        </p>
      </>
    );
  if (!usage)
    return (
      <>
        {toggle}
        <p className="muted" role="status">
          {usage === undefined ? t`Loading your cloud saves…` : t`Couldn't reach your cloud saves. Check your connection and try again.`}
        </p>
      </>
    );
  const used = sizeLabel(usage.used);
  const limit = sizeLabel(usage.limit);
  const keep = usage.keep;
  const sending = usage.on && !settings.cloudSavesOff;
  return (
    <>
      {toggle}
      <section className="settings-section cloud-usage" aria-label={t`Space used`}>
        <div
          className={usage.used >= usage.limit ? "storage-bar full" : "storage-bar"}
          role="progressbar"
          aria-label={t`Cloud saves used`}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.min(100, Math.round((usage.used / usage.limit) * 100))}
        >
          <span style={{ width: `${Math.min(100, (usage.used / usage.limit) * 100)}%` }} />
        </div>
        <p className="muted">{t`${used} of ${limit} used. Each app keeps its newest ${keep} copies.`}</p>
        {usage.why === "paused" && (
          <p className="save-said" role="status">
            <Trans>Cloud saves are paused for now, so nothing new is being sent. Your copies are safe, and backups carry on on this computer.</Trans>
          </p>
        )}
        {usage.why === "suspended" && (
          <p className="save-said" role="status">
            <Trans>Quiver has stopped this account adding to its cloud saves. You can still restore and remove the copies you have.</Trans>
          </p>
        )}
        {!usage.apps.length && (
          <p className="muted">
            <Trans>Nothing in your account yet. Copies appear here after you play an app from the catalog.</Trans>
          </p>
        )}
        <ul className="cloud-apps">
          {usage.apps.map((app) => {
            const [newest] = app.copies;
            const count = app.copies.length;
            const size = sizeLabel(app.size);
            const date = when(newest.createdAt);
            const lastDevice = newest.device;
            const name = app.name;
            const installed = Boolean(installs[app.entryId]);
            const shown = open === app.entryId;
            return (
              <li key={app.entryId} className="cloud-app">
                <button className="cloud-app-row" aria-expanded={shown} onClick={() => (setOpen(shown ? null : app.entryId), setAsking(null))}>
                  {shown ? <ChevronDown size={16} aria-hidden="true" /> : <ChevronRight size={16} aria-hidden="true" />}
                  <span className="save-main">
                    <span>{name}</span>
                    <span className="save-note">
                      {[plural(count, { one: "# copy", other: "# copies" }), size, t`Last synced ${date} from ${lastDevice}`].join(" · ")}
                    </span>
                  </span>
                </button>
                {shown && (
                  <div className="cloud-app-copies">
                    {!app.allowed && (
                      <p className="muted save-source">
                        <Trans>Quiver has switched cloud saves off for this app. These copies stay until you remove them.</Trans>
                      </p>
                    )}
                    {!installed && (
                      <p className="muted save-source">
                        <Trans>This app isn&apos;t installed on this computer. Install it to restore a copy.</Trans>
                      </p>
                    )}
                    {said?.app === app.entryId && (
                      <p className={said.error ? "save-said error" : "save-said"} role={said.error ? "alert" : "status"}>
                        {said.text}
                      </p>
                    )}
                    {app.copies.map((copy) => {
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
                            {asking === copy.id && (
                              <span className="save-note">
                                <Trans>Restore this copy? Your saves now are backed up first.</Trans>
                              </span>
                            )}
                          </span>
                          {asking === copy.id ? (
                            <>
                              <button className="save-button primary" disabled={busy} onClick={() => restore(app, copy)}>
                                <Trans>Restore</Trans>
                              </button>
                              <button className="save-button" disabled={busy} onClick={() => setAsking(null)}>
                                <Trans>Cancel</Trans>
                              </button>
                            </>
                          ) : (
                            <>
                              {installed && (
                                <button className="save-button" disabled={busy} onClick={() => setAsking(copy.id)}>
                                  <CloudDownload size={14} aria-hidden="true" /> <Trans>Restore</Trans>
                                </button>
                              )}
                              <button className="save-button" disabled={busy} aria-label={t`Remove this copy from your account`} title={t`Remove`} onClick={() => remove(app, { id: copy.id })}>
                                <Trash2 size={14} aria-hidden="true" />
                              </button>
                            </>
                          )}
                        </div>
                      );
                    })}
                    <div className="cloud-app-actions">
                      {installed && sending && app.allowed && (
                        <button className="save-button" disabled={busy} onClick={() => sendNow(app)}>
                          <CloudUpload size={14} aria-hidden="true" /> <Trans>Send saves now</Trans>
                        </button>
                      )}
                      {installed && (
                        <button className="save-button" disabled={busy} onClick={() => openFolder(app)}>
                          <FolderOpen size={14} aria-hidden="true" /> <Trans>Open save folder</Trans>
                        </button>
                      )}
                      {asking === app.entryId ? (
                        <>
                          <span className="save-note">{t`Remove every copy of ${name} from your account? Saves on your computers aren't touched.`}</span>
                          <button className="save-button danger" disabled={busy} onClick={() => remove(app, { entryId: app.entryId })}>
                            <Trans>Remove all</Trans>
                          </button>
                          <button className="save-button" disabled={busy} onClick={() => setAsking(null)}>
                            <Trans>Cancel</Trans>
                          </button>
                        </>
                      ) : (
                        <button className="save-button" disabled={busy} onClick={() => setAsking(app.entryId)}>
                          <Trash2 size={14} aria-hidden="true" /> <Trans>Remove all copies</Trans>
                        </button>
                      )}
                    </div>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </section>
    </>
  );
}
