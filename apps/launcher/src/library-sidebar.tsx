/**
 * The library's Sidebar layout (view.ts): the library as a list of names down
 * the left, under headings, and the app picked in it open beside it. The
 * sidebar stays in place under the top bar as the app's page scrolls.
 */
import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent as KeyEvent, type PointerEvent, type ReactNode, type RefObject } from "react";
import { Pin, X } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { hasUpdate, useLauncher, type Install, type LibraryItem } from "./store";
import { CardArt, useLibraryView } from "./library-view";
import { namesOf } from "./library-tile";
import { SIDEBAR_WIDTHS, dayLabel, playTimeLabel, sidebarWidth } from "./sidebar";
import { versionLabel } from "./versions";
import { GameMenu } from "./card-menu";
import { pageArea } from "./spatial";
import { zoom } from "./zoom";

/**
 * The sidebar beside the open app's page. The sidebar sits just under the top
 * bar, as tall as the window below it, kept so as the window and the top bar
 * change size. Its edge drags it wider or narrower, the page taking up the rest.
 */
export function SidebarLayout({ side, children }: { side: ReactNode; children: ReactNode }) {
  const { settings, setSettings } = useLauncher();
  const split = useRef<HTMLDivElement>(null);
  // While dragging, the width so far; saved when let go.
  const [dragged, setDragged] = useState<number>();
  const width = dragged ?? settings.sidebarWidth;
  const [height, setHeight] = useState(0);
  useLayoutEffect(() => {
    // What the page scrolls in: under the top bar, to the window's bottom.
    const page = pageArea();
    const measure = () => setHeight(page.clientHeight);
    measure();
    const observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(measure) : undefined;
    observer?.observe(page);
    return () => observer?.disconnect();
  }, []);
  const save = (sidebarWidth?: number) => {
    setDragged(undefined);
    setSettings({ ...settings, sidebarWidth });
  };
  return (
    <div
      ref={split}
      className="library-split"
      style={{ "--under-height": height ? `${height}px` : "100vh", ...(width ? { "--sidebar-width": `${width}px` } : {}) } as CSSProperties}
    >
      <aside className="library-sidebar" aria-label={t`Library`}>
        {side}
        <SidebarEdge split={split} width={width} onDrag={setDragged} onDone={save} />
      </aside>
      <div className="library-pane">{children}</div>
    </div>
  );
}

/**
 * The sidebar's edge: dragged, or moved with the arrow keys, it sets the
 * sidebar's width; a double click puts it back to fit the window.
 */
