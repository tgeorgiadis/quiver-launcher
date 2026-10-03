/**
 * Catalog components shared by Quiver Launcher and quiverlauncher.com, so an
 * app looks the same in both. Router-free: callers pass `onOpen` and actions.
 */
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { ArrowUpRight, Clock, Gamepad2 } from "lucide-react";
import type { IconType } from "react-icons";
import { FaAndroid, FaApple, FaLinux, FaWindows } from "react-icons/fa6";
import { SiIos } from "react-icons/si";
import type { Entry } from "@quiver/api";

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

export function PlatformIcons({ os }: { os: readonly string[] }) {
  const shown = Object.keys(OS_ICONS).filter((id) => os.includes(id));
  if (!shown.length) return null;
  const label = shown.map((id) => OS_NAMES[id]).join(", ");
  return (
    <div className="platform-icons" role="img" aria-label={label} title={label}>
      {shown.map((id) => {
        const Icon = OS_ICONS[id];
        return <Icon key={id} aria-hidden />;
      })}
    </div>
  );
}

export function Artwork({ src, name, className = "" }: { src?: string | null; name: string; className?: string }) {
  const [failed, setFailed] = useState(false);
  return (
    <div className={`artwork ${className}`}>
      {src && !failed ? (
        <img src={src} alt={`${name} artwork`} loading="lazy" decoding="async" onError={() => setFailed(true)} />
      ) : (
        <Gamepad2 size={52} strokeWidth={1} />
      )}
    </div>
  );
}

/** Tags are stored lowercase for matching; these are shown as written here (tagLabel.ts on the site). */
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
      SPECIAL[part] ?? (i > 0 && SMALL.has(part) ? part : part.split("-").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("-")),
    )
    .join(" ");
}

const DAY = 24 * 60 * 60 * 1000;
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
export const fullDate = (time: number) =>
  new Date(time).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });

/**
 * One-line text that truncates with an ellipsis, then scrolls back and forth
 * while its card is hovered or focused. `children` shows styled content in
 * place of `text`, which is then only its plain-text version for the tooltip.
 */
export function ScrollingText({ text, children, speed = 18 }: { text: string; children?: ReactNode; speed?: number }) {
  const outer = useRef<HTMLSpanElement>(null);
  const inner = useRef<HTMLSpanElement>(null);
  const [shift, setShift] = useState(0);
  const [active, setActive] = useState(false);
  useLayoutEffect(() => {
    const measure = () => outer.current && setShift(Math.max(0, outer.current.scrollWidth - outer.current.clientWidth));
    measure();
    const observer = new ResizeObserver(measure);
    if (outer.current) observer.observe(outer.current);
    return () => observer.disconnect();
  }, [text, children]);
  useEffect(() => {
    const host = outer.current?.closest(".entry-card");
    if (!host || !shift) return;
    const start = () => !window.matchMedia?.("(prefers-reduced-motion: reduce)").matches && setActive(true);
    const stop = () => setActive(false);
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
export function Score({ runs, issues, broken, scroll = false }: { runs: number; issues: number; broken: number; scroll?: boolean }) {
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

function ReleaseAge({ entry }: { entry: Entry }) {
  const at = entry.lastReleaseAt;
  if (at === undefined) return <span className="release-age none">No releases</span>;
  const title = `${entry.lastReleaseVersion ?? "Latest release"} · ${fullDate(at)}`;
  if (Date.now() - at > 365 * DAY)
    return (
      <span className="release-age stale" title={title}>
        <Clock size={11} /> No release in {Math.floor((Date.now() - at) / (365 * DAY))} yr+
      </span>
    );
  return (
    <span className="release-age" title={title}>
      Updated {relativeTime(at)}
    </span>
  );
}

/** The cover image a card shows: wide library art, else the icon. */
export const coverOf = (entry: Pick<Entry, "libraryArt" | "artwork">) =>
  entry.libraryArt?.header || entry.libraryArt?.capsule || entry.artwork;

/** The catalog card, as on quiverlauncher.com's catalog page. */
export function EntryCard({
  entry,
  onOpen,
  badge,
  action,
  consoleNames = {},
}: {
  entry: Entry;
  onOpen?: () => void;
  /** A label over the cover, such as "In library"; else "New" for a recently added app. */
  badge?: ReactNode;
  /** A button under the card, such as Get or Play. */
  action?: ReactNode;
  /** Console ids to their names, from the catalog's facets. */
  consoleNames?: Record<string, string>;
}) {
  const wide = Boolean(entry.libraryArt?.header || entry.libraryArt?.capsule);
  const kinds = entry.tags.slice(0, 2).map((tag) => consoleNames[tag] ?? tagLabel(tag));
  const fresh = Date.now() - entry.addedAt < 30 * DAY;
  return (
    <article className="entry-card" data-slug={entry.slug}>
      <button type="button" className="card-open" onClick={onOpen} aria-label={entry.projectName}>
        <div className="card-cover">
          <Artwork src={coverOf(entry)} name={entry.projectName} className={wide ? "cover-photo" : ""} />
          {badge ? <span className="cover-badge">{badge}</span> : fresh && <span className="cover-badge">New</span>}
          <span className="card-arrow">
            <ArrowUpRight size={17} />
          </span>
        </div>
        <div className="card-body">
          <div className="card-tags">
            {kinds.length > 0 && (
              <span className="card-kind">
                <ScrollingText text={kinds.join(" · ")} />
              </span>
            )}
            <AiChip level={entry.aiLevel} />
            <PlatformIcons os={entry.supportedOS} />
          </div>
          <h3 className="card-title">
            <ScrollingText text={entry.projectName} />
          </h3>
          {entry.games.length > 0 && (
            <div className="based-on">
              <span className="based-on-label">Based on</span>
              {entry.games.length === 1 ? (
                <span className="game-chip">
                  <ScrollingText text={entry.games[0].title} />
                </span>
              ) : (
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
            </div>
          )}
          <div className="card-bottom">
            <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} scroll />
            <span className="card-meta">
              <ReleaseAge entry={entry} />
            </span>
          </div>
        </div>
      </button>
      {action && <div className="card-action">{action}</div>}
    </article>
  );
}

export { Markdown } from "./markdown";
