# Quiver Launcher 3.4.1

## Desktop improvements

- Collapse the sidebar using the new icon beside the page title. The same button brings it back, with a short animation. Quiver remembers your choice between sessions, and the toggle supports keyboard and controller navigation.
- Remember window size, position and maximized state between sessions. Start Fullscreen still takes priority, and saved placement adjusts when monitors or display scaling change.
- Add 50% and 75% options under Settings → Appearance → Interface scale.
- Fix custom background images not appearing after selection.
- Make the update-check bar more compact and centre its text and controls. Incomplete or cancelled checks can now be dismissed while keeping Retry available.

## Linux and Flatpak

- Download, install, launch, update and uninstall direct `.flatpak` release bundles, including TriAevum. Flatpak bundles now count as Linux assets in catalog platform detection, and older cached results are refreshed.
- Use per-user Flatpak installations and keep saves when uninstalling. Quiver checks the installed application reference, detects external removal or changes, and prevents duplicate management of the same Flatpak application reference.
- Launch Flatpak apps through the GUI, CLI, desktop shortcuts and Steam shortcuts. Fix the misleading thread-access error that could appear after a successful launch.
- Recognise Linux executable files and launcher scripts correctly, instead of selecting documentation such as `LICENSE`.
- Keep selected shell wrappers such as Open Nectar's `nectar-launcher`, their companion binaries and libraries together. Selecting a Linux script from a mixed Linux/Windows package now launches it natively instead of through Wine.

Flatpak and its library must already be installed on your system. This release supports direct `.flatpak` bundles; `.flatpakref`, `.flatpakrepo`, bundles inside archives and Flatpak version rollback are not supported yet.

## Library and catalog fixes

- Keep merged catalog entries matched when a combined entry is split into separate games using release-asset filters. Adding the sibling no longer makes the merged game appear as a new Add, and duplicate Adds are blocked.
- Preserve existing installation folders and local preferences when merging split catalog entries.
- Avoid treating failed or blocked portable installations as installed apps. Missing executables, including files removed by antivirus software, no longer leave misleading installed/update status.
