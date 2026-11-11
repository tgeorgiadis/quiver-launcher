/**
 * The window's own buttons at the right of the top bar, in place of the
 * system's title bar (off unless the player turns it back on in Settings).
 * The top bar then moves the window, and a double-click on it maximizes. A
 * Mac keeps its own title bar; full screen has none, so it gets a way out.
 */
import { useEffect, useState, type MouseEvent, type PointerEvent } from "react";
import { Copy, Minimize2, Minus, Square, X } from "lucide-react";
import { t } from "@lingui/core/macro";
import type { Os } from "@quiverlauncher/api";
import { useLauncher } from "./store";
import { native, type Rect, type WindowRect } from "./native";

export type WindowButtons = "none" | "window" | "fullscreen" | "close";

/**
 * Which buttons the top bar shows: none under the system's title bar, which has its own. Only Close in Steam's Game
 * Mode: Steam has the window, so it can't be minimized or resized, but a player shouldn't have to know Steam's menu to leave.
 */
export function windowButtons(os: Os, systemTitleBar: boolean, fullscreen: boolean, gameMode = false): WindowButtons {
  if (os !== "windows" && os !== "linux") return "none";
  if (gameMode) return "close";
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
  return { buttons: windowButtons(config.os, Boolean(settings.systemTitleBar), state.fullscreen, config.gameMode), maximized: state.maximized };
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

/** A side or corner of the window, by compass point. */
export type Edge = "n" | "s" | "e" | "w" | "ne" | "nw" | "se" | "sw";
const EDGES: Edge[] = ["n", "s", "e", "w", "ne", "nw", "se", "sw"];

/**
 * Where the window goes as `edge` is pulled `dx` and `dy` screen pixels from where it was at `start`: the far side
 * stays put, and it's never smaller than its smallest.
 */
export function resized(start: WindowRect, edge: Edge, dx: number, dy: number): Rect {
  let { x, y, width, height } = start;
  if (edge.includes("e")) width = Math.max(start.minWidth, start.width + dx);
  if (edge.includes("w")) {
    width = Math.max(start.minWidth, start.width - dx);
    x = start.x + start.width - width;
  }
  if (edge.includes("s")) height = Math.max(start.minHeight, start.height + dy);
  if (edge.includes("n")) {
    height = Math.max(start.minHeight, start.height - dy);
    y = start.y + start.height - height;
  }
  return { x: Math.round(x), y: Math.round(y), width: Math.round(width), height: Math.round(height) };
}

/** Whether the page draws its own edges to resize the window by: on Linux with Quiver's own title bar, un-maximized. */
export function resizeEdges(os: Os, buttons: WindowButtons, maximized: boolean) {
  return os === "linux" && buttons === "window" && !maximized;
}

/**
 * Pulling one moves that side of the window. Linux runs the page in Chromium, whose window has no resize borders, and
 * the window manager can't take the pointer to resize it on X11 (as it does Windows' and the system title bar's), so
 * the page holds the pointer and sizes the window itself.
 */
export function ResizeEdges() {
  const pull = (edge: Edge) => (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    const handle = e.currentTarget;
    handle.setPointerCapture(e.pointerId);
    // Screen coordinates, which don't move with the window; the page's pixels are the screen's over its pixel ratio.
    const [x, y, scale] = [e.screenX, e.screenY, window.devicePixelRatio || 1];
    let start: WindowRect | undefined;
    let next: Rect | undefined;
    let sending = false;
    // One change at a time, and only the latest of those that came in meanwhile.
    const send = () => {
      if (sending || !next) return;
      const rect = next;
      next = undefined;
      sending = true;
      void native
        .setWindowRect(rect)
        .catch(() => {})
        .finally(() => {
          sending = false;
          send();
        });
    };
    void native.windowRect().then((r) => (start = r), () => {});
    const move = (m: globalThis.PointerEvent) => {
      if (!start) return;
      next = resized(start, edge, (m.screenX - x) * scale, (m.screenY - y) * scale);
      send();
    };
    const stop = () => {
      handle.removeEventListener("pointermove", move);
      handle.removeEventListener("pointerup", stop);
      handle.removeEventListener("pointercancel", stop);
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", stop);
    handle.addEventListener("pointercancel", stop);
  };
  return (
    <div className="resize-edges" aria-hidden="true">
      {EDGES.map((edge) => (
        <div key={edge} className={`resize-edge ${edge}`} onPointerDown={pull(edge)} />
      ))}
    </div>
  );
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
      {buttons !== "close" && (
        <>
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
        </>
      )}
      <button className="close-window" aria-label={close} title={close} onClick={() => void native.closeWindow()}>
        <X size={17} strokeWidth={1.5} />
      </button>
    </div>
  );
}
