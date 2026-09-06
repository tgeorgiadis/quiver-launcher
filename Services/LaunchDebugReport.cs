using System.Diagnostics;
using System.Text;

namespace QuiverLauncher.Services;

/// <summary>
/// Formats a per-launch report so Linux Play failures can be diagnosed
/// from files on disk (no debugger needed).
/// </summary>
internal static class LaunchDebugReport
{
    public const string GameFolderFileName = "quiver-launch.log";
    public const string UserDataFileName = "launch-debug.log";

    public static string UserDataLogPath =>
        Path.Combine(QuiverLauncherPaths.UserDataRoot, UserDataFileName);

    public static string GameFolderLogPath(string gamePath) =>
        Path.Combine(gamePath, GameFolderFileName);

    public static bool IsAllowlisted(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (key.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("PWD", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("APPDIR", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("APPIMAGE", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("ARGV0", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("OWD", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("WAYLAND_DISPLAY", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("XDG_DATA_DIRS", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("XDG_CURRENT_DESKTOP", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("XDG_SESSION_TYPE", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("SteamDeck", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("PYTHONHOME", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("GSETTINGS_SCHEMA_DIR", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return key.StartsWith("LD_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("APPIMAGE_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("QT_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("SDL_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("STEAM", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("LIBGL_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("GST_PLUGIN", StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> FilterAllowlisted(
        IEnumerable<KeyValuePair<string, string>> environment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in environment)
        {
            if (!IsAllowlisted(pair.Key))
                continue;

            result[pair.Key] = pair.Value;
        }

        return result;
    }

    public static Dictionary<string, string> SnapshotProcessEnvironment()
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
                pairs.Add(new KeyValuePair<string, string>(key, value));
        }

        return FilterAllowlisted(pairs);
    }

    public static Dictionary<string, string> SnapshotStartInfoEnvironment(ProcessStartInfo startInfo)
    {
        if (startInfo.UseShellExecute)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var pair in startInfo.Environment)
        {
            if (pair.Value == null)
                continue;

            pairs.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
        }

        return FilterAllowlisted(pairs);
    }

    public static List<string> KeysRemoved(
        IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, string> after)
    {
        return before.Keys
            .Where(key => !after.ContainsKey(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Dictionary<string, string> ParseProcEnviron(byte[] environBytes)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (environBytes.Length == 0)
            return result;

        var text = Encoding.UTF8.GetString(environBytes);
        foreach (var entry in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = entry[..separator];
            if (!IsAllowlisted(key))
                continue;

            result[key] = entry[(separator + 1)..];
        }

        return result;
    }

    public static Dictionary<string, string>? TryReadProcEnviron(int pid)
    {
        try
        {
            var path = $"/proc/{pid}/environ";
            if (!File.Exists(path))
                return null;

            return ParseProcEnviron(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    public static string FormatEnvBlock(string title, IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"=== {title} ===");
        if (environment.Count == 0)
        {
            builder.AppendLine("(none)");
            return builder.ToString();
        }

        foreach (var key in environment.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"{key}={environment[key]}");

        return builder.ToString();
    }

    public static string Build(
        DateTimeOffset timestamp,
        string gameName,
        string installPath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        int? pid,
        IReadOnlyDictionary<string, string>? liveProcEnviron,
        bool? hasExited = null,
        int? exitCode = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"timestamp={timestamp:O}");
        builder.AppendLine($"game={gameName}");
        builder.AppendLine($"installPath={installPath}");
        builder.AppendLine($"chosenExecutable={chosenExecutable}");
        builder.AppendLine($"selectedExecutableFile={selectedExecutableFile ?? "(none)"}");
        builder.AppendLine("candidates=");
        foreach (var candidate in candidates)
            builder.AppendLine($"  {candidate}");

        builder.AppendLine($"FileName={startInfo.FileName}");
        builder.AppendLine($"WorkingDirectory={startInfo.WorkingDirectory}");
        builder.AppendLine($"UseShellExecute={startInfo.UseShellExecute}");
        builder.AppendLine($"Arguments={startInfo.Arguments}");
        if (startInfo.ArgumentList.Count > 0)
        {
            builder.AppendLine("ArgumentList=");
            foreach (var argument in startInfo.ArgumentList)
                builder.AppendLine($"  {argument}");
        }

        var removed = KeysRemoved(processEnvBefore, startInfoEnvAfter);
        builder.AppendLine("removedBySanitize=");
        if (removed.Count == 0)
            builder.AppendLine("  (none)");
        else
        {
            foreach (var key in removed)
                builder.AppendLine($"  {key}");
        }

        builder.Append(FormatEnvBlock("process env (before)", processEnvBefore));
        builder.Append(FormatEnvBlock("ProcessStartInfo env (after sanitize)", startInfoEnvAfter));

        builder.AppendLine($"pid={pid?.ToString() ?? "(not started)"}");
        if (liveProcEnviron != null)
            builder.Append(FormatEnvBlock("/proc/pid/environ (live)", liveProcEnviron));
        else
            builder.AppendLine("liveProcEnviron=(unavailable)");

        if (hasExited != null)
        {
            builder.AppendLine($"HasExited={hasExited}");
            builder.AppendLine(exitCode != null ? $"ExitCode={exitCode}" : "ExitCode=(none)");
        }
        else
        {
            builder.AppendLine("HasExited=(pending)");
        }

        return builder.ToString();
    }

    public static void TryWrite(string gamePath, string report)
    {
        try
        {
            File.WriteAllText(GameFolderLogPath(gamePath), report);
        }
        catch
        {
        }

        try
        {
            QuiverLauncherPaths.EnsureUserDataRootExists();
            File.AppendAllText(UserDataLogPath, report + Environment.NewLine);
        }
        catch
        {
        }
    }

    public static void TryRewriteGameFolder(string gamePath, string report)
    {
        try
        {
            File.WriteAllText(GameFolderLogPath(gamePath), report);
        }
        catch
        {
        }
    }

    public static void TryAppendUserData(string text)
    {
        try
        {
            QuiverLauncherPaths.EnsureUserDataRootExists();
            File.AppendAllText(UserDataLogPath, text);
        }
        catch
        {
        }
    }
}
