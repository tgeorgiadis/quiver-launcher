import { expect, test, vi } from "vitest";
import type { Entry } from "@quiverlauncher/api";
import { appsOfRepository, withGame, type GameArt } from "./custom";
import { artFiles, artValues, artValuesOk, fromArtValues, withPictures } from "./custom-art";
import { changesFrom, applyServer } from "./sync";
import { forHereKey, hasUpdate, type Install, type LibraryItem } from "./store";

vi.mock("@tauri-apps/api/core", () => ({ convertFileSrc: (file: string, protocol: string) => `${protocol}://localhost/${file}`, invoke: vi.fn() }));

const entry = { id: "a", libraryArt: { header: "https://c/header", capsule: "https://c/box", hero: "https://c/hero", logo: "https://c/logo" }, artwork: "https://c/icon" } as unknown as Entry;

test("each picture takes its own place, and the rest stay the catalog's", () => {
  expect(withPictures(entry, { pictures: { box: "https://me/box" } }).libraryArt).toEqual({ ...entry.libraryArt, capsule: "https://me/box" });
  expect(withPictures(entry, { pictures: { logo: "https://me/logo" } }).libraryArt?.logo).toBe("https://me/logo");
  expect(withPictures(entry, { pictures: { icon: "https://me/icon" } })).toEqual({ artwork: "https://me/icon" });
  expect(withPictures(entry, {})).toEqual({});
});

test("a cover's address still shows across the app's page, unless it has a banner of its own", () => {
  expect(withPictures(entry, { cover: "https://me/cover" }).libraryArt).toMatchObject({ header: "https://me/cover", hero: "https://me/cover", capsule: "https://c/box" });
  expect(withPictures(entry, { cover: "https://me/cover", pictures: { banner: "https://me/banner" } }).libraryArt).toMatchObject({ header: "https://me/cover", hero: "https://me/banner" });
});

test("a cover from a file wins over the address that synced", () => {
  expect(withPictures(entry, { cover: "https://me/cover", pictures: { cover: "file:cover-1.jpg" } }).libraryArt?.header).toBe("quiverart://localhost/cover-1.jpg");
});

test("the form keeps a cover's address apart, and an address it can't use is refused", () => {
  const values = { ...artValues({}), cover: " https://me/cover ", box: "file:box-1.png" };
  expect(fromArtValues(values)).toEqual({ cover: "https://me/cover", pictures: { box: "file:box-1.png" } });
  expect(fromArtValues({ ...values, cover: "file:cover-1.png" })).toEqual({ cover: undefined, pictures: { box: "file:box-1.png", cover: "file:cover-1.png" } });
  expect(artValuesOk(values)).toBe(true);
  expect(artValuesOk({ ...values, logo: "http://me/logo" })).toBe(false);
  expect(artValues({ cover: "https://me/cover", pictures: { banner: "https://me/banner" } })).toMatchObject({ cover: "https://me/cover", banner: "https://me/banner", box: "" });
});

test("only kept files are kept", () => {
  expect(artFiles([{ box: "file:box-1.png", logo: "https://me/logo" }, undefined, { icon: "file:icon-1.png" }])).toEqual(["box-1.png", "icon-1.png"]);
});

test("pictures stay on this computer: never sent, and the account's values leave them be", () => {
  const item: LibraryItem = { id: "a", slug: "a", addedAt: 1, account: "me", pending: { cover: true }, overrides: { cover: "https://me/cover", pictures: { box: "file:box-1.png" } } };
  expect(changesFrom([item], "me")).toEqual([{ key: "a", artUrl: "https://me/cover" }]);
  const [synced] = applyServer([{ ...item, pending: undefined }], [{ key: "a", artUrl: "https://elsewhere/cover", removed: false, updatedAt: 2 }], "me");
  expect(synced.overrides).toMatchObject({ cover: "https://elsewhere/cover", pictures: { box: "file:box-1.png" } });
});

test("one repository's apps are all found, whatever its address's case", () => {
  const all = [
    { id: "ruby", provider: "github", repository: "MStan/RubySapphire" },
    { id: "sapphire", provider: "github", repository: "mstan/rubysapphire" },
    { id: "other", provider: "gitlab", repository: "mstan/rubysapphire" },
    { id: "none", provider: "github" },
  ];
  expect(appsOfRepository(all, { provider: "github", repository: "mstan/RUBYsapphire" }).map((s) => s.id)).toEqual(["ruby", "sapphire"]);
});

test("another game replaces all of the last one's art, and none clears it", () => {
  const art: GameArt = { game: { id: "g", slug: "g", title: "G" }, consoles: ["n64"], libraryArt: { capsule: "https://g/box" } };
  const had = { ...entry, games: [{ id: "old", slug: "old", title: "Old" }], consoles: ["snes"] };
  expect(withGame(had, art)).toMatchObject({ games: [art.game], consoles: ["n64"], libraryArt: { capsule: "https://g/box" }, artwork: undefined });
  expect(withGame(had, null)).toMatchObject({ games: [], consoles: undefined, libraryArt: undefined, artwork: undefined });
});

test("a local app under a catalog entry is never offered an update", () => {
  const listed = { verified: { version: "v2", releasedAt: 20 } } as unknown as Entry;
  const install: Install = { local: true, version: "", folder: "game.exe", executables: [] };
  expect(hasUpdate(listed, install)).toBe(false);
  const theirs = { ...install, local: undefined, version: "v1", releasedAt: 10 };
  expect(hasUpdate({ ...listed, forHere: { of: forHereKey(listed.verified!, theirs), release: listed.verified } }, theirs)).toBe(true);
});
