/**
 * Settings (Menu / Options): every TV mode setting by controller.
 * Up/Down picks a row, Left/Right (or A) changes it; some rows open a screen.
 * Also the console picker, used here (Unassigned games) and by Details.
 */
import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useTv, useInputHandler } from "../context";
import { LAYER } from "../input/router";
import type { TvApp } from "../bridge";
import { tvNative } from "../tvNative";
import type { TvSettings } from "../settings";
import { OTHER } from "../consoles/consoles";
import { useLauncher } from "../../store";
import { native } from "../../native";
import { INTERFACE_SIZES, nearestInterfaceSize } from "../../zoom";
import { resolveConsole } from "../consoles/resolve";

type Option<T> = { value: T; label: string };
type Row =
  | { kind: "choice"; label: string; detail?: string; options: Option<unknown>[]; value: unknown; set: (value: unknown) => void }
  | { kind: "action"; label: string; detail?: string; value?: string; run: () => void };

const onOff: Option<boolean>[] = [
  { value: true, label: "On" },
  { value: false, label: "Off" },
];
const SCALES = INTERFACE_SIZES.map((value) => ({ value, label: `${Math.round(value * 100)}%` }));

/** A list of rows with the focused one's value changeable left and right. */
function useRows(rows: Row[], onBack: () => void, layer: (typeof LAYER)[keyof typeof LAYER] = LAYER.overlay, active = true) {
  const [index, setIndex] = useState(0);
  const row = rows[Math.min(index, rows.length - 1)];
  const step = (by: 1 | -1) => {
    if (row?.kind !== "choice") return;
    const at = row.options.findIndex((o) => o.value === row.value);
    row.set(row.options[(Math.max(0, at) + by + row.options.length) % row.options.length].value);
  };
  useInputHandler(
    {
      onAction(action) {
        if (action === "up") setIndex((i) => (i - 1 + rows.length) % rows.length);
        else if (action === "down") setIndex((i) => (i + 1) % rows.length);
        else if (action === "left") step(-1);
        else if (action === "right") step(1);
        else if (action === "accept") (row?.kind === "action" ? row.run() : step(1));
        else if (action === "back" || action === "menu") onBack();
      },
      hints: () => [
        ...(row?.kind === "choice" ? [{ action: "right" as const, label: "Change" }] : [{ action: "accept" as const, label: "Open" }]),
        { action: "back", label: "Back" },
      ],
    },
    layer,
    active,
  );
  return { index, setIndex, step };
}

