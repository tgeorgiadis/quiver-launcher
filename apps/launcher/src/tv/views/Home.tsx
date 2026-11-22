/**
 * Home: the focused game large across the top, and shelves below:
 * recently played, updates (with Update all), recently installed. Each shelf
 * remembers its column; the whole thing remembers its place across tabs.
 */
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { RefreshCw } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useTv, useInputHandler } from "../context";
import { SourcedImage, heroSources } from "@quiverlauncher/ui";
import type { TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import { ShelfNavigator } from "../nav/shelf";
import { glideIntoView } from "./glide";
import { GameTile } from "./GameTile";
import { GameChips, GameLogo } from "./GameHeader";
import { Glyph } from "./Glyph";
import { appAt, homeShelves, rowLength } from "./homeShelves";
import { tileHints, useTileActions } from "./tileActions";

type Props = {
  nav: ShelfNavigator;
  onFocus: (app: TvApp | null) => void;
  onGlobal: (action: InputAction) => boolean;
  onDetails: (app: TvApp) => void;
  onDownloads: () => void;
  consoleName: (app: TvApp) => string;
};

/** Sizes at 720p, times the scale. */
const TILE_W = 124;
const TILE_H = 186;
const GAP = 18;

function UpdateAllCard({ count, focused }: { count: number; focused: boolean }) {
  return (
    <div className={`tv-tile tv-update-all${focused ? " focused" : ""}`} aria-label={t`Update all`}>
      <div className="tv-tile-inner">
        <RefreshCw aria-hidden="true" />
        <strong>
          <Trans>Update all</Trans>
        </strong>
        <span>{plural(count, { one: "# game", other: "# games" })}</span>
        <span className="tv-update-all-glyph">
          <Glyph action="secondary" />
        </span>
      </div>
    </div>
  );
}

/**
 * Nothing installed yet. With a Quiver Launcher 3 library on this computer, Home
 * offers to bring it over (A), as desktop mode does: a first start straight into
 * TV mode shouldn't need desktop mode for that.
 */
function EmptyHome({ onGlobal }: { onGlobal: (action: InputAction) => boolean }) {
  const { bridge } = useTv();
  const [state, setState] = useState<"idle" | "busy" | string[]>("idle");
  const old = bridge.oldApps;
  const bring = () => {
    if (state === "busy" || !old.length) return;
    setState("busy");
    void bridge.importOld().then(setState, () => setState("idle"));
  };
  useInputHandler({
    onAction(action) {
      if (!onGlobal(action) && action === "accept") bring();
    },
    hints: () => (old.length && state === "idle" ? [{ action: "accept", label: t`Bring them over` }] : []),
  });
  const count = old.length;
  if (count && state !== "busy" && !Array.isArray(state))
    return (
      <div className="tv-empty tv-import">
        <h2>
          <Trans>Bring over your Quiver Launcher 3 library?</Trans>
        </h2>
        <p>
          {plural(count, {
            one: "It's on this computer with # game. Installed games stay where they are, so nothing downloads again.",
            other: "It's on this computer with # games. Installed games stay where they are, so nothing downloads again.",
          })}
        </p>
        <button type="button" className="tv-details-button focused" onClick={bring}>
          <Glyph action="accept" size={22} />
          <Trans>Bring them over</Trans>
        </button>
      </div>
    );
  const names = Array.isArray(state) ? state.join(", ") : "";
  return (
    <div className="tv-empty">
      <h2>{state === "busy" ? t`Bringing your games over…` : t`Your library is empty`}</h2>
      <p>
        {state === "busy"
          ? t`Matching them with the catalog.`
          : names
            ? t`Couldn't bring over: ${names}. Find them in Discover.`
            : t`Find something to play in Discover.`}
      </p>
    </div>
  );
}

/** The shelf row and column of the tile under the mouse, if any. */
function cellAt(target: EventTarget) {
  const cell = (target as Element).closest?.("[data-row][data-col]");
  return cell ? { row: Number(cell.getAttribute("data-row")), col: Number(cell.getAttribute("data-col")) } : null;
}

export function Home({ nav, onFocus, onGlobal, onDetails, onDownloads, consoleName }: Props) {
  const { bridge, settings, scale, pointer } = useTv();
  const actions = useTileActions(onDownloads);
  const shelves = useMemo(() => homeShelves(bridge.apps), [bridge.apps]);
  const lengths = shelves.map(rowLength);
  nav.reset(lengths);
  const [, setVersion] = useState(0);
  const shelf = shelves[nav.row];
  const app = shelf ? appAt(shelf, nav.col) : null;

  useEffect(() => onFocus(app), [app, onFocus]);
  const focusCell = (at: { row: number; col: number }) => {
    nav.reset(lengths, at);
    setVersion((v) => v + 1);
  };

  const list = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    if (list.current) glideIntoView(list.current, list.current.querySelector(`[data-row="${nav.row}"]`), settings.reduceMotion);
  }, [nav.row, settings.reduceMotion]);

  useInputHandler({
    onAction(action) {
      if (onGlobal(action)) return;
      if (action === "up" || action === "down" || action === "left" || action === "right") {
        if (nav.move(action).moved) setVersion((v) => v + 1);
      } else if (action === "accept") {
        if (shelf?.updateAll && !app) actions.updateAll(shelf.apps);
        else actions.primary(app);
      } else if (action === "secondary") {
        if (shelf?.updateAll && !app) actions.updateAll(shelf.apps);
        else actions.update(app);
      } else if (action === "details" && app) onDetails(app);
    },
    holdFor: (action) => (action === "secondary" ? actions.holdFor(app) : undefined),
    onHold: (action) => action === "secondary" && actions.cancel(app),
    hints: () => (shelf?.updateAll && !app ? [{ action: "secondary", label: t`Update all` }] : tileHints(app)),
    // With nothing to show, the empty state (below) takes the input: a child's handler is pushed
    // before its parent's, so this one would otherwise sit on top of it.
  }, undefined, shelves.length > 0);

  if (!shelves.length) return <EmptyHome onGlobal={onGlobal} />;

  const hero = app?.art.hero ?? app?.art.capsule;
  const step = (TILE_W + GAP) * scale;
  return (
    <div className="tv-home">
      <section className="tv-home-hero" aria-live="polite">
        {hero && <SourcedImage key={hero} className="tv-home-hero-art" sources={heroSources(hero)} alt="" priority onFail={() => {}} />}
        <div className="tv-home-info" key={app?.id ?? "all"}>
          {app ? (
            <>
              <GameLogo app={app} className="large" />
              <GameChips app={app} consoleName={consoleName(app)} />
            </>
          ) : (
            <>
              <h1 className="tv-title large">
                <Trans>Updates waiting</Trans>
              </h1>
              <p className="tv-lead">
                <Trans>Update every game with one press, then play the newest versions.</Trans>
              </p>
            </>
          )}
        </div>
      </section>
      <div
        className="tv-shelves"
        ref={list}
        // The mouse: pointing at a tile focuses it, a click does what A does (X on Update all), a right click opens details.
        onMouseMove={(e) => {
          const at = cellAt(e.target);
          if (pointer && at && (at.row !== nav.row || at.col !== nav.col)) focusCell(at);
        }}
        onClick={(e) => {
          const at = cellAt(e.target);
          if (!at) return;
          focusCell(at);
          const s = shelves[at.row];
          const a = appAt(s, at.col);
          if (s.updateAll && !a) actions.updateAll(s.apps);
          else actions.primary(a);
        }}
        onContextMenu={(e) => {
          e.preventDefault();
          const at = cellAt(e.target);
          const a = at && appAt(shelves[at.row], at.col);
          if (a) onDetails(a);
        }}
      >
        {shelves.map((s, row) => {
          const length = rowLength(s);
          const col = row === nav.row ? nav.col : (nav.memory[row] ?? 0);
          // Keep the focused tile a tile away from the right edge while there's more to show.
          const visible = Math.max(1, Math.floor((window.innerWidth - 96 * scale) / step));
          const offset = Math.max(0, Math.min(col - visible + 2, length - visible)) * step;
          return (
            <section key={s.id} className={`tv-shelf${row === nav.row ? " active" : ""}`} data-row={row}>
              <h3>
                {s.title}
                <span className="tv-shelf-count">{s.apps.length}</span>
              </h3>
              <div className="tv-shelf-row" style={{ height: TILE_H * scale }}>
                <div className="tv-shelf-track" style={{ transform: `translateX(${-offset}px)` }}>
                  {Array.from({ length }, (_, c) => {
                    const a = appAt(s, c);
                    const focused = row === nav.row && c === nav.col;
                    const x = c * step;
                    return a ? (
                      <div key={a.id} data-row={row} data-col={c} className="tv-tile-slot" style={{ left: x, width: TILE_W * scale, height: TILE_H * scale }}>
                        <GameTile app={a} focused={focused} x={0} y={0} w={TILE_W * scale} h={TILE_H * scale} />
                      </div>
                    ) : (
                      <div key="all" data-row={row} data-col={c} className="tv-tile-slot" style={{ left: x, width: TILE_W * scale, height: TILE_H * scale }}>
                        <UpdateAllCard count={s.apps.length} focused={focused} />
                      </div>
                    );
                  })}
                </div>
              </div>
            </section>
          );
        })}
      </div>
    </div>
  );
}
