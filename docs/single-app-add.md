# Adding an app from the App Catalog

**Add to library** in an app's App Catalog details saves one app through the session's FIFO write queue (`LauncherSession.CatalogMutations`), so an Add and a Remove never interleave. Navigation does not cancel an accepted save, and the launcher session drains the queue before disposing services.

A catalog app becomes a library entry through the same parser as `apps.json` (`QuiverCatalogMapping.ToGameInfo`): the repository comes from the app's project on quiverlauncher.com, and the folder name, files to add, release filter and mods come from its launcher settings. An app from the player's own list is already in that format.

The commit rereads the saved library inside the queue. An app is already in the library when it has the same instance key, or the same repository and release filter in another folder. A different app using the same folder is refused with a message naming it. Otherwise the library is written to a same-directory temporary file, flushed, and atomically replaces `apps.json`. A failure leaves the previous file intact, and an unreadable or corrupt library fails the operation instead of being treated as empty.

All local library saves also preserve immutable snapshots in `Backups/apps` before
replacement. Failure to create or verify the backup stops the save. See
[library protection and recovery](library-recovery.md).

Only the new app receives filesystem preparation, a local status check and cached artwork loading. It is inserted into the existing library collection in sort order, without a library reload. If its folder already holds an install, it shows as installed and its files are left alone. Preparation failures keep the saved app and are reported separately; downloads await required preparation.

After insertion, session-owned background work resolves the usable release through the existing release-selection policy and fills missing artwork through the shared image loader. This work continues after navigation, is cancelled and drained on shutdown, and never holds the Add queue open.
