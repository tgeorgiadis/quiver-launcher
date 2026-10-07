# Flatpak packaging (unofficial, not published)

This repackages the self-contained Linux `dotnet publish` output as a
Flatpak. It is not wired into CI or Flathub — build it locally.

## Build

```bash
# 1. From the repo root: produce the self-contained publish output.
#    (The Flatpak build sandbox has no network access, so this can't
#    happen inside flatpak-builder — it has to be a prebuilt input.)
dotnet publish QuiverLauncher.Desktop/QuiverLauncher.Desktop.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishTrimmed=false -o publish/linux-x64

# 2. Build and install for the current user.
flatpak-builder --user --install --force-clean \
  packaging/flatpak/build packaging/flatpak/io.github.tgeorgiadis.QuiverLauncher.yml

# 3. Run it.
flatpak run io.github.tgeorgiadis.QuiverLauncher
```

Requires `flatpak` and `flatpak-builder`, plus the `org.freedesktop.Platform`
and `org.freedesktop.Sdk` runtimes (24.08) from Flathub:

```bash
flatpak install flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08
```

## Permissions (`finish-args`)

Requested as a broad, permissive set rather than tightly scoped:

| Grant | Why |
|---|---|
| `--share=network` | catalog/release downloads |
| `--socket=x11` (unconditional, not `fallback-x11`) | the app only ships Avalonia's X11 backend (no `Avalonia.Wayland` package), so it needs X11/XWayland even in a Wayland session — `fallback-x11` would withhold it there |
| `--socket=wayland` | granted for when/if a Wayland backend is added later; currently unused |
| `--socket=pulseaudio` | audio for launched games |
| `--device=all` | GPU (`dri`) plus controllers/joysticks for games |
| `--filesystem=host` | read/write anywhere a user picks a library or game install folder |
| `--filesystem=~/.var/app/com.valvesoftware.Steam:ro` | `--filesystem=host` deliberately excludes *other apps'* `~/.var/app/<id>` data (Flatpak's per-app isolation) — needed explicitly so Proton detection (`Services/WindowsRunnerService.cs`) can see `steamapps/common/Proton*` when Steam itself is Flatpak-installed |
| `--talk-name=org.freedesktop.Flatpak` | needed for `flatpak-spawn --host`, see gap below |
| `--system-talk-name=org.freedesktop.Flatpak.SystemHelper` | system-wide Flatpak installs, if ever used |

## Game launching is now sandbox-aware

The packaged app installs, launches, and its own UI (catalog browsing,
settings, library) comes up — [Services/QuiverLauncherPaths.cs](../../Services/QuiverLauncherPaths.cs)
already falls back to `XDG_DATA_HOME` when it isn't running as a
Velopack-managed AppImage, so under Flatpak it correctly lands in
`~/.var/app/io.github.tgeorgiadis.QuiverLauncher/data/` — no code
changes needed for that part.

Launching games, the `flatpak` CLI (used both to manage Flatpak-packaged
games and to launch them), `xdg-open`/`gio`/`kde-open`/`dolphin` (Open
Folder, links), and the `which wine`/`which wine64` runner-detection probe
all go through [Services/HostProcessEnvironment.cs](../../Services/HostProcessEnvironment.cs)'s
`RouteToHostIfSandboxed`, which — only when `FLATPAK_ID` is set — rewraps
the command as `flatpak-spawn --host --clear-env --env=... -- <command>
<args>` so it resolves and runs against the **host's** `PATH` and shared
libraries instead of the sandboxed runtime's. This is a no-op on every
other install method (AppImage, portable, Windows, macOS).

### Known remaining gap: process-group exit detection

`CLIHandler.cs` and `LauncherForegroundController.cs` poll `ps -o pgid=`/
`-o pid= -g <pgid>` **locally** to detect when a launched game's whole
process group (including Wine/Proton helper subprocesses) has fully
exited. Since the real game process now runs host-side via
`flatpak-spawn --host`, the sandbox's local `ps` can't see it or its
host-side children (separate PID namespace). Basic exit detection
(`Process.Exited`/`HasExited`) still works correctly, since `flatpak-spawn`
blocks until the host command exits and mirrors its exit code — but the
process-*group*-drain polling used to catch lingering helper processes
will effectively always see an empty group as soon as the local
`flatpak-spawn` proxy exits, regardless of real host stragglers. Fixing
this would need `ps` itself routed through `flatpak-spawn --host`, or a
different exit-tracking strategy — not done here.
