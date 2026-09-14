# Flatpak release bundles

On Linux, Quiver recognises direct `.flatpak` release downloads and installs them for the current user. Install Flatpak and libflatpak with your distribution's software manager first; see [Flatpak setup](https://flatpak.org/setup/).

Quiver downloads the selected bundle, reads its application identity, and asks Flatpak to install it and its dependencies. The installation stage can take longer than the bundle download because shared runtimes may also be needed. If the runtime cannot be found, configure the publisher's runtime repository in your software manager and retry.

The app itself is managed by Flatpak. Quiver's normal app folder holds release metadata and catalog support files. “Open Folder” opens `~/.var/app/<application-id>/` once the app has created its data directory; otherwise it opens Quiver's metadata folder. Install location settings do not relocate Flatpak packages or inject files into their sandbox.

Launch, desktop shortcuts and Steam shortcuts target the exact per-user application, architecture and branch. Updates install the chosen release bundle. Uninstall preserves saves and application data; Quiver does not request data deletion or cleanup of runtimes, extensions or remotes. A system-wide installation of the same app remains untouched.

This version supports direct application bundles only. Flatpak references (`.flatpakref`), repository definitions (`.flatpakrepo`), wrapped bundles, portable mod installation, and version rollback are unsupported. Uninstall before switching an entry between portable and Flatpak packages. Two Quiver entries cannot manage the same user application reference.

`flatpak-install.json` records the installed reference, commit and release tag. A pending record allows Quiver to recover when Flatpak succeeds but writing the final receipt fails. Keep these files with the entry. If another manager updates the app, Quiver shows its release version as `Unknown`; if another manager removes it, Quiver offers installation again.

## Automated verification

Run the normal test suite on any supported .NET development host:

```text
dotnet test QuiverLauncher.Tests/QuiverLauncher.Tests.csproj
```

The native integration test is opt-in because it installs a disposable app. On a Linux desktop with .NET 10, Flatpak/libflatpak, a working session bus and an already installed **per-user** runtime containing `/bin/sh`, choose its `ID/architecture/branch` from `flatpak list --user --runtime --columns=ref`, then run:

```bash
export QUIVER_FLATPAK_TEST_RUNTIME='ID/architecture/branch'
dotnet test QuiverLauncher.Tests/QuiverLauncher.Tests.csproj --filter FullyQualifiedName~FlatpakLinuxIntegrationTests
```

The test generates two bundles for a unique test application, verifies the native bundle reader, installs and launches both releases, checks their commits, uninstalls, and verifies save preservation. It cleans up its own disposable app/data and temporary files. It does not install or remove the selected runtime. Without the environment variable, the test is reported as skipped.

## Linux desktop smoke test

Run Quiver's packaged AppImage on a Linux desktop, and use [TriAevum's releases](https://github.com/coccofresco/TriAevum/releases):

1. Confirm the Linux x86_64 bundle appears in the download chooser and the catalog Linux filter.
2. Install, launch its setup wizard, and verify desktop and Steam shortcuts. ROM preparation can be checked separately with the user's own supported ROM.
3. Install a newer bundle and confirm the release tag changes while prepared data remains.
4. Remove the app outside Quiver and refresh; it should become uninstalled. Reinstall from Quiver, then uninstall from Quiver and confirm its data survives.
5. Repeat launch from Quiver's AppImage to check that host Flatpak processes are unaffected by AppImage environment variables.

Native Linux integration and TriAevum/AppImage smoke testing require that environment; passing the cross-platform tests alone does not establish those results.
