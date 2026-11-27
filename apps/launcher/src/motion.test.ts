// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { enter, enterCards } from "./motion";

type Animate = ReturnType<typeof vi.fn>;

function animated(element: Element = document.createElement("div")) {
  const animate = vi.fn();
  (element as unknown as { animate: Animate }).animate = animate;
  return { element, animate };
}

function prefersReducedMotion(reduce: boolean) {
  window.matchMedia = ((query: string) => ({ matches: reduce && query.includes("reduce") })) as unknown as typeof window.matchMedia;
}

const RISE = { opacity: 0, transform: "translateY(8px)" };
const FADE = [{ opacity: 0 }, { opacity: 1 }];

afterEach(() => {
  document.documentElement.removeAttribute("data-navigating");
  prefersReducedMotion(false);
});

describe("enter", () => {
  it("fades in and rises over the base time", () => {
    prefersReducedMotion(false);
    const { element, animate } = animated();
    enter(element);
    const [frames, timing] = animate.mock.calls[0];
    expect(frames[0]).toEqual(RISE);
    expect(timing).toMatchObject({ duration: 200, delay: 0, fill: "none" });
  });

  it("only fades when reduced motion is asked for", () => {
    prefersReducedMotion(true);
    const { element, animate } = animated();
    enter(element);
    expect(animate.mock.calls[0][0]).toEqual(FADE);
  });

  it("only fades while moving by the keys or a controller, so nothing is out of place for the focus box", () => {
    document.documentElement.setAttribute("data-navigating", "");
    const { element, animate } = animated();
    enter(element);
    expect(animate.mock.calls[0][0]).toEqual(FADE);
  });

  it("only fades, and quickly, for a page's tabs", () => {
    const { element, animate } = animated();
    enter(element, { fade: true });
    expect(animate.mock.calls[0][0]).toEqual(FADE);
    expect(animate.mock.calls[0][1].duration).toBe(120);
  });

  it("does nothing where elements can't animate", () => {
    expect(() => enter(document.createElement("div"))).not.toThrow();
    expect(() => enter(null)).not.toThrow();
  });
});

describe("enterCards", () => {
  /** A screen with a search field, then cards at these places (top, left; 0 wide when out of the layout). */
  function screen(...boxes: { top: number; left: number; width?: number }[]) {
    const root = document.createElement("div");
    root.innerHTML = `<div class="toolbar"><input type="search"></div>${boxes.map(() => `<article class="entry-card"></article>`).join("")}`;
    const cards = [...root.querySelectorAll(".entry-card")].map((card, i) => {
      const { top, left, width = 200 } = boxes[i];
      card.getBoundingClientRect = () => ({ top, left, width, height: 280, bottom: top + 280, right: left + width }) as DOMRect;
      return animated(card).animate;
    });
    const toolbar = animated(root.querySelector(".toolbar")!).animate;
    return { root, cards, toolbar };
  }

  it("brings in only the cards in sight, together, and nothing else", () => {
    window.innerHeight = 800;
    // Second row first in the markup; one below the window; one hidden.
    const { root, cards, toolbar } = screen({ top: 400, left: 0 }, { top: 100, left: 220 }, { top: 100, left: 0 }, { top: 1200, left: 0 }, { top: 100, left: 440, width: 0 });
    enterCards(root);
    expect(toolbar).not.toHaveBeenCalled();
    for (const card of cards.slice(0, 3)) expect(card.mock.calls[0][1]).toMatchObject({ delay: 0 });
    expect(cards[3]).not.toHaveBeenCalled();
    expect(cards[4]).not.toHaveBeenCalled();
  });
});
