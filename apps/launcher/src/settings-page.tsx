/**
 * Settings, a page like Downloads: its sections down the left and the one
 * picked beside them, as in Steam's. In a narrow window (a small window, or a
 * large interface size) the list is the page, and a section opens on its own
 * with a way back to the list; Back from a section goes to the list first.
 */
import { useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { ArrowLeft, ChevronDown, ChevronRight, FolderOpen, Gamepad2, Library, Monitor, RefreshCw, Wrench } from "lucide-react";
import { msg, plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n, LANGUAGES, type Language } from "@quiverlauncher/i18n";
import type { MessageDescriptor } from "@lingui/core";
import type { Console } from "@quiverlauncher/api";
import { useLauncher } from "./store";
import { foldersOn } from "./detail";
import { WHEEL_SPEEDS, pageArea } from "./spatial";
import { native } from "./native";
import { DEFAULT_MUSIC_VOLUME, musicProblem, watchMusicProblem } from "./music";
import { DEFAULT_BACKGROUND_OPACITY, useBackgroundImage } from "./library-background";
import { BrowseCardOptions, PlatformChoice, ViewOptions } from "./library-view";
import { Snapshots } from "./recovery";
import { FindOldLibrary } from "./old-library";
import { ControlSettings } from "./controls";
import { WindowsRunnerSetting } from "./launch-options";
import { UpdateSettings } from "./update";
import { GithubTokenSetting } from "./github-token";
import { TelemetrySetting } from "./telemetry-ui";
import type { Theme } from "./theme";
import { INTERFACE_SIZES } from "./zoom";

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
                <ConsoleFolders />
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

/**
 * A folder for each console the player picks one for: a new install of an app
 * for that console goes in it, in a folder of its own, not the apps folder.
 * Apps already installed stay where they are. This device's own, as the apps
 * folder is. Every console in the catalog, under its maker, each folded away
 * until opened; makers and their consoles A to Z.
 */
export function ConsoleFolders() {
  const { config, settings, setSettings, consoles, installs } = useLauncher();
  // What's wrong with the folder typed or picked for a console, by its id, until it's changed again.
  const [problems, setProblems] = useState<Record<string, string>>({});
  // Folded away until opened, as few players use it; then each maker folded away too.
  const [shown, setShown] = useState(false);
  const [open, setOpen] = useState<Set<string>>(new Set());
  const folders = settings.consoleFolders ?? {};
  const count = Object.keys(folders).length;
  const save = (next: Record<string, string>) => setSettings({ ...settings, consoleFolders: Object.keys(next).length ? next : undefined });
  const problem = (id: string, text?: string) => setProblems(({ [id]: _, ...rest }) => (text ? { ...rest, [id]: text } : rest));
  /**
   * A console's folder, typed or picked; nothing typed, the apps folder again. Only ever the folder its apps' own
   * folders go in (install.rs `destination`), and never in an installed app's folder, which uninstalling it would take.
   */
  const place = async ({ id }: Console, typed: string) => {
    // Without the separators at its end, as a picker gives it, unless that's all there is to it ("/", "C:\").
    const trimmed = typed.trim();
    const bare = trimmed.replace(/[\\/]+$/, "");
    const to = bare && !bare.endsWith(":") ? bare : trimmed;
    problem(id);
    if (to === (folders[id] ?? "")) return;
    if (!to) {
      const { [id]: _, ...rest } = folders;
      return save(rest);
    }
    // A local app's files are never removed, so a folder in one's is no risk.
    const installed = Object.values(installs).filter((i) => !i.local);
    const refused = await native.consoleFolderRefusal(to, installed.flatMap((i) => (i.dir ? [] : [i.folder])), installed.flatMap((i) => (i.dir ? [i.dir] : [])));
    if (refused) problem(id, refused);
    else save({ ...folders, [id]: to });
  };
  const browse = async (c: Console) => {
    const name = c.name;
    const to = await native.pickFolder(t`Choose where to install ${name} apps`, folders[c.id]);
    if (to) await place(c, to);
  };
  const toggle = (maker: string) => setOpen((o) => new Set(o.has(maker) ? [...o].filter((m) => m !== maker) : [...o, maker]));
  if (!foldersOn(config.os)) return null;
  // The catalog's makers by name, as the library's filters show them; its other platforms last, as the library's sections.
  const other = t`Other platforms`;
  const makerOf = (c: Console) => (!c.brand || c.brand === "OtherPlatforms" ? other : c.brand);
  const byName = (a: string, b: string) => a.localeCompare(b, i18n.locale, { sensitivity: "base" });
  const groups = new Map<string, Console[]>();
  for (const c of consoles) groups.set(makerOf(c), [...(groups.get(makerOf(c)) ?? []), c]);
  const makers = [...groups]
    .sort(([a], [b]) => (a === other ? 1 : b === other ? -1 : byName(a, b)))
    .map(([maker, list]) => [maker, list.sort((a, b) => byName(a.name, b.name))] as const);
  return (
    <section className="settings-section console-folders" aria-label={t`Installation directories for consoles`}>
      <h3>
        <button type="button" className="section-fold" aria-expanded={shown} onClick={() => setShown(!shown)}>
          <Trans>Installation directories for consoles</Trans>
          <ChevronDown size={16} aria-hidden="true" />
          {count > 0 && <span className="muted console-maker-count">{plural(count, { one: "# folder", other: "# folders" })}</span>}
        </button>
      </h3>
      {shown && (
        <>
          <p className="muted">
            <Trans>Set the default path apps install to for each console.</Trans>
          </p>
          <ConsoleMakers makers={makers} folders={folders} problems={problems} open={open} onToggle={toggle} onPlace={(c, to) => void place(c, to)} onBrowse={(c) => void browse(c)} />
        </>
      )}
    </section>
  );
}

/** Each maker, folded away until opened, over its consoles: each one's folder, typed in its box or browsed for. */
function ConsoleMakers({
  makers,
  folders,
  problems,
  open,
  onToggle: toggle,
  onPlace: place,
  onBrowse: browse,
}: {
  makers: (readonly [string, Console[]])[];
  folders: Record<string, string>;
  problems: Record<string, string>;
  open: Set<string>;
  onToggle: (maker: string) => void;
  onPlace: (console: Console, typed: string) => void;
  onBrowse: (console: Console) => void;
}) {
  const appsFolder = t({ message: "Apps folder", comment: "Settings: where a console's apps install when it has no folder of its own" });
  return (
    <ul className="console-makers">
      {makers.map(([maker, list]) => {
        const shown = open.has(maker);
        const count = list.filter((c) => folders[c.id]).length;
        return (
          <li key={maker}>
            <button type="button" className="console-maker" aria-expanded={shown} onClick={() => toggle(maker)}>
              <ChevronRight size={15} aria-hidden="true" className="console-maker-chevron" />
              <span className="console-maker-name">{maker}</span>
              {count > 0 && <span className="muted console-maker-count">{plural(count, { one: "# folder", other: "# folders" })}</span>}
            </button>
            {shown && (
              <ul className="console-folders-list">
                {list.map((c) => {
                  const name = c.name;
                  const folder = folders[c.id] ?? "";
                  const field = `console-folder-${c.id}`;
                  return (
                    <li key={c.id}>
                      <label className="console-folder-name" htmlFor={field}>
                        {name}
                      </label>
                      {/* Kept as typed until it's left or Enter is pressed; anew whenever the saved folder changes (Browse). */}
                      <input
                        key={folder}
                        id={field}
                        className="console-folder-path"
                        defaultValue={folder}
                        placeholder={appsFolder}
                        spellCheck={false}
                        autoComplete="off"
                        aria-invalid={Boolean(problems[c.id])}
                        aria-describedby={problems[c.id] ? `${field}-problem` : undefined}
                        onBlur={(e) => place(c, e.currentTarget.value)}
                        onKeyDown={(e) => e.key === "Enter" && e.currentTarget.blur()}
                      />
                      <button aria-label={t`Browse for a folder for ${name}`} onClick={() => browse(c)}>
                        <Trans comment="Opens a folder picker for a console's apps">Browse…</Trans>
                      </button>
                      {problems[c.id] && (
                        <p id={`${field}-problem`} className="job-error console-folder-problem" role="status">
                          {problems[c.id]}
                        </p>
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </li>
        );
      })}
    </ul>
  );
}
