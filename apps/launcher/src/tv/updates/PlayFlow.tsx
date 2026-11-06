/**
 * Play, through the update policy (spec §9): straight in, or the update prompt
 * (Update & play, Play without updating, Skip this version, Cancel), or updating
 * first with its progress (B stops the game starting; the download carries on,
 * as upstream can't stop one), and if the update fails, playing the version
 * already installed.
 */
import { useRef, useState, type ReactNode } from "react";
import { AlertTriangle, ArrowUpCircle } from "lucide-react";
import { useTv, useInputHandler } from "../context";
import type { TvApp } from "../bridge";
import type { Bridge } from "../useBridge";
import type { TvSidecar, SidecarStore } from "../data/sidecar";
import type { InputAction } from "../input/actions";
import { LAYER, type ButtonHint } from "../input/router";
import { versionLabel } from "../../versions";
import { Glyph } from "../views/Glyph";
import { GameLogo } from "../views/GameHeader";
import { decide, noteLines } from "./policy";

type Flow =
  | { kind: "ask"; app: TvApp; notes: string[] }
  | { kind: "updating"; id: string; token: number }
  | { kind: "failed"; app: TvApp; error: string }
  | null;

const bare = (version = "") => version.trim().replace(/^v/i, "");

export function usePlayFlow({ bridge, sidecar, data, launch }: { bridge: Bridge; sidecar: SidecarStore; data: TvSidecar; launch: (id: string) => void }) {
  const [flow, setFlowState] = useState<Flow>(null);
  // Async steps check the flow is still theirs: the player may have backed out meanwhile.
  const current = useRef<Flow>(null);
  const setFlow = (next: Flow) => setFlowState((current.current = next));
  const tokens = useRef(0);
  const latest = useRef({ bridge, data });
  latest.current = { bridge, data };

  async function updateThenLaunch(app: TvApp) {
    const token = ++tokens.current;
    setFlow({ kind: "updating", id: app.id, token });
    const error = await latest.current.bridge.updateAndWait(app.id);
    const now = current.current;
    if (now?.kind !== "updating" || now.token !== token) return;
    if (error) setFlow({ kind: "failed", app, error });
    else {
      setFlow(null);
      launch(app.id);
    }
  }

  function play(id: string) {
    const { bridge, data } = latest.current;
    const app = bridge.apps.find((a) => a.id === id);
    if (!app) return;
    const decision = decide(app, data.settings.updateBehavior, data.apps[id]?.skippedVersion);
    if (decision === "launch") return launch(id);
    if (decision === "update") return void updateThenLaunch(app);
    setFlow({ kind: "ask", app, notes: [] });
    // The new release's notes, when the catalog has them (apps the player added have none here).
    if (app.slug)
      void bridge
        .releases(app.slug)
        .then((page) => {
          const release = page.items.find((r) => bare(r.version) === bare(app.latestVersion)) ?? page.items[0];
          const now = current.current;
          if (now?.kind === "ask" && now.app.id === id) setFlow({ ...now, notes: noteLines(release?.notes) });
        })
        .catch(() => {});
  }

  const ui =
    flow?.kind === "ask" ? (
      <UpdatePrompt
        app={flow.app}
        notes={flow.notes}
        onChoice={(choice) => {
          setFlow(null);
          if (choice === "update") void updateThenLaunch(flow.app);
          else if (choice === "play") launch(flow.app.id);
          else if (choice === "skip") {
            sidecar.updateApp(flow.app.id, { skippedVersion: flow.app.latestVersion });
            launch(flow.app.id);
          }
        }}
      />
    ) : flow?.kind === "updating" ? (
      <Updating id={flow.id} onBack={() => setFlow(null)} />
    ) : flow?.kind === "failed" ? (
      <UpdateFailed
        app={flow.app}
        error={flow.error}
        onChoice={(playOld) => {
          setFlow(null);
          if (playOld) launch(flow.app.id);
        }}
      />
    ) : null;

  return { play, ui };
}

