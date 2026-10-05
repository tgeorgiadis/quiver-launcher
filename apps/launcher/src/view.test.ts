import { expect, test } from "vitest";
import { artFor, savedView, viewOf, DEFAULT_VIEW } from "./view";

test("a library view keeps only what differs from today's look, and ignores what it doesn't know", () => {
  expect(viewOf(undefined)).toEqual(DEFAULT_VIEW);
  expect(viewOf({ image: "icon", size: "huge" as never, names: false })).toEqual({ ...DEFAULT_VIEW, image: "icon", names: false });
  expect(savedView(DEFAULT_VIEW)).toBeUndefined();
  expect(savedView({ ...DEFAULT_VIEW, layout: "list" })).toEqual({ layout: "list" });
});

test("box art and icon cards fall back to the cover, then to nothing", () => {
  const all = { artwork: "icon", libraryArt: { capsule: "box", header: "cover" } };
  expect(artFor(all, "box")).toEqual([
    { src: "box", fit: "fill" },
    { src: "cover", fit: "letterbox" },
    { src: "icon", fit: "icon" },
  ]);
  expect(artFor(all, "icon")).toEqual([
    { src: "icon", fit: "icon" },
    { src: "cover", fit: "letterbox" },
  ]);
  expect(artFor({ libraryArt: { header: "cover" } }, "box")).toEqual([{ src: "cover", fit: "letterbox" }]);
  expect(artFor({}, "icon")).toEqual([]);
});
