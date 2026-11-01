/**
 * Along the bottom while a controller is connected: what its buttons do for
 * the focused control (spatial.ts's buttonsNow), labelled as on the controller
 * used last, by the player's bindings. Settings can hide it.
 */
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { t } from "@lingui/core/macro";
import { native, type PadLabels } from "./native";
import { useLauncher } from "./store";
import { DEFAULT_PAD, buttonsNow, confirmButton, label, pageArea, quickButton, takesText, withDefaults, type Action, type Bindings } from "./spatial";
import { boxZoom } from "./zoom";

/** The connected controllers' names, kept up to date. */
export function usePads() {
  const [pads, setPads] = useState<string[]>([]);
  useEffect(() => {
    // Listening before asking: one there from the start (a Steam Deck's own) is told of at once, maybe before this listens.
    const off = native.onControllers(setPads);
    void off.then(() => native.controllers()).then(setPads);
    return () => void off.then((stop) => stop());
  }, []);
  return pads;
}

/**
 * Beside a row of tabs while a controller's connected: the shoulder button that goes to the tab before (or after) the
 * open one (spatial.ts), as a console's store pages show it. None where Steam's desktop layout has the controller, whose
 * shoulder buttons aren't Quiver's there, or with the hints turned off.
 */
export function TabPrompt({ step }: { step: "prevTab" | "nextTab" }) {
  const { settings } = useLauncher();
  const pads = usePads();
  const labels = usePadLabels();
  const binding = withDefaults(settings.pad, DEFAULT_PAD)[step][0];
  if (!pads.length || settings.padOff || settings.padHintsOff || labels?.desktop || !binding) return null;
  return (
    <span className="tab-prompt" data-step={step} aria-hidden="true">
      {label("pad", binding, labels)}
    </span>
  );
}

/** What the controller used last calls its buttons, kept up to date. */
export function usePadLabels() {
  const [labels, setLabels] = useState<PadLabels | null>(null);
  useEffect(() => {
    // Listening before asking, as for the controllers.
    const off = native.onControllerLabels(setLabels);
    void off.then(() => native.controllerLabels()).then(setLabels);
    return () => void off.then((stop) => stop());
  }, []);
  return labels;
}

/** A pair of buttons as one hint, as LB RB for the tabs: both, or the one of them that's bound. */
const pairLabel = (pad: Bindings, labels: PadLabels | null, back: Action, on: Action) =>
  [pad[back][0], pad[on][0]].filter(Boolean).map((b) => label("pad", b, labels)).join(" ");

/** The right stick as "RS" when it scrolls both ways, else the two bindings. */
function scrollLabel(pad: Bindings, labels: PadLabels | null) {
  const [up, down] = [pad.scrollUp[0], pad.scrollDown[0]];
  if (up === "RightStickUp" && down === "RightStickDown") return "RS";
  if (up === "LeftStickUp" && down === "LeftStickDown") return "LS";
  return [up, down].filter(Boolean).map((b) => label("pad", b, labels)).join(" ");
}

/**
 * The hints for a controller whose presses come as Steam's desktop layout's keys, whatever the bindings say: A is
 * Enter, so Select, B is Escape, so Back, Y is Space, so Options, and on a text field X brings up Steam's keyboard.
 * Its other buttons are Steam's mouse and keys there, so nothing of Quiver's.
 */
function desktopHints(labels: PadLabels, { buttons, typing }: Now) {
  return buttons.flatMap(([action, does]) => {
    if (action === "confirm" && typing)
      return [{ action: "keyboard", binding: labels.west, does: t({ message: "Keyboard", comment: "Controller hint: what the button bringing up Steam's on-screen keyboard does" }) }];
    if (action === "confirm") return [{ action, binding: labels.south, does }];
    if (action === "back") return [{ action, binding: labels.east, does }];
    // Y is Space there, which spatial.ts takes as Options.
    if (action === "options") return [{ action, binding: labels.north, does }];
    return [];
  });
}

/** What the buttons do now (spatial.ts's buttonsNow), and whether the focus is on a text field. */
type Now = { buttons: [Action, string][]; typing: boolean };

