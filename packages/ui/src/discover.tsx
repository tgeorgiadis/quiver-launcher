/**
 * The catalog's front page, shared by Quiver Launcher's Browse and quiverlauncher.com's home: a few featured apps
 * across the top, then shelves of box art that scroll sideways. Router-free like the rest of the package: callers
 * render each shelf's cards, and say how a featured app opens (a link on the site, a button in the launcher).
 */
import { Children, useEffect, useRef, useState, type ReactNode } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { msg, t } from "@lingui/core/macro";
import { Score, SourcedImage, heroSources, type CardEntry, type ImageSource } from "./index";
import type { FeaturedReason } from "./featured";

export { featuredDay, featuredMix, runsWell, type FeaturedReason } from "./featured";

/** A featured app: a card's details, with the wide art and logo a slide is made of. */
export type FeaturedEntry = CardEntry & { id: string; description?: string };
/** A slide: the app, and the list it was picked from, which the slide says. */
export type FeaturedSlide = { entry: FeaturedEntry; reason?: FeaturedReason };

/** Apps on a shelf. */
export const SHELF_SIZE = 20;
/**
 * The consoles with the most apps in the catalog (n64, x360 and ps1 have about a hundred each, the original Xbox
 * forty), a shelf each, in this order, where the catalog has them.
 */
export const CONSOLE_SHELVES = ["n64", "x360", "ps1", "og xbox"];

// Put in the language when shown, not here, where the module loads before any is picked.
export const SHELF_TITLES = {
  rating: msg({ message: "Players say it runs well", comment: "A shelf on the catalog's front page: the apps players rated best" }),
  updated: msg({ message: "Just updated", comment: "A shelf on the catalog's front page: apps with a new release lately" }),
  added: msg({ message: "New in the catalog", comment: "A shelf on the catalog's front page: apps added lately" }),
};
export const SEE_ALL = msg({ message: "See all", comment: "Beside a shelf's title on the catalog's front page: its whole list" });
export const MORE_LIKE_THIS = msg({ message: "More like this", comment: "A shelf at the foot of an app's page: the best rated apps for other games on its console" });

/**
 * More like an app, for the shelf at the foot of its page: from a list of its console's best rated, the apps for other
 * games (its own game's other ways to play are on that game's page).
 */
export const likeThis = <T extends CardEntry & { id: string }>(entry: Pick<T, "id" | "games">, candidates: readonly T[], count = 14) =>
  candidates.filter((e) => e.id !== entry.id && !e.games.some((g) => entry.games.some((h) => h.id === g.id))).slice(0, count);
export const ALL_APPS = msg({ message: "All apps", comment: "Heading over the whole catalog, under the front page's shelves" });

const reducedMotion = () => typeof window !== "undefined" && Boolean(window.matchMedia?.("(prefers-reduced-motion: reduce)").matches);

/**
 * A few apps, one at a time across the page, each its game's wide art with the logo over it, why it's featured, the
 * app's name, its console and what players said. It moves on by itself every `interval`, except under the pointer, with the focus in
 * it, or when the player asked for less motion. The slides not showing are inert, so focus passes them by. Left and right
 * on a slide (the arrow keys, or a controller's in the launcher, which leaves them to it: data-arrows) go to the one before
 * or after, the focus going along.
 */
