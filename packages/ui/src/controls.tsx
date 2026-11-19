import {
  useEffect,
  useId,
  useRef,
  useState,
  type AnchorHTMLAttributes,
  type ButtonHTMLAttributes,
  type HTMLAttributes,
  type ReactNode,
} from "react";

/**
 * The basic controls of Quiver Launcher and quiverlauncher.com, styled by controls.css over tokens.css: one look in
 * both. They have no words of their own: every label comes from the app, in its language.
 */

/** "main" is the view's one filled action; "neutral" every other button; "text" reads as a link; "danger" destroys something. */
export type ButtonVariant = "main" | "neutral" | "text" | "danger";

type ButtonOwn = {
  variant?: ButtonVariant;
  /** Working on what it was pressed for: it can't be pressed again, and shows `busyLabel` (such as "Installing…"). */
  busy?: boolean;
  busyLabel?: ReactNode;
  /** As wide as what it's in. */
  wide?: boolean;
};
export type ButtonProps = ButtonOwn &
  (
    | ({ href: string; disabled?: boolean } & Omit<AnchorHTMLAttributes<HTMLAnchorElement>, "href">)
    | ({ href?: undefined } & ButtonHTMLAttributes<HTMLButtonElement>)
  );

/** The class names of a button, for markup that isn't React's (a static page's links). */
export function buttonClass(variant: ButtonVariant = "neutral", extra?: { wide?: boolean; className?: string }) {
  return ["ql-button", variant !== "neutral" && variant, extra?.wide && "wide", extra?.className].filter(Boolean).join(" ");
}

/** A button, or with `href` a link that looks like one. */
export function Button({ variant = "neutral", busy = false, busyLabel, wide, className, children, ...rest }: ButtonProps) {
  const classes = buttonClass(variant, { wide, className });
  const content = busy && busyLabel !== undefined ? busyLabel : children;
  if (rest.href !== undefined) {
    const { disabled, href, ...anchor } = rest;
    const off = disabled || busy;
    // A link that's off keeps its place and its words, and goes nowhere.
    return (
      <a {...anchor} href={off ? undefined : href} className={classes} aria-disabled={off || undefined} aria-busy={busy || undefined}>
        {content}
      </a>
    );
  }
  const { href: _none, ...button } = rest;
  return (
    <button type="button" {...button} className={classes} disabled={button.disabled || busy} aria-busy={busy || undefined}>
      {content}
    </button>
  );
}

/**
 * A label above its control: pass the input, select or textarea as the child. `hint` sits under it, and `error`
 * takes the hint's place when something is wrong (mark the control `aria-invalid` too).
 */
export function Field({
  label,
  hint,
  error,
  className,
  children,
  ...rest
}: { label: ReactNode; hint?: ReactNode; error?: ReactNode; children: ReactNode } & Omit<HTMLAttributes<HTMLLabelElement>, "children">) {
  return (
    <label {...rest} className={["ql-field", className].filter(Boolean).join(" ")}>
      <span className="ql-field-label">{label}</span>
      {children}
      {error ? (
        <span className="ql-field-error" role="alert">
          {error}
        </span>
      ) : (
        hint && <span className="ql-field-hint">{hint}</span>
      )}
    </label>
  );
}

/** A bordered section of a page, with its title if it has one. */
export function Panel({
  title,
  heading: Heading = "h3",
  className,
  children,
  ...rest
}: { title?: ReactNode; heading?: "h2" | "h3" | "h4"; children: ReactNode } & Omit<HTMLAttributes<HTMLElement>, "title">) {
  return (
    <section {...rest} className={["ql-panel", className].filter(Boolean).join(" ")}>
      {title !== undefined && <Heading className="ql-panel-title">{title}</Heading>}
      {children}
    </section>
  );
}

/**
 * Asks one question: a title, a sentence, and two buttons, the main one on the right. Cancel has the focus first, so
 * Enter can't confirm by accident; Escape or a click outside cancels. While `onConfirm`'s promise is pending the
 * confirm button is busy and nothing closes the dialog.
 */
export function Dialog({
  open,
  title,
  children,
  confirmLabel,
  cancelLabel,
  busyLabel,
  tone = "main",
  confirmDisabled = false,
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: ReactNode;
  children?: ReactNode;
  confirmLabel: ReactNode;
  cancelLabel: ReactNode;
  busyLabel?: ReactNode;
  /** "danger" when confirming destroys something. */
  tone?: "main" | "danger";
  confirmDisabled?: boolean;
  onConfirm: () => void | Promise<void>;
  onCancel: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      // Where there's no showModal (an old webview, a test's DOM) it still shows.
      if (typeof dialog.showModal === "function") dialog.showModal();
      else dialog.setAttribute("open", "");
    } else if (!open && dialog.open) {
      if (typeof dialog.close === "function") dialog.close();
      else dialog.removeAttribute("open");
    }
  }, [open]);
  return (
    <dialog
      ref={ref}
      // Named as well as implied, for the launcher's own checks for an open dialog.
      role="dialog"
      aria-modal="true"
      className="ql-dialog"
      aria-labelledby={titleId}
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) onCancel();
      }}
      onClick={(e) => {
        // A click on the backdrop lands on the dialog element itself.
        if (e.target === e.currentTarget && !busy) onCancel();
      }}
    >
      {open && (
        <>
          <h2 id={titleId} className="ql-dialog-title">
            {title}
          </h2>
          {children !== undefined && <div className="ql-dialog-body">{children}</div>}
          <div className="ql-actions">
            <Button autoFocus disabled={busy} onClick={onCancel}>
              {cancelLabel}
            </Button>
            <Button
              variant={tone}
              busy={busy}
              busyLabel={busyLabel}
              disabled={confirmDisabled}
              onClick={async () => {
                setBusy(true);
                try {
                  await onConfirm();
                } finally {
                  setBusy(false);
                }
              }}
            >
              {confirmLabel}
            </Button>
          </div>
        </>
      )}
    </dialog>
  );
}
