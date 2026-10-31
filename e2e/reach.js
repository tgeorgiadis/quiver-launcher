/**
 * Where a controller can get to: presses the d-pad as the Rust side reports
 * it ("pad" events) and follows the focus, from every control it reaches, in
 * all four directions, until nothing new turns up. Whatever the player can see
 * but the d-pad never reached is what a controller can't get to. Controls are
 * those spatial.ts moves between: in the topmost dialog when one is open, else
 * on the page.
 */

/** Puts the helpers in the page (again after it reloads). */
export function install(app) {
  return app.execute(async () => {
    if (window.__reach) return;
    // Nothing animates, so every move is done when it returns: no smooth
    // scrolling and no transitions (a focused card lifts). Where things end up
    // is the same; only the waiting goes.
    const still = document.head.appendChild(document.createElement("style"));
    still.textContent = "*, *::before, *::after { transition: none !important; animation: none !important; scroll-behavior: auto !important; }";
    const instant = (options) => (options && typeof options === "object" ? { ...options, behavior: "instant" } : options);
    for (const [target, name] of [[Element.prototype, "scrollIntoView"], [Element.prototype, "scrollTo"], [Element.prototype, "scrollBy"], [window, "scrollTo"], [window, "scrollBy"]]) {
      const original = target[name];
      target[name] = function (options, ...rest) {
        return original.call(this, instant(options), ...rest);
      };
    }
    // The controller's own eased scrolls (spatial.ts) go at once for a player who'd rather less moved.
    const matchMedia = window.matchMedia.bind(window);
    window.matchMedia = (query) => (/prefers-reduced-motion:\s*reduce/.test(query) ? { ...matchMedia(query), matches: true, media: query } : matchMedia(query));
    const FOCUSABLE = "button:not(:disabled), input, select, textarea, a[href], [tabindex]:not([tabindex='-1'])";
    const DIRECTIONS = ["DPadUp", "DPadDown", "DPadLeft", "DPadRight"];
    const scope = () => {
      const dialogs = document.querySelectorAll('[role="dialog"]');
      return dialogs[dialogs.length - 1] ?? document;
    };
    const visible = (el) => el.isConnected && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";
    const candidates = () => [...scope().querySelectorAll(FOCUSABLE)].filter(visible);
    const frame = () => new Promise((r) => requestAnimationFrame(() => r()));
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    // A "pad" listener of our own, after the app's: when it hears a press, the app has acted on it.
    const heard = [];
    const internals = window.__TAURI_INTERNALS__;
    await internals.invoke("plugin:event|listen", { event: "pad", target: { kind: "Any" }, handler: internals.transformCallback(() => heard.shift()?.()) });
    const emit = async (key, down) => {
      const arrived = new Promise((r) => heard.push(r));
      await internals.invoke("plugin:event|emit", { event: "pad", payload: { key, down } });
      await arrived;
    };
    /** Until the focused control stops moving (what a press did has rendered). */
    async function settle() {
      let last = null;
      for (let i = 0, still = 0; i < 90 && still < 2; i++) {
        await frame();
        const r = document.activeElement?.getBoundingClientRect();
        const now = r ? `${Math.round(r.top)},${Math.round(r.left)},${document.querySelector(".shell > main")?.scrollTop}` : "";
        still = now === last ? still + 1 : 0;
        last = now;
      }
    }
    /**
     * A button pressed and let go, once the app has heard both. The d-pad moves
     * the focus as it hears it; anything else may render after, so it waits for that.
     */
    async function press(key) {
      await emit(key, true);
      await emit(key, false);
      if (!DIRECTIONS.includes(key)) await settle();
    }
    /** What the player would call it, and where it is. */
    function describe(el) {
      const text = (s) => (s ?? "").replace(/\s+/g, " ").trim();
      const name = text(el.getAttribute("aria-label")) || text(el.innerText).slice(0, 60) || text(el.getAttribute("placeholder")) || text(el.getAttribute("title")) || text(el.value);
      const area = el.parentElement?.closest("[aria-label]:not(button):not(a)")?.getAttribute("aria-label");
      return `${el.tagName.toLowerCase()} "${name}"${area ? ` in ${area}` : ""}`;
    }
    /**
     * What cuts off the focused control's focus ring, if anything: the
     * nearest box around it that clips (or the window's sides).
     */
    function cutOff(el) {
      const style = getComputedStyle(el);
      const out = parseFloat(style.outlineOffset) + parseFloat(style.outlineWidth);
      if (style.outlineStyle === "none" || !(out > 0)) return null;
      const r = el.getBoundingClientRect();
      const ring = { left: r.left - out, right: r.right + out, top: r.top - out, bottom: r.bottom + out };
      // What's fixed (a dialog over the page, inside main) isn't clipped by what scrolls around it.
      for (let p = el.parentElement, fixed = style.position === "fixed"; p && p !== document.body && !fixed; p = p.parentElement) {
        const { overflowX, overflowY, position } = getComputedStyle(p);
        fixed = position === "fixed";
        const x = overflowX !== "visible", y = overflowY !== "visible";
        if (!x && !y) continue;
        const b = p.getBoundingClientRect();
        const left = b.left + p.clientLeft, top = b.top + p.clientTop;
        if ((x && (ring.left < left - 0.5 || ring.right > left + p.clientWidth + 0.5)) || (y && (ring.top < top - 0.5 || ring.bottom > top + p.clientHeight + 0.5)))
          return p.getAttribute("aria-label") || p.className || p.tagName.toLowerCase();
      }
      if (ring.left < -0.5 || ring.right > document.documentElement.clientWidth + 0.5) return "the window";
      return null;
    }
    /** Whether a control shows: in the window, below the top bar (unless it's in it), and in whatever scrolls it. */
    function inSight(el) {
      if (!el || el === document.body) return true;
      const r = el.getBoundingClientRect();
      const bar = document.querySelector(".topbar");
      // The controller's hints, for what's over them (they're only beside the library's sidebar).
      const hints = document.querySelector(".pad-hints")?.getBoundingClientRect();
      const under = hints && r.right > hints.left && r.left < hints.right;
      let top = bar && !bar.contains(el) ? bar.getBoundingClientRect().bottom : 0, bottom = under ? hints.top : document.documentElement.clientHeight;
      for (let p = el.parentElement, fixed = getComputedStyle(el).position === "fixed"; p && p !== document.body && !fixed; p = p.parentElement) {
        fixed = getComputedStyle(p).position === "fixed";
        if (!/auto|scroll|hidden/.test(getComputedStyle(p).overflowY)) continue;
        const b = p.getBoundingClientRect();
        top = Math.max(top, b.top + p.clientTop);
        bottom = Math.min(bottom, b.top + p.clientTop + p.clientHeight);
      }
      return r.bottom > top + 1 && r.top < bottom - 1;
    }
    let seen = new Set();
    /**
     * Every control the d-pad reaches from the focus (the first press focuses
     * one when none has it). `ignore`: controls a controller gets to another
     * way, left out of what it should reach.
     */
    async function explore(max, ignore) {
      max ??= 400;
      // The window may not be the system's focused one under test; the d-pad works as if it were.
      document.hasFocus = () => true;
      const area = scope();
      seen = new Set();
      const queue = [];
      const shown = new Set();
      const cut = [];
      const outOfSight = new Set();
      // Controls that look no different focused: how it and what it's in look, with the focus elsewhere and then on it.
      const unmarked = new Set();
      const looked = new Set();
      const look = (el) => {
        const parts = [];
        for (let e = el, i = 0; e && i < 4; e = e.parentElement, i++) {
          const st = getComputedStyle(e);
          parts.push([st.outlineStyle, st.outlineWidth, st.outlineColor, st.boxShadow, st.borderColor, st.backgroundColor, st.color, st.transform, st.textDecorationLine].join(" "));
        }
        // And what the control draws with ::before and ::after (the sidebar's edge, a line).
        for (const pseudo of ["::before", "::after"]) {
          const st = getComputedStyle(el, pseudo);
          parts.push([st.backgroundColor, st.borderColor, st.boxShadow, st.opacity].join(" "));
        }
        return parts.join(" / ");
      };
      const add = (el) => {
        if (!el || el === document.body || !area.contains(el) || seen.has(el)) return;
        seen.add(el);
        queue.push(el);
        const by = cutOff(el);
        if (by) cut.push(`${describe(el)}, by ${by}`);
      };
      if (!candidates().includes(document.activeElement)) await press("DPadDown");
      const start = document.activeElement;
      add(start);
      while (queue.length && seen.size < max) {
        const from = queue.shift();
        for (const direction of DIRECTIONS) {
          if (!visible(from) || scope() !== area) break;
          const before = !looked.has(from) && document.activeElement !== from ? look(from) : null;
          // As the app moves the focus (spatial.ts's show): with its ring, whatever was used before.
          from.focus({ focusVisible: true });
          if (before !== null) {
            looked.add(from);
            if (look(from) === before) unmarked.add(describe(from));
          }
          // In sight, as a move to it leaves it: WebKitGTK scrolls the page's area (main) to what took the focus
          // only a frame or more later, so the press would start from a control still out of sight.
          from.scrollIntoView({ block: "nearest" });
          await frame();
          for (const el of candidates()) if (!ignore || !el.matches(ignore)) shown.add(el);
          let at = from.getBoundingClientRect().top;
          await press(direction);
          // A press that only scrolled (on toward a control further on): again, as a player would.
          for (let i = 0; i < 40 && document.activeElement === from; i++) {
            const top = from.getBoundingClientRect().top;
            if (top === at) break;
            at = top;
            await press(direction);
          }
          // A move shows what it moves to. (Reading on through text can leave the focus behind, out of sight.)
          if (document.activeElement !== from && !inSight(document.activeElement)) outOfSight.add(`${describe(document.activeElement)}, after ${direction} from ${describe(from)}`);
          add(document.activeElement);
        }
      }
      const missed = [...shown].filter((el) => visible(el) && !seen.has(el));
      return { start: describe(start), reached: seen.size, shown: shown.size, missed: missed.map(describe), cut, outOfSight: [...outOfSight], unmarked: [...unmarked], capped: seen.size >= max };
    }
    /** Moves to a control the last explore reached (so the d-pad gets there) and presses `key` on it. */
    async function pressOn(selector, key = "South", { text } = {}) {
      const find = () => {
        const now = candidates();
        return [...seen].find((el) => now.includes(el) && el.matches(selector) && (!text || el.innerText.includes(text)));
      };
      // The last explore's, unless it was of another screen.
      let el = find();
      if (!el) await explore(), (el = find());
      if (!el) return `not reached: ${selector}${text ? ` "${text}"` : ""}`;
      el.focus();
      el.scrollIntoView({ block: "nearest" });
      await settle();
      await press(key);
      await sleep(100);
      await settle();
      return describe(el);
    }
    window.__reach = { explore, cutOff, inSight, press, pressOn, describe, candidates, focused: () => describe(document.activeElement) };
  });
}

export const explore = (app, { max, ignore } = {}) => app.execute((max, ignore) => window.__reach.explore(max, ignore), max, ignore);
export const press = (app, key) => app.execute((key) => window.__reach.press(key), key);
export const pressOn = (app, selector, key, options) => app.execute((s, k, o) => window.__reach.pressOn(s, k, o), selector, key, options ?? {});
export const focused = (app) => app.execute(() => window.__reach.focused());
