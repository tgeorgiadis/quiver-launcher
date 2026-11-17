/**
 * TV mode itself, loaded when it first opens (TvHost.tsx): its screens, the input it takes from the desktop, and its
 * dialogs over them.
 */
import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { t } from "@lingui/core/macro";
import { useLauncher } from "../store";
import { DEFAULT_KEYS, DEFAULT_PAD, suspendSpatialNavigation, withDefaults } from "../spatial";
import { usePadLabels, usePads } from "../pad-hints";
import { tvNative } from "./tvNative";
import { useTvSettings } from "./mode";
import { useWindowHeight } from "./useWindowHeight";
import { TvContext, type Tv, type InputFamily } from "./context";
import { keyAction, padAction, type InputAction } from "./input/actions";
import { RepeatFilter } from "./input/repeat";
import { InputGate } from "./input/gate";
import { InputRouter } from "./input/router";
import { playFor } from "./sound";
import { ConsoleCatalog } from "./consoles/consoles";
import { useBridge } from "./useBridge";
import { Shell } from "./views/Shell";
import { usePlayFlow } from "./updates/PlayFlow";
import { AssetPicker } from "./updates/AssetPicker";
import { Notices } from "./views/Notices";
import { RomDialogTv } from "./programs/RomDialog";
import { ProgramPrompt } from "./programs/ProgramPrompt";

/** Whose button pictures a controller gets: its own kind's, else the Xbox ones, which most controllers copy. */
const familyOf = (kind: string | undefined): InputFamily =>
  kind === "playstation" || kind === "nintendo" || kind === "steam" ? kind : "xbox";

