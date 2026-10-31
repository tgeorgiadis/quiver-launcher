/**
 * The library's view options (view.ts): the controls, in the library's View
 * menu and in Settings, and the picture a box art or icon card shows.
 */
import { useEffect, useState, type ReactNode } from "react";
import { ChevronDown, Gamepad2, LayoutGrid, PanelLeft, RectangleHorizontal, RectangleVertical, Square } from "lucide-react";
import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import type { MessageDescriptor } from "@lingui/core";
import { i18n } from "@quiverlauncher/i18n";
import type { Entry } from "@quiverlauncher/api";
import { OS_ICONS, OS_NAMES, blurSource, cardSources } from "@quiverlauncher/ui";
import { useLauncher } from "./store";
import { PLATFORMS, platformsOf, savedPlatforms, type Platform } from "./platforms";
import { SIDEBAR_SIZES } from "./sidebar";
import { GRID_GROUP_BYS, GROUP_BYS, gridGroupBy, isGroupBy, type GroupBy } from "./group";
import { CARD_IMAGES, CARD_SIZES, LAYOUTS, NAME_ORDERS, SIDEBAR_MIN_WIDTH, artFor, savedView, viewOf, type CardImage, type CardSize, type LibraryView } from "./view";

/** This computer's library view, and a way to change it. */
export function useLibraryView() {
  const { settings, setSettings } = useLauncher();
  const view = viewOf(settings.library);
  return {
    view,
    // Sections by console were a switch before there were other sections.
    groupBy: isGroupBy(settings.groupBy) ? settings.groupBy : settings.byConsole ? ("console" as const) : undefined,
    /** `groupBy: null` puts the library in one piece again. */
    set: (next: Partial<LibraryView> & { groupBy?: GroupBy | null }) => {
      const { groupBy, ...rest } = next;
      const { byConsole: _, groupBy: __, ...others } = settings;
      setSettings({
        ...(groupBy === undefined ? settings : { ...others, ...(groupBy ? { groupBy } : {}) }),
        library: savedView({ ...view, ...rest }),
      });
    },
  };
}

/** The library's sections, a menu of their own beside Sort; the grid's without sections by game (group.ts). */
export function GroupBySelect({ grid }: { grid?: boolean }) {
  const { groupBy: picked, set } = useLibraryView();
  const groupBy = grid ? gridGroupBy(picked) : picked;
  return (
    <span className={`quiet-select group-by${groupBy ? " on" : ""}`}>
      <select aria-label={t`Group by`} value={groupBy ?? ""} onChange={(e) => set({ groupBy: isGroupBy(e.target.value) ? e.target.value : null })}>
        {(grid ? GRID_GROUP_BYS : GROUP_BYS).map(([value, label]) => (
          <option key={value} value={value}>
            {i18n._(label)}
          </option>
        ))}
      </select>
      <ChevronDown size={14} aria-hidden="true" />
    </span>
  );
}

/**
 * Whether the Sidebar layout fits: a window wide enough, at the interface
 * size, for the sidebar beside an app's page, and not a phone or tablet.
 */
export function useSidebarFits() {
  const { config, settings } = useLauncher();
  // The interface size zooms the page by its scale.
  const scale = settings.scale ?? 1;
  const [width, setWidth] = useState(window.innerWidth);
  useEffect(() => {
    const resize = () => setWidth(window.innerWidth);
    window.addEventListener("resize", resize);
    return () => window.removeEventListener("resize", resize);
  }, []);
  const wide = width / scale >= SIDEBAR_MIN_WIDTH;
  return wide && config.os !== "android" && config.os !== "ios";
}

