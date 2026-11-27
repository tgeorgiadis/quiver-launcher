/**
 * A dropdown's list, drawn by Quiver rather than the system: every dropdown in
 * the launcher opens this, a menu of its options under it as the app's other
 * menus are (library.tsx's Popover), so it looks the same everywhere and in
 * both themes (DESIGN.md: every dropdown is styled). The <select> stays the
 * control underneath, its value and change event as before.
 *
 * A click, Space, Enter, Alt+Down or F4 opens it (startSelectLists), and so
 * does a controller's Confirm (spatial.ts), which a click from script never
 * could on the system's own list. Up and Down go through it, a letter jumps to
 * the next option starting with it, Confirm or a click picks (with the change
 * event the select's own list sends) and Back, Escape or a click away closes
 * it, the focus back on the dropdown.
 */
import { boxZoom, windowBox } from "./zoom";

let open: { select: HTMLSelectElement; panel: HTMLElement; off: () => void } | null = null;

/** The list open now, which the d-pad keeps to. */
export const pickerPanel = () => open?.panel ?? null;

/** Closes the list; the focus goes back to its dropdown unless it went somewhere else (a click away). */
export function closePicker(refocus = true) {
  if (!open) return;
  const { select, panel, off } = open;
  open = null;
  off();
  // Back on the dropdown before the list goes, so the focus is never left on nothing.
  if (refocus) select.focus({ preventScroll: true, focusVisible: true } as FocusOptions);
  panel.remove();
}

function pick(select: HTMLSelectElement, value: string) {
  closePicker();
  if (select.value === value) return;
  select.value = value;
  select.dispatchEvent(new Event("input", { bubbles: true }));
  select.dispatchEvent(new Event("change", { bubbles: true }));
}

/** A dropdown this list stands in for: one choice from a closed list (not a list box, which shows its options itself). */
const listed = (target: EventTarget | null): target is HTMLSelectElement =>
  target instanceof HTMLSelectElement && !target.multiple && target.size <= 1 && !target.disabled;

