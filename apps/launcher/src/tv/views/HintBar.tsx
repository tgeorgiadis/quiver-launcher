/**
 * The footer: Menu on the left, always, and Search beside it while a tab is
 * in front (nothing open over it); on the right what the buttons do now,
 * which is always the top handler's hints (spec §5.6).
 */
import { useSyncExternalStore, type PointerEvent } from "react";
import { useTv } from "../context";
import { Glyph } from "./Glyph";
import type { InputAction } from "../input/actions";
import { LAYER } from "../input/router";

/** Pressing a hint with the mouse presses its button, held as long as the mouse is, so holds work too. */
function useHintPress() {
  const { router } = useTv();
  return (action: InputAction) => ({
    onPointerDown: (e: PointerEvent<HTMLElement>) => {
      if (e.button !== 0) return;
      e.currentTarget.setPointerCapture(e.pointerId);
      router.dispatch({ action, phase: "press" });
    },
    onPointerUp: () => router.dispatch({ action, phase: "release" }),
    onPointerCancel: () => router.dispatch({ action, phase: "release" }),
  });
}

export function HintBar() {
  const { router } = useTv();
  const press = useHintPress();
  useSyncExternalStore(router.subscribe, router.snapshot);
  const hints = router.hints();
  return (
    <footer className="tv-hints">
      <span className="tv-hint tv-hint-menu" role="button" {...press("menu")}>
        <Glyph action="menu" />
        Menu
      </span>
      {router.topLayer === LAYER.view && (
        <span className="tv-hint tv-hint-menu" role="button" {...press("search")}>
          <Glyph action="search" />
          Search
        </span>
      )}
      <span className="tv-hints-right">
        {hints.map((hint) => (
          <span key={hint.action} className="tv-hint" role="button" {...press(hint.action)}>
            <Glyph action={hint.action} />
            {hint.label}
            {hint.hold && <span className="tv-hold-note">hold</span>}
          </span>
        ))}
      </span>
    </footer>
  );
}
