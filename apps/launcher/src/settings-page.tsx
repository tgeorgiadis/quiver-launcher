/**
 * Settings, a page like Downloads: its sections down the left and the one
 * picked beside them, as in Steam's. In a narrow window (a small window, or a
 * large interface size) the list is the page, and a section opens on its own
 * with a way back to the list; Back from a section goes to the list first.
 */
import { useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { ArrowLeft, ChevronRight, Disc3, FolderOpen, Gamepad2, Library, Monitor, RefreshCw, Wrench } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n, LANGUAGES, type Language } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import { folderClash, samePath, useLauncher, type AppsFolder as AppsFolderRow } from "./store";
import { WHEEL_SPEEDS, pageArea } from "./spatial";
import { native, type AppsMoving } from "./native";
import { DEFAULT_MUSIC_VOLUME, musicProblem, watchMusicProblem } from "./music";
import { DEFAULT_BACKGROUND_OPACITY, useBackgroundImage } from "./library-background";
import { BrowseCardOptions, PlatformChoice, ViewOptions } from "./library-view";
import { Snapshots } from "./recovery";
import { FindOldLibrary } from "./old-library";
import { ControlSettings } from "./controls";
import { WindowsRunnerSetting } from "./launch-options";
import { PortableDefaultSetting } from "./app-files";
import { UpdateSettings } from "./update";
import { GithubTokenSetting } from "./github-token";
import { TelemetrySetting } from "./telemetry-ui";
import type { Theme } from "./theme";
import { INTERFACE_SIZES } from "./zoom";
import { GameCopySettings } from "./own-copy-settings";
import { foldersOn } from "./detail";

