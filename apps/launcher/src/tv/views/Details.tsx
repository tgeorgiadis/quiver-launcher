/**
 * A game's page (Y; spec §8): its hero, logo and story, what's installed, and
 * buttons: Play (or Install / Get), Update, Change version, Console, Change program
 * (once one was picked: programs/usePrograms.ts), Open folder, Uninstall (held). Left/Right pick a button, A presses it.
 */
import { useEffect, useState, type ReactNode } from "react";
import { ArrowUpCircle, Download, FolderOpen, Gamepad2, History, Play, SquareTerminal, Trash2 } from "lucide-react";
import type { Release } from "@quiverlauncher/api";
import { useTv, useInputHandler } from "../context";
import { LAYER, type ButtonHint } from "../input/router";
import type { TvApp } from "../bridge";
import { isInstalled } from "../bridge";
import { resolveConsole } from "../consoles/resolve";
import { native } from "../../native";
import { versionLabel } from "../../versions";
import { GameChips, GameLogo, playedLabel } from "./GameHeader";
import { ConsolePicker } from "./Settings";
import { isBusy } from "./tileActions";

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
        { action: "accept", label: "Install this version" },
        { action: "back", label: "Back" },
      ],
    },
    LAYER.dialog,
  );
  useEffect(() => {
    document.querySelector(".tv-version-list .focused")?.scrollIntoView({ block: "nearest" });
  }, [index]);
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-label="Change version">
      <section className="tv-dialog tv-picker">
        <div className="tv-dialog-body">
          <h2>
            <History aria-hidden="true" /> Change version
          </h2>
          <p className="tv-dialog-lead">Installed: {app.installedVersion ? versionLabel(app.installedVersion) : "nothing yet"}</p>
          {error ? (
            <p className="tv-error">{error}</p>
          ) : !releases ? (
            <p className="tv-dialog-lead small">Loading releases…</p>
          ) : (
            <ul className="tv-settings-list tv-version-list">
              {releases.map((r, i) => (
                <li key={r.id} className={i === index ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => pick(r)}>
                  <span className="tv-settings-label">
                    {versionLabel(r.version)}
                    <small>
                      {new Date(r.releasedAt).toLocaleDateString()}
                      {r.prerelease ? " · pre-release" : ""}
                    </small>
                  </span>
                  <span className="tv-settings-value">
                    {r.version === app.installedVersion ? "Installed" : r.version === app.latestVersion ? "Latest" : ""}
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
  const { bridge, play, consoles, data, router, sidecar } = useTv();
  const app = bridge.apps.find((a) => a.id === opened.id) ?? bridge.view(opened.entry);
  const [sub, setSub] = useState<"version" | "console" | null>(null);
  const [index, setIndex] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const installed = isInstalled(app) && app.installedVersion;
  const consoleName = consoles.get(resolveConsole(app, data.apps[app.id]?.consoleOverride)).name;
  const plays = data.apps[app.id]?.playCount ?? 0;

  const buttons: Button[] = [
    ...(installed ? [{ id: "play", label: "Play", icon: <Play />, run: () => play(app.id) }] : []),
    ...(!installed && !isBusy(app)
      ? [{ id: "get", label: app.inLibrary ? "Install" : "Get", icon: <Download />, run: () => void bridge.install(app.entry) }]
      : []),
    ...(app.state === "updateAvailable" ? [{ id: "update", label: `Update to ${versionLabel(app.latestVersion)}`, icon: <ArrowUpCircle />, run: () => void bridge.update(app.id) }] : []),
    ...(app.slug && !isBusy(app) ? [{ id: "version", label: "Change version", icon: <History />, run: () => setSub("version") }] : []),
    ...(app.inLibrary ? [{ id: "console", label: "Console", icon: <Gamepad2 />, run: () => setSub("console") }] : []),
    ...(data.apps[app.id]?.program
      ? [
          {
            id: "program",
            label: "Change program",
            icon: <SquareTerminal />,
            run: () => {
              sidecar.updateApp(app.id, ({ program: _, ...rest }) => rest);
              setNotice(`It started ${data.apps[app.id]?.program}. The next Play asks which program starts it.`);
            },
          },
        ]
      : []),
    ...(installed
      ? [
          {
            id: "folder",
            label: "Open folder",
            icon: <FolderOpen />,
            run: () => void native.openFolder(app.id).then(() => setNotice("The folder opened on the desktop."), (e) => setNotice(String(e))),
          },
          { id: "uninstall", label: "Uninstall", icon: <Trash2 />, hold: true, run: () => (void bridge.uninstall(app.id), onClose()) },
        ]
      : []),
  ];
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
        { action: "back", label: "Back" },
      ],
    },
    LAYER.overlay,
    !sub,
  );

  const hero = app.art.hero ?? app.art.capsule;
  return (
    <section className="tv-overlay tv-details" role="dialog" aria-label={app.title}>
      {hero && <img className="tv-details-hero" src={hero} alt="" />}
      <div className="tv-details-body">
        <GameLogo app={app} className="large" />
        <GameChips app={app} consoleName={consoleName} />
        <div className="tv-details-actions" role="toolbar">
          {buttons.map((b, i) => (
            <button
              key={b.id}
              type="button"
              className={`tv-details-button${i === at ? " focused" : ""}${b.id === "play" || b.id === "get" ? " primary" : ""}${b.hold ? " danger" : ""}`}
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
              {app.state === "queued" ? "Waiting to download…" : app.progress !== null ? `Downloading · ${Math.round(app.progress * 100)}%` : "Downloading…"}
            </span>
          )}
        </div>
        {button?.hold && <p className="tv-details-hint">Hold to uninstall. Its files go to the Recycle Bin.</p>}
        {notice && <p className="tv-details-hint">{notice}</p>}
        <dl className="tv-details-facts">
          {app.installedVersion && (
            <div>
              <dt>Installed</dt>
              <dd>{versionLabel(app.installedVersion)}</dd>
            </div>
          )}
          {app.latestVersion && (
            <div>
              <dt>Latest</dt>
              <dd>{versionLabel(app.latestVersion)}</dd>
            </div>
          )}
          {app.lastPlayed && (
            <div>
              <dt>Last played</dt>
              <dd>{playedLabel(app.lastPlayed).replace(/^Played /, "")}</dd>
            </div>
          )}
          {plays > 0 && (
            <div>
              <dt>Played</dt>
              <dd>
                {plays} {plays === 1 ? "time" : "times"}
              </dd>
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
