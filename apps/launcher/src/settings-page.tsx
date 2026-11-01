/**
 * Settings, a page like Downloads: its sections down the left and the one
 * picked beside them, as in Steam's. In a narrow window (a small window, or a
 * large interface size) the list is the page, and a section opens on its own
 * with a way back to the list; Back from a section goes to the list first.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";
import { ArrowLeft, ChevronRight, FolderOpen, Gamepad2, Library, Monitor, RefreshCw, Wrench } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n, LANGUAGES, type Language } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import { useLauncher } from "./store";
import { pageArea } from "./spatial";
import { native } from "./native";
import { PlatformChoice, ViewOptions } from "./library-view";
import { Snapshots } from "./recovery";
import { FindOldLibrary } from "./old-library";
import { ControlSettings } from "./controls";
import { WindowsRunnerSetting } from "./launch-options";
import { UpdateSettings } from "./update";
import { GithubTokenSetting } from "./github-token";
import { TelemetrySetting } from "./telemetry-ui";
import type { Theme } from "./theme";

type Section = "general" | "library" | "apps" | "controllers" | "updates" | "advanced";
// Names put in the language when shown, not here, where the module loads before any is picked.
const SECTIONS: [Section, MessageDescriptor, ReactNode][] = [
  ["general", msg`General`, <Monitor size={17} />],
  ["library", msg`Library`, <Library size={17} />],
  ["apps", msg`Apps folder`, <FolderOpen size={17} />],
  ["controllers", msg`Controllers`, <Gamepad2 size={17} />],
  ["updates", msg`Updates`, <RefreshCw size={17} />],
  ["advanced", msg`Advanced`, <Wrench size={17} />],
];
/** The narrowest window, in CSS pixels at the interface size, the list fits beside a section in. */
const SIDE_BY_SIDE_WIDTH = 720;

/** Whether the list and a section fit side by side. */
function useSideBySide() {
  const { config, settings } = useLauncher();
  // The interface size zooms the page by its scale.
  const scale = settings.scale ?? 1;
  const [width, setWidth] = useState(window.innerWidth);
  useEffect(() => {
    const resize = () => setWidth(window.innerWidth);
    window.addEventListener("resize", resize);
    return () => window.removeEventListener("resize", resize);
  }, []);
  return width / scale >= SIDE_BY_SIDE_WIDTH && config.os !== "android" && config.os !== "ios";
}

/** `onRunSetup` opens the first start's setup (setup.tsx) again, from General. */
export function SettingsPage({ back, onBack, onRunSetup }: { back: string; onBack: () => void; onRunSetup: () => void }) {
  const sideBySide = useSideBySide();
  // Side by side, General shows until another is picked; narrow, the list does.
  const [picked, setPicked] = useState<Section | null>(null);
  const section = picked ?? (sideBySide ? "general" : null);
  const label = SECTIONS.find(([key]) => key === section)?.[1];
  const name = label && i18n._(label);
  const backLink = useRef<HTMLButtonElement>(null);
  const list = useRef<HTMLElement>(null);
  const left = useRef<Section | null>(null);
  // Narrow, Escape or a controller's Back leaves the section for the list, ahead of the page's own Back; a dialog over the page takes it instead.
  useEffect(() => {
    if (sideBySide || !picked) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape" || document.querySelector('[role="dialog"]')) return;
      e.stopImmediatePropagation();
      setPicked(null);
    };
    window.addEventListener("keydown", onKey, true);
    return () => window.removeEventListener("keydown", onKey, true);
  }, [sideBySide, picked]);
  // Narrow, the focus follows: onto the way back in a section, and onto the section left on the way back to the list.
  useEffect(() => {
    if (sideBySide) return;
    pageArea().scrollTo(0, 0);
    if (picked) backLink.current?.focus({ preventScroll: true });
    else if (left.current) list.current?.querySelector<HTMLElement>(`[data-section="${left.current}"]`)?.focus({ preventScroll: true });
    left.current = picked;
  }, [sideBySide, picked]);
  return (
    <section className="settings-page" aria-label={t`Settings`} data-side-by-side={sideBySide || undefined}>
      {sideBySide || !section ? (
        <button className="back-link" onClick={onBack}>
          <ArrowLeft size={15} /> {back}
        </button>
      ) : (
        <button ref={backLink} className="back-link" onClick={() => setPicked(null)}>
          <ArrowLeft size={15} /> <Trans>Settings</Trans>
        </button>
      )}
      <h1>{sideBySide || !name ? t`Settings` : name}</h1>
      <div className="settings-columns">
        {(sideBySide || !section) && (
          <nav ref={list} className="settings-nav" aria-label={t`Settings sections`}>
            {SECTIONS.map(([key, label, icon]) => (
              <button key={key} data-section={key} aria-current={key === section ? "page" : undefined} onClick={() => setPicked(key)}>
                {icon}
                <span>{i18n._(label)}</span>
                {!sideBySide && <ChevronRight size={16} className="settings-nav-more" aria-hidden="true" />}
              </button>
            ))}
          </nav>
        )}
        {section && (
          <div className="settings-pane" role="region" aria-label={name}>
            {sideBySide && <h2>{name}</h2>}
            {section === "general" ? (
              <GeneralSettings onRunSetup={onRunSetup} />
            ) : section === "library" ? (
              <LibrarySettings />
            ) : section === "apps" ? (
              <>
                <AppsFolder />
                <WindowsRunnerSetting />
              </>
            ) : section === "controllers" ? (
              <ControlSettings />
            ) : section === "updates" ? (
              <UpdateSettings />
            ) : (
              <GithubTokenSetting />
            )}
          </div>
        )}
      </div>
    </section>
  );
}

