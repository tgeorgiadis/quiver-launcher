/** Light or dark: the player's choice, or the system's when they haven't made one. */
export type Theme = "light" | "dark";

const systemQuery = () => window.matchMedia?.("(prefers-color-scheme: light)");

export function systemTheme(): Theme {
  return systemQuery()?.matches ? "light" : "dark";
}

/** Shows the page in a theme (styles.css's :root[data-theme]). */
export function showTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
}

/** Shows the chosen theme, or the system's and follows it as it changes; returns a stop. */
export function followTheme(chosen: Theme | undefined): () => void {
  if (chosen) {
    showTheme(chosen);
    return () => {};
  }
  showTheme(systemTheme());
  const query = systemQuery();
  if (!query) return () => {};
  const onChange = () => showTheme(systemTheme());
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}
