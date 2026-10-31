/** Usage data in the window: starting it, the switch in Settings, and the first-run notice. */
import { useEffect } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useLauncher, type Settings } from "./store";
import { useAccount } from "./account";
import { setTelemetryAccount, setTelemetryEnabled, setTelemetryLocal, startTelemetry, track, useTelemetryChoice } from "./telemetry";

let started = false;

/** Starts usage data once, following this computer's choice and, signed in, the account's. */
export function useTelemetrySetup() {
  const { config, settings, setSettings } = useLauncher();
  const { ready, user } = useAccount();
  useEffect(() => setTelemetryLocal(!settings.telemetryOff), [settings.telemetryOff]);
  useEffect(() => {
    if (started) return;
    started = true;
    startTelemetry({
      host: config.posthogHost,
      version: config.version,
      os: config.os,
      arch: config.arch,
      home: config.home,
      user: config.user,
      appsDir: config.appsDir,
    });
    track("launcher_started", { first_run: !settings.launched });
    if (!settings.launched) setSettings({ ...settings, launched: true });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(
    () =>
      setTelemetryAccount(
        !ready
          ? undefined
          : user && {
              id: user.id,
              name: user.name,
              role: user.role,
              provider: user.provider,
              createdAt: user.createdAt,
              optOut: Boolean(user.analyticsOptOut),
            },
      ),
    [ready, user?.id, user?.name, user?.role, user?.provider, user?.createdAt, user?.analyticsOptOut], // eslint-disable-line react-hooks/exhaustive-deps
  );
}

/** The switch: on or off here, and on the account too while signed in, as on the website. */
export function useTelemetrySwitch() {
  const { settings, setSettings } = useLauncher();
  const { user, setAnalytics } = useAccount();
  const choice = useTelemetryChoice();
  const setEnabled = (enabled: boolean, more: Partial<Settings> = {}) => {
    setSettings({ ...settings, telemetryOff: enabled ? undefined : true, ...more });
    setTelemetryEnabled(enabled);
    if (user) void setAnalytics(enabled).catch(() => {});
  };
  return { ...choice, setEnabled };
}

/** In Settings. */
export function TelemetrySetting() {
  const { available, enabled, signedIn, setEnabled } = useTelemetrySwitch();
  if (!available) return null;
  return (
    <div className="telemetry-setting">
      <label className="toggle">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
        <Trans>Send usage data</Trans>
      </label>
      <p className="muted">
        <Trans>Helps improve Quiver Launcher: which screens and features get used, and errors. Never your files or folders.</Trans>{" "}
        {signedIn ? (
          <Trans>While you&apos;re signed in, it&apos;s linked to your Quiver account.</Trans>
        ) : (
          <Trans>It&apos;s anonymous while you&apos;re signed out.</Trans>
        )}
      </p>
      {signedIn && (
        <p className="muted">
          <Trans>You&apos;re signed in, so this follows your Quiver account&apos;s setting, as on quiverlauncher.com.</Trans>
        </p>
      )}
    </div>
  );
}

/** Once, on the first start: what's sent, and the way to turn it off. Above the list, never over it. */
export function TelemetryNotice() {
  const { settings, setSettings } = useLauncher();
  const { available, enabled, setEnabled } = useTelemetrySwitch();
  if (!available || !enabled || settings.telemetryNoticeSeen) return null;
  return (
    <div className="banner telemetry-notice" role="status" aria-label={t`Usage data`}>
      <p>
        <Trans>Quiver Launcher sends usage data to help improve it. It&apos;s anonymous unless you sign in. You can turn this off in Settings.</Trans>
      </p>
      <button className="primary" onClick={() => setSettings({ ...settings, telemetryNoticeSeen: true })}>
        <Trans comment="Accept the usage data notice">OK</Trans>
      </button>
      <button onClick={() => setEnabled(false, { telemetryNoticeSeen: true })}>
        <Trans comment="Turn usage data off">Turn off</Trans>
      </button>
    </div>
  );
}