/** The window, the interface size and theme, the language, usage data, and the first start's setup again. */
function GeneralSettings({ onRunSetup }: { onRunSetup: () => void }) {
  const { settings, setSettings, config } = useLauncher();
  return (
    <>
      <label className="toggle">
        <input
          type="checkbox"
          checked={Boolean(settings.fullscreen)}
          onChange={(e) => {
            setSettings({ ...settings, fullscreen: e.target.checked });
            void native.setFullscreen(e.target.checked, Boolean(settings.systemTitleBar));
          }}
        />
        <Trans>Full screen</Trans>
      </label>
      <label>
        <Trans comment="Setting: the launcher's full-screen layout for a controller (tv.tsx)">TV mode</Trans>{" "}
        <select
          value={settings.tvMode ?? "auto"}
          onChange={(e) => setSettings({ ...settings, tvMode: e.target.value === "auto" ? undefined : (e.target.value as "on" | "off") })}
        >
          <option value="auto">{t({ message: "In Steam's Game Mode", comment: "TV mode setting: on by itself when the launcher runs in a Steam Deck's Game Mode" })}</option>
          <option value="on">{t({ message: "Always", comment: "TV mode setting" })}</option>
          <option value="off">{t({ message: "Off", comment: "TV mode setting" })}</option>
        </select>
      </label>
      {(config.os === "windows" || config.os === "linux") && (
        <label className="toggle">
          <input
            type="checkbox"
            checked={Boolean(settings.systemTitleBar)}
            onChange={(e) => {
              setSettings({ ...settings, systemTitleBar: e.target.checked });
              void native.setSystemTitleBar(e.target.checked);
            }}
          />
          <Trans>Use the system's title bar (instead of Quiver's own window buttons)</Trans>
        </label>
      )}
      <label>
        <Trans>Interface size</Trans>{" "}
        <select value={settings.scale ?? 1} onChange={(e) => setSettings({ ...settings, scale: Number(e.target.value) })}>
          <option value={0.9}>{t({ message: "Small", comment: "Interface size" })}</option>
          <option value={1}>{t({ message: "Normal", comment: "Interface size" })}</option>
          <option value={1.15}>{t({ message: "Large", comment: "Interface size" })}</option>
          <option value={1.3}>{t({ message: "Extra large", comment: "Interface size" })}</option>
        </select>
      </label>
      <label>
        <Trans>Theme</Trans>{" "}
        <select
          value={settings.theme ?? "system"}
          onChange={(e) => setSettings({ ...settings, theme: e.target.value === "system" ? undefined : (e.target.value as Theme) })}
        >
          <option value="system">{t`Same as the system`}</option>
          <option value="light">{t({ message: "Light", comment: "Theme" })}</option>
          <option value="dark">{t({ message: "Dark", comment: "Theme" })}</option>
        </select>
      </label>
      <LanguageSetting />
      <TelemetrySetting />
      <div className="settings-section">
        <button onClick={onRunSetup}>
          <Trans>Run setup again</Trans>
        </button>
        <p className="muted">
          <Trans>The first start's questions, in turn: the language, adding apps from Quiver Launcher 3, where apps install, usage data and signing in.</Trans>
        </p>
      </div>
    </>
  );
}

