/**
 * The Settings tab of a port whose own setup file lists settings the player can change (port_config.rs): its rows of
 * choices and sliders, grouped as the file groups them, with the advanced ones behind a button. A change is written
 * to the port's settings file at once, or kept for Play's command line. What the game itself changed shows when it
 * closes.
 */
import { useCallback, useEffect, useRef, useState } from "react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Button, Field } from "@quiverlauncher/ui/controls";
import type { Entry } from "@quiverlauncher/api";
import { native, type PortPick, type PortSetting, type PortSettings } from "./native";
import { setupFileText } from "./port-setup";
import { useLauncher, type Install } from "./store";

/** Advanced options shown, per app, for the session. */
const showingAdvanced = new Set<string>();

/**
 * What the tab shows for an installed app, loaded on its page: null while it's read and when the port has no settings
 * to show. Read again when the app closes, for what it changed itself.
 */
export function usePortSettings(entry: Entry, install: Install | undefined) {
  const { client, config } = useLauncher();
  const [view, setView] = useState<PortSettings | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const where = install ? ([install.folder, install.dir, Boolean(install.wine)] as const) : null;
  const key = JSON.stringify(where);
  const phone = config.os === "android" || config.os === "ios";
  const load = useCallback(async () => {
    if (!where || phone) return setView(null);
    const setupFile = await setupFileText(client, entry.slug);
    setView(await native.portSettings(...where, setupFile).catch(() => null));
  }, [key, entry.slug, phone]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    void load();
    const off = native.onAppClosed((closed) => closed.id === entry.id && void load());
    return () => void off.then((stop) => stop());
  }, [load, entry.id]);
  const change = async (id: string, pick: PortPick) => {
    if (!where) return;
    setProblem(null);
    try {
      const setupFile = await setupFileText(client, entry.slug);
      setView(await native.setPortSetting(...where, setupFile, id, pick));
    } catch (e) {
      setProblem(String(e));
      void load();
    }
  };
  return { view: view?.settings.length || view?.unreadable.length ? view : null, problem, change };
}

export type PortSettingsState = ReturnType<typeof usePortSettings>;

/** The tab itself. */
export function PortSettingsTab({ entry, state }: { entry: Entry; state: PortSettingsState }) {
  const { view, problem, change } = state;
  const [advanced, setAdvanced] = useState(showingAdvanced.has(entry.id));
  if (!view) return null;
  const hidden = view.settings.filter((s) => s.advanced).length;
  const shown = view.settings.filter((s) => advanced || !s.advanced);
  const toggle = () => {
    if (advanced) showingAdvanced.delete(entry.id);
    else showingAdvanced.add(entry.id);
    setAdvanced(!advanced);
  };
  return (
    <section className="app-panel port-settings" aria-label={t({ message: "Settings", comment: "Tab of an app's page: the game's own settings, from its setup file" })}>
      <p className="muted">
        <Trans>These are the game's own settings, as its developer lists them. Changes go into its settings file straight away, and changes you make in the game show here once it closes.</Trans>
      </p>
      {shown.map((s, i) => (
        <div key={s.id}>
          {s.group && s.group !== shown[i - 1]?.group && <h4 className="port-settings-group">{s.group}</h4>}
          <Row setting={s} onChange={(pick) => void change(s.id, pick)} />
        </div>
      ))}
      {hidden > 0 && (
        <Button className="port-settings-advanced" onClick={toggle}>
          {advanced ? <Trans>Hide advanced options</Trans> : <Trans>Show advanced options</Trans>}
        </Button>
      )}
      {view.unreadable.map((path) => (
        <p key={path} className="job-error" role="status">
          <Trans>Quiver couldn't read {path}, so the settings kept there aren't shown.</Trans>
        </p>
      ))}
      {problem && (
        <p className="job-error" role="status">
          {problem}
        </p>
      )}
    </section>
  );
}

function Row({ setting: s, onChange }: { setting: PortSetting; onChange: (pick: PortPick) => void }) {
  const hint = [s.help, s.commandLine ? t`Used when you press Play.` : null, s.enabled ? null : s.needs ? t`Only used when ${s.needs[0]} is ${s.needs[1]}.` : t`Only used with another setting changed.`].filter(Boolean).join(" ") || undefined;
  return (
    <Field label={s.label} hint={hint} className={s.enabled ? "port-setting" : "port-setting port-setting-off"}>
      {s.choices ? (
        <select
          aria-label={s.label}
          disabled={!s.enabled}
          value={s.chosen ?? ""}
          onChange={(e) => e.target.value !== "" && onChange({ choice: Number(e.target.value) })}
        >
          {/* Set in the game's own menu, or by hand: shown as it is until the player picks one. */}
          {s.chosen === null && (
            <option value="" disabled>
              {s.other ? t`Something else (${s.other})` : t`Not set`}
            </option>
          )}
          {s.choices.map((label, i) => (
            <option key={i} value={i}>
              {label}
            </option>
          ))}
        </select>
      ) : (
        s.slider && <SliderControl setting={s} onChange={onChange} />
      )}
    </Field>
  );
}

/** A slider: the number shown as it moves, written once it stops. */
function SliderControl({ setting: s, onChange }: { setting: PortSetting; onChange: (pick: PortPick) => void }) {
  const slider = s.slider!;
  const [moving, setMoving] = useState<number | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => () => clearTimeout(timer.current), []);
  const value = moving ?? slider.value;
  const move = (v: number) => {
    setMoving(v);
    clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      onChange({ value: v });
      setMoving(null);
    }, 400);
  };
  // Decimals as the step has them.
  const places = Math.min(4, (String(slider.step).split(".")[1] ?? "").length);
  const said = value === null ? (s.commandLine ? t`Game's own` : (slider.text ?? t`Not set`)) : moving === null && slider.text && Number(slider.text) === value ? slider.text : value.toFixed(places);
  return (
    <span className="port-slider">
      <input type="range" aria-label={s.label} min={slider.min} max={slider.max} step={slider.step} value={value ?? slider.min} disabled={!s.enabled} onChange={(e) => move(Number(e.target.value))} />
      <output>{said}</output>
    </span>
  );
}