function RowList({ rows, index, setIndex, step }: { rows: Row[]; index: number; setIndex: (i: number) => void; step: (by: 1 | -1) => void }) {
  useEffect(() => {
    document.querySelector(".tv-settings-list .focused")?.scrollIntoView({ block: "nearest" });
  }, [index]);
  return (
    <ul className="tv-settings-list">
      {rows.map((row, i) => {
        const focused = i === index;
        const value = row.kind === "choice" ? (row.options.find((o) => o.value === row.value)?.label ?? "") : row.value;
        return (
          <li key={row.label} className={focused ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => (row.kind === "action" ? row.run() : (setIndex(i), step(1)))}>
            <span className="tv-settings-label">
              {row.label}
              {focused && row.detail && <small>{row.detail}</small>}
            </span>
            {row.kind === "choice" ? (
              <span className="tv-settings-value">
                {focused && <ChevronLeft aria-hidden="true" onClick={(e) => (e.stopPropagation(), step(-1))} />}
                {value}
                {focused && <ChevronRight aria-hidden="true" onClick={(e) => (e.stopPropagation(), step(1))} />}
              </span>
            ) : (
              <span className="tv-settings-value">
                {value}
                <ChevronRight aria-hidden="true" />
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * Which console tab a game goes in: the catalog's (Automatic), or one the player picks. The pick is the first of the
 * app's own consoles, as its page in the launcher shows them; No console files it under Apps & Tools or Other.
 */
export function ConsolePicker({ app, onClose }: { app: TvApp; onClose: () => void }) {
  const { consoles } = useTv();
  const { setConsoles } = useLauncher();
  const own = app.ownConsoles;
  const automatic = consoles.get(resolveConsole({ ...app, ownConsoles: undefined })).name;
  const rows: Row[] = useMemo(() => {
    const ids = [...consoles.ids()].map((id) => consoles.get(id)).sort(consoles.compare);
    const chosen = own ?? app.consoles;
    const pick = (consoles: string[] | undefined) => () => {
      setConsoles(app.id, consoles);
      onClose();
    };
    return [
      { kind: "action", label: `Automatic (${automatic})`, value: own ? "" : "✓", run: pick(undefined) },
      ...ids.map((c): Row => ({ kind: "action", label: c.name, value: own?.[0] === c.id ? "✓" : "", run: pick([c.id, ...chosen.filter((id) => id !== c.id)]) })),
      { kind: "action", label: "No console", value: own && !own.length ? "✓" : "", run: pick([]) },
    ];
  }, [consoles, setConsoles, app.id, app.consoles, own, automatic, onClose]);
  const list = useRows(rows, onClose, LAYER.dialog);
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-label={`Console for ${app.title}`}>
      <section className="tv-dialog tv-picker">
        <div className="tv-dialog-body">
          <h2>Console for {app.title}</h2>
          <p className="tv-dialog-lead">Which tab it's in. Automatic follows the catalog.</p>
          <RowList rows={rows} {...list} />
        </div>
      </section>
    </div>
  );
}

/** Games filed under Other (spec §7.2): pick each one's console. */
function Unassigned({ onClose }: { onClose: () => void }) {
  const { bridge } = useTv();
  const [picking, setPicking] = useState<TvApp | null>(null);
  const games = bridge.apps.filter((a) => resolveConsole(a) === OTHER);
  const rows: Row[] = games.map((app) => ({ kind: "action", label: app.title, value: "Other", run: () => setPicking(app) }));
  const list = useRows(rows.length ? rows : [{ kind: "action", label: "Nothing here: every game has a console", run: onClose }], onClose, LAYER.overlay, !picking);
  return (
    <>
      <section className="tv-overlay tv-settings" role="dialog" aria-label="Unassigned games">
        <h2>Unassigned games</h2>
        <p className="tv-settings-note">Games the catalog doesn't place on a console are under Other. Pick their console here.</p>
        <RowList rows={rows.length ? rows : [{ kind: "action", label: "Nothing here: every game has a console", run: onClose }]} {...list} />
      </section>
      {picking && <ConsolePicker app={picking} onClose={() => setPicking(null)} />}
    </>
  );
}

export function Settings({ onClose }: { onClose: () => void }) {
  const { settings: s, setSettings, exit, bridge } = useTv();
  const launcher = useLauncher();
  const [screen, setScreen] = useState<"main" | "unassigned">("main");
  const [monitors, setMonitors] = useState<string[]>([]);
  useEffect(() => void tvNative.monitors().then((m) => setMonitors(Array.isArray(m) ? m : []), () => {}), []);
  const set = <K extends keyof TvSettings>(key: K) => (value: unknown) => setSettings({ [key]: value } as Partial<TvSettings>);
  const unassigned = bridge.apps.filter((a) => resolveConsole(a) === OTHER).length;

  const rows: Row[] = [
    {
      kind: "choice",
      label: "Updates when playing",
      detail: "When a game has an update: ask, play the installed version, or update first",
      options: [
        { value: "ask", label: "Ask" },
        { value: "off", label: "Don't update" },
        { value: "auto", label: "Update first" },
      ],
      value: s.updateBehavior,
      set: set("updateBehavior"),
    },
    {
      kind: "choice",
      label: "After getting a game",
      detail: "Stay in Discover to get more, or go to the game in your library",
      options: [
        { value: "stay", label: "Stay in Discover" },
        { value: "library", label: "Go to it" },
      ],
      value: s.afterGet,
      set: set("afterGet"),
    },
    {
      kind: "choice",
      label: "Offer TV mode when a controller connects",
      detail: "Outside TV mode, a note offers it when you turn a controller on",
      options: onOff,
      value: s.offerOnController,
      set: set("offerOnController"),
    },
    { kind: "choice", label: "Home tab", options: onOff, value: s.showHomeTab, set: set("showHomeTab") },
    {
      kind: "choice",
      label: "Console tab order",
      options: [
        { value: "manufacturerThenYear", label: "Maker, then year" },
        { value: "gameCount", label: "Most games first" },
        { value: "alphabetical", label: "A–Z" },
      ],
      value: s.consoleTabOrder,
      set: set("consoleTabOrder"),
    },
    {
      kind: "choice",
      label: "All tab order",
      options: [
        { value: "name", label: "A–Z" },
        { value: "recent", label: "Recently played" },
      ],
      value: s.allSort,
      set: set("allSort"),
    },
    {
      kind: "choice",
      label: "Size",
      detail: "Everything bigger or smaller, on top of fitting the screen: the launcher's interface size",
      options: SCALES,
      value: nearestInterfaceSize(launcher.settings.scale ?? 1),
      set: (value) => launcher.setSettings((settings) => ({ ...settings, scale: value as number })),
    },
    { kind: "choice", label: "Reduce motion", detail: "No zooming, sliding or sweeping", options: onOff, value: s.reduceMotion, set: set("reduceMotion") },
    {
      kind: "choice",
      label: "Close a game with Start + Select",
      detail: "Hold both for a second while a game runs, for games with no way to quit",
      options: onOff,
      value: s.closeGameCombo,
      set: set("closeGameCombo"),
    },
    ...(bridge.config.os === "linux"
      ? [
          {
            kind: "choice" as const,
            label: "Mouse pointer with Select",
            detail: "While a game runs, hold Select: right stick moves the pointer, RT clicks, LT right-clicks, left stick scrolls",
            options: onOff,
            value: s.pointerWithSelect,
            set: set("pointerWithSelect"),
          },
        ]
      : []),
    {
      kind: "choice",
      label: "Screen",
      detail: "Which monitor TV mode fills",
      options: [{ value: null, label: "Where Quiver is" }, ...monitors.map((m) => ({ value: m, label: m.replace(/^\\\\\.\\/, "") }))],
      value: s.monitor,
      set: (value) => {
        set("monitor")(value);
        void tvNative.useMonitor((value as string | null) ?? null).catch(() => {});
      },
    },
    { kind: "action", label: "Unassigned games", detail: "Give games under Other their console", value: String(unassigned), run: () => setScreen("unassigned") },
    { kind: "action", label: "Switch to desktop mode", detail: "Signing in and accounts are there too", run: exit },
    // Steam's Game Mode has no window buttons: the way out of Quiver, back to Steam.
    ...(launcher.config.gameMode ? [{ kind: "action" as const, label: "Close Quiver", detail: "Back to Steam", run: () => void native.closeWindow() }] : []),
  ];
  const list = useRows(rows, onClose, LAYER.overlay, screen === "main");

  return (
    <>
      <section className="tv-overlay tv-settings" role="dialog" aria-label="Settings">
        <h2>Settings</h2>
        <RowList rows={rows} {...list} />
      </section>
      {screen === "unassigned" && <Unassigned onClose={() => setScreen("main")} />}
    </>
  );
}
