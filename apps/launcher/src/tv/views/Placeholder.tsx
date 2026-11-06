/** Home, Discover and Downloads before they had views of their own: the tab keys still work. Nothing uses it now. */
import { useInputHandler } from "../context";
import type { Tab } from "../consoles/tabs";
import type { InputAction } from "../input/actions";
import type { Back } from "./GridTab";

const COMING: Record<string, string> = {
  home: "Recently played, updates and recently installed shelves arrive here.",
  discover: "Browse the catalog and get new games from here.",
  downloads: "Downloads in progress, waiting and finished show here.",
};

export function Placeholder({ tab, onGlobal, back }: { tab: Tab; onGlobal: (a: InputAction) => boolean; back?: Back }) {
  useInputHandler({
    onAction(action) {
      if (!onGlobal(action) && action === "back") back?.go();
    },
    hints: () => (back ? [{ action: "back", label: back.label }] : []),
  });
  return (
    <div className="tv-empty">
      <h2>{tab.label}</h2>
      <p>{COMING[tab.kind] ?? ""}</p>
    </div>
  );
}
