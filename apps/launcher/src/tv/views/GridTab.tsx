/**
 * All and console tabs: a grid of 2:3 tiles. Only the rows near the
 * screen are drawn, so a library of hundreds scrolls smoothly. Focus is the
 * view's own (not the browser's), and the focused tile is scrolled to stay a
 * row away from the edge.
 */
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from "react";
import { t } from "@lingui/core/macro";
import { useTv, useInputHandler } from "../context";
import { glideTarget, glideTo } from "./glide";
import type { TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import type { ButtonHint } from "../input/router";
import { GridNavigator } from "../nav/grid";
import { GameTile } from "./GameTile";
import { tileHints, useTileActions, type Back } from "./tileActions";

export type { Back };

type Props = {
  tabId: string;
  apps: TvApp[];
  memory: Map<string, string>;
  onFocus: (app: TvApp | null) => void;
  onGlobal: (action: InputAction) => boolean;
  back?: Back;
  onDetails: (app: TvApp) => void;
  onDownloads: () => void;
  /** False while something above the grid has the input (Discover's filter row). */
  active?: boolean;
  /** Pressing past the grid's edge (Up from the top row goes to Discover's filters). */
  onEdge?: (action: InputAction) => void;
  /** Focus is within two rows of the end: time to load more. */
  onNearEnd?: () => void;
  /** A game was got from this grid (Discover). */
  onGet?: (app: TvApp) => void;
  /** The mouse went to a tile while something above had the input: the grid takes it. */
  onPointerFocus?: () => void;
  empty?: { title: string; text: string };
  /** What else the buttons do here, after a tile's own (Discover's shoulder buttons). */
  moreHints?: ButtonHint[];
};

/** Sizes at 720p, times the scale. */
export function gridMetrics(scale: number, width: number) {
  const tileW = 150 * scale;
  const tileH = 225 * scale;
  const gap = 22 * scale;
  const padX = 48 * scale;
  const padY = 22 * scale;
  const columns = Math.max(1, Math.floor((width - 2 * padX + gap) / (tileW + gap)));
  return { tileW, tileH, gap, padX, padY, columns, rowH: tileH + gap };
}

export function useElementSize(ref: RefObject<HTMLElement | null>) {
  const [size, setSize] = useState({ width: 0, height: 0 });
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const measure = () => setSize({ width: el.clientWidth, height: el.clientHeight });
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(el);
    return () => observer.disconnect();
  }, [ref]);
  return size;
}

/** The grid index of the tile under the mouse, if any. */
const tileIndex = (target: EventTarget) => {
  const index = (target as Element).closest?.("[data-index]")?.getAttribute("data-index");
  return index == null ? null : Number(index);
};

