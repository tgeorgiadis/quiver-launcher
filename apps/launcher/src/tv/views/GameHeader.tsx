/** The focused game: its logo (or name) and chips for its console, version and state; and its art behind everything. */
import { useEffect, useState } from "react";
import { AlertTriangle, ArrowUpCircle, Clock, Download, Gamepad2 } from "lucide-react";
import type { TvApp } from "../bridge";
import { versionLabel } from "../../versions";
import { useFitted } from "../art/fitted";

const relative = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
/** "Played today", "Played 3 days ago", "Played last month". */
export function playedLabel(at: number, now = Date.now()) {
  const days = Math.round((at - now) / 86_400_000);
  if (days > -1) return "Played today";
  if (days > -30) return `Played ${relative.format(days, "day")}`;
  const months = Math.round(days / 30);
  if (months > -12) return `Played ${relative.format(months, "month")}`;
  return `Played ${relative.format(Math.round(days / 365), "year")}`;
}

export function GameLogo({ app, className = "" }: { app: TvApp; className?: string }) {
  const [failed, setFailed] = useState<string | null>(null);
  const logo = app.art.logo && failed !== app.art.logo ? app.art.logo : null;
  // An oversized logo is shown from its smaller copy (art/fitted.ts); nothing until its size is known.
  const src = useFitted(logo);
  if (logo && !src) return null;
  return logo && src ? (
    <img className={`tv-logo ${className}`} src={src} alt={app.title} onError={() => setFailed(logo)} />
  ) : (
    <h1 className={`tv-title ${className}`}>{app.title}</h1>
  );
}

export function GameChips({ app, consoleName }: { app: TvApp; consoleName: string }) {
  return (
    <div className="tv-chips">
      {consoleName && (
        <span className="tv-chip">
          <Gamepad2 aria-hidden="true" />
          {consoleName}
        </span>
      )}
      {app.state === "updateAvailable" && app.installedVersion && app.latestVersion ? (
        <span className="tv-chip accent">
          <ArrowUpCircle aria-hidden="true" />
          {versionLabel(app.installedVersion)} → {versionLabel(app.latestVersion)}
        </span>
      ) : app.installedVersion ? (
        <span className="tv-chip">{versionLabel(app.installedVersion)}</span>
      ) : app.latestVersion ? (
        <span className="tv-chip">{versionLabel(app.latestVersion)}</span>
      ) : null}
      {app.state === "notInstalled" && (
        <span className="tv-chip accent">
          <Download aria-hidden="true" />
          {app.inLibrary ? "In your library · not installed" : "Not in your library"}
        </span>
      )}
      {app.state === "error" && (
        <span className="tv-chip danger">
          <AlertTriangle aria-hidden="true" />
          {app.installedVersion ? "Didn't start" : "Download failed"}
        </span>
      )}
      {app.lastPlayed ? (
        <span className="tv-chip quiet">
          <Clock aria-hidden="true" />
          {playedLabel(app.lastPlayed)}
        </span>
      ) : null}
      {app.title !== app.gameTitle && <span className="tv-chip quiet">{app.gameTitle}</span>}
    </div>
  );
}

export function GameHeader({ app, consoleName }: { app: TvApp | null; consoleName: string }) {
  return (
    <header className="tv-header">
      {app && (
        <div className="tv-header-inner" key={app.id}>
          <GameLogo app={app} />
          <GameChips app={app} consoleName={consoleName} />
        </div>
      )}
    </header>
  );
}

/** The focused game's hero, blurred, crossfading (250 ms) from the last one. */
export function Backdrop({ app }: { app: TvApp | null }) {
  const url = app ? (app.art.hero ?? app.art.capsule ?? null) : null;
  const [layers, setLayers] = useState<string[]>([]);
  useEffect(() => {
    if (!url) return;
    setLayers((l) => (l.at(-1) === url ? l : [...l.slice(-1), url]));
  }, [url]);
  return (
    <div className="tv-backdrop" aria-hidden="true">
      {layers.map((src) => (
        <img key={src} src={src} alt="" className={src === url ? "shown" : ""} decoding="async" />
      ))}
    </div>
  );
}
