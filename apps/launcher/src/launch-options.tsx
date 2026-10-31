/**
 * Launch options for an installed app, for when the launcher's own choice
 * isn't right: which of its programs starts it, whether it runs directly,
 * through Wine or through a Proton (Linux), and launch options written the
 * way Steam takes them. Play, desktop shortcuts and Steam shortcuts all use them.
 */
import { useEffect, useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { PanelFormHead } from "./detail";
import { native, type LaunchOptions, type Proton } from "./native";
import { useLauncher } from "./store";

type Runner = LaunchOptions["runner"];

const runnerValue = (runner: Runner) => (!runner ? "" : runner.kind === "proton" ? `proton:${runner.path}` : runner.kind);
const runnerFrom = (value: string): Runner =>
  value === "direct" || value === "wine" ? { kind: value } : value.startsWith("proton:") ? { kind: "proton", path: value.slice(7) } : undefined;
/** A Proton's name from its folder. */
const folderName = (path: string) => path.replace(/[/\\]+$/, "").split(/[/\\]/).pop() || path;

export function LaunchOptionsButton({ id }: { id: string }) {
  const { installs, config, setLaunch } = useLauncher();
  const install = installs[id];
  const [open, setOpen] = useState(false);
  const [executable, setExecutable] = useState<string | undefined>();
  const [runner, setRunner] = useState<Runner>();
  const [command, setCommand] = useState("");
  const [protons, setProtons] = useState<Proton[]>([]);
  const [error, setError] = useState<string | null>(null);
  const linux = config.os === "linux";
  useEffect(() => {
    if (!open || !install) return;
    setExecutable(install.launch?.executable);
    setRunner(install.launch?.runner);
    setCommand(install.launch?.command ?? "");
    setError(null);
    if (linux) native.listProtons().then(setProtons, () => setProtons([]));
    // What's saved fills the form as it opens.
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!install) return null;
  const set = Boolean(install.launch);
  if (!open)
    return (
      <button className="panel-action opens" onClick={() => setOpen(true)}>
        <SlidersHorizontal size={16} aria-hidden="true" /> <Trans>Launch options</Trans>
        {set && (
          <span className="action-note">
            {" "}
            <Trans comment="Beside Launch options: the player changed them">(changed)</Trans>
          </span>
        )}
      </button>
    );
  const choose = () => {
    setError(null);
    native.pickExecutable(install.folder, install.dir, install.program).then(
      (path) => path && setExecutable(path),
      (e) => setError(String(e)),
    );
  };
  const pickRunner = (value: string) => {
    setError(null);
    if (value !== "other") return setRunner(runnerFrom(value));
    native.pickProton().then(
      (path) => path && setRunner({ kind: "proton", path }),
      (e) => setError(String(e)),
    );
  };
  const listed = runner?.kind === "proton" && !protons.some((p) => p.path === runner.path) ? [...protons, { name: folderName(runner.path), path: runner.path }] : protons;
  const unclosed = (command.match(/"/g)?.length ?? 0) % 2 === 1 || (command.match(/'/g)?.length ?? 0) % 2 === 1;
  return (
    <form
      className="panel-form launch-options"
      aria-label={t`Launch options`}
      onSubmit={(e) => {
        e.preventDefault();
        if (unclosed) return;
        setLaunch(id, { executable, runner, command: command.trim() || undefined });
        setOpen(false);
      }}
    >
      <PanelFormHead title={t`Launch options`} onClose={() => setOpen(false)} />
      <div className="launch-program">
        <span className="field-label">
          <Trans>Program</Trans>
        </span>
        <p className="muted">
          {executable ? (
            <Trans>
              Starts <code>{executable}</code>
            </Trans>
          ) : (
            t`Starts the program Quiver finds in its folder.`
          )}
        </p>
        <div className="row">
          <button type="button" onClick={choose}>
            <Trans>Choose program…</Trans>
          </button>
          {executable && (
            <button type="button" onClick={() => setExecutable(undefined)}>
              <Trans>Let Quiver choose</Trans>
            </button>
          )}
        </div>
      </div>
      {linux && (
        <label>
          <span className="field-label">
            <Trans>Run with</Trans>
          </span>
          <select aria-label={t`Run with`} value={runnerValue(runner)} onChange={(e) => pickRunner(e.target.value)}>
            <option value="">{t`Automatic (Wine for Windows apps)`}</option>
            <option value="direct">{t`The program itself`}</option>
            <option value="wine">Wine</option>
            {listed.map((p) => (
              <option key={p.path} value={`proton:${p.path}`}>
                {p.name}
              </option>
            ))}
            <option value="other">{t`Another Proton…`}</option>
          </select>
        </label>
      )}
      <label>
        <span className="field-label">
          <Trans>Launch flags</Trans>
        </span>
        <input
          aria-label={t`Launch flags`}
          placeholder={linux ? t`e.g. gamemoderun %command% --fullscreen` : t`e.g. --fullscreen`}
          value={command}
          onChange={(e) => setCommand(e.target.value)}
        />
        <span className="muted small">
          <Trans>Added after the program, or write %command% where the program should go. NAME=value at the start sets an environment variable.</Trans>
        </span>
      </label>
      {unclosed && (
        <p className="job-error">
          <Trans>A quote isn&apos;t closed.</Trans>
        </p>
      )}
      {error && (
        <p className="job-error" role="status">
          {error}
        </p>
      )}
      <div className="row">
        <button className="primary">
          <Trans>Save</Trans>
        </button>
        <button type="button" onClick={() => setOpen(false)}>
          <Trans>Cancel</Trans>
        </button>
        {set && (
          <button type="button" onClick={() => (setLaunch(id, {}), setOpen(false))}>
            <Trans comment="Puts the launch options back as Quiver chose them">Reset</Trans>
          </button>
        )}
      </div>
    </form>
  );
}
