import { useLayoutEffect, useRef } from "react";

/*
 * Card entrances: as another screen shows (a page opens, Back shows the one under it, Library and Browse swap), the
 * cards in sight on it fade in and rise 8px together over --duration-base (@quiverlauncher/ui's tokens.css), never one
 * after another. Only the cards: the top bar, search, filters and headings are simply there, as they would be in a real room.
 *
 * It never holds anything up: focus and the next key work at once. Moving by the keys or a controller
 * (data-navigating, spatial.ts) it only fades, so nothing is where it isn't yet when the focus box is drawn or a
 * card scrolled to; and with reduced motion asked for it only fades too. Played from here rather than app.css, so
 * that decision is made once, as the screen shows: a CSS animation would start over when the mouse moves, or play
 * again on every card a search or filter brings.
 */

const ENTER_MOVE: Keyframe[] = [
  { opacity: 0, transform: "translateY(8px)" },
  { opacity: 1, transform: "none" },
];
const ENTER_FADE: Keyframe[] = [{ opacity: 0 }, { opacity: 1 }];

/** What comes in: catalog and library cards, a list layout's rows, and the big ones over Browse and the library. */
const CARDS = ".entry-card, .sidebar-row, .featured, .continue";

function token(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

/** A motion token's length in milliseconds, or `fallback` where it isn't set (a test's DOM). */
function duration(name: string, fallback: number): number {
  const value = token(name);
  const ms = value.endsWith("ms") ? parseFloat(value) : value.endsWith("s") ? parseFloat(value) * 1000 : NaN;
  return Number.isFinite(ms) ? ms : fallback;
}

export function reducedMotion(): boolean {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
}

/** Plays the entrance on `element`. `fade` leaves out the rise and takes --duration-fast; `delay` holds it hidden that long first. */
export function enter(element: Element | null | undefined, { fade = false, delay = 0 }: { fade?: boolean; delay?: number } = {}): void {
  if (!element || typeof element.animate !== "function") return;
  const move = !fade && !reducedMotion() && !document.documentElement.hasAttribute("data-navigating");
  element.animate(move ? ENTER_MOVE : ENTER_FADE, {
    duration: fade ? duration("--duration-fast", 120) : duration("--duration-base", 200),
    easing: token("--ease-enter") || "cubic-bezier(0.2, 0, 0, 1)",
    delay,
    fill: delay ? "backwards" : "none",
  });
}

/** The cards in sight in `screen` as it shows, together (those out of sight aren't seen coming in, so they just are). */
export function enterCards(screen: Element | null | undefined): void {
  if (!screen) return;
  const height = window.innerHeight;
  // Where each is, all read before any starts: one layout, not one for each card after the one before started.
  const inSight = [...screen.querySelectorAll(CARDS)].filter((card) => {
    const box = card.getBoundingClientRect();
    return box.width > 0 && box.bottom > 0 && box.top < height;
  });
  for (const card of inSight) enter(card);
}

/** Plays `play` on what `target` finds each time `key` changes, not when it first shows. */
export function useEnterOnChange(target: () => Element | null | undefined, key: unknown, play: (element: Element | null | undefined) => void = enterCards): void {
  const last = useRef(key);
  useLayoutEffect(() => {
    if (Object.is(last.current, key)) return;
    last.current = key;
    play(target());
    // Only a change of key plays it; target and play may be new on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
}