export function FeaturedCarousel({
  slides,
  consoleName,
  open,
  sources = heroSources,
  interval = 8000,
}: {
  /** The apps, as `featuredMix` picks them. */
  slides: readonly FeaturedSlide[];
  /** A console's name for a tag that is one. */
  consoleName?: (tag: string) => string | undefined;
  /** A slide's way in, around its picture and words: a link on the site, a button in the launcher. */
  open: (entry: FeaturedEntry, content: ReactNode, props: { className: string; "aria-label": string }) => ReactNode;
  /** Copies of a slide's wide art to load, such as resized ones, which the site's Worker preloads too. */
  sources?: (hero: string) => ImageSource[];
  interval?: number;
}) {
  const [index, setIndex] = useState(0);
  const [paused, setPaused] = useState(false);
  const count = slides.length;
  const shown = count ? index % count : 0;
  useEffect(() => {
    if (paused || count < 2 || reducedMotion()) return;
    const timer = setTimeout(() => setIndex((i) => (i + 1) % count), interval);
    return () => clearTimeout(timer);
  }, [paused, count, interval, shown]);
  // Stepped to by an arrow key: the focus to the slide now showing, from the one gone inert.
  const all = useRef<HTMLDivElement>(null);
  const stepped = useRef(false);
  useEffect(() => {
    if (!stepped.current) return;
    stepped.current = false;
    all.current?.querySelector<HTMLElement>("[data-current] .featured-open")?.focus({ preventScroll: true, focusVisible: true } as FocusOptions);
  }, [shown]);
  if (!count) return null;
  const go = (step: number) => setIndex((shown + step + count) % count);
  return (
    <section
      className="featured"
      aria-roledescription={t({ message: "carousel", comment: "What the featured apps across the top of Browse are, for screen readers" })}
      aria-label={t({ message: "Featured", comment: "The apps shown one at a time across the top of Browse and the website's home" })}
      onPointerEnter={() => setPaused(true)}
      onPointerLeave={() => setPaused(false)}
      onFocus={() => setPaused(true)}
      onBlur={(e) => !e.currentTarget.contains(e.relatedTarget as Node | null) && setPaused(false)}
    >
      <div
        ref={all}
        className="featured-slides"
        data-arrows="x"
        onKeyDown={(e) => {
          if (count < 2 || (e.key !== "ArrowLeft" && e.key !== "ArrowRight")) return;
          e.preventDefault();
          stepped.current = true;
          go(e.key === "ArrowLeft" ? -1 : 1);
        }}
      >
        {slides.map(({ entry, reason }, i) => {
          const system = entry.tags.map((tag) => consoleName?.(tag)).find(Boolean);
          const game = entry.games[0]?.title;
          const content = (
            <>
              <FeaturedArt entry={entry} sources={sources} priority={i === 0} />
              <span className="featured-text">
                {reason && <span className="featured-reason">{reasonLabel(reason, entry)}</span>}
                {(system || game) && <span className="featured-eyebrow">{[system, game].filter(Boolean).join(" · ")}</span>}
                <FeaturedLogo entry={entry} />
                <span className="featured-name">{entry.projectName}</span>
                {entry.description && <span className="featured-description">{entry.description}</span>}
                {entry.recommended + entry.reportIssues + entry.reportBroken > 0 && <Score runs={entry.recommended} issues={entry.reportIssues} broken={entry.reportBroken} />}
              </span>
            </>
          );
          return (
            <div key={entry.id} className="featured-slide" data-current={i === shown || undefined} aria-hidden={i !== shown} inert={i !== shown}>
              {open(entry, content, { className: "featured-open", "aria-label": entry.projectName })}
            </div>
          );
        })}
      </div>
      {count > 1 && (
        <div className="featured-controls">
          <button type="button" className="featured-step" aria-label={t`Previous featured app`} onClick={() => go(-1)}>
            <ChevronLeft size={18} aria-hidden="true" />
          </button>
          {slides.map(({ entry }, i) => {
            const name = entry.projectName;
            return (
              <button
                key={entry.id}
                type="button"
                className="featured-dot"
                aria-label={t({ message: `Show ${name}`, comment: "A dot under the featured apps: shows that app's slide" })}
                aria-current={i === shown || undefined}
                onClick={() => setIndex(i)}
              />
            );
          })}
          <button type="button" className="featured-step" aria-label={t`Next featured app`} onClick={() => go(1)}>
            <ChevronRight size={18} aria-hidden="true" />
          </button>
        </div>
      )}
    </section>
  );
}

/** Why a slide's app is featured: the list it's from, or for a new release, its version. */
function reasonLabel(reason: FeaturedReason, entry: FeaturedEntry) {
  const version = entry.lastReleaseVersion;
  if (reason === "rating") return t({ message: "Top rated", context: "one app", comment: "On a featured app's slide: it's among the apps players rated best" });
  if (reason === "updated")
    return version
      ? t({ message: `New release: ${version}`, comment: "On a featured app's slide: it has just had a release, of this version" })
      : t({ message: "Just updated", context: "one app", comment: "On a featured app's slide: it has just had a release" });
  return t({ message: "New in the catalog", context: "one app", comment: "On a featured app's slide: it was added to the catalog lately" });
}

/**
 * The slide's art with its shade, in one box that moves as one (catalog.css): the art moving under a shade of its own
 * showed a strip of it unshaded along the banner's foot in WebKitGTK, where the two met the rounded edge apart.
 */
function FeaturedArt({ entry, sources, priority }: { entry: FeaturedEntry; sources: (hero: string) => ImageSource[]; priority: boolean }) {
  const [failed, setFailed] = useState(false);
  const hero = entry.libraryArt?.hero;
  if (!hero || failed) return <span className="featured-art featured-art-missing" />;
  return (
    <span className="featured-art">
      <SourcedImage sources={sources(hero)} alt="" priority={priority} onFail={() => setFailed(true)} />
    </span>
  );
}

