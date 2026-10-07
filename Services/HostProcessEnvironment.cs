using System.Diagnostics;

namespace QuiverLauncher.Services;

/// <summary>
/// Strips AppImage/Steam library paths and identity vars from child processes
/// so host binaries (games, file managers) see a normal desktop environment.
/// Also routes those processes through <c>flatpak-spawn --host</c> when Quiver
/// itself is running inside a Flatpak sandbox, since a bare command or host
/// path exec'd directly from inside the sandbox resolves against the
/// runtime's own /usr (missing binaries, or the wrong shared libraries).
/// </summary>
internal static class HostProcessEnvironment
{
    internal static readonly string[] HostBreakingEnvironmentVariables =
    [
        "LD_LIBRARY_PATH",
        "LD_PRELOAD",
        "QT_PLUGIN_PATH",
        "QTDIR",
        "QT_QPA_PLATFORM_PLUGIN_PATH",
        "APPDIR",
        "APPIMAGE",
        "ARGV0",
        "OWD",
    ];

    internal static void Sanitize(ProcessStartInfo startInfo)
    {
        startInfo.Environment.TryGetValue("APPDIR", out var appDir);

        foreach (var name in HostBreakingEnvironmentVariables)
            startInfo.Environment.Remove(name);

        var extraKeys = startInfo.Environment.Keys
            .Where(key => key.StartsWith("APPIMAGE_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in extraKeys)
            startInfo.Environment.Remove(key);

        SanitizePath(startInfo, appDir);
        ApplyWorkingDirectoryPwd(startInfo);
    }

    /// <summary>
    /// Clears host-breaking variables, then applies extra vars (Proton/Wine)
    /// so runner paths are not stripped.
    /// </summary>
    internal static void SanitizeThenApply(
        ProcessStartInfo startInfo,
        IEnumerable<KeyValuePair<string, string>>? additionalVariables = null)
    {
        Sanitize(startInfo);

        if (additionalVariables == null)
            return;

        foreach (var variable in additionalVariables)
            startInfo.Environment[variable.Key] = variable.Value;
    }

    /// <summary>
    /// Env vars that identify/scope *Quiver's own* Flatpak sandbox instance and
    /// must never be forwarded to a host process via <see cref="RouteToHostIfSandboxed"/>.
    /// Notably <c>DBUS_SESSION_BUS_ADDRESS</c>: Quiver's sandbox points this at its
    /// own private xdg-dbus-proxy socket (e.g. /run/flatpak/bus), which doesn't exist
    /// on the host at all - a host process (e.g. Proton, which launches games via a
    /// D-Bus portal call) inheriting it fails immediately trying to connect. The
    /// XDG_* vars similarly point at Quiver's own ~/.var/app/... subdirectories, not
    /// the host's real ones. Same story for audio/accessibility: ALSA_CONFIG_PATH
    /// points at a Flatpak-runtime-only shim config, and PULSE_SERVER/PULSE_CLIENTCONFIG/
    /// AT_SPI_BUS_ADDRESS point at sandbox-private proxy sockets under /run/flatpak/ -
    /// forwarding any of these breaks the host process's audio/accessibility entirely
    /// instead of letting it fall back to the real host defaults (which just work).
    /// </summary>
    internal static readonly string[] SandboxIdentityEnvironmentVariables =
    [
        "DBUS_SESSION_BUS_ADDRESS",
        "XDG_DATA_HOME",
        "XDG_CACHE_HOME",
        "XDG_STATE_HOME",
        "XDG_CONFIG_HOME",
        "XDG_DATA_DIRS",
        "XAUTHORITY",
        "FLATPAK_ID",
        "FLATPAK_SANDBOX_DIR",
        "container",
        "ALSA_CONFIG_PATH",
        "ALSA_CONFIG_DIR",
        "PULSE_SERVER",
        "PULSE_CLIENTCONFIG",
        "AT_SPI_BUS_ADDRESS",
    ];

    internal static bool IsSandboxed(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        return !string.IsNullOrEmpty(getEnvironmentVariable("FLATPAK_ID"));
    }

    /// <summary>
    /// When sandboxed, rewrites <paramref name="startInfo"/> in place to run via
    /// <c>flatpak-spawn --host</c> so the target resolves against the host's own
    /// PATH/libraries instead of the sandbox runtime's. No-op otherwise. Must be
    /// called last, after <see cref="Sanitize"/>/<see cref="SanitizeThenApply"/>
    /// and any other environment changes, and requires the target command/args
    /// to already be in <see cref="ProcessStartInfo.ArgumentList"/> (not the
    /// <see cref="ProcessStartInfo.Arguments"/> string).
    /// </summary>
    internal static void RouteToHostIfSandboxed(
        ProcessStartInfo startInfo,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!OperatingSystem.IsLinux() || !IsSandboxed(getEnvironmentVariable))
            return;

        // Capture before stripping: the sandbox's XAUTHORITY (e.g.
        // /run/flatpak/Xauthority) is a working cookie for the current X11
        // socket, but that path is a per-sandbox-instance proxy invisible on
        // the real host. There's no reliable way to discover the host's own
        // X11 auth file from in here (desktop environments vary - e.g. Mutter
        // uses a dynamically-named ~/.mutter-Xwaylandauth.* file, not
        // ~/.Xauthority), so copy the cookie we already know works to a real,
        // host-visible file instead of trying to locate the host's.
        var hostXauthority = TryCopyXauthorityForHost(startInfo);

        foreach (var name in SandboxIdentityEnvironmentVariables)
            startInfo.Environment.Remove(name);
        // The sandbox's own PATH (e.g. "/app/bin:/usr/bin") is meaningless on the
        // host; give the host process a normal one instead of forwarding it as-is.
        startInfo.Environment["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        if (hostXauthority != null)
            startInfo.Environment["XAUTHORITY"] = hostXauthority;

        var hostArgs = new List<string> { "--host" };
        if (!string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
            hostArgs.Add("--directory=" + startInfo.WorkingDirectory);

        // Reproduce startInfo.Environment exactly as the host command's env,
        // rather than letting flatpak-spawn forward the sandbox's own ambient
        // env (full of /app paths) by default.
        hostArgs.Add("--clear-env");
        foreach (var pair in startInfo.Environment)
        {
            if (pair.Value != null)
                hostArgs.Add($"--env={pair.Key}={pair.Value}");
        }

        hostArgs.Add("--");
        hostArgs.Add(startInfo.FileName);
        hostArgs.AddRange(startInfo.ArgumentList);

        startInfo.FileName = "flatpak-spawn";
        startInfo.ArgumentList.Clear();
        foreach (var arg in hostArgs)
            startInfo.ArgumentList.Add(arg);
    }

    static string? TryCopyXauthorityForHost(ProcessStartInfo startInfo)
    {
        try
        {
            if (!startInfo.Environment.TryGetValue("XAUTHORITY", out var sandboxXauthority) ||
                string.IsNullOrWhiteSpace(sandboxXauthority) || !File.Exists(sandboxXauthority))
                return null;

            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome) || !Directory.Exists(dataHome))
                return null;

            var destination = Path.Combine(dataHome, "flatpak-host-xauthority");
            File.Copy(sandboxXauthority, destination, overwrite: true);
            return destination;
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsAppImageMountPath(string path, string? appDir)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!string.IsNullOrWhiteSpace(appDir) &&
            path.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Contains("/.mount_", StringComparison.Ordinal);
    }

    static void SanitizePath(ProcessStartInfo startInfo, string? appDir)
    {
        if (!startInfo.Environment.TryGetValue("PATH", out var path) || string.IsNullOrEmpty(path))
            return;

        var kept = path
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !IsAppImageMountPath(entry, appDir))
            .ToList();

        startInfo.Environment["PATH"] = kept.Count > 0
            ? string.Join(':', kept)
            : "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
    }

    static void ApplyWorkingDirectoryPwd(ProcessStartInfo startInfo)
    {
        if (string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
            return;

        startInfo.Environment["PWD"] = startInfo.WorkingDirectory;
    }
}
