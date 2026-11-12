// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render } from "@testing-library/react";
import {
  Artwork,
  EntryCard,
  blurSource,
  cardSources,
  heroSources,
  EntryCardContent,
  ReleaseBadge,
  badgeState,
  Score,
  ScrollingText,
  isNew,
  isStale,
  platformList,
  relativeTime,
  tagLabel,
  type CardEntry,
} from "./index";

afterEach(cleanup);

const DAY = 24 * 60 * 60 * 1000;

describe("tagLabel", () => {
  it("title-cases plain tags", () => {
    expect(tagLabel("harbour masters")).toBe("Harbour Masters");
    expect(tagLabel("decomp")).toBe("Decomp");
  });
  it("keeps short joining words lowercase except at the start", () => {
    expect(tagLabel("plants vs zombies")).toBe("Plants vs Zombies");
    expect(tagLabel("keyboard and mouse")).toBe("Keyboard and Mouse");
    expect(tagLabel("the legend of dragoon")).toBe("The Legend of Dragoon");
  });
  it("uses known spellings for acronyms and stylised names", () => {
    expect(tagLabel("n64")).toBe("N64");
    expect(tagLabel("yugioh")).toBe("Yu-Gi-Oh!");
    expect(tagLabel("dantes inferno")).toBe("Dante's Inferno");
    expect(tagLabel("split-screen co-op")).toBe("Split-Screen Co-op");
    expect(tagLabel("f-zero")).toBe("F-Zero");
  });
  it("leaves tags that already have casing alone", () => {
    expect(tagLabel("GoldenEye")).toBe("GoldenEye");
  });
  it("treats tags named like built-in properties as plain words", () => {
    expect(tagLabel("constructor")).toBe("Constructor");
    expect(tagLabel("to string")).toBe("To String");
  });
});

describe("date helpers", () => {
  const now = Date.UTC(2026, 8, 27);
  it("formats relative times", () => {
    expect(relativeTime(now - 3_600_000, now)).toBe("today");
    expect(relativeTime(now - DAY, now)).toBe("1 day ago");
    expect(relativeTime(now - 3 * DAY, now)).toBe("3 days ago");
    expect(relativeTime(now - 15 * DAY, now)).toBe("2 wk ago");
    expect(relativeTime(now - 95 * DAY, now)).toBe("3 mo ago");
    expect(relativeTime(now - 400 * DAY, now)).toBe("1 yr ago");
    expect(relativeTime(now - 800 * DAY, now)).toBe("2 yrs ago");
    expect(relativeTime(now + DAY, now)).toBe("today");
  });
  it("flags releases older than a year as stale", () => {
    expect(isStale(undefined, now)).toBe(false);
    expect(isStale(now - 300 * DAY, now)).toBe(false);
    expect(isStale(now - 366 * DAY, now)).toBe(true);
  });
  it("marks entries added in the last 30 days as new", () => {
    expect(isNew(now - 10 * DAY, now)).toBe(true);
    expect(isNew(now - 31 * DAY, now)).toBe(false);
  });
});

it("lists known platforms in a fixed order and leaves out unknown", () => {
  expect(platformList(["android", "unknown", "windows", "linux", "macos"])).toBe("Windows, Linux, macOS, Android");
  expect(platformList(["ios"])).toBe("iOS");
  expect(platformList(["unknown"])).toBe("Not confirmed yet");
  expect(platformList([])).toBe("Not confirmed yet");
});

it("scores only what players said, in words that read naturally", () => {
  const text = (runs: number, issues: number, broken: number) =>
    render(<Score runs={runs} issues={issues} broken={broken} />).container.textContent;
  expect(text(0, 0, 0)).toBe("Not rated yet");
  expect(text(4, 0, 0)).toBe("Runs well · 4 players");
  expect(text(0, 1, 0)).toBe("Has issues · 1 player");
  expect(text(0, 0, 2)).toBe("Doesn't run · 2 players");
  expect(text(5, 1, 0)).toBe("Runs well · 5 run well, 1 with issues");
  expect(text(1, 0, 3)).toBe("Doesn't run · 1 runs well, 3 don't run");
  // A tie shows the more careful answer.
  expect(text(2, 2, 1)).toBe("Has issues · 2 run well, 2 with issues, 1 doesn't run");
  expect(text(0, 2, 2)).toBe("Doesn't run · 2 with issues, 2 don't run");
});

