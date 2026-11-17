import { expect, it, vi } from "vitest";
import { beforePlay, decide, fetchCopy, lastSent, onSent, restoreBackup, retries, RETRY_AFTER, sendBackup, sendCurrent, usable, type CloudApi, type CloudCopy, type CloudMine } from "./cloud-saves";
import type { SaveBackup, SavePlace, SavesCloud, SavesOf } from "./native";

const of: SavesOf = { id: "e1", folder: "Game", wine: false, locations: [], names: ["Game"] };
const entry = { id: "e1", projectName: "Game", launcher: { folderName: "Game", filesToAdd: [] } };
const A = "a".repeat(64);
const B = "b".repeat(64);
const C = "c".repeat(64);
const backup = (hash: string, more: Partial<SaveBackup> = {}): SaveBackup => ({ name: "1.zip", at: 1000, size: 2000, files: 2, version: "1.0", reason: "played", device: "DESKTOP", os: "windows", hash, ...more });
const copy = (hash: string, more: Partial<CloudCopy> = {}): CloudCopy => ({ id: `copy-${hash[0]}`, size: 2000, hash, os: "windows", device: "LAPTOP", files: 2, reason: "played", madeAt: 2000, createdAt: 2000, ...more });
const mine = (copies: CloudCopy[], more: Partial<CloudMine> = {}): CloudMine => ({ on: true, limit: 250, used: 0, keep: 5, maxCopy: 100 * 1024 * 1024, allowed: true, copies, ...more });

/** The account and this computer, as far as cloud saves reaches them. */
function world(start: { state: SavesCloud; copies?: CloudCopy[]; backups?: SaveBackup[]; mine?: Partial<CloudMine>; begin?: Awaited<ReturnType<CloudApi["begin"]>>; unknown?: SavePlace[] }) {
  const state = { ...start.state };
  const api = {
    mine: vi.fn(async () => mine(start.copies ?? [], start.mine)),
    usage: vi.fn(async () => null),
    begin: vi.fn(async () => start.begin ?? ({ status: "upload", id: "new", url: "https://bucket/put", headers: { "content-length": "2000" } } as const)),
    finish: vi.fn(async () => true),
    download: vi.fn(async () => ({ url: "https://bucket/get", size: 2000 })),
    remove: vi.fn(async () => {}),
  };
  const native = {
    savesCloud: vi.fn(async () => ({ ...state })),
    savesInfo: vi.fn(async () => ({ places: [], from: "found" as const, backups: start.backups ?? [] })),
    savesUpload: vi.fn(async () => {}),
    savesSynced: vi.fn(async (_: SavesOf, hash: string | null) => void (state.synced = hash)),
    savesDownload: vi.fn(async (_of: SavesOf, _url: string, _size: number, hash: string) => backup(hash, { name: "9.zip" })),
    // The folders the downloaded copy names that this computer doesn't keep the app's saves in.
    savesRestorePlan: vi.fn(async (): Promise<SavePlace[]> => start.unknown ?? []),
    savesRestore: vi.fn(async (_: SavesOf, name: string, _confirmed?: string[]) => (name === "9.zip" && (state.hash = (start.copies ?? [])[0]?.hash), 1)),
    savesBackup: vi.fn(async () => null),
  };
  const ask = vi.fn(async (): Promise<"cloud" | "here" | null> => null);
  const changed = vi.fn();
  return { api, native, ask, changed, state, via: { api, native, ask, changed } };
}
const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

it("tells which side changed since the saves last matched", () => {
  expect(decide({ hash: A, synced: A }, undefined)).toBe("send");
  expect(decide({ hash: A, synced: null }, { hash: A })).toBe("same");
  // Nothing here yet: a new computer, or a reinstall.
  expect(decide({ hash: null, synced: null }, { hash: B })).toBe("fetch");
  // Unchanged here since they matched, and the account has something else: another computer played.
  expect(decide({ hash: A, synced: A }, { hash: B })).toBe("fetch");
  // Changed here, and the account still has what they matched at.
  expect(decide({ hash: C, synced: A }, { hash: A })).toBe("send");
  // Both moved on, or they never matched.
  expect(decide({ hash: C, synced: A }, { hash: B })).toBe("ask");
  expect(decide({ hash: C, synced: null }, { hash: B })).toBe("ask");
});

