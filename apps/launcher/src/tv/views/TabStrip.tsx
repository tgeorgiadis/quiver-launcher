/**
 * The top bar: Quiver's mark, the tabs (LB  Home  All  N64 … │ Discover
 * Downloads  RB) with an underline that slides to the selected one, and on the
 * right downloads, controllers and the time. The library's tabs use short
 * console names when the full ones don't fit, and scroll if even those don't.
 */
import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { Gamepad2 } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import { useTv } from "../context";
import type { Tab } from "../consoles/tabs";
import { Glyph } from "./Glyph";

function TabLabel({ tab, selected, compact, pop, onSelect }: { tab: Tab; selected: boolean; compact: boolean; pop: boolean; onSelect: (id: string) => void }) {
  return (
    <span role="tab" aria-selected={selected} data-tab={tab.id} className={`tv-tab${selected ? " selected" : ""}${pop ? " pop" : ""}`} onClick={() => onSelect(tab.id)}>
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
  return now.toLocaleTimeString(i18n.locale, { hour: "numeric", minute: "2-digit" });
}

/** A ring filling with the active download, and how many are going. */
function DownloadStatus() {
  const { bridge } = useTv();
  const busy = bridge.apps.filter((a) => a.state === "downloading" || a.state === "updating" || a.state === "queued");
  if (!busy.length) return null;
  const p = busy.find((a) => a.progress !== null)?.progress ?? 0;
  const active = busy.length;
  return (
    <span className="tv-status-downloads" title={plural(active, { one: "# downloading", other: "# downloading" })}>
      <svg viewBox="0 0 36 36" aria-hidden="true">
        <circle cx="18" cy="18" r="15" className="track" />
        <circle cx="18" cy="18" r="15" className="fill" style={{ strokeDashoffset: 94.25 * (1 - p) }} />
      </svg>
      <span>{busy.length}</span>
    </span>
  );
}

/** `pop`: LB / RB or LT / RT just came to the selected tab (each time a new number), which pops as it does. */
export function TabStrip({ tabs, selected, onSelect, pop }: { tabs: Tab[]; selected: string; onSelect: (id: string) => void; pop?: number }) {
  const tabLabel = (tab: Tab) => (
    <TabLabel
      key={tab.id === selected && pop ? `${tab.id}:${pop}` : tab.id}
      tab={tab}
      selected={tab.id === selected}
      compact={compact}
      pop={tab.id === selected && Boolean(pop)}
      onSelect={onSelect}
    />
  );
  const { pads, tap } = useTv();
  const clock = useClock();
  const nav = useRef<HTMLElement>(null);
  const scroller = useRef<HTMLDivElement>(null);
  const [compact, setCompact] = useState(false);
  const [bar, setBar] = useState<{ left: number; width: number } | null>(null);
  /** Library tabs past the row's edges (more consoles than fit): each side fades to say so. */
  const [more, setMore] = useState({ left: false, right: false });
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
    const el = scroller.current;
    if (el) {
      const next = { left: el.scrollLeft > 1, right: el.scrollLeft + el.clientWidth < el.scrollWidth - 1 };
      setMore((m) => (m.left === next.left && m.right === next.right ? m : next));
    }
  }, [selected, compact, key, tabs.length]);

  return (
    <nav className="tv-topbar" aria-label={t`Library`} ref={nav}>
      <img className="tv-mark" src="/quiver-icon-96.png" alt="Quiver" />
      <button
        type="button"
        className="tv-glyph-button"
        aria-label={t({ message: "Previous tab", comment: "Controller action: the tab before the open one, as on an app's page" })}
        onClick={() => tap("tabPrev")}
      >
        <Glyph action="tabPrev" />
      </button>
      <div className={`tv-tabs${more.left ? " more-left" : ""}${more.right ? " more-right" : ""}`} ref={scroller} role="tablist">
        {library.map(tabLabel)}
      </div>
      <div className="tv-tabs-end" role="tablist">
        {/* LT / RT go straight between the library tab, Discover and Downloads. */}
        <button type="button" className="tv-glyph-button small" aria-label={t({ message: "Library, Discover, Downloads: back", comment: "TV mode's button that steps back between the library, Discover and Downloads" })}
          onClick={() => tap("pagePrev")}
        >
          <Glyph action="pagePrev" size={22} />
        </button>
        {end.map(tabLabel)}
        <button type="button" className="tv-glyph-button small" aria-label={t({ message: "Library, Discover, Downloads: forward", comment: "TV mode's button that steps on between the library, Discover and Downloads" })}
          onClick={() => tap("pageNext")}
        >
          <Glyph action="pageNext" size={22} />
        </button>
      </div>
      <button
        type="button"
        className="tv-glyph-button"
        aria-label={t({ message: "Next tab", comment: "Controller action: the tab after the open one, as on an app's page" })}
        onClick={() => tap("tabNext")}
      >
        <Glyph action="tabNext" />
      </button>
      {bar && <span className="tv-tab-bar" style={{ left: bar.left, width: bar.width }} />}
      <div className="tv-status">
        <DownloadStatus />
        <span className={`tv-status-pads${pads.length ? " on" : ""}`} title={pads.join(", ") || t`No controller connected`}>
          <Gamepad2 aria-hidden="true" />
          {pads.length > 1 ? pads.length : null}
        </span>
        <span className="tv-clock">{clock}</span>
      </div>
    </nav>
  );
}
