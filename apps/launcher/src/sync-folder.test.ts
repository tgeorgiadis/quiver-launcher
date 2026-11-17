import { expect, it, vi } from "vitest";
import { folderBeforePlay, putCurrent, restoreFromFolder, seed } from "./sync-folder";
import type { SaveBackup, SavePlace, SavesFolder, SavesOf } from "./native";

const of: SavesOf = { id: "e1", folder: "Game", wine: false, locations: [], names: ["Game"] };
const entry = { id: "e1", projectName: "Game", launcher: { folderName: "Game", filesToAdd: [] } };
const FOLDER = "/home/me/Dropbox/Quiver Saves";
const A = "a".repeat(64);
const B = "b".repeat(64);
const C = "c".repeat(64);
const backup = (hash: string, more: Partial<SaveBackup> = {}): SaveBackup => ({ name: "1.zip", at: 1000, size: 2000, files: 2, version: "1.0", reason: "played", device: "DESKTOP", os: "windows", hash, ...more });
/** One of the folder's, from the other computer. */
const kept = (hash: string, more: Partial<SaveBackup> = {}) => backup(hash, { name: `2000-${hash.slice(0, 8)}.zip`, at: 2000, device: "LAPTOP", ...more });

/** This computer and the folder, as far as the sync folder reaches them. */
function world(start: { state: Partial<SavesFolder>; backups?: SaveBackup[]; unknown?: SavePlace[] }) {
  const state: SavesFolder = { os: "windows", there: true, copies: [], ...start.state };
  const native = {
    savesFolderState: vi.fn(async () => ({ ...state, copies: [...state.copies] })),
    savesFolderSynced: vi.fn(async (_: SavesOf, _folder: string, hash: string | null) => void (state.synced = hash)),
    savesFolderPut: vi.fn(async (_: SavesOf, _folder: string, name: string) => {
      const put = (start.backups ?? []).find((b) => b.name === name)!;
      state.copies = [put, ...state.copies];
      state.synced = put.hash;
      return put;
    }),
    savesFolderTake: vi.fn(async (_: SavesOf, _folder: string, name: string) => backup(state.copies.find((c) => c.name === name)!.hash, { name: "9.zip" })),
    savesInfo: vi.fn(async () => ({ places: [], from: "found" as const, backups: start.backups ?? [] })),
    savesRestorePlan: vi.fn(async (): Promise<SavePlace[]> => start.unknown ?? []),
    savesRestore: vi.fn(async (_: SavesOf, _name: string, _confirmed?: string[]) => 1),
    savesBackup: vi.fn(async () => null),
  };
  const ask = vi.fn(async (): Promise<"cloud" | "here" | null> => null);
  const changed = vi.fn();
  return { native, ask, changed, state, via: { native, ask, changed } };
}
const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

it("starts the app when the saves match the folder's newest, or the folder isn't there", async () => {
  const same = world({ state: { hash: A, synced: null, copies: [kept(A)] } });
  expect(await folderBeforePlay(of, entry, FOLDER, same.via)).toEqual({ go: true });
  expect(same.native.savesFolderSynced).toHaveBeenCalledWith(of, FOLDER, A);
  expect(same.native.savesFolderTake).not.toHaveBeenCalled();

  // A drive that isn't plugged in: nothing is compared, asked or written.
  const gone = world({ state: { hash: A, synced: A, there: false } });
  expect(await folderBeforePlay(of, entry, FOLDER, gone.via)).toEqual({ go: true });
  await settled();
  expect(gone.native.savesFolderPut).not.toHaveBeenCalled();
  expect(gone.ask).not.toHaveBeenCalled();
});

it("puts the other computer's saves in place when only they changed, through the same restore as any backup", async () => {
  const w = world({ state: { hash: A, synced: A, copies: [kept(B), kept(A, { name: "1500-aaaaaaaa.zip" })] } });
  const done = await folderBeforePlay(of, entry, FOLDER, w.via);
  expect(done.go).toBe(true);
  expect(done.fetched?.device).toBe("LAPTOP");
  expect(w.native.savesFolderTake).toHaveBeenCalledWith(of, FOLDER, "2000-bbbbbbbb.zip");
  expect(w.native.savesRestorePlan).toHaveBeenCalledWith(of, "9.zip");
  expect(w.native.savesRestore).toHaveBeenCalledWith(of, "9.zip", []);
  expect(w.native.savesFolderSynced).toHaveBeenCalledWith(of, FOLDER, B);
  expect(w.ask).not.toHaveBeenCalled();
  expect(w.changed).toHaveBeenCalledWith("e1");
});

it("copies this computer's saves in when only they changed, without holding the app back", async () => {
  const w = world({ state: { hash: C, synced: A, copies: [kept(A)] }, backups: [backup(C, { name: "7.zip" })] });
  expect(await folderBeforePlay(of, entry, FOLDER, w.via)).toEqual({ go: true });
  await settled();
  expect(w.native.savesFolderPut).toHaveBeenCalledWith(of, FOLDER, "7.zip");
  expect(w.native.savesFolderTake).not.toHaveBeenCalled();
});

