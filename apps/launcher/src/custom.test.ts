import { expect, test } from "vitest";
import { parseRepository } from "@quiver/api";
import type { LibraryItem } from "./store";
import { changesFrom, joinAccount } from "./sync";
import { literalPattern } from "./custom";

test("a pasted repository is found on GitHub or GitLab, from any page of it", () => {
  expect(parseRepository("owner/name")).toEqual({ provider: "github", repository: "owner/name" });
  expect(parseRepository("https://github.com/Owner/Name/releases/tag/v1")).toEqual({ provider: "github", repository: "Owner/Name" });
  expect(parseRepository("github.com/owner/name.git")).toEqual({ provider: "github", repository: "owner/name" });
  expect(parseRepository("https://gitlab.com/group/sub/project/-/releases")).toEqual({ provider: "gitlab", repository: "group/sub/project" });
  expect(parseRepository("gitlab.com/group/project.git/")).toEqual({ provider: "gitlab", repository: "group/project" });
  expect(parseRepository("gitlab.com/project")).toBeNull();
  expect(parseRepository("gitlab.com/group/..")).toBeNull();
  expect(parseRepository("not a repository")).toBeNull();
});

test("apps on this computer only stay out of the account", () => {
  const local: LibraryItem = { id: "local:a", slug: "", addedAt: 1, local: { kind: "program", path: "C:\\Games\\a.exe", name: "A" } };
  const guest: LibraryItem = { id: "entry_b", slug: "b", addedAt: 1 };
  const joined = joinAccount([local, guest], "me");
  expect(joined).toContainEqual(local);
  expect(changesFrom(joined, "me").map((c) => c.key)).toEqual(["entry_b"]);
});

test("a picked program's name only matches itself", () => {
  expect(literalPattern("Game [Win] v1*.exe")).toBe("Game [[]Win[]] v1[*].exe");
  expect(literalPattern("game?.exe")).toBe("game[?].exe");
});
