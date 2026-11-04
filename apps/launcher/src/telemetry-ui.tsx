/** Usage data in the window: starting it, the switch in Settings, and the first-run notice. */
import { useEffect } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { telemetryContext, useLauncher, type Settings } from "./store";
import { useAccount } from "./account";
import { setTelemetryAccount, setTelemetryEnabled, setTelemetryLocal, startTelemetry, track, useTelemetryChoice } from "./telemetry";

let started = false;

/** Starts usage data once, following this computer's choice and, signed in, the account's. */
export function useTelemetrySetup() {
  const { config, settings, setSettings } = useLauncher();
  const { ready, user } = useAccount();
  useEffect(() => setTelemetryLocal(!settings.telemetryOff, Boolean(settings.telemetryConsent)), [settings.telemetryOff, settings.telemetryConsent]);
  useEffect(() => {
    if (started) return;
    started = true;
    startTelemetry(telemetryContext(config));
    track("launcher_started", { first_run: !settings.launched });
    if (!settings.launched) setSettings({ ...settings, launched: true });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(
    () => setTelemetryAccount(!ready ? undefined : user && { optOut: Boolean(user.analyticsOptOut) }),
    [ready, Boolean(user), user?.analyticsOptOut], // eslint-disable-line react-hooks/exhaustive-deps
  );
}

/** The switch: on or off here, and on the account too while signed in, as on the website. */
export function useTelemetrySwitch() {
  const { settings, setSettings } = useLauncher();
  const { user, setAnalytics } = useAccount();
  const choice = useTelemetryChoice();
  const setEnabled = (enabled: boolean, more: Partial<Settings> = {}) => {
    setSettings({ ...settings, telemetryOff: enabled ? undefined : true, telemetryConsent: enabled ? true : undefined, ...more });
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
        <Trans>It isn&apos;t linked to your Quiver account.</Trans>
      </p>
      {signedIn && (
        <p className="muted">
          <Trans>You&apos;re signed in, so this follows your Quiver account&apos;s setting, as on quiverlauncher.com.</Trans>
        </p>
      )}
    </div>
  );
}

/**
 * Once, on the first start: what's sent, and the way to turn it off. Where the player must say yes first, it asks
 * until they answer, whatever an earlier version told them. Above the list, never over it.
 */
export function TelemetryNotice() {
  const { settings, setSettings } = useLauncher();
  const { available, enabled, ask, setEnabled } = useTelemetrySwitch();
  if (ask)
    return (
      <div className="banner telemetry-notice" role="status" aria-label={t`Usage data`}>
        <p>
          <Trans>Can Quiver Launcher send usage data to help improve it? It isn&apos;t linked to your Quiver account, and nothing is sent unless you allow it. You can change this in Settings.</Trans>
        </p>
        <button className="primary" onClick={() => setEnabled(true, { telemetryNoticeSeen: true })}>
          <Trans comment="Allow usage data to be sent">Allow</Trans>
        </button>
        <button onClick={() => setEnabled(false, { telemetryNoticeSeen: true })}>
          <Trans comment="Don't send usage data">Don&apos;t allow</Trans>
        </button>
      </div>
    );
  if (!available || !enabled || settings.telemetryNoticeSeen) return null;
  return (
    <div className="banner telemetry-notice" role="status" aria-label={t`Usage data`}>
      <p>
        <Trans>Quiver Launcher sends usage data to help improve it. It isn&apos;t linked to your Quiver account. You can turn this off in Settings.</Trans>
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
