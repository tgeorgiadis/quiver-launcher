/**
 * The first start's setup: a few questions in turn, in place of a banner
 * for each. The language, where apps install, a Quiver Launcher 3 library
 * to import (and whether its apps move there), where the player keeps their game copies, how apps update,
 * usage data, and signing in, each answered or skipped, and
 * Settings has them all after. Only on a computer's first start; Skip setup
 * leaves what wasn't answered to the banners that ask it otherwise.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Bug, ChevronLeft, ChevronRight, CircleCheck, Clock3, Cloud, Disc3, Download, FolderSearch, Gamepad2, Languages, Laptop, ListVideo, Maximize, MessageSquare, Monitor, RefreshCw, Save, Share2, ShieldCheck, Sofa, Sparkles, Tv, UserRound, Users, type LucideIcon } from "lucide-react";
import { useLauncher, type Settings } from "./store";
import { useAccount } from "./account";
import { AppsFolder, LanguageSetting, MoveOldApps } from "./settings-page";
import { notImported, OldLibraryStep } from "./old-library";
import { GameCopySettings } from "./own-copy-settings";
import { AppUpdatesSetting } from "./app-updates";
import { TelemetrySetting, useTelemetrySwitch } from "./telemetry-ui";
import { track, trackScreen } from "./telemetry";

type Step = "welcome" | "apps" | "v3" | "copies" | "updates" | "usage" | "account";

/** Whether a start shows the setup: the first on this computer, until it's done. */
export const showsSetup = (settings: Pick<Settings, "launched" | "setupDone">) => !settings.launched && !settings.setupDone;

