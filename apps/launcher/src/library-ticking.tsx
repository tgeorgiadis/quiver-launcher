/**
 * Selecting many library apps and changing them all at once, in every layout: the grid with any picture, a game's
 * card (all its ports), the Sidebar layout's list, and TV mode's grid.
 *
 * Ctrl+click (Cmd on a Mac) selects a card or a row, and Shift+click every one from the one clicked last, as in a
 * file manager; an app's menu has Select too, for a touch screen and a controller. While any are selected, a click
 * or Confirm selects or unselects instead of opening or playing, a right-click or Options opens the selected apps'
 * menu, Ctrl+A selects everything showing, and Escape or Back stops. A bar along the foot says how many and has the
 * changes to make: playlists, tags, hiding, updates, installing and uninstalling, each made with what one app's menu
 * changes for one app (ticking.ts), so there's one way of changing an app.
 *
 * Cards and rows say which apps they are with `data-tick` (a game's card: its ports in the library, space apart);
 * the page takes their clicks first, so they need nothing more than `useTicked` to show a tick.
 */
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { Check, ChevronDown, EyeOff, ListPlus, Minus, MoreHorizontal, Tag, Trash2, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { PORTABLE_FILE, canGet, hasUpdate, pulledVerified, updatesByItself, useLauncher, type Install } from "./store";
import { Dialog, Popover, newKey } from "./library";
import { menuArrows } from "./card-menu";
import { tickRange, tickState, tickToggled, withIds, withOwnTag, type TickState } from "./ticking";
import { native } from "./native";
import { track } from "./telemetry";
import { zoom } from "./zoom";

type Tick = { ticked: ReadonlySet<string>; toggle: (ids: string[]) => void };
const TickContext = createContext<Tick | null>(null);

/** Whether the library is selecting, and whether these apps (a card's, a game card's ports) are selected; null outside the library. */
export function useTicked(ids: readonly string[]): { ticking: boolean; ticked: boolean } {
  const tick = useContext(TickContext);
  return { ticking: Boolean(tick?.ticked.size), ticked: Boolean(tick && ids.length && ids.every((id) => tick.ticked.has(id))) };
}

/** Selects an app, or unselects it, from its menu: none outside the library. */
export const useTickToggle = () => useContext(TickContext)?.toggle;

/** What's selecting and not: Escape and Back close these first. */
const OVER = '[role="dialog"], [role="menu"], [role="listbox"], .menu.floating, .page-layer:not([hidden])';

/**
 * Around the library's apps: takes clicks on its cards and rows first while selecting (or with Ctrl or Shift), and
 * shows the bar along its foot. `shown`: the apps showing, by tab, search and filters, which Select all selects;
 * `playlist`, the open one of the player's own, whose apps picked by hand can come off it.
 */
export function TickArea({ shown, playlist, children }: { shown: readonly string[]; playlist?: string; children: ReactNode }) {
  const { library } = useLauncher();
  const [all, setTicked] = useState<ReadonlySet<string>>(new Set());
  // Only apps still in the library: one removed meanwhile drops out.
  const inLibrary = new Set(library.map((i) => i.id));
  const ticked = [...all].some((id) => !inLibrary.has(id)) ? new Set([...all].filter((id) => inLibrary.has(id))) : all;
  const ticking = ticked.size > 0;
  // The card clicked last, which Shift+click selects from.
  const anchor = useRef<string>(undefined);
  const menu = useRef<((x: number, y: number) => void) | null>(null);
  const toggle = (ids: string[]) => {
    setTicked((t) => tickToggled(t, ids));
    anchor.current = ids[0];
  };
  const tick: Tick = { ticked, toggle };
  // Said on the page, for its looks (app.css) and a controller's buttons (spatial.ts); the cards stay where they are,
  // with no box around them that the layouts' rules would have to step over.
  useEffect(() => {
    document.documentElement.toggleAttribute("data-ticking", ticking);
    return () => document.documentElement.removeAttribute("data-ticking");
  }, [ticking]);
  // The page's own listeners, ahead of the cards' (React's, and a menu's opener): what the cards would do waits.
  const now = useRef({ ticking, toggle });
  now.current = { ticking, toggle };
  useEffect(() => {
    // The cards and rows showing, in the order they show (across sections, in any layout).
    const order = () => [...document.querySelectorAll<HTMLElement>("[data-tick]")].flatMap((el) => el.dataset.tick!.split(" "));
    const cardOf = (target: EventTarget | null) => (target instanceof Element ? target.closest<HTMLElement>("[data-tick]") : null);
    const stop = (e: Event) => (e.preventDefault(), e.stopPropagation());
    const click = (e: MouseEvent) => {
      const card = cardOf(e.target);
      if (!card || !(now.current.ticking || e.ctrlKey || e.metaKey || e.shiftKey)) return;
      // Not opened, played or anything else the card would do.
      stop(e);
      const ids = card.dataset.tick!.split(" ");
      if (e.shiftKey && !e.ctrlKey && !e.metaKey) {
        setTicked((t) => tickRange(order(), anchor.current, ids[0], t));
        anchor.current = ids[0];
      } else now.current.toggle(ids);
    };
    // A sidebar row's double click plays it: not while selecting.
    const doubleClick = (e: MouseEvent) => void (now.current.ticking && cardOf(e.target) && stop(e));
    // A right-click, the Menu key or a controller's Options button on a card, while selecting: the selected apps'
    // menu, with this one selected too.
    const openMenu = (card: HTMLElement, x: number, y: number) => {
      const ids = card.dataset.tick!.split(" ");
      setTicked((t) => new Set([...t, ...ids]));
      menu.current?.(x, y);
    };
    const contextMenu = (e: MouseEvent) => {
      const card = cardOf(e.target);
      if (!card || !now.current.ticking) return;
      stop(e);
      openMenu(card, e.clientX, e.clientY);
    };
    const key = (e: KeyboardEvent) => {
      if (!now.current.ticking) return;
      const card = cardOf(e.target);
      if (card && (e.key === "ContextMenu" || (e.key === "F10" && e.shiftKey))) {
        stop(e);
        const r = card.getBoundingClientRect();
        return openMenu(card, (r.left + 12) * zoom(), (r.top + 12) * zoom());
      }
      // Escape and Back stop selecting, and Ctrl+A selects everything showing, unless something's open over the library.
      if (document.querySelector(OVER)) return;
      if (e.key === "Escape") {
        e.stopPropagation();
        setTicked(new Set());
      } else if (e.key.toLowerCase() === "a" && (e.ctrlKey || e.metaKey) && !(e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement)) {
        e.preventDefault();
        setTicked((t) => new Set([...t, ...order()]));
      }
    };
    const listeners: [string, (e: never) => void][] = [["click", click], ["dblclick", doubleClick], ["contextmenu", contextMenu], ["keydown", key]];
    for (const [type, fn] of listeners) window.addEventListener(type, fn as EventListener, true);
    return () => {
      for (const [type, fn] of listeners) window.removeEventListener(type, fn as EventListener, true);
    };
  }, []);
  return (
    <TickContext.Provider value={tick}>
      {children}
      {ticking && <div className="tick-space" aria-hidden="true" />}
      {ticking && <TickBar ids={[...ticked]} shown={shown} playlist={playlist} menu={menu} onSelectAll={() => setTicked((t) => new Set([...t, ...shown]))} onDone={() => setTicked(new Set())} />}
    </TickContext.Provider>
  );
}

/** Along the foot while apps are selected: how many, Select all and Done, and what to change on them. */
function TickBar({
  ids,
  shown,
  playlist,
  menu,
  onSelectAll,
  onDone,
}: {
  ids: string[];
  shown: readonly string[];
  playlist?: string;
  menu: { current: ((x: number, y: number) => void) | null };
  onSelectAll: () => void;
  onDone: () => void;
}) {
  const [uninstalling, setUninstalling] = useState<"uninstall" | "remove" | null>(null);
  const count = ids.length;
  const away = ids.filter((id) => !shown.includes(id)).length;
  const items = (part: Part) => (close: () => void) => <TickedItems ids={ids} part={part} playlist={playlist} close={close} onDone={onDone} onUninstall={setUninstalling} />;
  return (
    <>
    <div className="tick-bar" role="toolbar" aria-label={t`Selected apps`}>
      <button type="button" className="icon" aria-label={t`Stop selecting`} title={t`Stop selecting`} onClick={onDone}>
        <X size={16} />
      </button>
      <span className="tick-count" aria-live="polite">
        {plural(count, { one: "# selected", other: "# selected" })}
        {away > 0 && <span className="muted"> {plural(away, { one: "(# not showing)", other: "(# not showing)" })}</span>}
      </span>
      {shown.some((id) => !ids.includes(id)) && (
        <button type="button" className="text-button" onClick={onSelectAll}>
          <Trans comment="Selects every app the library shows">Select all</Trans>
        </button>
      )}
      <span className="tick-actions">
        <Popover label={t`Add to playlist`} icon={<><ListPlus size={15} /> <span className="tool-label">{t({ message: "Playlists", comment: "Button over the selected apps: puts them on playlists" })}</span> <ChevronDown size={13} aria-hidden="true" /></>} role="menu" up className="tool tick-part" panel="card-menu-panel">
          {items("playlists")}
        </Popover>
        <Popover label={t({ message: "Tags", comment: "Button over the selected apps: the player's tags to put on them" })} icon={<><Tag size={15} /> <span className="tool-label">{t({ message: "Tags", comment: "Button over the selected apps: the player's tags to put on them" })}</span> <ChevronDown size={13} aria-hidden="true" /></>} role="menu" up className="tool tick-part" panel="card-menu-panel">
          {items("tags")}
        </Popover>
        <HideButton ids={ids} onDone={onDone} />
        <Popover label={t({ message: "More", comment: "Button over the selected apps: the rest of what can change on them" })} icon={<><MoreHorizontal size={15} /> <span className="tool-label">{t({ message: "More", comment: "Button over the selected apps: the rest of what can change on them" })}</span> <ChevronDown size={13} aria-hidden="true" /></>} role="menu" up className="tool tick-part" panel="card-menu-panel">
          {items("more")}
        </Popover>
        {/* Where the bar is too narrow for the buttons above: all of it in one menu. */}
        <Popover label={t`Edit selected`} icon={<><MoreHorizontal size={15} /> <span className="tool-label">{t`Edit selected`}</span></>} role="menu" up className="tool tick-whole" panel="card-menu-panel">
          {items("all")}
        </Popover>
      </span>
      <button type="button" className="primary tick-done" onClick={onDone}>
        <Trans comment="Button: stops selecting apps in the library">Done</Trans>
      </button>
      {/* A right-click or Options on a card while selecting: all of it at that point. */}
      <Popover label={t`Selected apps`} icon={null} bare role="menu" opener={menu} panel="card-menu-panel">
        {items("all")}
      </Popover>
    </div>
      {/* Not in the bar, whose place on the screen would be the dialog's. */}
      {uninstalling && <UninstallTicked ids={ids} remove={uninstalling === "remove"} onClose={() => setUninstalling(null)} onDone={onDone} />}
    </>
  );
}

/** Hides the selected apps from the library, or shows them again where they all are hidden (the Hidden tab). */
function HideButton({ ids, onDone }: { ids: string[]; onDone: () => void }) {
  const { settings } = useLauncher();
  const hide = useHide(ids, onDone);
  const shown = tickState(ids, (id) => Boolean(settings.hidden?.includes(id))) === "all";
  return (
    <button type="button" className="tool tick-part" onClick={hide}>
      <EyeOff size={15} /> <span className="tool-label">{shown ? t`Show in library` : t`Hide from library`}</span>
    </button>
  );
}

/** Hides them all, or shows them all once they're all hidden, then stops selecting: they leave what shows. */
function useHide(ids: string[], onDone: () => void) {
  const { settings, setSettings, setNotice } = useLauncher();
  const all = tickState(ids, (id) => Boolean(settings.hidden?.includes(id))) === "all";
  return () => {
    setSettings((s) => ({ ...s, hidden: withIds(s.hidden, ids, !all) }));
    setNotice(all ? plural(ids.length, { one: "# app shows in your library again.", other: "# apps show in your library again." }) : plural(ids.length, { one: "# app hidden from your library.", other: "# apps hidden from your library." }));
    track("library_bulk_edit", { action: all ? "show" : "hide", apps: ids.length });
    onDone();
  };
}

type Part = "playlists" | "tags" | "more" | "all";

/** A box ticked, half-ticked or not, by how many of the selected apps have it. */
function TickBox({ state }: { state: TickState }) {
  return <span className="game-menu-check">{state === "all" ? <Check size={14} /> : state === "some" ? <Minus size={14} /> : null}</span>;
}

/**
 * What can change on the selected apps, as a menu: `part` of it, under the bar's button for it, or all of it (a
 * right-click, or a bar too narrow for its buttons).
 */
function TickedItems({ ids, part, playlist, close, onDone, onUninstall }: { ids: string[]; part: Part; playlist?: string; close: () => void; onDone: () => void; onUninstall: (how: "uninstall" | "remove") => void }) {
  const { config, library, catalog, installs, jobs, collections, saveCollection, setTags, settings, setUpdates, get, setNotice } = useLauncher();
  const [name, setName] = useState("");
  const [newTag, setNewTag] = useState("");
  const hide = useHide(ids, onDone);
  const n = ids.length;
  const did = (action: string, apps = n) => track("library_bulk_edit", { action, apps });
  const show = (p: Part) => part === p || part === "all";
  const heading = (text: string) => part === "all" && <div className="game-menu-heading">{text}</div>;
  const item = (text: string, act: () => void, kind?: "danger") => (
    <button key={text} type="button" role="menuitem" data-item className={kind} onClick={() => (close(), act())}>
      {text}
    </button>
  );

  // Playlists: the player's own, each ticked by how many of them it has; a new one starts with them all.
  const playlists = collections.filter((c) => !c.follows);
  const onPlaylist = (key: string) => tickState(ids, (id) => Boolean(playlists.find((c) => c.key === key)?.apps?.includes(id)));
  const togglePlaylist = (key: string) => {
    const c = playlists.find((p) => p.key === key)!;
    saveCollection({ ...c, apps: withIds(c.apps, ids, onPlaylist(key) !== "all") });
    did("playlist");
  };
  const addPlaylist = () => {
    if (!name.trim()) return;
    saveCollection({ key: newKey(), name: name.trim().slice(0, 60), tags: [], consoles: [], apps: [...ids], order: collections.length });
    setName("");
    did("new_playlist");
  };
  // The open playlist's apps picked by hand, which can come off it; it then shows the rest.
  const open = playlists.find((c) => c.key === playlist);
  const offPlaylist = open && ids.some((id) => open.apps?.includes(id));

  // Tags: the player's own, on any of their apps, each ticked by how many of these have it.
  const tagsOf = (id: string) => library.find((i) => i.id === id)?.overrides?.tags ?? [];
  const allTags = library.flatMap((i) => i.overrides?.tags ?? []).filter((tag, i, all) => all.findIndex((x) => x.toLowerCase() === tag.toLowerCase()) === i);
  const hasTag = (tag: string) => tickState(ids, (id) => tagsOf(id).some((x) => x.toLowerCase() === tag.toLowerCase()));
  const putTag = (tag: string, on: boolean) => {
    for (const id of ids) {
      const now = tagsOf(id);
      const next = withOwnTag(now, tag, on);
      if (next.length !== now.length) setTags(id, next);
    }
    did("tag");
  };
  const addTag = () => {
    const tag = newTag.trim().slice(0, 50);
    if (tag) putTag(tag, true);
    setNewTag("");
  };

  // What installing, updating and uninstalling come to, of these.
  const installed = ids.filter((id) => installs[id] && !installs[id].local);
  const busy = (id: string) => Boolean(jobs[id] && !("error" in jobs[id]));
  const gettable = ids.filter((id) => !installs[id] && !busy(id) && catalog[id] && canGet(catalog[id], config.os) && !pulledVerified(catalog[id]));
  const updatable = ids.filter((id) => installs[id] && !busy(id) && catalog[id] && hasUpdate(catalog[id], installs[id]));
  const getAll = (list: string[], action: string) => {
    for (const id of list) void get(catalog[id]);
    did(action, list.length);
  };
  // How they update: the choice they all have ticked; "Install automatically" only where some can (updatesByItself).
  const updatesNow = (install: Install) => (install.updates === "auto" && !updatesByItself(install) ? "ask" : (install.updates ?? "default"));
  const updates = installed.map((id) => updatesNow(installs[id]));
  const auto = installed.some((id) => updatesByItself(installs[id]));
  const setAll = (value: "default" | "ask" | "auto" | "pinned") => {
    for (const id of installed) setUpdates(id, value === "default" ? undefined : value);
    did(`updates_${value}`, installed.length);
  };
  const updatesItem = (value: "default" | "ask" | "auto" | "pinned", text: string) => (
    <button key={value} type="button" role="menuitemradio" data-item aria-checked={updates.every((u) => u === value)} onClick={() => setAll(value)}>
      <TickBox state={tickState(updates, (u) => u === value)} />
      {text}
    </button>
  );
  const notInstalled = n - ids.filter((id) => installs[id]).length;

  return (
    <div className="game-menu tick-menu" data-arrows="" onKeyDown={menuArrows}>
      {part === "all" && <div className="game-menu-heading">{plural(n, { one: "# app selected", other: "# apps selected" })}</div>}
      {show("playlists") && (
        <>
          {heading(t({ message: "Add to playlist", comment: "Menu item opening the playlists to add an app to" }))}
          {playlists.map((c) => {
            const state = onPlaylist(c.key);
            return (
              <button key={c.key} type="button" role="menuitemcheckbox" data-item aria-checked={state === "all" ? true : state === "some" ? "mixed" : false} onClick={() => togglePlaylist(c.key)}>
                <TickBox state={state} />
                {c.name}
              </button>
            );
          })}
          <input
            data-item
            className="game-menu-new"
            aria-label={t`New playlist`}
            placeholder={t`+ New playlist…`}
            maxLength={60}
            value={name}
            onChange={(e) => setName(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), addPlaylist())}
          />
          {open && offPlaylist && (
            <>
              <hr />
              {item(t`Remove from ${open.name}`, () => {
                saveCollection({ ...open, apps: withIds(open.apps, ids, false) });
                did("off_playlist");
                onDone();
              })}
            </>
          )}
          {part === "all" && <hr />}
        </>
      )}
      {show("tags") && (
        <>
          {heading(t({ message: "Tags", comment: "Menu item opening the player's tags for an app" }))}
          {allTags.map((tag) => {
            const state = hasTag(tag);
            return (
              <button key={tag} type="button" role="menuitemcheckbox" data-item aria-checked={state === "all" ? true : state === "some" ? "mixed" : false} onClick={() => putTag(tag, state !== "all")}>
                <TickBox state={state} />
                {tag}
              </button>
            );
          })}
          <input
            data-item
            className="game-menu-new"
            aria-label={t`Add a tag`}
            placeholder={t`+ New tag…`}
            maxLength={50}
            value={newTag}
            onChange={(e) => setNewTag(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), addTag())}
          />
          {part === "all" && <hr />}
        </>
      )}
      {part === "all" && item(tickState(ids, (id) => Boolean(settings.hidden?.includes(id))) === "all" ? t`Show in library` : t`Hide from library`, hide)}
      {show("more") && (
        <>
          {gettable.length > 0 && item(plural(gettable.length, { one: "Install # app", other: "Install # apps" }), () => getAll(gettable, "install"))}
          {updatable.length > 0 && item(plural(updatable.length, { one: "Update # app", other: "Update # apps" }), () => getAll(updatable, "update"))}
          {installed.length > 0 && (
            <>
              {(gettable.length > 0 || updatable.length > 0 || part === "all") && <hr />}
              <div className="game-menu-heading">
                <Trans comment="How an installed app gets new releases">Updates</Trans>
              </div>
              {updatesItem("default", t({ message: "Use my default", comment: "How the selected apps update: whatever Settings, Updates says" }))}
              {updatesItem("ask", t({ message: "Offer them", comment: "Updates: offer new releases to install" }))}
              {auto && updatesItem("auto", t`Install automatically`)}
              {updatesItem("pinned", t({ message: "Stay on the installed version", comment: "How the selected apps update: never, each staying on the version it has" }))}
            </>
          )}
          <hr />
          {installed.length > 0 && item(plural(installed.length, { one: "Uninstall # app…", other: "Uninstall # apps…" }), () => onUninstall("uninstall"), "danger")}
          {item(installed.length ? t`Uninstall and remove from library…` : t`Remove from library…`, () => onUninstall("remove"), "danger")}
          {/* Nothing to install, update or uninstall: why the rest isn't here. */}
          {installed.length === 0 && gettable.length === 0 && notInstalled > 0 && part === "more" && (
            <p className="game-menu-note">
              <Trans>None of them are installed.</Trans>
            </p>
          )}
        </>
      )}
    </div>
  );
}

