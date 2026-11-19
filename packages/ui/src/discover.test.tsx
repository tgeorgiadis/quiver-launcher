// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render } from "@testing-library/react";
import { FeaturedCarousel, fitCards, type FeaturedSlide } from "./discover";

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("fitCards", () => {
  it("fits as many whole cards as there's room for at about the width asked, grown to fill the row", () => {
    // 1340 across, 22 between: six of 176 fit (1166), and grow to 205 each.
    const fit = fitCards(1340, 22, 176);
    expect(fit.count).toBe(6);
    expect(fit.width * 6 + 22 * 5).toBeCloseTo(1340);
  });

  it("keeps at least one card, however narrow the row", () => {
    expect(fitCards(120, 22, 176)).toEqual({ count: 1, width: 120 });
  });
});

describe("FeaturedCarousel", () => {
  const slide = (id: string): FeaturedSlide => ({
    entry: { id, projectName: id, tags: [], supportedOS: [], games: [], recommended: 0, reportIssues: 0, reportBroken: 0, addedAt: 0 },
  });
  const show = (opened = vi.fn()) => {
    const view = render(
      <FeaturedCarousel
        slides={["Alpha", "Beta", "Gamma"].map(slide)}
        open={(entry, content, props) => (
          <button type="button" {...props} onClick={() => opened(entry.id)}>
            {content}
          </button>
        )}
      />,
    );
    const slides = view.container.querySelector<HTMLElement>(".featured-slides")!;
    const current = () => view.container.querySelector<HTMLElement>("[data-current] .featured-open")!;
    // A finger down on the slide showing, moved by `x` and `y`, and lifted.
    const drag = (x: number, y = 0, pointerType = "touch") => {
      const at = current();
      fireEvent.pointerDown(at, { pointerId: 1, pointerType, clientX: 200, clientY: 100 });
      fireEvent.pointerUp(at, { pointerId: 1, pointerType, clientX: 200 + x, clientY: 100 + y });
    };
    return { slides, current, drag, opened };
  };

  it("steps to the next app on a swipe to the left, and back on one to the right", () => {
    const { current, drag } = show();
    expect(current().getAttribute("aria-label")).toBe("Alpha");
    drag(-120);
    expect(current().getAttribute("aria-label")).toBe("Beta");
    drag(120);
    drag(120);
    expect(current().getAttribute("aria-label")).toBe("Gamma");
  });

  it("leaves a tap's wobble, a move mostly up or down, a scroll the page took over and a mouse's drag alone", () => {
    const { slides, current, drag } = show();
    drag(-12);
    drag(-60, 90);
    drag(-120, 0, "mouse");
    fireEvent.pointerDown(current(), { pointerId: 2, pointerType: "touch", clientX: 200, clientY: 100 });
    fireEvent.pointerCancel(slides, { pointerId: 2, pointerType: "touch" });
    fireEvent.pointerUp(current(), { pointerId: 2, pointerType: "touch", clientX: 40, clientY: 100 });
    expect(current().getAttribute("aria-label")).toBe("Alpha");
  });

  it("doesn't open the slide a swipe stepped to with the tap the browser may make of it, but does a tap after", () => {
    const { current, drag, opened } = show();
    drag(-120);
    fireEvent.click(current());
    expect(opened).not.toHaveBeenCalled();
    vi.spyOn(performance, "now").mockReturnValue(performance.now() + 1000);
    fireEvent.click(current());
    expect(opened).toHaveBeenCalledWith("Beta");
  });
});
