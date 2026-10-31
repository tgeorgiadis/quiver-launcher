/**
 * Catalog components shared by Quiver Launcher and quiverlauncher.com, so an
 * app looks the same in both. Router-free: callers pass `onOpen`, links and
 * actions. Styles are in `@quiverlauncher/ui/catalog.css`; README rendering is
 * `@quiverlauncher/ui/markdown`, kept apart because it's large.
 */
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowUpRight, Clock, Gamepad2, ShieldAlert, ShieldCheck, ShieldX, type LucideIcon } from "lucide-react";
import type { IconType } from "react-icons";
import { FaAndroid, FaApple, FaLinux, FaWindows } from "react-icons/fa6";
import { SiIos } from "react-icons/si";
import type { Entry, LibraryArt } from "@quiverlauncher/api";
import type { MessageDescriptor } from "@lingui/core";
import { msg, plural, t } from "@lingui/core/macro";
import { i18n } from "@quiverlauncher/i18n";
import type { AlertKind } from "./markdown";

export const OS_NAMES: Record<string, string> = {
  windows: "Windows",
  linux: "Linux",
  macos: "macOS",
  android: "Android",
  ios: "iOS",
};
export const OS_ICONS: Record<string, IconType> = {
  windows: FaWindows,
  macos: FaApple,
  linux: FaLinux,
  android: FaAndroid,
  ios: SiIos,
};

/**
 * An app's platforms for display, in a fixed order. Release files nobody
 * could match to a platform are stored as "unknown", which isn't one.
 */
export function platformList(os: readonly string[]) {
  const names = Object.keys(OS_NAMES)
    .filter((id) => os.includes(id))
    .map((id) => OS_NAMES[id]);
  return names.length ? names.join(", ") : t`Not confirmed yet`;
}

/** Brand icons for an app's supported platforms, in a fixed order. */
export function PlatformIcons({ os }: { os: readonly string[] }) {
  const shown = Object.keys(OS_ICONS).filter((id) => os.includes(id));
  if (!shown.length) return null;
  const label = shown.map((id) => OS_NAMES[id]).join(", ");
  return (
    <span className="platform-icons" role="img" aria-label={label} title={label}>
      {shown.map((id) => {
        const Icon = OS_ICONS[id];
        return <Icon key={id} aria-hidden />;
      })}
    </span>
  );
}

/** One way to load an image: a URL, with smaller copies for the browser to pick from. */
export type ImageSource = { src: string; srcSet?: string; sizes?: string };

/**
 * quiverlauncher.com's resizer (Cloudflare's): a copy of a picture at the width
 * asked, in the best format the browser takes. The site's pages ask for these
 * same copies, so they're cached already.
 */
const RESIZER = "https://quiverlauncher.com/cdn-cgi/image";
/** SteamGridDB's grids and heroes, which it keeps a small JPEG of too. */
const STEAMGRIDDB = /^https:\/\/cdn2\.steamgriddb\.com\/(?:grid|hero)\/([0-9a-f]+)\.\w+$/i;
const resizable = (url: string) => /^https:\/\//i.test(url) && !/\.(ico|svg)(?:[?#]|$)/i.test(url);
const resized = (url: string, width: number, quality = 85) => `${RESIZER}/width=${Math.min(width, 3840)},quality=${quality},format=auto,fit=scale-down/${url}`;
const CARD_WIDTHS = [240, 360, 480, 720, 960];

/**
 * A card's picture at about the size it shows (`sizes`, as an img's), as the
 * site's cards ask for it: a resized copy, else SteamGridDB's thumbnail, else
 * the picture itself. A 600×900 box art decoded whole for each of a large
 * library's cards made scrolling it stutter on a Steam Deck.
 */
export function cardSources(url: string, sizes: string): ImageSource[] {
  const sources: ImageSource[] = [];
  if (resizable(url)) sources.push({ src: resized(url, 480, 70), srcSet: CARD_WIDTHS.map((w) => `${resized(url, w, 70)} ${w}w`).join(", "), sizes });
  const thumb = STEAMGRIDDB.exec(url);
  if (thumb) sources.push({ src: `https://cdn2.steamgriddb.com/thumb/${thumb[1]}.jpg` });
  sources.push({ src: url });
  return sources;
}

/** A small copy of a card's picture, for a blurred backdrop behind it: no more detail than the blur leaves. */
export const blurSource = (url: string) => (resizable(url) ? resized(url, 240, 70) : url);

