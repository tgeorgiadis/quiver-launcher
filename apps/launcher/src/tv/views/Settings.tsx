/**
 * Settings (Menu / Options): every TV mode setting by controller.
 * Up/Down picks a row, Left/Right (or A) changes it; some rows open a screen.
 * Also the console picker, used here (Unassigned games) and by Details.
 */
import { useEffect, useMemo, useRef, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { i18n } from "@quiverlauncher/i18n";
import { useTv, useInputHandler } from "../context";
import { LAYER } from "../input/router";
import type { TvApp } from "../bridge";
import { tvNative } from "../tvNative";
import { TV_SORTS, type TvSettings } from "../settings";
import { sortName } from "./SortList";
import { OTHER } from "../consoles/consoles";
import { useLauncher } from "../../store";
import { native } from "../../native";
import { CLOSE_COMBOS } from "../../controls";
import { usePadLabels } from "../../pad-hints";
import { label as padLabel } from "../../spatial";
import { INTERFACE_SIZES, nearestInterfaceSize } from "../../zoom";
import { resolveConsole } from "../consoles/resolve";

type Option<T> = { value: T; label: string };
export type Row =
  | { kind: "choice"; label: string; detail?: string; options: Option<unknown>[]; value: unknown; set: (value: unknown) => void }
  | { kind: "action"; label: string; detail?: string; value?: string; run: () => void };

/** A setting's On and Off, in the language shown. */
const onOff = (): Option<boolean>[] => [
  { value: true, label: t({ message: "On", comment: "A TV mode setting's value: turned on" }) },
  { value: false, label: t({ message: "Off", comment: "A TV mode setting's value: turned off" }) },
];
/** The launcher's interface sizes, as percentages in the language shown. */
const scales = () => {
  const percent = new Intl.NumberFormat(i18n.locale, { style: "percent" });
  return INTERFACE_SIZES.map((value) => ({ value, label: percent.format(value) }));
};

/** A list of rows with the focused one's value changeable left and right; `accept`, A's hint on an action's row, if not Open. */
export function useRows(rows: Row[], onBack: () => void, layer: (typeof LAYER)[keyof typeof LAYER] = LAYER.overlay, active = true, accept?: string) {
  const [index, showIndex] = useState(0);
  // The focus and each choice's value as of the last press, not the last render: presses quicker than a render (a held
  // direction, or two buttons at once) each act on what the one before left.
  const at = useRef(0);
  const values = useRef(new Map<string, unknown>());
  for (const r of rows) if (r.kind === "choice") values.current.set(r.label, r.value);
  const setIndex = (i: number) => showIndex((at.current = i));
  const row = rows[Math.min(index, rows.length - 1)];
  /** Moves a choice's value along: the focused row's, or the one clicked. */
  const step = (by: 1 | -1, which: Row | undefined = rows[Math.min(at.current, rows.length - 1)]) => {
    if (which?.kind !== "choice") return;
    const now = values.current.get(which.label);
    const from = which.options.findIndex((o) => o.value === now);
    const next = which.options[(Math.max(0, from) + by + which.options.length) % which.options.length].value;
    values.current.set(which.label, next);
    which.set(next);
  };
  useInputHandler(
    {
      onAction(action) {
        const focused = rows[Math.min(at.current, rows.length - 1)];
        if (action === "up") setIndex((at.current - 1 + rows.length) % rows.length);
        else if (action === "down") setIndex((at.current + 1) % rows.length);
        else if (action === "left") step(-1);
        else if (action === "right") step(1);
        else if (action === "accept") (focused?.kind === "action" ? focused.run() : step(1));
        else if (action === "back" || action === "menu") onBack();
      },
      hints: () => [
        ...(row?.kind === "choice"
          ? [{ action: "right" as const, label: t({ message: "Change", comment: "Controller hint in TV mode's settings: changes the focused setting's value" }) }]
          : [{ action: "accept" as const, label: accept ?? t({ message: "Open", comment: "Controller hint in TV mode's settings: opens or does the focused item" }) }]),
        { action: "back", label: t({ message: "Back", comment: "Controller hint: what Back does, closing or going back" }) },
      ],
    },
    layer,
    active,
  );
  return { index, setIndex, step };
}

export function RowList({ rows, index, setIndex, step }: { rows: Row[]; index: number; setIndex: (i: number) => void; step: (by: 1 | -1, which?: Row) => void }) {
  useEffect(() => {
    document.querySelector(".tv-settings-list .focused")?.scrollIntoView({ block: "nearest" });
  }, [index]);
  return (
    <ul className="tv-settings-list">
      {rows.map((row, i) => {
        const focused = i === index;
        const value = row.kind === "choice" ? (row.options.find((o) => o.value === row.value)?.label ?? "") : row.value;
        return (
          <li key={row.label} className={focused ? "focused" : ""} onMouseMove={() => setIndex(i)} onClick={() => (row.kind === "action" ? row.run() : (setIndex(i), step(1, row)))}>
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
      { kind: "action", label: t({ message: `Automatic (${automatic})`, comment: "A game's console in TV mode: the catalog's, named in brackets" }), value: own ? "" : "✓", run: pick(undefined) },
      ...ids.map((c): Row => ({ kind: "action", label: c.name, value: own?.[0] === c.id ? "✓" : "", run: pick([c.id, ...chosen.filter((id) => id !== c.id)]) })),
      { kind: "action", label: t({ message: "No console", comment: "A game's console in TV mode: none, so it's under Apps & Tools or Other" }), value: own && !own.length ? "✓" : "", run: pick([]) },
    ];
  }, [consoles, setConsoles, app.id, app.consoles, own, automatic, onClose]);
  const list = useRows(rows, onClose, LAYER.dialog);
  const name = app.title;
  const heading = t`Console for ${name}`;
  return (
    <div className="tv-dialog-backdrop" role="dialog" aria-label={heading}>
      <section className="tv-dialog tv-picker">
        <div className="tv-dialog-body">
          <h2>{heading}</h2>
          <p className="tv-dialog-lead">
            <Trans>Which tab it's in. Automatic follows the catalog.</Trans>
          </p>
          <RowList rows={rows} {...list} />
        </div>
      </section>
    </div>
  );
}

/** Games filed under Other: pick each one's console. */
function Unassigned({ onClose }: { onClose: () => void }) {
  const { bridge, consoles } = useTv();
  const [picking, setPicking] = useState<TvApp | null>(null);
  const games = bridge.apps.filter((a) => resolveConsole(a) === OTHER);
  const other = consoles.get(OTHER).name;
  const rows: Row[] = games.map((app) => ({ kind: "action", label: app.title, value: other, run: () => setPicking(app) }));
  const shown: Row[] = rows.length ? rows : [{ kind: "action", label: t`Nothing here: every game has a console`, run: onClose }];
  const list = useRows(shown, onClose, LAYER.overlay, !picking);
  const title = t`Unassigned games`;
  return (
    <>
      <section className="tv-overlay tv-settings" role="dialog" aria-label={title}>
        <h2>{title}</h2>
        <p className="tv-settings-note">
          <Trans>Games the catalog doesn't place on a console are under Other. Pick their console here.</Trans>
        </p>
        <RowList rows={shown} {...list} />
      </section>
      {picking && <ConsolePicker app={picking} onClose={() => setPicking(null)} />}
    </>
  );
}

export function Settings({ onClose }: { onClose: () => void }) {
  const { settings: s, setSettings, exit, bridge } = useTv();
  const launcher = useLauncher();
  const labels = usePadLabels();
  const [screen, setScreen] = useState<"main" | "unassigned">("main");
  const [monitors, setMonitors] = useState<string[]>([]);
  useEffect(() => void tvNative.monitors().then((m) => setMonitors(Array.isArray(m) ? m : []), () => {}), []);
  const set = <K extends keyof TvSettings>(key: K) => (value: unknown) => setSettings({ [key]: value } as Partial<TvSettings>);
  const unassigned = bridge.apps.filter((a) => resolveConsole(a) === OTHER).length;

  const onOffOptions = onOff();
  const aToZ = t({ message: "A–Z", comment: "TV mode's order of tabs or games: by name" });
  const rows: Row[] = [
    {
      kind: "choice",
      label: t`Updates when playing`,
      detail: t`When a game has an update: ask, play the installed version, or update first`,
      options: [
        { value: "ask", label: t({ message: "Ask", comment: "TV mode setting, Updates when playing: ask whether to update a game before it starts" }) },
        { value: "off", label: t({ message: "Don't update", comment: "TV mode setting, Updates when playing: play the version installed" }) },
        { value: "auto", label: t({ message: "Update first", comment: "TV mode setting, Updates when playing: update a game, then start it" }) },
      ],
      value: s.updateBehavior,
      set: set("updateBehavior"),
    },
    {
      kind: "choice",
      label: t`After getting a game`,
      detail: t`Stay in Discover to get more, or go to the game in your library`,
      options: [
        { value: "stay", label: t({ message: "Stay in Discover", comment: "TV mode setting, After getting a game" }) },
        { value: "library", label: t({ message: "Go to it", comment: "TV mode setting, After getting a game: go to the game in the library" }) },
      ],
      value: s.afterGet,
      set: set("afterGet"),
    },
    {
      kind: "choice",
      label: t`Offer TV mode when a controller connects`,
      detail: t`Outside TV mode, a note offers it when you turn a controller on`,
      options: onOffOptions,
      value: s.offerOnController,
      set: set("offerOnController"),
    },
    { kind: "choice", label: t({ message: "Home tab", comment: "TV mode setting: whether the Home tab shows" }), options: onOffOptions, value: s.showHomeTab, set: set("showHomeTab") },
    {
      kind: "choice",
      label: t`Console tab order`,
      options: [
        { value: "manufacturerThenYear", label: t({ message: "Maker, then year", comment: "TV mode's order of console tabs: by maker, then by when each came out" }) },
        { value: "gameCount", label: t({ message: "Most games first", comment: "TV mode's order of console tabs" }) },
        { value: "alphabetical", label: aToZ },
      ],
      value: s.consoleTabOrder,
      set: set("consoleTabOrder"),
    },
    {
      kind: "choice",
      label: t({ message: "Sort", comment: "Label of the library's sort order" }),
      detail: t`The order of the games on All and each console's tab; X there changes it too`,
      options: TV_SORTS.map((sort) => ({ value: sort, label: sortName(sort) })),
      value: s.sort,
      set: set("sort"),
    },
    {
      kind: "choice",
      label: t({ message: "Size", comment: "TV mode setting: the launcher's interface size" }),
      detail: t`Everything bigger or smaller, on top of fitting the screen: the launcher's interface size`,
      options: scales(),
      value: nearestInterfaceSize(launcher.settings.scale ?? 1),
      set: (value) => launcher.setSettings((settings) => ({ ...settings, scale: value as number })),
    },
    {
      kind: "choice",
      label: t`Reduce motion`,
      detail: t`No zooming, sliding or sweeping`,
      options: onOffOptions,
      value: s.reduceMotion,
      set: set("reduceMotion"),
    },
    {
      kind: "choice",
      label: t({ message: "Sounds", comment: "TV mode setting: sounds as the controller moves and chooses" }),
      detail: t`A soft click as you move, choose and go back`,
      options: onOffOptions,
      value: s.sounds,
      set: set("sounds"),
    },
    {
      // The launcher's setting (Settings → Controllers): off, or the buttons that close a game.
      kind: "choice",
      label: t`Close a game from the controller`,
      detail: t`Hold these buttons together for a second while a game runs, for games with no way to quit`,
      options: [
        onOffOptions[1],
        ...CLOSE_COMBOS.map((buttons) => ({ value: buttons.join("+"), label: buttons.map((b) => padLabel("pad", b, labels)).join(" + ") })),
      ],
      value: launcher.settings.closeComboOff ? false : (launcher.settings.closeCombo ?? CLOSE_COMBOS[0]).join("+"),
      set: (value) =>
        launcher.setSettings((settings) =>
          value === false
            ? { ...settings, closeComboOff: true }
            : { ...settings, closeComboOff: false, closeCombo: value === CLOSE_COMBOS[0].join("+") ? undefined : String(value).split("+") },
        ),
    },
    ...(bridge.config.os === "linux"
      ? [
          {
            kind: "choice" as const,
            label: t`Mouse pointer with Select`,
            detail: t`While a game runs, hold Select: right stick moves the pointer, RT clicks, LT right-clicks, left stick scrolls`,
            options: onOffOptions,
            value: Boolean(launcher.settings.selectPointer),
            set: (value: unknown) => launcher.setSettings((settings) => ({ ...settings, selectPointer: value ? true : undefined })),
          },
        ]
      : []),
    {
      kind: "choice",
      label: t({ message: "Screen", comment: "TV mode setting: the monitor it fills" }),
      detail: t`Which monitor TV mode fills`,
      options: [
        { value: null, label: t({ message: "Where Quiver is", comment: "TV mode setting, Screen: the monitor Quiver's window is on" }) },
        ...monitors.map((m) => ({ value: m, label: m.replace(/^\\\\\.\\/, "") })),
      ],
      value: s.monitor,
      set: (value) => {
        set("monitor")(value);
        void tvNative.useMonitor((value as string | null) ?? null).catch(() => {});
      },
    },
    { kind: "action", label: t`Unassigned games`, detail: t`Give games under Other their console`, value: String(unassigned), run: () => setScreen("unassigned") },
    { kind: "action", label: t`Switch to desktop mode`, detail: t`Signing in and accounts are there too`, run: exit },
    // Steam's Game Mode has no window buttons: the way out of Quiver, back to Steam.
    ...(launcher.config.gameMode
      ? [{ kind: "action" as const, label: t`Close Quiver`, detail: t`Back to Steam`, run: () => void native.closeWindow() }]
      : []),
  ];
  const list = useRows(rows, onClose, LAYER.overlay, screen === "main");
  const title = t`Settings`;

  return (
    <>
      <section className="tv-overlay tv-settings" role="dialog" aria-label={title}>
        <h2>{title}</h2>
        <RowList rows={rows} {...list} />
      </section>
      {screen === "unassigned" && <Unassigned onClose={() => setScreen("main")} />}
    </>
  );
}
