import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { I18nProvider, useLingui } from "@lingui/react";
import { Trans } from "@lingui/react/macro";
import { activate, i18n, pick } from "@quiverlauncher/i18n";
import "@quiverlauncher/ui/fonts.css";
import "./base.css";
import "@quiverlauncher/ui/catalog.css";
import "./app.css";
import { App } from "./App";
import { LauncherProvider } from "./store";
import { PadHints } from "./pad-hints";
import { onBackButtonPress } from "@tauri-apps/api/app";
import { goBack, startSpatialNavigation } from "./spatial";
import { native } from "./native";
import { ErrorBoundary } from "./boundary";
import { showTheme, systemTheme } from "./theme";
import { startFpsProbe } from "./fps-probe";
import { invoke } from "@tauri-apps/api/core";

// Until settings load, the window's own theme (set from them at start, or the system's).
showTheme(systemTheme());
startSpatialNavigation();
startFpsProbe();
// On CEF the window can't tell when it's in front (gamepad.rs), so controllers go by the page's focus.
window.addEventListener("focus", () => void invoke("page_focus", { focused: true }).catch(() => {}));
window.addEventListener("blur", () => void invoke("page_focus", { focused: false }).catch(() => {}));
// Android's Back (its button or gesture, or a TV remote's) goes back in the launcher, as Escape does; its
// own only knew the webview's history, which the launcher has none of, so it closed the launcher from any page. With
// nothing left to go back from, it puts the launcher away as Home does, its downloads still going.
void native
  .config()
  .then((config) => {
    if (config.os === "android") return onBackButtonPress(() => void (goBack() || native.toBackground().catch(() => {})));
  })
  .catch(() => {});
window.addEventListener("error", (e) => void native.logError(`ui: ${e.message} at ${e.filename}:${e.lineno}`).catch(() => {}));
window.addEventListener("unhandledrejection", (e) => void native.logError(`ui: ${String(e.reason?.stack ?? e.reason)}`).catch(() => {}));

// Web links (README, repository) open in the browser, not in the launcher's window. Caught on the way down, before a
// dialog's own click handler stops it on the way up (a link in Announcements took the whole window to the website).
document.addEventListener(
  "click",
  (e) => {
    const link = (e.target as Element).closest?.("a[href]") as HTMLAnchorElement | null;
    if (!link || !/^https?:/i.test(link.href)) return;
    e.preventDefault();
    void native.openUrl(link.href).catch(() => {});
  },
  true,
);

// A right-click shows no browser menu (Back, Refresh, Print, Inspect): only text fields keep theirs, for cut, copy and
// paste, and the launcher's own menus open as before. `tauri dev` keeps it, for Inspect.
const typing = (el: Element | null) => {
  const field = el?.closest?.("input, textarea, [contenteditable]:not([contenteditable='false'])");
  return !!field && !(field instanceof HTMLInputElement && /^(button|checkbox|color|file|hidden|image|radio|range|reset|submit)$/.test(field.type));
};
if (!import.meta.env.DEV)
  document.addEventListener("contextmenu", (e) => {
    if (!typing(e.target as Element)) e.preventDefault();
  });

/** The launcher, drawn again whole when the language changes: a string worked out outside a component has no hook of its own to follow it. */
function Localized() {
  useLingui();
  return (
    <>
      <App />
      <PadHints />
    </>
  );
}

// The system's language until settings load, when the store shows the one picked there; so even Recovery reads in it.
void activate(pick(navigator.languages)).finally(() =>
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <I18nProvider i18n={i18n}>
        {/* Last resort: a message and a way out, never a blank window. */}
        <ErrorBoundary
          fallback={(error) => (
            <div className="empty" role="alert">
              <h2>
                <Trans>Quiver Launcher hit a problem</Trans>
              </h2>
              <p>{error.message}</p>
              <button className="primary" onClick={() => location.reload()}>
                <Trans>Reload</Trans>
              </button>
            </div>
          )}
        >
          <LauncherProvider>
            <Localized />
          </LauncherProvider>
        </ErrorBoundary>
      </I18nProvider>
    </StrictMode>,
  ),
);