/** A wide picture across the window (an app's hero), as the site's pages ask for it, else the picture itself. */
export function heroSources(url: string): ImageSource[] {
  if (!resizable(url)) return [{ src: url }];
  const widths = [1280, 1920, 2560, 3840];
  return [{ src: resized(url, 1920), srcSet: widths.map((w) => `${resized(url, w)} ${w}w`).join(", "), sizes: "100vw" }, { src: url }];
}

/**
 * Tries each source in turn (a smaller copy first, then the original) and
 * calls `onFail` when none of them load.
 */
export function SourcedImage({
  sources,
  alt,
  priority = false,
  className,
  onFail,
}: {
  sources: readonly ImageSource[];
  alt: string;
  /** Above the fold: load straight away instead of lazily. */
  priority?: boolean;
  className?: string;
  onFail: () => void;
}) {
  const [index, setIndex] = useState(0);
  const source = sources[index];
  return (
    <img
      key={source.src}
      className={className}
      src={source.src}
      srcSet={source.srcSet}
      sizes={source.sizes}
      alt={alt}
      loading={priority ? "eager" : "lazy"}
      fetchPriority={priority ? "high" : undefined}
      decoding="async"
      onError={() => (index + 1 < sources.length ? setIndex(index + 1) : onFail())}
    />
  );
}

/** An app's or game's art, or a gamepad when there's none or it doesn't load. */
export function Artwork({
  src,
  sources,
  name,
  className = "",
  priority = false,
}: {
  src?: string | null;
  /** Copies of `src` to try first, such as resized ones; else just `src`. */
  sources?: readonly ImageSource[];
  name: string;
  className?: string;
  /** Above the fold: load straight away instead of lazily. */
  priority?: boolean;
}) {
  const [failed, setFailed] = useState(false);
  return (
    <span className={`artwork ${className}`}>
      {src && !failed ? (
        <SourcedImage
          key={src}
          sources={sources?.length ? sources : [{ src }]}
          alt={t`${name} artwork`}
          priority={priority}
          onFail={() => setFailed(true)}
        />
      ) : (
        <Gamepad2 size={52} strokeWidth={1} />
      )}
    </span>
  );
}

/** Tags are stored lowercase for matching; these are shown as written here. */
const SPECIAL: Record<string, string> = {
  "3ds": "3DS", gb: "GB", gba: "GBA", gbc: "GBC", gcn: "GCN", n64: "N64", nds: "NDS", nes: "NES", snes: "SNES",
  pc: "PC", ps1: "PS1", ps2: "PS2", psp: "PSP", smd: "SMD", ufc: "UFC", wwe: "WWE", wwf: "WWF", wcw: "WCW", nwo: "nWo",
  x360: "X360", xmen: "X-Men", yugioh: "Yu-Gi-Oh!", pokemon: "Pokémon", initiald: "Initial D", naomi2: "NAOMI 2",
  gbarecomp: "GBARecomp", psxrecomp: "PSXRecomp", rexglue: "ReXGlue", einhander: "Einhänder", dantes: "Dante's",
  soulcalibur: "SoulCalibur", "co-op": "Co-op",
};
const SMALL = new Set(["a", "an", "and", "in", "of", "on", "the", "to", "vs"]);

/** "harbour masters" → "Harbour Masters", "n64" → "N64". Leaves already-cased tags alone. */
export function tagLabel(tag: string) {
  if (tag !== tag.toLowerCase()) return tag;
  return tag
    .split(" ")
    .map((part, i) =>
      Object.hasOwn(SPECIAL, part)
        ? SPECIAL[part]
        : i > 0 && SMALL.has(part)
          ? part
          : part.split("-").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("-"),
    )
    .join(" ");
}

const DAY = 24 * 60 * 60 * 1000;
/** A release older than this is called out on its card. */
export const STALE_AFTER_MS = 365 * DAY;
/** An app added within this long is marked New. */
export const NEW_FOR_MS = 30 * DAY;

/** Compact "3 mo ago" style label. */
export function relativeTime(time: number, now = Date.now()) {
  const days = Math.floor(Math.max(0, now - time) / DAY);
  if (days < 1) return t({ message: "today", comment: "When something happened, as in \"Updated today\"" });
  if (days < 7) return plural(days, { one: "# day ago", other: "# days ago" });
  const weeks = Math.floor(days / 7);
  if (days < 30) return plural(weeks, { one: "# wk ago", other: "# wk ago" });
  const months = Math.floor(days / 30);
  if (days < 365) return plural(months, { one: "# mo ago", other: "# mo ago" });
  const years = Math.floor(days / 365);
  return plural(years, { one: "# yr ago", other: "# yrs ago" });
}
export const isStale = (lastReleaseAt: number | undefined, now = Date.now()) =>
  lastReleaseAt !== undefined && now - lastReleaseAt > STALE_AFTER_MS;