/** The language Quiver shows in, here and in the first start's setup (setup.tsx). */
export function LanguageSetting() {
  const { settings, setSettings } = useLauncher();
  return (
    <label>
      <Trans>Language</Trans>{" "}
      <select
        value={settings.language ?? "system"}
        onChange={(e) => setSettings({ ...settings, language: e.target.value === "system" ? undefined : (e.target.value as Language) })}
      >
        <option value="system">{t`Same as the system`}</option>
        {/* Each in itself, so a player finds theirs whatever Quiver shows in now. */}
        {LANGUAGES.map((l) => (
          <option key={l.code} value={l.code} lang={l.code}>
            {l.name}
          </option>
        ))}
      </select>
    </label>
  );
}

/** How the library looks, as in its View menu, which platforms' apps it shows, importing a Quiver Launcher 3 one, and restoring a backup. */
function LibrarySettings() {
  return (
    <>
      <ViewOptions groups />
      <section className="settings-section" aria-label={t`Platforms`}>
        <h3>
          <Trans>Platforms</Trans>
        </h3>
        <PlatformChoice />
      </section>
      <FindOldLibrary />
      <section className="settings-section" aria-label={t`Restore from backup`}>
        <h3>
          <Trans>Restore from backup</Trans>
        </h3>
        <Snapshots />
      </section>
    </>
  );
}

/**
 * Where apps install, and moving them all somewhere else (another drive, or a
 * Games folder): it asks first, says what's moving, and installs there after.
 */
export function AppsFolder() {
  const { config, jobs, appFolders, moveApps } = useLauncher();
  const [asking, setAsking] = useState<string | null>(null);
  const [moving, setMoving] = useState<{ done: number; total: number; folder: string } | null>(null);
  const [message, setMessage] = useState<{ text: string; error?: boolean } | null>(null);
  const busy = Object.values(jobs).some((j) => !("error" in j));
  const count = appFolders().length;
  const dir = config.appsDir;
  useEffect(() => {
    const off = native.onAppsMoving(setMoving);
    return () => void off.then((stop) => stop());
  }, []);
  const pick = async () => {
    setMessage(null);
    const to = await native.pickFolder(t`Choose where to install apps`);
    if (!to) return;
    const refused = await native.appsFolderRefusal(to, appFolders());
    if (refused) setMessage({ text: refused, error: true });
    else setAsking(to);
  };
  const move = (to: string) => {
    setAsking(null);
    setMoving({ done: 0, total: count, folder: "" });
    moveApps(to).then(
      () =>
        setMessage({
          text: count ? t`Moved ${plural(count, { one: "# app", other: "# apps" })}. New apps install here too.` : t`New apps install here.`,
        }),
      (e) => setMessage({ text: String(e), error: true }),
    ).finally(() => setMoving(null));
  };
  const step = moving && Math.min(moving.done + 1, moving.total);
  const total = moving?.total;
  const folder = moving?.folder;
  return (
    <section className="settings-section apps-folder" aria-label={t`Apps folder`}>
      <p className="muted">
        <Trans>Apps are installed in {dir}</Trans>
      </p>
      {asking ? (
        <div className="confirm" role="group" aria-label={t`Move apps`}>
          <p>
            {count
              ? plural(count, {
                  one: `Move # installed app to ${asking}? Its saves and settings go with it. Desktop and Steam shortcuts you made point at the old folder, so make them again after.`,
                  other: `Move # installed apps to ${asking}? Their saves and settings go with them. Desktop and Steam shortcuts you made point at the old folder, so make them again after.`,
                })
              : t`Install apps in ${asking} from now on?`}
          </p>
          <div className="row">
            <button className="primary" onClick={() => move(asking)}>
              {count ? t`Move apps` : t`Use this folder`}
            </button>
            <button onClick={() => setAsking(null)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        </div>
      ) : moving ? (
        <p role="status">
          {folder ? (
            <Trans>
              Moving {folder}… ({step} of {total})
            </Trans>
          ) : (
            <Trans>
              Moving apps… ({step} of {total})
            </Trans>
          )}
        </p>
      ) : (
        <button disabled={busy} title={busy ? t`Wait for downloads to finish first` : undefined} onClick={() => void pick()}>
          <Trans>Change…</Trans>
        </button>
      )}
      {message && (
        <p className={message.error ? "job-error" : "muted"} role="status">
          {message.text}
        </p>
      )}
    </section>
  );
}