function SidebarEdge({ split, width, onDrag, onDone }: { split: RefObject<HTMLDivElement | null>; width?: number; onDrag: (width: number) => void; onDone: (width?: number) => void }) {
  const drag = useRef<{ x: number; width: number; scale: number; total: number; to?: number }>(undefined);
  const sidebar = () => split.current?.querySelector<HTMLElement>(".library-sidebar");
  // In the page's pixels: the pointer moves in the window's, which the interface size zooms.
  const measure = () => {
    const el = sidebar();
    if (!el || !split.current) return undefined;
    return { width: el.offsetWidth, scale: 1 / zoom(), total: split.current.offsetWidth };
  };
  const start = (e: PointerEvent<HTMLDivElement>) => {
    const m = measure();
    if (e.button !== 0 || !m) return;
    // Focused as any click focuses it, so the arrow keys move it next, with no focus ring.
    e.currentTarget.setPointerCapture(e.pointerId);
    drag.current = { x: e.clientX, ...m };
    document.documentElement.classList.add("resizing-sidebar");
  };
  const move = (e: PointerEvent<HTMLDivElement>) => {
    // With another button already down, pressing or letting go of the left one comes as a move (a chorded press), not a down or up.
    if (e.button === 0 && !drag.current && e.buttons & 1) return start(e);
    if (e.button === 0 && drag.current && !(e.buttons & 1)) return end(e);
    const d = drag.current;
    if (!d) return;
    d.to = sidebarWidth(d.width + (e.clientX - d.x) * d.scale, d.total);
    onDrag(d.to);
  };
  const end = (e: PointerEvent<HTMLDivElement>) => {
    const d = drag.current;
    drag.current = undefined;
    // Held until every button is up otherwise, keeping the edge hovered (its line showing) after a chorded drag.
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
    document.documentElement.classList.remove("resizing-sidebar");
    if (d?.to !== undefined) onDone(d.to);
  };
  useEffect(() => () => document.documentElement.classList.remove("resizing-sidebar"), []);
  const key = (e: KeyEvent<HTMLDivElement>) => {
    const m = measure();
    const by = e.key === "ArrowLeft" ? -16 : e.key === "ArrowRight" ? 16 : 0;
    if (!m || !by) return;
    e.preventDefault();
    onDone(sidebarWidth(m.width + by, m.total));
  };
  return (
    <div
      className="sidebar-edge"
      role="separator"
      aria-orientation="vertical"
      aria-label={t`Sidebar width`}
      aria-valuemin={SIDEBAR_WIDTHS[0]}
      aria-valuemax={SIDEBAR_WIDTHS[1]}
      aria-valuenow={width ?? sidebar()?.offsetWidth}
      tabIndex={0}
      title={t`Drag to resize. Double-click to fit the window.`}
      onPointerDown={start}
      onPointerMove={move}
      onPointerUp={end}
      onPointerCancel={end}
      onDoubleClick={() => onDone(undefined)}
      onKeyDown={key}
    />
  );
}

/** The sidebar's filters, as a panel in the sidebar under its search, over its apps; Escape or its close button puts it away. */
export function FiltersPanel({ onClose, children }: { onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const key = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [onClose]);
  return (
    <section id="library-filters" className="sidebar-filters" role="group" aria-label={t`Library filters`}>
      <header>
        <h2>
          <Trans>Filters</Trans>
        </h2>
        <button type="button" className="icon" aria-label={t`Close filters`} onClick={onClose}>
          <X size={15} />
        </button>
      </header>
      {children}
    </section>
  );
}

/** The sidebar's apps under their headings, each heading folding its apps away. */
export function SidebarList({
  groups,
  selected,
  onSelect,
  actions = {},
  withMenu,
  folded,
  onFold,
}: {
  /** Headings and their apps; an empty heading shows its apps without one. */
  groups: [string, Entry[]][];
  selected?: string;
  onSelect: (entry: Entry) => void;
  /** A button under a heading, over its apps, by heading. */
  actions?: Record<string, ReactNode>;
  /** Whether an app's row has its menu, as a library card does. */
  withMenu: (entry: Entry) => boolean;
  /** The headings folded away, kept by the library so one button can fold or open them all. */
  folded: { has: (heading: string) => boolean };
  onFold: (heading: string) => void;
}) {
  const { view } = useLibraryView();
  const fold = onFold;
  return (
    <nav className="sidebar-list" aria-label={t`Your apps`}>
      {groups.map(([heading, entries]) => (
        <section key={heading || "all"} className="sidebar-group" aria-label={heading || undefined}>
          {heading && (
            <button type="button" className="sidebar-heading" aria-expanded={!folded.has(heading)} onClick={() => fold(heading)}>
              <span className="sidebar-fold" aria-hidden="true">
                {folded.has(heading) ? "+" : "–"}
              </span>
              <span className="sidebar-heading-name">{heading}</span>
              <span className="sidebar-count">({entries.length})</span>
            </button>
          )}
          {!folded.has(heading) && actions[heading]}
          {!folded.has(heading) &&
            entries.map((entry) => (
              <SidebarRow key={entry.id} entry={entry} names={namesOf(entry, view.nameOrder)} both={view.bothNames} selected={entry.id === selected} menu={withMenu(entry)} onSelect={() => onSelect(entry)} />
            ))}
        </section>
      ))}
    </nav>
  );
}

