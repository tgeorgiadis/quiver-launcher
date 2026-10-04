/**
 * Apps the player adds themselves, which the catalog doesn't list.
 * - Custom apps: a GitHub or GitLab repository. They show and install like
 *   catalog apps (unchecked), from a stand-in entry built from the repository
 *   and its latest release, and sync with the account.
 * - Local apps: a program already on this computer, or a folder in the apps
 *   folder the player fills. This device only; never downloaded, updated,
 *   moved or deleted.
 */
import type { Entry, LibraryArt, Os, Release } from "@quiver/api";
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
  const entry: CatalogEntry = {
    ...stub(id, app.name, `Added from ${app.provider}.com/${app.repository}. Quiver hasn't checked this app.`),
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
  const where = app.kind === "program" ? `Starts ${app.path} on this computer.` : `Starts the program you put in its folder, ${app.folder}.`;
  return withArt(stub(id, app.name, where), art) satisfies Entry;
}

/** A file name as a pattern that matches only itself (the launcher's executables are glob patterns). */
export const literalPattern = (name: string) => name.replace(/[[\]*?]/g, (c) => `[${c}]`);
