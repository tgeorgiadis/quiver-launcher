/**
 * The top bar: Quiver's mark, the tabs (LB  Home  All  N64 … │ Discover
 * Downloads  RB) with an underline that slides to the selected one, and on the
 * right downloads, controllers and the time. The library's tabs use short
 * console names when the full ones don't fit, and scroll if even those don't.
 */
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Gamepad2 } from "lucide-react";
import { useTv } from "../context";
import type { Tab } from "../consoles/tabs";
import { Glyph } from "./Glyph";

function TabLabel({ tab, selected, compact, onSelect }: { tab: Tab; selected: boolean; compact: boolean; onSelect: (id: string) => void }) {
  return (
    <span role="tab" aria-selected={selected} data-tab={tab.id} className={`tv-tab${selected ? " selected" : ""}`} onClick={() => onSelect(tab.id)}>
      {compact && tab.kind === "console" ? tab.short : tab.label}
      {tab.kind === "downloads" && tab.count ? <span className="tv-tab-count">{tab.count}</span> : null}
    </span>
  );
}

function useClock() {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15_000);
    return () => clearInterval(timer);
  }, []);
  return now.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

/** A ring filling with the active download, and how many are going. */
function DownloadStatus() {
  const { bridge } = useTv();
  const busy = bridge.apps.filter((a) => a.state === "downloading" || a.state === "updating" || a.state === "queued");
  if (!busy.length) return null;
  const active = busy.find((a) => a.progress !== null);
  const p = active?.progress ?? 0;
  return (
    <span className="tv-status-downloads" title={`${busy.length} downloading`}>
      <svg viewBox="0 0 36 36" aria-hidden="true">
        <circle cx="18" cy="18" r="15" className="track" />
        <circle cx="18" cy="18" r="15" className="fill" style={{ strokeDashoffset: 94.25 * (1 - p) }} />
      </svg>
      <span>{busy.length}</span>
    </span>
  );
}

export function TabStrip({ tabs, selected, onSelect }: { tabs: Tab[]; selected: string; onSelect: (id: string) => void }) {
  const { pads, tap } = useTv();
  const clock = useClock();
  const nav = useRef<HTMLElement>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const [compact, setCompact] = useState(false);
  const [bar, setBar] = useState<{ left: number; width: number } | null>(null);
  const library = tabs.filter((t) => t.kind !== "discover" && t.kind !== "downloads");
  const end = tabs.filter((t) => t.kind === "discover" || t.kind === "downloads");
  const key = library.map((t) => t.id).join(" ");

  // Full names first whenever the tabs change; short ones if they overflow.
  useLayoutEffect(() => setCompact(false), [key]);
  useLayoutEffect(() => {
    const el = scroller.current;
    if (el && !compact && el.scrollWidth > el.clientWidth) setCompact(true);
  });
  // The underline follows the selected tab.
  useLayoutEffect(() => {
    const tab = nav.current?.querySelector<HTMLElement>(`[data-tab="${CSS.escape(selected)}"]`);
    tab?.scrollIntoView({ block: "nearest", inline: "nearest" });
    const frame = nav.current?.getBoundingClientRect();
    const box = tab?.getBoundingClientRect();
    setBar(frame && box ? { left: box.left - frame.left, width: box.width } : null);
  }, [selected, compact, key, tabs.length]);

  return (
    <nav className="tv-topbar" aria-label="Library" ref={nav}>
      <img className="tv-mark" src="/quiver-icon-96.png" alt="Quiver" />
      <button type="button" className="tv-glyph-button" aria-label="Previous tab" onClick={() => tap("tabPrev")}>
        <Glyph action="tabPrev" />
      </button>
      <div className="tv-tabs" ref={scroller} role="tablist">
        {library.map((tab) => (
          <TabLabel key={tab.id} tab={tab} selected={tab.id === selected} compact={compact} onSelect={onSelect} />
        ))}
      </div>
      <div className="tv-tabs-end" role="tablist">
        {/* LT / RT go straight between the library tab, Discover and Downloads. */}
        <button type="button" className="tv-glyph-button small" aria-label="Library, Discover, Downloads: back" onClick={() => tap("pagePrev")}>
          <Glyph action="pagePrev" size={22} />
        </button>
        {end.map((tab) => (
          <TabLabel key={tab.id} tab={tab} selected={tab.id === selected} compact={compact} onSelect={onSelect} />
        ))}
        <button type="button" className="tv-glyph-button small" aria-label="Library, Discover, Downloads: forward" onClick={() => tap("pageNext")}>
          <Glyph action="pageNext" size={22} />
        </button>
      </div>
      <button type="button" className="tv-glyph-button" aria-label="Next tab" onClick={() => tap("tabNext")}>
        <Glyph action="tabNext" />
      </button>
      {bar && <span className="tv-tab-bar" style={{ left: bar.left, width: bar.width }} />}
      <div className="tv-status">
        <DownloadStatus />
        <span className={`tv-status-pads${pads.length ? " on" : ""}`} title={pads.join(", ") || "No controller connected"}>
          <Gamepad2 aria-hidden="true" />
          {pads.length > 1 ? pads.length : null}
        </span>
        <span className="tv-clock">{clock}</span>
      </div>
    </nav>
  );
}
