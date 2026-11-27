/**
 * Selecting many of Browse's apps and doing the same to them all at once, as the library's selecting does
 * (library-ticking.tsx), with Browse's own changes: adding them to the library, installing them, putting them on a
 * playlist (which adds them to the library too), and hiding them from Browse. What's about apps already got (tags,
 * updates, uninstalling) is the library's.
 *
 * Each goes to the account once for them all: the library's and the playlists' changes are saved together
 * (store.tsx), and hiding is a setting. Installing asks for each app's releases, as its own Install would.
 */
import { createContext, useContext, useRef, useState, type ComponentProps, type ReactNode } from "react";
import { Check, ChevronDown, Download, EyeOff, ListPlus, MoreHorizontal, Plus, X } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { Entry } from "@quiverlauncher/api";
import { EntryCard } from "@quiverlauncher/ui";
import { canGet, pulledVerified, useLauncher } from "./store";
import { Popover, newKey } from "./library";
import { menuArrows } from "./card-menu";
import { SelectArea, TickBox, useInstallAll, useTickSelect, useTicked, type BarProps } from "./library-ticking";
import { tickState, withIds } from "./ticking";
import { toggleHidden } from "./browse-hidden";
import { track } from "./telemetry";

/**
 * The apps Browse's cards have shown, by id: the store's catalog keeps only the library's (store.tsx's `remember`),
 * so what's selected and not in the library is found here.
 */
const Seen = createContext<Map<string, Entry> | null>(null);

/** Around Browse's cards: its front page's shelves and its list. `shown`, the list's apps, which Select all selects. */
export function BrowseTickArea({ shown, children }: { shown: readonly string[]; children: ReactNode }) {
  const seen = useRef(new Map<string, Entry>()).current;
  return (
    <Seen.Provider value={seen}>
      <SelectArea shown={shown} bar={(props) => <BrowseBar {...props} />}>
        {children}
      </SelectArea>
    </Seen.Provider>
  );
}

/** A Browse card that can be selected: its tick over the art while Browse is selecting. */
export function TickableCard(props: ComponentProps<typeof EntryCard> & { entry: { id: string } }) {
  const id = props.entry.id;
  const { ticking, ticked, selectable } = useTicked([id]);
  useContext(Seen)?.set(id, props.entry as Entry);
  if (!selectable) return <EntryCard {...props} />;
  return (
    <EntryCard
      {...props}
      tick={id}
      ticked={ticked}
      mark={
        ticking && (
          <span className="tick-mark" data-ticked={ticked || undefined} aria-hidden="true">
            {ticked && <Check size={16} strokeWidth={3} />}
          </span>
        )
      }
    />
  );
}

/** What the selected apps come to: those to add, to install, and how many are hidden from Browse. */
function useSelected(ids: string[]) {
  const { config, library, installs, jobs, catalog, settings } = useLauncher();
  const seen = useContext(Seen);
  // As the store has them (a library app's, with what it found for here), else as their cards showed them.
  const entries = ids.flatMap((id) => {
    const entry = (catalog[id] as Entry | undefined) ?? seen?.get(id);
    return entry ? [entry] : [];
  });
  const inLibrary = new Set(library.map((i) => i.id));
  const busy = (id: string) => Boolean(jobs[id] && !("error" in jobs[id]));
  // As each card's own button: Add for one that can install here, then Install.
  const addable = entries.filter((e) => !inLibrary.has(e.id) && canGet(e, config.os));
  const installable = entries.filter((e) => !installs[e.id] && !busy(e.id) && canGet(e, config.os) && !pulledVerified(e));
  const hiddenNow = new Set(settings.browseHidden?.map((h) => h.id));
  const allHidden = tickState(ids, (id) => hiddenNow.has(id)) === "all";
  return { entries, addable, installable, allHidden };
}

/** Along the foot while Browse's apps are selected: how many, Select all and Done, and what to do with them. */
function BrowseBar({ ids, shown, menu, onSelectAll, onDone }: BarProps) {
  const { addable, installable, allHidden } = useSelected(ids);
  const { add, install, hide } = useBrowseActions(ids, onDone);
  const count = ids.length;
  const away = ids.filter((id) => !shown.includes(id)).length;
  const items = (part: Part) => (close: () => void) => <BrowseTickedItems ids={ids} part={part} close={close} onDone={onDone} />;
  return (
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
        {/* Greyed rather than gone where none of them can: the bar keeps its buttons in their places. */}
        <button type="button" className="tool tick-part" disabled={!addable.length} onClick={add} title={addable.length ? undefined : t`They're all in your library already, or can't be installed here.`}>
          <Plus size={15} /> <span className="tool-label">{addCount(addable.length, count)}</span>
        </button>
        <button type="button" className="tool tick-part" disabled={!installable.length} onClick={install} title={installable.length ? undefined : t`They're all installed already, or can't be installed here.`}>
          <Download size={15} /> <span className="tool-label">{installCount(installable.length, count)}</span>
        </button>
        <Popover label={t`Add to playlist`} icon={<><ListPlus size={15} /> <span className="tool-label">{t({ message: "Playlists", comment: "Button over the selected apps: puts them on playlists" })}</span> <ChevronDown size={13} aria-hidden="true" /></>} role="menu" up className="tool tick-part" panel="card-menu-panel">
          {items("playlists")}
        </Popover>
        <button type="button" className="tool tick-part" onClick={hide}>
          <EyeOff size={15} /> <span className="tool-label">{allHidden ? t`Show in Discover` : t`Hide from Discover`}</span>
        </button>
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
  );
}

