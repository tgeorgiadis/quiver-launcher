/**
 * Closing a dialog's overlay on a click outside the dialog. A click is where
 * the press and the release have in common, so a drag that starts in the
 * dialog (selecting a text field's text) and ends outside it is a click on the
 * overlay: it only closes when the press started on the overlay too.
 */
import type { MouseEvent, PointerEvent } from "react";

/** Where the last press started; one overlay is pressed at a time. */
let pressed: EventTarget | null = null;

/** An overlay's handlers: `onClose` on a press and release both on it, outside the dialog it holds. */
export const closesOutside = (onClose: () => void) => ({
  onPointerDown: (e: PointerEvent) => void (pressed = e.target),
  onClick: (e: MouseEvent) => {
    if (e.target === e.currentTarget && pressed === e.currentTarget) onClose();
  },
});
