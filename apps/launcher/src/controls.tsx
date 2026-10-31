/** Settings: the controllers connected now, and keyboard and controller bindings. */
import { useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { usePadLabels, usePads } from "./pad-hints";
import { useLauncher } from "./store";
import { ACTIONS, ACTION_NAMES, DEFAULT_KEYS, DEFAULT_PAD, assign, captureNext, confirmBackSwapped, label, swapConfirmBack, withDefaults, type Action } from "./spatial";

export function ControlSettings() {
  const { settings, setSettings } = useLauncher();
  const pads = usePads();
  // A controller's buttons as the one used last labels them (✕ ○ on a PlayStation pad).
  const labels = usePadLabels();
  const [listening, setListening] = useState<{ action: Action; kind: "keys" | "pad" } | null>(null);
  const bindings = { keys: withDefaults(settings.keys, DEFAULT_KEYS), pad: withDefaults(settings.pad, DEFAULT_PAD) };
  async function rebind(action: Action, kind: "keys" | "pad") {
    setListening({ action, kind });
    const binding = await captureNext(kind);
    setListening(null);
    if (binding) setSettings({ ...settings, [kind]: assign(bindings[kind], action, binding) });
  }
  const cell = (action: Action, kind: "keys" | "pad") => {
    const now = listening?.action === action && listening.kind === kind;
    const name = ACTION_NAMES[action];
    return (
      <button
        aria-label={kind === "keys" ? t`${name}: keyboard` : t`${name}: controller`}
        className={now ? "chosen" : ""}
        disabled={Boolean(listening) && !now}
        onClick={() => rebind(action, kind)}
      >
        {now ? (kind === "keys" ? t`Press a key…` : t`Press a button…`) : bindings[kind][action].map((b) => label(kind, b, labels)).join(" / ") || "—"}
      </button>
    );
  };
  const south = label("pad", "South", labels);
  const east = label("pad", "East", labels);
  return (
    <section className="controls" aria-label={t`Controllers`}>
      {pads.length ? (
        <ol className="pads">
          {pads.map((name, i) => (
            <li key={i}>{name}</li>
          ))}
        </ol>
      ) : (
        <p className="muted">
          <Trans>No controllers detected. Connect one and it appears here.</Trans>
        </p>
      )}
      <label className="toggle">
        <input type="checkbox" checked={!settings.padOff} onChange={(e) => setSettings({ ...settings, padOff: !e.target.checked })} />
        <Trans>Use controllers to move around Quiver</Trans>
      </label>
      <label className="toggle">
        <input
          type="checkbox"
          checked={!settings.padHintsOff}
          disabled={settings.padOff}
          onChange={(e) => setSettings({ ...settings, padHintsOff: !e.target.checked })}
        />
        <Trans>Show what the buttons do along the bottom while a controller is connected</Trans>
      </label>
      <label className="toggle">
        <input
          type="checkbox"
          checked={confirmBackSwapped(bindings.pad)}
          disabled={settings.padOff}
          onChange={() => setSettings({ ...settings, pad: swapConfirmBack(bindings.pad) })}
        />
        <Trans comment="south and east are a controller's buttons as it labels them, like A and B or ✕ and ○">
          Swap {south} and {east}: Confirm on {east}, Back on {south}
        </Trans>
      </label>
      <p className="muted">
        {listening ? t`Press what you want to use, or Esc to cancel.` : t`Pick a binding, then press the key or button to use for it.`}
      </p>
      <table className="bindings">
        <thead>
          <tr>
            <th>
              <Trans comment="Heading of the bindings table's column of things to do">Action</Trans>
            </th>
            <th>
              <Trans>Keyboard and mouse</Trans>
            </th>
            <th>
              <Trans comment="Heading of the bindings table's column of controller buttons">Controller</Trans>
            </th>
          </tr>
        </thead>
        <tbody>
          {ACTIONS.map((action) => (
            <tr key={action}>
              <td>{ACTION_NAMES[action]}</td>
              <td>{cell(action, "keys")}</td>
              <td>{cell(action, "pad")}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <button onClick={() => setSettings({ ...settings, keys: undefined, pad: undefined })}>
        <Trans>Reset bindings to defaults</Trans>
      </button>
    </section>
  );
}
