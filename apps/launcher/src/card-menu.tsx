/**
 * An app's menu, laid out like Steam's library menu: its main action (Play,
 * Get, Try again) first, its update, then "Play with" (which of its programs
 * Play starts), "Add to" its playlists and "Manage" (its
 * folder, shortcuts, hiding and uninstalling) to the side, and its page.
 * A library card has it behind its ⋯ button; a card or a sidebar row opens it
 * on a right-click, a long press on a touch screen, the Menu key or Shift+F10,
 * or a controller's Options button.
 */
import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent as KeyEvent, type PointerEvent as PointerEv, type ReactNode } from "react";
import { Check, ChevronRight, Download, MoreHorizontal, Play } from "lucide-react";
import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { canGet, hasUpdate, pulledVerified, renamable, useLauncher } from "./store";
import { Popover, newKey } from "./library";
import { foldersOn, shortcutRequest, shortcutsOn } from "./detail";
import { versionLabel } from "./versions";
import { isLocal } from "./custom";
import { native } from "./native";
import { track } from "./telemetry";
import { boxZoom, windowBox, zoom } from "./zoom";

/** How long a finger stays down before it's a long press, in ms. */
const LONG_PRESS = 500;

/** At the top of a library card, opening below its ⋯ button; the card around it opens it too. */
export function CardMenu({ entry, onOpen }: { entry: Entry; onOpen: () => void }) {
  const anchor = useRef<HTMLSpanElement>(null);
  return (
    <span ref={anchor} className="card-menu">
      <GameMenu entry={entry} onOpen={onOpen} target={() => anchor.current?.closest("article") ?? null} where="card" />
    </span>
  );
}

/**
 * An app's menu over `target` (a card, a sidebar row), which opens it. With
 * `bare`, there's no ⋯ button: closing it puts focus back on what had it.
 */
export function GameMenu({ entry, onOpen, target, bare, where }: { entry: Entry; onOpen: () => void; target: () => Element | null; bare?: boolean; where: "card" | "sidebar" }) {
  const opener = useRef<((x: number, y: number) => void) | null>(null);
  const escape = useRef<(() => boolean) | null>(null);
  useEffect(() => {
    const card = target();
    if (!card) return;
    // Where a controller's Options button looks for a menu (spatial.ts).
    card.setAttribute("data-menu", "");
    let pressed = false;
    const open = (x: number, y: number, how: string) => {
      opener.current?.(x, y);
      track("card_menu_opened", { how, where });
    };
    // A point on the screen, as a right-click's is (zoom.ts).
    const at = (el: Element) => {
      const r = el.getBoundingClientRect();
      const s = zoom() / boxZoom();
      return [(r.left + 12) * s, (r.top + 12) * s] as const;
    };
    const contextmenu = (e: Event) => {
      const m = e as MouseEvent;
      m.preventDefault();
      // The keyboard's Menu key fires it at the focused control, not the pointer.
      if (m.button === 0 && m.clientX === 0 && m.clientY === 0) return open(...at(card), "key");
      // Some systems follow a right-click with a click: it doesn't open the app too.
      pressed = true;
      open(m.clientX, m.clientY, "right-click");
    };
    const keydown = (e: Event) => {
      const k = e as KeyboardEvent;
      if (k.key === "ContextMenu" || (k.key === "F10" && k.shiftKey)) {
        k.preventDefault();
        open(...at(document.activeElement && card.contains(document.activeElement) ? document.activeElement : card), "key");
      }
    };
    // A long press opens the menu; the tap that ends it neither opens the app nor, as a click outside, closes the menu.
    // Until the next press, `pressed` holds back that tap.
    let timer: ReturnType<typeof setTimeout> | undefined;
    let start: [number, number] | null = null;
    const anew = () => (pressed = false);
    const swallow = (e: MouseEvent) => pressed && e.stopPropagation();
    const down = (e: Event) => {
      const p = e as PointerEvent;
      if (p.pointerType !== "touch") return;
      start = [p.clientX, p.clientY];
      clearTimeout(timer);
      timer = setTimeout(() => {
        pressed = true;
        open(start![0], start![1], "long-press");
      }, LONG_PRESS);
    };
    const move = (e: Event) => {
      const p = e as PointerEvent;
      if (start && Math.hypot(p.clientX - start[0], p.clientY - start[1]) > 10) clearTimeout(timer);
    };
    const up = () => clearTimeout(timer);
    const click = (e: Event) => {
      if (!pressed) return;
      pressed = false;
      e.preventDefault();
      e.stopPropagation();
    };
    card.addEventListener("contextmenu", contextmenu);
    card.addEventListener("keydown", keydown);
    card.addEventListener("pointerdown", down);
    card.addEventListener("pointermove", move);
    card.addEventListener("pointerup", up);
    card.addEventListener("pointercancel", up);
    card.addEventListener("click", click, true);
    window.addEventListener("pointerdown", anew, true);
    window.addEventListener("mousedown", swallow, true);
    return () => {
      window.removeEventListener("pointerdown", anew, true);
      window.removeEventListener("mousedown", swallow, true);
      clearTimeout(timer);
      card.removeAttribute("data-menu");
      card.removeEventListener("contextmenu", contextmenu);
      card.removeEventListener("keydown", keydown);
      card.removeEventListener("pointerdown", down);
      card.removeEventListener("pointermove", move);
      card.removeEventListener("pointerup", up);
      card.removeEventListener("pointercancel", up);
      card.removeEventListener("click", click, true);
    };
  }, []);
  const name = entry.projectName;
  return (
    <Popover label={t({ message: `${name} menu`, comment: "An app's menu, named for the app" })} icon={<MoreHorizontal size={16} />} bare={bare} role="menu" opener={opener} escape={escape} align="end" panel="card-menu-panel">
      {(close) => <MenuItems entry={entry} onOpen={onOpen} close={close} escape={escape} />}
    </Popover>
  );
}

