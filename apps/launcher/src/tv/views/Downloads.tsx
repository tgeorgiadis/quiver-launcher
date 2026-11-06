/**
 * Downloads (spec §8): Update all (when there are updates), then what's
 * downloading, waiting, failed, and finished this session. A plays a finished
 * game or retries a failed one; X updates all, or (held) takes a waiting
 * download out of the queue. One already downloading can't be stopped (as upstream).
 */
import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, CheckCircle2, Clock, Download, RefreshCw } from "lucide-react";
import { useTv, useInputHandler } from "../context";
import type { TvApp } from "../bridge";
import type { InputAction } from "../input/actions";
import type { ButtonHint } from "../input/router";
import { versionLabel } from "../../versions";
import { CANCEL_HOLD_MS, useTileActions, type Back } from "./tileActions";
import { downloadRows, type DownloadRow } from "./lists";

/** "Finished this session" counts from when the launcher started. */
const SESSION_START = Date.now();

type Props = {
  onFocus: (app: TvApp | null) => void;
  onGlobal: (action: InputAction) => boolean;
  back?: Back;
  onDetails: (app: TvApp) => void;
};

function statusOf(row: Extract<DownloadRow, { kind: "app" }>) {
  const { app, status } = row;
  if (status === "active")
    return app.phase === "installing" ? "Installing…" : app.progress !== null ? `Downloading · ${Math.round(app.progress * 100)}%` : "Downloading…";
  if (status === "queued") return "Waiting for the download ahead";
  if (status === "failed") return app.error ?? "Failed";
  return `Installed ${versionLabel(app.installedVersion)}`;
}

function hintsFor(row: DownloadRow | undefined, back?: Back): ButtonHint[] {
  const hints: ButtonHint[] = [];
  if (row?.kind === "updateAll") hints.push({ action: "secondary", label: "Update all" });
  else if (row?.status === "done") hints.push({ action: "accept", label: "Play" });
  else if (row?.status === "failed") hints.push({ action: "accept", label: row.app.installedVersion ? "Play" : "Retry" });
  else if (row?.status === "queued") hints.push({ action: "secondary", label: "Cancel", hold: true });
  if (row?.kind === "app") hints.push({ action: "details", label: "Details" });
  if (back) hints.push({ action: "back", label: back.label });
  return hints;
}

export function Downloads({ onFocus, onGlobal, back, onDetails }: Props) {
  const { bridge } = useTv();
  const actions = useTileActions(() => {});
  const rows = useMemo(() => downloadRows(bridge.apps, SESSION_START), [bridge.apps]);
  // Focus follows the row's app, not its place: rows move as downloads finish.
  const [focusKey, setFocusKey] = useState<string | null>(null);
  const keyOf = (r: DownloadRow) => (r.kind === "updateAll" ? "updateAll" : r.app.id);
  const found = rows.findIndex((r) => keyOf(r) === focusKey);
  const index = found >= 0 ? found : 0;
  const row = rows[index];
  const app = row?.kind === "app" ? row.app : null;

  useEffect(() => onFocus(app), [app, onFocus]);
  const list = useRef<HTMLDivElement>(null);
  useEffect(() => {
    list.current?.querySelector(".tv-dl-row.focused")?.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }, [index]);

  const move = (to: number) => rows[to] && setFocusKey(keyOf(rows[to]));
  const act = (r: DownloadRow | undefined, action: InputAction) => {
    if (!r) return;
    if (r.kind === "updateAll") {
      if (action === "secondary" || action === "accept") actions.updateAll(r.apps);
    } else if (action === "accept" && (r.status === "done" || r.status === "failed")) actions.primary(r.app);
    else if (action === "details") onDetails(r.app);
  };

  useInputHandler({
    onAction(action) {
      if (onGlobal(action)) return;
      if (action === "up") move(index - 1);
      else if (action === "down") move(index + 1);
      else if (action === "back") back?.go();
      else act(row, action);
    },
    holdFor: (action) => (action === "secondary" && row?.kind === "app" && row.status === "queued" ? CANCEL_HOLD_MS : undefined),
    onHold: (action) => action === "secondary" && actions.cancel(app),
    hints: () => hintsFor(row, back),
  });

  if (!rows.length) {
    return (
      <div className="tv-empty">
        <h2>Nothing downloading</h2>
        <p>Games you get in Discover, and updates, download here.</p>
      </div>
    );
  }

  return (
    <div className="tv-downloads" ref={list}>
      {rows.map((r, i) => {
        const focused = i === index;
        const click = () => {
          move(i);
          act(r, r.kind === "updateAll" ? "secondary" : "accept");
        };
        if (r.kind === "updateAll")
          return (
            <div key="updateAll" className={`tv-dl-row update-all${focused ? " focused" : ""}`} onMouseMove={() => move(i)} onClick={click}>
              <span className="tv-dl-icon">
                <RefreshCw aria-hidden="true" />
              </span>
              <span className="tv-dl-text">
                <strong>Update all</strong>
                <span>
                  {r.apps.length} {r.apps.length === 1 ? "game has an update" : "games have updates"}
                </span>
              </span>
            </div>
          );
        const Icon = r.status === "done" ? CheckCircle2 : r.status === "failed" ? AlertTriangle : r.status === "queued" ? Clock : Download;
        return (
          <div key={r.app.id} className={`tv-dl-row ${r.status}${focused ? " focused" : ""}`} onMouseMove={() => move(i)} onClick={click}>
            {r.app.art.capsule ? <img className="tv-dl-art" src={r.app.art.capsule} alt="" /> : <span className="tv-dl-art" />}
            <span className="tv-dl-text">
              <strong>{r.app.title}</strong>
              <span className="tv-dl-status">
                <Icon aria-hidden="true" />
                {statusOf(r)}
              </span>
              {r.status === "active" && (
                <span className={`tv-dl-progress${r.app.progress === null ? " indeterminate" : ""}`}>
                  <i style={{ width: `${(r.app.progress ?? 0.3) * 100}%` }} />
                </span>
              )}
            </span>
            {r.app.latestVersion && <span className="tv-dl-version">{versionLabel(r.app.latestVersion)}</span>}
          </div>
        );
      })}
    </div>
  );
}