type Section = "general" | "library" | "apps" | "copies" | "controllers" | "updates" | "advanced";
// Names put in the language when shown, not here, where the module loads before any is picked.
const SECTIONS: [Section, MessageDescriptor, ReactNode][] = [
  ["general", msg`General`, <Monitor size={17} />],
  ["library", msg`Library`, <Library size={17} />],
  ["apps", msg`Apps folder`, <FolderOpen size={17} />],
  ["copies", msg`Game copies`, <Disc3 size={17} />],
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

/** `onRunSetup` opens the first start's setup (setup.tsx) again, from General. `start`: a section to open on. */
export function SettingsPage({ back, onBack, onRunSetup, start }: { back: string; onBack: () => void; onRunSetup: () => void; start?: Section }) {
  const sideBySide = useSideBySide();
  const { config } = useLauncher();
  // Game copies are for computers: a phone keeps an app's files to itself.
  const sections = SECTIONS.filter(([key]) => key !== "copies" || foldersOn(config.os));
  // Side by side, General shows until another is picked; narrow, the list does.
  const [picked, setPicked] = useState<Section | null>(start ?? null);
  const section = picked ?? (sideBySide ? "general" : null);
  const label = sections.find(([key]) => key === section)?.[1];
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
            {sections.map(([key, label, icon]) => (
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
                <PortableDefaultSetting />
                <WindowsRunnerSetting />
              </>
            ) : section === "copies" ? (
              <GameCopySettings />
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

/**
 * Close to tray, as Quiver Launcher 3 had it, and starting with Windows in the tray (tray.rs): the launcher keeps
 * running, and updating apps, with its window closed. Whether Windows starts it is Windows' to say, so it's read from there.
 */
function TraySettings() {
  const { settings, setSettings, setNotice } = useLauncher();
  const [starts, setStarts] = useState<boolean | null>(null);
  useEffect(() => {
    native.startWithWindows().then(setStarts, () => setStarts(false));
  }, []);
  return (
    <>
      <label className="toggle">
        <input type="checkbox" checked={Boolean(settings.closeToTray)} onChange={(e) => setSettings({ ...settings, closeToTray: e.target.checked || undefined })} />
        <Trans>Close to the system tray (Quiver Launcher keeps running and updating apps)</Trans>
      </label>
      <label className="toggle">
        <input
          type="checkbox"
          disabled={starts === null}
          checked={Boolean(starts)}
          onChange={(e) => {
            const on = e.target.checked;
            native.setStartWithWindows(on).then(
              () => setStarts(on),
              (error) => setNotice(String(error), { stays: true }),
            );
          }}
        />
        <Trans>Start with Windows, in the system tray</Trans>
      </label>
    </>
  );
}

/** Close to tray as the setting has it, and the tray's menu in the player's language (tray.rs; Windows only). */
export function useTray() {
  const { settings, config } = useLauncher();
  const open = t({ message: "Open Quiver Launcher", comment: "Item in the launcher's menu in the Windows system tray" });
  const quit = t({ message: "Quit", comment: "Item in the launcher's menu in the Windows system tray: closes the launcher" });
  useEffect(() => {
    if (config.os === "windows") native.setTray(Boolean(settings.closeToTray), open, quit).catch(() => {});
  }, [config.os, settings.closeToTray, open, quit]);
}

/** Discord's Rich Presence as the setting has it, in the player's language (discord.rs; computers only). */
export function useDiscordPresence() {
  const { settings, config } = useLauncher();
  const idle = t({ message: "Browsing games", comment: "On the player's Discord profile, under Quiver Launcher's name, while no game they started from it is running" });
  const button = t({
    message: "Get Quiver Launcher",
    comment: "Button on the player's Discord profile that opens Quiver Launcher's download page, for their friends. Discord cuts it at 32 characters",
  });
  const enabled = !settings.discordPresenceOff && onComputer(config.os);
  useEffect(() => void native.setDiscordPresence(enabled, { idle, button }).catch(() => {}), [enabled, idle, button]);
}

const onComputer = (os: string) => os === "windows" || os === "linux" || os === "macos";

/** The window, the interface size and theme, the language, usage data, and the first start's setup again. */
function GeneralSettings({ onRunSetup }: { onRunSetup: () => void }) {
  const { settings, setSettings, config } = useLauncher();
  return (
    <>
      <label className="toggle">
        <input
          type="checkbox"
          // Steam's Game Mode is always full screen (lib.rs in_game_mode).
          checked={Boolean(settings.fullscreen || config.gameMode)}
          disabled={config.gameMode}
          onChange={(e) => {
            setSettings({ ...settings, fullscreen: e.target.checked });
            void native.setFullscreen(e.target.checked, Boolean(settings.systemTitleBar));
          }}
        />
        <Trans>Full screen</Trans>
      </label>
      <label>
        <Trans comment="Setting: the launcher's larger, full-screen look for a controller">TV mode</Trans>{" "}
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
      {config.os === "windows" && <TraySettings />}
      <label>
        <Trans>Interface size</Trans>{" "}
        <select value={settings.scale ?? 1} onChange={(e) => setSettings({ ...settings, scale: Number(e.target.value) })}>
          {INTERFACE_SIZES.map((size) => (
            <option key={size} value={size}>
              {new Intl.NumberFormat(i18n.locale, { style: "percent" }).format(size)}
            </option>
          ))}
        </select>
      </label>
      <label>
        <Trans>Mouse wheel speed</Trans>{" "}
        <select value={settings.scrollSpeed ?? 1} onChange={(e) => setSettings({ ...settings, scrollSpeed: Number(e.target.value) === 1 ? undefined : Number(e.target.value) })}>
          {WHEEL_SPEEDS.map((speed) => (
            <option key={speed} value={speed}>
              {speed === 1 ? t({ message: "Normal", comment: "Mouse wheel speed: the system's" }) : t({ message: `${speed}×`, comment: "Mouse wheel speed: times as far as the system's, such as 2×" })}
            </option>
          ))}
        </select>
      </label>
      <label className="toggle">
        <input type="checkbox" checked={!settings.smoothScrollOff} onChange={(e) => setSettings({ ...settings, smoothScrollOff: e.target.checked ? undefined : true })} />
        <Trans comment="Setting: the mouse wheel and a controller's steps ease the page there, rather than jumping">Smooth scrolling</Trans>
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
      {config.os !== "android" && config.os !== "ios" && <MusicSetting />}
      {onComputer(config.os) && (
        <label className="toggle">
          <input type="checkbox" checked={!settings.discordPresenceOff} onChange={(e) => setSettings({ ...settings, discordPresenceOff: e.target.checked ? undefined : true })} />
          <Trans>Show on Discord what I'm playing, or that I'm in Quiver Launcher</Trans>
        </label>
      )}
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

/** How the library looks, as in its View menu, and Browse's cards; which platforms' apps it shows, importing a Quiver Launcher 3 one, and restoring a backup. */
function LibrarySettings() {
  const { config } = useLauncher();
  return (
    <>
      <ViewOptions groups />
      {config.os !== "android" && config.os !== "ios" && <BackgroundSetting />}
      <section className="settings-section" aria-label={t`Browse cards`}>
        <h3>
          <Trans comment="Settings section: how the catalog's cards look in Browse">Browse cards</Trans>
        </h3>
        <BrowseCardOptions />
      </section>
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
 * A picture behind the library's grid (library-background.tsx), as Quiver Launcher 3 had: a file to choose, its opacity
 * as it'll look over the page's background, and none. Not on a phone, whose picker gives a file without a path to read it by.
 */
function BackgroundSetting() {
  const { settings, setSettings } = useLauncher();
  const picture = settings.libraryBackground;
  const name = picture?.split(/[\\/]/).pop();
  const url = useBackgroundImage(picture);
  const opacity = settings.libraryBackgroundOpacity ?? DEFAULT_BACKGROUND_OPACITY;
  // A file it read but can't show.
  const [broken, setBroken] = useState<string>();
  const choose = async () => {
    // Opening where the last one was.
    const dir = picture && name ? picture.slice(0, -name.length) : undefined;
    const picked = await native.pickBackground(t`Choose a picture for the library's background`, dir).catch(() => null);
    if (picked) setSettings({ ...settings, libraryBackground: picked });
  };
  return (
    <section className="settings-section" aria-label={t`Library background`}>
      <h3>
        <Trans>Library background</Trans>
      </h3>
      <p className="muted">{name ?? <Trans>Show a picture of your own behind the library's grid. The Sidebar layout doesn't show it.</Trans>}</p>
      {url && broken !== url && (
        <div className="background-preview">
          <img src={url} alt="" style={{ opacity }} onError={() => setBroken(url)} />
        </div>
      )}
      <div className="music-buttons">
        <button onClick={() => void choose()}>{picture ? <Trans>Change…</Trans> : <Trans>Choose a picture…</Trans>}</button>
        {picture && (
          <button onClick={() => setSettings({ ...settings, libraryBackground: undefined })}>
            <Trans comment="Button: no background music, or no background picture">Remove</Trans>
          </button>
        )}
      </div>
      {picture && (
        <label>
          <Trans comment="How strongly the library's background picture shows">Opacity</Trans>{" "}
          {/* Past 60%, a bright picture is too strong for the library's words over it. */}
          <input type="range" min={10} max={60} step={5} value={Math.round(opacity * 100)} onChange={(e) => setSettings({ ...settings, libraryBackgroundOpacity: Number(e.target.value) / 100 })} />
        </label>
      )}
      {picture && (url === null || (url && broken === url)) && (
        <p className="job-error" role="status">
          <Trans>Quiver couldn't show that picture. It may have been moved or deleted.</Trans>
        </p>
      )}
    </section>
  );
}

/**
 * Music behind the launcher (music.ts), as Quiver Launcher 3 had: a file to choose, its volume, and none. Not on a
 * phone, whose picker gives a file without a path to read it by.
 */
function MusicSetting() {
  const { settings, setSettings } = useLauncher();
  const problem = useSyncExternalStore(watchMusicProblem, musicProblem);
  const music = settings.music;
  const name = music?.split(/[\\/]/).pop();
  const volume = Math.round((settings.musicVolume ?? DEFAULT_MUSIC_VOLUME) * 100);
  const choose = async () => {
    // Opening where the last one was.
    const dir = music && name ? music.slice(0, -name.length) : undefined;
    const picked = await native.pickMusic(t`Choose music to play in the background`, dir).catch(() => null);
    if (picked) setSettings({ ...settings, music: picked });
  };
  return (
    <section className="settings-section" aria-label={t`Background music`}>
      <h3>
        <Trans>Background music</Trans>
      </h3>
      <p className="muted">{name ?? <Trans>Play a music file of your own while Quiver is open. It fades out while you play.</Trans>}</p>
      <div className="music-buttons">
        <button onClick={() => void choose()}>{music ? <Trans>Change…</Trans> : <Trans>Choose music…</Trans>}</button>
        {music && (
          <button onClick={() => setSettings({ ...settings, music: undefined })}>
            <Trans comment="Button: no background music, or no background picture">Remove</Trans>
          </button>
        )}
      </div>
      {music && (
        <label>
          <Trans comment="Background music's volume">Volume</Trans>{" "}
          <input type="range" min={0} max={100} step={5} value={volume} onChange={(e) => setSettings({ ...settings, musicVolume: Number(e.target.value) / 100 })} />
        </label>
      )}
      {problem && (
        <p className="job-error" role="status">
          {problem}
        </p>
      )}
    </section>
  );
}

/**
 * The apps folders: the default, which new apps install in, and each other
 * one the player's apps are kept in (Quiver Launcher 3's, or the one before a
 * Change…). Changing the default keeps every app where it is unless the player
 * asks to move them too; another folder's apps can be moved into the default,
 * or it can become the default itself. A move asks first, says what's moving,
 * and a bar shows how many have moved, and how far a copy to another drive
 * has got.
 */
export function AppsFolder() {
  const { config, jobs, movableApps, appsFolders, switchAppsFolder, moveApps } = useLauncher();
  const [asking, setAsking] = useState<{ to: string } | null>(null);
  // Moving the apps along is the player's choice; keeping them where they are is instant and first.
  const [keep, setKeep] = useState(true);
  const [bring, setBring] = useState(true);
  // The other apps folder whose apps the player asked to move into the default.
  const [bringing, setBringing] = useState<AppsFolderRow | null>(null);
  const [moving, setMoving] = useState<AppsMoving | null>(null);
  // Measuring the apps against the drive's free space takes a while for many: said while it does, with nothing to press.
  const [checking, setChecking] = useState(false);
  const [message, setMessage] = useState<{ text: string; error?: boolean; left?: string[]; failed?: { folder: string; why: string }[] } | null>(null);
  const [states, setStates] = useState<Record<string, { exists: boolean; free?: number | null }>>({});
  const busy = Object.values(jobs).some((j) => !("error" in j));
  const { placed } = movableApps();
  const folders = appsFolders();
  const [home, ...others] = folders;
  const dir = config.appsDir;
  const count = home.apps.length;
  const kept = others.reduce((n, f) => n + f.apps.length, 0);
  const paths = folders.map((f) => f.path).join("\n");
  useEffect(() => {
    // Only where new apps go changing moves nothing, and needs no bar.
    const off = native.onAppsMoving((m) => m.total > 0 && setMoving(m));
    return () => void off.then((stop) => stop());
  }, []);
  useEffect(() => {
    let live = true;
    const list = paths.split("\n");
    native.appsFolderStates(list).then(
      (found) => live && setStates(Object.fromEntries(list.map((p, i) => [p, found?.[i] ?? { exists: true }]))),
      () => {},
    );
    return () => void (live = false);
  }, [paths, moving]);
  const idle = !asking && !bringing && !moving && !checking;
  const waitTitle = busy ? t`Wait for downloads to finish first` : undefined;
  const pick = async () => {
    setMessage(null);
    const to = await native.pickFolder(t`Choose where to install apps`);
    if (!to) return;
    if (samePath(to, dir)) return setMessage({ text: t`Apps already install there.`, error: true });
    const clash = folderClash(to, folders);
    if (clash) return setMessage({ text: clash, error: true });
    setKeep(true);
    setBring(true);
    setAsking({ to });
  };
  const useFolder = (to: string) => {
    setAsking(null);
    setMessage(null);
    switchAppsFolder(to).then(
      () =>
        setMessage({
          text: count
            ? plural(count, {
                one: `New apps install in ${to}. Your # installed app stays where it is and plays from there.`,
                other: `New apps install in ${to}. Your # installed apps stay where they are and play from there.`,
              })
            : t`New apps install in ${to}.`,
        }),
      (e) => setMessage({ text: String(e), error: true }),
    );
  };
  /** Checks there's room, then moves: the default's apps when `inside`, and those of the folders in `from`. */
  const checkThenMove = async (to: string, inside: boolean, from: AppsFolderRow[]) => {
    setAsking(null);
    setBringing(null);
    setMessage(null);
    setChecking(true);
    const apps = [...(inside ? home.apps : []), ...from.flatMap((f) => f.apps)];
    const refused = await native.appsFolderRefusal(to, apps).catch((e) => String(e));
    if (refused) {
      setChecking(false);
      return setMessage({ text: refused, error: true });
    }
    const n = (inside && !samePath(to, dir) ? count : 0) + from.reduce((s, f) => s + f.apps.length, 0);
    // Nothing to move (only where new apps go changes): no bar for it.
    if (n) setMoving({ done: 0, total: n, folder: "", copied: 0, size: 0 });
    setChecking(false);
    moveApps(to, { inside, from: from.map((f) => f.path) }).then(
      ({ left, failed }) =>
        setMessage(
          failed.length
            ? { text: t`Moved ${n - failed.length} of ${plural(n, { one: "# app", other: "# apps" })}.`, error: true, left, failed }
            : { text: n ? t`Moved ${plural(n, { one: "# app", other: "# apps" })}.` : t`New apps install here.`, left },
        ),
      (e) => setMessage({ text: String(e), error: true }),
    ).finally(() => setMoving(null));
  };
  const open = (path: string) => void native.openAppsFolder(path).catch((e) => setMessage({ text: String(e), error: true }));
  return (
    <section className="settings-section apps-folder" aria-label={t`Apps folders`}>
      <ul className="apps-folders">
        {folders.map((f) => {
          const state = states[f.path];
          return (
            <li key={f.path} className="apps-folder-row" data-default={f.default || undefined}>
              <div className="apps-folder-info">
                <p className="apps-folder-name">
                  <span>{f.path}</span>
                  {f.default && <span className="apps-folder-badge">{t`Default`}</span>}
                </p>
                <p className={state && !state.exists ? "job-error" : "muted"}>
                  {f.default ? t`New apps install here.` : t`Kept here from before.`} {plural(f.apps.length, { one: "# app", other: "# apps" })}
                  {state && !state.exists ? ` · ${t`Not found. Is its drive connected?`}` : state?.free != null ? ` · ${t`${space(state.free)} free`}` : ""}
                </p>
              </div>
              {idle && (
                <div className="row">
                  <button onClick={() => open(f.path)} disabled={state ? !state.exists : false}>
                    <Trans>Open</Trans>
                  </button>
                  {f.default ? (
                    <button disabled={busy} title={waitTitle} onClick={() => void pick()}>
                      <Trans>Change…</Trans>
                    </button>
                  ) : (
                    <>
                      <button className="primary" disabled={busy || state?.exists === false} title={waitTitle} onClick={() => setBringing(f)}>
                        <Trans>Move into default</Trans>
                      </button>
                      <button disabled={busy || state?.exists === false} title={waitTitle} onClick={() => useFolder(f.path)}>
                        <Trans>Make default</Trans>
                      </button>
                    </>
                  )}
                </div>
              )}
            </li>
          );
        })}
      </ul>
      {asking ? (
        <div className="confirm move-confirm" role="group" aria-label={t`Change the default apps folder`}>
          <h4>{t`Install new apps in ${asking.to} from now on?`}</h4>
          {count > 0 && (
            <div className="move-choice" role="radiogroup">
              <label className="toggle">
                <input type="radio" name="apps-keep" checked={keep} onChange={() => setKeep(true)} />
                <span>{plural(count, { one: `Keep your # installed app where it is, in ${dir}`, other: `Keep your # installed apps where they are, in ${dir}` })}</span>
              </label>
              <label className="toggle">
                <input type="radio" name="apps-keep" checked={!keep} onChange={() => setKeep(false)} />
                <span>{plural(count, { one: `Move it to ${asking.to} too`, other: `Move them to ${asking.to} too` })}</span>
              </label>
            </div>
          )}
          {!keep && kept > 0 && (
            <label className="toggle">
              <input type="checkbox" checked={bring} onChange={(e) => setBring(e.target.checked)} />
              {plural(kept, { one: "Include # app from your other apps folders", other: "Include # apps from your other apps folders" })}
            </label>
          )}
          {!keep && <MoveNotes placed={placed} />}
          <div className="row">
            <button className="primary" onClick={() => (keep || !count ? useFolder(asking.to) : void checkThenMove(asking.to, true, bring ? others : []))}>
              {keep || !count ? t`Use this folder` : t`Move apps`}
            </button>
            <button onClick={() => setAsking(null)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        </div>
      ) : bringing ? (
        <div className="confirm move-confirm" role="group" aria-label={t`Move apps into the default folder`}>
          <h4>
            {plural(bringing.apps.length, { one: `Move # app from ${bringing.path} into ${dir}?`, other: `Move # apps from ${bringing.path} into ${dir}?` })}
          </h4>
          <MoveNotes placed={placed} />
          <div className="row">
            <button className="primary" onClick={() => void checkThenMove(dir, false, [bringing])}>
              <Trans>Move apps</Trans>
            </button>
            <button onClick={() => setBringing(null)}>
              <Trans>Cancel</Trans>
            </button>
          </div>
        </div>
      ) : moving ? (
        <MovingApps moving={moving} />
      ) : checking ? (
        <div className="apps-moving" role="status">
          <div className="progress waiting" aria-label={t`Checking the folder`} role="progressbar">
            <div className="bar" />
            <span>{t`Checking there's room…`}</span>
          </div>
          <p className="muted">{t`Quiver is measuring the apps and the free space on the drive. This can take a moment.`}</p>
        </div>
      ) : null}
      {message && (
        <div className={message.error ? "job-error" : "muted"} role="status">
          <p>{message.text}</p>
          {message.failed?.length ? (
            <>
              <p>
                {plural(message.failed.length, {
                  one: "This one couldn't move. It's still where it was, with all its files, and plays from there:",
                  other: "These couldn't move. They're still where they were, with all their files, and play from there:",
                })}
              </p>
              <ul className="apps-left">
                {message.failed.map((f) => (
                  <li key={f.folder}>
                    <strong>{f.folder}</strong>: {f.why}.
                  </li>
                ))}
              </ul>
            </>
          ) : null}
          {message.left?.length ? (
            <>
              <p>{t`These old copies moved safely, but some of their files were in use and stayed behind. You can delete them:`}</p>
              <ul className="apps-left">
                {message.left.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            </>
          ) : null}
        </div>
      )}
    </section>
  );
}

/** Bytes as a drive's room is usually said: TB past a thousand GB. */
function space(bytes: number) {
  const gb = bytes / 1e9;
  return gb >= 1000 ? `${(gb / 1000).toFixed(1)} TB` : `${Math.floor(gb)} GB`;
}

/** What a move means, one point a line under its question, so each can be taken in. */
function MoveNotes({ placed }: { placed: number }) {
  return (
    <ul className="move-notes">
      <li>{t`The saves and settings come over too.`}</li>
      {placed > 0 && (
        <li>
          {plural(placed, {
            one: "# app in a folder you chose for it stays where it is.",
            other: "# apps in folders you chose for them stay where they are.",
          })}
        </li>
      )}
      <li>{t`Desktop and Steam shortcuts you made point at the old folder, so make them again after.`}</li>
      <li>{t`Keep Quiver open until it's done. Each app's old copy is deleted only once its new one is checked.`}</li>
    </ul>
  );
}

/** How far a move has got: a bar of the apps moved so far, filling as the one moving copies, and which one that is. */
function MovingApps({ moving }: { moving: AppsMoving }) {
  const { done, total, folder, copied, size } = moving;
  const part = size ? Math.min(copied / size, 1) : 0;
  const width = total ? Math.max(4, Math.floor(((done + part) / total) * 100)) : 4;
  const percent = Math.floor(part * 100);
  return (
    <div className="apps-moving" role="status">
      <div className="progress" aria-label={t`Moving apps`} aria-valuemin={0} aria-valuemax={total} aria-valuenow={done} role="progressbar">
        <div className="bar" style={{ width: `${width}%` }} />
        <span>{t`${done} of ${total} apps moved`}</span>
      </div>
      <p className="muted">
        {folder ? (size ? t`Copying ${folder}… ${percent}%` : t`Moving ${folder}…`) : done ? t`Moving apps…` : t`Getting ready…`} {t`Keep Quiver open until it's done.`}
      </p>
    </div>
  );
}