export function GridTab({ tabId, apps, memory, onFocus, onGlobal, back, onDetails, onDownloads, active = true, onEdge, onNearEnd, onGet, onPointerFocus, empty, moreHints = [] }: Props) {
  const { scale, settings, pointer } = useTv();
  const actions = useTileActions(onDownloads, onGet);
  const scroller = useRef<HTMLDivElement>(null);
  const { width, height } = useElementSize(scroller);
  const m = gridMetrics(scale, width);

  // Focus follows the app, not the position: an install or a rename reorders the grid under it.
  const [focus, setFocus] = useState(() => {
    const id = memory.get(tabId);
    return { id, index: Math.max(0, apps.findIndex((a) => a.id === id)) };
  });
  const found = focus.id ? apps.findIndex((a) => a.id === focus.id) : -1;
  const index = found >= 0 ? found : Math.min(focus.index, Math.max(0, apps.length - 1));
  const app = apps[index];

  const focusAt = (i: number) => {
    const next = apps[i];
    if (!next) return;
    setFocus({ id: next.id, index: i });
    memory.set(tabId, next.id);
  };

  useEffect(() => onFocus(app ?? null), [app, onFocus]);

  // Keep the focused row a row away from the top and bottom where there's room for it.
  const [scrollTop, setScrollTop] = useState(0);
  const row = Math.floor(index / m.columns);
  useLayoutEffect(() => {
    const el = scroller.current;
    if (!el || !height) return;
    const top = m.padY + row * m.rowH;
    const minTop = top + m.tileH + m.rowH - height; // a row below shows
    const maxTop = top - m.rowH; // a row above shows
    // From where it's heading, not where it is: it may still be on its way to the last row.
    let target = glideTarget(el);
    if (minTop > maxTop) target = top - (height - m.tileH) / 2;
    else if (target > maxTop) target = maxTop;
    else if (target < minTop) target = minTop;
    target = Math.max(0, target);
    glideTo(el, target, settings.reduceMotion);
  }, [row, height, m.rowH, m.tileH, m.padY, settings.reduceMotion]);

  const rows = Math.ceil(apps.length / m.columns);
  const firstRow = Math.max(0, Math.floor(scrollTop / m.rowH) - 2);
  const lastRow = Math.min(rows - 1, Math.ceil((scrollTop + height) / m.rowH) + 2);

  useInputHandler({
    onAction(action) {
      if (onGlobal(action)) return;
      const nav = new GridNavigator().reset(apps.length, m.columns, index);
      switch (action) {
        case "up":
        case "down":
        case "left":
        case "right": {
          const moved = nav.move(action);
          if (moved.moved) focusAt(moved.index);
          else onEdge?.(action);
          return;
        }
        case "accept":
          actions.primary(app);
          return;
        case "secondary":
          actions.update(app);
          return;
        case "details":
          if (app) onDetails(app);
          return;
        case "back":
          back?.go();
          return;
      }
    },
    holdFor: (action) => (action === "secondary" ? actions.holdFor(app) : undefined),
    onHold: (action) => action === "secondary" && actions.cancel(app),
    hints: () => [...tileHints(app, back), ...moreHints],
  }, undefined, active);

  // More to load when focus nears the end.
  useEffect(() => {
    if (onNearEnd && apps.length && Math.floor(index / m.columns) >= Math.floor((apps.length - 1) / m.columns) - 2) onNearEnd();
  }, [index, apps.length, m.columns, onNearEnd]);

  const tiles: ReactNode[] = [];
  for (let r = firstRow; r <= lastRow; r++) {
    for (let c = 0; c < m.columns; c++) {
      const i = r * m.columns + c;
      const a = apps[i];
      if (!a) break;
      tiles.push(
        <GameTile
          key={a.id}
          app={a}
          focused={active && i === index}
          x={m.padX + c * (m.tileW + m.gap)}
          y={m.padY + r * m.rowH}
          w={m.tileW}
          h={m.tileH}
          index={i}
        />,
      );
    }
  }

  return (
    <div
      className="tv-grid-scroll"
      ref={scroller}
      onScroll={(e) => setScrollTop(e.currentTarget.scrollTop)}
      // The mouse: pointing at a tile focuses it, a click does what A does, a right click opens its details.
      onMouseMove={(e) => {
        const i = tileIndex(e.target);
        if (pointer && i !== null && (i !== index || !active)) {
          focusAt(i);
          if (!active) onPointerFocus?.();
        }
      }}
      onClick={(e) => {
        const i = tileIndex(e.target);
        if (i === null) return;
        focusAt(i);
        if (!active) onPointerFocus?.();
        actions.primary(apps[i]);
      }}
      onContextMenu={(e) => {
        e.preventDefault();
        const i = tileIndex(e.target);
        if (i !== null) onDetails(apps[i]);
      }}
    >
      {apps.length ? (
        <div className="tv-grid" style={{ height: m.padY * 2 + rows * m.rowH }}>
          {width > 0 && tiles}
        </div>
      ) : (
        <div className="tv-empty">
          <h2>{empty?.title ?? t`No games here yet`}</h2>
          <p>{empty?.text ?? t`Find something to play in Discover.`}</p>
        </div>
      )}
    </div>
  );
}
