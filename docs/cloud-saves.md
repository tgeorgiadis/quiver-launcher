# Cloud saves

A signed-in player's save backups are also kept with their Quiver account, so they follow them to another
computer. It rides on save backups (`apps/launcher/src-tauri/src/saves.rs`): the copy sent is a backup's own zip,
and a copy from the account is taken in as a backup and put in place by the same Restore, with the same checks.

## What is sent

One zip per backup: the save files and a manifest naming the app, when the backup was made, the app's version, the
computer's name and system, each save folder as a path under one of the system's folders (`app/saves`,
`localData/Game`), and a checksum of every file. Nothing else from the computer.

It goes straight to quiverlauncher.com's private storage through a link the site signs for that one file, which
works for 10 minutes. The site keeps the record: which account, which app, the size, the checksum, the computer's
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

A copy made on another system (Windows, Linux, macOS) is only put in place by itself when the catalog says the
app's saves load on any system. The player can still restore any copy from the Saves tab.

The check never holds an app back for long: when the account can't be reached within a few seconds, the app starts
with the saves on the computer.

## Limits

The site sets them: 250 MB an account, the 5 newest copies of each app, 100 MB a copy. Over the limit, the backup
stays on the computer and the Saves tab says the account is full.

## Safety

A copy from the account goes through the same checks as any backup being restored: its files are only written into
folders Quiver allows as save locations, never a folder that holds keys or another program's data, and only files
that pass as saves, each checked against its checksum. A copy that isn't whole, or isn't the one asked for, is
thrown away and the saves on the computer are left as they were.
