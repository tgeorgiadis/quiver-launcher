/**
 * Cloud saves: a signed-in player's save backups, kept with their Quiver account so they follow them to another
 * computer. It rides on save backups (saves.rs): the copy sent is a backup's own zip, and a copy from the account is
 * taken in as a backup and put in place by the same Restore.
 *
 * - After a backup is made, it's sent (`sendBackup`).
 * - Before an app starts, the account's newest copy is compared with the saves here (`decide`, `beforePlay`): the
 *   same, nothing happens; the account's is newer and the saves here haven't changed since they last matched, it's
 *   put in place; both changed, the player chooses.
 *
 * A backup says where its saves were, but it may come from another computer, or from someone else with the player's
 * account. So its files only go to the places this computer keeps the app's saves in already; any other folder is
 * shown to the player first (`restoreBackup`), and nothing is put there by itself.
 *
 * The site keeps the limits (by default 250 MB an account, 5 copies an app, 100 MB a copy) and hands out links; the files go straight to and
 * from storage. Only apps from the catalog, and never in the way of playing: when the account can't be reached the
 * app starts as it is.
 */
import type { Entry } from "@quiverlauncher/api";
import type { native, SaveBackup, SavePlace, SavesCloud, SavesOf } from "./native";

/** One copy in the account, as the site lists it. */
export type CloudCopy = {
  id: string;
  size: number;
  hash: string;
  os: "windows" | "linux" | "macos";
  device: string;
  files: number;
  version?: string;
  reason: string;
  /** When its computer made it. */
  madeAt: number;
  /** When the account took it. */
  createdAt: number;
};
/** The account's cloud saves, with one app's copies newest first. */
export type CloudMine = {
  /** False while the site has no storage for them, or uploads are stopped (`why`). Copies can still be fetched and removed. */
  on: boolean;
  /** "paused": Quiver paused uploads for everyone. "suspended": it stopped this account's. */
  why?: "paused" | "suspended";
  limit: number;
  used: number;
  keep: number;
  maxCopy: number;
  /** False when Quiver switched cloud saves off for this app. */
  allowed: boolean;
  copies: CloudCopy[];
};
/** Everything the account keeps, by app, biggest first; each app's copies newest first. */
export type CloudUsage = {
  on: boolean;
  why?: "paused" | "suspended";
  limit: number;
  used: number;
  keep: number;
  apps: { entryId: string; name: string; slug?: string; artwork?: string; allowed: boolean; size: number; copies: CloudCopy[] }[];
};
export type CloudBegun =
  | { status: "upload"; id: string; url: string; headers: Record<string, string> }
  | { status: "have"; id: string }
  | { status: "full"; used: number; limit: number }
  | { status: "off" }
  | { status: "busy" };
/** The site's side, for the signed-in account (account.tsx). */
export type CloudApi = {
  mine: (entryId: string) => Promise<CloudMine | null>;
  usage: () => Promise<CloudUsage | null>;
  begin: (backup: { entryId: string; size: number; hash: string; os: CloudCopy["os"]; device: string; files: number; version?: string; reason: string; madeAt: number }) => Promise<CloudBegun>;
  finish: (id: string) => Promise<boolean>;
  download: (id: string) => Promise<{ url: string; size: number } | null>;
  remove: (what: { id: string } | { entryId: string }) => Promise<void>;
};

/** The site's refusal of a copy over what one can be, which it words this way (cloudSaves.ts there). */
const tooBig = (e: unknown) => /too big/i.test(String((e as { data?: unknown } | null)?.data ?? (e as Error | null)?.message ?? e));

/** How sending a backup went. "full": the account has no room. "big": over what one copy can be. */
export type Sent = "sent" | "full" | "big" | "off" | "later";

/** The last thing to say about each app's cloud saves, for its Saves tab. */
const said = new Map<string, Sent>();
export const lastSent = (id: string) => said.get(id);

type SentListener = (id: string, how: Sent, manual: boolean) => void;
const sentListeners = new Set<SentListener>();
/** Told how each send went, as it ends (store.tsx: the full-account question, and trying again later). `manual`: the player asked for it and is looking at the answer. */
export function onSent(listener: SentListener) {
  sentListeners.add(listener);
  return () => void sentListeners.delete(listener);
}

/** How long after a send that didn't get there the next try is, in turn. After the last, the next backup or start of the app sends. */
export const RETRY_AFTER = [1, 5, 15, 60].map((minutes) => minutes * 60_000);

/**
 * Tries again what didn't get there. `note` is told how every send went: one that ended "later" is tried again with
 * `attempt` after each of RETRY_AFTER in turn, until one ends another way; anything else ends the waiting for that app.
 */