export const isNew = (addedAt: number, now = Date.now()) => now - addedAt < NEW_FOR_MS;
export const fullDate = (time: number) =>
  new Date(time).toLocaleDateString(i18n.locale, { year: "numeric", month: "short", day: "numeric" });

/**
 * One-line text that truncates with an ellipsis, then scrolls back and forth
 * while its card is hovered or focused, from a moment after: not on each card
 * a controller passes on its way down a long grid, where starting and stopping
 * its animation had WebKit rebuild the page's layers with every step. `children` shows styled content in
 * place of `text`, which is then only its plain-text version for the tooltip.
 */
export function ScrollingText({
  text,
  children,
  speed = 18,
}: {
  text: string;
  children?: ReactNode;
  /** Pixels a second; a long row of chips reads better faster than text. */
  speed?: number;
}) {
  const outer = useRef<HTMLSpanElement>(null);
  const inner = useRef<HTMLSpanElement>(null);
  const [shift, setShift] = useState(0);
  const [active, setActive] = useState(false);
  useLayoutEffect(() => {
    const measure = () => outer.current && setShift(Math.max(0, outer.current.scrollWidth - outer.current.clientWidth));
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(measure);
    if (outer.current) observer.observe(outer.current);
    return () => observer.disconnect();
  }, [text, children]);
  useEffect(() => {
    const host = outer.current?.closest(".entry-card");
    if (!host || !shift) return;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const start = () => {
      clearTimeout(timer);
      if (!window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) timer = setTimeout(() => setActive(true), 700);
    };
    const stop = () => {
      clearTimeout(timer);
      setActive(false);
    };
    // focusin/focusout bubble, so this works whether the card itself or a button in it has focus.
    const events = [["pointerenter", start], ["pointerleave", stop], ["focusin", start], ["focusout", stop]] as const;
    events.forEach(([name, fn]) => host.addEventListener(name, fn));
    return () => {
      clearTimeout(timer);
      events.forEach(([name, fn]) => host.removeEventListener(name, fn));
    };
  }, [shift]);
  useEffect(() => {
    const el = inner.current;
    if (!active || !el?.animate) return;
    const move = (shift / speed) * 1000;
    const pause = 1200;
    const total = 2 * move + 2 * pause;
    const end = `translateX(-${shift}px)`;
    const animation = el.animate(
      [
        { transform: "translateX(0)", offset: 0 },
        { transform: end, offset: move / total },
        { transform: end, offset: (move + pause) / total },
        { transform: "translateX(0)", offset: (2 * move + pause) / total },
        { transform: "translateX(0)", offset: 1 },
      ],
      { duration: total, iterations: Infinity, easing: "linear" },
    );
    return () => animation.cancel();
  }, [active, shift, speed]);
  return (
    <span ref={outer} className={`scrolling-text${shift ? " overflows" : ""}${active ? " active" : ""}`} title={shift ? text : undefined}>
      <span ref={inner}>{children ?? text}</span>
    </span>
  );
}

/** What players said about an app, in one line. */
export function Score({
  runs,
  issues,
  broken,
  scroll = false,
}: {
  runs: number;
  issues: number;
  broken: number;
  /** On a catalog card: truncate to one line and scroll it while the card is hovered. */
  scroll?: boolean;
}) {
  if (runs + issues + broken === 0) return <span className="score muted">{t`Not rated yet`}</span>;
  // Always one verdict, as ProtonDB always shows one tier: what most players
  // said, and when it's a tie the more careful of the answers tied (the
  // counts after it show the split).
  const max = Math.max(runs, issues, broken);
  const [label, tone] =
    broken === max ? [t`Doesn't run`, "negative"] : issues === max ? [t`Has issues`, "caution"] : [t`Runs well`, "positive"];
  // Only what people actually said, in words that read naturally.
  const said = [
    runs && plural(runs, { one: "# runs well", other: "# run well" }),
    issues && plural(issues, { one: "# with issues", other: "# with issues" }),
    broken && plural(broken, { one: "# doesn't run", other: "# don't run" }),
  ].filter(Boolean);
  // Everyone agrees: "Runs well · 4 players", rather than repeating the verdict.
  const total = runs + issues + broken;
  const counts = ` · ${said.length === 1 ? plural(total, { one: "# player", other: "# players" }) : said.join(", ")}`;
  const content = (
    <>
      {label}
      <small>{counts}</small>
    </>
  );
  return <span className={`score ${tone}`}>{scroll ? <ScrollingText text={label + counts}>{content}</ScrollingText> : content}</span>;
}

