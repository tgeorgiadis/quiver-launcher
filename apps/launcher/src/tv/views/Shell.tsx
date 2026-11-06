/**
 * TV mode's frame: the tab strip, the focused game's backdrop
 * and header, the tab's view, the hint bar, and overlays above them.
 */
import { useEffect, useMemo, useRef, useState } from "react";
import { t } from "@lingui/core/macro";
import { useTv } from "../context";
import { inLibraryView, type TvApp } from "../bridge";
import { resolveConsole } from "../consoles/resolve";
import { buildTabs, consoleTabId, cycleTab, keepTab } from "../consoles/tabs";
import type { InputAction } from "../input/actions";
import type { AllSort } from "../settings";
import { TabStrip } from "./TabStrip";
import { GridTab } from "./GridTab";
import { HintBar } from "./HintBar";
import { Backdrop, GameHeader } from "./GameHeader";
import { Details } from "./Details";
import { Settings } from "./Settings";
import { Discover, newDiscoverState } from "./Discover";
import { Downloads } from "./Downloads";
import { Osk } from "./Osk";
import { Search } from "lucide-react";
import { matchesSearch } from "./lists";
import { Home } from "./Home";
import { ShelfNavigator } from "../nav/shelf";

type Overlay = { kind: "details"; app: TvApp } | { kind: "menu" } | null;

const byTitle = (a: TvApp, b: TvApp) => a.title.localeCompare(b.title, undefined, { sensitivity: "base", numeric: true });
export function sortApps(apps: TvApp[], sort: AllSort) {
  return [...apps].sort(sort === "recent" ? (a, b) => (b.lastPlayed ?? 0) - (a.lastPlayed ?? 0) || byTitle(a, b) : byTitle);
}