/** "Add to library", or how many of those selected it adds where it's fewer. */
const addCount = (n: number, of: number) =>
  n && n < of ? plural(n, { one: "Add # to library", other: "Add # to library" }) : t({ message: "Add to library", comment: "Button over the apps selected in Discover: adds them to the library without installing them" });
/** "Install", or how many of those selected it installs where it's fewer. */
const installCount = (n: number, of: number) =>
  n && n < of ? plural(n, { one: "Install #", other: "Install #" }) : t({ message: "Install", comment: "Button over the apps selected in Discover: installs them" });

/**
 * Adding, installing and hiding the selected apps; each stops selecting, as what they did shows on their cards
 * (In library, a download's progress) or they leave Browse. Hiding's toast has Undo, which selects them again.
 */
function useBrowseActions(ids: string[], onDone: () => void) {
  const { settings, setSettings, setNotice, addAllToLibrary } = useLauncher();
  const { entries, addable, installable, allHidden } = useSelected(ids);
  const installAll = useInstallAll();
  const select = useTickSelect();
  const did = (action: string, apps: number) => track("browse_bulk_edit", { action, apps });
  const add = () => {
    addAllToLibrary(addable);
    setNotice(plural(addable.length, { one: "# app is in your library.", other: "# apps are in your library." }));
    did("add", addable.length);
    onDone();
  };
  const install = () => {
    installAll(installable);
    did("install", installable.length);
    onDone();
  };
  const hide = () => {
    const before = settings.browseHidden;
    const hiddenNow = new Set(before?.map((h) => h.id));
    // Each by its slug too, as one card's Hide from Browse keeps it (browse-hidden.ts).
    const slugs = new Map(entries.map((e) => [e.id, e.slug]));
    const changed = ids.filter((id) => hiddenNow.has(id) === allHidden && slugs.has(id));
    let next = before ?? [];
    for (const id of changed) next = toggleHidden(next, { id, slug: slugs.get(id)! });
    setSettings((s) => ({ ...s, browseHidden: next.length ? next : undefined }));
    const undo = () => {
      setSettings((s) => {
        let back = s.browseHidden ?? [];
        for (const id of changed) back = toggleHidden(back, { id, slug: slugs.get(id)! });
        return { ...s, browseHidden: back.length ? back : undefined };
      });
      select?.(ids);
      did("undo-" + (allHidden ? "show" : "hide"), changed.length);
    };
    setNotice(
      allHidden
        ? plural(changed.length, { one: "# app shows in Discover again.", other: "# apps show in Discover again." })
        : plural(changed.length, { one: "# app hidden from Discover.", other: "# apps hidden from Discover." }),
      { action: { label: t({ message: "Undo", comment: "Button on the toast after hiding apps: shows them again, selected" }), act: undo } },
    );
    did(allHidden ? "show" : "hide", changed.length);
    onDone();
  };
  return { add, install, hide };
}

type Part = "playlists" | "all";

/**
 * What can be done with the selected apps, as a menu: the playlists, under the bar's button for them, or all of it (a
 * right-click, or a bar too narrow for its buttons).
 */
function BrowseTickedItems({ ids, part, close, onDone }: { ids: string[]; part: Part; close: () => void; onDone: () => void }) {
  const { collections, saveCollection, addAllToLibrary } = useLauncher();
  const { entries, addable, installable, allHidden } = useSelected(ids);
  const { add, install, hide } = useBrowseActions(ids, onDone);
  const [name, setName] = useState("");
  const n = ids.length;
  const did = (action: string, apps = n) => track("browse_bulk_edit", { action, apps });
  const item = (text: string, act: () => void, disabled = false) => (
    <button key={text} type="button" role="menuitem" data-item disabled={disabled} onClick={() => (close(), act())}>
      {text}
    </button>
  );
  // The player's own playlists, each ticked by how many of these it has. A playlist holds library apps: putting them
  // on one adds them to the library too, and only those the catalog has sent.
  const listed = entries.map((e) => e.id);
  const playlists = collections.filter((c) => !c.follows);
  const onPlaylist = (key: string) => tickState(listed, (id) => Boolean(playlists.find((c) => c.key === key)?.apps?.includes(id)));
  const togglePlaylist = (key: string) => {
    const c = playlists.find((p) => p.key === key)!;
    const on = onPlaylist(key) !== "all";
    if (on) addAllToLibrary(entries);
    saveCollection({ ...c, apps: withIds(c.apps, listed, on) });
    did(on ? "playlist" : "off_playlist", listed.length);
  };
  const addPlaylist = () => {
    if (!name.trim() || !listed.length) return;
    addAllToLibrary(entries);
    saveCollection({ key: newKey(), name: name.trim().slice(0, 60), tags: [], consoles: [], apps: listed, order: collections.length });
    setName("");
    did("new_playlist", listed.length);
  };
  return (
    <div className="game-menu tick-menu" data-arrows="" onKeyDown={menuArrows}>
      {part === "all" && (
        <>
          <div className="game-menu-heading">{plural(n, { one: "# app selected", other: "# apps selected" })}</div>
          {item(addCount(addable.length, n), add, !addable.length)}
          {item(installCount(installable.length, n), install, !installable.length)}
          <hr />
          <div className="game-menu-heading">{t({ message: "Add to playlist", comment: "Menu item opening the playlists to add an app to" })}</div>
        </>
      )}
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
      <p className="game-menu-note">
        <Trans>Apps put on a playlist are added to your library.</Trans>
      </p>
      {part === "all" && (
        <>
          <hr />
          {item(allHidden ? t`Show in Discover` : t`Hide from Discover`, hide)}
        </>
      )}
    </div>
  );
}