/** The game's logo, else the app's name in large type. */
function FeaturedLogo({ entry }: { entry: FeaturedEntry }) {
  const [failed, setFailed] = useState(false);
  const logo = entry.libraryArt?.logo;
  const game = entry.games[0]?.title ?? entry.projectName;
  if (!logo || failed) return <span className="featured-title">{game}</span>;
  return <img className="featured-logo" src={logo} alt={game} decoding="async" onError={() => setFailed(true)} />;
}

/**
 * The row's room for cards, inside the padding that leaves room for a card's lift and ring, and the gap between them.
 * `--shelf-card` is the width a card aims for.
 */
function rowRoom(el: HTMLElement) {
  const style = getComputedStyle(el);
  const width = el.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
  return { width, gap: parseFloat(style.columnGap) || 0, card: parseFloat(style.getPropertyValue("--shelf-card")) || 176 };
}

/**
 * How wide a shelf's cards are, so a whole number of them fills its row: as many as fit at about `card` wide, grown
 * to fill what's left. Cards at a set width left part of the next one showing at the row's end at some window sizes.
 */
export function fitCards(width: number, gap: number, card: number) {
  const count = Math.max(1, Math.floor((width + gap) / (card + gap)));
  return { count, width: (width - gap * (count - 1)) / count };
}

/**
 * A titled row of cards that scrolls sideways: by its arrows, a touchpad or a finger, or (in the launcher) by moving
 * the focus along it. It shows only whole cards, and its arrows move on by a rowful. While its cards load, grey boxes
 * keep their place so the page doesn't jump.
 */
export function Shelf({
  title,
  action,
  loading = false,
  children,
}: {
  title: string;
  /** Beside the title, such as See all. */
  action?: ReactNode;
  /** Placeholders in place of cards that haven't come yet. */
  loading?: boolean;
  children?: ReactNode;
}) {
  const row = useRef<HTMLDivElement>(null);
  const [ends, setEnds] = useState({ start: true, end: true });
  // Measured again as its cards come or go, not each time the page renders them again (a download's progress, say).
  const cards = Children.count(children);
  useEffect(() => {
    const el = row.current;
    if (!el) return;
    const measure = () => {
      const now = { start: el.scrollLeft <= 1, end: el.scrollLeft + el.clientWidth >= el.scrollWidth - 1 };
      setEnds((was) => (was.start === now.start && was.end === now.end ? was : now));
    };
    const fit = () => {
      const room = rowRoom(el);
      const width = room.width > 0 ? `${fitCards(room.width, room.gap, room.card).width}px` : "";
      // Only a new width: setting it restyles every card on the row.
      if (width && el.style.getPropertyValue("--shelf-fit") !== width) el.style.setProperty("--shelf-fit", width);
      measure();
    };
    fit();
    el.addEventListener("scroll", measure, { passive: true });
    const observer = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(fit);
    observer?.observe(el);
    return () => {
      el.removeEventListener("scroll", measure);
      observer?.disconnect();
    };
  }, [loading, cards]);
  const page = (way: -1 | 1) => {
    const el = row.current;
    if (!el) return;
    // A rowful: the cards that show, and the gap after the last.
    const room = rowRoom(el);
    el.scrollBy({ left: way * (room.width + room.gap), behavior: reducedMotion() ? "auto" : "smooth" });
  };
  return (
    <section className="shelf" aria-label={title}>
      <header className="shelf-head">
        <h2>{title}</h2>
        {action}
        <span className="shelf-arrows">
          {/* Mouse helpers: the focus moves along the row itself, so these stay out of its way. */}
          <button type="button" tabIndex={-1} aria-label={t`Scroll ${title} back`} disabled={ends.start} onClick={() => page(-1)}>
            <ChevronLeft size={18} aria-hidden="true" />
          </button>
          <button type="button" tabIndex={-1} aria-label={t`Scroll ${title} on`} disabled={ends.end} onClick={() => page(1)}>
            <ChevronRight size={18} aria-hidden="true" />
          </button>
        </span>
      </header>
      <div className="shelf-row" ref={row}>
        {loading ? Array.from({ length: 8 }, (_, i) => <span key={i} className="shelf-placeholder" aria-hidden="true" />) : children}
      </div>
    </section>
  );
}
