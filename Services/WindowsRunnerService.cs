using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public sealed class WindowsRunnerCommandSpec
{
    public required string FileName { get; init; }
    public required List<string> Arguments { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ProtonInstallationInfo
{
    public required string DisplayName { get; init; }
    public required string ProtonExecutable { get; init; }
    public required string SteamRoot { get; init; }
}

public static class WindowsRunnerService
{
    private sealed class ProtonInstallation
    {
        public required string ProtonExecutable { get; init; }
        public required string SteamRoot { get; init; }
    }

    private static readonly Dictionary<string, Func<string, string, string>> RunnerPlaceholderResolvers = new(StringComparer.Ordinal)
    {
        ["{exe}"] = (executablePath, _) => executablePath,
        ["{gamePath}"] = (_, gamePath) => gamePath,
        ["{exeDir}"] = (executablePath, gamePath) => Path.GetDirectoryName(executablePath) ?? gamePath,
    };

    public static LinuxWindowsRunnerKind ParseRunnerKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LinuxWindowsRunnerKind.Auto;

        return value.Trim().ToLowerInvariant() switch
        {
            "wine" => LinuxWindowsRunnerKind.Wine,
            "proton" => LinuxWindowsRunnerKind.Proton,
            "custom" => LinuxWindowsRunnerKind.Custom,
            _ => LinuxWindowsRunnerKind.Auto,
        };
    }

    public static string FormatRunnerKind(LinuxWindowsRunnerKind kind) =>
        kind switch
        {
            LinuxWindowsRunnerKind.Wine => "wine",
            LinuxWindowsRunnerKind.Proton => "proton",
            LinuxWindowsRunnerKind.Custom => "custom",
            _ => "auto",
        };

    public static string GetDefaultWinePrefixPath(string gamePath) =>
        Path.Combine(gamePath, ".wine-prefix");

    public static string GetDefaultProtonCompatDataPath(string gamePath) =>
        Path.Combine(gamePath, ".steam-compat-data");

    public static bool IsWindowsRunnerAvailable(AppSettings? settings = null, GameInfo? game = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        var kind = ParseRunnerKind(game?.LinuxRunner);
        if (kind == LinuxWindowsRunnerKind.Custom &&
            (!string.IsNullOrWhiteSpace(game?.LinuxCustomLaunchCommand) ||
             !string.IsNullOrWhiteSpace(settings?.LinuxWindowsLaunchCommand)))
        {
            return true;
        }

        if (kind == LinuxWindowsRunnerKind.Wine)
            return IsWineAvailable();

        if (kind == LinuxWindowsRunnerKind.Proton)
            return ListDetectedProtonInstallations().Count > 0 ||
                   (!string.IsNullOrWhiteSpace(game?.LinuxProtonPath) && File.Exists(game.LinuxProtonPath));

        if (!string.IsNullOrWhiteSpace(settings?.LinuxWindowsLaunchCommand))
            return true;

        return IsWineOrProtonAvailable();
    }

    public static bool IsWineAvailable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        (IsCommandAvailable("wine64") || IsCommandAvailable("wine"));

    public static IReadOnlyList<ProtonInstallationInfo> ListDetectedProtonInstallations()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return [];

        var results = new List<ProtonInstallationInfo>();
        foreach (var installation in GetProtonInstallations())
        {
            if (!File.Exists(installation.ProtonExecutable))
                continue;

            var dirName = Path.GetFileName(Path.GetDirectoryName(installation.ProtonExecutable)) ?? "Proton";
            results.Add(new ProtonInstallationInfo
            {
                DisplayName = dirName,
                ProtonExecutable = installation.ProtonExecutable,
                SteamRoot = installation.SteamRoot,
            });
        }

        return results;
    }

    public static WindowsRunnerCommandSpec BuildWindowsRunnerCommand(
        string commandTemplate,
        string executablePath,
        string gamePath)
    {
        var resolvedCommand = commandTemplate.Trim();

        if (!resolvedCommand.Contains("{exe}", StringComparison.Ordinal) &&
            !resolvedCommand.Contains("{gamePath}", StringComparison.Ordinal) &&
            !resolvedCommand.Contains("{exeDir}", StringComparison.Ordinal))
        {
            resolvedCommand += " {exe}";
        }

        var tokens = SplitRunnerCommand(resolvedCommand);
        if (tokens.Count == 0 || string.IsNullOrWhiteSpace(tokens[0]))
            throw new InvalidOperationException("The Linux Windows-runner command is empty.");

        var resolvedTokens = tokens
            .Select(token => ReplaceRunnerPlaceholders(token, executablePath, gamePath))
            .ToList();

        return new WindowsRunnerCommandSpec
        {
            FileName = resolvedTokens[0],
            Arguments = resolvedTokens.Skip(1).ToList(),
        };
    }

    public static WindowsRunnerCommandSpec? GetWindowsRunnerCommand(
        AppSettings settings,
        string executablePath,
        string gamePath,
        GameInfo? game = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        var kind = ParseRunnerKind(game?.LinuxRunner);

        if (kind == LinuxWindowsRunnerKind.Custom)
        {
            var custom = game?.LinuxCustomLaunchCommand;
            if (string.IsNullOrWhiteSpace(custom))
                custom = settings.LinuxWindowsLaunchCommand;
            if (string.IsNullOrWhiteSpace(custom))
                return null;

            return BuildWindowsRunnerCommand(custom, executablePath, gamePath);
        }

        if (kind == LinuxWindowsRunnerKind.Wine)
            return BuildWineCommand(executablePath, gamePath, game?.LinuxPrefixPath);

        if (kind == LinuxWindowsRunnerKind.Proton)
            return BuildProtonCommand(executablePath, gamePath, game?.LinuxPrefixPath, game?.LinuxProtonPath);

        // Auto: global custom command, then Proton (preferred), then Wine.
        if (!string.IsNullOrWhiteSpace(settings.LinuxWindowsLaunchCommand))
            return BuildWindowsRunnerCommand(settings.LinuxWindowsLaunchCommand, executablePath, gamePath);

        var proton = BuildProtonCommand(executablePath, gamePath, game?.LinuxPrefixPath, game?.LinuxProtonPath);
        if (proton != null)
            return proton;

        return BuildWineCommand(executablePath, gamePath, game?.LinuxPrefixPath);
    }

    public static LinuxWindowsRunnerKind GetPreferredDefaultKind()
    {
        if (ListDetectedProtonInstallations().Count > 0)
            return LinuxWindowsRunnerKind.Proton;
        if (IsWineAvailable())
            return LinuxWindowsRunnerKind.Wine;
        return LinuxWindowsRunnerKind.Custom;
    }

    public static string GetDefaultPrefixPathForKind(LinuxWindowsRunnerKind kind, string gamePath) =>
        kind == LinuxWindowsRunnerKind.Wine
            ? GetDefaultWinePrefixPath(gamePath)
            : GetDefaultProtonCompatDataPath(gamePath);

    internal static List<string> SplitRunnerCommand(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var escaping = false;

        foreach (var character in command)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && !inSingleQuotes)
            {
                escaping = true;
                continue;
            }

            if (character == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (character == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inSingleQuotes && !inDoubleQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (escaping || inSingleQuotes || inDoubleQuotes)
            throw new InvalidOperationException("The Linux Windows-runner command contains an unmatched quote or trailing escape character.");

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static WindowsRunnerCommandSpec? BuildWineCommand(
        string executablePath,
        string gamePath,
        string? prefixPath)
    {
        string? wineBinary = null;
        if (IsCommandAvailable("wine64"))
            wineBinary = "wine64";
        else if (IsCommandAvailable("wine"))
            wineBinary = "wine";

        if (wineBinary == null)
            return null;

        var prefix = string.IsNullOrWhiteSpace(prefixPath)
            ? GetDefaultWinePrefixPath(gamePath)
            : prefixPath.Trim();
        Directory.CreateDirectory(prefix);

        return new WindowsRunnerCommandSpec
        {
            FileName = wineBinary,
            Arguments = [executablePath],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["WINEPREFIX"] = prefix,
            },
        };
    }

    private static WindowsRunnerCommandSpec? BuildProtonCommand(
        string executablePath,
        string gamePath,
        string? prefixPath,
        string? protonExecutablePath)
    {
        ProtonInstallation? installation = null;

        if (!string.IsNullOrWhiteSpace(protonExecutablePath) && File.Exists(protonExecutablePath))
        {
            var steamRoot = FindSteamRootForProton(protonExecutablePath)
                            ?? GetSteamRoots().FirstOrDefault();
            if (steamRoot != null)
            {
                installation = new ProtonInstallation
                {
                    ProtonExecutable = protonExecutablePath,
                    SteamRoot = steamRoot,
                };
            }
        }

        installation ??= GetProtonInstallations().FirstOrDefault(p => File.Exists(p.ProtonExecutable));
        if (installation == null)
            return null;

        var compatDataPath = string.IsNullOrWhiteSpace(prefixPath)
            ? GetDefaultProtonCompatDataPath(gamePath)
            : prefixPath.Trim();
        var compatAppId = GetStableCompatAppId(executablePath);
        Directory.CreateDirectory(compatDataPath);

        return new WindowsRunnerCommandSpec
        {
            FileName = installation.ProtonExecutable,
            Arguments = ["waitforexitandrun", executablePath],
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = installation.SteamRoot,
                ["STEAM_COMPAT_DATA_PATH"] = compatDataPath,
                ["STEAM_COMPAT_APP_ID"] = compatAppId,
                ["SteamAppId"] = compatAppId,
                ["SteamGameId"] = compatAppId,
            },
        };
    }

    private static string? FindSteamRootForProton(string protonExecutable)
    {
        var dir = Path.GetDirectoryName(protonExecutable);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "steamapps")) ||
                File.Exists(Path.Combine(dir, "steam.sh")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static string ReplaceRunnerPlaceholders(string token, string executablePath, string gamePath)
    {
        var resolvedToken = token;

        foreach (var placeholder in RunnerPlaceholderResolvers)
        {
            if (resolvedToken.Contains(placeholder.Key, StringComparison.Ordinal))
            {
                resolvedToken = resolvedToken.Replace(
                    placeholder.Key,
                    placeholder.Value(executablePath, gamePath),
                    StringComparison.Ordinal);
            }
        }

        return resolvedToken;
    }

    private static bool IsWineOrProtonAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        if (IsCommandAvailable("wine") || IsCommandAvailable("wine64"))
            return true;

        foreach (var protonInstallation in GetProtonInstallations())
        {
            if (File.Exists(protonInstallation.ProtonExecutable))
                return true;
        }

        return false;
    }

    private static IEnumerable<ProtonInstallation> GetProtonInstallations()
    {
        foreach (var steamRoot in GetSteamRoots())
        {
            var commonPath = Path.Combine(steamRoot, "steamapps", "common");
            foreach (var protonDir in GetExistingDirectories(commonPath, "Proton*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var protonExe = Path.Combine(protonDir, "proton");
                if (File.Exists(protonExe))
                {
                    yield return new ProtonInstallation
                    {
                        ProtonExecutable = protonExe,
                        SteamRoot = steamRoot,
                    };
                }
            }

            var compatibilityToolsPath = Path.Combine(steamRoot, "compatibilitytools.d");
            foreach (var protonDir in GetExistingDirectories(compatibilityToolsPath, "*Proton*").OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var protonExe = Path.Combine(protonDir, "proton");
                if (File.Exists(protonExe))
                {
                    yield return new ProtonInstallation
                    {
                        ProtonExecutable = protonExe,
                        SteamRoot = steamRoot,
                    };
                }
            }
        }
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var steamRoots = new[]
        {
            Path.Combine(homePath, ".steam", "root"),
            Path.Combine(homePath, ".steam", "steam"),
            Path.Combine(homePath, ".local", "share", "Steam"),
            Path.Combine(homePath, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
        };

        return steamRoots
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetExistingDirectories(string parentPath, string searchPattern)
    {
        if (!Directory.Exists(parentPath))
            return [];

        try
        {
            return Directory.GetDirectories(parentPath, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static string GetStableCompatAppId(string executablePath)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in executablePath)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (hash & 0x7FFFFFFF).ToString();
        }
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var process = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(process);
            if (proc != null)
            {
                proc.WaitForExit();
                return proc.ExitCode == 0;
            }
        }
        catch
        {
        }

        return false;
    }
}
