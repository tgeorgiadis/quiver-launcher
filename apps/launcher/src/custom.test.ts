import { expect, test } from "vitest";
import { parseRepository, type Client, type GameDetail, type GameMatch } from "@quiverlauncher/api";
import type { LibraryItem } from "./store";
import { changesFrom, joinAccount } from "./sync";
import { bestGameArt, folderNameFor, freeFolder } from "./custom";

test("a pasted repository is found on GitHub or GitLab, from any page of it", () => {
  expect(parseRepository("owner/name")).toEqual({ provider: "github", host: "github.com", repository: "owner/name" });
  expect(parseRepository("https://github.com/Owner/Name/releases/tag/v1")).toEqual({ provider: "github", host: "github.com", repository: "Owner/Name" });
  expect(parseRepository("github.com/owner/name.git")).toEqual({ provider: "github", host: "github.com", repository: "owner/name" });
  expect(parseRepository("https://gitlab.com/group/sub/project/-/releases")).toEqual({ provider: "gitlab", host: "gitlab.com", repository: "group/sub/project" });
  expect(parseRepository("gitlab.com/group/project.git/")).toEqual({ provider: "gitlab", host: "gitlab.com", repository: "group/project" });
  expect(parseRepository("gitlab.com/project")).toBeNull();
  expect(parseRepository("gitlab.com/group/..")).toBeNull();
  expect(parseRepository("some.owner/repo")).toEqual({ provider: "github", host: "github.com", repository: "some.owner/repo" });
  expect(parseRepository("not a repository")).toBeNull();
});

test("Gitea, Forgejo, Bitbucket and an unknown forge are found from their addresses", () => {
  expect(parseRepository("https://codeberg.org/forgejo/forgejo")).toEqual({ provider: "gitea", host: "codeberg.org", repository: "forgejo/forgejo" });
  expect(parseRepository("git.ryujinx.app/Ryubing/Ryubing/releases")).toEqual({ provider: "gitea", host: "git.ryujinx.app", repository: "Ryubing/Ryubing" });
  expect(parseRepository("https://bitbucket.org/workspace/project/downloads")).toEqual({ provider: "bitbucket", host: "bitbucket.org", repository: "workspace/project" });
  expect(parseRepository("git.example.com/group/sub/project")).toEqual({ provider: "unknown", host: "git.example.com", repository: "group/sub/project" });
  expect(parseRepository("git.example.com/group/sub/project", "gitlab")).toEqual({ provider: "gitlab", host: "git.example.com", repository: "group/sub/project" });
  expect(parseRepository("https://example.com/feed.json", "feed")).toEqual({ provider: "feed", host: "example.com", repository: "feed.json" });
});

test("apps from a host the account doesn't know stay on this computer", () => {
  const item: LibraryItem = {
    id: "gitea:codeberg.org/o/r",
    slug: "",
    addedAt: 1,
    custom: { provider: "gitea", host: "codeberg.org", repository: "o/r", name: "R" },
    account: "me",
    pending: { added: true },
  };
  expect(changesFrom([item], "me")).toEqual([]);
});

test("apps on this computer only stay out of the account", () => {
  const local: LibraryItem = { id: "local:a", slug: "", addedAt: 1, local: { kind: "program", path: "C:\\Games\\a.exe", name: "A" } };
  const guest: LibraryItem = { id: "entry_b", slug: "b", addedAt: 1 };
  const joined = joinAccount([local, guest], "me");
  expect(joined).toContainEqual(local);
  expect(changesFrom(joined, "me").map((c) => c.key)).toEqual(["entry_b"]);
});

test("a folder you fill takes its name from the app's, minus what folders can't have", () => {
  expect(folderNameFor("Zelda: Ocarina of Time")).toBe("Zelda Ocarina of Time");
  expect(folderNameFor("Who Wants to Be a Millionaire?")).toBe("Who Wants to Be a Millionaire");
  expect(folderNameFor("Super Mario Bros.")).toBe("Super Mario Bros");
  expect(folderNameFor("CON")).toBe("CON app");
  expect(folderNameFor(" ?? ")).toBe("App");
});

test("two apps that ask for the same folder each get their own", () => {
  expect(freeFolder("SSX3-SSX3Recomp", undefined, [])).toBe("SSX3-SSX3Recomp");
  expect(freeFolder("SSX3-SSX3Recomp", undefined, ["ssx3-ssx3recomp"])).toBe("SSX3-SSX3Recomp (2)");
  expect(freeFolder("SSX3-SSX3Recomp", undefined, ["SSX3-SSX3Recomp", "SSX3-SSX3Recomp (2)"])).toBe("SSX3-SSX3Recomp (3)");
  // An update stays where it was installed, even once the folder it asks for is free.
  expect(freeFolder("SSX3-SSX3Recomp", "SSX3-SSX3Recomp (2)", ["SSX3-SSX3Recomp"])).toBe("SSX3-SSX3Recomp (2)");
  expect(freeFolder("SSX3-SSX3Recomp", "SSX3-SSX3Recomp (2)", [])).toBe("SSX3-SSX3Recomp (2)");
});

test("an app brought over takes the best-matching game's artwork and consoles, trying the name without a final s", async () => {
  const searched: string[] = [];
  const games: Record<string, GameMatch[]> = { "Mario Tenni": [{ slug: "mario-tennis", title: "Mario Tennis", apps: 1 }] };
  const client = {
    matchingGames: async (name: string) => (searched.push(name), games[name] ?? []),
    game: async (slug: string) => ({ game: { id: "g1", slug, title: "Mario Tennis", originalSystems: ["n64"] }, entries: [] }) as unknown as GameDetail,
  } as unknown as Client;
  expect(await bestGameArt(client, "Marios Tennis")).toEqual({ game: { id: "g1", slug: "mario-tennis", title: "Mario Tennis" }, consoles: ["n64"] });
  expect(searched).toEqual(["Marios Tennis", "Mario Tenni"]);
  expect(await bestGameArt(client, "Nothing")).toBeUndefined();
});