/** `icon`: the main action's, as the app's page has it. */
type Item = { text: string; act: () => void; kind?: "danger"; disabled?: boolean; icon?: "play" | "get" };
type Sub = { key: "programs" | "add" | "manage"; by: "hover" | "click"; focus?: boolean };

/** A program's file name, without the folders it's in. */
export const fileName = (path: string) => path.split(/[/\\]/).pop() || path;

/** The menu's items, made anew each time it opens. */
function MenuItems({ entry, onOpen, close, escape }: { entry: Entry; onOpen: () => void; close: () => void; escape: { current: (() => boolean) | null } }) {
  const { config, installs, jobs, catalog, collections, settings, setSettings, get, play, reinstall, cancel, dismiss, confirmUninstall, askRename, changeVersion, remove, saveCollection, setNotice, setLaunch } = useLauncher();
  const [sub, setSub] = useState<Sub | null>(null);
  // Where the open submenu sits: beside the menu, or under its item where neither side has room (a phone).
  const [place, setPlace] = useState<{ key: Sub["key"]; side: "right" | "left" | "below"; top: number }>();
  const [name, setName] = useState("");
  // The programs in the app's folder, looked for as Play with opens.
  const [found, setFound] = useState<string[] | null>(null);
  const root = useRef<HTMLDivElement>(null);
  const subMenu = useRef<HTMLDivElement>(null);
  const pending = useRef<ReturnType<typeof setTimeout>>(undefined);
  useEffect(() => () => clearTimeout(pending.current), []);
  const trigger = (key: Sub["key"]) => root.current?.querySelector<HTMLElement>(`[data-sub="${key}"]`);
  // Escape and Back close an open submenu first, its item keeping focus.
  escape.current = () => {
    if (!sub) return false;
    trigger(sub.key)?.focus();
    setSub(null);
    return true;
  };
  useLayoutEffect(() => {
    const el = subMenu.current;
    const panel = el?.offsetParent;
    const item = sub && trigger(sub.key);
    if (!el || !panel || !item) return;
    if (place?.key !== sub.key) {
      // The interface size zooms the page; the submenu's place is set in unzoomed pixels (zoom.ts).
      const [z, box] = [boxZoom(), windowBox()];
      const p = panel.getBoundingClientRect();
      const width = el.offsetWidth * z + 8;
      const side = box.width - p.right >= width ? "right" : p.left >= width ? "left" : "below";
      // Level with its item (less the menu's padding and border), kept above the bottom of the window.
      const top = Math.max(8 - p.top / z, Math.min(item.offsetTop - 7, (box.height - p.top) / z - el.offsetHeight - 8));
      setPlace({ key: sub.key, side, top });
    }
    // Once it shows: a hidden control can't take focus.
    else if (sub.focus) {
      el.querySelector<HTMLElement>("[data-item]")?.focus();
      setSub({ ...sub, focus: false });
    }
  }, [sub, place]);

  const install = installs[entry.id];
  // Play with: for an app in its own folder, not one the player added from where it is, or one its Windows installer put elsewhere.
  const programsOn = Boolean(install && !install.program && install.installer !== "msi" && foldersOn(config.os));
  useEffect(() => {
    if (sub?.key !== "programs" || found || !install) return;
    native.listPrograms(install.folder, install.dir).then(setFound, () => setFound([]));
  }, [sub?.key]); // eslint-disable-line react-hooks/exhaustive-deps
  const job = jobs[entry.id];
  const failed = job && "error" in job;
  const busy = job && !failed;
  const latest = catalog[entry.id] ?? entry;
  const hidden = settings.hidden?.includes(entry.id);
  const shortcut = (make: typeof native.createShortcut) =>
    make(shortcutRequest(entry, install!)).then(
      (text) => setNotice(text),
      (e) => setNotice(String(e)),
    );

  // The main action, as the app's page has it.
  const percent = busy && job.total ? Math.round((job.received / job.total) * 100) : null;
  const main: Item | undefined = failed
    ? // A local app has nothing to download: Try again plays it again.
      { text: t`Try again`, act: () => (install?.local ? (dismiss(entry.id), void play(entry.id)) : void get(entry)) }
    : busy
      ? { text: job.phase === "installing" ? t`Installing…` : percent === null ? t`Downloading…` : t`Downloading ${percent}%`, act: () => {}, disabled: true }
      : install
        ? { text: t({ message: "Play", comment: "Menu item that starts an installed app" }), act: () => void play(entry.id), icon: "play" }
        : canGet(entry, config.os)
          ? // Its verified release was withdrawn: the app's page has its versions.
            pulledVerified(latest)
            ? { text: t`Choose a version…`, act: onOpen }
            : { text: t({ message: "Get", comment: "Menu item that installs an app" }), act: () => void get(entry), icon: "get" }
          : undefined;
  const top: Item[] = [];
  // Once its files are going in, an install finishes: stopping then could leave the app half there.
  if (busy && job.phase !== "installing") top.push({ text: t`Cancel download`, act: () => cancel(entry.id) });
  // Its file was gone or not the one Quiver checked: the newest other verified release, in its place.
  const instead = job && "error" in job ? job.instead : undefined;
  const other = versionLabel(instead?.version);
  if (instead) top.push({ text: t`Install ${other} instead`, act: () => void get(latest, instead) });
  if (failed) top.push({ text: t({ message: "Dismiss", comment: "Menu item that clears a failed download" }), act: () => dismiss(entry.id) });
  const newer = versionLabel(latest.verified?.version);
  if (install && !job && hasUpdate(latest, install)) top.push({ text: t`Update to ${newer}`, act: () => void get(latest) });

  const manage: Item[] = [];
  if (install && foldersOn(config.os)) manage.push({ text: t`Open folder`, act: () => void native.openFolder(entry.id).catch((e) => setNotice(String(e))) });
  if (renamable(install) && foldersOn(config.os) && !busy) manage.push({ text: t`Rename folder…`, act: () => askRename(entry) });
  if (install && shortcutsOn(config.os))
    manage.push({ text: t`Desktop shortcut`, act: () => void shortcut(native.createShortcut) }, { text: t`Add to Steam`, act: () => void shortcut(native.addToSteam) });
  if (install?.installer === "msi" && !busy) manage.push({ text: t`Run installer again`, act: () => void reinstall(entry.id) });
  // Every release, verified or not, as the app's page has them: the player's own files have none.
  if (!isLocal(entry.id) && !install?.local && !busy) manage.push({ text: install ? t`Change version…` : t`Choose a version…`, act: () => changeVersion(entry) });
  manage.push({
    text: hidden ? t`Show in library` : t`Hide from library`,
    act: () => setSettings({ ...settings, hidden: hidden ? settings.hidden!.filter((h) => h !== entry.id) : [...(settings.hidden ?? []), entry.id] }),
  });
  // After a line: both ask before deleting the files; taking an app out of the library alone doesn't.
  const leave: Item[] = [];
  if (install && !install.local && !busy) leave.push({ text: t`Uninstall`, act: () => confirmUninstall({ entry }), kind: "danger" });
  if (install && !install.local) leave.push({ text: t`Uninstall and remove`, act: () => confirmUninstall({ entry, remove: true }), kind: "danger" });
  else leave.push({ text: t`Remove from library`, act: () => void remove(entry.id), kind: "danger" });

  const picked = install?.launch?.programs ?? [];
  const pick = (programs: string[]) => install && setLaunch(entry.id, { ...install.launch, programs: programs.length ? programs : undefined });
  const togglePlay = (program: string) => pick(picked.includes(program) ? picked.filter((p) => p !== program) : [...picked, program]);
  const chooseAnother = () =>
    install &&
    native.pickExecutable(install.folder, install.dir).then(
      (path) => path && !picked.includes(path) && pick([...picked, path]),
      (e) => setNotice(String(e)),
    );

  const playlists = collections.filter((c) => !c.follows);
  const on = (key: string) => Boolean(playlists.find((c) => c.key === key)?.apps?.includes(entry.id));
  const toggle = (key: string) => {
    const c = playlists.find((p) => p.key === key)!;
    saveCollection({ ...c, apps: on(key) ? c.apps!.filter((a) => a !== entry.id) : [...(c.apps ?? []), entry.id] });
  };
  const addNew = () => {
    if (!name.trim()) return;
    saveCollection({ key: newKey(), name: name.trim().slice(0, 60), tags: [], consoles: [], apps: [entry.id], order: collections.length });
    setName("");
  };

  // The mouse opens a submenu by moving onto its item, and moving onto another item of the menu closes it.
  // With one open, that waits until the pointer rests a moment (a short one, so a submenu still opens quickly): it can cross other items on its way into it.
  // Only a moving pointer counts: a menu opening under one that's resting (a right-click, a key, a long press) opens no submenu by itself, so Escape closes the menu.
  const hover = (key?: Sub["key"]) => (e: PointerEv) => {
    if (e.pointerType !== "mouse") return;
    clearTimeout(pending.current);
    const next = () => setSub((s) => (key ? (s?.key === key ? s : { key, by: "hover" }) : null));
    if (sub) pending.current = setTimeout(next, 120);
    else next();
  };
  const choose = (act: () => void) => () => (close(), act());
  const item = ({ text, act, kind, disabled }: Item, inSub = false): ReactNode => (
    <button key={text} type="button" role="menuitem" data-item className={kind} disabled={disabled} onClick={choose(act)} onPointerMove={inSub ? undefined : hover()}>
      {text}
    </button>
  );
  const opens = (key: Sub["key"], text: string) => (
    <button
      type="button"
      role="menuitem"
      data-item
      data-sub={key}
      aria-haspopup="menu"
      aria-expanded={sub?.key === key}
      // A click opens it; a second one, or Confirm on a controller, closes it. Opened from a key or a controller, its first item takes focus.
      onClick={(e) => (sub?.key === key && sub.by === "click" ? setSub(null) : setSub({ key, by: "click", focus: e.detail === 0 }))}
      onPointerMove={hover(key)}
    >
      {text}
      <ChevronRight size={14} className="game-menu-arrow" aria-hidden="true" />
    </button>
  );
  const submenu = (key: Sub["key"], label: string, children: ReactNode, className = "") =>
    sub?.key === key && (
      <div
        ref={subMenu}
        className={`game-submenu ${place?.key === key ? place.side : "right"} ${className}`}
        role="menu"
        aria-label={label}
        style={place?.key === key ? (place.side === "below" ? undefined : { top: place.top }) : { visibility: "hidden" }}
        onPointerEnter={() => clearTimeout(pending.current)}
      >
        {children}
      </div>
    );

  // Up and down go through the items in the list the focus is in, round again at the end; right goes into a submenu, left back out.
  const keys = (e: KeyEvent<HTMLDivElement>) => {
    const from = e.target as HTMLElement;
    const list = from.closest<HTMLElement>(".game-submenu, .game-menu");
    if (!list) return;
    const items = [...list.querySelectorAll<HTMLElement>(":scope > [data-item]:not(:disabled)")];
    const inSub = list.classList.contains("game-submenu");
    const typing = from instanceof HTMLInputElement;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const i = items.indexOf(from);
      items[(i + (e.key === "ArrowDown" ? 1 : items.length - 1)) % items.length]?.focus();
      if (!inSub) setSub(null);
    } else if (e.key === "ArrowRight" && !typing && from.dataset.sub) {
      e.preventDefault();
      setSub({ key: from.dataset.sub as Sub["key"], by: "click", focus: true });
    } else if (e.key === "ArrowLeft" && !typing && inSub) {
      e.preventDefault();
      escape.current?.();
    }
  };

  return (
    <div ref={root} className="game-menu" data-arrows="" onKeyDown={keys}>
      {main && (
        <button type="button" role="menuitem" data-item className="game-menu-main" disabled={main.disabled} onClick={choose(main.act)} onPointerMove={hover()}>
          {/* As the app's page has it. */}
          {main.icon === "play" && <Play size={15} aria-hidden="true" />}
          {main.icon === "get" && <Download size={15} aria-hidden="true" />}
          {main.text}
        </button>
      )}
      {top.map((i) => item(i))}
      {programsOn && opens("programs", t`Play with`)}
      {submenu(
        "programs",
        t`Play with`,
        <>
          {/* Ticked, Play starts it; with more than one ticked, Play asks which. */}
          {found === null ? (
            <button type="button" role="menuitem" data-item disabled>
              <Trans>Looking for programs…</Trans>
            </button>
          ) : (
            [...found, ...picked.filter((p) => !found.includes(p))].map((p) => (
              <button key={p} type="button" role="menuitemcheckbox" data-item aria-checked={picked.includes(p)} title={p} onClick={() => togglePlay(p)}>
                <span className="game-menu-check">{picked.includes(p) && <Check size={14} />}</span>
                {fileName(p)}
                {p !== fileName(p) && <span className="game-menu-path">{p.slice(0, -fileName(p).length - 1)}</span>}
              </button>
            ))
          )}
          {found?.length === 0 && picked.length === 0 && (
            <p className="game-menu-note">
              <Trans>No programs found in its folder.</Trans>
            </p>
          )}
          <hr />
          {item({ text: t`Choose another…`, act: () => void chooseAnother() }, true)}
          {picked.length > 0 && item({ text: t`Let Quiver choose`, act: () => pick([]) }, true)}
        </>,
      )}
      {opens("add", t({ message: "Add to", comment: "Menu item opening the playlists to add an app to" }))}
      {submenu(
        "add",
        t({ message: "Add to", comment: "Menu item opening the playlists to add an app to" }),
        <>
          {playlists.map((c) => (
            <button key={c.key} type="button" role="menuitemcheckbox" data-item aria-checked={on(c.key)} onClick={() => toggle(c.key)}>
              <span className="game-menu-check">{on(c.key) && <Check size={14} />}</span>
              {c.name}
            </button>
          ))}
          <input
            data-item
            className="game-menu-new"
            aria-label={t`New playlist`}
            placeholder={t`+ New playlist…`}
            maxLength={60}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), addNew())}
          />
        </>,
      )}
      {opens("manage", t({ message: "Manage", comment: "Menu item opening an app's folder, shortcuts and uninstalling" }))}
      {submenu(
        "manage",
        t({ message: "Manage", comment: "Menu item opening an app's folder, shortcuts and uninstalling" }),
        <>
          {manage.map((i) => item(i, true))}
          <hr />
          {leave.map((i) => item(i, true))}
        </>,
      )}
      <hr />
      {item({ text: t`App page`, act: onOpen })}
    </div>
  );
}
