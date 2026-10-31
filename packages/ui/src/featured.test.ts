import { describe, expect, it } from "vitest";
import { FEATURED_POOL, featuredDay, featuredMix, type Pickable } from "./featured";

const art = { hero: "hero.png", logo: "logo.png" };
const app = (id: string, more: Partial<Pickable> = {}): Pickable => ({ id, recommended: 0, reportIssues: 0, reportBroken: 0, libraryArt: art, ...more });
const list = (prefix: string, n: number, more: Partial<Pickable> = {}) => Array.from({ length: n }, (_, i) => app(`${prefix}${i}`, more));
const rated = list("r", 15, { recommended: 5 });
const lists = { rating: rated, updated: list("u", 15), added: list("a", 15) };

describe("featuredMix", () => {
  it("takes the lists in turn, the day's lead first", () => {
    const reasons = (day: number) => featuredMix(lists, day).map((s) => s.reason);
    expect(reasons(0)).toEqual(["rating", "updated", "added", "rating", "updated", "added"]);
    expect(reasons(1)).toEqual(["updated", "added", "rating", "updated", "added", "rating"]);
    expect(reasons(2)[0]).toBe("added");
  });

  it("picks the same all day and others on other days, from each list's first few", () => {
    const ids = (day: number) => featuredMix(lists, day).map((s) => s.entry.id);
    expect(ids(20_000)).toEqual(ids(20_000));
    const days = Array.from({ length: 30 }, (_, i) => ids(20_000 + i * 3).join());
    expect(new Set(days).size).toBeGreaterThan(20);
    const pool = new Set([...lists.rating, ...lists.updated, ...lists.added].filter((e) => Number(e.id.slice(1)) < FEATURED_POOL).map((e) => e.id));
    for (let day = 0; day < 30; day++) for (const id of ids(day)) expect(pool.has(id)).toBe(true);
  });

  it("leaves out apps without the art, mostly AI-generated ones, and ones players say run badly", () => {
    const picked = featuredMix(
      {
        rating: [app("noart", { recommended: 9, libraryArt: { hero: "h" } }), app("ai", { recommended: 9, aiLevel: "generated" }), app("split", { recommended: 1, reportBroken: 1 }), app("good", { recommended: 2 })],
        updated: [app("broken", { reportBroken: 2 }), app("unrated")],
        added: [],
      },
      0,
    );
    expect(picked.map((s) => s.entry.id)).toEqual(["good", "unrated"]);
  });

  it("shows an app on two lists once, and fills a short list's turns from the others", () => {
    const shared = app("both", { recommended: 3 });
    const picked = featuredMix({ rating: [shared], updated: [shared, ...list("u", 8)], added: [] }, 0);
    expect(picked.map((s) => s.entry.id).filter((id) => id === "both")).toHaveLength(1);
    expect(picked).toHaveLength(6);
  });

  it("counts days in UTC", () => {
    expect(featuredDay(Date.UTC(2026, 9, 28, 23, 59))).toBe(featuredDay(Date.UTC(2026, 9, 28, 0, 0)));
    expect(featuredDay(Date.UTC(2026, 9, 29, 0, 0))).toBe(featuredDay(Date.UTC(2026, 9, 28)) + 1);
  });
});
