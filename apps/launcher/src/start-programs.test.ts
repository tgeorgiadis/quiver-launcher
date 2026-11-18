import { beforeEach, expect, test, vi } from "vitest";
import { ApiError } from "@quiverlauncher/api";
import { reportMissedPrograms, startPatterns } from "./start-programs";

const launcher = (preferredExecutables?: Record<string, string[]>) => ({ launcher: { folderName: "Game", filesToAdd: [], ...(preferredExecutables ? { preferredExecutables } : {}) } });

test("Play looks for the catalog's programs as they are now, else the ones saved at install", () => {
  const install = { executables: ["Old.exe"] };
  expect(startPatterns(install, launcher({ windows: ["New.exe"] }), "windows")).toEqual(["New.exe"]);
  // The catalog dropped them: the automatic pick, not the stale ones.
  expect(startPatterns(install, launcher(), "windows")).toEqual([]);
  // Through Wine on Linux: Windows' programs.
  expect(startPatterns({ ...install, wine: true }, launcher({ windows: ["New.exe"], linux: ["game.AppImage"] }), "linux")).toEqual(["New.exe"]);
  expect(startPatterns(install, undefined, "windows")).toEqual(["Old.exe"]);
  expect(startPatterns({ ...install, local: true }, launcher({ windows: ["New.exe"] }), "windows")).toEqual(["Old.exe"]);
});

const install = { folder: "Game", version: "v1.2" };
const entry = { id: "e1", slug: "game", catalogId: "qcat_1" };

beforeEach(() => {
  const kept = new Map<string, string>();
  vi.stubGlobal("localStorage", { getItem: (k: string) => kept.get(k) ?? null, setItem: (k: string, v: string) => void kept.set(k, v) });
});

test("a missing start program is reported once for each app and version", async () => {
  const missed = vi.fn(async () => ["Game.exe", "bin/tool.exe"]);
  const send = vi.fn(async () => {});
  await reportMissedPrograms(install, entry, ["Gone.exe"], "windows", { missed, send });
  await reportMissedPrograms(install, entry, ["Gone.exe"], "windows", { missed, send });
  expect(send).toHaveBeenCalledTimes(1);
  expect(send).toHaveBeenCalledWith(entry, { os: "windows", version: "v1.2", programs: ["Game.exe", "bin/tool.exe"] });
  await reportMissedPrograms({ ...install, version: "v1.3", wine: true }, entry, ["Gone.exe"], "linux", { missed, send });
  expect(send).toHaveBeenLastCalledWith(entry, { os: "windows", version: "v1.3", programs: ["Game.exe", "bin/tool.exe"] });
});

test("nothing is reported when the catalog names no program, or one is there", async () => {
  const send = vi.fn(async () => {});
  await reportMissedPrograms(install, entry, [], "windows", { missed: async () => ["Game.exe"], send });
  await reportMissedPrograms(install, entry, ["Game.exe"], "windows", { missed: async () => null, send });
  expect(send).not.toHaveBeenCalled();
});

test("a report the site couldn't take is sent again next time", async () => {
  const send = vi.fn(async () => {
    throw new ApiError(503, "Unavailable");
  });
  await reportMissedPrograms(install, entry, ["Gone.exe"], "windows", { missed: async () => ["Game.exe"], send });
  await reportMissedPrograms(install, entry, ["Gone.exe"], "windows", { missed: async () => ["Game.exe"], send });
  expect(send).toHaveBeenCalledTimes(2);
});
