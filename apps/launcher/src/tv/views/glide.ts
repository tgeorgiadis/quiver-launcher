/**
 * Scrolls a list towards where the focus went, as one motion: a new place to go while it's still moving changes where
 * it's heading, where the browser's own smooth scrolling starts over from a standstill on every call, so holding a
 * direction (a step every 45 to 90 ms) crawled.
 */

/** How long the distance left takes to halve. */
const HALF_MS = 55;

/** Where a glide is after `ms` more: the distance left halves every HALF_MS. */
export const approach = (at: number, target: number, ms: number) => target + (at - target) * 0.5 ** (ms / HALF_MS);

type Glide = { top: number; at: number; last: number; frame: number };
const glides = new WeakMap<HTMLElement, Glide>();

/** Where the list is heading, or where it is when it's still. */
export const glideTarget = (el: HTMLElement) => glides.get(el)?.top ?? el.scrollTop;

export function glideTo(el: HTMLElement, top: number, instant = false) {
  top = Math.max(0, Math.min(top, el.scrollHeight - el.clientHeight));
  const running = glides.get(el);
  if (instant) {
    if (running) cancelAnimationFrame(running.frame);
    glides.delete(el);
    el.scrollTop = top;
    return;
  }
  if (running) {
    running.top = top;
    return;
  }
  if (Math.abs(top - el.scrollTop) <= 1) return;
  // Its own place, not scrollTop's: that one rounds, which would stall the last pixels.
  const glide: Glide = { top, at: el.scrollTop, last: performance.now(), frame: 0 };
  const step = (now: number) => {
    glide.at = approach(glide.at, glide.top, Math.min(now - glide.last, 50));
    glide.last = now;
    if (Math.abs(glide.top - glide.at) < 0.5) {
      el.scrollTop = glide.top;
      glides.delete(el);
      return;
    }
    el.scrollTop = glide.at;
    glide.frame = requestAnimationFrame(step);
  };
  glide.frame = requestAnimationFrame(step);
  glides.set(el, glide);
}

/** Glides just far enough that `child` shows whole, as scrollIntoView's "nearest" does. */
export function glideIntoView(el: HTMLElement, child: Element | null | undefined, instant = false) {
  if (!child) return;
  const box = el.getBoundingClientRect();
  const rect = child.getBoundingClientRect();
  const top = rect.top - box.top + el.scrollTop;
  const heading = glideTarget(el);
  if (top < heading) glideTo(el, top, instant);
  else if (top + rect.height > heading + el.clientHeight) glideTo(el, top + rect.height - el.clientHeight, instant);
}