export function PadHints() {
  const { settings } = useLauncher();
  const pads = usePads();
  const labels = usePadLabels();
  const shown = pads.length > 0 && !settings.padOff && !settings.padHintsOff;
  // The button a press of the controller's presses from the focus shows which: the Get (or Play) that Options presses
  // (a catalog card's, or the page's), as A opens such a card; else the Play or Get that Confirm presses on a library
  // card, where it has a word beside it (not an icon card's round Play, with no room).
  const bound = withDefaults(settings.pad, DEFAULT_PAD);
  const optionsGlyph = shown && bound.options[0] ? label("pad", bound.options[0], labels) : "";
  const confirmGlyph = shown && bound.confirm[0] ? label("pad", bound.confirm[0], labels) : "";
  const glyphs = useRef({ optionsGlyph, confirmGlyph });
  glyphs.current = { optionsGlyph, confirmGlyph };
  // Changed only when it does, and the mark taken off and put back restyled the button each time.
  const marked = useRef<HTMLElement | null>(null);
  const mark = useCallback(() => {
    const { optionsGlyph, confirmGlyph } = glyphs.current;
    const quick = optionsGlyph ? quickButton(document.activeElement) : null;
    const play = !quick && confirmGlyph ? confirmButton(document.activeElement) : null;
    const button = quick ?? (play && !play.closest(".image-icon") ? play : null);
    const glyph = quick ? optionsGlyph : confirmGlyph;
    if (marked.current !== button) marked.current?.removeAttribute("data-pad-glyph");
    if (button && button.getAttribute("data-pad-glyph") !== glyph) button.setAttribute("data-pad-glyph", glyph);
    marked.current = button;
  }, []);
  useLayoutEffect(mark, [mark, optionsGlyph, confirmGlyph]);
  useEffect(() => () => marked.current?.removeAttribute("data-pad-glyph"), []);
  // Looked at again whenever the focus moves, the page changes (a dialog, a menu) or has scrolled, and drawn again
  // only when what it says does: on a Steam Deck, drawing it anew with each frame a held d-pad scrolled the page, and
  // with each step, was most of that time.
  const [now, setNow] = useState<Now>({ buttons: [], typing: false });
  useEffect(() => {
    if (!shown) return;
    let frame = 0;
    let settled = 0;
    let said = "";
    const check = () => {
      mark();
      const next = { buttons: buttonsNow(), typing: takesText(document.activeElement) };
      const saying = JSON.stringify(next);
      if (saying !== said) (said = saying), setNow(next);
    };
    check();
    const soon = () => (frame ||= requestAnimationFrame(() => ((frame = 0), check())));
    // Scrolling changes only Top, Scroll and Show (spatial.ts's buttonsNow): looked at once it stops.
    const scrolled = () => {
      clearTimeout(settled);
      settled = window.setTimeout(soon, 150);
    };
    document.addEventListener("focusin", soon);
    document.addEventListener("focusout", soon);
    window.addEventListener("scroll", scrolled, { capture: true, passive: true });
    // Not its own changes, which would have it go round again.
    const changes = new MutationObserver((records) => records.some((r) => !bar.current?.contains(r.target)) && soon());
    changes.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["hidden"] });
    return () => {
      cancelAnimationFrame(frame);
      clearTimeout(settled);
      document.removeEventListener("focusin", soon);
      document.removeEventListener("focusout", soon);
      window.removeEventListener("scroll", scrolled, { capture: true });
      changes.disconnect();
    };
    // Again with the settings, the language among them, which what it says is in.
  }, [shown, settings, mark]);
  // The page leaves room for it at the bottom (app.css, and spatial.ts scrolls out from under it).
  const bar = useRef<HTMLElement>(null);
  useLayoutEffect(() => {
    const root = document.documentElement.style;
    if (!shown || !bar.current) {
      root.removeProperty("--pad-hints");
      return;
    }
    // Set only when its height changes: as the hints change, it gets wider or narrower, and each new value on the
    // page's root restyled the whole page.
    const room = new ResizeObserver(() => {
      const height = `${bar.current?.offsetHeight ?? 0}px`;
      if (root.getPropertyValue("--pad-hints") !== height) root.setProperty("--pad-hints", height);
    });
    room.observe(bar.current);
    return () => {
      room.disconnect();
      root.removeProperty("--pad-hints");
    };
  }, [shown]);
  // Beside the library's sidebar when it shows (the Sidebar layout), which runs to the window's bottom, and
  // as tall as the strip along its foot (app.css makes that as tall as this), so their tops are one line.
  // The page runs on under it there, so it stops at the page's scrollbar.
  const [left, setLeft] = useState(0);
  const [right, setRight] = useState(0);
  const [foot, setFoot] = useState(0);
  useLayoutEffect(() => {
    if (!shown) return;
    const side = document.querySelector<HTMLElement>(".library-sidebar");
    const strip = side?.querySelector<HTMLElement>(".sidebar-foot");
    const measure = () => {
      const showing = Boolean(side?.getClientRects().length);
      // The interface size zooms the page; the bar's place is set in unzoomed pixels (zoom.ts).
      setLeft(showing ? Math.round(side!.getBoundingClientRect().right / boxZoom()) : 0);
      const page = pageArea();
      setRight(showing ? page.offsetWidth - page.clientWidth : 0);
      setFoot(showing ? (strip?.offsetHeight ?? 0) : 0);
    };
    measure();
    // As it's dragged wider or narrower, and as the window changes size.
    const resized = side ? new ResizeObserver(measure) : undefined;
    if (side) resized?.observe(side);
    if (strip) resized?.observe(strip);
    window.addEventListener("resize", measure);
    return () => {
      resized?.disconnect();
      window.removeEventListener("resize", measure);
    };
  });
  if (!shown) return null;
  const hints = labels?.desktop
    ? desktopHints(labels, now)
    : now.buttons.flatMap(([action, does]) => {
        const binding =
          action === "scrollDown"
            ? scrollLabel(bound, labels)
            : action === "nextTab"
              ? pairLabel(bound, labels, "prevTab", "nextTab")
              : action === "nextLetter"
                ? pairLabel(bound, labels, "prevLetter", "nextLetter")
                : bound[action][0] && label("pad", bound[action][0], labels);
        return binding ? [{ action: action as string, binding, does }] : [];
      });
  return (
    // Only what the buttons say already: not for a screen reader, nor anything to move to.
    <footer ref={bar} className="pad-hints" aria-hidden="true" data-kind={labels?.kind} style={left ? { left, right, minHeight: foot || undefined } : undefined}>
      {hints.map(({ action, binding, does }) => (
        <span key={action} data-action={action}>
          <span className="pad-glyph">{binding}</span>
          {does}
        </span>
      ))}
    </footer>
  );
}
