/** The focused game: its name and chips for its console, version and state on one line; and its art behind everything. */
import { useEffect, useState } from "react";
import { AlertTriangle, ArrowUpCircle, Clock, Download, Gamepad2 } from "lucide-react";
import { t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import { SourcedImage, blurSource, cardSources } from "@quiverlauncher/ui";
import type { TvApp } from "../bridge";
import { versionLabel } from "../../versions";

/** When a game was played last, in the language shown: "today", "3 days ago", "last month". */
export function playedWhen(at: number, now = Date.now()) {
  const relative = new Intl.RelativeTimeFormat(i18n.locale, { numeric: "auto" });
  const days = Math.round((at - now) / 86_400_000);
  if (days > -1) return relative.format(0, "day");
  if (days > -30) return relative.format(days, "day");
  const months = Math.round(days / 30);
  if (months > -12) return relative.format(months, "month");
  return relative.format(Math.round(days / 365), "year");
}

/** "Played today", "Played 3 days ago", "Played last month". */
export function playedLabel(at: number, now = Date.now()) {
  const when = playedWhen(at, now);
  return t({ message: `Played ${when}`, comment: "When a game was played last; {when} is such as today, 3 days ago or last month" });
}

export function GameLogo({ app, className = "" }: { app: TvApp; className?: string }) {
  const [failed, setFailed] = useState<string | null>(null);
  const logo = app.art.logo && failed !== app.art.logo ? app.art.logo : null;
  // A copy no wider than it shows (the site's resizer): a logo can be 18106 × 4509, which lagged drawn from full size.
  return logo ? (
    <SourcedImage key={logo} className={`tv-logo ${className}`} sources={cardSources(logo, "40vw")} alt={app.title} priority onFail={() => setFailed(logo)} />
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
          {app.inLibrary ? t`In your library · not installed` : t`Not in your library`}
        </span>
      )}
      {app.state === "error" && (
        <span className="tv-chip danger">
          <AlertTriangle aria-hidden="true" />
          {!app.installedVersion ? t`Download failed` : app.downloadFailed ? t({ message: "Update failed", comment: "A game's update didn't install; the version it had is still there" }) : t({ message: "Didn't start", comment: "A game's launch failed" })}
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
        <div className="tv-header-inner">
          <h1 className="tv-header-title">{app.title}</h1>
          <GameChips app={app} consoleName={consoleName} />
        </div>
      )}
    </header>
  );
}

/** How long the focus rests on a game before the backdrop takes its colours: moving across a row changes nothing until it stops. */
const SETTLE_MS = 350;

/** The focused game's hero, blurred and dim, crossfading slowly from the last one once the focus has settled. */
export function Backdrop({ app }: { app: TvApp | null }) {
  const url = app ? (app.art.hero ?? app.art.capsule ?? null) : null;
  const [layers, setLayers] = useState<string[]>([]);
  const [shown, setShown] = useState<string | null>(null);
  useEffect(() => {
    if (!url) return;
    const show = () => {
      setLayers((l) => (l.at(-1) === url ? l : [...l.slice(-1), url]));
      setShown(url);
    };
    // The first one has nothing to switch from.
    if (shown === null) return show();
    const timer = setTimeout(show, SETTLE_MS);
    return () => clearTimeout(timer);
  }, [url, shown === null]);
  // A small copy: no more detail than the blur leaves.
  return (
    <div className="tv-backdrop" aria-hidden="true">
      {layers.map((src) => (
        <img key={src} src={blurSource(src)} alt="" className={src === shown ? "shown" : ""} decoding="async" />
      ))}
    </div>
  );
}
