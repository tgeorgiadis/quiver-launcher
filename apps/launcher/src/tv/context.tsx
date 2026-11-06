/** What every TV mode view shares while it's open. */
import { createContext, useContext, useEffect, useRef } from "react";
import type { Bridge } from "./useBridge";
import type { TvSidecar, SidecarStore } from "./data/sidecar";
import type { ConsoleCatalog } from "./consoles/consoles";
import type { ControllerFamily, InputAction } from "./input/actions";
import { LAYER, type InputHandler, type InputRouter, type Layer } from "./input/router";
import type { Bindings } from "../spatial";
import type { PadLabels } from "../native";

/** Whose button names the hints show: the last thing pressed. */
export type InputFamily = ControllerFamily | "keyboard";

export type Tv = {
  /** The player's keys and controller buttons (Settings → Controls), with the defaults. */
  bindings: { keys: Bindings; pad: Bindings };
  /** What the controller used last calls its buttons. */
  labels: PadLabels | null;
  sidecar: SidecarStore;
  /** The sidecar now; a new object after every change. */
  data: TvSidecar;
  router: InputRouter;
  family: InputFamily;
  bridge: Bridge;
  consoles: ConsoleCatalog;
  /** 1 at 720p, times the player's UI scale. */
  scale: number;
  /** Play: through the update policy (it may ask, or update first), then starts the game and counts the play. */
  play(id: string): void;
  /** Controllers connected now, by name. */
  pads: string[];
  /** The mouse is in use: tiles focus on hover and the cursor shows. */
  pointer: boolean;
  /** Presses and lets go of an action, as clicking a hint or a tab arrow does. */
  tap(action: InputAction): void;
  /** Back to desktop mode. */
  exit(): void;
};

export const TvContext = createContext<Tv | null>(null);
export const useTv = () => useContext(TvContext)!;

/**
 * Puts a handler on the input stack while the component is mounted (and
 * `active`). The handler can change every render; the router always calls the
 * latest one, and the hint bar follows its hints.
 */
export function useInputHandler(handler: InputHandler, layer: Layer = LAYER.view, active = true) {
  const { router } = useTv();
  const latest = useRef(handler);
  latest.current = handler;
  useEffect(() => {
    if (!active) return;
    return router.push(
      {
        onAction: (a, repeat) => latest.current.onAction(a, repeat),
        onHold: (a) => latest.current.onHold?.(a),
        holdFor: (a) => latest.current.holdFor?.(a),
        hints: () => latest.current.hints(),
        ...(latest.current.onText ? { onText: (text: string) => latest.current.onText?.(text) } : {}),
      },
      layer,
    );
  }, [router, layer, active]);
  // Hints often depend on what's focused: let the hint bar look again after every render.
  useEffect(() => {
    if (active) router.refresh();
  });
}
