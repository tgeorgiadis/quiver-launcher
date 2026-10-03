import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@quiver/ui/styles.css";
import "./app.css";
import { App } from "./App";
import { LauncherProvider } from "./store";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <LauncherProvider>
      <App />
    </LauncherProvider>
  </StrictMode>,
);
