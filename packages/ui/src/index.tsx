/**
 * Catalog components shared by Quiver Launcher and quiverlauncher.com, so an
 * app looks the same in both. Router-free: callers pass `onOpen`, links and
 * actions. Styles are in `@quiverlauncher/ui/catalog.css`; README rendering is
 * `@quiverlauncher/ui/markdown`, kept apart because it's large.
 */
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowUpRight, Clock, Gamepad2 } from "lucide-react";
import type { IconType } from "react-icons";
import { FaAndroid, FaApple, FaLinux, FaWindows } from "react-icons/fa6";
import { SiIos } from "react-icons/si";
import type { Entry, LibraryArt } from "@quiverlauncher/api";

export const OS_NAMES: Record<string, string> = {
  windows: "Windows",
  linux: "Linux",
  macos: "macOS",
  android: "Android",
  ios: "iOS",
};
const OS_ICONS: Record<string, IconType> = {
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
  return names.length ? names.join(", ") : "Not confirmed yet";
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
 * Tries each source in turn (a smaller copy first, then the original) and
 * calls `onFail` when none of them load.
 */
export function SourcedImage({
  sources,
  alt,
  priority = false,
  onFail,
}: {
  sources: readonly ImageSource[];
  alt: string;
  /** Above the fold: load straight away instead of lazily. */
  priority?: boolean;
  onFail: () => void;
}) {
  const [index, setIndex] = useState(0);
  const source = sources[index];
  return (
    <img
      key={source.src}
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
          alt={`${name} artwork`}
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
  if (days < 1) return "today";
  if (days < 7) return `${days} day${days === 1 ? "" : "s"} ago`;
  if (days < 30) return `${Math.floor(days / 7)} wk ago`;
  if (days < 365) return `${Math.floor(days / 30)} mo ago`;
  const years = Math.floor(days / 365);
  return `${years} yr${years === 1 ? "" : "s"} ago`;
}
export const isStale = (lastReleaseAt: number | undefined, now = Date.now()) =>
  lastReleaseAt !== undefined && now - lastReleaseAt > STALE_AFTER_MS;
export const isNew = (addedAt: number, now = Date.now()) => now - addedAt < NEW_FOR_MS;
export const fullDate = (time: number) =>
  new Date(time).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });

/**
 * One-line text that truncates with an ellipsis, then scrolls back and forth
 * while its card is hovered or focused. `children` shows styled content in
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
    const start = () => !window.matchMedia?.("(prefers-reduced-motion: reduce)").matches && setActive(true);
    const stop = () => setActive(false);
    // focusin/focusout bubble, so this works whether the card itself or a button in it has focus.
    const events = [["pointerenter", start], ["pointerleave", stop], ["focusin", start], ["focusout", stop]] as const;
    events.forEach(([name, fn]) => host.addEventListener(name, fn));
    return () => events.forEach(([name, fn]) => host.removeEventListener(name, fn));
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
  if (runs + issues + broken === 0) return <span className="score muted">Not rated yet</span>;
  const max = Math.max(runs, issues, broken);
  const mixed = [runs, issues, broken].filter((n) => n === max).length > 1;
  const [label, tone] = mixed
    ? ["Mixed", "muted"]
    : runs === max
      ? ["Mostly runs", "positive"]
      : issues === max
        ? ["Mostly runs with issues", "caution"]
        : ["Mostly doesn't run", "negative"];
  // Only what people actually said, in words that read naturally.
  const said = [
    runs && `${runs} ${runs === 1 ? "runs" : "run"} well`,
    issues && `${issues} with issues`,
    broken && `${broken} ${broken === 1 ? "doesn't" : "don't"} run`,
  ].filter(Boolean);
  const counts = ` · ${said.join(", ")}`;
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
    <span className="ai-chip" title={level === "assisted" ? "Built with some AI assistance" : "Mostly AI-generated"}>
      {level === "assisted" ? "AI-assisted" : "Mostly AI"}
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
  if (at === undefined) return <span className="release-age none">No releases</span>;
  const title = `${entry.lastReleaseVersion ?? "Latest release"} · ${fullDate(at)}`;
  if (isStale(at))
    return (
      <span className="release-age stale" title={title}>
        <Clock size={11} /> No release in {Math.floor((Date.now() - at) / STALE_AFTER_MS)} yr+
      </span>
    );
  return (
    <span className="release-age" title={title}>
      Updated {relativeTime(at)}
    </span>
  );
}

/** The cover image a card shows: wide library art, else the icon. */
export const coverOf = (entry: Pick<CardEntry, "libraryArt" | "artwork">) =>
  entry.libraryArt?.header || entry.libraryArt?.capsule || entry.artwork;

/**
 * What a catalog card shows: its cover and details. The caller wraps it in
 * an `.entry-card` that opens the app, such as `EntryCard`'s button or a
 * link, so it's all phrasing content, which a button may hold.
 */
export function EntryCardContent({
  entry,
  artwork,
  badge,
  consoleName,
  heading: Title = "span",
}: {
  entry: CardEntry;
  /** The cover, in place of the default `Artwork` of `coverOf(entry)`. */
  artwork?: ReactNode;
  /** A label over the cover, such as "In library"; else "New" for a recently added app. */
  badge?: ReactNode;
  /** A console's name for a tag that is one; other tags are shown with `tagLabel`. */
  consoleName?: (tag: string) => string | undefined;
  /** The title's element: a heading where the card isn't inside a button. */
  heading?: "h2" | "h3" | "span";
}) {
  const wide = Boolean(entry.libraryArt?.header || entry.libraryArt?.capsule);
  const kinds = entry.tags.slice(0, 2).map((tag) => consoleName?.(tag) ?? tagLabel(tag));
  return (
    <>
      <span className="card-cover">
        {artwork ?? <Artwork src={coverOf(entry)} name={entry.projectName} className={wide ? "cover-photo" : ""} />}
        {badge ? <span className="cover-badge">{badge}</span> : isNew(entry.addedAt) && <span className="cover-badge">New</span>}
        <span className="card-arrow">
          <ArrowUpRight size={17} />
        </span>
      </span>
      <span className="card-body">
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
            <span className="based-on-label">Based on</span>
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
      </span>
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
}: {
  entry: CardEntry & { slug: string };
  onOpen?: () => void;
  /** A label over the cover, such as "In library"; else "New" for a recently added app. */
  badge?: ReactNode;
  /** A button under the card, such as Get or Play. */
  action?: ReactNode;
  /** Console ids to their names, from the catalog's facets. */
  consoleNames?: Record<string, string>;
}) {
  const consoleName = (tag: string) => (Object.hasOwn(consoleNames, tag) ? consoleNames[tag] : undefined);
  return (
    <article className="entry-card" data-slug={entry.slug}>
      <button type="button" className="card-open" onClick={onOpen} aria-label={entry.projectName}>
        <EntryCardContent entry={entry} badge={badge} consoleName={consoleName} />
      </button>
      {action && <div className="card-action">{action}</div>}
    </article>
  );
}