/** Opens the list under (or over) the dropdown; `pointer` when a click opened it, so no focus ring shows on its option. */
export function openPicker(select: HTMLSelectElement, pointer = false) {
  closePicker(false);
  const panel = document.createElement("div");
  panel.className = "menu floating select-picker";
  panel.setAttribute("role", "listbox");
  panel.setAttribute("aria-label", select.getAttribute("aria-label") ?? select.labels?.[0]?.textContent?.trim() ?? "");
  panel.tabIndex = -1;
  let chosen: HTMLButtonElement | undefined;
  for (const option of select.options) {
    if (option.hidden) continue;
    const item = document.createElement("button");
    item.type = "button";
    item.setAttribute("role", "option");
    item.setAttribute("aria-selected", String(option.selected));
    item.disabled = option.disabled;
    item.textContent = option.label;
    item.addEventListener("click", () => pick(select, option.value));
    panel.append(item);
    if (option.selected) chosen = item;
  }
  // Under the dropdown, or above it when there's more room there, as Popover places its panels.
  // The interface size zooms the page; the panel's position is set in unzoomed pixels (zoom.ts).
  const [zoom, box] = [boxZoom(), windowBox()];
  const r = select.getBoundingClientRect();
  const below = box.height - r.bottom;
  const up = below < 240 && r.top > below;
  Object.assign(panel.style, {
    left: "0px",
    top: "0px",
    minWidth: `${r.width / zoom}px`,
    // Above a phone's navigation bar and the controller hints too.
    maxHeight: `calc(${Math.max(160, (up ? r.top : below) / zoom - 14)}px - ${up ? "0px" : "max(env(safe-area-inset-bottom, 0px), var(--pad-hints, 0px))"})`,
    overflowY: "auto",
  });
  // In an open dialog, inside it: a modal dialog leaves everything outside it out of reach.
  (select.closest("dialog[open]") ?? document.body).append(panel);
  // Where its corner landed: the window's, or that of a dialog its opening motion moved; the place is set from there.
  const corner = panel.getBoundingClientRect();
  const left = Math.max(8, Math.min(r.left, box.width - 8 - corner.width));
  const top = up ? r.top - 6 - corner.height : r.bottom + 6;
  panel.style.left = `${(left - corner.left) / zoom}px`;
  panel.style.top = `${(top - corner.top) / zoom}px`;

  const away = (e: MouseEvent) => !panel.contains(e.target as Node) && closePicker(false);
  // Focus going elsewhere (a click on another control) closes it, once it has settled there.
  let later: ReturnType<typeof setTimeout> | undefined;
  const settle = () => {
    clearTimeout(later);
    later = setTimeout(() => {
      const now = document.activeElement;
      if (now && now !== document.body && !panel.contains(now)) closePicker(false);
    });
  };
  const resize = () => closePicker();
  // The page scrolling would leave it behind, away from its dropdown; its own scrolling doesn't.
  const scrolled = (e: Event) => !panel.contains(e.target as Node) && closePicker();
  // A letter goes to the next option that starts with it; Home and End to the first and last.
  const keys = (e: KeyboardEvent) => {
    const items = [...panel.querySelectorAll<HTMLButtonElement>("button:not(:disabled)")];
    const now = items.indexOf(document.activeElement as HTMLButtonElement);
    let to: HTMLButtonElement | undefined;
    if (e.key === "Home") to = items[0];
    else if (e.key === "End") to = items[items.length - 1];
    else if (e.key.length === 1 && e.key !== " " && !e.ctrlKey && !e.metaKey && !e.altKey) {
      const letter = e.key.toLocaleLowerCase();
      const round = [...items.slice(now + 1), ...items.slice(0, now + 1)];
      to = round.find((item) => item.textContent?.trim().toLocaleLowerCase().startsWith(letter));
    }
    if (!to) return;
    e.preventDefault();
    to.focus({ preventScroll: true, focusVisible: true } as FocusOptions);
    to.scrollIntoView({ block: "nearest" });
  };
  window.addEventListener("mousedown", away, true);
  window.addEventListener("resize", resize);
  window.addEventListener("scroll", scrolled, true);
  panel.addEventListener("focusout", settle);
  panel.addEventListener("keydown", keys);
  open = {
    select,
    panel,
    off: () => {
      clearTimeout(later);
      window.removeEventListener("mousedown", away, true);
      window.removeEventListener("resize", resize);
      window.removeEventListener("scroll", scrolled, true);
      panel.removeEventListener("focusout", settle);
      panel.removeEventListener("keydown", keys);
    },
  };
  const first = chosen && !chosen.disabled ? chosen : panel.querySelector<HTMLButtonElement>("button:not(:disabled)");
  first?.focus({ preventScroll: true, focusVisible: !pointer } as FocusOptions);
  first?.scrollIntoView({ block: "nearest" });
}

/**
 * Every dropdown opens Quiver's list in place of the system's: on a click (a
 * second click on the same dropdown closes it), and on the keys that open a
 * dropdown's own list. Started once, before spatial navigation, so these run
 * before its keys do.
 */
export function startSelectLists() {
  window.addEventListener(
    "mousedown",
    (e) => {
      if (e.button !== 0 || !listed(e.target)) return;
      // The system's own list opens on this press: kept shut.
      e.preventDefault();
      const select = e.target;
      if (open?.select === select) return closePicker();
      select.focus({ preventScroll: true });
      openPicker(select, true);
    },
    true,
  );
  window.addEventListener(
    "keydown",
    (e) => {
      if (!listed(e.target) || e.ctrlKey || e.metaKey) return;
      const opens = e.key === " " || e.key === "Enter" || e.key === "F4" || (e.altKey && (e.key === "ArrowDown" || e.key === "ArrowUp"));
      if (!opens) return;
      e.preventDefault();
      e.stopImmediatePropagation();
      openPicker(e.target);
    },
    true,
  );
}