export function retries(attempt: (id: string) => Promise<Sent | null>, timer: { set: (run: () => void, ms: number) => unknown; clear: (handle: unknown) => void } = { set: (run, ms) => setTimeout(run, ms), clear: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>) }) {
  const waiting = new Map<string, unknown>();
  /** Apps a retry is being made for now: how it goes is the retry's to follow, not a new send's. */
  const trying = new Set<string>();
  const clear = (id: string) => {
    if (waiting.has(id)) timer.clear(waiting.get(id));
    waiting.delete(id);
  };
  const plan = (id: string, step: number) => {
    clear(id);
    if (step >= RETRY_AFTER.length) return;
    waiting.set(
      id,
      timer.set(() => {
        waiting.delete(id);
        trying.add(id);
        attempt(id)
          .catch(() => "later" as const)
          .then((how) => (trying.delete(id), how === "later" && plan(id, step + 1)));
      }, RETRY_AFTER[step]),
    );
  };
  return {
    note(id: string, how: Sent) {
      if (trying.has(id)) return;
      if (how === "later") plan(id, 0);
      else clear(id);
    },
    waiting: (id: string) => waiting.has(id),
    stop: () => [...waiting.keys()].forEach(clear),
  };
}

type Native = Pick<typeof native, "savesUpload" | "savesSynced" | "savesCloud" | "savesInfo" | "savesDownload" | "savesRestorePlan" | "savesRestore" | "savesBackup">;

/** Asks the player whether a backup may be put in folders this computer hasn't kept the app's saves in. */
export type ConfirmPlaces = (places: SavePlace[]) => Promise<boolean>;

/**
 * Puts one of an app's backups in place. A backup of the places this computer knows goes straight in; one that names
 * another folder is only restored once the player agreed to that folder. False when they didn't. Rejects with what
 * to say when it can't be restored at all.
 */
export async function restoreBackup(of: SavesOf, name: string, via: { native: Pick<Native, "savesRestorePlan" | "savesRestore">; confirm: ConfirmPlaces }): Promise<boolean> {
  const unknown = await via.native.savesRestorePlan(of, name);
  if (unknown.length && !(await via.confirm(unknown))) return false;
  await via.native.savesRestore(
    of,
    name,
    unknown.map((place) => place.template),
  );
  return true;
}

const SYSTEMS = ["windows", "linux", "macos"];

/**
 * Sends one backup to the account. "later" when it didn't get there (no connection, too many at once): the next
 * backup, or the next start of the app, sends what's newest then.
 */
export async function sendBackup(of: SavesOf, entryId: string, backup: SaveBackup, via: { api: CloudApi; native: Pick<Native, "savesUpload" | "savesSynced">; manual?: boolean }): Promise<Sent> {
  const done = (how: Sent) => {
    said.set(of.id, how);
    for (const listener of sentListeners) listener(of.id, how, Boolean(via.manual));
    return how;
  };
  if (!SYSTEMS.includes(backup.os)) return done("off");
  try {
    const begun = await via.api.begin({
      entryId,
      size: backup.size,
      hash: backup.hash,
      os: backup.os as CloudCopy["os"],
      device: backup.device,
      files: backup.files,
      ...(backup.version ? { version: backup.version } : {}),
      reason: backup.reason,
      madeAt: backup.at,
    });
    if (begun.status === "full" || begun.status === "off") return done(begun.status);
    if (begun.status === "busy") return done("later");
    if (begun.status === "upload") {
      await via.native.savesUpload(of.id, backup.name, begun.url, begun.headers);
      if (!(await via.api.finish(begun.id))) return done("later");
    }
    await via.native.savesSynced(of, backup.hash);
    return done("sent");
  } catch (e) {
    return done(tooBig(e) ? "big" : "later");
  }
}

/** The account's newest copy this computer can use: one from the same system, or any when the app's saves load anywhere. */
export const usable = (copies: CloudCopy[], os: string, crossPlatform: boolean | undefined) => copies.find((copy) => copy.os === os || crossPlatform === true);

/**
 * What to do before an app starts, from the saves here and the account's newest copy.
 * - "same": they match.
 * - "send": only this computer's changed (or the account has none): nothing to fetch.
 * - "fetch": only the account's changed, or there are no saves here: the account's copy is put in place.
 * - "ask": both changed since they last matched, or they never did: the player chooses.
 */
export function decide(here: Pick<SavesCloud, "hash" | "synced">, newest: Pick<CloudCopy, "hash"> | undefined): "same" | "send" | "fetch" | "ask" {
  if (!newest) return "send";
  if (here.hash === newest.hash) return "same";
  if (!here.hash || here.hash === here.synced) return "fetch";
  if (newest.hash === here.synced) return "send";
  return "ask";
}

/**
 * Downloads one of the account's copies and puts it in place, after backing up the saves here. False when it names a
 * folder this computer doesn't know for the app and the player didn't agree to it: the copy is then among the app's
 * backups, and the saves are as they were. Rejects with what to say.
 */
