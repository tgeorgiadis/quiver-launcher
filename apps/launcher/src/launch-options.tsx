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
import { native, type LaunchOptions, type Proton, type WindowsRunner } from "./native";
import { useLauncher } from "./store";

type Runner = LaunchOptions["runner"];

const runnerValue = (runner: Runner) => (!runner ? "" : runner.kind === "proton" ? `proton:${runner.path}` : runner.kind);
const runnerFrom = (value: string): Runner =>
  value === "direct" || value === "wine" ? { kind: value } : value.startsWith("proton:") ? { kind: "proton", path: value.slice(7) } : undefined;
/** A Proton's name from its folder. */
const folderName = (path: string) => path.replace(/[/\\]+$/, "").split(/[/\\]/).pop() || path;

/**
 * What runs a program, as a dropdown: Automatic first, saying what it runs Windows programs with (`automatic`), then
 * Wine, each Proton found, and another Proton the player picks. `direct`: running the program itself, for one app.
 */
export function RunnerSelect({ value, onChange, onError, automatic, direct }: { value: Runner; onChange: (runner: Runner) => void; onError: (error: string) => void; automatic: string; direct?: boolean }) {
  const [protons, setProtons] = useState<Proton[]>([]);
  useEffect(() => {
    native.listProtons().then(setProtons, () => setProtons([]));
  }, []);
  const pick = (choice: string) => {
    if (choice !== "other") return onChange(runnerFrom(choice));
    native.pickProton().then(
      (path) => path && onChange({ kind: "proton", path }),
      (e) => onError(String(e)),
    );
  };
  const listed = value?.kind === "proton" && !protons.some((p) => p.path === value.path) ? [...protons, { name: folderName(value.path), path: value.path }] : protons;
  return (
    <select aria-label={t`Run with`} value={runnerValue(value)} onChange={(e) => pick(e.target.value)}>
      <option value="">{automatic}</option>
      {direct && <option value="direct">{t`The program itself`}</option>}
      <option value="wine">Wine</option>
      {listed.map((p) => (
        <option key={p.path} value={`proton:${p.path}`}>
          {p.name}
        </option>
      ))}
      <option value="other">{t`Another Proton…`}</option>
    </select>
  );
}

/** What Automatic runs Windows programs with here, by name: a Proton Steam has, else Wine; null with neither, undefined until known. */
export function useAutomaticRunner() {
  const [name, setName] = useState<string | null>();
  useEffect(() => {
    native.automaticRunner().then(setName, () => setName(null));
  }, []);
  return name;
}

/** A runner Settings set, by name. */
const runnerName = (runner: NonNullable<Runner>) => (runner.kind === "proton" ? folderName(runner.path) : runner.kind === "wine" ? "Wine" : t`The program itself`);

/** Automatic's line in a Run with dropdown, saying what it runs Windows programs with. */
const automaticLine = (name: string | null | undefined) =>
  name === undefined ? t`Automatic` : name === null ? t`Automatic (no Proton or Wine found)` : t`Automatic: ${name}`;

/** One app's Run with: its first line is what Settings runs Windows programs with, which the app keeps to unless it has its own. */
function AppRunner({ value, onChange, onError, defaultRunner }: { value: Runner; onChange: (runner: Runner) => void; onError: (error: string) => void; defaultRunner?: WindowsRunner }) {
  const automatic = useAutomaticRunner();
  const name = defaultRunner && runnerName(defaultRunner);
  const first = name ? t({ message: `As in Settings: ${name}`, comment: "Run with: the runner chosen in Settings for every Windows app" }) : automaticLine(automatic);
  return <RunnerSelect value={value} onChange={onChange} onError={onError} automatic={first} direct />;
}

/** Settings' Run with for every Windows program on Linux, where an app has none of its own (Launch options). */
export function WindowsRunnerSetting() {
  const { config, settings, setSettings } = useLauncher();
  const automatic = useAutomaticRunner();
  const [error, setError] = useState<string | null>(null);
  if (config.os !== "linux") return null;
  return (
    <section className="settings-section" aria-label={t`Windows apps`}>
      <h3>
        <Trans>Windows apps</Trans>
      </h3>
      <label>
        <Trans>Run with</Trans>{" "}
        <RunnerSelect
          value={settings.windowsRunner}
          onChange={(runner) => (setError(null), setSettings({ ...settings, windowsRunner: runner?.kind === "direct" ? undefined : runner }))}
          onError={setError}
          automatic={automaticLine(automatic)}
        />
      </label>
      <p className="muted">
        <Trans>Automatic uses a Proton that Steam has, which comes with what most games need, or Wine where there's no Proton. An app's own launch options can choose another.</Trans>
      </p>
      {error && <p className="job-error">{error}</p>}
    </section>
  );
}

export function LaunchOptionsButton({ id }: { id: string }) {
  const { installs, config, settings, setLaunch } = useLauncher();
  const install = installs[id];
  const [open, setOpen] = useState(false);
  const [executable, setExecutable] = useState<string | undefined>();
  const [runner, setRunner] = useState<Runner>();
  const [command, setCommand] = useState("");
  const [error, setError] = useState<string | null>(null);
  const linux = config.os === "linux";
  useEffect(() => {
    if (!open || !install) return;
    setExecutable(install.launch?.executable);
    setRunner(install.launch?.runner);
    setCommand(install.launch?.command ?? "");
    setError(null);
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
          <AppRunner value={runner} onChange={(r) => (setError(null), setRunner(r))} onError={setError} defaultRunner={settings.windowsRunner} />
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
