/**
 * The first start's setup: a few questions in turn, in place of a banner
 * for each. The language, a Quiver Launcher 3 library to import, where
 * apps install, usage data, and signing in, each answered or skipped, and
 * Settings has them all after. Only on a computer's first start; Skip setup
 * leaves what wasn't answered to the banners that ask it otherwise.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useLauncher, type Settings } from "./store";
import { useAccount } from "./account";
import { AppsFolder, LanguageSetting } from "./settings-page";
import { OldLibraryStep } from "./old-library";
import { TelemetrySetting, useTelemetrySwitch } from "./telemetry-ui";
import { track, trackScreen } from "./telemetry";

type Step = "welcome" | "v3" | "apps" | "usage" | "account";

/** Whether a start shows the setup: the first on this computer, until it's done. */
export const showsSetup = (settings: Pick<Settings, "launched" | "setupDone">) => !settings.launched && !settings.setupDone;

/** `onClose` says whether a Quiver Launcher 3 library came over, so the library shows its apps rather than Browse. */
export function Setup({ onClose, onSignIn }: { onClose: (brought: boolean) => void; onSignIn: () => void }) {
  const { settings, setSettings, config } = useLauncher();
  const { available } = useTelemetrySwitch();
  const phone = config.os === "android" || config.os === "ios";
  // Quiver Launcher 3 and an apps folder are a computer's; usage data only where it's sent.
  const steps = (["welcome", !phone && "v3", !phone && "apps", available && "usage", "account"] as const).filter(Boolean) as Step[];
  const [at, setAt] = useState(0);
  const step = steps[Math.min(at, steps.length - 1)];
  const [offering, setOffering] = useState(false);
  const [brought, setBrought] = useState(false);
  // An import under way: the setup's own buttons wait for it.
  const [busy, setBusy] = useState(false);
  const dialog = useRef<HTMLElement>(null);
  useEffect(() => void trackScreen({ screen: "setup", step }), [step]);
  // Each step starts on what answers it, so a controller's A goes on.
  useEffect(() => {
    const body = dialog.current;
    (body?.querySelector<HTMLElement>(".setup-step button.primary, .setup-step .add-choice") ?? body?.querySelector<HTMLElement>(".setup-next"))?.focus();
  }, [step]);
  /** Leaving a step answers it: its banner has nothing more to ask. */
  const answered = (s: Step): Partial<Settings> =>
    s === "v3" ? { oldLibraryNoticeSeen: true } : s === "usage" ? { telemetryNoticeSeen: true } : {};
  const finish = (skipped: boolean) => {
    setSettings({ ...settings, ...(skipped ? {} : answered(step)), setupDone: true });
    track("setup_finished", { skipped, step, brought });
    onClose(brought);
  };
  const next = () => {
    if (at >= steps.length - 1) return finish(false);
    setSettings({ ...settings, ...answered(step) });
    setOffering(false);
    setAt(at + 1);
  };
  const back = () => at > 0 && !busy && (setOffering(false), setAt(at - 1));
  // As the step is now, for an import that finishes after the render that started it.
  const onward = useRef(next);
  onward.current = next;
  // Escape, or a controller's B, goes back a step; never out of the setup, which Skip setup is for. A dialog over it
  // (signing in) takes it instead.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const dialogs = document.querySelectorAll('[role="dialog"]');
      if (e.key === "Escape" && dialogs[dialogs.length - 1] === dialog.current) back();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });
  const titles: Record<Step, string> = {
    welcome: t`Welcome to Quiver Launcher!`,
    v3: t`Coming from Quiver Launcher 3?`,
    apps: t`Where apps install`,
    usage: t`Usage data`,
    account: t`Sign in to sync your library`,
  };
  const n = at + 1;
  const total = steps.length;
  return (
    <div className="overlay">
      <section ref={dialog} className="detail choose setup" role="dialog" aria-label={t`Set up Quiver Launcher`}>
        <div className="detail-body">
          <p className="muted setup-count">
            <Trans>
              Step {n} of {total}
            </Trans>
          </p>
          <h2>{titles[step]}</h2>
          <div className="setup-step" key={step}>
            {step === "welcome" && <Welcome />}
            {step === "v3" && (
              <OldLibraryStep
                onOffer={setOffering}
                onBusy={setBusy}
                onImported={(missing) => {
                  setBrought(true);
                  // Here, not by the step's onBusy: moving on now takes the step away before it would say so.
                  setBusy(false);
                  // All of them came: on to the next step, which says so. Otherwise the step stays, to say which didn't.
                  if (!missing.length) onward.current();
                }}
              />
            )}
            {step === "apps" && <AppsStep brought={brought} />}
            {step === "usage" && <TelemetrySetting />}
            {step === "account" && <AccountStep onSignIn={onSignIn} />}
          </div>
          <div className="setup-nav">
            <button className="setup-skip" disabled={busy} onClick={() => finish(true)}>
              <Trans>Skip setup</Trans>
            </button>
            {at > 0 && (
              <button disabled={busy} onClick={back}>
                <Trans comment="The setup's previous step">Back</Trans>
              </button>
            )}
            <button className={offering ? "setup-next" : "setup-next primary"} disabled={busy} onClick={next}>
              {offering ? t`Not now` : at < steps.length - 1 ? t({ message: "Next", comment: "The setup's next step" }) : t`Start using Quiver`}
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}

function Welcome() {
  return (
    <>
      <p>
        <Trans>A few quick questions to get you started. You can change any of these later in Settings.</Trans>
      </p>
      <LanguageSetting />
    </>
  );
}

function AppsStep({ brought }: { brought: boolean }) {
  return (
    <>
      {brought && (
        <p role="status">
          <Trans>All done: your Quiver Launcher 3 apps are in your library, and they stay where they are.</Trans>
        </p>
      )}
      <p className="muted">
        <Trans>New apps install here. Pick another drive or folder if this one is short of space, such as a Steam Deck's microSD card.</Trans>
      </p>
      <AppsFolder />
    </>
  );
}

function AccountStep({ onSignIn }: { onSignIn: () => void }) {
  const { user } = useAccount();
  const name = user?.name;
  let body: ReactNode;
  if (name) body = <p role="status">{t`You're signed in as ${name}.`}</p>;
  else
    body = (
      <>
        <p className="muted">
          <Trans>Your library syncs across your devices, and you can review apps. Everything works without an account too.</Trans>
        </p>
        <button className="primary" onClick={onSignIn}>
          <Trans>Sign in</Trans>
        </button>
      </>
    );
  return body;
}