it("only uses a copy from another system when the app's saves load anywhere", () => {
  const copies = [copy(A, { os: "linux" }), copy(B, { os: "windows" })];
  expect(usable(copies, "windows", undefined)?.hash).toBe(B);
  expect(usable(copies, "windows", false)?.hash).toBe(B);
  expect(usable(copies, "windows", true)?.hash).toBe(A);
  expect(usable(copies, "macos", undefined)).toBeUndefined();
});

it("sends a backup: asks for a link, uploads to it, confirms, and remembers they match", async () => {
  const { api, native } = world({ state: { hash: A, off: false, os: "windows" } });
  expect(await sendBackup(of, "e1", backup(A), { api, native })).toBe("sent");
  expect(api.begin).toHaveBeenCalledWith({ entryId: "e1", size: 2000, hash: A, os: "windows", device: "DESKTOP", files: 2, version: "1.0", reason: "played", madeAt: 1000 });
  expect(native.savesUpload).toHaveBeenCalledWith("e1", "1.zip", "https://bucket/put", { "content-length": "2000" });
  expect(api.finish).toHaveBeenCalledWith("new");
  expect(native.savesSynced).toHaveBeenCalledWith(of, A);
  expect(lastSent("e1")).toBe("sent");
});

it("doesn't upload what the account has, and says why a backup stays here", async () => {
  const have = world({ state: { hash: A, off: false, os: "windows" }, begin: { status: "have", id: "x" } });
  expect(await sendBackup(of, "e1", backup(A), have)).toBe("sent");
  expect(have.native.savesUpload).not.toHaveBeenCalled();
  expect(have.native.savesSynced).toHaveBeenCalledWith(of, A);

  const full = world({ state: { hash: A, off: false, os: "windows" }, begin: { status: "full", used: 250, limit: 250 } });
  expect(await sendBackup(of, "e1", backup(A), full)).toBe("full");
  expect(full.native.savesSynced).not.toHaveBeenCalled();
  expect(lastSent("e1")).toBe("full");

  const big = world({ state: { hash: A, off: false, os: "windows" } });
  // The site says what one copy can be, and refuses a bigger one in these words.
  big.api.begin.mockRejectedValue(Object.assign(new Error("ConvexError"), { data: "That backup is too big for cloud saves" }));
  expect(await sendBackup(of, "e1", backup(A, { size: 300 * 1024 * 1024 }), big)).toBe("big");
  expect(lastSent("e1")).toBe("big");

  // No connection, the upload refused, or it didn't arrive whole: tried again with a later backup.
  const offline = world({ state: { hash: A, off: false, os: "windows" } });
  offline.api.begin.mockRejectedValue(new Error("offline"));
  expect(await sendBackup(of, "e1", backup(A), offline)).toBe("later");
  const refused = world({ state: { hash: A, off: false, os: "windows" } });
  refused.native.savesUpload.mockRejectedValue("Cloud saves didn't take that backup (403).");
  expect(await sendBackup(of, "e1", backup(A), refused)).toBe("later");
  const partial = world({ state: { hash: A, off: false, os: "windows" } });
  partial.api.finish.mockResolvedValue(false);
  expect(await sendBackup(of, "e1", backup(A), partial)).toBe("later");
  expect(partial.native.savesSynced).not.toHaveBeenCalled();
});

