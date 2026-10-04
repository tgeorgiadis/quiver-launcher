/**
 * The library's view options (view.ts): the controls, in the library's View
 * menu and in Settings, and the picture a box art or icon card shows.
 */
import { useState } from "react";
import { Gamepad2 } from "lucide-react";
import type { Entry } from "@quiver/api";
import { useLauncher } from "./store";
import { CARD_IMAGES, CARD_SIZES, LAYOUTS, artFor, savedView, viewOf, type CardImage, type LibraryView } from "./view";

/** This computer's library view, and a way to change it. */
export function useLibraryView() {
  const { settings, setSettings } = useLauncher();
  const view = viewOf(settings.library);
  return {
    view,
    byConsole: Boolean(settings.byConsole),
    set: (next: Partial<LibraryView> & { byConsole?: boolean }) => {
      const { byConsole, ...rest } = next;
      setSettings({
        ...settings,
        library: savedView({ ...view, ...rest }),
        ...(byConsole !== undefined ? { byConsole } : {}),
      });
    },
  };
}

/** Layout, card image, card size, names and sections by console. */
export function ViewOptions() {
  const { view, byConsole, set } = useLibraryView();
  return (
    <div className="view-options">
      <Choice label="Layout" options={LAYOUTS} value={view.layout} onChange={(layout) => set({ layout })} />
      <Choice label="Card image" options={CARD_IMAGES} value={view.image} onChange={(image) => set({ image })} />
      <Choice label="Card size" options={CARD_SIZES} value={view.size} onChange={(size) => set({ size })} />
      {/* A list always shows names. */}
      <label className="toggle">
        <input type="checkbox" checked={view.layout === "list" || view.names} disabled={view.layout === "list"} onChange={(e) => set({ names: e.target.checked })} />
        Show names under cards
      </label>
      <label className="toggle">
        <input type="checkbox" checked={byConsole} onChange={(e) => set({ byConsole: e.target.checked })} />
        Group by console
      </label>
    </div>
  );
}

/** A few choices side by side, one picked. */
function Choice<T extends string>({ label, options, value, onChange }: { label: string; options: [T, string][]; value: T; onChange: (value: T) => void }) {
  return (
    <div className="view-choice">
      <span className="view-label">{label}</span>
      <div className="segmented" role="group" aria-label={label}>
        {options.map(([option, text]) => (
          <button key={option} type="button" aria-pressed={option === value} onClick={() => onChange(option)}>
            {text}
          </button>
        ))}
      </div>
    </div>
  );
}

/** A card's box art or icon; the next picture when one doesn't load, then the placeholder. */
export function CardArt({ entry, image }: { entry: Pick<Entry, "projectName" | "libraryArt" | "artwork">; image: Exclude<CardImage, "cover"> }) {
  const tries = artFor(entry, image);
  const key = tries.map((a) => a.src).join(" ");
  const [failed, setFailed] = useState({ key, count: 0 });
  const art = tries[failed.key === key ? failed.count : 0];
  if (!art)
    return (
      <div className="card-art placeholder" data-fit="none">
        <Gamepad2 size={44} strokeWidth={1} />
      </div>
    );
  return (
    <div className={`card-art ${art.fit}`} data-fit={art.fit}>
      {art.fit === "letterbox" && <img className="card-art-blur" src={art.src} alt="" aria-hidden="true" loading="lazy" decoding="async" />}
      <img
        src={art.src}
        alt={`${entry.projectName} ${image === "box" ? "box art" : "icon"}`}
        loading="lazy"
        decoding="async"
        onError={() => setFailed((f) => ({ key, count: (f.key === key ? f.count : 0) + 1 }))}
      />
    </div>
  );
}