export function Shell() {
  const { bridge, settings, consoles } = useTv();

  const consoleOf = useMemo(
    () => new Map(bridge.apps.map((app) => [app.id, resolveConsole(app)])),
    [bridge.apps],
  );
  /** The library's tabs: installed games and first downloads. */
  const installed = useMemo(() => bridge.apps.filter(inLibraryView), [bridge.apps]);
  const busy = bridge.apps.filter((a) => a.state === "downloading" || a.state === "updating" || a.state === "queued").length;
  const tabs = useMemo(
    () => buildTabs(installed.map((a) => consoleOf.get(a.id)!), consoles, settings, busy),
    [installed, consoleOf, consoles, settings, busy],
  );

  const [selectedId, setSelectedId] = useState(() => tabs[0].id);
  const tab = keepTab(tabs, selectedId);
  // Its last game uninstalled: the console's tab is gone, so All is shown (and remembered).
  useEffect(() => {
    if (tab.id !== selectedId) setSelectedId(tab.id);
  }, [tab.id, selectedId]);

  /** Each tab's focused app, so coming back to a tab finds the same tile. */
  const memory = useRef(new Map<string, string>());
  const homeNav = useRef(new ShelfNavigator());
  const discover = useRef(newDiscoverState());
  const [focused, setFocused] = useState<TvApp | null>(null);
  /** Which way the last tab change went, for the page's slide. */
  const [direction, setDirection] = useState<1 | -1>(1);
  const [overlay, setOverlay] = useState<Overlay>(null);
  /** Search: results filter live in the tab; Discover searches the catalog. */
  const [search, setSearch] = useState("");
  const [typing, setTyping] = useState(false);
  const searchBefore = useRef("");

  /** Shows a tab, sliding the page in from the side it's on. */
  const go = (id: string) => {
    if (id === tab.id) return;
    setDirection(tabs.findIndex((t) => t.id === id) < tabs.findIndex((t) => t.id === tab.id) ? -1 : 1);
    setSelectedId(id);
  };
  /** LB / RB: the next or previous tab. */
  const step = (by: 1 | -1) => {
    const i = tabs.findIndex((t) => t.id === tab.id);
    go(tabs[Math.min(tabs.length - 1, Math.max(0, i + by))].id);
  };
  /** The library tab (Home, All or a console) last shown, to come back to from Discover and Downloads. */
  const library = useRef(tabs[0].id);
  if (tab.kind !== "discover" && tab.kind !== "downloads") library.current = tab.id;
  const cycle = (by: 1 | -1) => go(cycleTab(tabs, tab.id, library.current, by));

  /**
   * After Get in Discover, with "Go to it": the game's console tab with it focused. The tab may only
   * appear once the store has it, a moment later; until then All has it focused.
   */
  const pending = useRef<{ tabId: string; until: number } | null>(null);
  const goToGame = (app: TvApp) => {
    const tabId = consoleTabId(resolveConsole(app));
    memory.current.set(tabId, app.id);
    memory.current.set("all", app.id);
    pending.current = { tabId, until: Date.now() + 8000 };
    go(tabs.some((t) => t.id === tabId) ? tabId : "all");
  };
  useEffect(() => {
    const p = pending.current;
    if (!p) return;
    if (Date.now() > p.until || tab.id === p.tabId) pending.current = null;
    else if (tabs.some((t) => t.id === p.tabId)) {
      pending.current = null;
      go(p.tabId);
    }
  });
  /** Actions every tab's view leaves to the frame. */
  const global = (action: InputAction) => {
    if (action === "tabPrev") step(-1);
    else if (action === "tabNext") step(1);
    else if (action === "pagePrev") cycle(-1);
    else if (action === "pageNext") cycle(1);
    else if (action === "menu") setOverlay({ kind: "menu" });
    else if (action === "search") {
      // Home and Downloads have nothing to search in: All does.
      if (tab.kind === "home" || tab.kind === "downloads") go("all");
      searchBefore.current = search;
      setTyping(true);
    }
    else return false;
    return true;
  };
  const first = tabs[0];
  /** B on Discover and Downloads: back to the library tab they were reached from. */
  const backToLibrary = { label: t`Library`, go: () => go(keepTab(tabs, library.current).id) };
  const toFirst = search
    ? { label: t`Clear search`, go: () => setSearch("") }
    : tab.id === first.id
      ? undefined
      : { label: first.label, go: () => setSelectedId(first.id) };

  const grid = tab.kind === "all" || tab.kind === "console";
  const apps = useMemo(() => {
    const found = installed.filter((a) => matchesSearch(a, search));
    if (tab.kind === "all") return sortApps(found, settings.allSort);
    if (tab.kind === "console") return sortApps(found.filter((a) => consoleOf.get(a.id) === tab.console!.id), "name");
    return [];
  }, [tab, installed, consoleOf, settings.allSort, search]);
  const shown = tab.kind === "downloads" ? null : focused;
  const openDetails = (app: TvApp) => setOverlay({ kind: "details", app });
  const toDownloads = () => go("downloads");
  const consoleName = (app: TvApp) => consoles.get(consoleOf.get(app.id) ?? resolveConsole(app)).name;

  return (
    <>
      <Backdrop app={shown} />
      <TabStrip
        tabs={tabs}
        selected={tab.id}
        onSelect={go}
      />
      <main className="tv-content">
        <div className={`tv-page${direction < 0 ? " from-left" : ""}`} key={tab.id}>
          {tab.kind === "home" ? (
            <Home
              nav={homeNav.current}
              onFocus={setFocused}
              onGlobal={global}
              onDetails={openDetails}
              onDownloads={toDownloads}
              consoleName={consoleName}
            />
          ) : grid ? (
            <div className="tv-library">
              <GameHeader app={shown} consoleName={shown ? consoleName(shown) : ""} />
              <GridTab
                tabId={tab.id}
                apps={apps}
                memory={memory.current}
                onFocus={setFocused}
                onGlobal={global}
                back={toFirst}
                onDetails={openDetails}
                onDownloads={toDownloads}
                empty={search ? { title: t`Nothing matches “${search}”`, text: t`Search again, or B to clear the search.` } : undefined}
              />
            </div>
          ) : tab.kind === "discover" ? (
            <Discover
              state={discover.current}
              memory={memory.current}
              focused={focused}
              onFocus={setFocused}
              onGlobal={global}
              back={backToLibrary}
              onDetails={openDetails}
              onDownloads={toDownloads}
              consoleName={consoleName}
              search={search}
              onGot={(app) => settings.afterGet === "library" && goToGame(app)}
            />
          ) : (
            <Downloads onFocus={setFocused} onGlobal={global} back={backToLibrary} onDetails={openDetails} />
          )}
        </div>
      </main>
      {search && (grid || tab.kind === "discover") && (
        <button type="button" className="tv-search-chip" onClick={() => setSearch("")} aria-label={t`Clear the search`}>
          <Search aria-hidden="true" />“{search}”<span>✕</span>
        </button>
      )}
      <HintBar />
      {typing && (
        <Osk
          title={t({ message: "Search", comment: "Title of TV mode's on-screen keyboard, typing a search" })}
          value={search}
          onChange={setSearch}
          onDone={(value) => {
            setSearch(value.trim());
            setTyping(false);
          }}
          onCancel={() => {
            setSearch(searchBefore.current);
            setTyping(false);
          }}
        />
      )}
      {overlay?.kind === "details" && <Details app={overlay.app} onClose={() => setOverlay(null)} />}
      {overlay?.kind === "menu" && <Settings onClose={() => setOverlay(null)} />}
    </>
  );
}
