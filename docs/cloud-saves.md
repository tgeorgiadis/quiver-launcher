# Cloud saves

A signed-in player's save backups are also kept with their Quiver account, so they follow them to another
computer. It rides on save backups (`apps/launcher/src-tauri/src/saves.rs`): the copy sent is a backup's own zip,
and a copy from the account is taken in as a backup and put in place by the same Restore, with the same checks.

A player who'd rather not use an account, or wants a second place, can have backups kept in a folder of their own
that Dropbox, OneDrive or Syncthing syncs: see [Sync folder](sync-folder.md).

## What is sent

One zip per backup: the save files and a manifest naming the app, when the backup was made, the app's version, the
computer's name and system, each save folder as a path under one of the system's folders (`app/saves`,
`localData/Game`), and a checksum of every file. Nothing else from the computer.

It goes straight to quiverlauncher.com's private storage through a link the site signs for that one file, which
works for 2 minutes. The site keeps the record: which account, which app, the size, the checksum, the computer's
name. Only that account can list, download or remove its copies.

Only apps from the catalog have cloud saves, only while signed in, and not for an app or a launcher it's switched
off for (Settings, or the app's Saves tab).

## When

- **After a backup** (after playing, before an update or an uninstall, or "Back up now"): the backup is sent
  (`sendBackup` in `apps/launcher/src/cloud-saves.ts`). If the account already has the same saves, nothing is
  uploaded.
- **Before an app starts** (`beforePlay`): the account's newest copy is compared with the saves on the computer.

| Saves here | Account's newest copy | What happens |
| --- | --- | --- |
| The same | | The app starts |
| None yet, or unchanged since they last matched | Different | The copy is downloaded and put in place, then the app starts |
| Changed since they last matched | What they last matched | The app starts; these go up after playing |
| Changed | Changed too, or they never matched | The player chooses which to keep |

"Last matched" is one checksum per app, kept beside its backups (`state.json`, `synced`). Putting a copy in place
first backs up the saves that are there, so either choice can be undone from the Saves tab.

A copy made by a newer version of the app than the one installed is never put in place by itself before play
(`fromNewer`): an older app may not load it, or may write over it. The same question says both versions, and when
the app has an update it offers it first ("Update to v1.1.0"), which starts nothing and leaves the saves as they
are; after the update, Play puts the copy in place as usual. Only at play: a restore from the Saves tab is the
player's own choice and isn't asked about. Versions compare as numbers; one that isn't a number never asks.

A copy made on another system (Windows, Linux, macOS) is only put in place by itself when the catalog says the
app's saves load on any system. The player can still restore any copy from the Saves tab.

The check never holds an app back for long: when the account can't be reached within a few seconds, the app starts
with the saves on the computer.

## From a shortcut

A desktop shortcut or a Steam shortcut made in Quiver starts the game through Quiver
(`apps/launcher/src-tauri/src/shortcut_play.rs`), so the same happens as with Play: the saves are brought in line
before the game starts, and backed up and sent after it closes. The shortcut runs `quiver --launch <app>`. That finds
the Quiver that's open, or starts one with no window, which works out the game's command as Play would, with the
player's launch options as they are now. Anything the player adds to the shortcut's own arguments goes after the
game's.

The shortcut starts the game itself, not Quiver: Steam ties its overlay, the shortcut's controller settings, its
"playing" status and its Stop to the program it started and what that starts. A game started by another program has
none of them, and Game Mode doesn't show it at all.

- **A question to ask** (both sides changed): a Quiver the shortcut started shows its window for it, then hides it
  again. In Game Mode that works because that Quiver runs among the shortcut's processes, which Game Mode shows.
- **What Play would ask first** (the player's copy of the game, which of several programs, the program a Windows
  installer put somewhere): the shortcut starts the first program, or says to play it from Quiver once.
- **Steam's Stop (Windows)** ends the shortcut and the game at once. A Quiver the shortcut starts there runs outside
  Steam's processes, so it's still there to back up and send the saves.
- **Steam's Exit Game (Linux)** asks the shortcut's processes to stop and ends them two seconds later. The shortcut
  starts a Quiver outside Steam's processes at once (`systemd-run --user`, or `flatpak run` on the host for the Flatpak)
  with `--upload-saves`, which backs up the saves once the game has closed, sends them and quits. Offline, the backup
  waits on the computer, and the next start of the app sends it.

A Quiver a shortcut started quits once the saves are sent, unless the player opened its window. Each step is noted in
`quiver.log` ("shortcut: …"). Quiver listens for
shortcuts on 127.0.0.1, at a port written to `shortcuts.json` in its data folder with a token only the player's own
programs can read.

## Limits

The site sets them, and the launcher shows what the site says: by default 250 MB an account, the 5 newest copies of
each app, 100 MB a copy. Quiver can change them, give one app a bigger copy or one account more room. Over the limit,
the backup stays on the computer and the Saves tab says the account is full.

Quiver can also pause uploads for everyone, or stop one account's. Nothing is sent then, and the launcher says which
it is. Copies already in the account can still be restored and removed, and backups on the computer carry on.

## When a backup doesn't get there

A send that fails (no connection, the site busy, an upload cut off) is tried again after 1, 5, 15 and 60 minutes
(`retries` in `cloud-saves.ts`). After that the next backup, or the next start of the app, sends what's newest.

When the account has no room, the backup stays on the computer and the launcher says so once, in a dialog with two
ways to make room: "Free up space" opens Settings at Cloud saves, and "Turn off cloud saves" for the app keeping the
most switches it off on this computer and removes its copies from the account. It isn't said again until a backup
gets there. Once copies are removed in the launcher, the backups that didn't fit are sent by themselves.

## The whole account

Settings has a Cloud saves section (`cloud-saves-page.tsx`) with everything the account keeps: a bar of the room used
and each app that has copies, biggest first, with how many copies, their size, and when and from which computer it
last synced. An app opens to its copies. Any copy can be removed, and all of an app's after a question. For an app
installed on this computer, a copy can be restored, its saves sent now, and its save folder opened. The switch for
cloud saves as a whole is there too.

## Safety

A copy from the account is treated as a file from a stranger: the site never looks inside one, so anyone who got
into the account, or the storage behind it, could have put it there. It goes through the same checks as any backup
being restored (`restore` in `saves.rs`):

- **It never chooses where its files go.** A backup names the places its saves were in, but files are only written
  into places this computer already keeps the app's saves in: the folder the player chose, the catalog's, or the
  ones the launcher found here. A backup that names another folder is not restored by itself. The player is shown
  the folder as it is on this computer and agrees to it first (before play, in the "which saves" question), and
  only a folder inside the app's own, or one named like the app, can be offered at all. Agreeing remembers the
  folder as a place the app saves. A downloaded copy sets no save places by itself.
- Never a folder that holds keys, another program's data, or files a system runs when it starts, however the name
  is written (a dot or a space at the end, or Windows's short names, are refused).
- Only files that pass as saves, each checked against its checksum. Programs, scripts and shortcuts are never
  backed up, removed or put back, so a backup can't swap the script an app starts with.
- No more than saves hold: a backup naming too many files, or too much in them, isn't unpacked.

A copy that isn't whole, or isn't the one asked for, is thrown away and the saves on the computer are left as they
were.