export default function TvMode({ debug, picking, exit }: { debug: boolean; picking: boolean; exit: () => void }) {
  const launcher = useLauncher();
  const bridge = useBridge();
  const { settings, set: setSettings } = useTvSettings();
  const pads = usePads();
  const labels = usePadLabels();
  /** Whether a controller was pressed last, not a key: its kind (labels) can be told after the press. */
  const [padLast, setPadLast] = useState(false);
  const family: InputFamily = padLast ? familyOf(labels?.kind) : "keyboard";
  // The player's bindings, read when pressed: rebinding on the desktop applies here at once.
  const linux = launcher.config.os === "linux";
  const { keys: savedKeys, pad: savedPad } = launcher.settings;
  const bindings = useMemo(() => ({ keys: withDefaults(savedKeys, DEFAULT_KEYS), pad: withDefaults(savedPad, DEFAULT_PAD) }), [savedKeys, savedPad]);
  const bound = useRef(bindings);
  bound.current = bindings;
  /** QUIVER_TV_DEBUG=1: the last controller events, and whether input is getting through. */
  const [events, setEvents] = useState<string[]>([]);
  /** The cursor shows while the mouse moves, and hides again at the first key or button. */
  const [pointer, setPointer] = useState(false);
  /** A game TV mode started is running: input is closed and animations rest. */
  const [inGame, setInGame] = useState(false);
  const height = useWindowHeight();
  // While the file browser is open, the keys and controllers are its (spatial.ts), not TV mode's.
  const browsing = useRef(picking);
  browsing.current = picking;
  useEffect(() => suspendSpatialNavigation(!picking), [picking]);
  const scale = Math.max(0.6, (height / 720) * (launcher.settings.scale ?? 1));

  const router = useMemo(() => new InputRouter(), []);
  // Each press with its sound (Settings → Sounds), then to whatever is on top.
  const sounds = useRef(settings.sounds);
  sounds.current = settings.sounds;
  const gate = useMemo(
    () =>
      new InputGate(
        new RepeatFilter((e) => {
          if (sounds.current && e.phase !== "release") playFor(e.action, e.phase === "repeat");
          router.dispatch(e);
        }),
      ),
    [router],
  );
  const consoles = useMemo(() => new ConsoleCatalog(launcher.consoles), [launcher.consoles]);

  // Keys and controllers only reach the window in front: take it.
  useEffect(() => {
    void tvNative.focus().catch(() => {});
  }, []);

  // The chosen monitor (Settings → Screen).
  const monitor = settings.monitor;
  useEffect(() => {
    if (monitor) void tvNative.useMonitor(monitor).catch(() => {});
  }, [monitor]);

  // TV mode takes the keyboard and controllers from the desktop's spatial navigation while it shows.
  useEffect(() => {
    suspendSpatialNavigation(true);
    // A key's action is fixed when it goes down: Shift let go before Tab mustn't turn its release into another action.
    const keysDown = new Map<string, InputAction>();
    const onKey = (down: boolean) => (e: KeyboardEvent) => {
      if (browsing.current) return;
      // The on-screen keyboard takes typing from a physical one too.
      const typing = router.top?.onText;
      if (typing && down && gate.isOpen && !e.ctrlKey && !e.altKey && !e.metaKey) {
        const text = e.key === "Backspace" ? "\b" : e.key === "Enter" ? "\n" : e.key.length === 1 ? e.key : null;
        if (text !== null) {
          e.preventDefault();
          e.stopImmediatePropagation();
          setPadLast(false);
          typing(text);
          return;
        }
      }
      const action = down ? keyAction(e, bound.current.keys) : keysDown.get(e.code);
      if (!action) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      if (down) {
        if (e.repeat) return;
        keysDown.set(e.code, action);
        setPadLast(false);
        setPointer(false);
      } else keysDown.delete(e.code);
      gate.input(`key:${e.code}`, action, down);
    };
    const keyDown = onKey(true);
    const keyUp = onKey(false);
    window.addEventListener("keydown", keyDown, true);
    window.addEventListener("keyup", keyUp, true);
    const onPointer = (e: MouseEvent) => (e.movementX || e.movementY) && setPointer(true);
    window.addEventListener("mousemove", onPointer);

    // The launcher's controller events (gamepad.rs), named as gilrs named the controls.
    const pad = listen<{ key: string; down: boolean }>("pad", ({ payload }) => {
      if (debug) {
        const time = new Date().toLocaleTimeString(undefined, { hour12: false });
        const line = `${time} ${payload.key} ${payload.down ? "down" : "up"} · ${gate.isOpen ? "open" : "dropped (window not in front)"}`;
        setEvents((e) => [line, ...e].slice(0, 8));
      }
      const action = padAction(payload.key, bound.current.pad);
      if (!action || browsing.current) return;
      if (payload.down && gate.isOpen) {
        setPadLast(true);
        setPointer(false);
      }
      gate.input(`pad:${payload.key}`, action, payload.down);
    });

    // The native window's focus, not the page's: a webview not clicked yet can say it isn't focused while its window is in front.
    // Except on Linux: on CEF the window hands the X focus to Chromium's own window as soon as it gets it, so it never
    // says it's in front; the page's focus says instead, as it does for the controllers (gamepad.rs).
    const win = getCurrentWindow();
    const setActive = (active: boolean) => {
      // Back in front during a game (alt-tab, or a launcher that never quits): the player is here, listen again.
      if (active && gate.gameInForeground) {
        gate.gameInForeground = false;
        setInGame(false);
      }
      gate.windowActive = active;
      if (debug) setEvents((e) => [`${new Date().toLocaleTimeString(undefined, { hour12: false })} window ${active ? "in front" : "behind"}`, ...e].slice(0, 8));
    };
    const pageFocus = () => setActive(true);
    const pageBlur = () => setActive(false);
    if (linux) {
      setActive(document.hasFocus());
      window.addEventListener("focus", pageFocus);
      window.addEventListener("blur", pageBlur);
    } else void win.isFocused().then(setActive, () => setActive(true));
    const focus = linux ? Promise.resolve(() => {}) : win.onFocusChanged(({ payload }) => setActive(payload));
    // Games the launcher started (src-tauri/src/tv/session.rs): closed while one runs, and back in front when the last ends.
    let playing = 0;
    const started = listen("app-started", () => {
      playing++;
      gate.gameInForeground = true;
      setInGame(true);
    });
    const exited = listen("app-closed", () => {
      playing = Math.max(0, playing - 1);
      if (playing) return;
      gate.gameInForeground = false;
      setInGame(false);
      void tvNative.focus().catch(() => {});
    });
    return () => {
      void started.then((stop) => stop());
      void exited.then((stop) => stop());
      suspendSpatialNavigation(false);
      window.removeEventListener("keydown", keyDown, true);
      window.removeEventListener("keyup", keyUp, true);
      window.removeEventListener("mousemove", onPointer);
      window.removeEventListener("focus", pageFocus);
      window.removeEventListener("blur", pageBlur);
      void focus.then((stop) => stop(), () => {});
      void pad.then((stop) => stop());
    };
  }, [gate, router, debug, linux]);

  /** Starts the game, no more questions. */
  const launch = (id: string) => void bridge.play(id);
  const flow = usePlayFlow({ bridge, settings, launch, decline: launcher.declineUpdate });

  const value: Tv = {
    bindings,
    labels,
    settings,
    setSettings,
    router,
    family,
    bridge,
    consoles,
    scale,
    pads,
    pointer,
    tap: (action) => {
      router.dispatch({ action, phase: "press" });
      router.dispatch({ action, phase: "release" });
    },
    play: flow.play,
    exit,
  };

  return (
    <TvContext.Provider value={value}>
      <div
        className={`tv-root${settings.reduceMotion ? " tv-reduce-motion" : ""}${pointer ? " tv-pointer" : ""}${inGame ? " tv-in-game" : ""}`}
        style={{ "--u": `${scale}px` } as CSSProperties}
        role="application"
        aria-label={t`Quiver Launcher, TV mode`}
      >
        <Shell />
        {flow.ui}
        <AssetPicker />
        <RomDialogTv play={flow.play} />
        <ProgramPrompt />
        <Notices />
        {debug && (
          <aside className="tv-debug">
            <strong>
              Input: {gate.isOpen ? "open" : "closed"} · glyphs {family} · pads {pads.length ? pads.join(", ") : "none"}
            </strong>
            {events.length ? events.map((e, i) => <span key={i}>{e}</span>) : <span>No controller events yet</span>}
          </aside>
        )}
      </div>
    </TvContext.Provider>
  );
}
