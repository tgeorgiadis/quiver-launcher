/**
 * TV mode: a fullscreen, controller-first layer over the launcher, mounted around <App/> (main.tsx). The desktop
 * stays mounted underneath, inert, so both share one store and leaving TV mode finds the desktop as it was.
 */
import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from "react";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { native } from "../native";
import { useLauncher } from "../store";
import { DEFAULT_KEYS, DEFAULT_PAD, suspendSpatialNavigation, withDefaults } from "../spatial";
import { usePadLabels, usePads } from "../pad-hints";
import { tvNative } from "./tvNative";
import { SidecarStore } from "./data/sidecar";
import { useTvMode } from "./mode";
import { TvContext, type Tv, type InputFamily } from "./context";
import { keyAction, padAction, type InputAction } from "./input/actions";
import { RepeatFilter } from "./input/repeat";
import { InputGate } from "./input/gate";
import { InputRouter } from "./input/router";
import { ConsoleCatalog } from "./consoles/consoles";
import { useBridge } from "./useBridge";
import { justInstalled, type TvState } from "./bridge";
import { withArtOverrides } from "./art/art";
import { Shell } from "./views/Shell";
import { usePlayFlow } from "./updates/PlayFlow";
import { AssetPicker } from "./updates/AssetPicker";
import { Notices } from "./views/Notices";
import { ControllerToast } from "./ControllerToast";
import { useFailures } from "./programs/usePrograms";
import { RomDialogDesktop, RomDialogTv } from "./programs/RomDialog";
import "./tv.css";

const io = { read: tvNative.readSidecar, write: tvNative.writeSidecar };