it("starts the app as it is when the saves match, cloud saves are off, or the account can't be reached", async () => {
  const same = world({ state: { hash: A, synced: null, off: false, os: "windows" }, copies: [copy(A)] });
  expect(await beforePlay(of, entry, same.via)).toEqual({ go: true });
  expect(same.native.savesSynced).toHaveBeenCalledWith(of, A);
  expect(same.api.download).not.toHaveBeenCalled();

  const off = world({ state: { hash: A, off: true, os: "windows" }, copies: [copy(B)] });
  expect(await beforePlay(of, entry, off.via)).toEqual({ go: true });
  expect(off.api.mine).not.toHaveBeenCalled();

  const down = world({ state: { hash: A, synced: A, off: false, os: "windows" }, copies: [copy(B)] });
  down.api.mine.mockRejectedValue(new Error("offline"));
  expect(await beforePlay(of, entry, down.via)).toEqual({ go: true });

  for (const more of [{ on: false }, { allowed: false }]) {
    const none = world({ state: { hash: A, synced: A, off: false, os: "windows" }, copies: [copy(B)], mine: more });
    expect(await beforePlay(of, entry, none.via)).toEqual({ go: true });
    expect(none.api.download).not.toHaveBeenCalled();
  }
});

it("puts the account's newer copy in place when the saves here haven't changed", async () => {
  const w = world({ state: { hash: A, synced: A, off: false, os: "windows" }, copies: [copy(B)] });
  expect(await beforePlay(of, entry, w.via)).toEqual({ go: true, fetched: copy(B) });
  expect(w.api.download).toHaveBeenCalledWith("copy-b");
  expect(w.native.savesDownload).toHaveBeenCalledWith(of, "https://bucket/get", 2000, B);
  expect(w.native.savesRestore).toHaveBeenCalledWith(of, "9.zip", []);
  expect(w.state).toMatchObject({ hash: B, synced: B });
  expect(w.changed).toHaveBeenCalledWith("e1");
  expect(w.ask).not.toHaveBeenCalled();
  // A copy from another system isn't put in place by itself.
  const linux = world({ state: { hash: A, synced: A, off: false, os: "windows" }, copies: [copy(B, { os: "linux" })] });
  expect(await beforePlay(of, entry, linux.via)).toEqual({ go: true });
  expect(linux.api.download).not.toHaveBeenCalled();
});

it("starts the app anyway when the account's copy can't be put in place, and says so", async () => {
  const w = world({ state: { hash: null, off: false, os: "windows" }, copies: [copy(B)] });
  w.native.savesRestore.mockRejectedValue("This backup's saves were in a folder Quiver doesn't know on this computer.");
  expect(await beforePlay(of, entry, w.via)).toEqual({ go: true, problem: "This backup's saves were in a folder Quiver doesn't know on this computer." });
  expect(w.native.savesSynced).not.toHaveBeenCalled();
});

const ELSEWHERE: SavePlace[] = [{ template: "localData/Game/saves", path: "C:\\Users\\me\\AppData\\Local\\Game\\saves", exists: false }];

it("never puts a copy in a folder this computer doesn't know without the player seeing it", async () => {
  // Unchanged here and newer in the account: it would go in by itself, but it names another folder.
  const start = () => world({ state: { hash: A, synced: A, off: false, os: "windows" }, copies: [copy(B)], backups: [backup(A)], unknown: ELSEWHERE });
  const stop = start();
  expect(await beforePlay(of, entry, stop.via)).toEqual({ go: false });
  expect(stop.ask).toHaveBeenCalledWith({ entry, cloud: copy(B), here: backup(A), places: ELSEWHERE });
  expect(stop.native.savesRestore).not.toHaveBeenCalled();
  expect(stop.native.savesSynced).not.toHaveBeenCalled();

  const cloud = start();
  cloud.ask.mockResolvedValue("cloud");
  expect(await beforePlay(of, entry, cloud.via)).toMatchObject({ go: true, fetched: { hash: B } });
  expect(cloud.native.savesRestore).toHaveBeenCalledWith(of, "9.zip", ["localData/Game/saves"]);
  expect(cloud.state).toMatchObject({ hash: B, synced: B });

  const here = start();
  here.ask.mockResolvedValue("here");
  expect(await beforePlay(of, entry, here.via)).toEqual({ go: true });
  expect(here.native.savesRestore).not.toHaveBeenCalled();
  // Seen and turned down: it isn't asked about before every start.
  expect(here.state.synced).toBe(B);

  // A new computer with no saves is asked the same way.
  const fresh = world({ state: { hash: null, off: false, os: "windows" }, copies: [copy(B)], unknown: ELSEWHERE });
  expect(await beforePlay(of, entry, fresh.via)).toEqual({ go: false });
  expect(fresh.ask).toHaveBeenCalledWith({ entry, cloud: copy(B), here: undefined, places: ELSEWHERE });
  expect(fresh.native.savesRestore).not.toHaveBeenCalled();
});