/** Layout, card image, card size and names; in Settings and on a phone, the library's sections too (elsewhere the library has them beside Sort). */
export function ViewOptions({ groups }: { groups?: boolean }) {
  const { view, set } = useLibraryView();
  const fits = useSidebarFits();
  // Where the sidebar doesn't fit, the library is a grid; the choice is kept for a wider window.
  const layout = view.layout === "sidebar" && !fits ? "grid" : view.layout;
  const cards = layout !== "sidebar";
  return (
    <div className="view-options">
      {/* Where the sidebar doesn't fit, the grid is the only layout. */}
      {fits && <Choice label={t({ message: "Layout", comment: "Library view option" })} options={LAYOUTS} icons={LAYOUT_ICONS} value={layout} onChange={(layout) => set({ layout })} />}
      {/* The sidebar has no cards. */}
      {cards && <Choice label={t`Card image`} options={CARD_IMAGES} icons={IMAGE_ICONS} value={view.image} onChange={(image) => set({ image })} />}
      {cards && <Choice label={t`Card size`} options={CARD_SIZES} value={view.size} onChange={(size) => set({ size })} />}
      {cards && (
        <label className="toggle">
          <input type="checkbox" checked={view.names} onChange={(e) => set({ names: e.target.checked })} />
          <Trans>Show names on cards</Trans>
        </label>
      )}
      {cards && (
        <label className="toggle">
          <input type="checkbox" checked={view.byGame} onChange={(e) => set({ byGame: e.target.checked })} />
          <Trans comment="Library view option: a game's ports (apps that play it) share one card, which lists them">One card for each game</Trans>
        </label>
      )}
      {/* A game's card has the game's name; an app's, the one picked. */}
      {cards && view.names && !view.byGame && <Choice label={t`Large name`} options={NAME_ORDERS} value={view.nameOrder} onChange={(nameOrder) => set({ nameOrder })} />}
      {/* The same choice, for the sidebar's names: the one shown, or the one on top of both. */}
      {layout === "sidebar" && <Choice label={view.bothNames ? t`Name on top` : t({ message: "Name", comment: "Library view option: which name the sidebar shows, the game's or the app's" })} options={NAME_ORDERS} value={view.nameOrder} onChange={(nameOrder) => set({ nameOrder })} />}
      {layout === "sidebar" && (
        <label className="toggle">
          <input type="checkbox" checked={view.bothNames} onChange={(e) => set({ bothNames: e.target.checked })} />
          <Trans>Show both names</Trans>
        </label>
      )}
      {layout === "sidebar" && <SidebarWidth />}
      {/* The sidebar has none. */}
      {cards && (
        <label className="toggle">
          <input type="checkbox" checked={view.continuePlaying} onChange={(e) => set({ continuePlaying: e.target.checked })} />
          <Trans>Show Continue playing</Trans>
        </label>
      )}
      {cards && (
        <label className="toggle">
          <input type="checkbox" checked={view.marks} onChange={(e) => set({ marks: e.target.checked })} />
          <Trans>Show installed and update icons</Trans>
        </label>
      )}
      {groups && (
        <div className="view-choice">
          <span className="view-label">
            <Trans comment="Library view option: how the library is split into sections">Sections</Trans>
          </span>
          <GroupBySelect grid={cards} />
        </div>
      )}
    </div>
  );
}

/** The platforms whose apps the library shows on this device (platforms.ts), and a way to change them. */
export function usePlatforms() {
  const { config, settings, setSettings } = useLauncher();
  return {
    platforms: platformsOf(settings.platforms, config.os),
    /** The player picked other platforms than this device's own. */
    picked: settings.platforms !== undefined,
    set: (picked: Platform[]) => setSettings({ ...settings, platforms: savedPlatforms(picked, config.os) }),
  };
}

/**
 * A box for each platform, at least one ticked; in the library's platform menu and in Settings. In the library's, by
 * each platform not ticked, how many of its apps ticking it would show (`hidden`), rather than a line under its filters.
 */
export function PlatformChoice({ onChange, hidden }: { onChange?: () => void; hidden?: Partial<Record<Platform, number>> }) {
  const { platforms, picked, set } = usePlatforms();
  const change = (next: Platform[]) => (set(next), onChange?.());
  return (
    <div className="view-choice platform-choice">
      <span className="view-label">
        <Trans comment="Above a box for each platform (Windows, macOS, Linux, Android): the library lists apps for the ones ticked">Show apps for</Trans>
      </span>
      {PLATFORMS.map((os) => {
        const on = platforms.includes(os);
        const Icon = OS_ICONS[os];
        const count = on ? 0 : (hidden?.[os] ?? 0);
        return (
          <label key={os} className="toggle">
            {/* The last one ticked stays: a library of no platform's apps would be empty. */}
            <input type="checkbox" checked={on} disabled={on && platforms.length === 1} onChange={() => change(on ? platforms.filter((p) => p !== os) : [...platforms, os])} />
            <Icon aria-hidden />
            {OS_NAMES[os]}
            {count > 0 && (
              <span className="platform-count">
                <span aria-hidden="true">{count}</span>
                <span className="sr-only">{plural(count, { one: "# app hidden", other: "# apps hidden" })}</span>
              </span>
            )}
          </label>
        );
      })}
      <p className="muted platform-hint">
        <Trans>Apps installed here always show.</Trans>
      </p>
      {picked && (
        <button type="button" className="text-button" onClick={() => change([])}>
          <Trans comment="Puts the library's platforms back to this device's own">Just this device's</Trans>
        </button>
      )}
    </div>
  );
}