it("asks which saves to keep when both changed, saying they're the sync folder's", async () => {
  const start = { state: { hash: C, synced: A, copies: [kept(B)] }, backups: [backup(C, { name: "7.zip" })] };
  const theirs = world(start);
  theirs.ask.mockResolvedValue("cloud");
  expect((await folderBeforePlay(of, entry, FOLDER, theirs.via)).fetched?.hash).toBe(B);
  expect(theirs.ask).toHaveBeenCalledWith(expect.objectContaining({ from: "folder", cloud: expect.objectContaining({ hash: B, device: "LAPTOP", madeAt: 2000 }), here: expect.objectContaining({ hash: C }) }));
  expect(theirs.native.savesRestore).toHaveBeenCalled();

  // This computer's stay: the folder's are seen, so it isn't asked again, and these become its newest.
  const mine = world(start);
  mine.ask.mockResolvedValue("here");
  expect(await folderBeforePlay(of, entry, FOLDER, mine.via)).toEqual({ go: true });
  await settled();
  expect(mine.native.savesRestore).not.toHaveBeenCalled();
  expect(mine.native.savesFolderSynced).toHaveBeenCalledWith(of, FOLDER, B);
  expect(mine.native.savesFolderPut).toHaveBeenCalledWith(of, FOLDER, "7.zip");

  const neither = world(start);
  expect(await folderBeforePlay(of, entry, FOLDER, neither.via)).toEqual({ go: false });
  expect(neither.native.savesFolderTake).not.toHaveBeenCalled();
});

it("never puts a backup in a folder this computer doesn't know without the player seeing it", async () => {
  const unknown = [{ template: "app/bin", path: "/apps/Game/bin", exists: true }];
  const w = world({ state: { hash: A, synced: A, copies: [kept(B)] }, unknown });
  w.ask.mockResolvedValue("here");
  expect(await folderBeforePlay(of, entry, FOLDER, w.via)).toEqual({ go: true });
  expect(w.ask).toHaveBeenCalledWith(expect.objectContaining({ from: "folder", places: unknown }));
  expect(w.native.savesRestore).not.toHaveBeenCalled();

  const confirm = vi.fn(async () => false);
  expect(await restoreFromFolder(of, FOLDER, kept(B), { native: w.native, confirm })).toBe(false);
  expect(confirm).toHaveBeenCalledWith(unknown);
  expect(w.native.savesRestore).not.toHaveBeenCalled();
});

it("only uses another system's saves by itself when the app's saves load anywhere", async () => {
  const w = world({ state: { hash: A, synced: A, copies: [kept(B, { os: "linux" })] } });
  expect(await folderBeforePlay(of, entry, FOLDER, w.via)).toEqual({ go: true });
  expect(w.native.savesFolderTake).not.toHaveBeenCalled();
  const anywhere = { ...entry, launcher: { ...entry.launcher, saves: { locations: [], crossPlatform: true } } };
  expect((await folderBeforePlay(of, anywhere, FOLDER, w.via)).fetched?.hash).toBe(B);
});

it("starts the app with the saves here when the folder's can't be taken", async () => {
  const w = world({ state: { hash: A, synced: A, copies: [kept(B)] } });
  w.native.savesFolderTake.mockRejectedValue("That backup hasn't finished syncing to this computer yet. Try again in a moment.");
  expect(await folderBeforePlay(of, entry, FOLDER, w.via)).toEqual({ go: true, problem: "That backup hasn't finished syncing to this computer yet. Try again in a moment." });
  expect(w.native.savesRestore).not.toHaveBeenCalled();
});

it("puts only a backup of the saves as they are, and nothing when the folder has them", async () => {
  const have = world({ state: { hash: A, copies: [kept(A)] }, backups: [backup(A)] });
  expect(await putCurrent(of, FOLDER, have.via)).toBeNull();
  const older = world({ state: { hash: C, copies: [] }, backups: [backup(A)] });
  expect(await putCurrent(of, FOLDER, older.via)).toBeNull();
  expect(older.native.savesFolderPut).not.toHaveBeenCalled();
});

it("a newly chosen folder gets saves only for the apps it has nothing of", async () => {
  const empty = world({ state: { hash: A }, backups: [backup(A, { name: "3.zip" })] });
  expect(await seed([of], FOLDER, empty.via)).toBe(1);
  expect(empty.native.savesFolderPut).toHaveBeenCalledWith(of, FOLDER, "3.zip");

  // Another computer's saves are there: this one's never go on top as the newest. Play asks instead.
  const theirs = world({ state: { hash: A, copies: [kept(B)] }, backups: [backup(A, { name: "3.zip" })] });
  expect(await seed([of], FOLDER, theirs.via)).toBe(0);
  expect(theirs.native.savesFolderPut).not.toHaveBeenCalled();
  expect(theirs.native.savesBackup).not.toHaveBeenCalled();
});
