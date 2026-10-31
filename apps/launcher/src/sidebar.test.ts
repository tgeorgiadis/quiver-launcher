import { describe, expect, it } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { byWhen, dayLabel, playTimeLabel, sidebarWidth, whenHeading } from "./sidebar";

const now = new Date(2026, 9, 20, 12).getTime();
const at = (y: number, m: number, d: number) => new Date(y, m, d, 12).getTime();
const app = (id: string) => ({ id, projectName: id }) as unknown as Entry;

describe("the library sidebar's headings", () => {
  it("puts the last two weeks under Recent, this year by month, then by year", () => {
    expect(whenHeading(at(2026, 9, 19), now)).toBe("Recent");
    expect(whenHeading(at(2026, 9, 7), now)).toBe("Recent");
    expect(whenHeading(at(2026, 8, 3), now)).toBe("September");
    expect(whenHeading(at(2026, 0, 1), now)).toBe("January");
    expect(whenHeading(at(2025, 11, 31), now)).toBe("2025");
    expect(whenHeading(undefined, now)).toBe("Not played yet");
  });

  it("keeps the order the apps were sorted in", () => {
    const times: Record<string, number | undefined> = { a: at(2026, 9, 18), b: at(2026, 8, 3), c: at(2026, 8, 1), d: at(2024, 2, 2), e: undefined };
    const groups = byWhen(["a", "b", "c", "d", "e"].map(app), (e) => times[e.id], now);
    expect(groups.map(([h, apps]) => [h, apps.map((e) => e.id)])).toEqual([
      ["Recent", ["a"]],
      ["September", ["b", "c"]],
      ["2024", ["d"]],
      ["Not played yet", ["e"]],
    ]);
  });

  it("says the day short", () => {
    expect(dayLabel(now - 60_000, now)).toBe("Today");
    expect(dayLabel(at(2026, 9, 19), now)).toBe("Yesterday");
    expect(dayLabel(at(2026, 8, 3), now)).toBe("Sep 3");
    expect(dayLabel(at(2024, 8, 3), now)).toBe("Sep 3, 2024");
  });

  it("says how long it was played", () => {
    expect(playTimeLabel(0)).toBe("Under a minute");
    expect(playTimeLabel(59)).toBe("Under a minute");
    expect(playTimeLabel(60)).toBe("1 minute");
    expect(playTimeLabel(25 * 60 + 30)).toBe("25 minutes");
    expect(playTimeLabel(3600)).toBe("1 hour");
    expect(playTimeLabel(5400)).toBe("1.5 hours");
    expect(playTimeLabel(438.95 * 3600)).toBe("438.9 hours");
    expect(playTimeLabel(1234 * 3600)).toBe("1,234 hours");
  });
});

describe("the sidebar's width", () => {
  it("stays between its narrowest and widest, leaving the app's page room", () => {
    expect(sidebarWidth(300.4, 1280)).toBe(300);
    expect(sidebarWidth(100, 1280)).toBe(220);
    expect(sidebarWidth(900, 1600)).toBe(640);
    expect(sidebarWidth(600, 1000)).toBe(440);
    // Never narrower than its narrowest, however little room.
    expect(sidebarWidth(600, 700)).toBe(220);
  });
});
