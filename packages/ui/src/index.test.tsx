// @vitest-environment jsdom
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render } from "@testing-library/react";
import {
  Artwork,
  EntryCard,
  EntryCardContent,
  ReleaseBadge,
  Score,
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

describe("EntryCard", () => {
  it("shows the app's details, and opens it from its button", () => {
    let opened = 0;
    const { container, getByRole } = render(
      <EntryCard entry={entry} onOpen={() => opened++} consoleNames={{ n64: "Nintendo 64" }} action={<button>Get</button>} />,
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

  it("shows a given badge in place of New, and leaves tags that aren't consoles to tagLabel", () => {
    const { container } = render(<EntryCard entry={{ ...entry, tags: ["constructor"] }} badge="In library" />);
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
      </>,
    );
    const badges = [...container.querySelectorAll(".release-badge")];
    expect(badges.map((b) => [b.className, b.textContent])).toEqual([
      ["release-badge verified", "Verified"],
      ["release-badge unverified", "Unverified"],
      ["release-badge blocked", "Withdrawn"],
    ]);
    expect(badges.every((b) => b.querySelector("svg[aria-hidden=true]"))).toBe(true);
    expect(badges[2].getAttribute("title")).toBe("Pulled by a maintainer");
  });
});
