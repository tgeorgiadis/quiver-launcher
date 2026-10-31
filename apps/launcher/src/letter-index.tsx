/**
 * Beside a grid of cards sorted by name (TV mode's library, the library's), down its right: the letters its apps file
 * under, the focused app's in the accent. The triggers jump letter to letter (spatial.ts's jump) and the letter come to
 * shows large for a moment, readable from across a room; a letter clicked or tapped goes to its first app. Its letters
 * aren't stops for the d-pad, which moves through the cards: the triggers are its way there.
 */
import { useEffect, useState, type RefObject } from "react";
import { t } from "@lingui/core/macro";
import { focusOn } from "./spatial";

export function LetterIndex({ grid, letters }: { grid: RefObject<HTMLElement | null>; letters: readonly string[] }) {
  const [current, setCurrent] = useState<string | null>(null);
  const [big, setBig] = useState<{ letter: string; at: number } | null>(null);
  useEffect(() => {
    const el = grid.current;
    if (!el) return;
    const focus = (e: Event) => {
      const letter = e.target instanceof Element ? e.target.closest("[data-letter]")?.getAttribute("data-letter") : null;
      if (letter) setCurrent(letter);
    };
    let timer: ReturnType<typeof setTimeout> | undefined;
    const jumped = (e: Event) => {
      setBig({ letter: (e as CustomEvent<string>).detail, at: performance.now() });
      clearTimeout(timer);
      timer = setTimeout(() => setBig(null), 900);
    };
    el.addEventListener("focusin", focus);
    el.addEventListener("letterjump", jumped);
    return () => {
      el.removeEventListener("focusin", focus);
      el.removeEventListener("letterjump", jumped);
      clearTimeout(timer);
    };
  }, [grid]);
  const go = (letter: string, mouse: boolean) => {
    const to = grid.current?.querySelector<HTMLElement>(`[data-letter="${CSS.escape(letter)}"] .card-open, .card-open[data-letter="${CSS.escape(letter)}"]`);
    if (!to) return;
    setCurrent(letter);
    // A click scrolls it to the top and puts the focus there quietly; a controller's or the keys' press, with its box.
    if (mouse) {
      to.focus({ preventScroll: true });
      to.scrollIntoView({ block: "start" });
    } else focusOn(to);
  };
  const shown = current ?? letters[0];
  return (
    <>
      <nav className="letter-index" aria-label={t({ message: "Letters", comment: "The list of letters beside a library sorted by name, each going to its first app" })}>
        {letters.map((letter) => (
          <button key={letter} type="button" tabIndex={-1} data-current={letter === shown || undefined} onClick={(e) => go(letter, e.detail > 0)}>
            {letter}
          </button>
        ))}
      </nav>
      {big && (
        <span key={big.at} className="letter-big" aria-hidden="true">
          {big.letter}
        </span>
      )}
    </>
  );
}