export async function fetchCopy(of: SavesOf, copy: CloudCopy, via: { api: CloudApi; native: Pick<Native, "savesDownload" | "savesRestorePlan" | "savesRestore" | "savesSynced">; confirm: ConfirmPlaces }): Promise<boolean> {
  const link = await via.api.download(copy.id);
  if (!link) throw new Error("gone");
  const backup = await via.native.savesDownload(of, link.url, link.size, copy.hash);
  if (!(await restoreBackup(of, backup.name, via))) return false;
  await via.native.savesSynced(of, copy.hash);
  return true;
}

/** Sends the backup of the saves as they are now, when there is one the account doesn't have. */
export async function sendCurrent(of: SavesOf, entryId: string, via: { api: CloudApi; native: Native }): Promise<Sent | null> {
  const [state, info] = await Promise.all([via.native.savesCloud(of), via.native.savesInfo(of)]);
  if (state.off || !state.hash || state.hash === state.synced) return null;
  // Only a backup of the saves as they are: an older one, or what a restore replaced, isn't where the player is.
  const backup = info?.backups.find((b) => b.hash === state.hash);
  return backup ? sendBackup(of, entryId, backup, via) : null;
}

export type Conflict = {
  entry: Pick<Entry, "id" | "projectName">;
  /** The account's newest copy. */
  cloud: CloudCopy;
  /** The newest backup of the saves here, when there is one of them as they are. */
  here?: SaveBackup;
  /** Folders the account's copy would go in that this computer hasn't kept the app's saves in: the player is shown them before choosing it. */
  places?: SavePlace[];
};

const within = <T>(work: Promise<T>, ms: number) => Promise.race([work, new Promise<never>((_, reject) => setTimeout(() => reject(new Error("timeout")), ms))]);

/**
 * Before an app starts: brings its saves in line with the account's. Resolves to whether to start it ("stop" only
 * when the player closed the question), with `problem` when the account's copy couldn't be put in place.
 */
export async function beforePlay(
  of: SavesOf,
  entry: Pick<Entry, "id" | "projectName" | "launcher">,
  via: { api: CloudApi; native: Native; ask: (conflict: Conflict) => Promise<"cloud" | "here" | null>; changed: (id: string) => void },
): Promise<{ go: boolean; fetched?: CloudCopy; problem?: string }> {
  let state: SavesCloud;
  let mine: CloudMine | null;
  try {
    state = await via.native.savesCloud(of);
    if (state.off) return { go: true };
    // Never long in the way of playing.
    mine = await within(via.api.mine(entry.id), 6000);
  } catch {
    return { go: true };
  }
  if (!mine?.on || !mine.allowed) return { go: true };
  const newest = usable(mine.copies, state.os, entry.launcher?.saves?.crossPlatform);
  const hereNow = async () => (await via.native.savesInfo(of).catch(() => null))?.backups.find((b) => b.hash === state.hash);
  /** This computer's saves stay: the account's copy has been seen, so it isn't asked about again, and these go up. */
  const keepHere = async (copy: CloudCopy) => {
    await via.native.savesSynced(of, copy.hash).catch(() => {});
    await via.native.savesBackup(of, "manual").catch(() => null);
    void sendCurrent(of, entry.id, via).catch(() => {});
    return { go: true };
  };
  const fetch = async (copy: CloudCopy) => {
    // A copy for a folder this computer doesn't know is never put there by itself: the player sees the folder first.
    let choice: "cloud" | "here" | null = "cloud";
    try {
      const confirm: ConfirmPlaces = async (places) => (choice = await via.ask({ entry, cloud: copy, here: await hereNow(), places })) === "cloud";
      if (await fetchCopy(of, copy, { ...via, confirm })) {
        via.changed(of.id);
        return { go: true, fetched: copy };
      }
    } catch (e) {
      return { go: true, problem: typeof e === "string" ? e : "" };
    }
    // The copy was taken in as a backup either way.
    via.changed(of.id);
    return choice ? keepHere(copy) : { go: false };
  };
  const what = decide(state, newest);
  if (what === "same") {
    if (state.synced !== state.hash) await via.native.savesSynced(of, state.hash ?? null).catch(() => {});
    return { go: true };
  }
  if (what === "send" || !newest) {
    // Catching up on a backup that didn't get there; the app doesn't wait for it.
    void sendCurrent(of, entry.id, via).catch(() => {});
    return { go: true };
  }
  if (what === "fetch") return fetch(newest);
  const choice = await via.ask({ entry, cloud: newest, here: await hereNow() });
  if (!choice) return { go: false };
  if (choice === "cloud") return fetch(newest);
  return keepHere(newest);
}
