import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@quiver/ui/styles.css";
import "./app.css";
import { App } from "./App";
import { LauncherProvider } from "./store";
import { startSpatialNavigation } from "./spatial";
import { native } from "./native";

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
    <LauncherProvider>
      <App />
    </LauncherProvider>
  </StrictMode>,
);
