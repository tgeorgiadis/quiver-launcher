# Sync folder

A player can choose a folder of their own that save backups are also kept in (Settings, Cloud saves, Sync folder).
Whatever syncs that folder, such as Dropbox, OneDrive, Google Drive or Syncthing, or a USB stick carried between
computers, then takes their saves to their other computers. It needs no Quiver account, and it works for apps the
player added themselves, which cloud saves don't cover. It can be on together with [cloud saves](cloud-saves.md).

Like cloud saves it rides on save backups (`apps/launcher/src-tauri/src/saves.rs`): what goes in the folder is a
backup's own zip, and what comes out is taken in as a backup and put in place by the same Restore, with the same
checks. The folder's side is `sync_folder.rs`; when it's written and read is `apps/launcher/src/sync-folder.ts`.

## What is in the folder

```
Quiver Saves/
  About this folder.txt
  Zelda 64 Recompiled (k17abc…)/
    1731832000000-0a1b2c3d.zip
    1731835600000-9f8e7d6c.zip
```

- **The folder.** The one the player picked when it's empty or Quiver's already, else a `Quiver Saves` folder inside
  it, so picking all of Dropbox doesn't fill it with a folder per app. Picking the same folder on another computer
  comes to the same place.
- **A folder per app**, named for people, and found again by the app's id in brackets.
- **The newest 5 backups of each app**, from all the player's computers together.

## When

- **After a backup** (after playing, before an update or an uninstall, or "Back up now"), the backup is also copied
  into the folder.
- **Before an app starts**, the folder's newest backup is compared with the saves on the computer, exactly as the
  account's newest copy is (`inLine` in `cloud-saves.ts`, and the table in [cloud saves](cloud-saves.md)): the same,
  the app starts; only the folder's is new, it's put in place; both changed, the player chooses.
- **When the folder is chosen**, the saves of each installed app go in, but only for apps the folder has nothing of.
  For an app another computer already put there, the player is asked when it next starts: a computer that just
  joined never pushes its saves over another's.

"Last matched" is kept per app and per folder, apart from the account's, so the two never mislead each other, and
choosing another folder starts afresh.

A backup made on another system is only put in place by itself when the catalog says the app's saves load on any
system. The player can restore any of the folder's backups from the app's Saves tab.

A folder that isn't there (a drive not plugged in) is never in the way of playing. The launcher says so once, backups
carry on on the computer, and the newest goes in when the folder is back.

## A folder something else writes

Sync programs copy files while the launcher isn't looking, and can do it in any order.

- **A file is written once and never changed.** Its name holds the start of its saves' checksum, so two computers
  never write different things under one name and a sync program has nothing to merge. The launcher writes under a
  temporary name first (`~….zip.tmp`, which Dropbox and OneDrive leave alone) and renames it when it's whole.
- **"Conflicted copy" files are never read and never removed.** Only a name of the launcher's own form is a backup.
- **A file that isn't whole isn't there.** One still arriving has no manifest yet, or files that don't match their
  checksums. It's passed over, never removed, and found whole a little later. What's offered before an app starts
  has been checked file by file.
- **The number in the name orders them**, not a file's date. It's when the backup was made, or one more than the
  newest number in the folder when that's higher, so a backup sorts after everything its computer had seen even when
  one computer's clock is behind.

What can't be told apart: two computers that both played before either's backup reached the other. The one whose
backup has the higher number is the newest; the other computer is asked which saves to keep when its own had changed,
and its saves are in its backups either way.

## Safety

Anything can be in that folder: another program can write there, and so can anyone it's shared with. A backup from
it is treated as a file from a stranger, as one from the account is ([cloud saves, Safety](cloud-saves.md#safety)):

- It's only taken when its manifest adds up, every file matches its checksum, it holds no more than saves do, it
  names the app whose folder it's in, and its name's checksum is its own.
- It's copied out of the folder first and checked there, so what's restored is what was checked.
- **It never chooses where its files go.** It goes through `restore` in `saves.rs`: files only go to places this
  computer already keeps the app's saves in, another folder is shown to the player first, and only saves come out.
- Links in the folder aren't followed, and a folder full of other things costs a bounded amount of looking.
- Nothing in the folder is removed but backups that read as the app's own, beyond the newest 5.
