/** Settings: the controllers connected now, and keyboard and controller bindings. */
import { useEffect, useState } from "react";
import { native } from "./native";
import { useLauncher } from "./store";
import { ACTIONS, ACTION_NAMES, DEFAULT_KEYS, DEFAULT_PAD, assign, captureNext, label, type Action } from "./spatial";

export function ControlSettings() {
  const { settings, setSettings } = useLauncher();
  const [pads, setPads] = useState<string[]>([]);
  const [listening, setListening] = useState<{ action: Action; kind: "keys" | "pad" } | null>(null);
  useEffect(() => {
    void native.controllers().then(setPads);
    const off = native.onControllers(setPads);
    return () => void off.then((stop) => stop());
  }, []);
  const bindings = { keys: settings.keys ?? DEFAULT_KEYS, pad: settings.pad ?? DEFAULT_PAD };
  async function rebind(action: Action, kind: "keys" | "pad") {
    setListening({ action, kind });
    const binding = await captureNext(kind);
    setListening(null);
    if (binding) setSettings({ ...settings, [kind]: assign(bindings[kind], action, binding) });
  }
  const cell = (action: Action, kind: "keys" | "pad") => {
    const now = listening?.action === action && listening.kind === kind;
    return (
      <button
        aria-label={`${ACTION_NAMES[action]}: ${kind === "keys" ? "keyboard" : "controller"}`}
        className={now ? "chosen" : ""}
        disabled={Boolean(listening) && !now}
        onClick={() => rebind(action, kind)}
      >
        {now ? (kind === "keys" ? "Press a key…" : "Press a button…") : bindings[kind][action].map((b) => label(kind, b)).join(" / ") || "—"}
      </button>
    );
  };
  return (
    <section className="controls" aria-label="Controllers">
      <h3>Controllers</h3>
      {pads.length ? (
        <ol className="pads">
          {pads.map((name, i) => (
            <li key={i}>{name}</li>
          ))}
        </ol>
      ) : (
        <p className="muted">No controllers detected. Connect one and it appears here.</p>
      )}
      <label className="toggle">
        <input type="checkbox" checked={!settings.padOff} onChange={(e) => setSettings({ ...settings, padOff: !e.target.checked })} />
        Use controllers to move around Quiver
      </label>
      <p className="muted">
        {listening ? "Press what you want to use, or Esc to cancel." : "Pick a binding, then press the key or button to use for it."}
      </p>
      <table className="bindings">
        <thead>
          <tr>
            <th>Action</th>
            <th>Keyboard</th>
            <th>Controller</th>
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
      <button onClick={() => setSettings({ ...settings, keys: undefined, pad: undefined })}>Reset bindings to defaults</button>
    </section>
  );
}