describe("resized pictures", () => {
  const grid = "https://cdn2.steamgriddb.com/grid/fb3fb416095812f23a22302e9efe50b6.png";
  const resized = (width: number, quality: number, url = grid) => `https://quiverlauncher.com/cdn-cgi/image/width=${width},quality=${quality},format=auto,fit=scale-down/${url}`;
  it("asks for a card's picture as the site does, then SteamGridDB's thumbnail, then the picture itself", () => {
    expect(cardSources(grid, "200px")).toEqual([
      { src: resized(480, 70), srcSet: [240, 360, 480, 720, 960].map((w) => `${resized(w, 70)} ${w}w`).join(", "), sizes: "200px" },
      { src: "https://cdn2.steamgriddb.com/thumb/fb3fb416095812f23a22302e9efe50b6.jpg" },
      { src: grid },
    ]);
  });
  it("resizes other pictures on the web, but not an icon file, a vector, or one not on https", () => {
    const icon = "https://raw.githubusercontent.com/a/b/main/icon.png";
    expect(cardSources(icon, "120px").map((s) => s.src)).toEqual([resized(480, 70, icon), icon]);
    for (const url of ["https://example.com/favicon.ico", "https://example.com/logo.svg?v=2", "http://127.0.0.1:4000/cover.png"]) expect(cardSources(url, "120px")).toEqual([{ src: url }]);
  });
  it("asks for a hero across the window, and a small copy for a blurred backdrop", () => {
    const [hero, original] = heroSources(grid);
    expect(hero.src).toBe(resized(1920, 85));
    expect(hero.srcSet).toBe([1280, 1920, 2560, 3840].map((w) => `${resized(w, 85)} ${w}w`).join(", "));
    expect(original).toEqual({ src: grid });
    expect(blurSource(grid)).toBe(resized(240, 70));
    expect(heroSources("http://127.0.0.1/hero.png")).toEqual([{ src: "http://127.0.0.1/hero.png" }]);
  });
});

describe("Artwork", () => {
  it("tries each source in turn, then shows the placeholder", () => {
    const { container } = render(
      <Artwork src="full.png" sources={[{ src: "small.png", srcSet: "small.png 1x, medium.png 2x" }, { src: "full.png" }]} name="Port" />,
    );
    const img = () => container.querySelector("img");
    expect(img()?.getAttribute("src")).toBe("small.png");
    expect(img()?.getAttribute("srcset")).toBe("small.png 1x, medium.png 2x");
    expect(img()?.getAttribute("loading")).toBe("lazy");
    fireEvent.error(img()!);
    expect(img()?.getAttribute("src")).toBe("full.png");
    fireEvent.error(img()!);
    expect(img()).toBeNull();
    expect(container.querySelector(".artwork svg")).not.toBeNull();
  });
  it("loads straight away when asked", () => {
    const { container } = render(<Artwork src="a.png" name="Port" priority />);
    expect(container.querySelector("img")?.getAttribute("loading")).toBe("eager");
    expect(container.querySelector("img")?.getAttribute("fetchpriority")).toBe("high");
  });
});

const entry: CardEntry & { slug: string } = {
  slug: "test-port",
  projectName: "Test Port",
  tags: ["n64", "harbour masters", "decomp"],
  supportedOS: ["windows", "linux"],
  aiLevel: "assisted",
  games: [{ id: "g1", title: "Test Game" }],
  recommended: 3,
  reportIssues: 1,
  reportBroken: 0,
  addedAt: Date.now() - 2 * DAY,
  lastReleaseAt: Date.now() - 400 * DAY,
  lastReleaseVersion: "v1.2",
  libraryArt: { header: "header.png" },
};

describe("ScrollingText", () => {
  it("scrolls a title that doesn't fit while the button it's in is hovered, with no card around it", () => {
    vi.useFakeTimers();
    const widths = { scrollWidth: 300, clientWidth: 200 };
    for (const [name, value] of Object.entries(widths)) Object.defineProperty(HTMLElement.prototype, name, { configurable: true, get: () => value });
    try {
      const { container } = render(
        <button>
          <ScrollingText text="The Legend of Zelda: Ocarina of Time 3D" />
        </button>,
      );
      // A card's name cut short says itself in full over its picture too, where a mouse more often is.
      const card = render(<EntryCard entry={entry} />).container;
      expect(card.querySelector(".card-cover")?.getAttribute("title")).toBe("Test Port");
      const text = container.querySelector(".scrolling-text")!;
      // The whole title on a mouse's tooltip too, cut off as it is.
      expect(text.getAttribute("title")).toBe("The Legend of Zelda: Ocarina of Time 3D");
      expect(text.classList.contains("overflows")).toBe(true);
      fireEvent.pointerEnter(container.querySelector("button")!);
      act(() => void vi.advanceTimersByTime(700));
      expect(text.classList.contains("active")).toBe(true);
      fireEvent.pointerLeave(container.querySelector("button")!);
      expect(text.classList.contains("active")).toBe(false);
    } finally {
      for (const name of Object.keys(widths)) delete (HTMLElement.prototype as unknown as Record<string, unknown>)[name];
      vi.useRealTimers();
    }
  });
});

