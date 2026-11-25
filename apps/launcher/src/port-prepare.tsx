/**
 * The port's own setup step, which Play runs before the game when its setup file has one (port_prepare.rs): how far
 * it has got, with Stop; or, when it failed, why, with the last lines it printed, Copy, the full log and Try again.
 */
import { useState } from "react";
import { Check, Copy, FileText, RotateCcw, Square, TriangleAlert, Wrench } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { OS_NAMES } from "@quiverlauncher/ui";
import { Dialog } from "./library";
import { native } from "./native";
import { useLauncher, type Preparing } from "./store";

export function PortPreparePrompt() {
  const { preparing } = useLauncher();
  if (!preparing) return null;
  return preparing.failed ? <Failed preparing={preparing} /> : <Running preparing={preparing} />;
}

function Running({ preparing }: { preparing: Preparing }) {
  const { name, label, progress } = preparing;
  const percent = progress?.fraction == null ? null : Math.round(progress.fraction * 100);
  const title = t`Setting up ${name}`;
  return (
    // It goes on until it ends or is stopped: Escape and a click outside leave it running.
    <Dialog label={title} className="port-prepare" onClose={() => {}}>
      <div className="detail-body port-prepare-body">
        <h2>
          <Wrench size={18} aria-hidden="true" /> {title}
        </h2>
        <p className="muted">
          {label ? (
            <Trans>Its own setup step is making what it needs before it can start: {label}. It runs again after each update, and the game starts once it&apos;s done.</Trans>
          ) : (
            <Trans>Its own setup step is making what it needs before it can start. It runs again after each update, and the game starts once it&apos;s done.</Trans>
          )}
        </p>
        <div className={percent === null ? "progress waiting" : "progress"} role="status" aria-label={t`Setting up`}>
          <div className="bar" style={{ width: `${percent ?? 100}%` }} />
          <span>{percent === null ? t`Working…` : `${percent}%`}</span>
        </div>
        {progress?.text && <p className="port-prepare-step">{progress.text}</p>}
        <div className="row">
          <button type="button" onClick={preparing.stop}>
            <Square size={14} aria-hidden="true" /> <Trans>Stop</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}

function Failed({ preparing }: { preparing: Preparing }) {
  const { config, installs } = useLauncher();
  const [copied, setCopied] = useState(false);
  const [noLog, setNoLog] = useState(false);
  const failed = preparing.failed!;
  const { name } = preparing;
  const code = failed.code;
  const title = t`${name} couldn't be set up`;
  const why = failed.problem
    ? failed.problem
    : failed.stuck
      ? t`Its setup step printed nothing for an hour, so Quiver stopped it.`
      : code !== null
        ? t`Its setup step stopped with code ${code}.`
        : t`Its setup step stopped before it finished.`;
  const copy = () => {
    const install = installs[preparing.id];
    const os = config.steamDeck ? "Steam Deck" : ((OS_NAMES as Record<string, string>)[config.os] ?? config.os);
    const text = [`${name} ${install?.version ?? ""}`.trim(), `${os} ${config.osVersion ?? ""} (${config.arch})`.replace(/\s+/g, " "), `Quiver Launcher ${config.version}`, why, "", ...failed.lines].join("\n");
    void navigator.clipboard?.writeText(text).then(
      () => setCopied(true),
      // The system refused: the lines are on screen to select.
      () => {},
    );
  };
  return (
    <Dialog label={title} className="port-prepare" onClose={preparing.close}>
      <div className="detail-body port-prepare-body">
        <h2>
          <TriangleAlert size={18} aria-hidden="true" /> {title}
        </h2>
        <p>{why}</p>
        {failed.lines.length > 0 && (
          <pre className="port-prepare-lines" tabIndex={0}>
            {failed.lines.join("\n")}
          </pre>
        )}
        {failed.hint && <p>{failed.hint}</p>}
        {!failed.problem && (
          <p className="muted">
            <Trans>The game&apos;s developer can tell what went wrong from these lines. Copy them to send with your question.</Trans>
          </p>
        )}
        {noLog && (
          <p className="job-error" role="status">
            <Trans>There&apos;s no log of its setup step.</Trans>
          </p>
        )}
        <div className="row">
          <button className="primary" autoFocus onClick={preparing.retry}>
            <RotateCcw size={15} aria-hidden="true" /> <Trans>Try again</Trans>
          </button>
          {!failed.problem && (
            <>
              <button type="button" onClick={copy}>
                {copied ? <Check size={15} aria-hidden="true" /> : <Copy size={15} aria-hidden="true" />} {copied ? <Trans>Copied</Trans> : <Trans>Copy</Trans>}
              </button>
              <button type="button" onClick={() => void native.openSetupLog(preparing.id).catch(() => setNoLog(true))}>
                <FileText size={15} aria-hidden="true" /> <Trans>Open full log</Trans>
              </button>
            </>
          )}
          <button type="button" onClick={preparing.close}>
            <Trans>Close</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}
