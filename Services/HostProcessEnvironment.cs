using System.Diagnostics;

namespace QuiverLauncher.Services;

/// <summary>
/// Strips AppImage/Steam library paths and identity vars from child processes
/// so host binaries (games, file managers) see a normal desktop environment.
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
