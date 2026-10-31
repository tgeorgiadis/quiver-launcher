/**
 * Apps the player adds themselves, which the catalog doesn't list.
 * - Custom apps: a GitHub or GitLab repository. They show and install like
 *   catalog apps (unchecked), from a stand-in entry built from the repository
 *   and its latest release, and sync with the account.
 * - Local apps: a program already on this computer, or a folder in the apps
 *   folder the player fills. This device only; never downloaded, updated,
 *   moved or deleted.
 */
import { t } from "@lingui/core/macro";
import type { Client, Entry, GameDetail, LibraryArt, Os, Release } from "@quiverlauncher/api";
import type { CatalogEntry } from "./store";

export type CustomApp = { provider: "github" | "gitlab"; repository: string; name: string; assetFilter?: string };
export type LocalApp = { kind: "program"; path: string; name: string } | { kind: "folder"; folder: string; name: string };
/** A game's artwork and consoles, matched by name, for an app the player added. */
export type GameArt = { game: { id: string; slug: string; title: string }; artwork?: string; libraryArt?: LibraryArt; consoles: string[] };

export const customKey = (provider: CustomApp["provider"], repository: string) => `${provider}:${repository.toLowerCase()}`;
export const isCustom = (id: string) => /^(github|gitlab):/.test(id);
export const isLocal = (id: string) => id.startsWith("local:");
/** Added by the player: no catalog page, reviews or checked releases. */
export const isOwn = (id: string) => isCustom(id) || isLocal(id);
export const localKey = () => `local:${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;

/** The games an app's name matches, best first. File names drop apostrophes ("Marios Tennis"): without a match, the words without a final s. */
export function gamesNamed(client: Client, name: string) {
  const loose = name.replace(/\b(\w{3,})s\b/gi, "$1");
  return client.matchingGames(name).then((found) => (found.length || loose === name ? found : client.matchingGames(loose)));
}

/** A game's artwork and consoles, from its page. */
export const gameArt = (slug: string, { game }: GameDetail): GameArt => ({
  game: { id: game.id, slug, title: game.title },
  artwork: game.artwork,
  libraryArt: game.libraryArt,
  consoles: game.originalSystems,
});

/** The best-matching game's artwork and consoles for an app's name, as adding an app picks them; undefined without one. */
export async function bestGameArt(client: Client, name: string): Promise<GameArt | undefined> {
  const [best] = await gamesNamed(client, name).catch(() => []);
  const detail = best && (await client.game(best.slug).catch(() => null));
  return detail ? gameArt(best.slug, detail) : undefined;
}

/** The game's artwork, consoles and "Based on" in place of the app's own. */
const withArt = (entry: CatalogEntry, art?: GameArt): CatalogEntry =>
  art
    ? { ...entry, games: [art.game], consoles: art.consoles, artwork: art.artwork ?? entry.artwork, libraryArt: art.libraryArt ?? entry.libraryArt }
    : entry;

const stub = (id: string, name: string, description: string): CatalogEntry => ({
  id,
  slug: "",
  name,
  projectName: name,
  description,
  games: [],
  tags: [],
  launcher: { folderName: "", filesToAdd: [] },
  projectType: "port",
  supportedOS: ["unknown"],
  recommended: 0,
  reviewCount: 0,
  reportIssues: 0,
  reportBroken: 0,
  addedAt: 0,
});

export function customEntry(id: string, app: CustomApp, latest?: Release, art?: GameArt): CatalogEntry {
  const oses = [...new Set(latest?.assets.map((a) => a.os) ?? [])] as Os[];
  const site = `${app.provider}.com/${app.repository}`;
  const entry: CatalogEntry = {
    ...stub(id, app.name, t`Added from ${site}. Quiver hasn't checked this app.`),
    launcher: { folderName: app.repository.split("/").at(-1)!, filesToAdd: [], releaseAssetFilter: app.assetFilter },
    supportedOS: oses.length ? oses : ["unknown"],
    ...(latest
      ? {
          verified: {
            version: latest.version,
            releasedAt: latest.releasedAt,
            prerelease: latest.prerelease,
            pinned: latest.assets.length > 0 && latest.assets.every((a) => a.checksum),
          },
        }
      : {}),
  };
  return withArt(entry, art);
}

export function localEntry(id: string, app: LocalApp, art?: GameArt): CatalogEntry {
  let where: string;
  if (app.kind === "program") {
    const path = app.path;
    where = t`Starts ${path} on this computer.`;
  } else {
    const folder = app.folder;
    where = t`Starts the program you put in its folder, ${folder}.`;
  }
  return withArt(stub(id, app.name, where), art) satisfies Entry;
}

/** A folder name for an app's name: what Windows, macOS and Linux all allow. */
export function folderNameFor(name: string) {
  const clean = name
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/[. ]+$/, "");
  return /^(con|prn|aux|nul|com\d|lpt\d)(\.|$)/i.test(clean) ? `${clean} app` : clean || "App";
}

/**
 * The folder an app installs to: the one it's already in, so an update never
 * leaves its old files behind. Otherwise the one it asks for, unless another
 * app in the library is there (two catalog apps can ask for the same one);
 * then that one with a number.
 */
export function freeFolder(wanted: string, current: string | undefined, taken: string[]) {
  const used = new Set(taken.map((f) => f.toLowerCase()));
  const free = (f: string) => !used.has(f.toLowerCase());
  if (current && free(current)) return current;
  if (free(wanted)) return wanted;
  for (let n = 2; ; n++) if (free(`${wanted} (${n})`)) return `${wanted} (${n})`;
}