/** A picture of each layout and card image, for players who don't know the words yet. */
const LAYOUT_ICONS: Record<LibraryView["layout"], ReactNode> = { grid: <LayoutGrid size={15} aria-hidden="true" />, sidebar: <PanelLeft size={15} aria-hidden="true" /> };
const IMAGE_ICONS: Record<CardImage, ReactNode> = {
  cover: <RectangleHorizontal size={15} aria-hidden="true" />,
  box: <RectangleVertical size={15} aria-hidden="true" />,
  icon: <Square size={13} aria-hidden="true" />,
};

/** The sidebar's width, from a few; one dragged to with the mouse is none of them. */
function SidebarWidth() {
  const { settings, setSettings } = useLauncher();
  const picked = SIDEBAR_SIZES.find(([, , width]) => width === settings.sidebarWidth)?.[0] ?? null;
  return (
    <Choice
      label={t`Sidebar width`}
      options={SIDEBAR_SIZES.map(([size, text]) => [size, text])}
      value={picked}
      onChange={(size) => setSettings({ ...settings, sidebarWidth: SIDEBAR_SIZES.find(([s]) => s === size)?.[2] })}
    />
  );
}

/** A few choices side by side, one picked, each with its picture when it has one. */
function Choice<T extends string>({ label, options, icons, value, onChange }: { label: string; options: [T, MessageDescriptor][]; icons?: Record<T, ReactNode>; value: T | null; onChange: (value: T) => void }) {
  return (
    <div className="view-choice">
      <span className="view-label">{label}</span>
      <div className="segmented" role="group" aria-label={label}>
        {options.map(([option, text]) => (
          <button key={option} type="button" aria-pressed={option === value} onClick={() => onChange(option)}>
            {icons?.[option]}
            {i18n._(text)}
          </button>
        ))}
      </div>
    </div>
  );
}

/** About how wide a card's picture shows (app.css's grids), for the size of copy to load: a catalog card's cover is a medium cover's. */
const ART_WIDTHS: Record<CardImage, Record<CardSize, number>> = {
  box: { small: 160, medium: 200, large: 260 },
  icon: { small: 100, medium: 120, large: 160 },
  cover: { small: 240, medium: 300, large: 380 },
};
export const artSizes = (image: CardImage, size: CardSize) => `${ART_WIDTHS[image][size]}px`;
/** About how wide the catalog's box art shows (Browse, a game's ways to play), for the size of copy to load. */
export const CATALOG_BOX = artSizes("box", "medium");

/**
 * A card's box art or icon, at about the size it shows (cardSources: a resized copy, else SteamGridDB's thumbnail,
 * else the picture itself); the next picture when one doesn't load, then the placeholder.
 */
export function CardArt({ entry, image, sizes = artSizes(image, "medium") }: { entry: Pick<Entry, "projectName" | "libraryArt" | "artwork">; image: Exclude<CardImage, "cover">; sizes?: string }) {
  const tries = artFor(entry, image).flatMap((art) => cardSources(art.src, sizes).map((source) => ({ ...source, fit: art.fit, art: art.src })));
  const key = tries.map((a) => a.src).join(" ");
  const [failed, setFailed] = useState({ key, count: 0 });
  const art = tries[failed.key === key ? failed.count : 0];
  const name = entry.projectName;
  if (!art)
    return (
      <div className="card-art placeholder" data-fit="none">
        <Gamepad2 size={44} strokeWidth={1} />
      </div>
    );
  return (
    <div className={`card-art ${art.fit}`} data-fit={art.fit}>
      {art.fit === "letterbox" && <img className="card-art-blur" src={blurSource(art.art)} alt="" aria-hidden="true" loading="lazy" decoding="async" />}
      <img
        key={art.src}
        src={art.src}
        srcSet={art.srcSet}
        sizes={art.sizes}
        alt={image === "box" ? t`${name} box art` : t`${name} icon`}
        loading="lazy"
        decoding="async"
        onError={() => setFailed((f) => ({ key, count: (f.key === key ? f.count : 0) + 1 }))}
      />
    </div>
  );
}