/**
 * One app in the sidebar: its icon and name (the app's or its game's), dimmed
 * when it isn't installed, with what it's doing after it; the other name
 * under it when both show. With `menu`, a right-click or a controller's
 * Options button opens the app's menu, as on a library card.
 */
function SidebarRow({
  entry,
  names: [name, other],
  both,
  selected,
  menu,
  onSelect,
}: {
  entry: Entry;
  /** The name shown, and the other one (none when the app has no game, or one of its own name). */
  names: [string, string | undefined];
  both: boolean;
  selected: boolean;
  menu: boolean;
  onSelect: () => void;
}) {
  const { installs, jobs, catalog, play } = useLauncher();
  const install = installs[entry.id];
  const job = jobs[entry.id];
  const row = useRef<HTMLButtonElement>(null);
  // The app picked elsewhere (its page opened from a game's, say) scrolls into view.
  useLayoutEffect(() => {
    if (selected) row.current?.scrollIntoView?.({ block: "nearest" });
  }, [selected]);
  const percent = job && !("error" in job) && job.total ? Math.round((job.received / job.total) * 100) : undefined;
  const [state, note] =
    job && "error" in job
      ? ["error", t`Couldn't install`]
      : job
        ? ["busy", job.phase === "installing" ? t`Installing` : percent !== undefined ? t`Downloading ${percent}%` : t`Downloading`]
        : install && hasUpdate(catalog[entry.id] ?? entry, install)
          ? ["update", t`Update available`]
          : install
            ? ["ready", undefined]
            : ["missing", undefined];
  return (
    <>
      <button
        ref={row}
        type="button"
        className={`sidebar-row${selected ? " selected" : ""}`}
        aria-current={selected || undefined}
        data-slug={entry.slug}
        data-state={state}
        data-installed={Boolean(install)}
        data-lines={both && other ? 2 : 1}
        title={other ? `${name}\n${other}` : name}
        onClick={onSelect}
        // As in a file list: a double click plays an installed app.
        onDoubleClick={() => install && !job && void play(entry.id)}
      >
        <span className="sidebar-icon" aria-hidden="true">
          <CardArt entry={entry} image="icon" />
        </span>
        <span className="sidebar-names">
          <span className="sidebar-line">
            <span className="sidebar-name">{name}</span>
            {install?.updates === "pinned" && <Pin className="sidebar-pin" size={12} strokeWidth={2.5} aria-hidden="true" />}
            {note && <span className="sidebar-note"> – {note}</span>}
          </span>
          {both && other && <span className="sidebar-other">{other}</span>}
        </span>
      </button>
      {/* Its page is the one beside the sidebar: App page picks it. */}
      {menu && <GameMenu entry={entry} onOpen={onSelect} target={() => row.current} bare where="sidebar" />}
    </>
  );
}

/** What this computer knows of the player and an app, under its page's buttons: when they last played it, for how long, when they added it, and what's installed. */
export function PlayStats({ install, item, added = true }: { install?: Install; item?: LibraryItem; /** When it came to the library too. */ added?: boolean }) {
  const stats: [string, string][] = [
    ...(install
      ? ([[t`Last played`, install.lastPlayed ? dayLabel(install.lastPlayed) : t({ message: "Never", comment: "Last played: never" })]] as [string, string][])
      : []),
    // Counted from this launcher's Play button, since play time came in.
    ...(install?.playTime !== undefined ? ([[t`Play time`, playTimeLabel(install.playTime)]] as [string, string][]) : []),
    ...(added && item?.addedAt ? ([[t({ message: "Added", comment: "When the player added an app to their library, then a date" }), dayLabel(item.addedAt)]] as [string, string][]) : []),
    ...(install && !install.local ? ([[t({ message: "Installed", context: "release", comment: "The version installed, then a version" }), versionLabel(install.version)]] as [string, string][]) : []),
  ];
  if (!stats.length) return null;
  return (
    <dl className="play-stats" aria-label={t`Your play`}>
      {stats.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
