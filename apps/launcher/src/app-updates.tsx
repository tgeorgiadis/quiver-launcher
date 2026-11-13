/**
 * How apps update: to releases that have been out 48 hours, which leaves Quiver time to withdraw a harmful one, or to
 * the newest right away. Worded by the wait, which is what Quiver can promise: not every release is scanned or tried.
 * The setup asks, Settings → Updates has it after, and a banner asks a player set up before there was a choice.
 * Either way a release Quiver blocked or withdrew is never an update; waiting, the player can still update one app
 * early by asking, and right away, a release VirusTotal hasn't cleared asks first when the player installs it, though
 * an automatic update takes it without asking (store.tsx `installNow`).
 */
import { Rocket, ShieldCheck, Zap } from "lucide-react";
import type { ReactNode } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useLauncher, type Settings } from "./store";
import { track } from "./telemetry";

type Mode = "wait" | "newest";

export const appUpdatesMode = (settings: Pick<Settings, "appUpdates">): Mode => (settings.appUpdates === "newest" ? "newest" : "wait");

/** The setting with `mode` picked, and the question answered. */
export const withAppUpdates = (settings: Settings, mode: Mode): Settings => {
  const { appUpdates: _, ...rest } = settings;
  return { ...rest, ...(mode === "newest" ? { appUpdates: "newest" as const } : {}), appUpdatesAsked: true };
};

/** `inSetup`: in the first start's setup, whose step has the heading. */
export function AppUpdatesSetting({ inSetup = false }: { inSetup?: boolean }) {
  const { settings, setSettings } = useLauncher();
  const mode = appUpdatesMode(settings);
  const pick = (to: Mode) => {
    if (to === mode) return;
    setSettings((s) => withAppUpdates(s, to));
    track("app_updates_mode_set", { mode: to, in_setup: inSetup });
  };
  const option = (to: Mode, icon: ReactNode, title: string, text: string) => (
    <button type="button" role="radio" aria-checked={mode === to} className="add-choice update-choice" onClick={() => pick(to)}>
      {icon}
      <span>
        <strong>{title}</strong>
        <small>{text}</small>
      </span>
    </button>
  );
  return (
    <section className="settings-section app-updates" aria-label={t`App updates`}>
      {!inSetup && (
        <h3>
          <Trans>App updates</Trans>
        </h3>
      )}
      <div className="update-choices" role="radiogroup" aria-label={t`How apps update`}>
        {option(
          "wait",
          <ShieldCheck size={22} aria-hidden="true" />,
          t({ message: "Wait 48 hours (recommended)", comment: "How apps update: only to releases that have been out for 48 hours" }),
          t`Updates arrive 48 hours after they come out. That gives Quiver time to withdraw a harmful release before it reaches you. You can still update an app early yourself.`,
        )}
        {option(
          "newest",
          <Zap size={22} aria-hidden="true" />,
          t({ message: "Newest right away", comment: "How apps update: to the newest release as soon as it's out, without waiting 48 hours" }),
          t`Updates install as soon as they come out, without asking first. If one turns out to be harmful, you may already have it before Quiver can withdraw it. Quiver still stops releases it has blocked.`,
        )}
      </div>
      {!inSetup && <AutoUpdatesSetting />}
      {!inSetup && <PrereleasesSetting />}
    </section>
  );
}

/** Whether apps install their updates by themselves by default; each app can say otherwise in its own settings. */
function AutoUpdatesSetting() {
  const { settings, setSettings } = useLauncher();
  return (
    <div className="prereleases-setting">
      <label className="toggle">
        <input
          type="checkbox"
          checked={Boolean(settings.autoUpdates)}
          onChange={(e) => {
            const on = e.target.checked;
            setSettings((s) => {
              const { autoUpdates: _, ...rest } = s;
              return on ? { ...rest, autoUpdates: true } : rest;
            });
            track("app_auto_updates_set", { on });
          }}
        />
        <Trans comment="Setting: apps install their updates by themselves, without asking">Install updates automatically</Trans>
      </label>
      <p className="muted">
        {appUpdatesMode(settings) === "newest" ? (
          <Trans>Apps install each new release as soon as it's out, without asking. Apps installed with a Windows installer still ask, as their setup needs you.</Trans>
        ) : (
          <Trans>Apps install each new release once its 48 hours are up, without asking. Apps installed with a Windows installer still ask, as their setup needs you.</Trans>
        )}
      </p>
      <p className="muted">
        <Trans>Each app can update by itself, ask first, or stay on its version, whatever this says, from its own settings.</Trans>
      </p>
    </div>
  );
}

/** Whether apps get pre-releases by default; each app can say otherwise in its own settings. */
function PrereleasesSetting() {
  const { settings, setSettings } = useLauncher();
  return (
    <div className="prereleases-setting">
      <label className="toggle">
        <input
          type="checkbox"
          checked={Boolean(settings.prereleases)}
          onChange={(e) => {
            const on = e.target.checked;
            setSettings((s) => {
              const { prereleases: _, ...rest } = s;
              return on ? { ...rest, prereleases: true } : rest;
            });
            track("app_prereleases_set", { on });
          }}
        />
        <Trans comment="Setting: apps also update to releases their developers marked as pre-releases">Include pre-releases</Trans>
      </label>
      <p className="muted">
        <Trans>
          Developers mark some releases as pre-releases: early or experimental builds, such as betas and nightlies. They can bring new features sooner, but they may
          change how things work, break saves or settings, or leave out builds for some systems. Most players should use main releases.
        </Trans>
      </p>
      <p className="muted">
        <Trans>Each app can include pre-releases or not, whatever this says, from its own settings.</Trans>
      </p>
    </div>
  );
}

/**
 * Once, for a player who finished the setup before it asked how apps update (or skipped it): a banner to say there's
 * a choice. Answered by opening it, by Not now, or by the setup's step.
 */
export function AppUpdatesNotice({ onOpen }: { onOpen: () => void }) {
  const { settings, setSettings } = useLauncher();
  if (!settings.setupDone || settings.appUpdatesAsked || settings.appUpdates) return null;
  const answer = () => setSettings((s) => ({ ...s, appUpdatesAsked: true }));
  return (
    <div className="banner app-updates-notice" role="status" aria-label={t`App updates`}>
      <Rocket size={18} aria-hidden="true" />
      <p>
        <Trans>Quiver waits 48 hours before it offers an app's update, so a harmful release can be withdrawn first. If you'd rather have updates as soon as they're out, you can now choose that.</Trans>
      </p>
      <button className="primary" onClick={() => (answer(), onOpen())}>
        <Trans>Choose…</Trans>
      </button>
      <button onClick={answer}>
        <Trans>Not now</Trans>
      </button>
    </div>
  );
}
