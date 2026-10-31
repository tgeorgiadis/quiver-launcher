/**
 * A dropdown's list, for a controller: a click from script never opens a
 * <select>'s own list, so Confirm on one (a controller's A) opens this in its
 * place, a menu of its options under it as the app's other menus are
 * (library.tsx's Popover). Up and Down go through it, Confirm picks (with the
 * change event the select's own list sends) and Back closes it, the focus back
 * on the dropdown. Enter still opens the webview's own list, as a Steam Deck's
 * A does in its desktop (Steam's desktop layout makes it Enter).
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

export function openPicker(select: HTMLSelectElement) {
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
    left: `${r.left / zoom}px`,
    minWidth: `${r.width / zoom}px`,
    ...(up ? { bottom: `${(box.height - r.top) / zoom + 6}px` } : { top: `${r.bottom / zoom + 6}px` }),
    // Above a phone's navigation bar and the controller hints too.
    maxHeight: `calc(${Math.max(160, (up ? r.top : below) / zoom - 14)}px - ${up ? "0px" : "max(env(safe-area-inset-bottom, 0px), var(--pad-hints, 0px))"})`,
    overflowY: "auto",
  });
  document.body.append(panel);
  // Kept inside the window.
  const p = panel.getBoundingClientRect();
  if (p.right > box.width - 8) panel.style.left = `${Math.max(8, box.width - 8 - p.width) / zoom}px`;

  const away = (e: MouseEvent) => !panel.contains(e.target as Node) && closePicker(false);
  // Focus going elsewhere (a click on another control) closes it, once it has settled there.
  let later: ReturnType<typeof setTimeout> | undefined;
  const left = () => {
    clearTimeout(later);
    later = setTimeout(() => {
      const now = document.activeElement;
      if (now && now !== document.body && !panel.contains(now)) closePicker(false);
    });
  };
  const resize = () => closePicker();
  window.addEventListener("mousedown", away, true);
  window.addEventListener("resize", resize);
  panel.addEventListener("focusout", left);
  open = {
    select,
    panel,
    off: () => {
      clearTimeout(later);
      window.removeEventListener("mousedown", away, true);
      window.removeEventListener("resize", resize);
      panel.removeEventListener("focusout", left);
    },
  };
  const first = chosen && !chosen.disabled ? chosen : panel.querySelector<HTMLButtonElement>("button:not(:disabled)");
  first?.focus({ preventScroll: true, focusVisible: true } as FocusOptions);
  first?.scrollIntoView({ block: "nearest" });
}