describe("EntryCard", () => {
  it("shows the box art over the catalog's details, and opens it from its button", () => {
    let opened = 0;
    const { container, getByRole } = render(
      <EntryCard
        entry={{ ...entry, libraryArt: { header: "header.png", capsule: "capsule.png" } }}
        onOpen={() => opened++}
        consoleNames={{ n64: "Nintendo 64" }}
        action={<button>Get</button>}
      />,
    );
    const card = container.querySelector('article.entry-card.box[data-slug="test-port"]')!;
    expect(card.querySelector(".artwork.cover-photo img")?.getAttribute("src")).toBe("capsule.png");
    expect(card.querySelector(".card-title")?.textContent).toBe("Test Port");
    expect(card.querySelector(".card-kind")?.textContent).toBe("Nintendo 64 · Harbour Masters");
    expect(card.querySelector(".card-subtitle")?.textContent).toBe("Test Game");
    expect(card.querySelector(".card-bottom .score.positive")?.textContent).toBe("Runs well · 3 run well, 1 with issues");
    expect(card.querySelector(".cover-strip .ai-chip")?.textContent).toBe("AI-assisted");
    expect(card.querySelector(".cover-platforms .platform-icons")?.getAttribute("aria-label")).toBe("Windows, Linux");
    // A shelf of the newest says New; a badge on every recent box was noise.
    expect(card.querySelector(".cover-badge")).toBeNull();
    // Its name fits: no tooltip over the picture.
    expect(card.querySelector(".card-cover")?.hasAttribute("title")).toBe(false);
    expect(card.querySelector(".card-action")?.textContent).toBe("Get");
    fireEvent.click(getByRole("button", { name: "Test Port" }));
    expect(opened).toBe(1);
  });

  it("shows wide art whole over a blurred copy when there's no box art", () => {
    const { container } = render(<EntryCard entry={{ ...entry, recommended: 0, reportIssues: 0 }} badge="In library" />);
    expect(container.querySelector(".box-fallback .box-fallback-blur")).not.toBeNull();
    expect(container.querySelector(".box-fallback .artwork img")?.getAttribute("src")).toBe("header.png");
    expect(container.querySelector(".card-bottom .release-age.stale")?.textContent).toBe(" No release in 1 yr+");
    expect(container.querySelector(".cover-badge")?.textContent).toBe("In library");
  });

  it("shows the wide look's details, and opens it from its button", () => {
    let opened = 0;
    const { container, getByRole } = render(
      <EntryCard entry={entry} look="wide" onOpen={() => opened++} consoleNames={{ n64: "Nintendo 64" }} action={<button>Get</button>} />,
    );
    const card = container.querySelector('article.entry-card[data-slug="test-port"]')!;
    expect(card.querySelector(".card-kind")?.textContent).toBe("Nintendo 64 · Harbour Masters");
    expect(card.querySelector(".ai-chip")?.textContent).toBe("AI-assisted");
    expect(card.querySelector(".platform-icons")?.getAttribute("aria-label")).toBe("Windows, Linux");
    expect(card.querySelector(".card-title")?.textContent).toBe("Test Port");
    expect(card.querySelector(".based-on .game-chip")?.textContent).toBe("Test Game");
    expect(card.querySelector(".cover-badge")?.textContent).toBe("New");
    expect(card.querySelector(".artwork.cover-photo img")?.getAttribute("src")).toBe("header.png");
    expect(card.querySelector(".release-age.stale")?.textContent).toBe(" No release in 1 yr+");
    // Its name fits: no tooltip over the picture.
    expect(card.querySelector(".card-cover")?.hasAttribute("title")).toBe(false);
    expect(card.querySelector(".card-action")?.textContent).toBe("Get");
    fireEvent.click(getByRole("button", { name: "Test Port" }));
    expect(opened).toBe(1);
  });

  it("holds only phrasing content in its button, as HTML requires", () => {
    const { container } = render(<EntryCard entry={entry} />);
    const inside = container.querySelector(".card-open")!.querySelectorAll("*");
    // Icons' SVG drawing aside.
    const flow = [...inside].filter((el) => !el.closest("svg") && !["SPAN", "IMG", "SMALL"].includes(el.tagName));
    expect(flow.map((el) => el.tagName)).toEqual([]);
  });

  it.each(["box", "wide"] as const)("shows the details picked on a %s card, and leaves the rest out", (look) => {
    const names = { n64: "Nintendo 64" };
    const all = render(<EntryCard entry={{ ...entry, recommended: 0, reportIssues: 0 }} look={look} consoleNames={names} details={["subtitle", "kinds", "rating", "updated", "platforms", "ai", "new"]} />).container;
    expect(all.querySelector(".card-kind")?.textContent).toBe("Nintendo 64 · Harbour Masters");
    expect(all.querySelector(".ai-chip")).not.toBeNull();
    expect(all.querySelector(".platform-icons")).not.toBeNull();
    expect(all.querySelector(".card-subtitle, .based-on .game-chip")?.textContent).toBe("Test Game");
    expect(all.querySelector(".card-bottom .score")?.textContent).toBe("Not rated yet");
    expect(all.querySelector(".card-bottom .release-age")?.textContent).toBe(" No release in 1 yr+");
    expect(all.querySelector(".cover-badge")?.textContent).toBe("New");
    cleanup();
    const none = render(<EntryCard entry={entry} look={look} consoleNames={names} details={["rating"]} />).container;
    expect(none.querySelector(".card-title")?.textContent).toBe("Test Port");
    expect(none.querySelector(".card-kind, .ai-chip, .platform-icons, .cover-strip, .cover-platforms, .card-subtitle, .based-on, .release-age, .cover-badge")).toBeNull();
    expect(none.querySelector(".card-bottom .score")?.textContent).toBe("Runs well · 3 run well, 1 with issues");
  });

  it.each(["box", "wide"] as const)("marks an app with Easy setup on a %s card, while that detail is on", (look) => {
    const easy = { ...entry, launcher: { ...entry.launcher, setupFiles: [{ id: "rom", label: "Game ROM", accepted: [] }] } } as typeof entry;
    const on = render(<EntryCard entry={easy} look={look} details={["setup"]} />).container;
    expect(on.querySelector(".easy-chip")?.textContent).toBe("Easy setup");
    cleanup();
    expect(render(<EntryCard entry={easy} look={look} details={["ai"]} />).container.querySelector(".easy-chip")).toBeNull();
    cleanup();
    expect(render(<EntryCard entry={entry} look={look} details={["setup"]} />).container.querySelector(".easy-chip")).toBeNull();
  });

  it("shows a given badge in place of New, and leaves tags that aren't consoles to tagLabel", () => {
    const { container } = render(<EntryCard entry={{ ...entry, tags: ["constructor"] }} look="wide" badge="In library" />);
    expect(container.querySelector(".cover-badge")?.textContent).toBe("In library");
    expect(container.querySelector(".card-kind")?.textContent).toBe("Constructor");
  });
});

