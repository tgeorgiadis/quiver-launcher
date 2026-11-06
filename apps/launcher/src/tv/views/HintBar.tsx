/**
 * The footer: Menu on the left, always, and Search beside it while a tab is
 * in front (nothing open over it); on the right what the buttons do now,
 * which is always the top handler's hints.
 */
import { useSyncExternalStore, type PointerEvent } from "react";
import { t } from "@lingui/core/macro";
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
        {t({ message: "Menu", comment: "TV mode's controller hint: what Start does, opening its menu (Settings)" })}
      </span>
      {router.topLayer === LAYER.view && (
        <span className="tv-hint tv-hint-menu" role="button" {...press("search")}>
          <Glyph action="search" />
          {t({ message: "Search", comment: "TV mode's controller hint: what Select (View) does, opening the search" })}
        </span>
      )}
      <span className="tv-hints-right">
        {hints.map((hint) => (
          <span key={hint.action} className="tv-hint" role="button" {...press(hint.action)}>
            <Glyph action={hint.action} />
            {hint.label}
            {hint.hold && <span className="tv-hold-note">{t({ message: "hold", comment: "After a controller hint, such as Cancel: the button must be held down to do it" })}</span>}
          </span>
        ))}
      </span>
    </footer>
  );
}