/** A small neutral chip; nothing is shown when no AI use is known. */
export function AiChip({ level }: { level?: Entry["aiLevel"] }) {
  if (!level || level === "none") return null;
  return (
    <span className="ai-chip" title={level === "assisted" ? t`Built with some AI assistance` : t`Mostly AI-generated`}>
      {level === "assisted" ? t`AI-assisted` : t`Mostly AI`}
    </span>
  );
}

/** What a catalog card shows of an app: an `Entry`, or the site's own catalog row. */
export type CardEntry = {
  projectName: string;
  tags: readonly string[];
  supportedOS: readonly string[];
  aiLevel?: Entry["aiLevel"];
  games: readonly { id: string; title: string }[];
  recommended: number;
  reportIssues: number;
  reportBroken: number;
  addedAt: number;
  lastReleaseAt?: number;
  lastReleaseVersion?: string;
  libraryArt?: LibraryArt;
  artwork?: string;
};

function ReleaseAge({ entry }: { entry: CardEntry }) {
  const at = entry.lastReleaseAt;
  if (at === undefined) return <span className="release-age none">{t`No releases`}</span>;
  const title = `${entry.lastReleaseVersion ?? t`Latest release`} · ${fullDate(at)}`;
  if (isStale(at)) {
    const years = Math.floor((Date.now() - at) / STALE_AFTER_MS);
    return (
      <span className="release-age stale" title={title}>
        <Clock size={11} /> {plural(years, { one: "No release in # yr+", other: "No release in # yr+" })}
      </span>
    );
  }
  const when = relativeTime(at);
  return (
    <span className="release-age" title={title}>
      {t({ message: `Updated ${when}`, comment: "when: \"today\", \"3 days ago\", \"2 wk ago\"…" })}
    </span>
  );
}

/**
 * Whether Quiver checked a release: verified (published, its files
 * checked), unverified (not yet, or never), or blocked (withdrawn, taken
 * down, stopped, or flagged, so it's never installed).
 */
export type ReleaseState = "verified" | "unverified" | "blocked";
// Put in the language when shown, not here, where the module loads before any is picked.
const RELEASE_STATES: Record<ReleaseState, { icon: LucideIcon; label: MessageDescriptor; title: MessageDescriptor }> = {
  verified: { icon: ShieldCheck, label: msg({ message: "Verified", comment: "A release Quiver checked" }), title: msg`Its files have each been out for 48 hours, a maintainer checked any that VirusTotal flagged, and Quiver Launcher checks every download against the file Quiver verified.` },
  unverified: { icon: ShieldAlert, label: msg({ message: "Unverified", comment: "A release Quiver hasn't checked" }), title: msg`Quiver hasn't verified this release` },
  blocked: { icon: ShieldX, label: msg({ message: "Blocked", comment: "A release Quiver won't install" }), title: msg`Quiver won't install this release` },
};

/** A release's state, green, orange or red, with its shield. */
export function ReleaseBadge({ state, label, title }: { state: ReleaseState; label?: ReactNode; title?: string }) {
  const { icon: Icon, ...words } = RELEASE_STATES[state];
  return (
    <span className={`release-badge ${state}`} title={title ?? i18n._(words.title)}>
      <Icon size={13} aria-hidden="true" />
      {label ?? i18n._(words.label)}
    </span>
  );
}

/**
 * GitHub's alert headings in the page's language, for Markdown's
 * `alertTitles`: here, not in markdown.tsx, which the site's Worker renders
 * without Lingui's macros.
 */
export const alertTitles = (): Record<AlertKind, string> => ({
  note: t({ message: "Note", context: "README callout", comment: "Heading of a README's note callout, as on GitHub" }),
  tip: t({ message: "Tip", context: "README callout", comment: "Heading of a README's tip callout, as on GitHub" }),
  important: t({ message: "Important", context: "README callout", comment: "Heading of a README's important callout, as on GitHub" }),
  warning: t({ message: "Warning", context: "README callout", comment: "Heading of a README's warning callout, as on GitHub" }),
  caution: t({ message: "Caution", context: "README callout", comment: "Heading of a README's caution callout, as on GitHub" }),
});

