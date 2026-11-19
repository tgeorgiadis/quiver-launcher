/**
 * The sync folder: a folder of the player's own that save backups are also kept in, so whatever syncs it (Dropbox,
 * OneDrive, Syncthing) takes their saves to their other computers. No account is needed, and it works for apps that
 * aren't in the catalog. The folder's side is sync_folder.rs; this is when it's written to and read:
 *
 * - After a backup is made, it's also put in the folder (`putBackup`, store.tsx).
 * - Before an app starts, the folder's newest backup is compared with the saves here, the way the account's newest
 *   copy is (`inLine` in cloud-saves.ts): the same, nothing happens; only the folder's is new, it's put in place;
 *   both changed, the player chooses.
 *
 * Anything can be in that folder, so a backup from it is treated as a file from a stranger: it's put in place by
 * the same Restore as every backup (`restoreBackup`), which never lets a backup say where its files go.
 */
import type { Entry } from "@quiverlauncher/api";
import type { native, SaveBackup, SavesOf } from "./native";
import { inLine, restoreBackup, type CloudCopy, type ConfirmPlaces, type Conflict, type Elsewhere } from "./cloud-saves";

type Native = Pick<typeof native, "savesFolderState" | "savesFolderSynced" | "savesFolderPut" | "savesFolderTake" | "savesInfo" | "savesRestorePlan" | "savesRestore" | "savesBackup">;

/** One of the folder's backups, as the copies saves are compared with are written. */
const asCopy = (backup: SaveBackup): CloudCopy => ({
  id: backup.name,
  size: backup.size,
  hash: backup.hash,
  os: backup.os as CloudCopy["os"],
  device: backup.device,
  files: backup.files,
  ...(backup.version ? { version: backup.version } : {}),
  reason: backup.reason,
  madeAt: backup.at,
  createdAt: backup.at,
});

/** Puts the backup of the saves as they are now in the folder, when its newest isn't them. Null when there was nothing to put. */
export async function putCurrent(of: SavesOf, folder: string, via: { native: Pick<Native, "savesFolderState" | "savesFolderPut" | "savesInfo"> }): Promise<SaveBackup | null> {
  const [state, info] = await Promise.all([via.native.savesFolderState(of, folder), via.native.savesInfo(of)]);
  if (!state.there || !state.hash || state.copies[0]?.hash === state.hash) return null;
  // Only a backup of the saves as they are: an older one, or what a restore replaced, isn't where the player is.
  const backup = info?.backups.find((b) => b.hash === state.hash);
  return backup ? via.native.savesFolderPut(of, folder, backup.name) : null;
}

/**
 * Takes one of the folder's backups in and puts it in place, after backing up the saves here. False when it names a
 * folder this computer doesn't know for the app and the player didn't agree to it: it's then among the app's
 * backups, and the saves are as they were. Rejects with what to say.
 */
export async function restoreFromFolder(of: SavesOf, folder: string, backup: SaveBackup, via: { native: Pick<Native, "savesFolderTake" | "savesFolderSynced" | "savesRestorePlan" | "savesRestore">; confirm: ConfirmPlaces }): Promise<boolean> {
  const taken = await via.native.savesFolderTake(of, folder, backup.name);
  if (!(await restoreBackup(of, taken.name, via))) return false;
  await via.native.savesFolderSynced(of, folder, backup.hash);
  return true;
}

/**
 * Before an app starts: brings its saves in line with the sync folder's. Resolves to whether to start it, as
 * `beforePlay` does for the account. A folder that isn't there (a drive not plugged in) is never in the way.
 */
export function folderBeforePlay(
  of: SavesOf,
  entry: Pick<Entry, "id" | "projectName" | "launcher">,
  folder: string,
  via: { native: Native; ask: (conflict: Conflict) => Promise<"cloud" | "here" | null>; changed: (id: string) => void },
): Promise<{ go: boolean; fetched?: CloudCopy; problem?: string }> {
  const kept: Elsewhere = {
    from: "folder",
    async look() {
      const state = await via.native.savesFolderState(of, folder);
      return state.there ? { ...state, copies: state.copies.map(asCopy) } : null;
    },
    take: (copy) => via.native.savesFolderTake(of, folder, copy.id),
    synced: (hash) => via.native.savesFolderSynced(of, folder, hash),
    sendCurrent: () => putCurrent(of, folder, via),
  };
  return inLine(of, entry, kept, via);
}

/**
 * When a folder is first chosen: each app's saves go in it, for the apps it has nothing of yet. An app the folder
 * already has backups of is left for when it next starts, where the player is asked which saves to keep: a computer
 * that just joined must never push its saves over another's as the newest.
 */
export async function seed(apps: SavesOf[], folder: string, via: { native: Pick<Native, "savesFolderState" | "savesFolderPut" | "savesInfo" | "savesBackup"> }): Promise<number> {
  let put = 0;
  for (const of of apps) {
    try {
      const state = await via.native.savesFolderState(of, folder);
      if (!state.there) return put;
      if (state.copies.length || !state.hash) continue;
      await via.native.savesBackup(of, "manual").catch(() => null);
      if (await putCurrent(of, folder, via)) put += 1;
    } catch {
      // One app's saves that can't be read don't stop the rest.
    }
  }
  return put;
}
