/**
 * Quiver Launcher updating itself: checked when it starts and from Settings,
 * offered in a banner above the list, and installed in one click where this
 * copy can update itself (otherwise the release page opens).
 */
import { useEffect, useSyncExternalStore } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { native, type LauncherUpdate } from "./native";
import { useLauncher } from "./store";

type State = {
  available: LauncherUpdate | null;
  checking: boolean;
  /** Settings asked, and there's nothing newer. */
  upToDate: boolean;
  dismissed: boolean;
  installing: { done: number; total?: number | null } | null;
  error: string | null;
};

let state: State = { available: null, checking: false, upToDate: false, dismissed: false, installing: null, error: null };
const listeners = new Set<() => void>();
const set = (change: Partial<State>) => {
  state = { ...state, ...change };
  listeners.forEach((l) => l());
};
const useUpdateState = () =>
  useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    () => state,
  );

async function check(preview: boolean, asked: boolean) {
  set({ checking: true, error: null, upToDate: false });
  try {
    const available = await native.checkUpdate(preview);
    set({ available, upToDate: asked && !available, dismissed: asked ? false : state.dismissed });
  } catch (e) {
    // A check that fails on its own is quiet; one the player asked for says why.
    if (asked) set({ error: String(e) });
  } finally {
    set({ checking: false });
  }
}

async function install(version: string) {
  set({ installing: { done: 0 }, error: null });
  const off = native.onUpdateProgress((p) => set({ installing: p }));
  try {
    // On success this copy closes and the new one starts.
    await native.installUpdate(version);
  } catch (e) {
    set({ installing: null, error: String(e) });
  } finally {
    void off.then((stop) => stop());
  }
}

/** Checks once when the launcher starts. */
export function useUpdateCheck() {
  const { settings } = useLauncher();
  useEffect(() => void check(Boolean(settings.previewUpdates), false), []); // eslint-disable-line react-hooks/exhaustive-deps
}

/** The launcher's own update, for Downloads: what's out, and installing it. */
export function useLauncherUpdate() {
  const { available, installing, error } = useUpdateState();
  return { available, installing, error, install };
}

const percent = (p: { done: number; total?: number | null }) => (p.total ? Math.floor((p.done / p.total) * 100) : null);

/** Above the list while an update is waiting. */
export function UpdateBanner() {
  const { available, dismissed, installing, error } = useUpdateState();
  if (!available || dismissed) return null;
  const version = available.version;
  const done = installing && percent(installing);
  return (
    <div className="banner launcher-update" role="status" aria-label={t`Launcher update`}>
      {installing ? (
        <p>
          {done === null
            ? t`Updating to ${version}… Quiver Launcher restarts when it's done.`
            : t`Updating to ${version}… ${done}% Quiver Launcher restarts when it's done.`}
        </p>
      ) : (
        <>
          <p>
            {available.preview ? t`A preview of Quiver Launcher ${version} is out.` : t`Quiver Launcher ${version} is out.`}
            {available.manual ? ` ${available.manual}` : ""}
            {error && <span className="job-error"> {error}</span>}
          </p>
          {available.manual ? (
            <button className="primary" onClick={() => void native.openUrl(available.url)}>
              <Trans>Open the release page</Trans>
            </button>
          ) : (
            <>
              <button className="primary" onClick={() => void install(available.version)}>
                {error ? t`Try again` : t`Update now`}
              </button>
              <button onClick={() => void native.openUrl(available.url)}>
                <Trans>What&apos;s new</Trans>
              </button>
            </>
          )}
          <button onClick={() => set({ dismissed: true })}>
            <Trans>Not now</Trans>
          </button>
        </>
      )}
    </div>
  );
}

/** Settings: the version, preview updates, and checking now. */
export function UpdateSettings() {
  const { config, settings, setSettings } = useLauncher();
  const { available, checking, upToDate, error, dismissed } = useUpdateState();
  const version = config.version;
  const newer = available?.version;
  return (
    <section className="settings-section" aria-label="Quiver Launcher">
      <p className="muted">
        <Trans>Version {version}</Trans>
      </p>
      <label className="toggle">
        <input
          type="checkbox"
          checked={Boolean(settings.previewUpdates)}
          onChange={(e) => setSettings({ ...settings, previewUpdates: e.target.checked })}
        />
        <Trans>Include preview updates</Trans>
      </label>
      <button disabled={checking} onClick={() => void check(Boolean(settings.previewUpdates), true)}>
        {checking ? t`Checking…` : t`Check for updates`}
      </button>
      {upToDate && (
        <p role="status">
          <Trans>You have the newest version.</Trans>
        </p>
      )}
      {available && !dismissed && (
        <p role="status">
          <Trans>{newer} is ready to install: see the banner above the list.</Trans>
        </p>
      )}
      {error && !available && (
        <p className="job-error" role="status">
          {error}
        </p>
      )}
    </section>
  );
}
