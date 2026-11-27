/**
 * A screen kept as it was while another shows (Library while the player is in Browse, and Browse while they're in the
 * library): going back to it shows it again at once, rather than building every one of its cards again, which took the
 * better part of a second for Browse's front page or a library of hundreds of apps.
 *
 * Not hidden in the page but taken out of it: its cards, fields and buttons aren't the page's while another screen is,
 * for anything looking for them (the controller's moves, the tests' selectors, a screen reader). React keeps it up to
 * date out there all the same (a portal): what it shows of the launcher's state, its own state, all of it.
 *
 * What the window passes it isn't drawn again while it's away, nor as it comes back: it comes back as it was, at once,
 * and takes what changed meanwhile just after, without holding up the window (a transition).
 */
import { startTransition, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";

export function KeptScreen({ shown, children }: { shown: boolean; children: ReactNode }) {
  const [box] = useState(() => {
    const div = document.createElement("div");
    div.className = "kept-box";
    return div;
  });
  const wasShown = useRef(shown);
  const drawn = useRef(children);
  const [, catchUp] = useState(0);
  const back = shown && !wasShown.current;
  // The same element as last time draws nothing again (React leaves what it made as it is).
  const now = shown && !back ? children : drawn.current;
  useLayoutEffect(() => {
    drawn.current = now;
    wasShown.current = shown;
  });
  useEffect(() => {
    if (back) startTransition(() => catchUp((n) => n + 1));
  }, [back]);
  return (
    <>
      {/* Put in or taken out as each change is made, before the screen's own effects measure anything in it. */}
      <div
        className="kept-screen"
        ref={(spot) => {
          if (!spot) return;
          if (!shown) box.remove();
          else if (box.parentNode !== spot) spot.append(box);
        }}
      />
      {createPortal(now, box)}
    </>
  );
}
