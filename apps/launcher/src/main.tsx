import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./base.css";
import "@quiverlauncher/ui/catalog.css";
import "./app.css";
import { App } from "./App";
import { LauncherProvider } from "./store";
import { startSpatialNavigation } from "./spatial";
import { native } from "./native";
import { ErrorBoundary } from "./boundary";
import { showTheme, systemTheme } from "./theme";

// Until settings load, the window's own theme (set from them at start, or the system's).
showTheme(systemTheme());
startSpatialNavigation();
window.addEventListener("error", (e) => void native.logError(`ui: ${e.message} at ${e.filename}:${e.lineno}`).catch(() => {}));
window.addEventListener("unhandledrejection", (e) => void native.logError(`ui: ${String(e.reason?.stack ?? e.reason)}`).catch(() => {}));

// Web links (README, repository) open in the browser, not in the launcher's window.
document.addEventListener("click", (e) => {
  const link = (e.target as Element).closest?.("a[href]") as HTMLAnchorElement | null;
  if (!link || !/^https?:/i.test(link.href)) return;
  e.preventDefault();
  void native.openUrl(link.href).catch(() => {});
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    {/* Last resort: a message and a way out, never a blank window. */}
    <ErrorBoundary
      fallback={(error) => (
        <div className="empty" role="alert">
          <h2>Quiver Launcher hit a problem</h2>
          <p>{error.message}</p>
          <button className="primary" onClick={() => location.reload()}>
            Reload
          </button>
        </div>
      )}
    >
      <LauncherProvider>
        <App />
      </LauncherProvider>
    </ErrorBoundary>
  </StrictMode>,
);
