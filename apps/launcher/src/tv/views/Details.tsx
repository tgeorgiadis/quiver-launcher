/**
 * A game's page (Y): its hero, logo and story, what's installed, and
 * buttons: Play (or Install / Get), Update, Change version, Console, Change program
 * (once one was picked: programs/usePrograms.ts), Open folder, Uninstall (held). Left/Right pick a button, A presses it.
 */
import { useEffect, useState, type ReactNode } from "react";
import { ArrowUpCircle, Download, FolderOpen, Gamepad2, History, Play, SquareTerminal, Trash2 } from "lucide-react";
import type { Release } from "@quiverlauncher/api";
import { SourcedImage, heroSources } from "@quiverlauncher/ui";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import { useTv, useInputHandler } from "../context";
import { LAYER, type ButtonHint } from "../input/router";
import type { TvApp } from "../bridge";
import { isInstalled } from "../bridge";
import { resolveConsole } from "../consoles/resolve";
import { native } from "../../native";
import { useLauncher } from "../../store";
import { playTimeLabel } from "../../sidebar";
import { versionLabel } from "../../versions";
import { GameChips, GameLogo, playedWhen } from "./GameHeader";
import { ConsolePicker } from "./Settings";
import { HINT, isBusy } from "./tileActions";

const back = () => t({ message: "Back", comment: "Controller hint: what Back does, closing or going back" });

const UNINSTALL_HOLD_MS = 1500;

type Button = { id: string; label: string; icon: ReactNode; run: () => void; hold?: boolean };

