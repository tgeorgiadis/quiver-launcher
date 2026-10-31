import { describe, expect, it } from "vitest";
import { createClient, type Announcement } from "@quiverlauncher/api";
import { bannerAnnouncement, keptDismissals, sorted } from "./announcements";

const news = (id: string): Announcement => ({ id, message: id, parts: [{ text: id }], publishedAt: 0 });
// Newest first, as the site lists them.
const list = ["e", "d", "c", "b", "a"].map(news);

describe("announcements", () => {
  it("come from one address, and a site without them has none", async () => {
    const asked: string[] = [];
    const site = (answer: Response) =>
      createClient("https://api.test", { fetch: (async (url: string) => (asked.push(url), answer.clone())) as typeof fetch });
    expect(await site(new Response(JSON.stringify({ items: list }))).announcements()).toEqual(list);
    expect(asked).toEqual(["https://api.test/announcements"]);
    expect(await site(new Response("{}", { status: 404 })).announcements()).toEqual([]);
    await expect(site(new Response("{}", { status: 500 })).announcements()).rejects.toThrow();
  });

  it("splits the dismissed ones out, keeping the order", () => {
    expect(sorted(list, ["d", "a"])).toEqual({ shown: [news("e"), news("c"), news("b")], dismissed: [news("d"), news("a")] });
    expect(sorted(list).shown).toHaveLength(5);
  });

  it("puts only the newest in the banner, never an older one in its place", () => {
    expect(bannerAnnouncement(list)).toEqual(news("e"));
    expect(bannerAnnouncement(list, ["e"])).toBeNull();
    expect(bannerAnnouncement([])).toBeNull();
  });

  it("forgets dismissals of announcements the site stopped listing", () => {
    expect(keptDismissals(["e", "old", "b"], list)).toEqual(["e", "b"]);
  });
});
