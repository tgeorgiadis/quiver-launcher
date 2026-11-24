/**
 * An app the launcher started closed with an error right after starting, or crashed: says so, shows what the app
 * last wrote, and for a catalog app offers to send Quiver a report (with error reports on). Nothing goes to the app's
 * developer from here: Quiver staff pass on what's theirs to fix. The player can copy the details for themselves.
 * An app that closed after asking for a key had a message for the player (game-report.ts's ownMessage): that's
 * shown as the app's own words, with no report to send.
 */
import { useEffect, useMemo, useState } from "react";
import { Check, CircleCheck, Copy, Send, TriangleAlert } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { OS_NAMES } from "@quiverlauncher/ui";
import { codeText, endOf, failureLine, ownMessage, reportText, type GameFailure, type ReportDetails } from "./game-report";
import { Dialog } from "./library";
import { native } from "./native";
import { appRef, useLauncher, type CatalogEntry } from "./store";
import { playerReportsAvailable, scrub, sendPlayerReport, track } from "./telemetry";

export function GameFailedPrompt() {
  const { failed, closeFailed, catalog } = useLauncher();
  const entry = failed?.id ? catalog[failed.id] : undefined;
  if (!failed || !entry) return null;
  // A new one for each failure: the same app failing again starts from its own output, nothing sent yet.
  return <GameFailed key={`${failed.id} ${failed.at}`} failure={failed} entry={entry} onClose={closeFailed} />;
}

function GameFailed({ failure, entry, onClose }: { failure: GameFailure; entry: CatalogEntry; onClose: () => void }) {
  const { config, installs } = useLauncher();
  const install = installs[entry.id];
  const [log, setLog] = useState<string | null>(null);
  const [sent, setSent] = useState<"sending" | "sent" | "failed" | null>(null);
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    let live = true;
    // Without paths, the home folder or the user name, before it's shown or sent anywhere.
    native.gameLog(entry.id).then(
      (text) => live && setLog(scrub(endOf(text))),
      () => live && setLog(""),
    );
    return () => void (live = false);
  }, [entry.id]);

  const app = entry.projectName;
  const ref = appRef(entry.id, entry.slug);
  const details = useMemo<ReportDetails>(() => {
    const os = config.steamDeck ? "Steam Deck" : (OS_NAMES as Record<string, string>)[config.os] ?? config.os;
    return {
      name: app,
      version: install?.version ?? "",
      launcher: config.version,
      system: [os, config.osVersion, `(${config.arch})`].filter(Boolean).join(" "),
      wine: Boolean(install?.wine),
      failure: { ...failure, line: failure.line && scrub(failure.line) },
      log: log ?? "",
    };
  }, [app, install?.version, install?.wine, config, failure, log]);
  const message = log ? ownMessage(failure, log) : null;
  // Only a catalog app's report is Quiver's to look at: one the player added is named to no one. And what the app
  // asked of the player isn't a fault for anyone to look into.
  const toQuiver = Boolean(ref.slug) && playerReportsAvailable() && !message;
  const code = failure.code === null ? null : codeText(failure.code, config.os);

  const send = () => {
    setSent("sending");
    const said = failureLine(failure, config.os);
    void sendPlayerReport("game_exit", `${ref.slug}: ${said}`, [ref.slug ?? "", said], {
      slug: ref.slug,
      version: details.version,
      kind: failure.kind,
      code,
      what: failure.what,
      line: details.failure.line,
      wine: details.wine,
      log: details.log,
    }).then((ok) => {
      setSent(ok ? "sent" : "failed");
      if (ok) track("app_failure_reported", ref);
    });
  };
  const copy = () =>
    void navigator.clipboard?.writeText(reportText(details, config.os)).then(
      () => setCopied(true),
      // The system refused: the text is on screen to select.
      () => {},
    );

  const title = message ? t`${app} closed with a message` : failure.kind === "crashed" ? t`${app} crashed` : t`${app} closed right after starting`;
  return (
    <Dialog label={title} className="game-failed" onClose={onClose}>
      <div className="detail-body game-failed-body">
        <h2>
          <TriangleAlert size={18} aria-hidden="true" /> {title}
        </h2>
        {message ? (
          <>
            <p>
              <Trans>It closed right after starting. This is what it said:</Trans>
            </p>
            <pre className="game-failed-message" tabIndex={0}>
              {message}
            </pre>
          </>
        ) : (
          <>
            <p>
              {failure.kind === "crashed" ? (
                <Trans>It stopped by itself while it was running.</Trans>
              ) : code ? (
                <Trans>It stopped with an error (exit code {code}) before it got going.</Trans>
              ) : (
                <Trans>It stopped with an error before it got going.</Trans>
              )}{" "}
              {toQuiver && <Trans>Send a report so Quiver staff can look into it.</Trans>}
            </p>
            {log ? (
              <details className="game-failed-log" open>
                <summary>
                  <Trans>What it said last</Trans>
                </summary>
                <pre tabIndex={0}>{log}</pre>
              </details>
            ) : (
              log === "" && (
                <p className="muted">
                  <Trans>It didn&apos;t say why.</Trans>
                </p>
              )
            )}
          </>
        )}
        {toQuiver && (
          <p className="muted">
            <Trans>A report has the app and its version, your system, and the text above. Folder and user names are taken out.</Trans>
          </p>
        )}
        {sent === "sent" && (
          <p className="game-failed-sent" role="status">
            <CircleCheck size={16} aria-hidden="true" /> <Trans>Report sent. Thanks.</Trans>
          </p>
        )}
        {sent === "failed" && (
          <p className="job-error" role="status">
            <Trans>Couldn&apos;t send the report. Check your connection and try again.</Trans>
          </p>
        )}
        <div className="row">
          {toQuiver && sent !== "sent" && (
            <button className="primary" autoFocus disabled={sent === "sending" || log === null} onClick={send}>
              <Send size={15} aria-hidden="true" /> {sent === "sending" ? <Trans>Sending…</Trans> : <Trans>Send report</Trans>}
            </button>
          )}
          <button type="button" disabled={log === null} onClick={copy}>
            {copied ? <Check size={15} aria-hidden="true" /> : <Copy size={15} aria-hidden="true" />} {copied ? <Trans>Copied</Trans> : <Trans>Copy details</Trans>}
          </button>
          <button type="button" onClick={onClose}>
            <Trans>Close</Trans>
          </button>
        </div>
      </div>
    </Dialog>
  );
}