/** A dialog's buttons, also in the hint bar; clickable. */
function DialogButtons({ hints, onPress }: { hints: ButtonHint[]; onPress: (action: InputAction) => void }) {
  return (
    <div className="tv-dialog-buttons">
      {hints.map((h) => (
        <button key={h.action} type="button" className={h.action === "accept" ? "primary" : ""} onClick={() => onPress(h.action)}>
          <Glyph action={h.action} size={22} />
          {h.label}
        </button>
      ))}
    </div>
  );
}

function Dialog({ app, children }: { app: TvApp; children: ReactNode }) {
  const banner = app.art.hero ?? app.art.capsule;
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-modal="true" aria-label={app.title}>
      <section className="tv-dialog">
        <div className="tv-dialog-banner">
          {banner && <img src={banner} alt="" />}
          <GameLogo app={app} />
        </div>
        <div className="tv-dialog-body">{children}</div>
      </section>
    </div>
  );
}

type Choice = "update" | "play" | "skip" | "cancel";

function UpdatePrompt({ app, notes, onChoice }: { app: TvApp; notes: string[]; onChoice: (choice: Choice) => void }) {
  const hints: ButtonHint[] = [
    { action: "accept", label: "Update & play" },
    { action: "secondary", label: "Play without updating" },
    { action: "details", label: "Skip this version" },
    { action: "back", label: "Cancel" },
  ];
  const press = (action: InputAction) => {
    const choice = ({ accept: "update", secondary: "play", details: "skip", back: "cancel" } as Partial<Record<InputAction, Choice>>)[action];
    if (choice) onChoice(choice);
  };
  useInputHandler({ onAction: press, hints: () => hints }, LAYER.dialog);
  return (
    <Dialog app={app}>
      <h2>
        <ArrowUpCircle aria-hidden="true" /> Update available
      </h2>
      <p className="tv-dialog-delta">
        {versionLabel(app.installedVersion)} <span>→</span> {versionLabel(app.latestVersion)}
      </p>
      {notes.length > 0 && (
        <ul className="tv-dialog-notes">
          {notes.map((line, i) => (
            <li key={i}>{line}</li>
          ))}
        </ul>
      )}
      <DialogButtons hints={hints} onPress={press} />
    </Dialog>
  );
}

function Updating({ id, onBack }: { id: string; onBack: () => void }) {
  const { bridge } = useTv();
  const app = bridge.apps.find((a) => a.id === id);
  const hints: ButtonHint[] = [{ action: "back", label: "Don't start it" }];
  useInputHandler({ onAction: (a) => a === "back" && onBack(), hints: () => hints }, LAYER.dialog);
  if (!app) return null;
  const waiting = app.state === "queued";
  return (
    <Dialog app={app}>
      <h2>{waiting ? "Waiting to update…" : `Updating to ${versionLabel(app.latestVersion)}…`}</h2>
      <p className="tv-dialog-lead">It starts as soon as the update is installed.</p>
      <div className={`tv-dialog-progress${app.progress === null ? " indeterminate" : ""}`}>
        <i style={{ width: `${(app.progress ?? 0.3) * 100}%` }} />
      </div>
      <p className="tv-dialog-lead small">{app.phase === "installing" ? "Installing" : waiting ? "Another download is ahead of it" : app.progress !== null ? `${Math.round(app.progress * 100)}%` : "Downloading"}</p>
      <DialogButtons hints={hints} onPress={onBack} />
    </Dialog>
  );
}

function UpdateFailed({ app, error, onChoice }: { app: TvApp; error: string; onChoice: (playOld: boolean) => void }) {
  const hints: ButtonHint[] = [
    { action: "accept", label: `Play ${versionLabel(app.installedVersion)}` },
    { action: "back", label: "Back" },
  ];
  const press = (action: InputAction) => (action === "accept" ? onChoice(true) : action === "back" ? onChoice(false) : undefined);
  useInputHandler({ onAction: press, hints: () => hints }, LAYER.dialog);
  return (
    <Dialog app={app}>
      <h2 className="danger">
        <AlertTriangle aria-hidden="true" /> Update failed
      </h2>
      <p className="tv-dialog-lead">{error}</p>
      <DialogButtons hints={hints} onPress={press} />
    </Dialog>
  );
}
