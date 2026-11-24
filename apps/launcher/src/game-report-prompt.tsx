/**
 * An app the launcher started closed with an error right after starting, or crashed: says so, shows what the app
 * last wrote, and offers to report it. A catalog app's report goes to Quiver (with error reports on) and can be
 * taken to the app's developer; an app the player added goes only to its own repository.
 */
import { useEffect, useMemo, useState } from "react";
import { Check, CircleCheck, Copy, ExternalLink, Send, TriangleAlert } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { OS_NAMES } from "@quiverlauncher/ui";
import { providerName, useAppDetail } from "./detail";
import { codeText, endOf, failureLine, issueUrl, reportText, type GameFailure, type ReportDetails } from "./game-report";
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
  const { source } = useAppDetail(entry);
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
  // Only a catalog app's report is Quiver's to look at: one the player added is named to no one.
  const toQuiver = Boolean(ref.slug) && playerReportsAvailable();
  const code = failure.code === null ? null : codeText(failure.code, config.os);
  const host = source ? providerName(source.provider, source.host) : null;

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
      if (ok) track("app_failure_reported", { ...ref, to: "quiver" });
    });
  };
  const toDeveloper = () => {
    if (!source) return;
    track("app_failure_reported", { ...ref, to: "developer" });
    void native.openUrl(issueUrl(source, details, config.os));
  };
  const copy = () =>
    void navigator.clipboard?.writeText(reportText(details, config.os)).then(
      () => setCopied(true),
      // The system refused: the text is on screen to select.
      () => {},
    );

  const title = failure.kind === "crashed" ? t`${app} crashed` : t`${app} closed right after starting`;
  return (
    <Dialog label={title} className="game-failed" onClose={onClose}>
      <div className="detail-body game-failed-body">
        <h2>
          <TriangleAlert size={18} aria-hidden="true" /> {title}
        </h2>
        <p>
          {failure.kind === "crashed" ? (
            <Trans>It stopped by itself while it was running.</Trans>
          ) : code ? (
            <Trans>It stopped with an error (exit code {code}) before it got going.</Trans>
          ) : (
            <Trans>It stopped with an error before it got going.</Trans>
          )}{" "}
          {toQuiver ? (
            <Trans>Send a report so Quiver staff can look into it.</Trans>
          ) : source ? (
            <Trans>Its developer can look into it if you report it.</Trans>
          ) : null}
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
        {(toQuiver || source) && (
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
          {source && host && (
            <button type="button" className={toQuiver ? undefined : "primary"} disabled={log === null} onClick={toDeveloper}>
              <ExternalLink size={15} aria-hidden="true" /> <Trans>Report on {host}</Trans>
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
