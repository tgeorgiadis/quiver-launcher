/**
 * TV mode: a fullscreen, controller-first layer over the launcher, mounted around <App/> (main.tsx). The desktop
 * stays mounted underneath, inert, so both share one store and leaving TV mode finds the desktop as it was. This is
 * what the desktop always has of it; TV mode itself (TvMode.tsx) loads when it opens.
 */
import { Suspense, lazy, useCallback, useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { useLauncher } from "../store";
import { FilePicker } from "../file-picker";
import { OwnCopyPrompt } from "../own-copy-setup";
import { SavesConflictPrompt, SavesPlacesPrompt } from "../App";
import { tvNative } from "./tvNative";
import { useTvMode, useTvSettings } from "./mode";
import { useWindowHeight } from "./useWindowHeight";
import { ControllerToast } from "./ControllerToast";
import { useFailures } from "./programs/usePrograms";
import { romNeed } from "./programs/romNeed";
import "./tv.css";

// Loaded when first wanted: a desktop that never opens TV mode, or never has a game ask for its ROM, never loads them.
const TvMode = lazy(() => import("./TvMode"));
const RomDialogDesktop = lazy(() => import("./programs/RomDialog").then((m) => ({ default: m.RomDialogDesktop })));

export function TvHost({ children }: { children: ReactNode }) {
  const tv = useTvMode();
  const { settings } = useTvSettings();
  const { settings: launcherSettings, settingUpCopy, savesConflict, savesPlaces } = useLauncher();
  const [debug, setDebug] = useState(false);
  /** The launcher's file browser is open (Steam's Game Mode): over TV mode, which lets it have the controls. */
  const [picking, setPicking] = useState(false);
  const height = useWindowHeight();
  // Games that fail right after starting, in both modes.
  useFailures();
  const turnOn = useCallback(() => tv.set(true), [tv]);
  const askingForRom = useSyncExternalStore(romNeed.subscribe, romNeed.get) !== null;
  /** A question Play waits for (the player's own copy of the game, which saves to keep) is open over TV mode, which lets it have the controls too. */
  const asking = Boolean(settingUpCopy || savesConflict || savesPlaces);

  // Started with --tv-mode (a Steam shortcut's), or QUIVER_TV_DEBUG=1.
  useEffect(() => {
    void tvNative.launchArgs().then((args) => {
      setDebug(Boolean(args?.debug));
      if (args?.tvMode) turnOn();
    }, () => {});
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <div className="tv-desktop" inert={tv.open}>
        {children}
      </div>
      <Suspense>
        {tv.open && <TvMode debug={debug} picking={picking || asking} exit={() => tv.set(false)} />}
        {!tv.open && askingForRom && <RomDialogDesktop />}
      </Suspense>
      {!tv.open && <ControllerToast enabled={settings.offerOnController} onOpen={turnOn} />}
      {/*
        Over both modes, as large in TV mode as the desktop's would be: what Play asks before a game starts (the player's
        own copy of it, which saves to keep), and over those and every dialog that asks for a file or a folder in Steam's
        Game Mode, the file browser.
      */}
      <div className="tv-picker-layer" style={tv.open ? { zoom: Math.max(1, (height / 1080) * 1.15) * (launcherSettings.scale ?? 1) } : undefined}>
        <OwnCopyPrompt />
        <SavesConflictPrompt />
        <SavesPlacesPrompt />
        <FilePicker onAsking={setPicking} />
      </div>
    </>
  );
}