it("restores a backup of another folder only once the player agreed to that folder", async () => {
  const w = world({ state: { hash: A, off: false, os: "windows" }, copies: [copy(B)], unknown: ELSEWHERE });
  const no = vi.fn(async () => false);
  expect(await restoreBackup(of, "9.zip", { native: w.native, confirm: no })).toBe(false);
  expect(no).toHaveBeenCalledWith(ELSEWHERE);
  expect(await fetchCopy(of, copy(B), { api: w.api, native: w.native, confirm: no })).toBe(false);
  expect(w.native.savesRestore).not.toHaveBeenCalled();
  expect(w.native.savesSynced).not.toHaveBeenCalled();
  expect(await fetchCopy(of, copy(B), { api: w.api, native: w.native, confirm: async () => true })).toBe(true);
  expect(w.native.savesRestore).toHaveBeenCalledWith(of, "9.zip", ["localData/Game/saves"]);
  // The places known here need no question.
  const known = world({ state: { hash: A, off: false, os: "windows" }, copies: [copy(B)] });
  expect(await restoreBackup(of, "9.zip", { native: known.native, confirm: no })).toBe(true);
  expect(no).toHaveBeenCalledTimes(2);
  // One that names a folder no app saves in isn't restored at all.
  known.native.savesRestorePlan.mockRejectedValue("This backup's saves were in a folder Quiver doesn't keep this app's saves in on this computer.");
  await expect(restoreBackup(of, "9.zip", { native: known.native, confirm: async () => true })).rejects.toMatch(/doesn't keep/);
  expect(known.native.savesRestore).toHaveBeenCalledTimes(1);
});

it("sends what's here when only this computer's saves changed", async () => {
  const w = world({ state: { hash: C, synced: A, off: false, os: "windows" }, copies: [copy(A)], backups: [backup(C, { name: "5.zip" }), backup(A)] });
  expect(await beforePlay(of, entry, w.via)).toEqual({ go: true });
  await settled();
  expect(w.native.savesUpload).toHaveBeenCalledWith("e1", "5.zip", "https://bucket/put", expect.anything());
  expect(w.api.download).not.toHaveBeenCalled();
  // Only a backup of the saves as they are now is sent, never what a restore replaced.
  const stale = world({ state: { hash: C, synced: A, off: false, os: "windows" }, backups: [backup(B, { reason: "restore" })] });
  expect(await sendCurrent(of, "e1", stale.via)).toBeNull();
  expect(stale.api.begin).not.toHaveBeenCalled();
});

it("asks when both changed, and does what the player picks", async () => {
  const start = () => world({ state: { hash: C, synced: A, off: false, os: "windows" }, copies: [copy(B)], backups: [backup(C, { name: "5.zip" })] });
  const stop = start();
  expect(await beforePlay(of, entry, stop.via)).toEqual({ go: false });
  expect(stop.ask).toHaveBeenCalledWith({ entry, cloud: copy(B), here: backup(C, { name: "5.zip" }) });
  expect(stop.native.savesRestore).not.toHaveBeenCalled();
  expect(stop.native.savesSynced).not.toHaveBeenCalled();

  const cloud = start();
  cloud.ask.mockResolvedValue("cloud");
  expect(await beforePlay(of, entry, cloud.via)).toMatchObject({ go: true, fetched: { hash: B } });
  expect(cloud.state).toMatchObject({ hash: B, synced: B });

  const here = start();
  here.ask.mockResolvedValue("here");
  expect(await beforePlay(of, entry, here.via)).toEqual({ go: true });
  await settled();
  expect(here.native.savesRestore).not.toHaveBeenCalled();
  // This computer's saves go up and become the newest, so it isn't asked again.
  expect(here.native.savesUpload).toHaveBeenCalledWith("e1", "5.zip", "https://bucket/put", expect.anything());
  expect(here.state).toMatchObject({ hash: C, synced: C });
});

it("tells whoever listens how each send went, and whether the player asked for it", async () => {
  const heard: unknown[] = [];
  const off = onSent((id, how, manual) => heard.push([id, how, manual]));
  const sent = world({ state: { hash: A, off: false, os: "windows" } });
  await sendBackup(of, "e1", backup(A), sent);
  const full = world({ state: { hash: A, off: false, os: "windows" }, begin: { status: "full", used: 250, limit: 250 } });
  await sendBackup(of, "e1", backup(A), { ...full, manual: true });
  off();
  await sendBackup(of, "e1", backup(A), sent);
  expect(heard).toEqual([
    ["e1", "sent", false],
    ["e1", "full", true],
  ]);
});

it("tries a send that didn't get there again after 1, 5, 15 and 60 minutes, then leaves it", async () => {
  const timers: { run: () => void; ms: number; cleared?: true }[] = [];
  const timer = { set: (run: () => void, ms: number) => timers[timers.push({ run, ms }) - 1], clear: (handle: unknown) => void ((handle as (typeof timers)[number]).cleared = true) };
  const answers: ("later" | "sent" | "full" | null)[] = [];
  const attempt = vi.fn(async () => answers.shift() ?? "later");
  const again = retries(attempt, timer);
  /** The newest timer goes off, and what it started finishes. */
  const fire = async () => (timers.at(-1)!.run(), await settled());

  again.note("e1", "sent");
  expect(timers).toHaveLength(0);
  again.note("e1", "later");
  expect(again.waiting("e1")).toBe(true);
  for (let i = 0; i < RETRY_AFTER.length; i++) {
    expect(timers.at(-1)!.ms).toBe(RETRY_AFTER[i]);
    await fire();
  }
  expect(RETRY_AFTER).toEqual([60_000, 300_000, 900_000, 3_600_000]);
  expect(attempt).toHaveBeenCalledTimes(4);
  expect(timers).toHaveLength(4);
  expect(again.waiting("e1")).toBe(false);

  // One that gets there, or finds the account full, ends the waiting.
  answers.push("later", "sent");
  again.note("e1", "later");
  await fire();
  await fire();
  expect(timers).toHaveLength(6);
  expect(again.waiting("e1")).toBe(false);
  answers.push("full");
  again.note("e1", "later");
  await fire();
  expect(timers).toHaveLength(7);

  // A send that gets there in the meantime calls the retry off; a retry's own sends don't start the count again.
  again.note("e1", "later");
  again.note("e1", "sent");
  expect(timers.at(-1)!.cleared).toBe(true);
  expect(again.waiting("e1")).toBe(false);
  const noting = retries(async (id) => (noting.note(id, "later"), "later"), timer);
  noting.note("e2", "later");
  const before = timers.length;
  await fire();
  expect(timers).toHaveLength(before + 1);
  expect(timers.at(-1)!.ms).toBe(RETRY_AFTER[1]);
  noting.stop();
  expect(noting.waiting("e2")).toBe(false);
});
