# Android launcher updates

Android builds check the official Quiver GitHub Releases feed on startup/resume,
at most once every six hours. Manual checks bypass that interval, while respecting
GitHub retry timing. Debug builds skip automatic checks; Settings still supports
manual checks. No background worker or notification permission is needed.

A compact banner announces a new APK. Dismissing it is remembered for that version;
Settings → General → Quiver updates and the update badge retain access. Downloads
start only after Update. Android asks for installation permission if necessary and
then confirms installation. Cancelling keeps the validated download for Install update.

The process-owned updater stores metadata and staged APKs in the Android app's private
`launcher-updates` directory. Completed downloads survive navigation and restarts;
partial downloads are removed and restarted. The package ID, version, and signing
certificate are checked before installation. Android remains the authority on package
signatures and valid signing-key rotation. A persisted handoff and the installed package
identity reconcile success after Android terminates Quiver to replace it.

The GitHub asset must be named `QuiverLauncher-android.apk`. Stable users receive
stable releases unless Include preview updates is enabled; installed preview versions
continue to receive previews, matching desktop behavior. Keep the same package ID and
release signing key. The APK display version must match the release tag after normalizing
the `v` prefix; Android version codes must never decrease. Equal codes are permitted for
newer display versions, supporting the existing preview-to-stable version-code scheme.

Existing Android installations require one manual upgrade to get this updater.

## Device validation

Use an isolated emulator or test device and two successive APK builds signed with the
same release key. Install the older build, create library/settings data, and update using
the banner or Settings. Test unknown-source permission denial/grant, installer Cancel,
reopening after replacement, offline reuse of a downloaded APK, and retained library
data. Also check a build signed with a different key is rejected without uninstalling.
Never uninstall a user's existing Quiver installation to work around a signature mismatch.
