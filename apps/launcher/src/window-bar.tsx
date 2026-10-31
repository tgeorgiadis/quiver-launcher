/**
 * The window's own buttons at the right of the top bar, in place of the
 * system's title bar (off unless the player turns it back on in Settings).
 * The top bar then moves the window, and a double-click on it maximizes. A
 * Mac keeps its own title bar; full screen has none, so it gets a way out.
 */
import { useEffect, useState, type MouseEvent } from "react";
import { Copy, Minimize2, Minus, Square, X } from "lucide-react";
import { t } from "@lingui/core/macro";
import type { Os } from "@quiverlauncher/api";
import { useLauncher } from "./store";
import { native } from "./native";

export type WindowButtons = "none" | "window" | "fullscreen";

/** Which buttons the top bar shows: none under the system's title bar, which has its own. */
export function windowButtons(os: Os, systemTitleBar: boolean, fullscreen: boolean): WindowButtons {
  if (os !== "windows" && os !== "linux") return "none";
  if (fullscreen) return "fullscreen";
  return systemTitleBar ? "none" : "window";
}

/** The buttons to show, and whether the window is maximized, kept as the window changes. */
export function useWindowButtons() {
  const { settings, config } = useLauncher();
  const [state, setState] = useState({ maximized: false, fullscreen: Boolean(settings.fullscreen) });
  useEffect(() => {
    const check = () => void native.windowState().then(setState, () => {});
    check();
    const off = native.onWindowResized(check);
    return () => void off.then((stop) => stop());
  }, []);
  return { buttons: windowButtons(config.os, Boolean(settings.systemTitleBar), state.fullscreen), maximized: state.maximized };
}

/** What a press in the top bar can't move the window from: its buttons, links and fields. */
const CONTROLS = "button, a, input, select, textarea, label, summary, [role], [tabindex]";

/**
 * Pressing the top bar's empty space and moving the pointer moves the window;
 * a double-click maximizes or restores it. The move starts once the pointer
 * moves, not on the press: a window manager moving the window holds the
 * pointer, and on Linux took the second click of a double-click.
 */
export function moveWindow(e: MouseEvent) {
  if (e.button !== 0 || (e.target as Element).closest(CONTROLS)) return;
  e.preventDefault();
  if (e.detail === 2) {
    void native.toggleMaximize();
    return;
  }
  const [x, y] = [e.screenX, e.screenY];
  const stop = () => {
    window.removeEventListener("mousemove", move);
    window.removeEventListener("mouseup", stop);
  };
  const move = (m: globalThis.MouseEvent) => {
    if (!(m.buttons & 1)) return stop();
    if (Math.hypot(m.screenX - x, m.screenY - y) < 4) return;
    stop();
    void native.startDragging();
  };
  window.addEventListener("mousemove", move);
  window.addEventListener("mouseup", stop);
}

export function WindowControls({ buttons, maximized }: { buttons: WindowButtons; maximized: boolean }) {
  const { settings, setSettings } = useLauncher();
  if (buttons === "none") return null;
  const exitFullscreen = () => {
    setSettings({ ...settings, fullscreen: false });
    void native.setFullscreen(false, Boolean(settings.systemTitleBar));
  };
  const minimize = t({ message: "Minimize", comment: "Window button" });
  const exit = t`Exit full screen`;
  const size = maximized ? t({ message: "Restore", comment: "Window button: un-maximize" }) : t({ message: "Maximize", comment: "Window button" });
  const close = t({ message: "Close", comment: "Window button: close Quiver's window" });
  return (
    <div className="window-controls">
      <button aria-label={minimize} title={minimize} onClick={() => void native.minimize()}>
        <Minus size={16} strokeWidth={1.5} />
      </button>
      {buttons === "fullscreen" ? (
        <button aria-label={exit} title={exit} onClick={exitFullscreen}>
          <Minimize2 size={15} strokeWidth={1.5} />
        </button>
      ) : (
        <button aria-label={size} title={size} onClick={() => void native.toggleMaximize()}>
          {maximized ? <Copy size={14} strokeWidth={1.5} className="restore" /> : <Square size={13} strokeWidth={1.5} />}
        </button>
      )}
      <button className="close-window" aria-label={close} title={close} onClick={() => void native.closeWindow()}>
        <X size={17} strokeWidth={1.5} />
      </button>
    </div>
  );
}