/** The catalog's releases of the game: the player picks one to install (and stays on it until they update). */
function VersionPicker({ app, onClose }: { app: TvApp; onClose: () => void }) {
  const { bridge } = useTv();
  const [releases, setReleases] = useState<Release[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [index, setIndex] = useState(0);
  useEffect(() => {
    void bridge
      .releases(app.slug, 20)
      .then((page) => setReleases(page.items))
      .catch((e) => setError(e instanceof Error ? e.message : String(e)));
  }, [bridge, app.slug]);
  const pick = (release: Release) => {
    void bridge.install(app.entry, release);
    onClose();
  };
  useInputHandler(
    {
      onAction(action) {
        if (action === "up") setIndex((i) => Math.max(0, i - 1));
        else if (action === "down") setIndex((i) => Math.min((releases?.length ?? 1) - 1, i + 1));
        else if (action === "accept" && releases?.[index]) pick(releases[index]);
        else if (action === "back") onClose();
      },
      hints: () => [
        { action: "accept", label: t`Install this version` },
        { action: "back", label: back() },
      ],
    },
    LAYER.dialog,
  );
  useEffect(() => {
    document.querySelector(".tv-version-list .focused")?.scrollIntoView({ block: "nearest" });
  }, [index]);
  const installed = app.installedVersion && versionLabel(app.installedVersion);
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-label={t`Change version`}>
      <section className="tv-dialog tv-picker">
        <div className="tv-dialog-body">
          <h2>
            <History aria-hidden="true" /> <Trans>Change version</Trans>
          </h2>
          <p className="tv-dialog-lead">{installed ? t`Installed: ${installed}` : t`Installed: nothing yet`}</p>
          {error ? (
            <p className="tv-error">{error}</p>
          ) : !releases ? (
            <p className="tv-dialog-lead small">
              <Trans>Loading releases…</Trans>
            </p>
          ) : (
            <ul className="tv-settings-list tv-version-list">
              {releases.map((r, i) => (
                <li key={r.id} className={i === index ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => pick(r)}>
                  <span className="tv-settings-label">
                    {versionLabel(r.version)}
                    <small>
                      {new Date(r.releasedAt).toLocaleDateString(i18n.locale)}
                      {r.prerelease ? ` · ${t({ message: "pre-release", comment: "After a release's date: its developer marked it as not yet stable" })}` : ""}
                    </small>
                  </span>
                  <span className="tv-settings-value">
                    {r.version === app.installedVersion
                      ? t({ message: "Installed", context: "release", comment: "This release is the one on this computer" })
                      : r.version === app.latestVersion
                        ? t({ message: "Latest", comment: "This release is the newest" })
                        : ""}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      </section>
    </div>
  );
}

/** `app` as it was opened; shown live from the library when it's in it (a game from Discover may not be). */
export function Details({ app: opened, onClose }: { app: TvApp; onClose: () => void }) {
  const { bridge, play, consoles, router } = useTv();
  const { installs, setLaunch } = useLauncher();
  const app = bridge.apps.find((a) => a.id === opened.id) ?? bridge.view(opened.entry);
  const [sub, setSub] = useState<"version" | "console" | null>(null);
  const [index, setIndex] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const installed = isInstalled(app) && app.installedVersion;
  const consoleName = consoles.get(resolveConsole(app)).name;

  const next = versionLabel(app.latestVersion);
  const program = app.program;
  const buttons: Button[] = [
    ...(installed ? [{ id: "play", label: HINT.play(), icon: <Play />, run: () => play(app.id) }] : []),
    ...(!installed && !isBusy(app)
      ? [{ id: "get", label: app.inLibrary ? HINT.install() : HINT.get(), icon: <Download />, run: () => void bridge.install(app.entry) }]
      : []),
    ...(app.state === "updateAvailable" ? [{ id: "update", label: t`Update to ${next}`, icon: <ArrowUpCircle />, run: () => void bridge.update(app.id) }] : []),
    ...(app.slug && !isBusy(app) ? [{ id: "version", label: t`Change version`, icon: <History />, run: () => setSub("version") }] : []),
    ...(app.inLibrary ? [{ id: "console", label: t({ message: "Console", comment: "Button on a game's page in TV mode: picks the console tab it's in" }), icon: <Gamepad2 />, run: () => setSub("console") }] : []),
    ...(program && installs[app.id]
      ? [
          {
            id: "program",
            label: t`Change program`,
            icon: <SquareTerminal />,
            run: () => {
              const { executable: _, ...launch } = installs[app.id].launch ?? {};
              setLaunch(app.id, launch);
              setNotice(t`It started ${program}. The next Play asks which program starts it.`);
            },
          },
        ]
      : []),
    ...(installed
      ? [
          {
            id: "folder",
            label: t`Open folder`,
            icon: <FolderOpen />,
            run: () => void native.openFolder(app.id).then(() => setNotice(t`The folder opened on the desktop.`), (e) => setNotice(String(e))),
          },
          { id: "uninstall", label: t`Uninstall`, icon: <Trash2 />, hold: true, run: () => (void bridge.uninstall(app.id), onClose()) },
        ]
      : []),
  ];
  const percent = app.progress !== null ? Math.round(app.progress * 100) : null;
  const at = Math.min(index, buttons.length - 1);
  const button = buttons[at];

  useInputHandler(
    {
      onAction(action) {
        if (action === "left") setIndex(Math.max(0, at - 1));
        else if (action === "right") setIndex(Math.min(buttons.length - 1, at + 1));
        else if (action === "accept" && button && !button.hold) button.run();
        else if (action === "back" || action === "details") onClose();
      },
      holdFor: (action) => (action === "accept" && button?.hold ? UNINSTALL_HOLD_MS : undefined),
      onHold: (action) => action === "accept" && button?.hold && button.run(),
      hints: (): ButtonHint[] => [
        ...(button ? [{ action: "accept" as const, label: button.label, hold: button.hold }] : []),
        { action: "back", label: back() },
      ],
    },
    LAYER.overlay,
    !sub,
  );

  const hero = app.art.hero ?? app.art.capsule;
  return (
    <section className="tv-overlay tv-details" role="dialog" aria-label={app.title}>
      {hero && <SourcedImage key={hero} className="tv-details-hero" sources={heroSources(hero)} alt="" priority onFail={() => {}} />}
      <div className="tv-details-body">
        <GameLogo app={app} className="large" />
        <GameChips app={app} consoleName={consoleName} />
        <div className="tv-details-actions" role="toolbar">
          {buttons.map((b, i) => (
            <button
              key={b.id}
              type="button"
              className={`tv-details-button${i === at ? " focused" : ""}${b.id === "play" || b.id === "get" ? " tv-primary" : ""}${b.hold ? " danger" : ""}`}
              onMouseMove={() => setIndex(i)}
              onClick={() => !b.hold && b.run()}
              // A held button is held with the mouse too: pressed as long as it's down, as A is.
              onPointerDown={(e) => {
                if (!b.hold || e.button !== 0) return;
                setIndex(i);
                e.currentTarget.setPointerCapture(e.pointerId);
                setTimeout(() => router.dispatch({ action: "accept", phase: "press" }), 0);
              }}
              onPointerUp={() => b.hold && router.dispatch({ action: "accept", phase: "release" })}
              onPointerCancel={() => b.hold && router.dispatch({ action: "accept", phase: "release" })}
            >
              {b.icon}
              {b.label}
            </button>
          ))}
          {isBusy(app) && (
            <span className="tv-details-busy">
              {app.state === "queued" ? t`Waiting to download…` : percent !== null ? t`Downloading ${percent}%` : t`Downloading…`}
            </span>
          )}
        </div>
        {button?.hold && (
          <p className="tv-details-hint">
            <Trans>Hold to uninstall. Its files go to the Recycle Bin.</Trans>
          </p>
        )}
        {notice && <p className="tv-details-hint">{notice}</p>}
        <dl className="tv-details-facts">
          {app.installedVersion && (
            <div>
              <dt>{t({ message: "Installed", context: "release", comment: "The version installed, then a version" })}</dt>
              <dd>{versionLabel(app.installedVersion)}</dd>
            </div>
          )}
          {app.latestVersion && (
            <div>
              <dt>{t({ message: "Latest", comment: "The newest version, then a version" })}</dt>
              <dd>{versionLabel(app.latestVersion)}</dd>
            </div>
          )}
          {app.lastPlayed && (
            <div>
              <dt>
                <Trans>Last played</Trans>
              </dt>
              <dd>{playedWhen(app.lastPlayed)}</dd>
            </div>
          )}
          {app.playTime !== undefined && (
            <div>
              <dt>
                <Trans>Play time</Trans>
              </dt>
              <dd>{playTimeLabel(app.playTime)}</dd>
            </div>
          )}
        </dl>
        <p className="tv-description">{app.entry.description}</p>
        {app.error && <p className="tv-error">{app.error}</p>}
      </div>
      {sub === "version" && <VersionPicker app={app} onClose={() => setSub(null)} />}
      {sub === "console" && <ConsolePicker app={app} onClose={() => setSub(null)} />}
    </section>
  );
}