describe("EntryCardContent", () => {
  it("takes a heading, cover and console names from the page it's on", () => {
    const { container } = render(
      <a className="entry-card" href="/apps/test-port">
        <EntryCardContent
          entry={{ ...entry, addedAt: Date.now() - 60 * DAY, games: [{ id: "a", title: "One" }, { id: "b", title: "Two" }] }}
          heading="h2"
          look="wide"
          artwork={<span className="artwork custom" />}
          consoleName={(tag) => (tag === "n64" ? "N64 console" : undefined)}
        />
      </a>,
    );
    expect(container.querySelector("h2.card-title")?.textContent).toBe("Test Port");
    expect(container.querySelector("h2.card-title")?.parentElement?.tagName).toBe("DIV");
    expect(container.querySelector(".artwork.custom")).not.toBeNull();
    expect(container.querySelector(".cover-badge")).toBeNull();
    expect(container.querySelector(".card-kind")?.textContent).toBe("N64 console · Harbour Masters");
    expect([...container.querySelectorAll(".game-chip-row .game-chip")].map((c) => c.textContent)).toEqual(["One", "Two"]);
  });
});

describe("ReleaseBadge", () => {
  it("says whether Quiver verified a release, in its colour and with its shield", () => {
    const { container } = render(
      <>
        <ReleaseBadge state="verified" />
        <ReleaseBadge state="unverified" />
        <ReleaseBadge state="blocked" label="Withdrawn" title="Pulled by a maintainer" />
        <ReleaseBadge state={badgeState({ state: "unverified", assets: [] })} />
        <ReleaseBadge state={badgeState({ state: "unverified", assets: [{}] })} />
        <ReleaseBadge state={badgeState({ state: "blocked", assets: [] })} />
      </>,
    );
    const badges = [...container.querySelectorAll(".release-badge")];
    expect(badges.map((b) => [b.className, b.textContent])).toEqual([
      ["release-badge verified", "Available"],
      ["release-badge unverified", "Not offered"],
      ["release-badge blocked", "Withdrawn"],
      // Nothing to install isn't a wait: it says so in place of "Not offered".
      ["release-badge empty", "No files"],
      ["release-badge unverified", "Not offered"],
      ["release-badge blocked", "Blocked"],
    ]);
    expect(badges.every((b) => b.querySelector("svg[aria-hidden=true]"))).toBe(true);
    expect(badges[2].getAttribute("title")).toBe("Pulled by a maintainer");
  });
});
