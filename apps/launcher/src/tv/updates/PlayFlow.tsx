/**
 * Play, through the update policy: straight in, or the update prompt
 * (Update & play, Play without updating, Skip this version, Cancel), or updating
 * first with its progress (B stops the game starting; the download carries on,
 * as upstream can't stop one), and if the update fails, playing the version
 * already installed.
 */
import { useRef, useState, type ReactNode } from "react";
import { AlertTriangle, ArrowUpCircle } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { SourcedImage, heroSources } from "@quiverlauncher/ui";
import { useTv, useInputHandler } from "../context";
import type { TvApp } from "../bridge";
import type { Bridge } from "../useBridge";
import type { TvSettings } from "../settings";
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

export function usePlayFlow({ bridge, settings, launch, decline }: { bridge: Bridge; settings: TvSettings; launch: (id: string) => void; decline: (id: string, version: string) => void }) {
  const [flow, setFlowState] = useState<Flow>(null);
  // Async steps check the flow is still theirs: the player may have backed out meanwhile.
  const current = useRef<Flow>(null);
  const setFlow = (next: Flow) => setFlowState((current.current = next));
  const tokens = useRef(0);
  const latest = useRef({ bridge, settings });
  latest.current = { bridge, settings };

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
    const { bridge, settings } = latest.current;
    const app = bridge.apps.find((a) => a.id === id);
    if (!app) return;
    const decision = decide(app, settings.updateBehavior);
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
          else if (choice === "updateOnly") void latest.current.bridge.update(flow.app.id);
          else if (choice === "play") launch(flow.app.id);
          else if (choice === "skip") {
            // The launcher's own skip: no update shown for it anywhere until a newer one comes.
            if (flow.app.latestVersion) decline(flow.app.id, flow.app.latestVersion);
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
        <button key={h.action} type="button" className={h.action === "accept" ? "tv-primary" : ""} onClick={() => onPress(h.action)}>
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
          {banner && <SourcedImage key={banner} sources={heroSources(banner)} alt="" priority onFail={() => {}} />}
          <GameLogo app={app} />
        </div>
        <div className="tv-dialog-body">{children}</div>
      </section>
    </div>
  );
}

type Choice = "update" | "updateOnly" | "play" | "skip" | "cancel";

function UpdatePrompt({ app, notes, onChoice }: { app: TvApp; notes: string[]; onChoice: (choice: Choice) => void }) {
  const hints: ButtonHint[] = [
    { action: "accept", label: t`Update & play` },
    // As X on its tile: the update alone, to play later.
    { action: "secondary", label: t({ message: "Update", comment: "Button: update the one app with an update" }) },
    { action: "details", label: t`Play without updating` },
    { action: "menu", label: t`Skip this version` },
    { action: "back", label: t`Cancel` },
  ];
  const press = (action: InputAction) => {
    const choice = ({ accept: "update", secondary: "updateOnly", details: "play", menu: "skip", back: "cancel" } as Partial<Record<InputAction, Choice>>)[action];
    if (choice) onChoice(choice);
  };
  useInputHandler({ onAction: press, hints: () => hints }, LAYER.dialog);
  return (
    <Dialog app={app}>
      <h2>
        <ArrowUpCircle aria-hidden="true" /> <Trans>Update available</Trans>
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
  const hints: ButtonHint[] = [{ action: "back", label: t({ message: "Don't start it", comment: "Controller hint while a game updates before it starts: it won't start once updated; the update carries on" }) }];
  useInputHandler({ onAction: (a) => a === "back" && onBack(), hints: () => hints }, LAYER.dialog);
  if (!app) return null;
  const waiting = app.state === "queued";
  const version = versionLabel(app.latestVersion);
  const percent = app.progress !== null ? Math.round(app.progress * 100) : null;
  return (
    <Dialog app={app}>
      <h2>{waiting ? t`Waiting to update…` : t`Updating to ${version}…`}</h2>
      <p className="tv-dialog-lead">
        <Trans>It starts as soon as the update is installed.</Trans>
      </p>
      <div className={`tv-dialog-progress${app.progress === null ? " indeterminate" : ""}`}>
        <i style={{ width: `${(app.progress ?? 0.3) * 100}%` }} />
      </div>
      <p className="tv-dialog-lead small">
        {app.phase === "installing"
          ? t`Installing`
          : waiting
            ? t`Another download is ahead of it`
            : percent !== null
              ? t({ message: `${percent}%`, comment: "How much of an update has downloaded" })
              : t`Downloading`}
      </p>
      <DialogButtons hints={hints} onPress={onBack} />
    </Dialog>
  );
}

function UpdateFailed({ app, error, onChoice }: { app: TvApp; error: string; onChoice: (playOld: boolean) => void }) {
  const installed = versionLabel(app.installedVersion);
  const hints: ButtonHint[] = [
    { action: "accept", label: t({ message: `Play ${installed}`, comment: "Button: plays the version already installed, as in Play v1.2" }) },
    { action: "back", label: t({ message: "Back", comment: "Controller hint: what Back does, closing or going back" }) },
  ];
  const press = (action: InputAction) => (action === "accept" ? onChoice(true) : action === "back" ? onChoice(false) : undefined);
  useInputHandler({ onAction: press, hints: () => hints }, LAYER.dialog);
  return (
    <Dialog app={app}>
      <h2 className="danger">
        <AlertTriangle aria-hidden="true" /> <Trans>Update failed</Trans>
      </h2>
      <p className="tv-dialog-lead">{error}</p>
      <DialogButtons hints={hints} onPress={press} />
    </Dialog>
  );
}