/** `onClose` says whether a Quiver Launcher 3 library came over, so the library shows its apps rather than Browse. */
export function Setup({ onClose, onSignIn }: { onClose: (brought: boolean) => void; onSignIn: () => void }) {
  const { setSettings, config } = useLauncher();
  const { available } = useTelemetrySwitch();
  const phone = config.os === "android" || config.os === "ios";
  // Quiver Launcher 3, an apps folder and game copies are a computer's; usage data only where it's sent.
  // Where apps install comes before Quiver Launcher 3, whose apps can then move there.
  const steps = (["welcome", !phone && "apps", !phone && "v3", !phone && "copies", "updates", available && "usage", "account"] as const).filter(Boolean) as Step[];
  const [at, setAt] = useState(0);
  const step = steps[Math.min(at, steps.length - 1)];
  const [offering, setOffering] = useState(false);
  // A Quiver Launcher 3 library that came over on this run: the names of the apps that couldn't, and its installs' folders.
  const [imported, setImported] = useState<{ missing: string[]; dirs: string[] } | null>(null);
  const brought = Boolean(imported);
  // An import under way: the setup's own buttons wait for it.
  const [busy, setBusy] = useState(false);
  const dialog = useRef<HTMLElement>(null);
  useEffect(() => void trackScreen({ screen: "setup", step }), [step]);
  // Each step starts on what answers it, so a controller's A goes on.
  useEffect(() => {
    const body = dialog.current;
    (body?.querySelector<HTMLElement>('.setup-step button.primary, .setup-step .add-choice[aria-checked="true"], .setup-step .add-choice:not([role="radio"])') ?? body?.querySelector<HTMLElement>(".setup-next"))?.focus();
  }, [step, brought]);
  /** Leaving a step answers it: its banner has nothing more to ask. */
  const answered = (s: Step): Partial<Settings> =>
    s === "v3" ? { oldLibraryNoticeSeen: true } : s === "usage" ? { telemetryNoticeSeen: true } : s === "copies" ? { copyFoldersAsked: true } : s === "updates" ? { appUpdatesAsked: true } : {};
  // From the settings as they are by then: an import that finishes on a step saves the apps it hid.
  const finish = (skipped: boolean) => {
    setSettings((s) => ({ ...s, ...(skipped ? {} : answered(step)), setupDone: true }));
    track("setup_finished", { skipped, step, brought });
    onClose(brought);
  };
  const next = () => {
    if (at >= steps.length - 1) return finish(false);
    setSettings((s) => ({ ...s, ...answered(step) }));
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
    v3: imported ? t`Your Quiver Launcher 3 apps are in your library` : offering ? t`Bring your apps over from Quiver Launcher 3?` : t`Coming from Quiver Launcher 3?`,
    apps: t`Where apps install`,
    copies: t`Where are your games?`,
    updates: t`How should apps update?`,
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
            {step === "v3" && imported && (
              <>
                {imported.missing.length > 0 && <p role="status">{notImported(imported.missing)}</p>}
                <MoveOldApps dirs={imported.dirs} onBusy={setBusy} onKeep={() => onward.current()} />
              </>
            )}
            {step === "v3" && !imported && (
              <OldLibraryStep
                onOffer={setOffering}
                onBusy={setBusy}
                onImported={(missing, dirs) => {
                  // The step stays, to say what came and offer to move its installs. Here, not by the step's onBusy,
                  // which goes with the step.
                  setImported({ missing, dirs });
                  setOffering(false);
                  setBusy(false);
                }}
              />
            )}
            {step === "apps" && <AppsStep onBusy={setBusy} />}
            {step === "copies" && <CopiesStep />}
            {step === "updates" && <AppUpdatesSetting inSetup />}
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

/**
 * The first step: a word from Quiver's developer on what Quiver Launcher 4 is to someone who knows 3 (rebuilt, the
 * same things and some more, some of it in new places), then what the setup is, and the language.
 */
function Welcome() {
  return (
    <>
      <p>
        <Trans>Quiver Launcher is one place to find, install, update and play the games and apps people release on GitHub and GitLab, such as modern ports (source ports and recompilations) and fan projects.</Trans>
      </p>
      <p>
        <Trans>Quiver Launcher 4 has been rebuilt from the ground up. I've tried to make sure it does everything Quiver Launcher 3 did, with some extras:</Trans>
      </p>
      <FeatureTour />
      <p>
        <Trans>Some things look and work differently now. Take a moment to find your way around.</Trans>
      </p>
      <p className="muted setup-questions">
        <Trans>A few quick questions to get you started. You can change any of these later in Settings.</Trans>
      </p>
      <LanguageSetting />
    </>
  );
}

type Feature = { key: string; title: string; text: string; art: ReactNode };

/** A picture for a feature: its icon large, with smaller ones round it for what it does. */
const art = (Main: LucideIcon, around: LucideIcon[]) => (
  <>
    <span className="tour-main">
      <Main size={46} strokeWidth={1.6} />
    </span>
    {around.map((Icon, i) => (
      <span key={i} className={`tour-around tour-around-${i}`}>
        <Icon size={20} />
      </span>
    ))}
  </>
);

/** What's new in Quiver Launcher 4, a slide at a time, each with a picture. */
function features(): Feature[] {
  return [
    { key: "cloud", title: t`Cloud saves`, text: t`Your saves are backed up each time you play, and when you sign in they follow you to your other computers.`, art: art(Cloud, [Save, Laptop, Monitor]) },
    { key: "account", title: t`Sign in and sync`, text: t`Use the same account as the website to sync your apps and playlists, share feedback and report issues without leaving Quiver.`, art: art(UserRound, [RefreshCw, MessageSquare, Bug]) },
    { key: "easy", title: t`Easy setup`, text: t`For apps that support it, you only provide your ROM or disc image, and Quiver finds it in the folders you set up.`, art: art(Disc3, [FolderSearch, Sparkles, CircleCheck]) },
    {
      key: "protection",
      title: t`Simpler protection`,
      text: t`Get updates right away, or wait 48 hours in case a release is harmful, which gives the Quiver team time to withdraw it.`,
      art: (
        <>
          {art(ShieldCheck, [Download, Users])}
          <span className="tour-hours" aria-hidden="true">
            <Clock3 size={14} /> 48h
          </span>
        </>
      ),
    },
    {
      key: "views",
      title: t`Two library views`,
      text: t`List, inspired by Steam, and Grid, with box art, covers or icons, and your playlists along the top or in a side panel.`,
      art: (
        <>
          <span className="tour-list">
            {[0, 1, 2, 3].map((i) => (
              <span key={i}>
                <i />
                <b />
              </span>
            ))}
          </span>
          <span className="tour-grid">
            {[0, 1, 2, 3, 4, 5].map((i) => (
              <i key={i} />
            ))}
          </span>
        </>
      ),
    },
    { key: "tv", title: t`TV mode`, text: t`A full-screen launcher made for playing with a controller from the couch.`, art: art(Tv, [Gamepad2, Sofa, Maximize]) },
    { key: "playlists", title: t`Shared playlists`, text: t`Share your playlists, and add other people's that stay synced.`, art: art(ListVideo, [Share2, Users, RefreshCw]) },
    {
      key: "languages",
      title: t`Ten languages`,
      text: t`Quiver speaks ten languages. Choose yours below.`,
      art: (
        <>
          {art(Languages, [])}
          {/* Each in itself, so no translation. */}
          {["Deutsch", "Français", "한국어", "Español", "中文", "Русский"].map((name, i) => (
            <span key={name} className={`tour-word tour-word-${i}`} lang={["de", "fr", "ko", "es", "zh", "ru"][i]}>
              {name}
            </span>
          ))}
        </>
      ),
    },
  ];
}

/** How long a slide shows before the next, while nobody's looking at it or has picked one. */
const TOUR_STEP = 6000;

/**
 * Quiver Launcher 4's new features as slides: a picture, a name and a line each, one at a time. Moves on by itself until
 * the player picks one (an arrow, a dot or a swipe), and holds while the pointer or the focus is on it; not at all for a
 * player who'd rather have less motion. Every slide is laid in the same place, so the tallest sets the height and
 * nothing below jumps.
 */
function FeatureTour() {
  const slides = features();
  const total = slides.length;
  const [at, setAt] = useState(0);
  const [picked, setPicked] = useState(false);
  const [held, setHeld] = useState(false);
  const still = useRef(window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false);
  const go = (i: number) => {
    setPicked(true);
    setAt((i + total) % total);
  };
  useEffect(() => {
    if (picked || held || still.current) return;
    const timer = window.setTimeout(() => setAt((i) => (i + 1) % total), TOUR_STEP);
    return () => window.clearTimeout(timer);
  }, [at, picked, held, total]);
  const swipe = useRef<number | null>(null);
  const n = at + 1;
  return (
    <section
      className="tour"
      aria-roledescription={t({ message: "carousel", comment: "What a screen reader calls the setup's slides of new features" })}
      aria-label={t`New in Quiver Launcher 4`}
      onPointerEnter={(e) => e.pointerType === "mouse" && setHeld(true)}
      onPointerLeave={() => setHeld(false)}
      onFocus={() => setHeld(true)}
      onBlur={(e) => !e.currentTarget.contains(e.relatedTarget as Node | null) && setHeld(false)}
      onPointerDown={(e) => (swipe.current = e.clientX)}
      onPointerUp={(e) => {
        const from = swipe.current;
        swipe.current = null;
        if (from === null || Math.abs(e.clientX - from) < 40) return;
        go(at + (e.clientX < from ? 1 : -1));
      }}
    >
      <div className="tour-slides" aria-live={picked ? "polite" : "off"}>
        {slides.map((slide, i) => (
          <div
            key={slide.key}
            className="tour-slide"
            data-feature={slide.key}
            data-shown={i === at || undefined}
            role="group"
            aria-roledescription={t({ message: "slide", comment: "What a screen reader calls one of the setup's slides of new features" })}
            aria-label={t`${n} of ${total}`}
            aria-hidden={i !== at || undefined}
            inert={i !== at || undefined}
          >
            <div className="tour-art" aria-hidden="true">
              {slide.art}
            </div>
            <h3>{slide.title}</h3>
            <p>{slide.text}</p>
          </div>
        ))}
      </div>
      <div className="tour-nav">
        <button className="icon" aria-label={t`Previous feature`} title={t`Previous feature`} onClick={() => go(at - 1)}>
          <ChevronLeft size={18} />
        </button>
        <div className="tour-dots">
          {slides.map((slide, i) => (
            <button key={slide.key} className="tour-dot" aria-label={slide.title} title={slide.title} aria-current={i === at || undefined} onClick={() => go(i)} />
          ))}
        </div>
        <button className="icon" aria-label={t`Next feature`} title={t`Next feature`} onClick={() => go(at + 1)}>
          <ChevronRight size={18} />
        </button>
      </div>
    </section>
  );
}

/** Where apps install: the one folder, with a way to change it. The rest of Settings' apps folders waits there. */
function AppsStep({ onBusy }: { onBusy: (busy: boolean) => void }) {
  return (
    <>
      <p className="muted">
        <Trans>New apps install here. Pick another drive or folder if this one is short of space, such as a Steam Deck's microSD card.</Trans>
      </p>
      <AppsFolder inSetup onBusy={onBusy} />
    </>
  );
}

function CopiesStep() {
  return (
    <>
      <p className="muted">
        <Trans>Most apps need your own copy of a game, like a ROM or a disc image, and Quiver Launcher has Easy setup for some of them. Add folders for a console or two now, and add more in Settings anytime.</Trans>
      </p>
      <GameCopySettings inSetup />
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
