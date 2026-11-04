/**
 * The top bar compacts when what's in it doesn't fit: how much it needs depends on the language, the player's name and
 * which buttons show, so it's measured, not guessed at a window width (app.css's .topbar[data-compact]). A bar that's
 * still too wide says so with data-overflows: it scrolls sideways under the window's buttons, which stay in the corner.
 */
import { useLayoutEffect, type RefObject } from "react";

const LEVELS = 3;

function fit(bar: HTMLElement) {
  const room = bar.parentElement?.clientWidth;
  // A window still hidden has no width to fit.
  if (!room) return;
  const fits = () => Math.max(bar.offsetWidth, bar.scrollWidth) <= room;
  delete bar.dataset.overflows;
  // From the top each time, so a bar with room again goes back: each step is laid out and measured before anything paints.
  delete bar.dataset.compact;
  for (let level = 1; level <= LEVELS && !fits(); level++) bar.dataset.compact = String(level);
  if (!fits()) bar.dataset.overflows = "";
}

/** Keeps `bar` fitted as the window, the interface size, the language or the bar's buttons change. */
export function useTopBarFit(bar: RefObject<HTMLElement | null>) {
  useLayoutEffect(() => {
    const el = bar.current;
    const shell = el?.parentElement;
    if (!el || !shell) return;
    const refit = () => fit(el);
    refit();
    const sized = new ResizeObserver(refit);
    sized.observe(shell);
    // Not attributes: fitting sets them, and a download's progress changes them all the time.
    const changed = new MutationObserver(refit);
    changed.observe(el, { childList: true, subtree: true, characterData: true });
    let gone = false;
    void document.fonts.ready.then(() => gone || refit());
    return () => {
      gone = true;
      sized.disconnect();
      changed.disconnect();
    };
  }, [bar]);
}
