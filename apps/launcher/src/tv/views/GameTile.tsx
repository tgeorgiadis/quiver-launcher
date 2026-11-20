/**
 * A 2:3 tile: the art (or a stand-in), the title over a scrim when
 * focused or when there's no art, a state badge, and a progress bar. Focus
 * comes from the view (`focused`), not the browser.
 */
import { memo, useState } from "react";
import { AlertTriangle, ArrowUp, CheckCircle2, Download, Loader2 } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { TvApp } from "../bridge";
import { useIconColors } from "../art/useIconColors";

/** A steady hue per app, for tiles without art. */
export function hueOf(id: string) {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
  return h % 360;
}

/** A tile for an app without a 2:3 picture: its icon on a gradient of the icon's own colours, the title below. */
export function FallbackArt({ app }: { app: TvApp }) {
  const [iconFailed, setIconFailed] = useState(false);
  const icon = app.art.icon && !iconFailed ? app.art.icon : undefined;
  const colors = useIconColors(icon);
  const hue = hueOf(app.id);
  const [top, bottom] = colors ?? [`hsl(${hue} 45% 32%)`, `hsl(${(hue + 40) % 360} 50% 12%)`];
  return (
    <div className="tv-fallback" style={{ background: `radial-gradient(circle at 50% 35%, ${top} 0%, transparent 70%), linear-gradient(180deg, ${top}, ${bottom})` }}>
      {icon ? <img src={icon} alt="" onError={() => setIconFailed(true)} /> : <span className="tv-fallback-letter">{app.title.charAt(0)}</span>}
    </div>
  );
}

function Badge({ app }: { app: TvApp }) {
  switch (app.state) {
    case "notInstalled":
      return <span className="tv-badge" title={app.inLibrary ? t`In your library, not installed` : t`Not in your library`}><Download /></span>;
    case "updateAvailable":
      return <span className="tv-badge update" title={t`Update available`}><ArrowUp /></span>;
    case "error":
      return <span className="tv-badge error" title={app.error}><AlertTriangle /></span>;
    case "queued":
    case "downloading":
    case "updating":
      return <span className="tv-badge busy" title={app.state === "queued" ? t`Waiting to download` : t`Downloading`}><Loader2 /></span>;
    default:
      return null;
  }
}

/** Placed by the grid: numbers, not a style object, so unchanged tiles skip rendering. */
export const GameTile = memo(function GameTile({ app, focused, x, y, w, h, index }: { app: TvApp; focused: boolean; x: number; y: number; w: number; h: number; index?: number }) {
  const [failed, setFailed] = useState<string | null>(null);
  const art = app.art.capsule && failed !== app.art.capsule ? app.art.capsule : null;
  const busy = app.state === "downloading" || app.state === "updating";
  return (
    <div
      className={`tv-tile${focused ? " focused" : ""}${art ? "" : " no-art"}${app.fresh ? " fresh" : ""}`}
      aria-label={app.title}
      aria-current={focused || undefined}
      data-index={index}
      style={{ left: x, top: y, width: w, height: h }}
    >
      <div className="tv-tile-inner">
        {art ? <img className="tv-tile-art" src={art} alt="" loading="lazy" decoding="async" draggable={false} onError={() => setFailed(art)} /> : <FallbackArt app={app} />}
        {(focused || !art) && (
          <div className="tv-tile-scrim">
            <span>{app.title}</span>
          </div>
        )}
        {app.fresh ? (
          <div className="tv-fresh">
            <CheckCircle2 aria-hidden="true" />
            <span>
              <Trans comment="On a game's tile in Discover: the game just got has finished installing">Installed</Trans>
            </span>
          </div>
        ) : (
          <Badge app={app} />
        )}
        {busy && (
          <div className={`tv-progress${app.progress === null ? " indeterminate" : ""}`}>
            <i style={{ width: `${(app.progress ?? 0.3) * 100}%` }} />
          </div>
        )}
      </div>
    </div>
  );
});
