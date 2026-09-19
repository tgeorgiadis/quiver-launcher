# Library protection and recovery

`apps.json` is the authoritative library. Quiver does not rebuild it from the `Apps`
folder: installed files alone do not contain all library metadata and preferences.

Quiver validates the whole local library before using it. Invalid JSON, unsupported
root structure, malformed entries, and file access errors stop the operation; they
are never interpreted as an empty library. Startup does not normalize or rewrite
an existing file. Remote catalog parsing remains separate from local library safety.

## Backups

The first successful read of an existing library preserves its original bytes in
`Backups/apps/` alongside `apps.json`. Each changed save preserves both the previous
file and the new snapshot before atomically replacing the library. Backups and the
temporary replacement are flushed to disk before being committed. A failed backup
prevents the save. Writes use a shared lock across Quiver instances.

Backup names are the SHA-256 hash of their contents followed by `.json`. Identical
snapshots reuse the same file and are never overwritten. This also preserves
non-empty libraries after repeated empty saves. To keep recovery storage bounded,
Quiver retains the newest 50 library snapshots and prunes older snapshots once
the folder exceeds 25 MB. The newest snapshot is always retained, and pruning
failures never block a successful library save. Move the `Backups` folder with
the launcher data, and include it in external backups.
These copies use the same disk as the library and do not protect against loss of
that disk. They cannot recover data that was already lost before this protection
was installed.

A missing library with existing recovery files is an error, not a fresh install.
Quiver does not automatically restore a snapshot because an older snapshot may omit
recent changes. A genuinely new library can start empty. Legacy `games.json`
migration validates and backs up the complete original file and leaves it intact.

## Restore a snapshot

Self-updates also save paired copies of `apps.json` and `settings.json` in a unique
dated folder under `Backups/updates`. Desktop updates back up before downloading
(a staged update can apply on next startup) and again before applying/restarting.
Android backs up before handing the APK to the installer. A failed backup stops
the update operation and leaves the originals unchanged. Completed snapshots
include a `manifest.json` with hashes; files absent in a new profile are recorded
as null rather than replaced with empty files. Folders ending in `.incomplete`
are unfinished attempts. Completed snapshots are retained up to 10 folders or
25 MB, whichever limit is reached first; the newest complete snapshot is always
kept and pruning failures never block an update.

To restore an update snapshot, close Quiver, preserve the current data folder, and
copy the desired `apps.json` and/or `settings.json` back to the data root.

1. Close every Quiver instance.
2. Copy the launcher data folder somewhere safe, including the damaged `apps.json`
   and `Backups` folder.
3. Open `Backups/apps`. Use modification dates and inspect the JSON contents to
   choose the desired library. A backup may have been prepared for a save that did
   not finish, so check the entries rather than assuming the newest file is right.
4. Copy that backup to the data root as `apps.json`, retaining the backup itself.
5. Reopen Quiver and check the library. Installed files in `Apps` are not changed
   by restoring the library file.

If no backup exists, check an older launcher data folder, legacy `games.json`, or
external backups. Do not replace a damaged file with an empty library as a repair.

When rebuilding a library after data loss, adding the same catalog apps checks
their expected folders under the configured Apps directory. Complete installations
are reused without downloading, and existing `version.txt` and app files are kept.
This applies to both single Add and bulk Add. An empty folder, metadata alone, or
an unfinished installation is not treated as installed. Renamed folders or custom
installation paths lost with the library may need **Locate Install**.