/** The cover image a card shows: wide library art, else the icon. */
export const coverOf = (entry: Pick<CardEntry, "libraryArt" | "artwork">) =>
  entry.libraryArt?.header || entry.libraryArt?.capsule || entry.artwork;

/** The picture a box-art card shows: the portrait capsule, else the wide art (shown whole), else the icon. */
export const boxOf = (entry: Pick<CardEntry, "libraryArt" | "artwork">) =>
  entry.libraryArt?.capsule || entry.libraryArt?.header || entry.artwork;

/**
 * How a card looks. Box: the portrait box art, as a store shelf shows games, with the app's name, its game and
 * console, and one line of what players said under it. Wide: the wide library art over the catalog's details.
 */
export type CardLook = "box" | "wide";

/** A box-art card's picture: the capsule; wide art shown whole over a blurred copy of itself; or the icon. */
function BoxArt({ entry, sources, name }: { entry: CardEntry; sources?: readonly ImageSource[]; name: string }) {
  const src = boxOf(entry);
  if (entry.libraryArt?.capsule || !src) return <Artwork src={src} sources={sources} name={name} className={src ? "cover-photo" : ""} />;
  return (
    <span className="box-fallback">
      {entry.libraryArt?.header && <img className="box-fallback-blur" src={blurSource(src)} alt="" aria-hidden="true" loading="lazy" decoding="async" />}
      <Artwork src={src} sources={sources} name={name} />
    </span>
  );
}

/**
 * Under a box card's title (and a library card's, in the launcher): the games it's based on and its console, or its
 * kinds when it's no game's.
 */
export function boxSubtitle(entry: CardEntry, consoleName?: (tag: string) => string | undefined) {
  const system = entry.tags.map((tag) => consoleName?.(tag)).find(Boolean);
  const games = entry.games.map((g) => g.title).join(", ");
  const kinds = entry.tags.slice(0, 2).map((tag) => consoleName?.(tag) ?? tagLabel(tag));
  return (games ? [games, system] : kinds).filter(Boolean).join(" · ");
}

/**
 * What a catalog card shows: its cover and details. The caller wraps it in
 * an `.entry-card` that opens the app, such as `EntryCard`'s button or a
 * link. With the default `span` title it's all phrasing content, which a
 * button may hold; a heading title puts the details in a `div`.
 */
