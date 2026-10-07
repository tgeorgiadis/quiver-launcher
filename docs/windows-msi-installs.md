# Windows Installer apps

Quiver handles `.msi` release assets through the Windows setup wizard. After setup,
select the installed `.exe`; the program can stay in its normal installation folder.
For OpenGOAL Launcher this is usually
`C:\Program Files\OpenGOAL-Launcher\OpenGOAL-Launcher.exe`.
OpenGOAL handles game setup after it starts.

- Cancelling executable selection leaves **Select executable** available across restarts.
- **Change executable** replaces the link; cancelling keeps the existing link.
- **Run installer again** downloads a release and reopens setup.
- Updates require interaction, even if Auto Update was previously enabled.
- **Uninstall in Windows…** opens Installed apps. Removing the Quiver entry only
  removes Quiver's link; it does not uninstall the Windows application.

Quiver stores `windows-installer.json` and `installer-*.log` in its per-app metadata
folder. It does not move installed program files or store metadata beside an
external executable. Version tracking reflects the last successful installation
through Quiver, not updates performed by the application itself.

MSI installation is supported in the Windows desktop interface only. Linux/Wine
installation, unattended CLI installation, automatic executable discovery, and
direct MSI uninstallation are outside this implementation.

## Manual Windows acceptance check

The automated tests use a mocked installer and never install third-party software.
Before release, verify the actual wizard and UAC interaction on a Windows desktop:

1. Add `open-goal/launcher`, choose its Windows MSI, and select **Run installer**.
2. Install in the default location, then select `OpenGOAL-Launcher.exe`.
3. Launch it from Quiver, restart Quiver, and launch again.
4. Cancel executable selection on a separate fresh test entry; restart Quiver and
   confirm **Select executable** resumes linking without reinstalling.
5. Run the installer again and cancel the wizard; the previous link and version
   should remain usable. Verify a successful update reuses the link.
6. Select **Uninstall in Windows…** and confirm Installed apps opens without Quiver
   deleting files. If Windows uninstalls the app, refreshing Quiver should show
   **Select executable**, with **Run installer again** available.