/**
 * Uninstalling the selected apps takes a yes, as one does; `remove`, they leave the library too (those not installed,
 * or the player's own files, only that). Says which, and that saves kept in their folders go with them.
 */
function UninstallTicked({ ids, remove, onClose, onDone }: { ids: string[]; remove: boolean; onClose: () => void; onDone: () => void }) {
  const { installs, catalog, library, uninstall, remove: removeApp, setNotice } = useLauncher();
  const installed = ids.filter((id) => installs[id] && !installs[id].local);
  const going = remove ? ids : installed;
  const [portable, setPortable] = useState<string[]>([]);
  // Saves kept in an app's folder leave with it, which a player wouldn't otherwise expect of an uninstall.
  useEffect(() => {
    let current = true;
    void Promise.all(installed.map((id) => native.appFiles(installs[id].folder, installs[id].dir, [PORTABLE_FILE]).then((found) => (found?.length ? [id] : []), () => []))).then(
      (found) => current && setPortable(found.flat()),
    );
    return () => void (current = false);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  // As the library names them: the player's own name for one, else the catalog's.
  const nameOf = (id: string): string => library.find((i) => i.id === id)?.overrides?.name ?? (catalog[id] as Entry | undefined)?.projectName ?? id;
  const names = going.map(nameOf);
  const listed = names.slice(0, 8).join(", ");
  const more = names.length - 8;
  const count = going.length;
  const files = installed.length;
  const go = async () => {
    onClose();
    onDone();
    track("library_bulk_edit", { action: remove ? "remove" : "uninstall", apps: count });
    setNotice(remove ? plural(count, { one: "Removing # app…", other: "Removing # apps…" }) : plural(count, { one: "Uninstalling # app…", other: "Uninstalling # apps…" }));
    // One after another: each moves its own folder to the trash.
    for (const id of going) await (remove ? removeApp(id) : uninstall(id)).catch(() => {});
  };
  return (
    <Dialog label={remove ? t`Remove apps` : t`Uninstall apps`} onClose={onClose}>
      <div className="detail-body uninstall-prompt">
        <h2>
          <Trash2 size={18} />{" "}
          {remove
            ? files
              ? plural(count, { one: "Uninstall # app and remove it from your library?", other: "Uninstall and remove # apps from your library?" })
              : plural(count, { one: "Remove # app from your library?", other: "Remove # apps from your library?" })
            : plural(count, { one: "Uninstall # app?", other: "Uninstall # apps?" })}
        </h2>
        <p className="muted">{more > 0 ? t`${listed} and ${more} more` : listed}</p>
        <p>
          {remove
            ? files
              ? plural(files, { one: "The files of the one installed go to the trash, and they all leave your library.", other: "The files of the # installed go to the trash, and they all leave your library." })
              : t`They leave your library. Nothing is deleted.`
            : t`Their files go to the trash. They stay in your library, so you can get them again.`}
        </p>
        {portable.length > 0 && (
          <p>
            {plural(portable.length, { one: "# of them may keep your saves in its folder. They'll go to the trash too.", other: "# of them may keep your saves in their folders. They'll go to the trash too." })}
          </p>
        )}
        <div className="row">
          <button className="primary" autoFocus onClick={onClose}>
            <Trans>Cancel</Trans>
          </button>
          <button className="danger" onClick={() => void go()}>
            {remove ? (files ? t`Uninstall and remove` : t({ message: "Remove", comment: "Button: takes the selected apps out of the library" })) : t`Uninstall`}
          </button>
        </div>
      </div>
    </Dialog>
  );
}