export function TvHost({ children }: { children: ReactNode }) {
  const tv = useTvMode();
  const [sidecar, setSidecar] = useState<SidecarStore | null>(null);
  const [debug, setDebug] = useState(false);
  // Games that fail right after starting, in both modes.
  useFailures();
  const turnOn = useCallback(() => tv.set(true), [tv]);

  useEffect(() => {
    let cancelled = false;
    const logError = (e: unknown) => void native.logError(`tv.json: ${String(e)}`).catch(() => {});
    void SidecarStore.load(io, 500, logError).then(async (store) => {
      if (cancelled) return;
      setSidecar(store);
      const args = (await tvNative.launchArgs().catch(() => null)) ?? { tvMode: false, debug: false };
      setDebug(args.debug);
      if (args.tvMode) turnOn();
    });
    return () => void (cancelled = true);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return (
    <>
      <div className="tv-desktop" inert={tv.open}>
        {children}
      </div>
      {tv.open && sidecar && <TvMode sidecar={sidecar} debug={debug} exit={() => tv.set(false)} />}
      {!tv.open && sidecar && <ControllerToast enabled={sidecar.settings.offerTvModeOnControllerConnect} onOpen={turnOn} />}
      {!tv.open && <RomDialogDesktop />}
    </>
  );
}

function useWindowHeight() {
  const [height, setHeight] = useState(() => window.innerHeight);
  useEffect(() => {
    const onResize = () => setHeight(window.innerHeight);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);
  return height;
}

/** Whose button pictures a controller gets: its own kind's, else the Xbox ones, which most controllers copy. */
const familyOf = (kind: string | undefined): InputFamily =>
  kind === "playstation" || kind === "nintendo" || kind === "steam" ? kind : "xbox";

function TvMode({ sidecar, debug, exit }: { sidecar: SidecarStore; debug: boolean; exit: () => void }) {
  const launcher = useLauncher();
  const store = useBridge();
  const data = useSyncExternalStore(sidecar.subscribe, () => sidecar.snapshot);
  // The player's own pictures over the catalog's; everything else reads the bridge as it is.
  const apps = useMemo(() => withArtOverrides(store.apps, data.apps), [store.apps, data.apps]);
  const bridge = useMemo(() => ({ ...store, apps }), [store, apps]);
  const pads = usePads();
  const labels = usePadLabels();
  /** Whether a controller was pressed last, not a key: its kind (labels) can be told after the press. */
  const [padLast, setPadLast] = useState(false);
  const family: InputFamily = padLast ? familyOf(labels?.kind) : "keyboard";
  // The player's bindings, read when pressed: rebinding on the desktop applies here at once.
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
  const scale = Math.max(0.6, (height / 720) * data.settings.uiScale);

  const router = useMemo(() => new InputRouter(), []);
  const gate = useMemo(() => new InputGate(new RepeatFilter((e) => router.dispatch(e))), [router]);
  const consoles = useMemo(() => new ConsoleCatalog(launcher.consoles), [launcher.consoles]);

  // When a download finishes, note when: Home's "Recently installed".
  const states = useRef(new Map<string, TvState>());
  useEffect(() => {
    for (const app of bridge.apps) {
      if (justInstalled(states.current.get(app.id), app)) sidecar.updateApp(app.id, { installedAt: Date.now() });
      states.current.set(app.id, app.state);
    }
  }, [bridge.apps, sidecar]);

  // Start + Select closes the game being played (gamepad.rs), unless the player turned it off.
  const closeCombo = data.settings.closeGameCombo;
  useEffect(() => void tvNative.closeCombo(closeCombo).catch(() => {}), [closeCombo]);
  const pointerWithSelect = data.settings.pointerWithSelect;
  useEffect(() => void tvNative.pointer(pointerWithSelect).catch(() => {}), [pointerWithSelect]);

  // Keys and controllers only reach the window in front: take it.
  useEffect(() => {
    void tvNative.focus().catch(() => {});
  }, []);

  // The chosen monitor (Settings → Screen).
  const monitor = data.settings.monitor;
  useEffect(() => {
    if (monitor) void tvNative.useMonitor(monitor).catch(() => {});
  }, [monitor]);

  // TV mode takes the keyboard and controllers from the desktop's spatial navigation while it shows.
  useEffect(() => {
    suspendSpatialNavigation(true);
    // A key's action is fixed when it goes down: Shift let go before Tab mustn't turn its release into another action.
    const keysDown = new Map<string, InputAction>();
    const onKey = (down: boolean) => (e: KeyboardEvent) => {
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
      if (!action) return;
      if (payload.down && gate.isOpen) {
        setPadLast(true);
        setPointer(false);
      }
      gate.input(`pad:${payload.key}`, action, payload.down);
    });

    // The native window's focus, not the page's: a webview not clicked yet can say it isn't focused while its window is in front.
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
    void win.isFocused().then(setActive, () => setActive(true));
    const focus = win.onFocusChanged(({ payload }) => setActive(payload));
    // Game sessions (src-tauri/src/tv/session.rs): closed while one runs, and back in front when it ends.
    const started = listen("tv-game-started", () => {
      gate.gameInForeground = true;
      setInGame(true);
    });
    const exited = listen("tv-game-exited", () => {
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
      void focus.then((stop) => stop(), () => {});
      void pad.then((stop) => stop());
    };
  }, [gate, router, debug]);

  /** Starts the game (no more questions) and counts the play. */
  const launch = (id: string) => {
    sidecar.updateApp(id, (s) => ({ ...s, playCount: (s.playCount ?? 0) + 1 }));
    void bridge.play(id);
  };
  const flow = usePlayFlow({ bridge, sidecar, data, launch });

  const value: Tv = {
    bindings,
    labels,
    sidecar,
    data,
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
    exit: () => {
      void sidecar.flush();
      exit();
    },
  };

  return (
    <TvContext.Provider value={value}>
      <div
        className={`tv-root${data.settings.reduceMotion ? " tv-reduce-motion" : ""}${pointer ? " tv-pointer" : ""}${inGame ? " tv-in-game" : ""}`}
        style={{ "--u": `${scale}px` } as CSSProperties}
        role="application"
        aria-label="Quiver Launcher, TV mode"
      >
        <Shell />
        {flow.ui}
        <AssetPicker />
        <RomDialogTv play={flow.play} />
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