export function EntryCardContent({
  entry,
  artwork,
  coverSources,
  badge,
  consoleName,
  heading: Title = "span",
  status,
  look = "box",
}: {
  entry: CardEntry;
  /** The cover, in place of the default `Artwork` of `boxOf(entry)` (a box card) or `coverOf(entry)` (a wide one). */
  artwork?: ReactNode;
  /** Copies of the default cover to try first, such as resized ones (`cardSources`). */
  coverSources?: readonly ImageSource[];
  /** Box art and three short lines (the default), or the wide art over the catalog's details. */
  look?: CardLook;
  /** A label over the cover, such as "In library"; else "New" for a recently added app. */
  badge?: ReactNode;
  /** A console's name for a tag that is one; other tags are shown with `tagLabel`. */
  consoleName?: (tag: string) => string | undefined;
  /** The title's element: a heading where the card isn't inside a button. */
  heading?: "h2" | "h3" | "h4" | "span";
  /**
   * A line about the player's own copy ("Played yesterday", "Not installed")
   * in place of the catalog's details: the launcher's library. Without a
   * badge, such a card isn't marked New either.
   */
  status?: ReactNode;
}) {
  const wide = Boolean(entry.libraryArt?.header || entry.libraryArt?.capsule);
  // A heading isn't phrasing content, so it can't sit in a span.
  const Body = Title === "span" ? "span" : "div";
  const kinds = entry.tags.slice(0, 2).map((tag) => consoleName?.(tag) ?? tagLabel(tag));
  const shelf = status !== undefined;
  if (look === "box") {
    const subtitle = boxSubtitle(entry, consoleName);
    const rated = entry.recommended + entry.reportIssues + entry.reportBroken > 0;
    const os = entry.supportedOS.filter((id) => Object.hasOwn(OS_ICONS, id));
    return (
      <>
        <span className="card-cover">
          {artwork ?? <BoxArt entry={entry} sources={coverSources} name={entry.projectName} />}
          {/* No New on a box: a shelf of the newest says it, where a badge on every recent card was noise. */}
          {badge && <span className="cover-badge">{badge}</span>}
          {!shelf && os.length > 0 && (
            <span className="cover-os">
              <PlatformIcons os={os} />
            </span>
          )}
          {entry.aiLevel && entry.aiLevel !== "none" && (
            <span className="cover-ai">
              <AiChip level={entry.aiLevel} />
            </span>
          )}
        </span>
        <Body className="card-body">
          <Title className="card-title">
            <ScrollingText text={entry.projectName} />
          </Title>
          {subtitle && (
            <span className="card-subtitle">
              <ScrollingText text={subtitle} />
            </span>
          )}
          {shelf ? (
            <span className="card-status">{status}</span>
          ) : (
            <span className="card-line">{rated ? <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} scroll /> : <ReleaseAge entry={entry} />}</span>
          )}
        </Body>
      </>
    );
  }
  return (
    <>
      <span className="card-cover">
        {artwork ?? <Artwork src={coverOf(entry)} sources={coverSources} name={entry.projectName} className={wide ? "cover-photo" : ""} />}
        {badge ? <span className="cover-badge">{badge}</span> : !shelf && isNew(entry.addedAt) && <span className="cover-badge">{t({ message: "New", comment: "Badge on an app added to the catalog lately" })}</span>}
        <span className="card-arrow">
          <ArrowUpRight size={17} />
        </span>
      </span>
      <Body className="card-body">
        {shelf ? (
          <>
            <Title className="card-title">
              <ScrollingText text={entry.projectName} />
            </Title>
            <span className="card-status">{status}</span>
          </>
        ) : (
          <>
            <span className="card-tags">
              {kinds.length > 0 && (
                <span className="card-kind">
                  <ScrollingText text={kinds.join(" · ")} />
                </span>
              )}
              <AiChip level={entry.aiLevel} />
              <PlatformIcons os={entry.supportedOS} />
            </span>
            <Title className="card-title">
              <ScrollingText text={entry.projectName} />
            </Title>
            {entry.games.length > 0 && (
              <span className="based-on">
                <span className="based-on-label">{t({ message: "Based on", comment: "Before the game(s) an app is made from" })}</span>
                {entry.games.length === 1 ? (
                  <span className="game-chip">
                    <ScrollingText text={entry.games[0].title} />
                  </span>
                ) : (
                  // Several games keep full-size chips; the row scrolls on hover.
                  <span className="game-chip-row">
                    <ScrollingText text={entry.games.map((g) => g.title).join(", ")} speed={45}>
                      {entry.games.map((game) => (
                        <span key={game.id} className="game-chip">
                          {game.title}
                        </span>
                      ))}
                    </ScrollingText>
                  </span>
                )}
              </span>
            )}
            <span className="card-bottom">
              <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} scroll />
              <span className="card-meta">
                <ReleaseAge entry={entry} />
              </span>
            </span>
          </>
        )}
      </Body>
    </>
  );
}

/** The catalog card as a button, as in the launcher; the site wraps `EntryCardContent` in a link instead. */
export function EntryCard({
  entry,
  onOpen,
  badge,
  action,
  consoleNames = {},
  cover,
  coverSources,
  className,
  status,
  look = "box",
}: {
  entry: CardEntry & { slug: string };
  /** Box art and three short lines (the default), or the wide art over the catalog's details. */
  look?: CardLook;
  /** A picture in place of the cover (the launcher's box art or icon cards). */
  cover?: ReactNode;
  /** Copies of the cover to try first, such as resized ones (`cardSources`). */
  coverSources?: readonly ImageSource[];
  /** More classes on the card. */
  className?: string;
  onOpen?: () => void;
  /** A label over the cover, such as "In library"; else "New" for a recently added app. */
  badge?: ReactNode;
  /** A button under the card, such as Get or Play. */
  action?: ReactNode;
  /** Console ids to their names, from the catalog's facets. */
  consoleNames?: Record<string, string>;
  /** A line about the player's own copy, in place of the catalog's details (`EntryCardContent`). */
  status?: ReactNode;
}) {
  const consoleName = (tag: string) => (Object.hasOwn(consoleNames, tag) ? consoleNames[tag] : undefined);
  return (
    <article className={["entry-card", look, className].filter(Boolean).join(" ")} data-slug={entry.slug}>
      <button type="button" className="card-open" onClick={onOpen} aria-label={entry.projectName}>
        <EntryCardContent entry={entry} artwork={cover} coverSources={coverSources} badge={badge} consoleName={consoleName} status={status} look={look} />
      </button>
      {action && <div className="card-action">{action}</div>}
    </article>
  );
}
